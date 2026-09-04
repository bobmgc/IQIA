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
using IQIAIndicator.Engine.ScientificModels.Abstractions;
using IQIAIndicator.Engine.TradePlan;
using IQIAIndicator.Tests.BacktestTests.Yahoo;
using Xunit;

namespace IQIAIndicator.Tests.Research.StopLossContractAudit;

/// <summary>
/// READ-ONLY. 2026-08-31 audit "sensibilite du seuil d'entree MeanReverting". No production change.
/// PHASE 0 finding (see report): production has NO deviation-magnitude threshold - EntryTriggerBuilder
/// emits a directional MeanReverting candidate on sign(DynamicZScore) alone (zScore != 0), gated only by
/// the regime ambiguity gate (0.95) and a scientific-confidence floor. This probe re-runs the unchanged
/// RunFullBacktest, captures per-trade deviation-to-equilibrium |EntryPrice - EstimatedEquilibrium| and
/// the current sigma (VolatilityModel.CurrentVolatility), then applies a POST-HOC filter |dev|/sigma >= x
/// for x in {0.5,1,1.5,2,2.5,3} and reports net-of-cost expectancy per threshold, on native 1h (best
/// statistical power) and M5.
/// </summary>
public sealed class MeanRevertingDeviationThresholdAuditTests
{
    private readonly ITestOutputHelper _output;
    private readonly YahooSessionDataset _yahoo;

    public MeanRevertingDeviationThresholdAuditTests(ITestOutputHelper output, YahooSessionDataset yahoo)
    {
        _output = output;
        _yahoo = yahoo;
    }

    private const int WarmupBars = 128;
    private const int HorizonBars = 10;
    private const decimal InitialCapital = 25_000m;
    private const decimal PointValue = 5m;
    private const double BaseCostRoundTripUsd = 1.25 + 0.625 + 1.24 + 0.74;   // = 3.855

    private static RiskPolicy Policy() => new(0.01m, null, null, null, null, null, null, null, null, null);
    private static InstrumentRiskSpecification Spec() => InstrumentRiskSpecification.FromInstrumentInfo(
        new InstrumentInfo("MES", TickSize: 0.25m, TickValue: 1.25m, PointValue: PointValue, Decimals: 2),
        minQuantity: 1, maxQuantity: 50, quantityStep: 1);

    private readonly List<string> _log = new();
    private void W(string s) { _output.WriteLine(s); _log.Add(s); }

    private sealed record Row(int Bar, double DevPts, double Sigma, double Rr, double StopPts,
        double Gross, double Net, string Exit)
    {
        public double DevOverSigma => Sigma > 0 ? DevPts / Sigma : double.NaN;
        public double RiskUsd => StopPts * (double)PointValue;
        public double NetR => Net / RiskUsd;
        public double GrossR => Gross / RiskUsd;
    }

    [Fact]
    public void Sweep_Deviation_Threshold_On_MeanReverting()
    {
        HistoricalSeries m5;
        try { m5 = _yahoo.Require(); }
        catch (Exception ex) when (ex is YahooProviderException or HttpRequestException or TaskCanceledException)
        {
            Assert.Skip($"Yahoo provider unavailable: {ex.GetType().Name}: {ex.Message}");
            return;
        }

        var series = new List<(string Tf, HistoricalSeries S)> { ("M5 (59d)", m5) };
        try
        {
            var client = new HttpYahooChartClient();
            string ticker = YahooSymbolMap.Resolve("MES");
            DateTime to = DateTime.UtcNow;
            string json = client.FetchChartJson(ticker, "1h", to.AddDays(-720), to, CancellationToken.None);
            YahooChartParser.ParseResult parsed = YahooChartParser.Parse(json);
            series.Insert(0, ("H1 native (720d)", HistoricalSeries.Create("MES", "H1", "UTC", "Yahoo(native-1h)", parsed.Bars)));
            W($"native 1h: {parsed.Bars.Count} bars, {parsed.GapCount} gaps omitted");
        }
        catch (Exception ex) { W($"native 1h pull failed ({ex.GetType().Name}: {ex.Message}) - M5 only."); }

        foreach ((string tf, HistoricalSeries s) in series)
        {
            W("");
            W("########################################################################");
            W($"###  {tf}   ({s.Count} bars)");
            W("########################################################################");
            try { RunOne(s); }
            catch (Exception ex) { W($"  RUN FAILED: {ex.GetType().Name}: {ex.Message}"); }
        }
        WriteReport();
    }

