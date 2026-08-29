using System.Collections.Generic;

namespace IQIAIndicator.Engine.Risk;

/// <summary>
/// Sprint 15.25 (Lot 10, Section 11-12). Structured output of RiskEngine.Evaluate. Deliberately a new,
/// standalone type rather than an overload of TradePlan (Lot 10, Section 12): TradePlan already has its
/// own tested field set and status machine (NO_TRADE/SIGNAL_ONLY/PLAN_READY/PLAN_BLOCKED) driven by
/// TradePlanBuilder; RiskAssessment is the Risk Engine's own contract, only ACCEPTED/REJECTED. Every
/// nullable field mirrors TradePlan's convention: null means "cannot be honestly derived from the
/// inputs", never a fabricated fallback.
/// </summary>
public sealed record RiskAssessment(
    RiskDecisionStatus Status,
    IReadOnlyList<RiskRejectionReason> RejectionReasons,
    TradeDirection Direction,
    decimal EntryPrice,
    decimal? StopLoss,
    decimal? TakeProfit,
    decimal? RiskDistance,
    decimal? RewardDistance,
    decimal? RiskPerUnit,
    decimal? RiskBudget,
    int? PositionSize,
    decimal? RiskAmount,
    decimal? RewardAmount,
    double? RiskRewardRatio,
    IReadOnlyList<string> Diagnostics)
{
    public bool IsAccepted => Status == RiskDecisionStatus.ACCEPTED;
}
