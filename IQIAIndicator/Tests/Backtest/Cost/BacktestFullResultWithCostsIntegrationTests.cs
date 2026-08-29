using IQIAIndicator.Backtest;
using IQIAIndicator.Backtest.Cost;
using IQIAIndicator.Backtest.Execution;
using IQIAIndicator.Backtest.Measurement;
using IQIAIndicator.Backtest.Pnl;
using IQIAIndicator.Core;
using IQIAIndicator.Core.MarketData;
using IQIAIndicator.Engine.Risk;
using IQIAIndicator.Tests.BacktestTests;
using Xunit;

namespace IQIAIndicator.Tests.BacktestTests.Cost;

/// <summary>
/// Sprint 15.25 (Lot 14.7, brief §11/§12/§14). Integration-level proof, through
/// <see cref="BacktestEngine.RunFullBacktestWithCosts"/>, of determinism, run isolation, and zero-cost
/// equivalence to the untouched <see cref="BacktestEngine.RunFullBacktest"/> (Lot 14.6).
/// </summary>
public sealed class BacktestFullResultWithCostsIntegrationTests
{
    private static InstrumentRiskSpecification Spec() => InstrumentRiskSpecification.FromInstrumentInfo(
        new InstrumentInfo("ES", TickSize: 0.25m, TickValue: 12.5m, PointValue: 50m, Decimals: 2),
        minQuantity: 1, maxQuantity: 50, quantityStep: 1);

    private static RiskPolicy Policy() => new(0.02m, null, null, null, null, null, null, null, null, null);

    private static BacktestScenario ScenarioFor(HistoricalSeries series) =>
        BacktestScenario.Create(
            series,
            new BacktestWindow("W", series.FirstTimestamp, series.LastTimestamp.AddMinutes(1)),
            50_000m, Spec(), Policy());

    private static MeasurementConfiguration Measurement() => MeasurementConfiguration.Create(10, new[] { 0.001 });

    private static ExecutionConfiguration Exec() => ExecutionConfiguration.Create(10);

    private static PnLConfiguration Pnl() =>
        PnLConfiguration.Create(InstrumentPnLSpecification.Create("ES", 50m, "USD"), quantity: 1, startingCapital: 100000m);

    private static ExecutionCostConfiguration NonZeroCosts() => ExecutionCostConfiguration.Create(
        enabled: true,
        slippage: SlippageConfiguration.Fixed(0.25m),
        spread: SpreadConfiguration.Fixed(0.5m),
        commission: CommissionConfiguration.Create(2.00m, 0.50m),
        fees: FeesConfiguration.Create(0.10m));

    // ── §11: determinism ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SameInputs_RunTwice_ProduceTheSameDeterministicHashes()
    {
        BacktestScenario scenario = ScenarioFor(BacktestTestSeriesBuilder.MeanRevertingOu(200, seed: 4UL));

        BacktestFullResultWithCosts first = new BacktestEngine().RunFullBacktestWithCosts(scenario, 128, Measurement(), Exec(), Pnl(), NonZeroCosts());
        BacktestFullResultWithCosts second = new BacktestEngine().RunFullBacktestWithCosts(scenario, 128, Measurement(), Exec(), Pnl(), NonZeroCosts());

        Assert.Equal(first.FullResult.PnLResult.DeterministicHash, second.FullResult.PnLResult.DeterministicHash);
        Assert.Equal(first.CostResult.DeterministicHash, second.CostResult.DeterministicHash);
    }

    // ── §11: run isolation ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RunA_RunB_RunA_ProduceIdenticalResultsForTheRepeatedRunA()
    {
        BacktestScenario scenarioA = ScenarioFor(BacktestTestSeriesBuilder.MeanRevertingOu(180, seed: 42UL));
        BacktestScenario scenarioB = ScenarioFor(BacktestTestSeriesBuilder.WhiteNoise(140, seed: 99UL));
        var engine = new BacktestEngine();

        BacktestFullResultWithCosts runA1 = engine.RunFullBacktestWithCosts(scenarioA, 128, Measurement(), Exec(), Pnl(), NonZeroCosts());
        engine.RunFullBacktestWithCosts(scenarioB, 128, Measurement(), Exec(), Pnl(), NonZeroCosts());
        BacktestFullResultWithCosts runA2 = engine.RunFullBacktestWithCosts(scenarioA, 128, Measurement(), Exec(), Pnl(), NonZeroCosts());

        Assert.Equal(runA1.CostResult.DeterministicHash, runA2.CostResult.DeterministicHash);
    }

    // ── §8/§9/§14: zero-cost equivalence to the untouched Lot 14.6 RunFullBacktest ──────────────────

    [Fact]
    public void Disabled_FullResultMatchesPlainRunFullBacktest_AndNetEquivalentToGross()
    {
        BacktestScenario scenario = ScenarioFor(BacktestTestSeriesBuilder.MeanRevertingOu(200, seed: 7UL));
        var engine = new BacktestEngine();

        BacktestFullResult plain = engine.RunFullBacktest(scenario, 128, Measurement(), Exec(), Pnl());
        BacktestFullResultWithCosts withCosts = engine.RunFullBacktestWithCosts(scenario, 128, Measurement(), Exec(), Pnl(), ExecutionCostConfiguration.Disabled());

        // The Gross/Lot 14.6 side is untouched - same hash as calling RunFullBacktest directly.
        Assert.Equal(plain.PnLResult.DeterministicHash, withCosts.FullResult.PnLResult.DeterministicHash);

        // The Net side is numerically equivalent to the Gross side.
        Assert.Equal(plain.PnLResult.FinalGrossPnL, withCosts.CostResult.FinalNetPnL);
        Assert.Equal(plain.PnLResult.MaximumDrawdown, withCosts.CostResult.MaximumNetDrawdown);
        Assert.Equal(plain.PnLResult.FinalEquity, withCosts.CostResult.FinalNetEquity);
        Assert.Equal(0m, withCosts.CostResult.TotalCost);
    }

    [Fact]
    public void NonZeroCosts_NeverChangeTheGrossSide()
    {
        BacktestScenario scenario = ScenarioFor(BacktestTestSeriesBuilder.MeanRevertingOu(200, seed: 7UL));
        var engine = new BacktestEngine();

        BacktestFullResult plain = engine.RunFullBacktest(scenario, 128, Measurement(), Exec(), Pnl());
        BacktestFullResultWithCosts withCosts = engine.RunFullBacktestWithCosts(scenario, 128, Measurement(), Exec(), Pnl(), NonZeroCosts());

        Assert.Equal(plain.PnLResult.DeterministicHash, withCosts.FullResult.PnLResult.DeterministicHash);
        Assert.Equal(plain.PnLResult.FinalGrossPnL, withCosts.FullResult.PnLResult.FinalGrossPnL);
    }
}
