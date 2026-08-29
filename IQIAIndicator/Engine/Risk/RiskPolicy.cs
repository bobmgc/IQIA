namespace IQIAIndicator.Engine.Risk;

/// <summary>
/// Sprint 15.25 (Lot 10, Section 4). Configurable risk policy. Every limit is nullable and opt-in - a
/// null limit means "not constrained by this rule", never a hidden default. All *Percent fields are
/// fractions (0.01 = 1%), consistent with AccountState.CurrentDrawdownPercent. No PropFirm-specific rule
/// is hardcoded here: callers supply whatever limits their account documentation specifies.
/// </summary>
public sealed record RiskPolicy(
    decimal? MaxRiskPerTradePercent,
    decimal? MaxRiskPerTradeAmount,
    decimal? MaxDailyLossPercent,
    decimal? MaxDailyLossAmount,
    decimal? MaxDrawdownPercent,
    decimal? MaxDrawdownAmount,
    decimal? MaxOpenRiskPercent,
    decimal? MaxOpenRiskAmount,
    double? MinRiskReward,
    int? MaxPositionSize,
    int? MinPositionSize = null);
