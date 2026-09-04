using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IQIAIndicator.Backtest;
using IQIAIndicator.Backtest.Execution;
using IQIAIndicator.Backtest.Measurement;
using IQIAIndicator.Backtest.Pnl;
using IQIAIndicator.Core;
using IQIAIndicator.Core.MarketData;
using IQIAIndicator.Core.MarketData.Yahoo;
using IQIAIndicator.Engine.Decision.States;
using IQIAIndicator.Engine.EntryTrigger;
using IQIAIndicator.Engine.Risk;
using IQIAIndicator.Engine.TradePlan;
using Xunit;

namespace IQIAIndicator.Tests.Research.NewDataSourceFeasibility;

/// <summary>
/// QDE-014 — READ-ONLY pilot. Does the implied-vol regime (VIX level / recent change) condition the
/// forward expectancy of the QDE-012 MeanReverting H1 book? No production type modified, no entry to
/// YahooSymbolMap (the probe calls HttpYahooChartClient.FetchChartJson directly, as in QDE-012/013).
/// Reuses the exact QDE-012 pipeline (RunFullBacktest on MES native 1h, warmup 128, horizon 10, base
/// round-trip cost ~$3.855). Cross-asset series are joined by STRICT CAUSAL forward-fill: for a signal
/// whose bar closes at T, only a cross-asset bar fully closed at or before T is used.
/// </summary>
public sealed class VixRegimeConditioningPilotTests
{
    private const int WarmupBars = 128;
    private const int HorizonBars = 10;
    private const decimal InitialCapital = 25_000m;
    private const decimal PointValue = 5m;                       // MES $/point
    private const double BaseCostRoundTripUsd = 3.855;           // identical to QDE-012
    private const int PurgeBars = 20;                            // identical to QDE-012 splits
    private const double TrainFraction = 0.60;

    private readonly ITestOutputHelper _output;
    private readonly List<string> _log = new();
    public VixRegimeConditioningPilotTests(ITestOutputHelper output) => _output = output;
    private void W(string s) { _output.WriteLine(s); _log.Add(s); }

    private static RiskPolicy Policy() => new(0.01m, null, null, null, null, null, null, null, null, null);
    private static InstrumentRiskSpecification Spec() => InstrumentRiskSpecification.FromInstrumentInfo(
        new InstrumentInfo("MES", TickSize: 0.25m, TickValue: 1.25m, PointValue: PointValue, Decimals: 2),
        minQuantity: 1, maxQuantity: 50, quantityStep: 1);

    /// <summary>A cross-asset hourly series with a causal "value strictly before instant T" lookup.</summary>
    private sealed class Series
    {
        private readonly DateTime[] _closeInstants;   // bar.Timestamp + 1h  (the instant the bar is fully known)
        private readonly double[] _values;            // bar.Close
        public string Name { get; }
        public int Count => _values.Length;

        public Series(string name, IReadOnlyList<HistoricalBar> bars)
        {
            Name = name;
            _closeInstants = bars.Select(b => b.Timestamp.AddHours(1)).ToArray();
            _values = bars.Select(b => (double)b.Close).ToArray();
        }

        /// <summary>Last value whose bar was FULLY CLOSED at or before <paramref name="instant"/> (strict
        /// causal forward-fill: never a bar still forming, never a future bar). null if none / staler than
        /// <paramref name="maxAgeHours"/>.</summary>
        public double? AsOf(DateTime instant, int maxAgeHours = 120)
        {
            int lo = 0, hi = _closeInstants.Length - 1, ans = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (_closeInstants[mid] <= instant) { ans = mid; lo = mid + 1; }
                else hi = mid - 1;
            }
            if (ans < 0) return null;
            double ageH = (instant - _closeInstants[ans]).TotalHours;
            return ageH <= maxAgeHours ? _values[ans] : null;
        }

