using System.Linq;
using IQIAIndicator.Backtest;
using IQIAIndicator.Backtest.Cost;
using IQIAIndicator.Backtest.Execution;
using IQIAIndicator.Backtest.Measurement;
using IQIAIndicator.Backtest.Pnl;
using IQIAIndicator.Backtest.Risk;
using IQIAIndicator.Core;
using IQIAIndicator.Core.MarketData;
using IQIAIndicator.Engine.Risk;
using IQIAIndicator.Tests.BacktestTests;
using Xunit;

namespace IQIAIndicator.Tests.BacktestTests.Risk;

/// <summary>
/// Sprint 15.25 (Lot 14.8, brief §17/§18/§20). Integration-level proof, through
/// <see cref="BacktestEngine.RunFullBacktestWithRisk"/>, of determinism, run isolation, zero-regression
/// against <see cref="BacktestEngine.RunFullBacktestWithCosts"/>, and the scientific invariants (brief §20)
/// holding across a real signal-pipeline run.
/// </summary>
public sealed class BacktestFullResultWithRiskIntegrationTests
{
    private static InstrumentRiskSpecification Spec() => InstrumentRiskSpecification.FromInstrumentInfo(
        new InstrumentInfo("ES", TickSize: 0.25m, TickValue: 12.5m, PointValue: 50m, Decimals: 2),
        minQuantity: 1, maxQuantity: 50, quantityStep: 1);

    private static RiskPolicy Policy() => new(0.02m, null, null, null, null, null, null, null, null, null);

    private static BacktestScenario ScenarioFor(HistoricalSeries series) =>
        BacktestScenario.Create(series, new BacktestWindow("W", series.FirstTimestamp, series.LastTimestamp.AddMinutes(1)), 50_000m, Spec(), Policy());

    private static MeasurementConfiguration Measurement() => MeasurementConfiguration.Create(10, new[] { 0.001 });

    private static ExecutionConfiguration Exec() => ExecutionConfiguration.Create(10);

    private static PnLConfiguration Pnl() =>
        PnLConfiguration.Create(InstrumentPnLSpecification.Create("ES", 50m, "USD"), quantity: 1, startingCapital: 50_000m);

    private static BacktestRiskConfiguration RiskEnabled(decimal? maxExposure = null) =>
        BacktestRiskConfiguration.Create(true, RiskDistanceConfiguration.Fixed(2m), maxExposure);

    // ── §17/§20 Invariant 6: determinism ─────────────────────────────────────────────────────────────

    [Fact]
    public void SameInputs_RunTwice_ProduceTheSameDeterministicHash()
    {
        BacktestScenario scenario = ScenarioFor(BacktestTestSeriesBuilder.MeanRevertingOu(200, seed: 4UL));

        BacktestFullResultWithRisk first = new BacktestEngine().RunFullBacktestWithRisk(scenario, 128, Measurement(), Exec(), Pnl(), ExecutionCostConfiguration.Disabled(), RiskEnabled());
        BacktestFullResultWithRisk second = new BacktestEngine().RunFullBacktestWithRisk(scenario, 128, Measurement(), Exec(), Pnl(), ExecutionCostConfiguration.Disabled(), RiskEnabled());

        Assert.Equal(first.RiskResult.DeterministicHash, second.RiskResult.DeterministicHash);
    }

    // ── §17: run isolation ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RunA_RunB_RunA_ProduceIdenticalResultsForTheRepeatedRunA()
    {
        BacktestScenario scenarioA = ScenarioFor(BacktestTestSeriesBuilder.MeanRevertingOu(180, seed: 42UL));
        BacktestScenario scenarioB = ScenarioFor(BacktestTestSeriesBuilder.WhiteNoise(140, seed: 99UL));
        var engine = new BacktestEngine();

        BacktestFullResultWithRisk runA1 = engine.RunFullBacktestWithRisk(scenarioA, 128, Measurement(), Exec(), Pnl(), ExecutionCostConfiguration.Disabled(), RiskEnabled());
        engine.RunFullBacktestWithRisk(scenarioB, 128, Measurement(), Exec(), Pnl(), ExecutionCostConfiguration.Disabled(), RiskEnabled());
        BacktestFullResultWithRisk runA2 = engine.RunFullBacktestWithRisk(scenarioA, 128, Measurement(), Exec(), Pnl(), ExecutionCostConfiguration.Disabled(), RiskEnabled());

        Assert.Equal(runA1.RiskResult.DeterministicHash, runA2.RiskResult.DeterministicHash);
    }

    // ── §18: risk+cost disabled reproduces RunFullBacktestWithCosts exactly ─────────────────────────

