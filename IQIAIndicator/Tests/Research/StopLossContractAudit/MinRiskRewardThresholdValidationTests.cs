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
using IQIAIndicator.Backtest.Cost;
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
/// READ-ONLY. 2026-08-31 audit "Validation du seuil MinRiskReward (MeanReverting)". Does NOT modify any
/// production type and does NOT enable MinRiskReward anywhere. Runs the unmodified
/// <see cref="BacktestEngine.RunFullBacktest"/>, then applies <see cref="PositionCostCalculator"/>
/// (Lot 14.7, unmodified) post-hoc with several realistic MES cost scenarios, and reports the executed
/// MeanReverting trades bucketed by R:R (coarse + 0.1-wide fine grid, with n / stddev / 95% CI), in $
/// and in R multiples, for the 59-day window, a purged temporal split of it, and (best effort) ES=F as a
/// second instrument.
/// </summary>
public sealed class MinRiskRewardThresholdValidationTests
{
    private readonly ITestOutputHelper _output;
    private readonly YahooSessionDataset _yahoo;

    public MinRiskRewardThresholdValidationTests(ITestOutputHelper output, YahooSessionDataset yahoo)
    {
        _output = output;
        _yahoo = yahoo;
    }

    private const int WarmupBars = 128;
    private const int HorizonBars = 10;
    private const decimal InitialCapital = 25_000m;

    // MinRiskReward stays null (production default). This audit validates a threshold, never sets one.
    private static RiskPolicy Policy() => new(0.01m, null, null, null, null, null, null, null, null, null);

    private static InstrumentRiskSpecification Spec(string sym, decimal tick, decimal point) =>
        InstrumentRiskSpecification.FromInstrumentInfo(
            new InstrumentInfo(sym, TickSize: tick, TickValue: tick * point / 1m, PointValue: point, Decimals: 2),
            minQuantity: 1, maxQuantity: 50, quantityStep: 1);

    // ── realistic MES cost scenarios (round-trip, quantity 1) ────────────────────────────────────
    // Commission is modelled via Commission.PerUnit (charged on both legs); exchange/regulatory via Fees.PerOrder.
    private static ExecutionCostConfiguration CostOptimistic(decimal tick) => ExecutionCostConfiguration.Create(
        enabled: true,
        slippage: SlippageConfiguration.None(),
        spread: SpreadConfiguration.FromTicks(0.5m, tick),                 // half a tick crossed
        commission: CommissionConfiguration.Create(perOrder: 0m, perUnit: 0.37m),   // ~$0.74 RT broker
        fees: FeesConfiguration.Create(perOrder: 0.25m));                  // ~$0.50 RT exch/reg

    private static ExecutionCostConfiguration CostBase(decimal tick) => ExecutionCostConfiguration.Create(
        enabled: true,
        slippage: SlippageConfiguration.FromTicks(0.25m, tick),           // 0.25 tick / leg
        spread: SpreadConfiguration.FromTicks(1m, tick),                  // MES is 1 tick wide
        commission: CommissionConfiguration.Create(perOrder: 0m, perUnit: 0.62m),   // ~$1.24 RT broker
        fees: FeesConfiguration.Create(perOrder: 0.37m));                 // ~$0.74 RT exch/reg

    private static ExecutionCostConfiguration CostPessimistic(decimal tick) => ExecutionCostConfiguration.Create(
        enabled: true,
        slippage: SlippageConfiguration.FromTicks(0.5m, tick),            // 0.5 tick / leg
        spread: SpreadConfiguration.FromTicks(1m, tick),
        commission: CommissionConfiguration.Create(perOrder: 0m, perUnit: 0.62m),
        fees: FeesConfiguration.Create(perOrder: 0.37m));

    private sealed record Trade(int Bar, DateTime Ts, double Rr, double StopPts, decimal PointValue,
        double GrossUsd, double NetOpt, double NetBase, double NetPess, string Exit)
    {
        public double RiskUsd => StopPts * (double)PointValue;   // quantity 1
        public double GrossR => GrossUsd / RiskUsd;
        public double NetBaseR => NetBase / RiskUsd;
        public double NetOptR => NetOpt / RiskUsd;
        public double NetPessR => NetPess / RiskUsd;
    }

