using System.Collections.Generic;
using IQIAIndicator.Backtest;
using IQIAIndicator.Core;
using IQIAIndicator.Core.MarketData;
using IQIAIndicator.Engine.Risk;
using IQIAIndicator.Tests.BacktestTests;
using Xunit;

namespace IQIAIndicator.Tests.BacktestTests.Pipeline;

/// <summary>
/// Sprint 15.25 (Lot 14.3, brief §24, BLOCKING acceptance criterion). Same input -&gt; same fingerprint;
/// changing one bar -&gt; a different fingerprint. Complements <see cref="Backtest.BacktestSignalFingerprint"/>'s
/// own doc comment on what is deliberately excluded (wall-clock Timestamps, free text).
/// </summary>
public sealed class BacktestSignalPipelineDeterminismTests
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
    public void SameScenario_RunTwice_ProducesTheSameDeterministicHash()
    {
        BacktestScenario scenario = ScenarioFor(BacktestTestSeriesBuilder.MeanRevertingOu(200, seed: 4UL));

        BacktestSignalPipelineResult first = new BacktestEngine().RunSignalPipeline(scenario, warmupBars: 128);
        BacktestSignalPipelineResult second = new BacktestEngine().RunSignalPipeline(scenario, warmupBars: 128);

        Assert.Equal(first.DeterministicHash, second.DeterministicHash);
        Assert.Equal(first.BarsProcessed, second.BarsProcessed);
        Assert.Equal(first.BuyCount, second.BuyCount);
        Assert.Equal(first.SellCount, second.SellCount);
        Assert.Equal(first.NoActionCount, second.NoActionCount);
        Assert.Equal(first.TradePlanSignalOnlyCount, second.TradePlanSignalOnlyCount);
    }

    [Fact]
    public void SameScenario_RunTwice_WithARealWallClockDelayBetweenRuns_StillProducesTheSameHash()
    {
        // Proves empirically - not just by code inspection - that the wall-clock Timestamps every stage
        // stamps (DateTime.UtcNow on EntryCandidate/EntryTriggerAssessment/TradePlan/MethodologySelection
        // - see the Lot 14.3 report's Determinism section for the full inventory) never leak into
        // DeterministicHash, exactly mirroring Lot 14.1's own BacktestDeterminismTests wall-clock-delay
        // test for the Regime-only fingerprint.
        BacktestScenario scenario = ScenarioFor(BacktestTestSeriesBuilder.MeanRevertingOu(150, seed: 8UL));

        BacktestSignalPipelineResult first = new BacktestEngine().RunSignalPipeline(scenario, warmupBars: 128);
        System.Threading.Thread.Sleep(50);
        BacktestSignalPipelineResult second = new BacktestEngine().RunSignalPipeline(scenario, warmupBars: 128);

        Assert.Equal(first.DeterministicHash, second.DeterministicHash);
    }

    [Fact]
    public void ChangingOneBar_ProducesADifferentDeterministicHash()
    {
        HistoricalSeries baseline = BacktestTestSeriesBuilder.MeanRevertingOu(150, seed: 6UL);

        var mutatedBars = new List<HistoricalBar>(baseline.Bars);
        HistoricalBar original = mutatedBars[100];
        mutatedBars[100] = original with { Close = original.Close + 5m };
        HistoricalSeries mutated = HistoricalSeries.Create(
            baseline.Symbol, baseline.TimeFrame, baseline.TimeZone, baseline.Provider, mutatedBars);

        BacktestSignalPipelineResult baselineResult = new BacktestEngine().RunSignalPipeline(ScenarioFor(baseline), warmupBars: 128);
        BacktestSignalPipelineResult mutatedResult = new BacktestEngine().RunSignalPipeline(ScenarioFor(mutated), warmupBars: 128);

        Assert.NotEqual(baselineResult.DeterministicHash, mutatedResult.DeterministicHash);
        // ScenarioId is a function of series content too (Lot 14.1 - BacktestFingerprint.ComputeScenarioId
        // hashes Series.Count/First/Last, not every bar) - only assert what that function actually covers.
    }

    [Fact]
    public void DifferentWarmupBars_ChangesTheHash_ButNotTheScenarioId()
    {
        // Mirrors Lot 14.1's own BacktestDeterminismTests: WarmupBars is a parameter of the RUN, not of
        // the scenario's identity.
        BacktestScenario scenario = ScenarioFor(BacktestTestSeriesBuilder.MeanRevertingOu(150, seed: 6UL));

        BacktestSignalPipelineResult warmup30 = new BacktestEngine().RunSignalPipeline(scenario, warmupBars: 30);
        BacktestSignalPipelineResult warmup128 = new BacktestEngine().RunSignalPipeline(scenario, warmupBars: 128);

        Assert.Equal(warmup30.ScenarioId, warmup128.ScenarioId);
        Assert.NotEqual(warmup30.DeterministicHash, warmup128.DeterministicHash);
    }
}