    [Fact]
    public void RiskAndCostDisabled_MatchesRunFullBacktestWithCostsExactly()
    {
        BacktestScenario scenario = ScenarioFor(BacktestTestSeriesBuilder.MeanRevertingOu(200, seed: 7UL));
        var engine = new BacktestEngine();

        BacktestFullResultWithCosts costsResult = engine.RunFullBacktestWithCosts(scenario, 128, Measurement(), Exec(), Pnl(), ExecutionCostConfiguration.Disabled());
        BacktestFullResultWithRisk riskResult = engine.RunFullBacktestWithRisk(scenario, 128, Measurement(), Exec(), Pnl(), ExecutionCostConfiguration.Disabled(), BacktestRiskConfiguration.Disabled());

        Assert.Equal(costsResult.FullResult.PnLResult.FinalGrossPnL, riskResult.RiskResult.FinalNetPnL);
        Assert.Equal(costsResult.FullResult.PnLResult.MaximumDrawdown, riskResult.RiskResult.MaximumDrawdown);
        Assert.Equal(costsResult.FullResult.PnLResult.FinalEquity, riskResult.RiskResult.FinalEquity);
    }

    [Fact]
    public void RiskEnabled_NeverChangesTheUnderlyingSignalMeasurementExecutionResults()
    {
        BacktestScenario scenario = ScenarioFor(BacktestTestSeriesBuilder.MeanRevertingOu(200, seed: 7UL));
        var engine = new BacktestEngine();

        BacktestFullResultWithCosts costsResult = engine.RunFullBacktestWithCosts(scenario, 128, Measurement(), Exec(), Pnl(), ExecutionCostConfiguration.Disabled());
        BacktestFullResultWithRisk riskResult = engine.RunFullBacktestWithRisk(scenario, 128, Measurement(), Exec(), Pnl(), ExecutionCostConfiguration.Disabled(), RiskEnabled());

        Assert.Equal(costsResult.FullResult.SignalResult.DeterministicHash, riskResult.SignalResult.DeterministicHash);
        Assert.Equal(costsResult.FullResult.ExecutionResult.DeterministicHash, riskResult.ExecutionResult.DeterministicHash);
    }

    // ── §20: scientific invariants hold across a real signal-pipeline run ──────────────────────────

    [Fact]
    public void Invariants_HoldAcrossEveryAllowedPositionInARealRun()
    {
        BacktestScenario scenario = ScenarioFor(BacktestTestSeriesBuilder.MeanRevertingOu(250, seed: 11UL));
        BacktestRiskConfiguration riskConfig = RiskEnabled(maxExposure: 20000m);

        BacktestFullResultWithRisk result = new BacktestEngine().RunFullBacktestWithRisk(
            scenario, 128, Measurement(), Exec(), Pnl(), ExecutionCostConfiguration.Disabled(), riskConfig);

        Assert.True(result.RiskResult.RequestedCount > 0, "Expected at least one Closed position for this scenario.");

        int checkedCount = 0;
        foreach (var outcome in result.RiskResult.Outcomes.Where(o => o.RiskEvaluation.IsAllowed))
        {
            checkedCount++;
            // Invariant 1.
            Assert.True(outcome.RiskEvaluation.AllowedQuantity <= outcome.RiskEvaluation.RequestedQuantity);
            // Invariant 2 (Spec().MaxQuantity = 50).
            Assert.True(outcome.RiskEvaluation.AllowedQuantity <= 50);
            // Invariant 3.
            Assert.True(outcome.RiskEvaluation.TotalEstimatedRisk <= outcome.RiskEvaluation.MaximumAllowedRisk);
            // Invariant 4.
            if (outcome.RiskEvaluation.MaxExposure is decimal maxExposure)
                Assert.True(outcome.RiskEvaluation.Exposure <= maxExposure);
        }

        Assert.True(checkedCount > 0, "Expected at least one ALLOWED position to verify invariants against.");
    }

    [Fact]
    public void Invariant5_RejectedPositions_ProduceNoExecutionNoCostNoPnlNoEquityChange()
    {
        BacktestScenario scenario = ScenarioFor(BacktestTestSeriesBuilder.MeanRevertingOu(200, seed: 13UL));
        // A risk distance so small it never yields a usable stop for this instrument's PointValue (RiskPerUnit
        // would be 0.02 * 50 = 1, but MaxRiskPerTradeAmount below is smaller than one unit's risk).
        BacktestRiskConfiguration tightConfig = BacktestRiskConfiguration.Create(
            true, RiskDistanceConfiguration.Fixed(1000000m)); // absurdly large distance -> RiskPerUnit huge -> always ZeroRisk

        BacktestFullResultWithRisk result = new BacktestEngine().RunFullBacktestWithRisk(
            scenario, 128, Measurement(), Exec(), Pnl(), ExecutionCostConfiguration.Disabled(), tightConfig);

        foreach (var outcome in result.RiskResult.Outcomes.Where(o => o.Status == PositionStatus.Closed))
        {
            if (outcome.RiskEvaluation.IsAllowed)
                continue;

            Assert.Null(outcome.CostResult);
            Assert.Null(outcome.NetPnL);
            Assert.Equal(outcome.EquityBefore, outcome.EquityAfter);
        }
    }
}
