using System;
using System.Collections.Generic;
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
using IQIAIndicator.Tests.BacktestTests.Yahoo;
using Xunit;

namespace IQIAIndicator.Tests.Research.StopLossContractAudit;

/// <summary>
/// READ-ONLY. 2026-08-31 lot "MeanReverting pipeline complet M15/H1". No production type modified, no
/// window constant changed. RunFullBacktest is run UNCHANGED on M5 bars re-aggregated to M15 and to H1
/// (and, if Yahoo serves it, native 1h with a longer history). Only the temporal aggregation of the input
/// series changes. Same "base" cost model (~$3.855 RT, timeframe-independent) applied post-hoc, identical
/// methodology to the M5 MeanReverting / Trending audits.
/// </summary>
public sealed class SlowTimeframePipelineAuditTests
{
    private readonly ITestOutputHelper _output;
    private readonly YahooSessionDataset _yahoo;

    public SlowTimeframePipelineAuditTests(ITestOutputHelper output, YahooSessionDataset yahoo)
    {
        _output = output;
        _yahoo = yahoo;
    }

    private const int WarmupBars = 128;
    private const int HorizonBars = 10;
    private const decimal InitialCapital = 25_000m;
    private const decimal PointValue = 5m;   // MES
    private const double BaseCostRoundTripUsd = 1.25 + 0.625 + 1.24 + 0.74;   // = 3.855

    private static RiskPolicy Policy() => new(0.01m, null, null, null, null, null, null, null, null, null);
    private static InstrumentRiskSpecification Spec() => InstrumentRiskSpecification.FromInstrumentInfo(
        new InstrumentInfo("MES", TickSize: 0.25m, TickValue: 1.25m, PointValue: PointValue, Decimals: 2),
        minQuantity: 1, maxQuantity: 50, quantityStep: 1);

    private readonly List<string> _log = new();
    private void W(string s) { _output.WriteLine(s); _log.Add(s); }

    [Fact]
    public void Measure_MeanReverting_And_Trending_On_M15_And_H1()
    {
        HistoricalSeries m5;
        try { m5 = _yahoo.Require(); }
        catch (Exception ex) when (ex is YahooProviderException or HttpRequestException or TaskCanceledException)
        {
            Assert.Skip($"Yahoo provider unavailable: {ex.GetType().Name}: {ex.Message}");
            return;
        }

        W($"M5 base series: {m5.Count} bars, {m5.FirstTimestamp:O}..{m5.LastTimestamp:O}");

        var runs = new List<(string Tf, HistoricalSeries S)>();

        HistoricalSeries m15agg = Aggregate(m5, TimeSpan.FromMinutes(15), "M15", "Yahoo(agg-from-M5)");
        runs.Add(("M15 (agg 59d)", m15agg));
        HistoricalSeries h1agg = Aggregate(m5, TimeSpan.FromHours(1), "H1", "Yahoo(agg-from-M5)");
        runs.Add(("H1 (agg 59d)", h1agg));

        // Attempt a longer native 1h history (Yahoo typically serves 1h up to ~730d).
        try
        {
            var client = new HttpYahooChartClient();
            string ticker = YahooSymbolMap.Resolve("MES");
            DateTime to = DateTime.UtcNow;
            DateTime from = to.AddDays(-720);
            string json = client.FetchChartJson(ticker, "1h", from, to, CancellationToken.None);
            YahooChartParser.ParseResult parsed = YahooChartParser.Parse(json);
            if (parsed.Bars.Count > h1agg.Count + 50)
            {
                var native = HistoricalSeries.Create("MES", "H1", "UTC", "Yahoo(native-1h)", parsed.Bars);
                runs.Add(("H1 (native)", native));
                W($"native 1h: {native.Count} bars, {native.FirstTimestamp:O}..{native.LastTimestamp:O} " +
                  $"(~{(native.LastTimestamp - native.FirstTimestamp).TotalDays:0} calendar days) - {parsed.GapCount} gap slots omitted");
            }
            else
            {
                W($"native 1h returned only {parsed.Bars.Count} bars - not longer than the aggregated series, skipped.");
            }
        }
        catch (Exception ex)
        {
            W($"native 1h pull failed ({ex.GetType().Name}: {ex.Message}) - continuing with aggregated H1 only.");
        }

        foreach ((string tf, HistoricalSeries s) in runs)
        {
            W("");
            W("########################################################################");
            W($"###  {tf}   ({s.Count} bars, {s.FirstTimestamp:O}..{s.LastTimestamp:O})");
            W("########################################################################");
            try { RunOne(tf, s); }
            catch (Exception ex) { W($"  RUN FAILED: {ex.GetType().Name}: {ex.Message}"); }
        }

        WriteReport();
    }

