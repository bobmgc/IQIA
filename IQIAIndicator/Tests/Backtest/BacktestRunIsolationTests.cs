using IQIAIndicator.Backtest;
using IQIAIndicator.Core;
using IQIAIndicator.Core.MarketData;
using IQIAIndicator.Engine.Risk;
using Xunit;

namespace IQIAIndicator.Tests.BacktestTests;

/// <summary>
/// Sprint 15.25 (Lot 14.1, brief §16 - MANDATORY). Proves no engine state leaks between runs: TRAIN then
/// VALIDATION must produce the exact same VALIDATION result as VALIDATION run alone.
///
/// The Lot 13 report flagged FusionStateManager as having no reset hook at all (§13.4/RISK-10) - a
/// standing risk for whichever future lot wires it in. This lot does not wire FusionStateManager in
/// (only RegimeEngine, brief §1/§11), but the rule this test enforces - "one instance per run, reused
/// engine object or not, TRAIN's execution must be invisible to VALIDATION" - is the exact contract
/// every later stateful component this engine grows must also satisfy.
/// </summary>
public sealed class BacktestRunIsolationTests
{
    private const int WarmupBars = 30;

    private static InstrumentRiskSpecification Spec() => InstrumentRiskSpecification.FromInstrumentInfo(
        new InstrumentInfo("ES", TickSize: 0.25m, TickValue: 12.5m, PointValue: 50m, Decimals: 2),
        minQuantity: 1, maxQuantity: 50, quantityStep: 1);

    private static RiskPolicy Policy() => new(0.02m, null, null, null, null, null, null, null, null, null);

    private static BacktestScenario Scenario(HistoricalSeries series, string windowName)
    {
        var window = new BacktestWindow(windowName, series.FirstTimestamp, series.LastTimestamp.AddMinutes(1));
        return BacktestScenario.Create(series, window, 50_000m, Spec(), Policy());
    }

    [Fact]
    public void ValidationResult_IsIdentical_WhetherRunAlone_OrAfterTrain_OnTheSameEngineInstance()
    {
        HistoricalSeries trainSeries = BacktestTestSeriesBuilder.WhiteNoise(180, seed: 42UL);
        HistoricalSeries validationSeries = BacktestTestSeriesBuilder.WhiteNoise(150, seed: 43UL);

        BacktestScenario trainScenario = Scenario(trainSeries, "TRAIN");
        BacktestScenario validationScenario = Scenario(validationSeries, "VALIDATION");

        // Case 1: VALIDATION run alone.
        var engineAlone = new BacktestEngine();
        BacktestFoundationResult validationAlone = engineAlone.Run(validationScenario, WarmupBars);

        // Case 2: the SAME engine instance runs TRAIN first, then VALIDATION - proving reuse of one
        // BacktestEngine object across scenarios carries no residual state.
        var engineSequential = new BacktestEngine();
        BacktestFoundationResult trainResult = engineSequential.Run(trainScenario, WarmupBars);
        BacktestFoundationResult validationAfterTrain = engineSequential.Run(validationScenario, WarmupBars);

        // Sanity: TRAIN actually ran and processed its own, different series - this is not a vacuous test.
        Assert.NotEqual(trainResult.ScenarioId, validationAlone.ScenarioId);
        Assert.True(trainResult.BarsProcessed > 0);

        Assert.Equal(validationAlone, validationAfterTrain);
    }

    [Fact]
    public void TrainResult_IsIdentical_WhetherRunAlone_OrBeforeValidation()
    {
        // Complementary direction: running something AFTER TRAIN must not retroactively change what TRAIN
        // itself already returned (TRAIN's result is captured before VALIDATION ever runs, in both cases).
        HistoricalSeries trainSeries = BacktestTestSeriesBuilder.WhiteNoise(180, seed: 42UL);
        HistoricalSeries validationSeries = BacktestTestSeriesBuilder.WhiteNoise(150, seed: 43UL);

        BacktestScenario trainScenario = Scenario(trainSeries, "TRAIN");
        BacktestScenario validationScenario = Scenario(validationSeries, "VALIDATION");

        var engineAlone = new BacktestEngine();
        BacktestFoundationResult trainAlone = engineAlone.Run(trainScenario, WarmupBars);

        var engineSequential = new BacktestEngine();
        BacktestFoundationResult trainBeforeValidation = engineSequential.Run(trainScenario, WarmupBars);
        engineSequential.Run(validationScenario, WarmupBars);

        Assert.Equal(trainAlone, trainBeforeValidation);
    }

    [Fact]
    public void TwoIndependentEnginesOnTheSameScenario_ProduceIdenticalResults()
    {
        HistoricalSeries series = BacktestTestSeriesBuilder.WhiteNoise(160, seed: 44UL);
        BacktestScenario scenario = Scenario(series, "OOS");

        BacktestFoundationResult resultA = new BacktestEngine().Run(scenario, WarmupBars);
        BacktestFoundationResult resultB = new BacktestEngine().Run(scenario, WarmupBars);

        Assert.Equal(resultA, resultB);
    }
}
