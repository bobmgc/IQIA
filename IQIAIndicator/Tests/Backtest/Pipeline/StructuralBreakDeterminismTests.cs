using System;
using System.Collections.Generic;
using IQIAIndicator.Backtest;
using IQIAIndicator.Core;
using IQIAIndicator.Core.MarketData;
using IQIAIndicator.Engine.Fusion.Core;
using IQIAIndicator.Engine.Fusion.Rules;
using IQIAIndicator.Engine.Fusion.State;
using IQIAIndicator.Engine.Regime.Core;
using IQIAIndicator.Engine.Risk;
using IQIAIndicator.Tests.BacktestTests;
using Xunit;
using FusionEngine = IQIAIndicator.Engine.Fusion.EvidenceFusionEngine;

namespace IQIAIndicator.Tests.BacktestTests.Pipeline;

/// <summary>
/// Sprint 15.25 (Lot 15.8). Determinism for the StructuralBreak evidence dimension: (1) the whole,
/// unmodified pipeline (<see cref="BacktestEngine.RunSignalPipeline"/>, which already includes
/// <see cref="StructuralBreakEvidenceRule"/>) run twice on the same scenario must produce the same
/// <see cref="BacktestSignalPipelineResult.DeterministicHash"/>; (2) two INDEPENDENT instances of every
/// component this lot touches (new <see cref="StructuralBreakEvidenceRule"/>, new
/// <see cref="Engine.Fusion.EvidenceFusionEngine"/>, new <see cref="FusionStateManager"/>) fed the exact
/// same bar-by-bar evidence sequence must produce bit-identical raw contracts and stabilized dimension
/// values - <see cref="StructuralBreakEvidenceRule"/> carries no field (stateless, verified by inspection),
/// so this also empirically confirms that has no hidden effect.
/// </summary>
public sealed class StructuralBreakDeterminismTests
{
    private static InstrumentRiskSpecification Spec() => InstrumentRiskSpecification.FromInstrumentInfo(
        new InstrumentInfo("ES", TickSize: 0.25m, TickValue: 12.5m, PointValue: 50m, Decimals: 2),
        minQuantity: 1, maxQuantity: 50, quantityStep: 1);

    private static RiskPolicy Policy() => new(0.02m, null, null, null, null, null, null, null, null, null);

    private static BacktestScenario ScenarioFor(HistoricalSeries series) =>
        BacktestScenario.Create(
            series, new BacktestWindow("W", series.FirstTimestamp, series.LastTimestamp.AddMinutes(1)),
            50_000m, Spec(), Policy());

    private static FusionEngine BuildProductionEquivalentFusionEngine() => new(new IFusionRule[]
    {
        new StationarityRule(), new PersistenceRule(), new MeanReversionRule(), new RandomWalkRule(),
        new StructuralBreakEvidenceRule()
    });

    private sealed record StructuralBreakSnapshot(
        int BarIndex, bool Detected, double Strength, int BreakCountMagnitude,
        StructuralBreakAgreement Agreement, int? BreakLocationBarIndex,
        double StableValue, double StableConfidence, bool StableIsAvailable);

    private static List<StructuralBreakSnapshot> WalkFreshInstances(IReadOnlyList<BacktestSignalResult> bars, HistoricalSeries series)
    {
        // Deliberately brand-new instances every call - the point of this helper.
        FusionEngine fusionEngine = BuildProductionEquivalentFusionEngine();
        var fusionState = new FusionStateManager();
        var list = new List<StructuralBreakSnapshot>();

        foreach (BacktestSignalResult bar in bars)
        {
            if (bar.Status is BacktestSignalStatus.Rejected or BacktestSignalStatus.Exception) continue;
            if (bar.Regime is null) continue;

            StructuralBreakContract contract = StructuralBreakEvidenceRule.BuildContract(bar.Regime.Cusum, bar.Regime.BaiPerron);
            FusionResult raw = fusionEngine.Fuse(new FusionContext
            {
                Evidence = bar.Regime, Timestamp = bar.Timestamp, Symbol = series.Symbol, TimeFrame = series.TimeFrame, EvaluationId = Guid.Empty
            });
            FusionSnapshot snapshot = fusionState.Update(raw, bar.Timestamp);
            FusionConfidence stable = snapshot.StableResult.Dimensions[FusionDimension.StructuralBreak];

            list.Add(new StructuralBreakSnapshot(
                bar.BarIndex, contract.Detected, contract.Strength, contract.BreakCountMagnitude,
                contract.Agreement, contract.BreakLocationBarIndex,
                stable.Value, stable.Confidence, stable.IsAvailable));
        }

        return list;
    }

    [Fact]
    public void WholePipeline_RunTwiceOnTheSameScenario_ProducesTheSameDeterministicHash()
    {
        HistoricalSeries series = BacktestTestSeriesBuilder.MeanRevertingOu(260, seed: 21UL);
        BacktestScenario scenario = ScenarioFor(series);

        BacktestSignalPipelineResult first = new BacktestEngine().RunSignalPipeline(scenario, warmupBars: 128);
        BacktestSignalPipelineResult second = new BacktestEngine().RunSignalPipeline(scenario, warmupBars: 128);

        Assert.Equal(0, first.ExceptionCount);
        Assert.Equal(first.DeterministicHash, second.DeterministicHash);
    }

    [Fact]
    public void TwoIndependentFusionEngineAndStateManagerInstances_OnTheSameEvidenceSequence_ProduceBitIdenticalStructuralBreakResults()
    {
        HistoricalSeries series = BacktestTestSeriesBuilder.MeanRevertingOu(260, seed: 21UL);
        BacktestSignalPipelineResult signalResult = new BacktestEngine().RunSignalPipeline(ScenarioFor(series), warmupBars: 128);
        Assert.Equal(0, signalResult.ExceptionCount);

        List<StructuralBreakSnapshot> instanceA = WalkFreshInstances(signalResult.Bars, series);
        List<StructuralBreakSnapshot> instanceB = WalkFreshInstances(signalResult.Bars, series);

        Assert.True(instanceA.Count > 0);
        Assert.Equal(instanceA.Count, instanceB.Count);
        for (int i = 0; i < instanceA.Count; i++)
            Assert.Equal(instanceA[i], instanceB[i]); // record equality: every field, bit-identical
    }

    [Fact]
    public void SameInputs_RunTwice_WithARealWallClockDelayBetweenRuns_StillProducesTheSameResult()
    {
        HistoricalSeries series = BacktestTestSeriesBuilder.MeanRevertingOu(180, seed: 5UL);
        BacktestScenario scenario = ScenarioFor(series);

        BacktestSignalPipelineResult first = new BacktestEngine().RunSignalPipeline(scenario, warmupBars: 128);
        System.Threading.Thread.Sleep(50);
        BacktestSignalPipelineResult second = new BacktestEngine().RunSignalPipeline(scenario, warmupBars: 128);

        Assert.Equal(first.DeterministicHash, second.DeterministicHash);
    }
}
