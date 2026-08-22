using System;
using IQIAIndicator.Backtest.Execution;
using IQIAIndicator.Engine.EntryTrigger;

namespace IQIAIndicator.Backtest.Pnl;

/// <summary>
/// Sprint 15.25 (Lot 14.6, brief §9). One <see cref="SimulatedPosition"/> (Lot 14.5) converted into
/// theoretical money, via <see cref="PositionPnLCalculator"/>. Deliberately does NOT copy the whole
/// position (brief §9: "Ne pas recopier inutilement toute la position") - only the fields needed to
/// interpret the P&amp;L in isolation, traceable back to the source position by
/// <see cref="PositionId"/> (the same identifier <see cref="SimulatedPosition.PositionId"/> already
/// uses).
///
/// <see cref="Return"/> is copied VERBATIM from <see cref="SimulatedPosition.Return"/> - never
/// recomputed (brief §8/§39: "Le P&amp;L ne doit PAS recalculer un Return différent").
/// <see cref="GrossPnL"/> is the only new, derived value this lot introduces.
/// </summary>
public sealed record PositionPnLResult(
    int PositionId,
    PositionStatus Status,
    DirectionCandidate Direction,
    DateTime EntryTimestamp,
    decimal? EntryPrice,
    DateTime? ExitTimestamp,
    decimal? ExitPrice,
    int? HoldingBars,
    decimal? GrossPriceMove,
    double? Return,
    decimal? GrossPnL,
    string Currency,
    int Quantity,
    ExitReason? ExitReason);
