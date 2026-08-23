using System;
using IQIAIndicator.Backtest.Execution;
using IQIAIndicator.Engine.EntryTrigger;

namespace IQIAIndicator.Backtest.Cost;

/// <summary>
/// Sprint 15.25 (Lot 14.7, brief §4 "ExecutionResult"). One <see cref="SimulatedPosition"/> (Lot 14.5),
/// enriched with the executed entry/exit prices and cost breakdown, produced by
/// <see cref="PositionCostCalculator"/>. Named "PositionCostResult" rather than the brief's literal
/// "ExecutionResult" (brief §4: "les noms exacts peuvent être adaptés") to avoid colliding with the
/// already-existing, unrelated <see cref="BacktestExecutionResult"/> (Lot 14.5) in the sibling
/// <c>Backtest.Execution</c> namespace - mirrors <see cref="Pnl.PositionPnLResult"/>'s naming instead.
///
/// <see cref="GrossPnL"/> is copied VERBATIM from <see cref="Pnl.PositionPnLResult.GrossPnL"/> - never
/// recomputed (same discipline Lot 14.6 already applied to Return). Only <see cref="Cost"/> and
/// <see cref="NetPnL"/> are new, derived values.
///
/// For any <see cref="PositionStatus"/> other than <see cref="PositionStatus.Closed"/>: ExecutedEntryPrice/
/// ExecutedExitPrice/GrossPnL/Cost/NetPnL are all null - no fill was ever attempted, so no cost was ever
/// incurred (brief §43 applied here too: "Ne jamais produire un P&amp;L silencieusement faux").
/// </summary>
public sealed record PositionCostResult(
    int PositionId,
    PositionStatus Status,
    DirectionCandidate Direction,
    DateTime EntryTimestamp,
    decimal? TheoreticalEntryPrice,
    decimal? ExecutedEntryPrice,
    DateTime? ExitTimestamp,
    decimal? TheoreticalExitPrice,
    decimal? ExecutedExitPrice,
    decimal? GrossPnL,
    ExecutionCost? Cost,
    decimal? NetPnL,
    string Currency,
    int Quantity);
