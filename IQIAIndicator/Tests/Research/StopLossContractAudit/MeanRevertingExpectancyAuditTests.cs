using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
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
/// READ-ONLY. 2026-08-30 audit "MinRiskReward configuration &amp; MeanReverting expectancy". Runs the
/// unmodified <see cref="BacktestEngine.RunFullBacktest"/> (Signal -&gt; Measurement -&gt; Execution -&gt; P&amp;L)
/// over the canonical Yahoo MES M5 window with the SAME config the calibration/Yahoo integration suites
/// use (warmup 128, HorizonBars 10, quantity 1) and with the production-default MinRiskReward (OFF).
/// Buckets the executed MeanReverting trades by R:R tranche and reports win rate / avg win / avg loss /
/// expectancy in $ from the real backtest P&amp;L, plus the break-even win rate 1/(1+R:R).
/// </summary>
public sealed class MeanRevertingExpectancyAuditTests
{
    private readonly ITestOutputHelper _output;
    private readonly YahooSessionDataset _yahoo;

    public MeanRevertingExpectancyAuditTests(ITestOutputHelper output, YahooSessionDataset yahoo)
    {
        _output = output;
        _yahoo = yahoo;
    }

    private const decimal PointValue = 5m;       // MES $/point
    private const decimal InitialCapital = 25_000m;
    private const int WarmupBars = 128;
    private const int HorizonBars = 10;

    private static InstrumentRiskSpecification Spec() => InstrumentRiskSpecification.FromInstrumentInfo(
        new InstrumentInfo("MES", TickSize: 0.25m, TickValue: 1.25m, PointValue: PointValue, Decimals: 2),
        minQuantity: 1, maxQuantity: 50, quantityStep: 1);

    // Production-default MinRiskReward: OFF (null). This mirrors IQIAIndicator.cs (RiskPolicyMinRiskReward
    // default 0.0 -> null) and every backtest scenario in the repo.
    private static RiskPolicy Policy() => new(0.01m, null, null, null, null, null, null, null, null, null);