    private void RunOne(string tf, HistoricalSeries series)
    {
        var scenario = BacktestScenario.Create(
            series, new BacktestWindow($"SLOWTF-{tf}", series.FirstTimestamp, series.LastTimestamp.AddSeconds(1)),
            InitialCapital, Spec(), Policy());
        MeasurementConfiguration meas = MeasurementConfiguration.Create(10, new[] { 0.001, 0.002 });
        ExecutionConfiguration exec = ExecutionConfiguration.Create(HorizonBars);
        PnLConfiguration pnl = PnLConfiguration.Create(
            InstrumentPnLSpecification.Create("MES", priceUnitValue: PointValue, currency: "USD"), 1, InitialCapital);

        BacktestFullResult full = new BacktestEngine().RunFullBacktest(scenario, WarmupBars, meas, exec, pnl);

        var byBar = new Dictionary<int, BacktestSignalResult>();
        foreach (BacktestSignalResult b in full.SignalResult.Bars) byBar[b.BarIndex] = b;

        W($"  bars processed={full.SignalResult.BarsProcessed} ready={full.SignalResult.ReadyBars} " +
          $"exceptions={full.SignalResult.ExceptionCount}  regimeDetected={full.SignalResult.RegimeDetectedCount}");
        W($"  BUY={full.SignalResult.BuyCount} SELL={full.SignalResult.SellCount} " +
          $"NO_ACTION={full.SignalResult.NoActionCount} WATCH={full.SignalResult.WatchCount}  " +
          $"TP_READY={full.SignalResult.TradePlanReadyCount} TP_SIGNAL_ONLY={full.SignalResult.TradePlanSignalOnlyCount} " +
          $"TP_NOTRADE={full.SignalResult.TradePlanNoTradeCount} TP_BLOCKED={full.SignalResult.TradePlanBlockedCount}");

        foreach (MarketState regime in new[] { MarketState.MeanReverting, MarketState.Trending })
        {
            // status mix among directional signals of this regime
            int dir = 0, ready = 0, sigOnly = 0, rej = 0, other = 0;
            var dayset = new HashSet<DateTime>();
            foreach (BacktestSignalResult sb in full.SignalResult.Bars)
            {
                if (sb.Status != BacktestSignalStatus.Ready || sb.Decision is not { } d || d.Winner != regime) continue;
                if (sb.TradePlan is not { } p) continue;
                if (p.Direction is not (DirectionCandidate.BUY_CANDIDATE or DirectionCandidate.SELL_CANDIDATE)) continue;
                dir++; dayset.Add(sb.Timestamp.Date);
                switch (p.Status)
                {
                    case TradePlanStatus.PLAN_READY: ready++; break;
                    case TradePlanStatus.SIGNAL_ONLY: sigOnly++; break;
                    case TradePlanStatus.PLAN_REJECTED: rej++; break;
                    default: other++; break;
                }
            }

            var rows = new List<Row>();
            var slD = new List<double>(); var tpD = new List<double>();
            foreach (PositionPnLResult pp in full.PnLResult.PositionPnLResults)
            {
                if (!byBar.TryGetValue(pp.PositionId, out BacktestSignalResult? sb)) continue;
                if (sb.Decision is not { } d || d.Winner != regime) continue;
                if (pp.Direction is not (DirectionCandidate.BUY_CANDIDATE or DirectionCandidate.SELL_CANDIDATE)) continue;
                if (sb.TradePlan is { EntryPrice: decimal ep, StopLoss: decimal sl })
                {
                    slD.Add((double)Math.Abs(ep - sl));
                    if (sb.TradePlan.TakeProfit is decimal tp) tpD.Add((double)Math.Abs(ep - tp));
                }
                if (pp.Status != PositionStatus.Closed || pp.GrossPnL is not decimal g) continue;
                if (sb.TradePlan is not { EntryPrice: decimal e2, StopLoss: decimal s2 }) continue;
                double stopPts = (double)Math.Abs(e2 - s2);
                if (stopPts <= 0) continue;
                rows.Add(new Row(pp.PositionId, sb.Timestamp, sb.TradePlan.RiskRewardRatio ?? double.NaN, stopPts,
                    (double)g, (double)g - BaseCostRoundTripUsd, pp.ExitReason?.ToString() ?? "?", pp.HoldingBars ?? -1));
            }

            W("");
            W($"  ===== {regime} =====");
            W($"  directional signals={dir} over {dayset.Count} days -> {dir / (double)Math.Max(1, dayset.Count):0.00}/day   " +
              $"[PLAN_READY={ready} SIGNAL_ONLY={sigOnly} PLAN_REJECTED={rej} other={other}]");
            if (slD.Count > 0)
                W($"  SL distance pts: median={Med(slD):0.00} p90={Pct(slD, .9):0.00}   " +
                  $"TP distance pts: median={(tpD.Count > 0 ? Med(tpD) : double.NaN):0.00} p90={(tpD.Count > 0 ? Pct(tpD, .9) : double.NaN):0.00}   " +
                  $"(cost floor {BaseCostRoundTripUsd / (double)PointValue:0.00} pt)");
            if (rows.Count == 0) { W("  no closed trade."); continue; }

            var hb = rows.Select(r => (double)r.Hold).ToList();
            string barDur = tf.Contains("M15") ? "15 min" : "1 h";
            W($"  holding bars: median={Med(hb):0.0}  p90={Pct(hb, .9):0.0}  %==0={100.0 * rows.Count(r => r.Hold == 0) / rows.Count:0.0}  " +
              $"(1 bar = {barDur})");
            W("  exit reason:");
            foreach (var grp in rows.GroupBy(r => r.Exit).OrderByDescending(x => x.Count()))
                W($"    {grp.Key,-12} n={grp.Count(),4} ({100.0 * grp.Count() / rows.Count,4:0.0}%)  " +
                  $"gross$/tr={grp.Average(x => x.Gross),7:0.00}  net$/tr={grp.Average(x => x.Net),7:0.00}");

            Block("  ALL", rows);

            // OOS split if each half has n>=30
            int lo = rows.Min(r => r.Bar), hi = rows.Max(r => r.Bar);
            int split = lo + (int)((hi - lo) * 0.60);
            var train = rows.Where(r => r.Bar < split - 20).ToList();
            var oos = rows.Where(r => r.Bar >= split).ToList();
            if (train.Count >= 30 && oos.Count >= 30)
            {
                Block("  TRAIN", train);
                Block("  OOS  ", oos);
            }
            else
            {
                W($"  (no OOS split: train n={train.Count}, oos n={oos.Count} - one side < 30)");
            }
        }
    }

