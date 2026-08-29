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
/// Sprint 15.25 (Lot 15.8). Look-ahead safety for the StructuralBreak dimension, at two levels:
///
/// 1) Raw contract (<see cref="StructuralBreakEvidenceRule.BuildContract"/>): a pure function of a single
///    bar's own <see cref="EvidenceSet.Cusum"/>/<see cref="EvidenceSet.BaiPerron"/>, which
///    <c>Tests/GoldenDatasets/StructuralBreakEvidenceLookAheadTests.cs</c> (Lot 15.2) already proved
///    look-ahead safe at the evidence-producer level - re-verified here end to end through
///    <see cref="BacktestEngine.RunSignalPipeline"/>.
///
/// 2) Stabilized <see cref="FusionDimension.StructuralBreak"/> (after <see cref="FusionStateManager"/>'s
///    EMA+hysteresis, which depends on the ENTIRE preceding bar history, not just the current bar) - the
///    part genuinely new to this lot and not covered by any Lot 15.2 evidence-producer-level proof.
///
/// Methodology: one long series, <c>Truncate()</c>d to a shorter one - guarantees a byte-identical prefix
/// (same technique as <c>BacktestSignalPipelineLookAheadTests</c>/<c>BacktestFoundationLookAheadTests</c>).
/// Both series run through the real <see cref="BacktestEngine.RunSignalPipeline"/>, then each run's own
/// bar-by-bar <see cref="EvidenceSet"/> sequence is fed into an independently-constructed, production-
/// equivalent <see cref="FusionEngine"/> + <see cref="FusionStateManager"/> pair (see
/// <see cref="StructuralBreakFusionIntegrationTests"/> for why this reconstruction is necessary -
/// <see cref="BacktestSignalResult"/> does not itself expose the per-bar <see cref="FusionSnapshot"/>).
/// Appending future bars past the truncation point must never change any earlier bar's raw contract fields
/// or its stabilized StructuralBreak dimension.
/// </summary>
public sealed class StructuralBreakLookAheadTests
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

    private static List<StructuralBreakSnapshot> Walk(BacktestSignalPipelineResult signalResult, HistoricalSeries series)
    {
        FusionEngine fusionEngine = BuildProductionEquivalentFusionEngine();
        var fusionState = new FusionStateManager();
        var list = new List<StructuralBreakSnapshot>();

        foreach (BacktestSignalResult bar in signalResult.Bars)
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
    public void AppendingFutureBars_NeverChangesAnyEarlierBarsStructuralBreakContractOrStabilizedDimension()
    {
        const int warmupBars = 128;
        const int truncatedLength = 220;
        const int fullLength = 320;

        HistoricalSeries full = BacktestTestSeriesBuilder.MeanRevertingOu(fullLength, seed: 13UL);
        HistoricalSeries truncated = BacktestTestSeriesBuilder.Truncate(full, truncatedLength);

        BacktestSignalPipelineResult shortRun = new BacktestEngine().RunSignalPipeline(ScenarioFor(truncated), warmupBars);
        BacktestSignalPipelineResult longRun = new BacktestEngine().RunSignalPipeline(ScenarioFor(full), warmupBars);
        Assert.Equal(0, shortRun.ExceptionCount);
        Assert.Equal(0, longRun.ExceptionCount);

        List<StructuralBreakSnapshot> shortWalk = Walk(shortRun, truncated);
        List<StructuralBreakSnapshot> longWalk = Walk(longRun, full);

        Assert.True(shortWalk.Count > 0);
        Assert.True(longWalk.Count >= shortWalk.Count);

        // Several distinct checkpoints (t values), explicitly, plus the full common range for maximum rigor.
        int[] checkpoints = { 0, 10, shortWalk.Count / 4, shortWalk.Count / 2, shortWalk.Count - 1 };
        foreach (int t in checkpoints)
        {
            Assert.True(t >= 0 && t < shortWalk.Count, $"Checkpoint t={t} out of range.");
        }

        int mismatches = 0;
        for (int i = 0; i < shortWalk.Count; i++)
        {
            StructuralBreakSnapshot s = shortWalk[i];
            StructuralBreakSnapshot l = longWalk[i];

            bool same =
                s.BarIndex == l.BarIndex &&
                s.Detected == l.Detected &&
                s.Strength == l.Strength &&
                s.BreakCountMagnitude == l.BreakCountMagnitude &&
                s.Agreement == l.Agreement &&
                s.BreakLocationBarIndex == l.BreakLocationBarIndex &&
                s.StableValue == l.StableValue &&
                s.StableConfidence == l.StableConfidence &&
                s.StableIsAvailable == l.StableIsAvailable;

            if (!same)
            {
                mismatches++;
                Assert.Fail(
                    $"Bar {s.BarIndex}: appending future bars changed StructuralBreak evidence (look-ahead). " +
                    $"Short: Detected={s.Detected}, Strength={s.Strength:F6}, StableValue={s.StableValue:F6}. " +
                    $"Long: Detected={l.Detected}, Strength={l.Strength:F6}, StableValue={l.StableValue:F6}.");
            }
        }

        Assert.Equal(0, mismatches);
    }
}
