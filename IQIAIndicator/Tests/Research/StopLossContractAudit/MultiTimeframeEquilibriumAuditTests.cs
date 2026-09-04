using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
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
/// READ-ONLY. 2026-08-31 audit "MeanReverting : equilibre multi-timeframe (M5 entree/stop, H1 cible)".
/// Isolated research probe - does NOT touch TradePlanBuilder / KalmanFilterModel / any production type.
/// Single variable changed: the Kalman equilibrium that becomes the TP is recomputed on a look-ahead-safe
/// H1 re-aggregation of the same M5 series (last COMPLETED hour only) instead of on the M5 closes. Entry
/// bar, entry price, stop-loss distance, horizon and sizing are all left exactly as the production
/// pipeline produced them. Reports the new TP-distance / R:R distribution and the net-of-cost expectancy
/// by R:R tranche, with the identical methodology as the previous MinRiskReward-threshold audit.
/// </summary>
public sealed class MultiTimeframeEquilibriumAuditTests
{
    private readonly ITestOutputHelper _output;
    private readonly YahooSessionDataset _yahoo;

    public MultiTimeframeEquilibriumAuditTests(ITestOutputHelper output, YahooSessionDataset yahoo)
    {
        _output = output;
        _yahoo = yahoo;
    }

    private const int WarmupBars = 128;
    private const int HorizonBars = 10;
    private const decimal InitialCapital = 25_000m;
    private const decimal PointValue = 5m;      // MES
    private const decimal TickSize = 0.25m;
    private const int KalmanWindow = 20;        // KalmanFilterModel.ObservationWindowSize

    // Base realistic MES round-trip cost, quantity 1 (identical to the previous audit's "base" scenario):
    //   spread 1 tick ($1.25) + slippage 0.25 tick/leg ($0.625) + commission ~$1.24 + fees ~$0.74
    private const double BaseCostRoundTripUsd = 1.25 + 0.625 + 1.24 + 0.74;   // = 3.855

    private static RiskPolicy Policy() => new(0.01m, null, null, null, null, null, null, null, null, null);
    private static InstrumentRiskSpecification Spec() => InstrumentRiskSpecification.FromInstrumentInfo(
        new InstrumentInfo("MES", TickSize: TickSize, TickValue: 1.25m, PointValue: PointValue, Decimals: 2),
        minQuantity: 1, maxQuantity: 50, quantityStep: 1);

    [Fact]
    public void Measure_H1_Equilibrium_Target_On_M5_Entries()
    {
        HistoricalSeries series;
        try { series = _yahoo.Require(); }
        catch (Exception ex) when (ex is YahooProviderException or HttpRequestException or TaskCanceledException)
        {
            Assert.Skip($"Yahoo provider unavailable: {ex.GetType().Name}: {ex.Message}");
            return;
        }

        var log = new List<string>();
        void W(string s) { _output.WriteLine(s); log.Add(s); }

        IReadOnlyList<HistoricalBar> bars = series.Bars;

        // ── PHASE 0 evidence: build the look-ahead-safe H1 close series from the M5 grid ────────────
        // Yahoo intraday HistoricalBar.Timestamp is the bar's OPEN (start of the 5-minute slot).
        // An M5 bar with open T closes at T+5min. An H1 bucket [H, H+1h) closes at H+1h.
        // At the decision instant for signal bar k (= close of bar k = bars[k].Timestamp + 5min, the
        // same instant the fill at bars[k+1].Open happens), bucket [H,H+1h) is usable IFF
        //     H + 1h <= bars[k].Timestamp + 5min
        // i.e. the ENTIRE hour lies at or before the current M5 close - a partially-formed hour can
        // never satisfy this. Kalman is stateless (recomputed from its 20-value window every call), so
        // feeding it H1 closes instead of M5 closes introduces no carried-over state.
        var hourClose = new List<(DateTime HourStart, decimal Close)>();
        {
            DateTime curHour = default;
            decimal lastCloseInHour = 0m;
            bool have = false;
            foreach (HistoricalBar b in bars)
            {
                DateTime h = new(b.Timestamp.Year, b.Timestamp.Month, b.Timestamp.Day, b.Timestamp.Hour, 0, 0, DateTimeKind.Utc);
                if (!have) { curHour = h; lastCloseInHour = b.Close; have = true; }
                else if (h == curHour) { lastCloseInHour = b.Close; }
                else { hourClose.Add((curHour, lastCloseInHour)); curHour = h; lastCloseInHour = b.Close; }
            }
            if (have) hourClose.Add((curHour, lastCloseInHour));
        }
        W($"H1 buckets re-aggregated from M5: {hourClose.Count} (over {bars.Count} M5 bars).");

        // last COMPLETED hour index for a given M5 signal-bar close instant
        int LastSafeHourIndex(DateTime m5Close)
        {
            int lo = 0, hi = hourClose.Count - 1, ans = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (hourClose[mid].HourStart.AddHours(1) <= m5Close) { ans = mid; lo = mid + 1; }
                else hi = mid - 1;
            }
            return ans;
        }

