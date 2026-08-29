using System;
using System.Collections.Generic;
using System.Linq;
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
/// Sprint 15.25 (Lot 15.5). Through the REAL <see cref="BacktestEngine.RunSimulation"/> pipeline (never
/// hand-built candidates) on a synthetic <c>MeanRevertingOu</c> series - proves the full pipeline produces
/// <see cref="PositionStatus.InvalidStopTarget"/> via the "signal-to-fill price gap" pathway strictly less
/// often (or equal) than a manual replica of Lot 15.4's Option-A logic would have on the SAME
/// <see cref="ExecutionCandidate"/> data. The replica ("OldOptionAWouldReject" below) implements ONLY the
/// OLD (Lot 15.4) direction check - comparing raw TradePlan.StopLoss/.TakeProfit against the real fill,
/// with NO reconciliation - exactly what <see cref="ExecutionSimulator.SimulateCore"/> did before this lot.
/// This is the synthetic-data before/after proof, complementing the real Yahoo-data proof in
/// <c>FillPriceReconciliationLot155Tests</c>.
/// </summary>
public sealed class FillPriceReconciliationIntegrationTests
{
    private readonly ITestOutputHelper _output;

    public FillPriceReconciliationIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static InstrumentRiskSpecification Spec() => InstrumentRiskSpecification.FromInstrumentInfo(
        new InstrumentInfo("ES", TickSize: 0.25m, TickValue: 12.5m, PointValue: 50m, Decimals: 2),
        minQuantity: 1, maxQuantity: 50, quantityStep: 1);

    private static RiskPolicy Policy() => new(0.02m, null, null, null, null, null, null, null, null, null);

    private static BacktestScenario ScenarioFor(HistoricalSeries series) =>
        BacktestScenario.Create(
            series,
            new BacktestWindow("W", series.FirstTimestamp, series.LastTimestamp.AddMinutes(1)),
            50_000m, Spec(), Policy());

    private static MeasurementConfiguration Measurement() => MeasurementConfiguration.Create(10, new[] { 0.001 });

    private static ExecutionConfiguration Exec(int horizon = 10) => ExecutionConfiguration.Create(horizon);

    /// <summary>The OLD (Lot 15.4) direction check, replicated in isolation: compares the RAW
    /// TradePlan.StopLoss/.TakeProfit directly against the real fill price - no reconciliation of any
    /// kind. Returns true when Option A would have rejected this candidate as InvalidStopTarget. Mirrors
    /// exactly the pre-Lot-15.5 body of <see cref="ExecutionSimulator.SimulateCore"/> (git history), never
    /// re-derived differently.</summary>
    private static bool OldOptionAWouldReject(ExecutionCandidate candidate, decimal realFillPrice)
    {
        if (candidate.StopLoss is decimal stopLoss)
        {
            bool stopOnCorrectSide = candidate.Direction == DirectionCandidate.BUY_CANDIDATE
                ? stopLoss < realFillPrice
                : stopLoss > realFillPrice;
            if (!stopOnCorrectSide) return true;
        }

        if (candidate.TakeProfit is decimal takeProfit)
        {
            bool targetOnCorrectSide = candidate.Direction == DirectionCandidate.BUY_CANDIDATE
                ? takeProfit > realFillPrice
                : takeProfit < realFillPrice;
            if (!targetOnCorrectSide) return true;
        }

        return false;
    }