    [Fact]
    public void Measure_MeanReverting_Executed_Trades_And_Expectancy_By_RR()
    {
        HistoricalSeries series;
        try { series = _yahoo.Require(); }
        catch (Exception ex) when (ex is YahooProviderException or HttpRequestException or TaskCanceledException)
        {
            Assert.Skip($"Yahoo provider unavailable (not a code failure): {ex.GetType().Name}: {ex.Message}");
            return;
        }

        var scenario = BacktestScenario.Create(
            series, new BacktestWindow("MR-EXPECTANCY-AUDIT", series.FirstTimestamp, series.LastTimestamp.AddMinutes(1)),
            InitialCapital, Spec(), Policy());

        MeasurementConfiguration meas = MeasurementConfiguration.Create(10, new[] { 0.001, 0.002 });
        ExecutionConfiguration exec = ExecutionConfiguration.Create(HorizonBars);
        PnLConfiguration pnl = PnLConfiguration.Create(
            InstrumentPnLSpecification.Create("MES", priceUnitValue: PointValue, currency: "USD"),
            quantity: 1, startingCapital: InitialCapital);

        BacktestFullResult result = new BacktestEngine().RunFullBacktest(scenario, WarmupBars, meas, exec, pnl);

        // Index every signal bar by BarIndex for the join back to each position.
        var byBar = new Dictionary<int, BacktestSignalResult>();
        int planReady = 0, planSignalOnly = 0, planRejected = 0, planBlocked = 0, planNoTrade = 0, mrDirectional = 0;
        foreach (BacktestSignalResult b in result.SignalResult.Bars)
        {
            byBar[b.BarIndex] = b;
            if (b.Status != BacktestSignalStatus.Ready || b.Decision is not { } dec || b.TradePlan is not { } p) continue;
            bool mr = dec.Winner == MarketState.MeanReverting;
            bool dir = p.Direction is DirectionCandidate.BUY_CANDIDATE or DirectionCandidate.SELL_CANDIDATE;
            if (!mr || !dir) continue;
            mrDirectional++;
            switch (p.Status)
            {
                case TradePlanStatus.PLAN_READY: planReady++; break;
                case TradePlanStatus.SIGNAL_ONLY: planSignalOnly++; break;
                case TradePlanStatus.PLAN_REJECTED: planRejected++; break;
                case TradePlanStatus.PLAN_BLOCKED: planBlocked++; break;
                case TradePlanStatus.NO_TRADE: planNoTrade++; break;
            }
        }

        _output.WriteLine("=== CONFIG ===");
        _output.WriteLine($"Symbol={series.Symbol} TF={series.TimeFrame} Bars={series.Count} " +
            $"Range={series.FirstTimestamp:O}..{series.LastTimestamp:O}");
        _output.WriteLine($"warmup={WarmupBars} HorizonBars={HorizonBars} quantity=1 MinRiskReward=OFF(null) " +
            $"MaxRiskPerTradePercent=0.01 costs=off");
        _output.WriteLine("");
        _output.WriteLine("=== MeanReverting directional TradePlan status (gate OFF) ===");
        _output.WriteLine($"total MR directional = {mrDirectional} : PLAN_READY={planReady} SIGNAL_ONLY={planSignalOnly} " +
            $"PLAN_REJECTED={planRejected} PLAN_BLOCKED={planBlocked} NO_TRADE={planNoTrade}");

        // Executed MeanReverting positions with real P&L.
        var rows = new List<Row>();
        var statusMix = new Dictionary<PositionStatus, int>();
        foreach (PositionPnLResult pp in result.PnLResult.PositionPnLResults)
        {
            if (!byBar.TryGetValue(pp.PositionId, out BacktestSignalResult? sb)) continue;
            if (sb.Decision is not { Winner: MarketState.MeanReverting }) continue;
            if (pp.Direction is not (DirectionCandidate.BUY_CANDIDATE or DirectionCandidate.SELL_CANDIDATE)) continue;

            statusMix[pp.Status] = statusMix.GetValueOrDefault(pp.Status) + 1;
            if (pp.Status != PositionStatus.Closed || pp.GrossPnL is not decimal g) continue;

            double? rr = sb.TradePlan?.RiskRewardRatio;
            rows.Add(new Row(pp.PositionId, rr, (double)g, pp.ExitReason, pp.HoldingBars ?? -1,
                pp.GrossPriceMove is decimal m ? (double)m : 0.0));
        }

        _output.WriteLine("");
        _output.WriteLine("=== MeanReverting position status (execution stage) ===");
        foreach ((PositionStatus st, int c) in statusMix.OrderByDescending(k => k.Value))
            _output.WriteLine($"  {st} = {c}");
        int closed = rows.Count;
        _output.WriteLine($"  => CLOSED MeanReverting trades with P&L = {closed}");

        {
            string? d = AppContext.BaseDirectory;
            while (d is not null && !File.Exists(Path.Combine(d, "IQIAIndicator.Tests.csproj")))
                d = Directory.GetParent(d)?.FullName;
            if (d is not null)
            {
                string od = Path.Combine(d, "Research", "StopLossContractAudit", "Output");
                Directory.CreateDirectory(od);
                var sm = new StringBuilder();
                sm.AppendLine($"MR directional total={mrDirectional} PLAN_READY={planReady} SIGNAL_ONLY={planSignalOnly} PLAN_REJECTED={planRejected} PLAN_BLOCKED={planBlocked} NO_TRADE={planNoTrade}");
                foreach ((PositionStatus st, int c) in statusMix.OrderByDescending(k => k.Value))
                    sm.AppendLine($"PositionStatus {st}={c}");
                sm.AppendLine($"CLOSED MR trades={closed}");
                File.WriteAllText(Path.Combine(od, "summary.txt"), sm.ToString());
            }
        }

        if (closed == 0) { _output.WriteLine("No closed MeanReverting trade - nothing to summarise."); return; }

        // Exit reason mix.
        _output.WriteLine("");
        _output.WriteLine("=== Exit reason (CLOSED MeanReverting) ===");
        foreach (var grp in rows.GroupBy(r => r.Exit).OrderByDescending(x => x.Count()))
            _output.WriteLine($"  {grp.Key} = {grp.Count()} ({100.0 * grp.Count() / closed:0.0}%)  " +
                $"winRate={grp.Count(x => x.Pnl > 0) / (double)grp.Count():0.000}  " +
                $"expectancy=${grp.Average(x => x.Pnl):0.00}/trade");

        // Overall.
        Bucket("ALL CLOSED MeanReverting", rows);

        // R:R tranches.
        var withRr = rows.Where(r => r.Rr is not null).ToList();
        _output.WriteLine("");
        _output.WriteLine($"=== R:R tranches ({withRr.Count}/{closed} closed trades have a computable R:R) ===");
        Bucket("R:R < 0.5", withRr.Where(r => r.Rr < 0.5).ToList());
        Bucket("0.5 <= R:R < 1.0", withRr.Where(r => r.Rr >= 0.5 && r.Rr < 1.0).ToList());
        Bucket("R:R >= 1.0", withRr.Where(r => r.Rr >= 1.0).ToList());

        // Finer tranches for context.
        _output.WriteLine("");
        _output.WriteLine("=== finer R:R deciles ===");
        foreach (var (lo, hi) in new[] { (0.0, 0.25), (0.25, 0.5), (0.5, 0.75), (0.75, 1.0), (1.0, 1.5), (1.5, 3.0) })
            Bucket($"R:R [{lo:0.00},{hi:0.00})", withRr.Where(r => r.Rr >= lo && r.Rr < hi).ToList());

        WriteCsv(series, rows);
    }