        // ── run the production pipeline once (baseline = M5-equilibrium TP) ────────────────────────
        var scenario = BacktestScenario.Create(
            series, new BacktestWindow("MTF-EQ", series.FirstTimestamp, series.LastTimestamp.AddMinutes(1)),
            InitialCapital, Spec(), Policy());
        MeasurementConfiguration meas = MeasurementConfiguration.Create(10, new[] { 0.001, 0.002 });
        ExecutionConfiguration exec = ExecutionConfiguration.Create(HorizonBars);
        PnLConfiguration pnl = PnLConfiguration.Create(
            InstrumentPnLSpecification.Create("MES", priceUnitValue: PointValue, currency: "USD"), 1, InitialCapital);
        BacktestFullResult full = new BacktestEngine().RunFullBacktest(scenario, WarmupBars, meas, exec, pnl);

        var byBar = new Dictionary<int, BacktestSignalResult>();
        foreach (BacktestSignalResult b in full.SignalResult.Bars) byBar[b.BarIndex] = b;
        var grossByPos = new Dictionary<int, decimal>();
        foreach (PositionPnLResult p in full.PnLResult.PositionPnLResults)
            if (p.Status == PositionStatus.Closed && p.GrossPnL is decimal g) grossByPos[p.PositionId] = g;

        // ── per MeanReverting directional signal ──────────────────────────────────────────────────
        var rows = new List<Row>();
        int mrDirectional = 0, m5Consistent = 0, h1Consistent = 0, h1NoBucket = 0, resimCheckN = 0;
        double resimAbsErrSum = 0;
        var days = new HashSet<DateTime>();

        foreach (BacktestSignalResult sb in full.SignalResult.Bars)
        {
            if (sb.Status != BacktestSignalStatus.Ready || sb.Decision is not { Winner: MarketState.MeanReverting }) continue;
            if (sb.TradePlan is not { } plan) continue;
            bool isBuy = plan.Direction == DirectionCandidate.BUY_CANDIDATE;
            bool isSell = plan.Direction == DirectionCandidate.SELL_CANDIDATE;
            if (!isBuy && !isSell) continue;
            if (plan.EntryPrice is not decimal refPrice || plan.StopLoss is not decimal sl) continue;
            double stopPts = (double)Math.Abs(refPrice - sl);
            if (stopPts <= 0) continue;
            int k = sb.BarIndex;
            if (k + 1 + HorizonBars >= bars.Count) continue;   // need full horizon window (same as ExecutionSimulator)

            mrDirectional++;
            days.Add(sb.Timestamp.Date);

            // M5-equilibrium consistency (what production actually did)
            bool m5Cons = plan.TakeProfit is decimal m5tp &&
                          (isBuy ? m5tp > refPrice : m5tp < refPrice);
            if (m5Cons) m5Consistent++;

            // H1 equilibrium: identical Kalman recursion over the last 20 COMPLETED hourly closes
            DateTime m5Close = bars[k].Timestamp.AddMinutes(5);
            int lastHour = LastSafeHourIndex(m5Close);
            if (lastHour < 1) { h1NoBucket++; continue; }
            int start = Math.Max(0, lastHour - KalmanWindow + 1);
            double[] obs = new double[lastHour - start + 1];
            for (int i = start; i <= lastHour; i++) obs[i - start] = (double)hourClose[i].Close;
            if (obs.Length < 2) { h1NoBucket++; continue; }
            double eqH1 = KalmanEstimatedMean(obs);
            if (!double.IsFinite(eqH1)) { h1NoBucket++; continue; }

            bool h1Cons = isBuy ? eqH1 > (double)refPrice : eqH1 < (double)refPrice;
            if (h1Cons) h1Consistent++;

            double m5TpDist = m5Cons ? (double)Math.Abs(refPrice - (decimal)plan.TakeProfit!.Value) : double.NaN;
            double h1TpDist = Math.Abs((double)refPrice - eqH1);
            double rrH1 = h1TpDist / stopPts;
            double rrM5 = plan.RiskRewardRatio ?? double.NaN;

            // ---- re-simulate the trade with the H1 target (SL/entry/horizon unchanged) ----
            (double grossH1, string exitH1) = Resim(bars, k, isBuy, refPrice, sl, h1Cons ? eqH1 : (double?)null);

            // ---- self-check: re-simulate with the ORIGINAL M5 target, compare to RunFullBacktest ----
            if (m5Cons && grossByPos.TryGetValue(k, out decimal realGross))
            {
                (double grossM5Resim, _) = Resim(bars, k, isBuy, refPrice, sl, (double)plan.TakeProfit!.Value);
                resimAbsErrSum += Math.Abs(grossM5Resim - (double)realGross);
                resimCheckN++;
            }

            double netH1 = grossH1 - (h1Cons ? BaseCostRoundTripUsd : 0.0);
            rows.Add(new Row(k, sb.Timestamp, rrM5, rrH1, m5TpDist, h1TpDist, stopPts,
                m5Cons, h1Cons, grossH1, netH1, exitH1));
        }