    private sealed record Row(int Bar, DateTime Ts, double Rr, double StopPts, double Gross, double Net, string Exit, int Hold)
    {
        public double RiskUsd => StopPts * (double)PointValue;
        public double GrossR => Gross / RiskUsd;
        public double NetR => Net / RiskUsd;
    }

    private void Block(string label, IReadOnlyList<Row> ts)
    {
        if (ts.Count == 0) { W($"  [{label}] n=0"); return; }
        var winG = ts.Where(t => t.Gross > 0).ToList();
        var winN = ts.Where(t => t.Net > 0).ToList();
        var losN = ts.Where(t => t.Net <= 0).ToList();
        (double m, double ci) g = MeanCi(ts.Select(t => t.Gross));
        (double m, double ci) n = MeanCi(ts.Select(t => t.Net));
        (double m, double ci) gr = MeanCi(ts.Select(t => t.GrossR));
        (double m, double ci) nr = MeanCi(ts.Select(t => t.NetR));
        double meanRr = ts.Where(t => !double.IsNaN(t.Rr)).Select(t => t.Rr).DefaultIfEmpty(double.NaN).Average();
        double be = double.IsNaN(meanRr) ? double.NaN : 1.0 / (1.0 + meanRr);
        double wr = winG.Count / (double)ts.Count;
        W($"  [{label}] n={ts.Count}  winRate(gross)={wr:0.000}  winRate(net)={winN.Count / (double)ts.Count:0.000}  " +
          $"meanR:R={meanRr:0.00} breakEvenWR={be:0.000} gap={(double.IsNaN(be) ? double.NaN : wr - be):+0.000;-0.000}");
        W($"           avgWin(net)=${(winN.Count > 0 ? winN.Average(t => t.Net) : 0):0.00}  avgLoss(net)=${(losN.Count > 0 ? losN.Average(t => t.Net) : 0):0.00}");
        W($"           exp GROSS = ${g.m:0.00} +/-{g.ci:0.00}/tr  ({gr.m:0.000} +/-{gr.ci:0.000} R)");
        W($"           exp NET   = ${n.m:0.00} +/-{n.ci:0.00}/tr  ({nr.m:0.000} +/-{nr.ci:0.000} R)   totalNet=${ts.Sum(t => t.Net):0.0}");
    }

