using System.Collections.Generic;
using System.Linq;
using IQIAIndicator.Backtest;
using IQIAIndicator.Core;
using IQIAIndicator.Core.MarketData;
using IQIAIndicator.Engine.Risk;
using IQIAIndicator.Tests.BacktestTests;
using Xunit;

namespace IQIAIndicator.Tests.BacktestTests.Pipeline;

/// <summary>
/// Sprint 15.25 (Lot 15.3, brief §17 Case J / §16). Two properties specific to the new
/// <see cref="Engine.Risk.VolatilityStopLossModel"/> wiring in <see cref="BacktestEngine.RunSignalPipeline"/>
/// (brief §14/§15 of the task): (1) look-ahead safety of <c>TradePlan.StopLoss</c> specifically, using the
/// exact truncated-vs-extended methodology already established by
/// <see cref="BacktestSignalPipelineLookAheadTests"/> (which itself already asserts StopLoss equality as
/// part of its broader field-by-field comparison - this file adds a StopLoss-focused test with an explicit
/// "at least one non-null StopLoss was actually compared" sanity check, so the invariant is never vacuous);
/// (2) run isolation for StopLoss specifically, using the exact interleaved-scenario methodology already
/// established by <see cref="BacktestSignalPipelineRunIsolationTests"/>. VolatilityStopLossModel itself has
/// no mutable state (readonly fields, pure static entry point) - these tests prove that property survives
/// end-to-end through BacktestEngine.
/// </summary>
public sealed class VolatilityStopLossLot153PipelineTests
{
    private static InstrumentRiskSpecification Spec() => InstrumentRiskSpecification.FromInstrumentInfo(
        new InstrumentInfo("ES", TickSize: 0.25m, TickValue: 12.5m, PointValue: 50m, Decimals: 2),
        minQuantity: 1, maxQuantity: 50, quantityStep: 1);

    // MaxRiskPerTradePercent set (unlike some sibling fixtures' null) so RiskPerTrade resolves to a
    // non-null value too, exercising the full TradeRiskParameters this lot wires in - not just StopLoss.
    private static RiskPolicy Policy() => new(0.02m, null, null, null, null, null, null, null, null, null);

    private static BacktestScenario ScenarioFor(HistoricalSeries series) =>
        BacktestScenario.Create(
            series,
            new BacktestWindow("W", series.FirstTimestamp, series.LastTimestamp.AddMinutes(1)),
            50_000m, Spec(), Policy());

    [Fact]
    public void AppendingFutureBars_NeverChangesTradePlanStopLossAlreadyProducedForAnEarlierBar()
    {
        HistoricalSeries full = BacktestTestSeriesBuilder.MeanRevertingOu(320, seed: 13UL);
        HistoricalSeries truncated = BacktestTestSeriesBuilder.Truncate(full, 220);

        const int warmupBars = 128;
        BacktestSignalPipelineResult shortRun = new BacktestEngine().RunSignalPipeline(ScenarioFor(truncated), warmupBars);
        BacktestSignalPipelineResult longRun = new BacktestEngine().RunSignalPipeline(ScenarioFor(full), warmupBars);

        Assert.Equal(220, shortRun.Bars.Count);

        int nonNullCompared = 0;
        for (int i = 0; i < 220; i++)
        {
            decimal? shortStop = shortRun.Bars[i].TradePlan?.StopLoss;
            decimal? longStop = longRun.Bars[i].TradePlan?.StopLoss;

            Assert.True(shortStop == longStop,
                $"Bar {i}: appending future data changed TradePlan.StopLoss (look-ahead). Short={shortStop}, Long={longStop}.");

            if (shortStop is not null)
                nonNullCompared++;
        }

        Assert.True(nonNullCompared > 0,
            "This dataset/seed must actually produce at least one non-null StopLoss in the compared prefix, " +
            "or the equality check above would be vacuously true for this lot's own new field.");
    }

    [Fact]
    public void RunA_RunB_RunA_ProduceIdenticalTradePlanStopLossSequenceForTheRepeatedRunA()
    {
        BacktestScenario scenarioA = ScenarioFor(BacktestTestSeriesBuilder.MeanRevertingOu(220, seed: 13UL));
        BacktestScenario scenarioB = ScenarioFor(BacktestTestSeriesBuilder.WhiteNoise(140, seed: 99UL));

        var engine = new BacktestEngine();
        BacktestSignalPipelineResult runA1 = engine.RunSignalPipeline(scenarioA, warmupBars: 128);
        engine.RunSignalPipeline(scenarioB, warmupBars: 128);
        BacktestSignalPipelineResult runA2 = engine.RunSignalPipeline(scenarioA, warmupBars: 128);

        List<decimal?> stopSequence1 = runA1.Bars.Select(b => b.TradePlan?.StopLoss).ToList();
        List<decimal?> stopSequence2 = runA2.Bars.Select(b => b.TradePlan?.StopLoss).ToList();

        Assert.Equal(stopSequence1, stopSequence2);
        Assert.Equal(runA1.TradePlanReadyCount, runA2.TradePlanReadyCount);
        Assert.True(stopSequence1.Any(s => s is not null), "This scenario must actually produce at least one non-null StopLoss, or the sequence-equality check would be vacuous.");
    }
}
