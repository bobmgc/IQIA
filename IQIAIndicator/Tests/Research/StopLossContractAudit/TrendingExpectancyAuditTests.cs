using System;
using System.Collections.Generic;
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
/// READ-ONLY. 2026-08-31 audit "Trending expectancy nette de couts". Isolated probe, no production change.
/// Same dataset / config / "base" cost model as the MeanReverting audits, but isolates the Trending-regime
/// Closed trades (TP = 2 x stop distance, R:R fixed at 2.0). Global expectancy (gross + net base) in $ and
/// R with 95% CI, exit-reason mix, and the same purged temporal split.
/// </summary>
public sealed class TrendingExpectancyAuditTests
{
    private readonly ITestOutputHelper _output;
    private readonly YahooSessionDataset _yahoo;

    public TrendingExpectancyAuditTests(ITestOutputHelper output, YahooSessionDataset yahoo)
    {
        _output = output;
        _yahoo = yahoo;
    }

    private const int WarmupBars = 128;
    private const int HorizonBars = 10;
    private const decimal InitialCapital = 25_000m;
    private const decimal PointValue = 5m;   // MES
    private const double BaseCostRoundTripUsd = 1.25 + 0.625 + 1.24 + 0.74;   // = 3.855, identical to prior audits

    private static RiskPolicy Policy() => new(0.01m, null, null, null, null, null, null, null, null, null);
    private static InstrumentRiskSpecification Spec() => InstrumentRiskSpecification.FromInstrumentInfo(
        new InstrumentInfo("MES", TickSize: 0.25m, TickValue: 1.25m, PointValue: PointValue, Decimals: 2),
        minQuantity: 1, maxQuantity: 50, quantityStep: 1);

    private sealed record Trade(int Bar, DateTime Ts, double Rr, double StopPts, double GrossUsd, double NetUsd, string Exit)
    {
        public double RiskUsd => StopPts * (double)PointValue;
        public double GrossR => GrossUsd / RiskUsd;
        public double NetR => NetUsd / RiskUsd;
    }

    [Fact]
    public void Measure_Trending_Expectancy_Net_Of_Costs()
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

        var scenario = BacktestScenario.Create(
            series, new BacktestWindow("TREND-EXP", series.FirstTimestamp, series.LastTimestamp.AddMinutes(1)),
            InitialCapital, Spec(), Policy());
        MeasurementConfiguration meas = MeasurementConfiguration.Create(10, new[] { 0.001, 0.002 });
        ExecutionConfiguration exec = ExecutionConfiguration.Create(HorizonBars);
        PnLConfiguration pnl = PnLConfiguration.Create(
            InstrumentPnLSpecification.Create("MES", priceUnitValue: PointValue, currency: "USD"), 1, InitialCapital);

        BacktestFullResult full = new BacktestEngine().RunFullBacktest(scenario, WarmupBars, meas, exec, pnl);

        var byBar = new Dictionary<int, BacktestSignalResult>();
        int trendDirectional = 0, planReady = 0, planSignalOnly = 0, planRejected = 0, planOther = 0;
        foreach (BacktestSignalResult b in full.SignalResult.Bars)
        {
            byBar[b.BarIndex] = b;
            if (b.Status != BacktestSignalStatus.Ready || b.Decision is not { Winner: MarketState.Trending }) continue;
            if (b.TradePlan is not { } p) continue;
            if (p.Direction is not (DirectionCandidate.BUY_CANDIDATE or DirectionCandidate.SELL_CANDIDATE)) continue;
            trendDirectional++;
            switch (p.Status)
            {
                case TradePlanStatus.PLAN_READY: planReady++; break;
                case TradePlanStatus.SIGNAL_ONLY: planSignalOnly++; break;
                case TradePlanStatus.PLAN_REJECTED: planRejected++; break;
                default: planOther++; break;
            }
        }

        var trades = new List<Trade>();
        var statusMix = new Dictionary<PositionStatus, int>();
        foreach (PositionPnLResult pp in full.PnLResult.PositionPnLResults)
        {
            if (!byBar.TryGetValue(pp.PositionId, out BacktestSignalResult? sb)) continue;
            if (sb.Decision is not { Winner: MarketState.Trending }) continue;
            if (pp.Direction is not (DirectionCandidate.BUY_CANDIDATE or DirectionCandidate.SELL_CANDIDATE)) continue;
            statusMix[pp.Status] = statusMix.GetValueOrDefault(pp.Status) + 1;
            if (pp.Status != PositionStatus.Closed || pp.GrossPnL is not decimal g) continue;
            if (sb.TradePlan is not { } plan || plan.EntryPrice is not decimal ep || plan.StopLoss is not decimal sl) continue;
            double stopPts = (double)Math.Abs(ep - sl);
            if (stopPts <= 0) continue;
            trades.Add(new Trade(pp.PositionId, sb.Timestamp, plan.RiskRewardRatio ?? double.NaN, stopPts,
                (double)g, (double)g - BaseCostRoundTripUsd, pp.ExitReason?.ToString() ?? "?"));
        }