    // ── M5 -> N aggregation ─────────────────────────────────────────────────────────────────────
    private static HistoricalSeries Aggregate(HistoricalSeries m5, TimeSpan bucket, string tfLabel, string provider)
    {
        long ticks = bucket.Ticks;
        var outBars = new List<HistoricalBar>();
        DateTime curKey = default; decimal o = 0, h = 0, l = 0, c = 0, v = 0; bool open = false;
        foreach (HistoricalBar b in m5.Bars)
        {
            DateTime key = new(b.Timestamp.Ticks - (b.Timestamp.Ticks % ticks), DateTimeKind.Utc);
            if (!open) { curKey = key; o = b.Open; h = b.High; l = b.Low; c = b.Close; v = b.Volume; open = true; }
            else if (key == curKey) { h = Math.Max(h, b.High); l = Math.Min(l, b.Low); c = b.Close; v += b.Volume; }
            else
            {
                outBars.Add(new HistoricalBar(curKey, o, h, l, c, v));
                curKey = key; o = b.Open; h = b.High; l = b.Low; c = b.Close; v = b.Volume;
            }
        }
        if (open) outBars.Add(new HistoricalBar(curKey, o, h, l, c, v));
        return HistoricalSeries.Create(m5.Symbol, tfLabel, m5.TimeZone, provider, outBars);
    }

    private static double Med(IEnumerable<double> xs) => Pct(xs, 0.5);
    private static double Pct(IEnumerable<double> xs, double p)
    {
        var v = xs.OrderBy(x => x).ToArray();
        if (v.Length == 0) return double.NaN;
        if (v.Length == 1) return v[0];
        double r = p * (v.Length - 1);
        int lo = (int)Math.Floor(r), hi = (int)Math.Ceiling(r);
        return v[lo] + (v[hi] - v[lo]) * (r - lo);
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

    private void WriteReport()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "IQIAIndicator.Tests.csproj")))
            dir = Directory.GetParent(dir)?.FullName;
        if (dir is null) return;
        string od = Path.Combine(dir, "Research", "StopLossContractAudit", "Output");
        Directory.CreateDirectory(od);
        File.WriteAllText(Path.Combine(od, "slow_timeframe_pipeline.txt"), string.Join("\n", _log) + "\n");
    }
}
