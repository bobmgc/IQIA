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
/// Sprint 15.25 (Lot 15.8). End-to-end proof, through the real, unmodified
/// <see cref="BacktestEngine.RunSignalPipeline"/> (which already wires <see cref="StructuralBreakEvidenceRule"/>
/// into its fusion rule list - see that method's own doc comment), that the pipeline runs without exception
/// on a realistic synthetic scenario and that every processed bar's stabilized fusion state carries a
/// <see cref="FusionDimension.StructuralBreak"/> entry.
///
/// <see cref="BacktestSignalResult"/> intentionally does not expose the per-bar <see cref="FusionSnapshot"/>
/// (only <see cref="BacktestSignalResult.Decision"/>, its already-arbitrated output, is kept) - so this test
/// reconstructs the stabilized fusion state itself via a SEPARATE, freshly-constructed
/// <see cref="FusionEngine"/> (identical rule list, identical order, to the one
/// <see cref="BacktestEngine.RunSignalPipeline"/> builds internally) and <see cref="FusionStateManager"/>,
/// fed bar-by-bar from each processed bar's own <see cref="EvidenceSet"/> - the exact parallel-walk
/// technique already used by <c>StructuralBreakEvidenceAblationLot152Tests</c> (Tests/Backtest/Calibration).
/// </summary>
public sealed class StructuralBreakFusionIntegrationTests
{
    private static InstrumentRiskSpecification Spec() => InstrumentRiskSpecification.FromInstrumentInfo(
        new InstrumentInfo("ES", TickSize: 0.25m, TickValue: 12.5m, PointValue: 50m, Decimals: 2),
        minQuantity: 1, maxQuantity: 50, quantityStep: 1);

    private static RiskPolicy Policy() => new(0.02m, null, null, null, null, null, null, null, null, null);

    private static BacktestScenario ScenarioFor(HistoricalSeries series) =>
        BacktestScenario.Create(
            series, new BacktestWindow("W", series.FirstTimestamp, series.LastTimestamp.AddMinutes(1)),
            50_000m, Spec(), Policy());

    /// <summary>Same rule list/order <see cref="BacktestEngine.RunSignalPipeline"/> constructs internally.</summary>
    private static FusionEngine BuildProductionEquivalentFusionEngine() => new(new IFusionRule[]
    {
        new StationarityRule(), new PersistenceRule(), new MeanReversionRule(), new RandomWalkRule(),
        new StructuralBreakEvidenceRule()
    });

    [Fact]
    public void RunSignalPipeline_OnRealisticScenario_RunsWithoutException()
    {
        HistoricalSeries series = BacktestTestSeriesBuilder.MeanRevertingOu(400, seed: 11UL);
        BacktestSignalPipelineResult result = new BacktestEngine().RunSignalPipeline(ScenarioFor(series), warmupBars: 128);

        Assert.Equal(0, result.ExceptionCount);
        Assert.True(result.BarsProcessed > 0);
        Assert.True(result.ReadyBars > 0);
    }

    [Fact]
    public void EveryProcessedBar_StabilizedFusionState_ContainsStructuralBreakDimension()
    {
        HistoricalSeries series = BacktestTestSeriesBuilder.MeanRevertingOu(400, seed: 11UL);
        BacktestSignalPipelineResult result = new BacktestEngine().RunSignalPipeline(ScenarioFor(series), warmupBars: 128);
        Assert.Equal(0, result.ExceptionCount);

        FusionEngine fusionEngine = BuildProductionEquivalentFusionEngine();
        var fusionState = new FusionStateManager();

        int processedBars = 0;
        foreach (BacktestSignalResult bar in result.Bars)
        {
            if (bar.Status is BacktestSignalStatus.Rejected or BacktestSignalStatus.Exception) continue;
            if (bar.Regime is null) continue;

            FusionResult raw = fusionEngine.Fuse(new FusionContext
            {
                Evidence = bar.Regime,
                Timestamp = bar.Timestamp,
                Symbol = series.Symbol,
                TimeFrame = series.TimeFrame,
                EvaluationId = Guid.Empty
            });
            FusionSnapshot snapshot = fusionState.Update(raw, bar.Timestamp);

            Assert.True(
                snapshot.StableResult.Dimensions.ContainsKey(FusionDimension.StructuralBreak),
                $"Bar {bar.BarIndex}: StableResult is missing the StructuralBreak dimension.");
            processedBars++;
        }

        Assert.True(processedBars > 0, "Expected at least one processed bar to make this assertion meaningful.");
        Assert.Equal(result.BarsProcessed, processedBars);
    }
}