        W("");
        W("=== PHASE 1 : results ===");
        W($"MeanReverting directional signals (full horizon available) = {mrDirectional}");
        W($"trading days spanned = {days.Count}  ->  signals/day = {mrDirectional / (double)Math.Max(1, days.Count):0.0}  " +
          "(entry cadence unchanged: same M5 signals, question 4 CONFIRMED)");
        W($"re-sim self-check vs RunFullBacktest (M5 target): n={resimCheckN}  mean|grossErr|=${(resimCheckN > 0 ? resimAbsErrSum / resimCheckN : 0):0.0000}  (should be ~0)");
        W("");
        W($"TP-target consistency (equilibrium on profitable side of entry):");
        W($"  M5 equilibrium : {m5Consistent}/{mrDirectional} = {100.0 * m5Consistent / mrDirectional:0.0}%");
        W($"  H1 equilibrium : {h1Consistent}/{mrDirectional} = {100.0 * h1Consistent / mrDirectional:0.0}%   (question 5)");
        W($"  H1 had <2 completed hourly buckets (warmup, dropped) : {h1NoBucket}");

        var h1Rows = rows.Where(r => r.H1Cons).ToList();
        W("");
        W($"=== TP distance (points) : M5 target vs H1 target ===");
        var m5d = rows.Where(r => !double.IsNaN(r.M5TpDist)).Select(r => r.M5TpDist).ToList();
        var h1d = h1Rows.Select(r => r.H1TpDist).ToList();
        W($"  M5 target  n={m5d.Count}  median={Pct(m5d, .5):0.00}  p90={Pct(m5d, .9):0.00}  mean={m5d.DefaultIfEmpty(0).Average():0.00}");
        W($"  H1 target  n={h1d.Count}  median={Pct(h1d, .5):0.00}  p90={Pct(h1d, .9):0.00}  mean={h1d.DefaultIfEmpty(0).Average():0.00}");
        W($"  cost floor for reference: base round-trip = {BaseCostRoundTripUsd / (double)PointValue:0.00} pt");
        W("");
        W($"=== R:R per trade : M5 vs H1 ===");
        var rrM5all = rows.Where(r => !double.IsNaN(r.RrM5)).Select(r => r.RrM5).ToList();
        var rrH1all = h1Rows.Select(r => r.RrH1).ToList();
        W($"  M5  median={Pct(rrM5all, .5):0.000}  p90={Pct(rrM5all, .9):0.000}  %>=0.5={100.0 * rrM5all.Count(x => x >= 0.5) / Math.Max(1, rrM5all.Count):0.0}");
        W($"  H1  median={Pct(rrH1all, .5):0.000}  p90={Pct(rrH1all, .9):0.000}  %>=0.5={100.0 * rrH1all.Count(x => x >= 0.5) / Math.Max(1, rrH1all.Count):0.0}  %>=1.0={100.0 * rrH1all.Count(x => x >= 1.0) / Math.Max(1, rrH1all.Count):0.0}");
        W("");
        W("=== H1-target trades : exit reason (gross / net base) ===");
        foreach (var grp in h1Rows.GroupBy(r => r.Exit).OrderByDescending(x => x.Count()))
            W($"  {grp.Key,-12} n={grp.Count(),5}  gross$/tr={grp.Average(r => r.GrossUsd),7:0.00}  net$/tr={grp.Average(r => r.NetUsd),7:0.00}");