        W($"=== CONFIG ===  warmup={WarmupBars} horizon={HorizonBars} qty=1  base RT cost=${BaseCostRoundTripUsd:0.000}");
        W($"Trending directional signals = {trendDirectional} : PLAN_READY={planReady} SIGNAL_ONLY={planSignalOnly} " +
          $"PLAN_REJECTED={planRejected} other={planOther}");
        W("Trending position status: " + string.Join(", ", statusMix.OrderByDescending(k => k.Value).Select(k => $"{k.Key}={k.Value}")));
        W($"=> CLOSED Trending trades with P&L = {trades.Count}");
        if (trades.Count == 0) { W("no closed Trending trade."); WriteReport(log); return; }

        W("");
        W($"R:R : min={trades.Min(t => t.Rr):0.000} median={Median(trades.Select(t => t.Rr)):0.000} max={trades.Max(t => t.Rr):0.000}  " +
          $"(all == 2.0 by construction: TP = 2 x stop distance)");
        W($"stop distance pts : median={Median(trades.Select(t => t.StopPts)):0.00}  " +
          $"=> risk $/trade median=${Median(trades.Select(t => t.RiskUsd)):0.0}");

        W("");
        W("=== exit reason (CLOSED Trending) ===");
        foreach (var grp in trades.GroupBy(t => t.Exit).OrderByDescending(x => x.Count()))
            W($"  {grp.Key,-12} n={grp.Count(),4} ({100.0 * grp.Count() / trades.Count,4:0.0}%)  " +
              $"winRate(gross)={grp.Count(x => x.GrossUsd > 0) / (double)grp.Count():0.000}  " +
              $"gross$/tr={grp.Average(x => x.GrossUsd),7:0.00}  net$/tr={grp.Average(x => x.NetUsd),7:0.00}");

        W("");
        W("=== GLOBAL Trending expectancy ===");
        Block("ALL Trending", trades, W);

        // temporal split (same recipe as the MeanReverting audit: 60% split, purge 100 bars)
        int minBar = trades.Min(t => t.Bar), maxBar = trades.Max(t => t.Bar);
        int split = minBar + (int)((maxBar - minBar) * 0.60);
        const int purge = 100;
        var train = trades.Where(t => t.Bar < split - purge).ToList();
        var oos = trades.Where(t => t.Bar >= split).ToList();
        W("");
        W($"=== temporal split (train bar<{split - purge}, purge {purge}, oos bar>={split}) ===");
        Block("TRAIN", train, W);
        Block("OOS", oos, W);

        WriteReport(log);
    }

    private static void Block(string label, IReadOnlyList<Trade> ts, Action<string> W)
    {
        if (ts.Count == 0) { W($"[{label}] n=0"); return; }
        var wins = ts.Where(t => t.NetUsd > 0).ToList();
        var los = ts.Where(t => t.NetUsd <= 0).ToList();
        var winsG = ts.Where(t => t.GrossUsd > 0).ToList();
        (double m, double ci) g = MeanCi(ts.Select(t => t.GrossUsd));
        (double m, double ci) n = MeanCi(ts.Select(t => t.NetUsd));
        (double m, double ci) gr = MeanCi(ts.Select(t => t.GrossR));
        (double m, double ci) nr = MeanCi(ts.Select(t => t.NetR));
        double wrGross = winsG.Count / (double)ts.Count;
        double breakEven = 1.0 / (1.0 + 2.0);   // R:R fixed at 2.0
        W($"[{label}] n={ts.Count}");
        W($"  win rate (gross) = {wrGross:0.000}   break-even threshold 1/(1+2.0) = {breakEven:0.000}   " +
          $"gap = {wrGross - breakEven:+0.000;-0.000}");
        W($"  win rate (net)   = {wins.Count / (double)ts.Count:0.000}");
        W($"  avg win  (net) = ${(wins.Count > 0 ? wins.Average(t => t.NetUsd) : 0):0.00}    avg loss (net) = ${(los.Count > 0 ? los.Average(t => t.NetUsd) : 0):0.00}");
        W($"  expectancy GROSS = ${g.m:0.00} +/- {g.ci:0.00} /trade   ({gr.m:0.000} +/- {gr.ci:0.000} R)");
        W($"  expectancy NET   = ${n.m:0.00} +/- {n.ci:0.00} /trade   ({nr.m:0.000} +/- {nr.ci:0.000} R)");
        W($"  total net P&L (qty 1) = ${ts.Sum(t => t.NetUsd):0.0}");
    }

    private static double Median(IEnumerable<double> xs)
    {
        var v = xs.OrderBy(x => x).ToArray();
        if (v.Length == 0) return double.NaN;
        return v.Length % 2 == 1 ? v[v.Length / 2] : 0.5 * (v[v.Length / 2 - 1] + v[v.Length / 2]);
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
        File.WriteAllText(Path.Combine(od, "trending_expectancy.txt"), string.Join("\n", lines) + "\n");
    }
}
