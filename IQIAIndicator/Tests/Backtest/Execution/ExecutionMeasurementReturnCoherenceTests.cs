using IQIAIndicator.Backtest;
using IQIAIndicator.Backtest.Execution;
using IQIAIndicator.Backtest.Measurement;
using IQIAIndicator.Core;
using IQIAIndicator.Core.MarketData;
using IQIAIndicator.Engine.EntryTrigger;
using IQIAIndicator.Engine.Risk;
using IQIAIndicator.Tests.BacktestTests;
using Xunit;

namespace IQIAIndicator.Tests.BacktestTests.Execution;

/// <summary>
/// Sprint 15.25 (Lot 14.5, brief §15/§35/§36; reinterpreted in Lot 14.10, P0-1). Before Lot 14.10, this
/// test proved Lot 14.4's <see cref="MeasurementResult.Return"/> and Lot 14.5's
/// <see cref="SimulatedPosition.Return"/> were bit-identical - but that was only because BOTH engines
/// happened to use the same (biased) Close[SignalBarIndex] entry convention, not because they answer the
/// same question. Lot 14.10 corrects Execution's entry to Open[SignalBarIndex+1] and its exit to
/// Close[SignalBarIndex+1+HorizonBars] (the realistic, look-ahead-safe convention - see
/// <see cref="ExecutionSimulator.SimulateCore"/>'s own doc comment) while leaving Measurement UNCHANGED (it
/// still answers "what happened after the signal, regardless of any exit convention", anchored on
/// Close[SignalBarIndex] and Close[SignalBarIndex+HorizonBars]). The two engines now genuinely diverge -
/// this test proves that divergence is real and present, rather than silently re-asserting an equality
/// that no longer holds for the right reason.
/// </summary>
public sealed class ExecutionMeasurementReturnCoherenceTests
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
    public void ExecutionReturn_IsInternallyConsistentWithItsOwnEntryExitPrices_UsingTheUnchangedSignConvention()
    {
        // Sprint 15.25 (Lot 14.10, P0-1): proves Execution.Return still follows the exact sign convention
        // documented since Lot 14.5 (brief §15) - (Exit-Entry)/Entry for BUY, (Entry-Exit)/Entry for SELL -
        // computed independently here from the position's OWN recorded EntryPrice/ExitPrice, never by
        // trusting SimulatedPosition.Return blindly.
        const int horizon = 10;
        HistoricalSeries series = BacktestTestSeriesBuilder.MeanRevertingOu(300, seed: 23UL, kappa: 0.8m);
        BacktestScenario scenario = ScenarioFor(series);

        BacktestSimulationResult result = new BacktestEngine().RunSimulation(
            scenario, warmupBars: 128,
            MeasurementConfiguration.Create(horizon, new[] { 0.001 }),
            ExecutionConfiguration.Create(horizon));

        int checkedCount = 0;
        foreach (SimulatedPosition position in result.ExecutionResult.Positions)
        {
            if (position.Status != PositionStatus.Closed)
                continue;

            checkedCount++;
            double entry = (double)position.EntryPrice!.Value;
            double exit = (double)position.ExitPrice!.Value;
            double expected = position.Direction == DirectionCandidate.BUY_CANDIDATE
                ? (exit - entry) / entry
                : (entry - exit) / entry;

            Assert.Equal(expected, position.Return!.Value, 12);
        }

        Assert.True(checkedCount > 0, "Expected at least one Closed position for this scenario.");
    }

    [Fact]
    public void ExecutionReturn_NowGenuinelyDivergesFromMeasurementReturn_BecauseTheExitBarShiftedByOne()
    {
        // Sprint 15.25 (Lot 14.10, P0-1): before this lot, ExecutionReturn and MeasurementReturn were
        // bit-identical because both engines used the signal bar i's own Close as entry AND bar i+HorizonBars
        // as exit. Execution now anchors on bar i+1 instead (Open[i+1] entry, Close[i+1+HorizonBars] exit -
        // see ExecutionSimulator.SimulateCore's doc comment) while Measurement is UNCHANGED. On the
        // synthetic, gap-free series this test suite uses (BacktestTestSeriesBuilder.BuildFromCloses sets
        // every bar's Open to the PREVIOUS bar's Close), the two engines' ENTRY prices still happen to
        // coincide numerically (Open[i+1] == Close[i] exactly, no gap) - but their EXIT bars are now
        // genuinely different bars (Close[i+HorizonBars] vs Close[i+1+HorizonBars]), so Return must still
        // diverge whenever the series actually moves between those two bars, proving the fix changed real
        // behaviour and not just bookkeeping.
        const int horizon = 10;
        HistoricalSeries series = BacktestTestSeriesBuilder.MeanRevertingOu(300, seed: 23UL, kappa: 0.8m);
        BacktestScenario scenario = ScenarioFor(series);

        BacktestSimulationResult result = new BacktestEngine().RunSimulation(
            scenario, warmupBars: 128,
            MeasurementConfiguration.Create(horizon, new[] { 0.001 }),
            ExecutionConfiguration.Create(horizon));

        int comparedCount = 0, divergedReturnCount = 0;
        for (int i = 0; i < result.ExecutionResult.Positions.Count; i++)
        {
            SimulatedPosition position = result.ExecutionResult.Positions[i];
            MeasurementResult measurement = result.Measurements[i];

            if (position.Status != PositionStatus.Closed || measurement.Status != MeasurementStatus.Measured)
                continue;

            comparedCount++;

            // Sanity check on THIS specific gap-free synthetic series: entry prices coincide numerically -
            // the divergence below comes entirely from the exit bar, not the entry price.
            Assert.Equal(measurement.EntryPrice!.Value, position.EntryPrice!.Value);

            if (position.Return!.Value != measurement.Return!.Value)
                divergedReturnCount++;
        }

        Assert.True(comparedCount > 0, "Expected at least one bar where both Measurement and Execution reached a comparable Closed/Measured state.");
        Assert.True(divergedReturnCount > 0,
            "Expected at least one position where ExecutionReturn genuinely differs from MeasurementReturn - otherwise this dataset cannot exercise the Lot 14.10 fix at all.");
    }
}
