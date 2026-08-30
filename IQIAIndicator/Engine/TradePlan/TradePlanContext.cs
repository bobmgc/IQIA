using IQIAIndicator.Core;
using IQIAIndicator.Engine.EntryTrigger;

namespace IQIAIndicator.Engine.TradePlan;

/// <summary>
/// Sprint 15.8 (Phase 1 audit finding): no Risk Engine, stop-loss methodology, or account risk-budget
/// source exists anywhere in the codebase today (QDE-011_Risk_Engine_Theory.md is a design document,
/// not an implementation). RiskParameters is the extension point a future Risk Engine sprint would
/// populate; today IQIAIndicator.cs passes null, and TradePlanBuilder must never invent a value to
/// fill the gap (see TradePlanBuilder's StopLoss/PositionSize handling).
/// </summary>
public sealed record TradeRiskParameters(
    decimal? StopLoss,
    decimal? RiskPerTrade,
    // Audit 2026-08-29: minimum acceptable reward/risk ratio. When set (> 0) and the plan's
    // RiskRewardRatio is below it, TradePlanBuilder returns PLAN_REJECTED instead of PLAN_READY - it
    // stops the pipeline presenting a structurally unfavourable trade (e.g. a mean-reversion entry
    // taken near equilibrium: tiny target, volatility-wide stop). Sourced from RiskPolicy.MinRiskReward
    // (the "Risk/Reward minimum" indicator parameter / BacktestScenario policy). Null/0 = no gate.
    double? MinRiskReward = null,
    // Audit 2026-08-30 (P0-2): TakeProfit fallback as a multiple of the stop distance
    // (TP = Entry +/- R x |Entry - StopLoss|). The equilibrium target (EntryTriggerAssessment
    // .EstimatedEquilibrium) is a mean-reversion concept and does not exist for a trend-following
    // trade, so TradePlanBuilder uses this R-multiple ONLY when the equilibrium target is
    // absent/unfavourable and a StopLoss is present. Null/0 = no fallback (mean-reversion trades keep
    // their exact prior behaviour: equilibrium target or none).
    double? TakeProfitRMultiple = null);

public sealed record TradePlanContext(
    EntryTriggerCandidate EntryTriggerCandidate,
    InstrumentInfo InstrumentInfo,
    TradeRiskParameters? RiskParameters = null);