        // ── expectancy by R:R tranche, H1 target, net of base cost ────────────────────────────────
        W("");
        W("=== H1 target : expectancy net of base cost, by R:R tranche ===");
        W("  -- coarse --");
        W("  tranche            n   winRate  avgWin$  avgLoss$   expGross$  expNet$   expNetR (95%CI)");
        foreach (var (lbl, lo, hi) in new[] { ("R:R<0.5", 0.0, 0.5), ("0.5<=R:R<1.0", 0.5, 1.0), ("1.0<=R:R<2.0", 1.0, 2.0), ("R:R>=2.0", 2.0, 1e9) })
        {
            var b = h1Rows.Where(r => r.RrH1 >= lo && r.RrH1 < hi).ToList();
            if (b.Count == 0) { W($"  {lbl,-16} n=0"); continue; }
            var wins = b.Where(r => r.NetUsd > 0).ToList();
            var los = b.Where(r => r.NetUsd <= 0).ToList();
            (double m, double ci) nr = MeanCi(b.Select(r => r.NetUsd / (r.StopPts * (double)PointValue)));
            W($"  {lbl,-16} {b.Count,4}  {wins.Count / (double)b.Count,7:0.000}  {(wins.Count > 0 ? wins.Average(r => r.NetUsd) : 0),7:0.00}  " +
              $"{(los.Count > 0 ? los.Average(r => r.NetUsd) : 0),8:0.00}  {b.Average(r => r.GrossUsd),8:0.00}  {b.Average(r => r.NetUsd),7:0.00}  {nr.m,7:0.000} +/-{nr.ci,5:0.000}");
        }
        W("  -- fine 0.1 grid (n>=30 only trustworthy) --");
        W("  band          n     gross$/tr    net$/tr (95%CI)         netR/tr (95%CI)      flag");
        for (double lo = 0.0; lo < 2.5 - 1e-9; lo += 0.1)
        {
            double hi = lo + 0.1;
            var b = h1Rows.Where(r => r.RrH1 >= lo && r.RrH1 < hi).ToList();
            if (b.Count == 0) continue;
            (double m, double ci) n = MeanCi(b.Select(r => r.NetUsd));
            (double m, double ci) nr = MeanCi(b.Select(r => r.NetUsd / (r.StopPts * (double)PointValue)));
            string flag = b.Count < 30 ? "n<30" : (n.m - n.ci > 0 ? "NET+ robust" : n.m + n.ci < 0 ? "NET- robust" : "straddles 0");
            W($"  [{lo:0.0},{hi:0.0}) {b.Count,5}  {b.Average(r => r.GrossUsd),9:0.00}   {n.m,8:0.00} +/-{n.ci,6:0.00}   {nr.m,7:0.000} +/-{nr.ci,6:0.000}   {flag}");
        }
        W("  -- cumulative: all H1-target trades with R:R >= x, net base --");
        W("  x     n     net$/tr (95%CI)        netR/tr (95%CI)");
        for (double x = 0.0; x <= 2.4 + 1e-9; x += 0.2)
        {
            var b = h1Rows.Where(r => r.RrH1 >= x).ToList();
            if (b.Count == 0) continue;
            (double m, double ci) d = MeanCi(b.Select(r => r.NetUsd));
            (double m, double ci) dr = MeanCi(b.Select(r => r.NetUsd / (r.StopPts * (double)PointValue)));
            W($"  {x:0.0} {b.Count,6}  {d.m,8:0.00} +/-{d.ci,6:0.00}    {dr.m,7:0.000} +/-{dr.ci,6:0.000}");
        }

        // whole-book comparison
        W("");
        W("=== whole MeanReverting book, net of base cost ===");
        W($"  M5 target (previous audit)   : expectancy ~ -4.00 $/trade  (-0.203 R)   n~2293");
        double bookNet = h1Rows.Sum(r => r.NetUsd) + rows.Where(r => !r.H1Cons).Sum(_ => 0.0);
        W($"  H1 target (this audit)       : expectancy {h1Rows.Average(r => r.NetUsd):0.00} $/trade  " +
          $"({h1Rows.Average(r => r.NetUsd / (r.StopPts * (double)PointValue)):0.000} R)   n={h1Rows.Count} tradeable " +
          $"(+{rows.Count - h1Rows.Count} now SIGNAL_ONLY: H1 target inconsistent)");

