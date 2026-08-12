using System;
using System.Collections.Generic;
using IQIAIndicator.Engine.EntryTrigger;

namespace IQIAIndicator.Engine.TradePlan;

/// <summary>
/// Sprint 15.8. Classifies how complete a trade plan is for the current bar.
/// NO_TRADE: Direction has no directional candidate (NO_ACTION/WATCH) - nothing to plan.
/// SIGNAL_ONLY: a directional candidate exists but a required component (StopLoss, PositionSize, ...)
/// is categorically unavailable in the system today (no methodology/config exists for it yet).
/// PLAN_READY: Direction, EntryPrice, StopLoss, TakeProfit, RiskPerUnit, PositionSize and
/// RiskRewardRatio are all valid.
/// PLAN_BLOCKED: a directional candidate exists and the required inputs were nominally present, but
/// the computation itself is degenerate/unsafe (e.g. zero or negative risk) - never NaN/Infinity.
/// </summary>
public enum TradePlanStatus
{
    NO_TRADE,
    SIGNAL_ONLY,
    PLAN_READY,
    PLAN_BLOCKED
}

/// <summary>
/// Sprint 15.8 - Trade Plan Contract. Represents a potential trade deterministically from data the
/// pipeline already produced: never fabricates a price, stop, target or size. Any field that cannot be
/// honestly derived is null (see TradePlanBuilder for the exact source of each field, and Diagnostics /
/// InvalidationReason for why a field is null on a given bar). Produced only for observability - this
/// sprint does not send orders or enable automatic execution.
/// </summary>
public sealed record TradePlan(
    bool IsValid,
    TradePlanStatus Status,
    DirectionCandidate Direction,
    decimal? EntryPrice,
    decimal? StopLoss,
    decimal? TakeProfit,
    decimal? RiskPerUnit,
    decimal? RiskAmount,
    int? PositionSize,
    double? RiskRewardRatio,
    string? InvalidationReason,
    IReadOnlyList<string> Diagnostics,
    DateTime Timestamp);
