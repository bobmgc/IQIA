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
/// QDE-015 — READ-ONLY walk-forward validation of the Trending H1 signal IN ISOLATION (no conditioning,
/// no calibration). No production type modified; no TimeSeriesMomentumModel parameter touched; no entry
/// added to YahooSymbolMap. Reuses the exact QDE-012 Trending H1 pipeline (RunFullBacktest on MES=F 1h
/// native ~720d, warmup 128, horizon 10, base round-trip cost ~$3.855). The full pipeline is run ONCE
/// over the whole series (so warmup/regime buffer never restart mid-history and parameters are identical
/// by construction across every segment); the resulting Trending trades are then partitioned by signal
/// timestamp into contiguous non-overlapping segments.
/// </summary>
public sealed class TrendingWalkForwardValidationTests
{
    private const int WarmupBars = 128;
    private const int HorizonBars = 10;
    private const decimal InitialCapital = 25_000m;
    private const decimal PointValue = 5m;
    private const double BaseCostRoundTripUsd = 3.855;

    private readonly ITestOutputHelper _output;
    private readonly List<string> _log = new();
    public TrendingWalkForwardValidationTests(ITestOutputHelper output) => _output = output;
    private void W(string s) { _output.WriteLine(s); _log.Add(s); }

    private static RiskPolicy Policy() => new(0.01m, null, null, null, null, null, null, null, null, null);
    private static InstrumentRiskSpecification Spec() => InstrumentRiskSpecification.FromInstrumentInfo(
        new InstrumentInfo("MES", TickSize: 0.25m, TickValue: 1.25m, PointValue: PointValue, Decimals: 2),
        minQuantity: 1, maxQuantity: 50, quantityStep: 1);

    private sealed record Trade(int Bar, DateTime Ts, double StopPts, double Rr, double Gross, double Net, string Exit)
    {
        public double RiskUsd => StopPts * (double)PointValue;
        public double NetR => Net / RiskUsd;
        public double GrossR => Gross / RiskUsd;
    }