    [Fact]
    public void Validate_MinRiskReward_Threshold_With_Costs_And_OOS()
    {
        HistoricalSeries mes;
        try { mes = _yahoo.Require(); }
        catch (Exception ex) when (ex is YahooProviderException or HttpRequestException or TaskCanceledException)
        {
            Assert.Skip($"Yahoo provider unavailable: {ex.GetType().Name}: {ex.Message}");
            return;
        }

        var outLines = new List<string>();
        void W(string s) { _output.WriteLine(s); outLines.Add(s); }

        W($"=== CONFIG ===  warmup={WarmupBars} horizon={HorizonBars} qty=1 MinRiskReward=OFF(null)");
        W("Cost scenarios (round-trip, qty 1):");
        W("  optimistic  : spread 0.5 tick, slippage 0,          comm ~$0.74, fees ~$0.50");
        W("  base        : spread 1 tick,  slippage 0.25 tick/leg, comm ~$1.24, fees ~$0.74");
        W("  pessimistic : spread 1 tick,  slippage 0.5 tick/leg,  comm ~$1.24, fees ~$0.74");

        List<Trade> mesTrades = BuildTrades(mes, "MES", 0.25m, 5m, W);
        W("");
        W("################  MES  M5  full 59d  ################");
        Report(mesTrades, W);

        // ── temporal purged split (pseudo-OOS on the same window) ────────────────────────────────
        if (mesTrades.Count > 0)
        {
            int minBar = mesTrades.Min(t => t.Bar), maxBar = mesTrades.Max(t => t.Bar);
            int split = minBar + (int)((maxBar - minBar) * 0.60);
            const int purge = 100;
            var train = mesTrades.Where(t => t.Bar < split - purge).ToList();
            var oos = mesTrades.Where(t => t.Bar >= split).ToList();
            W("");
            W($"################  MES temporal split  (train bar<{split - purge}, purge {purge}, oos bar>={split})  ################");
            W($"-- TRAIN n={train.Count} --");
            Report(train, W);
            W($"-- OOS n={oos.Count} --");
            Report(oos, W);
        }

        // ── ES=F as a second instrument (best effort) ────────────────────────────────────────────
        try
        {
            var src = new YahooHistoricalBarSource();
            DateTime to = DateTime.UtcNow;
            DateTime from = to.AddDays(-YahooHistoricalBarSource.DefaultMaxChunkSpanDays);
            HistoricalSeries es = src.Load("ES", "M5", from, to, CancellationToken.None);
            List<Trade> esTrades = BuildTrades(es, "ES", 0.25m, 50m, W);
            W("");
            W("################  ES  M5  full 59d  (cross-instrument OOS; costs scaled by PointValue)  ################");
            Report(esTrades, W);
        }
        catch (Exception ex) when (ex is YahooProviderException or HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            W("");
            W($"ES cross-instrument run skipped (Yahoo unavailable): {ex.GetType().Name}: {ex.Message}");
        }

        WriteReport(outLines);
    }

    private List<Trade> BuildTrades(HistoricalSeries series, string sym, decimal tick, decimal point, Action<string> W)
    {
        var scenario = BacktestScenario.Create(
            series, new BacktestWindow($"{sym}-THRESH", series.FirstTimestamp, series.LastTimestamp.AddMinutes(1)),
            InitialCapital, Spec(sym, tick, point), Policy());

        MeasurementConfiguration meas = MeasurementConfiguration.Create(10, new[] { 0.001, 0.002 });
        ExecutionConfiguration exec = ExecutionConfiguration.Create(HorizonBars);
        PnLConfiguration pnl = PnLConfiguration.Create(
            InstrumentPnLSpecification.Create(sym, priceUnitValue: point, currency: "USD"),
            quantity: 1, startingCapital: InitialCapital);

        BacktestFullResult r = new BacktestEngine().RunFullBacktest(scenario, WarmupBars, meas, exec, pnl);

        var byBar = new Dictionary<int, BacktestSignalResult>();
        foreach (BacktestSignalResult b in r.SignalResult.Bars) byBar[b.BarIndex] = b;

        ExecutionCostConfiguration cOpt = CostOptimistic(tick), cBase = CostBase(tick), cPess = CostPessimistic(tick);

        var trades = new List<Trade>();
        var positions = r.ExecutionResult.Positions;
        var pnls = r.PnLResult.PositionPnLResults;
        for (int i = 0; i < positions.Count; i++)
        {
            SimulatedPosition pos = positions[i];
            PositionPnLResult pp = pnls[i];
            if (pos.Status != PositionStatus.Closed || pp.GrossPnL is not decimal gross) continue;
            if (!byBar.TryGetValue(pp.PositionId, out BacktestSignalResult? sb)) continue;
            if (sb.Decision is not { Winner: MarketState.MeanReverting }) continue;
            if (sb.TradePlan is not { } plan) continue;
            if (plan.RiskRewardRatio is not double rr) continue;
            if (plan.EntryPrice is not decimal ep || plan.StopLoss is not decimal sl) continue;
            double stopPts = (double)Math.Abs(ep - sl);
            if (stopPts <= 0) continue;

            double net(ExecutionCostConfiguration c) =>
                (double)(PositionCostCalculator.Calculate(pos, pp, pnl, c).NetPnL ?? gross);

            trades.Add(new Trade(pp.PositionId, sb.Timestamp, rr, stopPts, point,
                (double)gross, net(cOpt), net(cBase), net(cPess), pos.ExitReason?.ToString() ?? "?"));
        }
        W($"[{sym}] closed MeanReverting trades with computable R:R = {trades.Count} " +
          $"(bars {(trades.Count > 0 ? trades.Min(t => t.Bar) : -1)}..{(trades.Count > 0 ? trades.Max(t => t.Bar) : -1)})");
        return trades;
    }