    private sealed record Row(int Bar, double? Rr, double Pnl, ExitReason? Exit, int HoldingBars, double GrossPriceMove);

    private void Bucket(string label, IReadOnlyList<Row> rs)
    {
        if (rs.Count == 0) { _output.WriteLine($"[{label}] n=0"); return; }
        var wins = rs.Where(r => r.Pnl > 0).ToList();
        var losses = rs.Where(r => r.Pnl <= 0).ToList();
        double wr = wins.Count / (double)rs.Count;
        double avgWin = wins.Count > 0 ? wins.Average(r => r.Pnl) : 0.0;
        double avgLoss = losses.Count > 0 ? losses.Average(r => r.Pnl) : 0.0;
        double exp = rs.Average(r => r.Pnl);
        double meanRr = rs.Where(r => r.Rr is not null).Select(r => r.Rr!.Value).DefaultIfEmpty(double.NaN).Average();
        double breakEvenWr = double.IsNaN(meanRr) ? double.NaN : 1.0 / (1.0 + meanRr);
        double totalPnl = rs.Sum(r => r.Pnl);
        _output.WriteLine(
            $"[{label}] n={rs.Count}  winRate={wr:0.000}  avgWin=${avgWin:0.00}  avgLoss=${avgLoss:0.00}  " +
            $"expectancy=${exp:0.00}/trade  totalPnL=${totalPnl:0.0}  " +
            $"meanR:R={meanRr:0.000}  breakEvenWinRate(1/(1+R:R))={breakEvenWr:0.000}  " +
            $"gap(actual-breakeven)={(double.IsNaN(breakEvenWr) ? double.NaN : wr - breakEvenWr):0.000}  " +
            $"avgHold={rs.Average(r => r.HoldingBars):0.0}bars");
    }

    private void WriteCsv(HistoricalSeries series, IReadOnlyList<Row> rows)
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "IQIAIndicator.Tests.csproj")))
            dir = Directory.GetParent(dir)?.FullName;
        if (dir is null) return;
        string outDir = Path.Combine(dir, "Research", "StopLossContractAudit", "Output");
        Directory.CreateDirectory(outDir);
        var sb = new StringBuilder("bar,rr,pnlUsd,exitReason,holdingBars,grossPriceMove\n");
        foreach (Row r in rows)
            sb.AppendLine(string.Join(",", r.Bar,
                r.Rr?.ToString("G17", CultureInfo.InvariantCulture) ?? "",
                r.Pnl.ToString("G17", CultureInfo.InvariantCulture),
                r.Exit?.ToString() ?? "", r.HoldingBars,
                r.GrossPriceMove.ToString("G17", CultureInfo.InvariantCulture)));
        File.WriteAllText(Path.Combine(outDir, "mr_trades.csv"), sb.ToString());
        _output.WriteLine($"\nWrote {rows.Count} MeanReverting closed trades to {Path.Combine(outDir, "mr_trades.csv")}");
    }
}