    [Fact]
    public void WalkForward_Validate_Trending_H1_Signal()
    {
        HistoricalSeries mes;
        try
        {
            var client = new HttpYahooChartClient();
            string json = client.FetchChartJson("MES=F", "1h", DateTime.UtcNow.AddDays(-720), DateTime.UtcNow, CancellationToken.None);
            YahooChartParser.ParseResult p = YahooChartParser.Parse(json);
            mes = HistoricalSeries.Create("MES", "H1", "UTC", "Yahoo(native)", p.Bars);
            W($"MES=F 1h: {p.Bars.Count} bars {p.Bars[0].Timestamp:yyyy-MM-dd}..{p.Bars[^1].Timestamp:yyyy-MM-dd}");
        }
        catch (Exception ex) when (ex is YahooProviderException or HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            Assert.Skip($"Yahoo unavailable: {ex.GetType().Name}: {ex.Message}");
            return;
        }

        var scenario = BacktestScenario.Create(
            mes, new BacktestWindow("QDE015", mes.FirstTimestamp, mes.LastTimestamp.AddSeconds(1)),
            InitialCapital, Spec(), Policy());
        MeasurementConfiguration meas = MeasurementConfiguration.Create(10, new[] { 0.001, 0.002 });
        ExecutionConfiguration exec = ExecutionConfiguration.Create(HorizonBars);
        PnLConfiguration pnl = PnLConfiguration.Create(
            InstrumentPnLSpecification.Create("MES", priceUnitValue: PointValue, currency: "USD"), 1, InitialCapital);
        BacktestFullResult full = new BacktestEngine().RunFullBacktest(scenario, WarmupBars, meas, exec, pnl);

        var byBar = new Dictionary<int, BacktestSignalResult>();
        foreach (BacktestSignalResult b in full.SignalResult.Bars) byBar[b.BarIndex] = b;

        var tr = new List<Trade>();
        foreach (PositionPnLResult pp in full.PnLResult.PositionPnLResults)
        {
            if (!byBar.TryGetValue(pp.PositionId, out BacktestSignalResult? sb)) continue;
            if (sb.Decision is not { Winner: MarketState.Trending }) continue;
            if (pp.Direction is not (DirectionCandidate.BUY_CANDIDATE or DirectionCandidate.SELL_CANDIDATE)) continue;
            if (pp.Status != PositionStatus.Closed || pp.GrossPnL is not decimal g) continue;
            if (sb.TradePlan is not { EntryPrice: decimal ep, StopLoss: decimal sl }) continue;
            double stopPts = (double)Math.Abs(ep - sl);
            if (stopPts <= 0) continue;
            tr.Add(new Trade(pp.PositionId, sb.Timestamp, stopPts, sb.TradePlan.RiskRewardRatio ?? double.NaN,
                (double)g, (double)g - BaseCostRoundTripUsd, pp.ExitReason?.ToString() ?? "?"));
        }
        tr = tr.OrderBy(t => t.Ts).ToList();

        W("");
        W("================ PHASE 0 — ANTI-DATA-SNOOPING ================");
        W("TimeSeriesMomentumModel constants (verified via git blame, commit 3ca0418, 2026-08-30):");
        W("  DefaultLookbacks {12,36,72,144}, TStatScale=2.0, AutocorrelationWindow=100, VolatilityWindow=60");
        W("  All four introduced in ONE commit, each doc-commented 'NON CALIBRATED'. The P0-2 calibration");
        W("  attempt (Tests/Research/TrendingCalibration) ran on M5 6-market 59d, delivered NOTHING (all 81");
        W("  grid cells negative), defaults unchanged. This MES H1 720d window was NEVER used to tune them.");
        W("  Caveat: lookbacks are documented as '1h/3h/6h/12h on an M5 chart' -> on H1 they mean 12h..144h");
        W("  (0.5..6 days). Timeframe-intent mismatch, but NOT a data-snooping issue (never fitted).");

        W("");
        W($"================ PHASE 1 — WALK-FORWARD SEGMENTS ================");
        W($"Total Trending Closed trades = {tr.Count}  ({tr.First().Ts:yyyy-MM-dd}..{tr.Last().Ts:yyyy-MM-dd})");
        W($"distinct signal days = {tr.Select(t => t.Ts.Date).Distinct().Count()}  (Trending is a rare, clustered regime)");
        W($"R:R: all == {tr.Min(t => t.Rr):0.00}..{tr.Max(t => t.Rr):0.00}");

        DateTime t0 = mes.FirstTimestamp, t1 = mes.LastTimestamp;
        foreach (int k in new[] { 6, 5, 4 })
        {
            W("");
            W($"--- {k} contiguous CALENDAR-EQUAL segments ---");
            W("  seg  dateRange                    n    winRate   grossR      netR (95% CI)         verdict");
            var segNet = new List<(int Idx, int N, double M, double Ci)>();
            for (int i = 0; i < k; i++)
            {
                DateTime a = t0 + TimeSpan.FromTicks((t1 - t0).Ticks * i / k);
                DateTime b = t0 + TimeSpan.FromTicks((t1 - t0).Ticks * (i + 1) / k);
                var seg = tr.Where(t => t.Ts >= a && t.Ts < b).ToList();
                if (i == k - 1) seg = tr.Where(t => t.Ts >= a).ToList();
                PrintSeg($"S{i + 1}", $"{a:yyyy-MM-dd}..{b:yyyy-MM-dd}", seg);
                (double m, double ci) = MeanCi(seg.Select(t => t.NetR));
                segNet.Add((i + 1, seg.Count, m, ci));
            }
            // Phase 2 tally for this k
            int pos = segNet.Count(s => s.N >= 20 && s.M - s.Ci > 0);
            int neg = segNet.Count(s => s.N >= 20 && s.M + s.Ci < 0);
            int inc = segNet.Count(s => s.N >= 20) - pos - neg;
            int tooFew = segNet.Count(s => s.N < 20);
            W($"  PHASE 2 [{k} seg]: robustly positive={pos}  robustly negative={neg}  inconclusive={inc}  (n<20, not counted={tooFew})");
        }

        // trade-count-equal view (equal n, unequal time) - exposes clustering
        W("");
        W("--- 6 TRADE-COUNT-EQUAL segments (equal n, unequal calendar span) ---");
        W("  seg  dateRange                    n    winRate   grossR      netR (95% CI)         verdict");
        int per = tr.Count / 6;
        for (int i = 0; i < 6; i++)
        {
            var seg = tr.Skip(i * per).Take(i == 5 ? tr.Count - 5 * per : per).ToList();
            PrintSeg($"Q{i + 1}", $"{seg.First().Ts:yyyy-MM-dd}..{seg.Last().Ts:yyyy-MM-dd}", seg);
        }

        // ================ PHASE 3 — CONCENTRATION ================
        W("");
        W("================ PHASE 3 — CONCENTRATION DIAGNOSTIC ================");
        ConcentrationReport("WHOLE BOOK", tr);

        // largest winning cluster = 15-calendar-day window with max summed netR
        (DateTime cs, DateTime ce, double clr, int cln) = LargestWinningWindow(tr, TimeSpan.FromDays(15));
        W($"largest winning 15-day window: {cs:yyyy-MM-dd}..{ce:yyyy-MM-dd}  n={cln}  sum netR={clr:0.00}");
        var without = tr.Where(t => t.Ts < cs || t.Ts > ce).ToList();
        (double wm, double wci) = MeanCi(without.Select(t => t.NetR));
        (double am, double aci) = MeanCi(tr.Select(t => t.NetR));
        W($"  whole book WITH cluster : n={tr.Count}  netR={am:+0.000;-0.000} +/-{aci:0.000}");
        W($"  whole book WITHOUT it   : n={without.Count}  netR={wm:+0.000;-0.000} +/-{wci:0.000}  " +
          $"=> sign {(Math.Sign(am) == Math.Sign(wm) && wm - wci > 0 ? "SURVIVES (still robustly +)" : wm > 0 ? "positive but not robust" : "does NOT survive (flips <= 0)")}");

        // repeat for the single best calendar-equal segment (k=6)
        DateTime st0 = mes.FirstTimestamp, st1 = mes.LastTimestamp;
        var seg6 = Enumerable.Range(0, 6).Select(i =>
        {
            DateTime a = st0 + TimeSpan.FromTicks((st1 - st0).Ticks * i / 6);
            DateTime b = i == 5 ? st1.AddSeconds(1) : st0 + TimeSpan.FromTicks((st1 - st0).Ticks * (i + 1) / 6);
            return (i + 1, tr.Where(t => t.Ts >= a && t.Ts < b).ToList());
        }).ToList();
        var best = seg6.Where(s => s.Item2.Count >= 20).OrderByDescending(s => MeanCi(s.Item2.Select(t => t.NetR)).mean).FirstOrDefault();
        if (best.Item2 is { Count: >= 20 })
        {
            W("");
            ConcentrationReport($"BEST k=6 SEGMENT (S{best.Item1})", best.Item2);
            (DateTime bs, DateTime be, double blr, int bln) = LargestWinningWindow(best.Item2, TimeSpan.FromDays(15));
            var bw = best.Item2.Where(t => t.Ts < bs || t.Ts > be).ToList();
            (double bm, double bci) = MeanCi(bw.Select(t => t.NetR));
            (double bam, double baci) = MeanCi(best.Item2.Select(t => t.NetR));
            W($"  S{best.Item1} largest winning 15-day window {bs:yyyy-MM-dd}..{be:yyyy-MM-dd} n={bln} sumR={blr:0.00}");
            W($"  S{best.Item1} WITH:    n={best.Item2.Count} netR={bam:+0.000;-0.000} +/-{baci:0.000}");
            W($"  S{best.Item1} WITHOUT: n={bw.Count} netR={bm:+0.000;-0.000} +/-{bci:0.000}  " +
              $"=> {(bm - bci > 0 ? "SURVIVES" : "does NOT survive")}");
        }

        // ================ PHASE 4 — RECONCILE WITH QDE-012 SINGLE 60/40 SPLIT ================
        W("");
        W("================ PHASE 4 — RECONCILIATION WITH QDE-012 60/40 SPLIT ================");
        int lo = tr.Min(t => t.Bar), hi = tr.Max(t => t.Bar);
        int splitBar = lo + (int)((hi - lo) * 0.60);
        var qTrain = tr.Where(t => t.Bar < splitBar - 20).ToList();
        var qOos = tr.Where(t => t.Bar >= splitBar).ToList();
        (double trm, double trci) = MeanCi(qTrain.Select(t => t.NetR));
        (double oom, double ooci) = MeanCi(qOos.Select(t => t.NetR));
        DateTime splitDate = byBar.TryGetValue(splitBar, out var sbSplit) ? sbSplit.Timestamp : default;
        W($"QDE-012 split: bar {splitBar} (purge 20) ~ {splitDate:yyyy-MM-dd}");
        W($"  TRAIN n={qTrain.Count} netR={trm:+0.000;-0.000} +/-{trci:0.000}   ({qTrain.First().Ts:yyyy-MM-dd}..{qTrain.Last().Ts:yyyy-MM-dd})");
        W($"  OOS   n={qOos.Count} netR={oom:+0.000;-0.000} +/-{ooci:0.000}   ({qOos.First().Ts:yyyy-MM-dd}..{qOos.Last().Ts:yyyy-MM-dd})");
        W($"  => the single OOS block is calendar range {qOos.First().Ts:yyyy-MM-dd}..{qOos.Last().Ts:yyyy-MM-dd};");
        W($"     compare to the k=6 segment view above to see which segment(s) that block overlaps.");

        WriteReport();
        Assert.True(tr.Count >= 50, $"expected >=50 Trending trades, got {tr.Count}");
    }

