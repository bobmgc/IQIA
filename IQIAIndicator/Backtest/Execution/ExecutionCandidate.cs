using System;
using IQIAIndicator.Engine.EntryTrigger;
using IQIAIndicator.Engine.TradePlan;

namespace IQIAIndicator.Backtest.Execution;

/// <summary>
/// Sprint 15.25 (Lot 14.5, brief §5). Explicit representation of an execution opportunity, extracted
/// from a <see cref="BacktestSignalResult"/> - never a recomputation of the signal itself (brief §5: "Ne
/// pas recalculer le signal"). Deliberately does NOT carry the whole <see cref="TradePlan"/> (brief §5:
/// "Ne pas copier inutilement tout le TradePlan") - only the fields <see cref="ExecutionSimulator"/>
/// actually needs to decide whether/how to simulate a position.
///
/// Sprint 15.25 (Lot 15.4, brief §4): <see cref="StopLoss"/>/<see cref="TakeProfit"/> extend this same
/// record rather than introducing a second representation - <c>TradePlan.StopLoss</c>/<c>.TakeProfit</c>
/// remain the single source of truth (brief §4: "Le Stop Loss doit rester une seule source de vérité").
/// Both are null exactly when <c>TradePlan</c>'s own fields are null (SIGNAL_ONLY or no plan at all) -
/// never a fabricated fallback.
/// </summary>
public sealed record ExecutionCandidate(
    int SignalBarIndex,
    DateTime SignalTimestamp,
    DirectionCandidate Direction,
    decimal? EntryPrice,
    TradePlanStatus? TradePlanStatus,
    decimal? StopLoss = null,
    decimal? TakeProfit = null)
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
            plan?.Status,
            plan?.StopLoss,
            plan?.TakeProfit);
    }
}