        /// <summary>Value as-of <paramref name="instant"/> minus value as-of (instant - lookback).</summary>
        public double? ChangeAsOf(DateTime instant, TimeSpan lookback)
        {
            double? now = AsOf(instant);
            double? past = AsOf(instant - lookback);
            return now is double a && past is double b ? a - b : (double?)null;
        }
    }

    [Fact]
    public void Pilot_VIX_Regime_Conditions_MeanReverting_H1_Expectancy()
    {
        var client = new HttpYahooChartClient();
        DateTime now = DateTime.UtcNow;
        DateTime from = now.AddDays(-720);

        HistoricalSeries mes;
        var cross = new Dictionary<string, Series>();
        try
        {
            mes = Load(client, "MES=F", "1h", from, now, "MES");
            foreach ((string tf, string label) in new[]
            {
                ("^VIX", "VIX"), ("^VIX9D", "VIX9D"), ("^VIX3M", "VIX3M"),
                ("^TNX", "TNX"), ("ZN=F", "ZN"), ("DX-Y.NYB", "DXY"), ("NQ=F", "NQ"),
            })
            {
                HistoricalSeries s = Load(client, tf, "1h", from, now, label);
                cross[label] = new Series(label, s.Bars);
            }
        }
        catch (Exception ex) when (ex is YahooProviderException or HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            Assert.Skip($"Yahoo unavailable: {ex.GetType().Name}: {ex.Message}");
            return;
        }

        // ── PHASE 0 : gap quality on the MES H1 series ────────────────────────────────────────────
        W("================ PHASE 0 — DATA QUALITY (MES=F 1h) ================");
        GapAudit(mes.Bars, W);

        // ── run the QDE-012 pipeline on MES native 1h ────────────────────────────────────────────
        var scenario = BacktestScenario.Create(
            mes, new BacktestWindow("QDE014", mes.FirstTimestamp, mes.LastTimestamp.AddSeconds(1)),
            InitialCapital, Spec(), Policy());
        MeasurementConfiguration meas = MeasurementConfiguration.Create(10, new[] { 0.001, 0.002 });
        ExecutionConfiguration exec = ExecutionConfiguration.Create(HorizonBars);
        PnLConfiguration pnl = PnLConfiguration.Create(
            InstrumentPnLSpecification.Create("MES", priceUnitValue: PointValue, currency: "USD"), 1, InitialCapital);
        BacktestFullResult full = new BacktestEngine().RunFullBacktest(scenario, WarmupBars, meas, exec, pnl);

        var byBar = new Dictionary<int, BacktestSignalResult>();
        foreach (BacktestSignalResult b in full.SignalResult.Bars) byBar[b.BarIndex] = b;

        // ── PHASE 1 : build the conditioned trade table (strict causal join) ──────────────────────
        var rows = new List<Row>();
        int missingVix = 0;
        foreach (PositionPnLResult pp in full.PnLResult.PositionPnLResults)
        {
            if (!byBar.TryGetValue(pp.PositionId, out BacktestSignalResult? sb)) continue;
            if (sb.Decision is not { Winner: MarketState.MeanReverting }) continue;
            if (pp.Direction is not (DirectionCandidate.BUY_CANDIDATE or DirectionCandidate.SELL_CANDIDATE)) continue;
            if (pp.Status != PositionStatus.Closed || pp.GrossPnL is not decimal g) continue;
            if (sb.TradePlan is not { EntryPrice: decimal ep, StopLoss: decimal sl }) continue;
            double stopPts = (double)Math.Abs(ep - sl);
            if (stopPts <= 0) continue;

            // decision/fill instant = signal bar close = bar.Timestamp + 1h
            DateTime tClose = sb.Timestamp.AddHours(1);

            double? vix = cross["VIX"].AsOf(tClose);
            if (vix is not double vixLevel) { missingVix++; continue; }
            double? vixChg24 = cross["VIX"].ChangeAsOf(tClose, TimeSpan.FromHours(24));
            double? vix3m = cross["VIX3M"].AsOf(tClose);
            double? slope = (vix3m is double v3 && vixLevel > 0) ? v3 / vixLevel : (double?)null;
            double? tnxChg = cross["TNX"].ChangeAsOf(tClose, TimeSpan.FromHours(24));
            double? znChg = cross["ZN"].ChangeAsOf(tClose, TimeSpan.FromHours(24));
            double? dxyChgRaw = cross["DXY"].ChangeAsOf(tClose, TimeSpan.FromHours(24));
            double? dxyNow = cross["DXY"].AsOf(tClose);
            double? dxyChgPct = (dxyChgRaw is double d && dxyNow is double dn && dn > 0) ? 100.0 * d / dn : (double?)null;
            // MES vs NQ normalised 24h return spread
            double? mesNow = cross.TryGetValue("MES", out _) ? null : null; // MES handled from pipeline below
            double? nqNow = cross["NQ"].AsOf(tClose);
            double? nqPast = cross["NQ"].AsOf(tClose - TimeSpan.FromHours(24));
            double mes24 = Ret(mes.Bars, sb.BarIndex, 24);
            double? nq24 = (nqNow is double a && nqPast is double b2 && b2 > 0) ? Math.Log(a / b2) : (double?)null;
            double? mesMinusNq = nq24 is double n24 ? mes24 - n24 : (double?)null;

            double net = (double)g - BaseCostRoundTripUsd;
            rows.Add(new Row(
                pp.PositionId, sb.Timestamp, stopPts, (double)g, net,
                vixLevel, vixChg24, slope, tnxChg, znChg, dxyChgPct, mesMinusNq));
        }

        W("");
        W("================ PHASE 1 — CAUSAL ALIGNMENT ================");
        W($"MeanReverting closed trades = {rows.Count}  (dropped, no causal VIX value: {missingVix})");
        W("Join rule: cross-asset value = last bar with (Timestamp + 1h) <= (MES signal bar Timestamp + 1h).");
        W("Code guaranteeing causality: Series.AsOf() binary-searches _closeInstants for the last entry <= instant;");
        W("_closeInstants[i] = bar[i].Timestamp + 1h (the instant bar i is fully known). No future bar can be selected.");
        var vlist = rows.Select(r => r.VixLevel).OrderBy(x => x).ToList();
        W($"VIX level at signal: min={vlist.First():0.00} p33={P(vlist, 1.0 / 3):0.00} median={P(vlist, .5):0.00} " +
          $"p67={P(vlist, 2.0 / 3):0.00} p90={P(vlist, .9):0.00} max={vlist.Last():0.00}");

        // ── PHASE 2 — PRIMARY HYPOTHESIS : VIX level terciles + VIX 24h change sign ───────────────
        W("");
        W("================ PHASE 2 — PRIMARY HYPOTHESIS (VIX regime) ================");
        W("Metric: net-of-cost expectancy per trade in R = (GrossPnL - 3.855) / (stopPts * 5).");
        double t33 = P(vlist, 1.0 / 3), t67 = P(vlist, 2.0 / 3);
        int splitBar = rows.Count > 0 ? rows.Min(r => r.Bar) + (int)((rows.Max(r => r.Bar) - rows.Min(r => r.Bar)) * TrainFraction) : 0;

        W("");
        W("-- by VIX LEVEL tercile --");
        Segment(rows, "VIX low   (<= p33)", r => r.VixLevel <= t33, splitBar, W);
        Segment(rows, "VIX mid   (p33..p67)", r => r.VixLevel > t33 && r.VixLevel <= t67, splitBar, W);
        Segment(rows, "VIX high  (> p67)", r => r.VixLevel > t67, splitBar, W);

        W("");
        W("-- by VIX 24h CHANGE sign --");
        Segment(rows, "VIX falling/flat (chg <= 0)", r => r.VixChg24 is double c && c <= 0, splitBar, W);
        Segment(rows, "VIX rising       (chg > 0)", r => r.VixChg24 is double c && c > 0, splitBar, W);
        Segment(rows, "VIX rising fast  (chg > +1.0)", r => r.VixChg24 is double c && c > 1.0, splitBar, W);

        W("");
        W("-- 2x3 grid : VIX change sign x VIX level tercile --");
        foreach ((string cl, Func<Row, bool> cf) in new (string, Func<Row, bool>)[]
                 { ("chg<=0", r => r.VixChg24 is double c && c <= 0), ("chg>0", r => r.VixChg24 is double c && c > 0) })
        foreach ((string ll, Func<Row, bool> lf) in new (string, Func<Row, bool>)[]
                 { ("low", r => r.VixLevel <= t33), ("mid", r => r.VixLevel > t33 && r.VixLevel <= t67), ("high", r => r.VixLevel > t67) })
            Segment(rows, $"VIX {cl,-6} & {ll,-4}", r => cf(r) && lf(r), splitBar, W);

        // ── PHASE 3 — SECONDARY HYPOTHESES (EXPLORATORY ONLY) ────────────────────────────────────
        W("");
        W("================ PHASE 3 — SECONDARY HYPOTHESES (EXPLORATORY — never conclusive alone) ================");
        Exploratory(rows, "term-structure slope VIX3M/VIX", r => r.Slope, W, splitBar);
        Exploratory(rows, "TNX 24h change (yield)", r => r.TnxChg, W, splitBar);
        Exploratory(rows, "ZN 24h change (bond price)", r => r.ZnChg, W, splitBar);
        Exploratory(rows, "DXY 24h change %", r => r.DxyChgPct, W, splitBar);
        Exploratory(rows, "MES - NQ 24h log-return spread", r => r.MesMinusNq, W, splitBar);

        // baseline (unconditioned), for reference
        W("");
        W("-- BASELINE (unconditioned, all trades) --");
        Segment(rows, "ALL", _ => true, splitBar, W);

        WriteReport();
        Assert.True(rows.Count > 100, $"expected >100 conditioned trades, got {rows.Count}");
    }

    private sealed record Row(int Bar, DateTime Ts, double StopPts, double Gross, double Net,
        double VixLevel, double? VixChg24, double? Slope, double? TnxChg, double? ZnChg, double? DxyChgPct, double? MesMinusNq)
    {
        public double RiskUsd => StopPts * (double)PointValue;
        public double NetR => Net / RiskUsd;
        public double GrossR => Gross / RiskUsd;
    }

    private static void Segment(IReadOnlyList<Row> all, string label, Func<Row, bool> pred, int splitBar, Action<string> W)
    {
        var b = all.Where(pred).ToList();
        if (b.Count == 0) { W($"  {label,-32} n=0"); return; }
        (double m, double ci) full = MeanCi(b.Select(r => r.NetR));
        (double m, double ci) gr = MeanCi(b.Select(r => r.GrossR));
        var tr = b.Where(r => r.Bar < splitBar - PurgeBars).ToList();
        var oo = b.Where(r => r.Bar >= splitBar).ToList();
        string split = tr.Count >= 30 && oo.Count >= 30
            ? $"TRAIN n={tr.Count} {Fmt(MeanCi(tr.Select(r => r.NetR)))}  OOS n={oo.Count} {Fmt(MeanCi(oo.Select(r => r.NetR)))}"
            : $"(split n/a: train {tr.Count}, oos {oo.Count})";
        string verdict = full.m - full.ci > 0 ? "NET+ ROBUST" : full.m + full.ci < 0 ? "net- robust" : "straddles 0";
        double wr = b.Count(r => r.Gross > 0) / (double)b.Count;
        W($"  {label,-32} n={b.Count,5}  wr={wr:0.000}  grossR={gr.m,+7:0.000}  netR={full.m,+7:0.000} +/-{full.ci:0.000}  [{verdict}]");
        W($"       {split}");
    }

    private static void Exploratory(IReadOnlyList<Row> all, string label, Func<Row, double?> sel, Action<string> W, int splitBar)
    {
        var b = all.Where(r => sel(r).HasValue).Select(r => (r, v: sel(r)!.Value)).ToList();
        if (b.Count < 60) { W($"  [{label}] n={b.Count} (<60) — not analysed"); return; }
        var sorted = b.OrderBy(x => x.v).ToList();
        double lo = sorted[(int)(sorted.Count / 3.0)].v, hi = sorted[(int)(2 * sorted.Count / 3.0)].v;
        W($"  [{label}]  terciles at {lo:0.0000} / {hi:0.0000}");
        Segment(sorted.Where(x => x.v <= lo).Select(x => x.r).ToList(), "    low tercile", _ => true, splitBar, W);
        Segment(sorted.Where(x => x.v > lo && x.v <= hi).Select(x => x.r).ToList(), "    mid tercile", _ => true, splitBar, W);
        Segment(sorted.Where(x => x.v > hi).Select(x => x.r).ToList(), "    high tercile", _ => true, splitBar, W);
    }

    // ── PHASE 0 helper ──────────────────────────────────────────────────────────────────────────
    private static void GapAudit(IReadOnlyList<HistoricalBar> bars, Action<string> W)
    {
        int weekend = 0, dailyBreak = 0, holidayOrOther = 0, inSession = 0;
        long weekendH = 0, dailyH = 0, otherH = 0;
        var otherSamples = new List<string>();
        for (int i = 1; i < bars.Count; i++)
        {
            double gapH = (bars[i].Timestamp - bars[i - 1].Timestamp).TotalHours;
            if (gapH <= 1.01) continue;
            DateTime start = bars[i - 1].Timestamp;      // last bar before the gap (its slot)
            int missing = (int)Math.Round(gapH) - 1;
            // CME equity-index futures: daily maintenance break 21:00-22:00 UTC; weekend Fri 21:00 -> Sun 22:00 UTC
            bool isWeekend = start.DayOfWeek == DayOfWeek.Friday && start.Hour is 20 or 21 && gapH is > 40 and < 56;
            bool isDaily = start.Hour is 20 or 21 && gapH is > 1.01 and <= 3.5;
            if (isWeekend) { weekend++; weekendH += missing; }
            else if (isDaily) { dailyBreak++; dailyH += missing; }
            else
            {
                holidayOrOther++; otherH += missing;
                bool likelyInSession = start.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)
                                       && start.Hour is > 13 and < 20 && gapH < 6;
                if (likelyInSession) inSession++;
                if (otherSamples.Count < 15)
                    otherSamples.Add($"{start:yyyy-MM-dd ddd HH:mm}Z +{gapH:0.0}h ({missing} slots){(likelyInSession ? "  <-- IN-SESSION?" : "")}");
            }
        }
        W($"total bars={bars.Count}, range {bars[0].Timestamp:O}..{bars[^1].Timestamp:O}");
        W($"gaps: weekend={weekend} ({weekendH} slots), dailyBreak(21:00-22:00Z)={dailyBreak} ({dailyH} slots), " +
          $"holiday/other={holidayOrOther} ({otherH} slots), of which flagged possibly in-session={inSession}");
        W("holiday/other gap samples:");
        foreach (string s in otherSamples) W($"  {s}");
        W(inSession == 0
            ? "=> NO gap flagged as in-session. All gaps coincide with weekends, the daily CME break, or holidays. Forward-fill is safe."
            : $"=> {inSession} gap(s) flagged as possibly in-session — inspect before trusting forward-fill across them.");
    }

    private static double Ret(IReadOnlyList<HistoricalBar> bars, int idx, int lookbackBars)
    {
        int j = Math.Max(0, idx - lookbackBars);
        double a = (double)bars[idx].Close, b = (double)bars[j].Close;
        return b > 0 ? Math.Log(a / b) : 0.0;
    }

    private HistoricalSeries Load(HttpYahooChartClient client, string ticker, string interval, DateTime from, DateTime to, string label)
    {
        string json = client.FetchChartJson(ticker, interval, from, to, CancellationToken.None);
        YahooChartParser.ParseResult p = YahooChartParser.Parse(json);
        W($"loaded {label,-6} ({ticker,-10}) {interval}: {p.Bars.Count} bars {p.Bars[0].Timestamp:yyyy-MM-dd}..{p.Bars[^1].Timestamp:yyyy-MM-dd} ({p.GapCount} parser gaps)");
        return HistoricalSeries.Create(label, interval == "1h" ? "H1" : interval, "UTC", "Yahoo(native)", p.Bars);
    }

    private static double P(IReadOnlyList<double> v, double p)
    {
        if (v.Count == 0) return double.NaN;
        var s = v.OrderBy(x => x).ToArray();
        if (s.Length == 1) return s[0];
        double r = p * (s.Length - 1);
        int lo = (int)Math.Floor(r), hi = (int)Math.Ceiling(r);
        return s[lo] + (s[hi] - s[lo]) * (r - lo);
    }

    private static (double mean, double ci95) MeanCi(IEnumerable<double> xs)
    {
        double[] v = xs.ToArray();
        if (v.Length == 0) return (double.NaN, double.NaN);
        double m = v.Average();
        if (v.Length < 2) return (m, double.NaN);
        double sd = Math.Sqrt(v.Sum(x => (x - m) * (x - m)) / (v.Length - 1));
        return (m, 1.96 * sd / Math.Sqrt(v.Length));
    }

    private static string Fmt((double m, double ci) x) => $"netR={x.m:+0.000;-0.000}+/-{x.ci:0.000}";

    private void WriteReport()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "IQIAIndicator.Tests.csproj")))
            dir = Directory.GetParent(dir)?.FullName;
        if (dir is null) return;
        string od = Path.Combine(dir, "Research", "NewDataSourceFeasibility", "Output");
        Directory.CreateDirectory(od);
        File.WriteAllText(Path.Combine(od, "vix_regime_pilot.txt"), string.Join("\n", _log) + "\n");
    }
}