    private void PrintSeg(string tag, string range, IReadOnlyList<Trade> seg)
    {
        if (seg.Count == 0) { W($"  {tag,-4} {range,-28} n=0"); return; }
        (double m, double ci) = MeanCi(seg.Select(t => t.NetR));
        double gr = seg.Average(t => t.GrossR);
        double wr = seg.Count(t => t.Gross > 0) / (double)seg.Count;
        string verdict = seg.Count < 20 ? "n<20 (weak)"
            : m - ci > 0 ? "ROBUSTLY POSITIVE"
            : m + ci < 0 ? "robustly negative"
            : "inconclusive (straddles 0)";
        W($"  {tag,-4} {range,-28} {seg.Count,4}  {wr,7:0.000}  {gr,+8:0.000}  {m,+8:0.000} +/-{ci:0.000}   {verdict}");
    }

    private void ConcentrationReport(string label, IReadOnlyList<Trade> ts)
    {
        var byR = ts.OrderByDescending(t => t.NetR).ToList();
        double totalR = ts.Sum(t => t.NetR);
        double posR = ts.Where(t => t.NetR > 0).Sum(t => t.NetR);
        int top10 = Math.Max(1, (int)Math.Ceiling(ts.Count * 0.10));
        double top10R = byR.Take(top10).Sum(t => t.NetR);
        double top1R = byR.First().NetR;
        int nToHalfPos = 0; double acc = 0;
        foreach (Trade t in byR.Where(x => x.NetR > 0)) { acc += t.NetR; nToHalfPos++; if (acc >= 0.5 * posR) break; }
        W($"  [{label}] n={ts.Count}  sum netR={totalR:0.00}  (sum of positive-R trades={posR:0.00})");
        W($"     top 1 trade  = {top1R:0.00} R  ({(totalR != 0 ? 100 * top1R / totalR : 0):0}% of total, {(posR != 0 ? 100 * top1R / posR : 0):0}% of positive)");
        W($"     top 10% ({top10} trades) = {top10R:0.00} R  ({(totalR != 0 ? 100 * top10R / totalR : 0):0}% of total)");
        W($"     {nToHalfPos} winning trades ({(ts.Count > 0 ? 100.0 * nToHalfPos / ts.Count : 0):0}% of all) carry 50% of the positive R");
    }

    private static (DateTime start, DateTime end, double sumR, int n) LargestWinningWindow(IReadOnlyList<Trade> ts, TimeSpan width)
    {
        if (ts.Count == 0) return (default, default, 0, 0);
        var ordered = ts.OrderBy(t => t.Ts).ToList();
        double bestSum = double.NegativeInfinity; DateTime bs = default, be = default; int bn = 0;
        foreach (Trade anchor in ordered)
        {
            DateTime end = anchor.Ts + width;
            var win = ordered.Where(t => t.Ts >= anchor.Ts && t.Ts <= end).ToList();
            double s = win.Sum(t => t.NetR);
            if (s > bestSum) { bestSum = s; bs = anchor.Ts; be = end; bn = win.Count; }
        }
        return (bs, be, bestSum, bn);
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
        string od = Path.Combine(dir, "Research", "NewDataSourceFeasibility", "Output");
        Directory.CreateDirectory(od);
        File.WriteAllText(Path.Combine(od, "trending_walkforward.txt"), string.Join("\n", _log) + "\n");
    }
}
