using System.Collections.Generic;

namespace IQIAIndicator.Backtest.Risk;

/// <summary>
/// Sprint 15.25 (Lot 14.8). Full result of the risk/position-sizing layer, built by
/// <see cref="BacktestRiskResultBuilder"/>. Unlike <see cref="Pnl.BacktestPnLResult"/>/
/// <see cref="Cost.BacktestCostResult"/> (which express Equity as CumulativePnL offset by an OPTIONAL
/// StartingCapital), this result ALWAYS has a real <see cref="InitialCapital"/> - a risk evaluation is
/// structurally impossible without one (<c>Engine.Risk.RiskEngine.Evaluate</c>'s own Phase 1 requires
/// InitialCapital/CurrentEquity &gt; 0) - so Equity/Drawdown are tracked directly, never as an optional
/// add-on.
/// </summary>
/// <param name="Outcomes">One entry per input position, in the original (unsorted) order - same convention
/// as every prior lot's per-position result list.</param>
/// <param name="RequestedCount">Closed positions for which a risk evaluation was actually attempted.</param>
/// <param name="AllowedCount">Of those, how many resolved to AllowedQuantity &gt; 0.</param>
/// <param name="RejectedCount">RequestedCount - AllowedCount.</param>
/// <param name="InitialCapital">Echoes <see cref="Backtest.BacktestScenario.InitialCapital"/>.</param>
/// <param name="FinalEquity">InitialCapital + sum of every executed position's NetPnL.</param>
/// <param name="FinalNetPnL">FinalEquity - InitialCapital (brief §20 Invariant 6 target, and the same
/// number as Lot 14.6/14.7's own FinalGrossPnL/FinalNetPnL when risk/costs are both disabled).</param>
/// <param name="PeakEquity">Running maximum of Equity reached across the sequential walk.</param>
/// <param name="MaximumDrawdown">Minimum (Equity - PeakEquity) across the walk, or 0 when no position was
/// ever executed.</param>
/// <param name="DeterministicHash">See <see cref="RiskResultFingerprint.ComputeHash"/>.</param>
public sealed record BacktestRiskResult(
    IReadOnlyList<PositionRiskOutcome> Outcomes,
    int RequestedCount,
    int AllowedCount,
    int RejectedCount,
    decimal InitialCapital,
    decimal FinalEquity,
    decimal FinalNetPnL,
    decimal PeakEquity,
    decimal MaximumDrawdown,
    string DeterministicHash);