    private void RunOne(HistoricalSeries series)
    {
        var scenario = BacktestScenario.Create(
            series, new BacktestWindow("DEV-THRESH", series.FirstTimestamp, series.LastTimestamp.AddSeconds(1)),
            InitialCapital, Spec(), Policy());
        MeasurementConfiguration meas = MeasurementConfiguration.Create(10, new[] { 0.001, 0.002 });
        ExecutionConfiguration exec = ExecutionConfiguration.Create(HorizonBars);
        PnLConfiguration pnl = PnLConfiguration.Create(
            InstrumentPnLSpecification.Create("MES", priceUnitValue: PointValue, currency: "USD"), 1, InitialCapital);
        BacktestFullResult full = new BacktestEngine().RunFullBacktest(scenario, WarmupBars, meas, exec, pnl);

        var byBar = new Dictionary<int, BacktestSignalResult>();
        foreach (BacktestSignalResult b in full.SignalResult.Bars) byBar[b.BarIndex] = b;

        var rows = new List<Row>();
        int noSigma = 0, noEq = 0;
        foreach (PositionPnLResult pp in full.PnLResult.PositionPnLResults)
        {
            if (!byBar.TryGetValue(pp.PositionId, out BacktestSignalResult? sb)) continue;
            if (sb.Decision is not { Winner: MarketState.MeanReverting }) continue;
            if (pp.Direction is not (DirectionCandidate.BUY_CANDIDATE or DirectionCandidate.SELL_CANDIDATE)) continue;
            if (pp.Status != PositionStatus.Closed || pp.GrossPnL is not decimal g) continue;
            if (sb.TradePlan is not { EntryPrice: decimal ep, StopLoss: decimal sl }) continue;
            double stopPts = (double)Math.Abs(ep - sl);
            if (stopPts <= 0) continue;

            if (sb.EntryTrigger?.Assessment.EstimatedEquilibrium is not double eq || !double.IsFinite(eq)) { noEq++; continue; }
            double devPts = Math.Abs((double)ep - eq);
            double sigma = ReadCurrentVolatility(sb.EntryTrigger);
            if (!(sigma > 0)) { noSigma++; continue; }

            rows.Add(new Row(pp.PositionId, devPts, sigma, sb.TradePlan.RiskRewardRatio ?? double.NaN, stopPts,
                (double)g, (double)g - BaseCostRoundTripUsd, pp.ExitReason?.ToString() ?? "?"));
        }

        W($"  MeanReverting closed trades usable = {rows.Count}  (dropped: noEquilibrium={noEq}, noSigma={noSigma})");
        if (rows.Count == 0) return;

        // sanity: dev/sigma should be ~ 2 x R:R (SL = 2 sigma, rounded toward entry)
        var withRr = rows.Where(r => !double.IsNaN(r.Rr) && r.Rr > 0).ToList();
        if (withRr.Count > 0)
            W($"  check: mean(dev/sigma)={rows.Average(r => r.DevOverSigma):0.000}  mean(2*R:R)={2 * withRr.Average(r => r.Rr):0.000}  " +
              $"mean(SL/sigma)={rows.Average(r => r.StopPts / r.Sigma):0.000} (expect ~2.0)");

        var ds = rows.Select(r => r.DevOverSigma).Where(double.IsFinite).OrderBy(x => x).ToList();
        W($"  deviation/sigma distribution: min={ds.First():0.00} p10={P(ds, .1):0.00} p25={P(ds, .25):0.00} " +
          $"median={P(ds, .5):0.00} p75={P(ds, .75):0.00} p90={P(ds, .9):0.00} max={ds.Last():0.00}");
        W($"  (hypothesis check: median deviation ~0.76 sigma?)  -> measured median = {P(ds, .5):0.000} sigma");

        W("");
        W("  === POST-HOC FILTER  |dev|/sigma >= x  (base cost) ===");
        W("  x     n     %kept  medR:R   winRate  avgWinNet$  avgLossNet$   expNet$/tr (95%CI)     expNetR/tr (95%CI)     verdict");
        foreach (double x in new[] { 0.0, 0.5, 1.0, 1.5, 2.0, 2.5, 3.0, 3.5, 4.0 })
        {
            var b = rows.Where(r => r.DevOverSigma >= x).ToList();
            if (b.Count == 0) { W($"  {x:0.0}   0"); continue; }
            var wn = b.Where(r => r.Net > 0).ToList();
            var ln = b.Where(r => r.Net <= 0).ToList();
            (double m, double ci) n = MeanCi(b.Select(r => r.Net));
            (double m, double ci) nr = MeanCi(b.Select(r => r.NetR));
            double medrr = P(b.Where(r => !double.IsNaN(r.Rr)).Select(r => r.Rr).OrderBy(v => v).ToList(), .5);
            string verdict = b.Count < 30 ? "n<30 (ignore)" : (n.m - n.ci > 0 ? "NET+ ROBUST" : n.m + n.ci < 0 ? "net- robust" : "straddles 0");
            W($"  {x:0.0} {b.Count,6}  {100.0 * b.Count / rows.Count,4:0.0}  {medrr,6:0.000}  {wn.Count / (double)b.Count,6:0.000}  " +
              $"{(wn.Count > 0 ? wn.Average(r => r.Net) : 0),9:0.00}  {(ln.Count > 0 ? ln.Average(r => r.Net) : 0),10:0.00}   " +
              $"{n.m,7:0.00} +/-{n.ci,6:0.00}    {nr.m,7:0.000} +/-{nr.ci,6:0.000}   {verdict}");
        }

        W("");
        W("  === train/OOS split at each threshold (60/40, purge 20 bars) ===");
        int lo = rows.Min(r => r.Bar), hi = rows.Max(r => r.Bar);
        int split = lo + (int)((hi - lo) * 0.60);
        foreach (double x in new[] { 0.0, 1.0, 1.5, 2.0, 2.5, 3.0 })
        {
            var b = rows.Where(r => r.DevOverSigma >= x).ToList();
            var tr = b.Where(r => r.Bar < split - 20).ToList();
            var oo = b.Where(r => r.Bar >= split).ToList();
            if (tr.Count < 30 || oo.Count < 30) { W($"  x={x:0.0}: train n={tr.Count}, oos n={oo.Count} - one side <30, skip"); continue; }
            (double m, double ci) trN = MeanCi(tr.Select(r => r.NetR));
            (double m, double ci) ooN = MeanCi(oo.Select(r => r.NetR));
            W($"  x={x:0.0}  TRAIN n={tr.Count} netR={trN.m:+0.000;-0.000} +/-{trN.ci:0.000}   " +
              $"OOS n={oo.Count} netR={ooN.m:+0.000;-0.000} +/-{ooN.ci:0.000}");
        }

        // fine grid on dev/sigma
        W("");
        W("  === fine grid on |dev|/sigma (0.25 wide, n>=30) ===");
        W("  band          n     gross$/tr   net$/tr (95%CI)        netR/tr (95%CI)      flag");
        for (double x = 0.0; x < 4.0 - 1e-9; x += 0.25)
        {
            var b = rows.Where(r => r.DevOverSigma >= x && r.DevOverSigma < x + 0.25).ToList();
            if (b.Count == 0) continue;
            (double m, double ci) n = MeanCi(b.Select(r => r.Net));
            (double m, double ci) nr = MeanCi(b.Select(r => r.NetR));
            string flag = b.Count < 30 ? "n<30" : (n.m - n.ci > 0 ? "NET+ ROBUST" : n.m + n.ci < 0 ? "net- robust" : "straddles 0");
            W($"  [{x:0.00},{x + 0.25:0.00}) {b.Count,5}  {b.Average(r => r.Gross),9:0.00}   {n.m,8:0.00} +/-{n.ci,6:0.00}   {nr.m,7:0.000} +/-{nr.ci,6:0.000}   {flag}");
        }
    }

    private static double ReadCurrentVolatility(EntryTriggerCandidate etc)
    {
        IReadOnlyList<ScientificModelResult>? results = etc.EntryCandidate?.Assessment?.ScientificAssessment?.ScientificResults;
        if (results is null) return double.NaN;
        foreach (ScientificModelResult r in results)
        {
            if (!string.Equals(r.ModelName, "VolatilityModel", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(r.ModelName, "TimeSeriesMomentumModel", StringComparison.OrdinalIgnoreCase)) continue;
            if (!r.Success || r.Metrics is null) continue;
            if (r.Metrics.TryGetValue(ScientificMetricKeys.CurrentVolatility, out object? raw) && raw is double d && double.IsFinite(d) && d > 0)
                return d;
        }
        return double.NaN;
    }

    private static double P(List<double> v, double p)
    {
        if (v.Count == 0) return double.NaN;
        if (v.Count == 1) return v[0];
        double r = p * (v.Count - 1);
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
        File.WriteAllText(Path.Combine(od, "deviation_threshold.txt"), string.Join("\n", _log) + "\n");
    }
}