        WriteReport(log);
    }

    private sealed record Row(int Bar, DateTime Ts, double RrM5, double RrH1, double M5TpDist, double H1TpDist,
        double StopPts, bool M5Cons, bool H1Cons, double GrossUsd, double NetUsd, string Exit);

    /// <summary>Exact copy of KalmanFilterModel.Evaluate's recursion: EstimatedMean = stateMean AFTER the
    /// loop over observations[1..n-1] (the last observation is NOT applied as an update - it only feeds the
    /// innovation diagnostic there). Window slicing to the last 20 obs is done by the caller.</summary>
    private static double KalmanEstimatedMean(double[] observations)
    {
        if (observations.Length < 2) return double.NaN;
        const double MinVar = 1e-6, MinNoise = 1e-6;
        double mean = observations.Average();
        double measNoise = Math.Max(observations.Sum(v => (v - mean) * (v - mean)) / observations.Length, MinNoise);
        double procNoise = Math.Max(measNoise * 0.1, MinNoise);
        double stateMean = observations[0];
        double stateCov = Math.Max(observations.Sum(v => (v - mean) * (v - mean)) / (observations.Length - 1), MinVar);
        for (int i = 1; i < observations.Length; i++)
        {
            double predMean = stateMean;
            double predCov = stateCov + procNoise;
            double innovation = observations[i] - predMean;
            double innovCov = predCov + measNoise;
            double gain = innovCov <= 0.0 ? 0.0 : predCov / innovCov;
            gain = Math.Clamp(gain, 0.0, 1.0);
            stateMean = predMean + gain * innovation;
            stateCov = Math.Max((1.0 - gain) * predCov, MinVar);
        }
        return stateMean;
    }

    /// <summary>Mirrors ExecutionSimulator.SimulateCore's intrabar SL/TP race (Lot 15.4/15.5):
    /// fill at bars[k+1].Open, reconcile SL/TP DISTANCE from the signal reference price onto the fill,
    /// monitor bars[k+1 .. k+1+Horizon] inclusive, ambiguous bar -> StopLoss, else exit at horizon close.</summary>
    private static (double grossUsd, string exit) Resim(
        IReadOnlyList<HistoricalBar> bars, int k, bool isBuy, decimal refPrice, decimal sl, double? targetEq)
    {
        int fill = k + 1;
        decimal entry = bars[fill].Open;
        decimal slDist = Math.Abs(refPrice - sl);
        decimal slExec = isBuy ? entry - slDist : entry + slDist;
        decimal? tpExec = null;
        if (targetEq is double eq)
        {
            decimal tpDist = Math.Abs(refPrice - (decimal)eq);
            tpExec = isBuy ? entry + tpDist : entry - tpDist;
        }
        int exitBar = fill + HorizonBars;
        for (int m = fill; m <= exitBar; m++)
        {
            HistoricalBar mb = bars[m];
            bool stop = isBuy ? mb.Low <= slExec : mb.High >= slExec;
            bool tgt = tpExec is decimal tl && (isBuy ? mb.High >= tl : mb.Low <= tl);
            if (!stop && !tgt) continue;
            decimal px; string why;
            if (stop && tgt) { px = slExec; why = "Ambiguous"; }
            else if (stop) { px = slExec; why = "StopLoss"; }
            else { px = tpExec!.Value; why = "TakeProfit"; }
            decimal mv0 = isBuy ? px - entry : entry - px;
            return ((double)(mv0 * PointValue), why);
        }
        decimal exitPx = bars[exitBar].Close;
        decimal mv = isBuy ? exitPx - entry : entry - exitPx;
        return ((double)(mv * PointValue), "TimeHorizon");
    }

    private static double Pct(List<double> xs, double p)
    {
        if (xs.Count == 0) return double.NaN;
        var v = xs.OrderBy(x => x).ToArray();
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

    private void WriteReport(IReadOnlyList<string> lines)
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "IQIAIndicator.Tests.csproj")))
            dir = Directory.GetParent(dir)?.FullName;
        if (dir is null) return;
        string od = Path.Combine(dir, "Research", "StopLossContractAudit", "Output");
        Directory.CreateDirectory(od);
        File.WriteAllText(Path.Combine(od, "mtf_equilibrium.txt"), string.Join("\n", lines) + "\n");
    }
}