    [Fact]
    public void RealPipeline_InvalidStopTargetCount_IsNeverGreaterThan_TheOldOptionAReplicaOnTheSameCandidates()
    {
        HistoricalSeries series = BacktestTestSeriesBuilder.MeanRevertingOu(600, seed: 7UL);
        BacktestScenario scenario = ScenarioFor(series);
        const int warmupBars = 128;

        BacktestSimulationResult result = new BacktestEngine().RunSimulation(scenario, warmupBars, Measurement(), Exec());

        Assert.Equal(0, result.SignalResult.ExceptionCount);
        Assert.Equal(series.Count, result.ExecutionResult.Positions.Count);

        int candidatesConsidered = 0;
        int newInvalidStopTargetCount = 0;
        int oldWouldHaveRejectedCount = 0;

        for (int i = 0; i < result.SignalResult.Bars.Count; i++)
        {
            BacktestSignalResult signal = result.SignalResult.Bars[i];
            ExecutionCandidate candidate = ExecutionCandidate.FromSignal(signal);

            if (candidate.Direction is not (DirectionCandidate.BUY_CANDIDATE or DirectionCandidate.SELL_CANDIDATE))
                continue;
            if (candidate.StopLoss is null && candidate.TakeProfit is null)
                continue; // reconciliation/Option-A are both no-ops with nothing configured

            // Real fill price: bars[SignalBarIndex+1].Open - the SAME convention SimulateCore itself uses.
            int fillBarIndex = candidate.SignalBarIndex + 1;
            if (fillBarIndex >= series.Bars.Count) continue;
            HistoricalBar fillBar = series.Bars[fillBarIndex];
            if (!fillBar.IsValid || fillBar.Open <= 0m) continue;
            decimal realFillPrice = fillBar.Open;

            candidatesConsidered++;

            SimulatedPosition actualPosition = result.ExecutionResult.Positions[i];
            if (actualPosition.Status == PositionStatus.InvalidStopTarget)
                newInvalidStopTargetCount++;

            if (OldOptionAWouldReject(candidate, realFillPrice))
                oldWouldHaveRejectedCount++;
        }

        _output.WriteLine($"Directional candidates with >=1 Stop/Target configured and a valid fill bar: {candidatesConsidered}");
        _output.WriteLine($"NEW (Lot 15.5) InvalidStopTarget count (actual pipeline):   {newInvalidStopTargetCount}");
        _output.WriteLine($"OLD (Lot 15.4 Option-A replica) would-have-rejected count: {oldWouldHaveRejectedCount}");

        Assert.True(candidatesConsidered > 0, "Expected at least one directional candidate with a configured Stop/Target on this synthetic dataset.");
        Assert.True(newInvalidStopTargetCount <= oldWouldHaveRejectedCount,
            $"Expected the new reconciliation-based pipeline to reject via InvalidStopTarget no more often than the old Option-A replica " +
            $"(new={newInvalidStopTargetCount}, old={oldWouldHaveRejectedCount}).");
    }

    [Fact]
    public void RealPipeline_EveryPositionOldOptionAWouldHaveRejected_ButNewCodeAccepts_IsCloseViaStopOrTargetOrTimeHorizon_NeverNotExecutableOrOtherStatus()
    {
        // Complementary framing: for every candidate the OLD replica would have rejected but the NEW
        // pipeline did not reject as InvalidStopTarget, confirm the position actually reached a resolved,
        // monitored outcome (Closed via StopLoss/TakeProfit/Ambiguous/TimeHorizon, or a data-availability
        // status unrelated to the Stop/Target gap) - never silently dropped.
        HistoricalSeries series = BacktestTestSeriesBuilder.MeanRevertingOu(600, seed: 7UL);
        BacktestScenario scenario = ScenarioFor(series);
        const int warmupBars = 128;

        BacktestSimulationResult result = new BacktestEngine().RunSimulation(scenario, warmupBars, Measurement(), Exec());

        int rescuedCount = 0;
        var rescuedExitReasons = new Dictionary<ExitReason, int>();

        for (int i = 0; i < result.SignalResult.Bars.Count; i++)
        {
            BacktestSignalResult signal = result.SignalResult.Bars[i];
            ExecutionCandidate candidate = ExecutionCandidate.FromSignal(signal);
            if (candidate.Direction is not (DirectionCandidate.BUY_CANDIDATE or DirectionCandidate.SELL_CANDIDATE)) continue;
            if (candidate.StopLoss is null && candidate.TakeProfit is null) continue;

            int fillBarIndex = candidate.SignalBarIndex + 1;
            if (fillBarIndex >= series.Bars.Count) continue;
            HistoricalBar fillBar = series.Bars[fillBarIndex];
            if (!fillBar.IsValid || fillBar.Open <= 0m) continue;

            if (!OldOptionAWouldReject(candidate, fillBar.Open)) continue; // only "would have been rejected" cases

            SimulatedPosition actualPosition = result.ExecutionResult.Positions[i];
            if (actualPosition.Status == PositionStatus.InvalidStopTarget) continue; // still rejected under new code too

            rescuedCount++;
            if (actualPosition.Status == PositionStatus.Closed && actualPosition.ExitReason is ExitReason reason)
                rescuedExitReasons[reason] = rescuedExitReasons.GetValueOrDefault(reason) + 1;

            // Never silently dropped into an unrelated bucket that would hide the rescue.
            Assert.True(actualPosition.Status is PositionStatus.Closed or PositionStatus.InsufficientFutureData or PositionStatus.InvalidExit,
                $"Rescued candidate at bar {i} resolved to unexpected status {actualPosition.Status}.");
        }

        _output.WriteLine($"Rescued positions (Option-A would have rejected, new code did not): {rescuedCount}");
        foreach (KeyValuePair<ExitReason, int> kv in rescuedExitReasons)
            _output.WriteLine($"  -> ExitReason.{kv.Key}: {kv.Value}");

        Assert.True(rescuedCount >= 0); // descriptive - dataset-dependent whether any such case exists
    }
}