    private void Report(IReadOnlyList<Trade> ts, Action<string> W)
    {
        if (ts.Count == 0) { W("  (no trades)"); return; }

        // coarse tranches: full table
        W("  -- coarse R:R tranches --");
        W("  tranche            n   winRate  avgWin$  avgLoss$   exp$/tr   exp(base)$  expR(gross)  expR(base)");
        foreach (var (lbl, lo, hi) in new[] { ("R:R<0.5", 0.0, 0.5), ("0.5<=R:R<1.0", 0.5, 1.0), ("R:R>=1.0", 1.0, 99.0) })
        {
            var b = ts.Where(t => t.Rr >= lo && t.Rr < hi).ToList();
            if (b.Count == 0) { W($"  {lbl,-16} n=0"); continue; }
            var wins = b.Where(t => t.GrossUsd > 0).ToList();
            var los = b.Where(t => t.GrossUsd <= 0).ToList();
            W($"  {lbl,-16} {b.Count,4}  {wins.Count / (double)b.Count,7:0.000}  {(wins.Count > 0 ? wins.Average(t => t.GrossUsd) : 0),7:0.00}  " +
              $"{(los.Count > 0 ? los.Average(t => t.GrossUsd) : 0),8:0.00}  {b.Average(t => t.GrossUsd),8:0.00}  {b.Average(t => t.NetBase),9:0.00}   " +
              $"{b.Average(t => t.GrossR),9:0.000}   {b.Average(t => t.NetBaseR),9:0.000}");
        }

        // fine 0.1 grid with CI
        W("  -- fine R:R grid (0.1 wide) : mean net$ +/- 95%CI, and mean net-R --");
        W("  band          n    gross$/tr   optimistic$   base$ (95%CI)              pess$      base R/tr (95%CI)     flag");
        for (double lo = 0.0; lo < 1.5 - 1e-9; lo += 0.1)
        {
            double hi = lo + 0.1;
            var b = ts.Where(t => t.Rr >= lo && t.Rr < hi).ToList();
            if (b.Count == 0) { W($"  [{lo:0.0},{hi:0.0})     0"); continue; }
            (double m, double ci) g = MeanCi(b.Select(t => t.GrossUsd));
            (double m, double ci) o = MeanCi(b.Select(t => t.NetOpt));
            (double m, double ci) ba = MeanCi(b.Select(t => t.NetBase));
            (double m, double ci) pe = MeanCi(b.Select(t => t.NetPess));
            (double m, double ci) br = MeanCi(b.Select(t => t.NetBaseR));
            string flag = b.Count < 30 ? "n<30 (ignore)" : (ba.m - ba.ci > 0 ? "NET+ robust" : ba.m + ba.ci < 0 ? "NET- robust" : "straddles 0");
            W($"  [{lo:0.0},{hi:0.0}) {b.Count,5}  {g.m,9:0.00}   {o.m,9:0.00}   {ba.m,8:0.00} +/-{ba.ci,6:0.00}   {pe.m,8:0.00}   {br.m,7:0.000} +/-{br.ci,6:0.000}   {flag}");
        }

        // cumulative "R:R >= x" net expectancy (base cost), to locate the crossover
        W("  -- cumulative: all trades with R:R >= x  (base cost) --");
        W("  x     n     exp$/tr (95%CI)        expR/tr (95%CI)");
        for (double x = 0.0; x <= 1.4 + 1e-9; x += 0.1)
        {
            var b = ts.Where(t => t.Rr >= x).ToList();
            if (b.Count == 0) { W($"  {x:0.0}   0"); continue; }
            (double m, double ci) d = MeanCi(b.Select(t => t.NetBase));
            (double m, double ci) rr = MeanCi(b.Select(t => t.NetBaseR));
            W($"  {x:0.0} {b.Count,6}  {d.m,8:0.00} +/-{d.ci,6:0.00}    {rr.m,7:0.000} +/-{rr.ci,6:0.000}");
        }

        // exit-reason split (gross vs base)
        W("  -- exit reason --");
        foreach (var grp in ts.GroupBy(t => t.Exit).OrderByDescending(x => x.Count()))
            W($"     {grp.Key,-12} n={grp.Count(),5}  gross$/tr={grp.Average(t => t.GrossUsd),7:0.00}  base$/tr={grp.Average(t => t.NetBase),7:0.00}");
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
        File.WriteAllText(Path.Combine(od, "threshold_validation.txt"), string.Join("\n", lines) + "\n");
    }
}
