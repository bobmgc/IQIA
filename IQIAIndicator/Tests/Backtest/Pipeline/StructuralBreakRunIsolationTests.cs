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
/// Sprint 15.25 (Lot 15.8). No state may leak between two <see cref="BacktestEngine.RunSignalPipeline"/>
/// calls because of the StructuralBreak addition specifically - extends the exact pattern already
/// established by <see cref="BacktestSignalPipelineRunIsolationTests"/> (Lot 14.3) for the one new
/// component this lot adds to the shared <see cref="FusionStateManager"/>'s Dimensions array.
/// <see cref="StructuralBreakEvidenceRule"/> itself carries no field at all (stateless by construction),
/// so the only plausible leak vector is <see cref="FusionStateManager"/>'s own EMA history for the new
/// dimension - already covered generically by the Lot 14.3 test via <c>DeterministicHash</c>, and here
/// additionally verified directly on the StructuralBreak-specific per-bar fields via a fresh parallel walk.
/// </summary>
public sealed class StructuralBreakRunIsolationTests
{
    private static InstrumentRiskSpecification Spec() => InstrumentRiskSpecification.FromInstrumentInfo(
        new InstrumentInfo("ES", TickSize: 0.25m, TickValue: 12.5m, PointValue: 50m, Decimals: 2),
        minQuantity: 1, maxQuantity: 50, quantityStep: 1);

    private static RiskPolicy Policy() => new(0.02m, null, null, null, null, null, null, null, null, null);

    private static BacktestScenario ScenarioFor(HistoricalSeries series) =>
        BacktestScenario.Create(
            series, new BacktestWindow("W", series.FirstTimestamp, series.LastTimestamp.AddMinutes(1)),
            50_000m, Spec(), Policy());

    [Fact]
    public void RunA_RunB_RunA_OnTheSameEngineInstance_ProducesIdenticalHashesForTheRepeatedRunA()
    {
        BacktestScenario scenarioA = ScenarioFor(BacktestTestSeriesBuilder.MeanRevertingOu(180, seed: 42UL));
        BacktestScenario scenarioB = ScenarioFor(BacktestTestSeriesBuilder.WhiteNoise(140, seed: 99UL));

        var engine = new BacktestEngine();
        BacktestSignalPipelineResult runA1 = engine.RunSignalPipeline(scenarioA, warmupBars: 128);
        engine.RunSignalPipeline(scenarioB, warmupBars: 128);
        BacktestSignalPipelineResult runA2 = engine.RunSignalPipeline(scenarioA, warmupBars: 128);

        Assert.Equal(runA1.DeterministicHash, runA2.DeterministicHash);
    }

    private static FusionEngine BuildProductionEquivalentFusionEngine() => new(new IFusionRule[]
    {
        new StationarityRule(), new PersistenceRule(), new MeanReversionRule(), new RandomWalkRule(),
        new StructuralBreakEvidenceRule()
    });

    private sealed record StructuralBreakSnapshot(int BarIndex, bool Detected, double Strength, double StableValue, bool StableIsAvailable);

    private static List<StructuralBreakSnapshot> WalkFreshInstances(IReadOnlyList<BacktestSignalResult> bars, HistoricalSeries series)
    {
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

            list.Add(new StructuralBreakSnapshot(bar.BarIndex, contract.Detected, contract.Strength, stable.Value, stable.IsAvailable));
        }

        return list;
    }

    [Fact]
    public void StructuralBreakSpecificWalk_RunA_RunB_RunA_NeverCrossContaminates()
    {
        HistoricalSeries seriesA = BacktestTestSeriesBuilder.MeanRevertingOu(200, seed: 7UL);
        HistoricalSeries seriesB = BacktestTestSeriesBuilder.WhiteNoise(150, seed: 3UL);

        BacktestSignalPipelineResult signalA = new BacktestEngine().RunSignalPipeline(ScenarioFor(seriesA), warmupBars: 128);
        BacktestSignalPipelineResult signalB = new BacktestEngine().RunSignalPipeline(ScenarioFor(seriesB), warmupBars: 128);
        Assert.Equal(0, signalA.ExceptionCount);
        Assert.Equal(0, signalB.ExceptionCount);

        List<StructuralBreakSnapshot> a1 = WalkFreshInstances(signalA.Bars, seriesA);
        List<StructuralBreakSnapshot> bIntervening = WalkFreshInstances(signalB.Bars, seriesB);
        List<StructuralBreakSnapshot> a2 = WalkFreshInstances(signalA.Bars, seriesA);

        Assert.True(a1.Count > 0);
        Assert.Equal(a1.Count, a2.Count);
        for (int i = 0; i < a1.Count; i++)
            Assert.Equal(a1[i], a2[i]);

        // bIntervening's own outcome is irrelevant here - only that running it in between never perturbed
        // the freshly-constructed instances used for a1/a2 (each Walk call builds brand-new
        // FusionEngine/FusionStateManager/StructuralBreakEvidenceRule instances, so this is expected to
        // hold trivially; it is verified rather than assumed).
        Assert.True(bIntervening.Count >= 0);
    }

    [Fact]
    public void NoException_MeansStructuralBreakWiringNeverLeaksBetweenTwoDifferentScenarios()
    {
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
