using IQIAIndicator.Backtest;
using IQIAIndicator.Core;
using IQIAIndicator.Core.MarketData;
using IQIAIndicator.Engine.Risk;
using IQIAIndicator.Tests.BacktestTests;
using Xunit;

namespace IQIAIndicator.Tests.BacktestTests.Pipeline;

/// <summary>
/// Sprint 15.25 (Lot 14.3, brief §23, BLOCKING acceptance criterion). No state may leak between two
/// <see cref="BacktestEngine.RunSignalPipeline"/> calls - in particular
/// <see cref="Engine.Fusion.State.FusionStateManager"/>, the one genuinely stateful component this lot
/// wires in (see <see cref="BacktestEngine.RunSignalPipeline"/>'s doc comment).
/// </summary>
public sealed class BacktestSignalPipelineRunIsolationTests
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

    [Fact]
    public void RunA_RunB_RunA_ProduceIdenticalResultsForTheRepeatedRunA()
    {
        BacktestScenario scenarioA = ScenarioFor(BacktestTestSeriesBuilder.MeanRevertingOu(180, seed: 42UL));
        BacktestScenario scenarioB = ScenarioFor(BacktestTestSeriesBuilder.WhiteNoise(140, seed: 99UL));

        var engine = new BacktestEngine();
        BacktestSignalPipelineResult runA1 = engine.RunSignalPipeline(scenarioA, warmupBars: 128);
        engine.RunSignalPipeline(scenarioB, warmupBars: 128);
        BacktestSignalPipelineResult runA2 = engine.RunSignalPipeline(scenarioA, warmupBars: 128);

        Assert.Equal(runA1.DeterministicHash, runA2.DeterministicHash);
        Assert.Equal(runA1.ScenarioId, runA2.ScenarioId);
        Assert.Equal(runA1.BuyCount, runA2.BuyCount);
        Assert.Equal(runA1.SellCount, runA2.SellCount);
        Assert.Equal(runA1.TradePlanSignalOnlyCount, runA2.TradePlanSignalOnlyCount);
    }

    [Fact]
    public void RunMes_RunOtherFixture_RunMesAgain_SecondMesRunIsIdenticalToTheFirst()
    {
        BacktestScenario mesLike = ScenarioFor(BacktestTestSeriesBuilder.MeanRevertingOu(200, seed: 7UL));
        BacktestScenario otherFixture = ScenarioFor(BacktestTestSeriesBuilder.WhiteNoise(90, seed: 3UL));

        var engine = new BacktestEngine();
        BacktestSignalPipelineResult first = engine.RunSignalPipeline(mesLike, warmupBars: 128);
        engine.RunSignalPipeline(otherFixture, warmupBars: 30);
        BacktestSignalPipelineResult second = engine.RunSignalPipeline(mesLike, warmupBars: 128);

        Assert.Equal(first.DeterministicHash, second.DeterministicHash);
    }

    [Fact]
    public void TwoIndependentEngineInstances_OnTheSameScenario_ProduceIdenticalResults()
    {
        BacktestScenario scenario = ScenarioFor(BacktestTestSeriesBuilder.MeanRevertingOu(160, seed: 17UL));

        BacktestSignalPipelineResult first = new BacktestEngine().RunSignalPipeline(scenario, warmupBars: 128);
        BacktestSignalPipelineResult second = new BacktestEngine().RunSignalPipeline(scenario, warmupBars: 128);

        Assert.Equal(first.DeterministicHash, second.DeterministicHash);
        Assert.Equal(first.ScenarioId, second.ScenarioId);
    }

    [Fact]
    public void NoException_MeansFusionStateManagerNeverLeaksBetweenTwoDifferentScenarios()
    {
        // If FusionStateManager (or any other component) were shared/cached across runs instead of
        // constructed fresh inside RunSignalPipeline, running a short, differently-shaped scenario right
        // after a long one could carry stale smoothing state into the short run's very first bars -
        // this would not necessarily throw, but it WOULD change the short run's own result depending on
        // run order, which the isolation tests above already rule out empirically. This test adds the
        // structural check that back-to-back heterogeneous runs never throw either.
        var engine = new BacktestEngine();
        BacktestScenario long1 = ScenarioFor(BacktestTestSeriesBuilder.MeanRevertingOu(250, seed: 1UL));
        BacktestScenario short1 = ScenarioFor(BacktestTestSeriesBuilder.MeanRevertingOu(5, seed: 2UL));

        BacktestSignalPipelineResult r1 = engine.RunSignalPipeline(long1, warmupBars: 128);
        BacktestSignalPipelineResult r2 = engine.RunSignalPipeline(short1, warmupBars: 128);
        BacktestSignalPipelineResult r3 = engine.RunSignalPipeline(short1, warmupBars: 128);

        Assert.Equal(0, r1.ExceptionCount);
        Assert.Equal(0, r2.ExceptionCount);
        Assert.Equal(r2.DeterministicHash, r3.DeterministicHash);
    }
}
