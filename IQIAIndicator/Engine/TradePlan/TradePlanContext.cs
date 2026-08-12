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
    decimal? RiskPerTrade);

public sealed record TradePlanContext(
    EntryTriggerCandidate EntryTriggerCandidate,
    InstrumentInfo InstrumentInfo,
    TradeRiskParameters? RiskParameters = null);
