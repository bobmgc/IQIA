using System;
using IQIAIndicator.Engine.EntryTrigger;
using IQIAIndicator.Engine.TradePlan;

namespace IQIAIndicator.Backtest.Execution;

/// <summary>
/// Sprint 15.25 (Lot 14.5, brief §5). Explicit representation of an execution opportunity, extracted
/// from a <see cref="BacktestSignalResult"/> - never a recomputation of the signal itself (brief §5: "Ne
/// pas recalculer le signal"). Deliberately does NOT carry the whole <see cref="TradePlan"/> (brief §5:
/// "Ne pas copier inutilement tout le TradePlan") - only the four fields <see cref="ExecutionSimulator"/>
/// actually needs to decide whether/how to simulate a position.
/// </summary>
public sealed record ExecutionCandidate(
    int SignalBarIndex,
    DateTime SignalTimestamp,
    DirectionCandidate Direction,
    decimal? EntryPrice,
    TradePlanStatus? TradePlanStatus)
{
    /// <summary>The one place this lot reads <see cref="BacktestSignalResult.TradePlan"/> - mirrors
    /// exactly the extraction <see cref="Backtest.Measurement.ScientificMeasurementEngine.Measure"/>
    /// (Lot 14.4) already does for Direction/EntryPrice, applied here to build the execution-specific
    /// candidate instead.</summary>
    public static ExecutionCandidate FromSignal(BacktestSignalResult signal)
    {
        ArgumentNullException.ThrowIfNull(signal);

        TradePlan? plan = signal.TradePlan;
        return new ExecutionCandidate(
            signal.BarIndex,
            signal.Timestamp,
            plan?.Direction ?? DirectionCandidate.NO_ACTION,
            plan?.EntryPrice,
            plan?.Status);
    }
}
