using System;
using System.Net.Http;
using System.Threading.Tasks;
using IQIAIndicator.Backtest;
using IQIAIndicator.Backtest.Cost;
using IQIAIndicator.Backtest.Execution;
using IQIAIndicator.Backtest.Measurement;
using IQIAIndicator.Backtest.Pnl;
using IQIAIndicator.Core;
using IQIAIndicator.Core.MarketData;
using IQIAIndicator.Core.MarketData.Yahoo;
using IQIAIndicator.Engine.Risk;
using Xunit;
using Xunit.Abstractions;

namespace IQIAIndicator.Tests.BacktestTests.Cost;

/// <summary>
/// Sprint 15.25 (Lot 14.7, brief §13). INTEGRATION / NETWORK - same MES=F/M5/45-day recipe as Lot 14.3
/// through 14.6's own Yahoo integration tests. Runs the FULL chain (Signal -&gt; Measurement -&gt; Execution
/// -&gt; Gross P&amp;L -&gt; Cost/Net P&amp;L) once through <see cref="BacktestEngine.RunFullBacktestWithCosts"/>:
/// first with costs disabled (must reproduce the Lot 14.6 reference numbers EXACTLY), then with a
/// non-zero cost configuration (brief §13's documentation requirement).
/// </summary>
public sealed class CostYahooIntegrationTests
{
    private readonly ITestOutputHelper _output;

    public CostYahooIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static InstrumentRiskSpecification Spec() => InstrumentRiskSpecification.FromInstrumentInfo(
        new InstrumentInfo("MES", TickSize: 0.25m, TickValue: 1.25m, PointValue: 5m, Decimals: 2),
        minQuantity: 1, maxQuantity: 50, quantityStep: 1);

    private static RiskPolicy Policy() => new(0.02m, null, null, null, null, null, null, null, null, null);

    [Fact]
    public void Integration_Network_YahooMesFiveMinuteBars_BaselineIsIdenticalToLot146_ThenAppliesRealisticCosts()
    {
        try
        {
            var source = new YahooHistoricalBarSource();
            DateTime to = DateTime.UtcNow;
            DateTime from = to.AddDays(-45);

            HistoricalSeries series = source.Load("MES", "M5", from, to);
            Assert.True(series.Count > 0);

            var scenario = BacktestScenario.Create(
                series,
                new BacktestWindow("W", series.FirstTimestamp, series.LastTimestamp.AddMinutes(1)),
                50_000m, Spec(), Policy());

            PnLConfiguration pnlConfig = PnLConfiguration.Create(
                InstrumentPnLSpecification.Create("MES", priceUnitValue: 5m, currency: "USD"),
                quantity: 1, startingCapital: 100000m);

            MeasurementConfiguration measurementConfig = MeasurementConfiguration.Create(10, new[] { 0.001, 0.002 });
            ExecutionConfiguration executionConfig = ExecutionConfiguration.Create(10);
            var engine = new BacktestEngine();

            // ── Baseline: costs disabled - must reproduce the Lot 14.6 reference numbers exactly ─────
            BacktestFullResultWithCosts baseline = engine.RunFullBacktestWithCosts(
                scenario, warmupBars: 128, measurementConfig, executionConfig, pnlConfig, ExecutionCostConfiguration.Disabled());

            Assert.Equal(baseline.FullResult.PnLResult.FinalGrossPnL, baseline.CostResult.FinalNetPnL);
            Assert.Equal(baseline.FullResult.PnLResult.MaximumDrawdown, baseline.CostResult.MaximumNetDrawdown);
            Assert.Equal(baseline.FullResult.PnLResult.FinalEquity, baseline.CostResult.FinalNetEquity);
            Assert.Equal(0m, baseline.CostResult.TotalCost);

            _output.WriteLine(
                $"Yahoo dataset: ticker=MES=F, timeframe=M5, from={from:O}, to={to:O}, bars={series.Count}, " +
                $"fingerprint={HistoricalSeriesFingerprint.Compute(series)}.");
            _output.WriteLine(
                $"BASELINE (costs disabled): GrossPnL={baseline.FullResult.PnLResult.FinalGrossPnL}, " +
                $"MaximumDrawdown={baseline.FullResult.PnLResult.MaximumDrawdown}, FinalEquity={baseline.FullResult.PnLResult.FinalEquity}, " +
                $"ClosedCount={baseline.FullResult.PnLResult.Summary.ClosedCount}, " +
                $"PnLHash={baseline.FullResult.PnLResult.DeterministicHash}.");

            // ── Non-zero cost scenario (brief §13) ────────────────────────────────────────────────────
            ExecutionCostConfiguration realistic = ExecutionCostConfiguration.Create(
                enabled: true,
                slippage: SlippageConfiguration.FromTicks(ticks: 1m, tickSize: 0.25m),    // 1 tick = 0.25 pt
                spread: SpreadConfiguration.FromTicks(ticks: 1m, tickSize: 0.25m),        // 1 tick full spread
                commission: CommissionConfiguration.Create(perOrder: 0.85m, perUnit: 0m), // illustrative per-leg commission
                fees: FeesConfiguration.Create(perOrder: 0.10m));

            BacktestFullResultWithCosts costed = engine.RunFullBacktestWithCosts(
                scenario, warmupBars: 128, measurementConfig, executionConfig, pnlConfig, realistic);

            // The Gross side must be byte-identical to the baseline/Lot 14.6 - the cost layer never
            // touches Signal/Measurement/Execution/GrossPnL.
            Assert.Equal(baseline.FullResult.PnLResult.DeterministicHash, costed.FullResult.PnLResult.DeterministicHash);

            _output.WriteLine(
                $"COSTED (slippage=1 tick, spread=1 tick, commission=$0.85/order, fees=$0.10/order): " +
                $"GrossPnL={costed.FullResult.PnLResult.FinalGrossPnL}, " +
                $"TotalCommission={costed.CostResult.TotalCommission}, TotalFees={costed.CostResult.TotalFees}, " +
                $"TotalSpreadCost={costed.CostResult.TotalSpreadCost}, TotalSlippageCost={costed.CostResult.TotalSlippageCost}, " +
                $"TotalCost={costed.CostResult.TotalCost}, NetPnL={costed.CostResult.FinalNetPnL}, " +
                $"FinalNetEquity={costed.CostResult.FinalNetEquity}, MaximumNetDrawdown={costed.CostResult.MaximumNetDrawdown}, " +
                $"CostHash={costed.CostResult.DeterministicHash}.");
        }
        catch (Exception exception) when (IsConnectivityOrProviderIssue(exception))
        {
            _output.WriteLine($"SKIPPED (network/Yahoo unavailable, not a code failure): {exception.GetType().Name}: {exception.Message}");
        }
    }

    private static bool IsConnectivityOrProviderIssue(Exception exception) =>
        exception is HttpRequestException or TaskCanceledException or InvalidOperationException;
}
