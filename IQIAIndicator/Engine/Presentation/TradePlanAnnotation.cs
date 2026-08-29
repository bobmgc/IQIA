using System;
using System.Collections.Generic;
using IQIAIndicator.Engine.EntryTrigger;

namespace IQIAIndicator.Engine.Presentation;

public enum TradePlanLevelKind
{
    Entry,
    StopLoss,
    TakeProfit
}

/// <summary>
/// Sprint 15.24 (Lot 1 - chart display). A single price level to draw on the chart. Price is copied
/// unchanged from TradePlan (see TradePlanAnnotationBuilder) - this record never computes a price, it
/// only carries one that TradePlanBuilder already produced.
/// </summary>
public sealed record TradePlanLevel(TradePlanLevelKind Kind, decimal Price);

/// <summary>
/// Sprint 15.24 (Lot 1 - chart display). Presentation-layer output for the chart-space rendering of a
/// TradePlan, mirroring ChartAnnotationCandidate's Context/Candidate/Builder/Engine shape but carrying
/// real price levels instead of screen-anchored text boxes. Levels is empty whenever TradePlan is null,
/// invalid, or a given field (EntryPrice/StopLoss/TakeProfit) is null - never fabricated.
/// </summary>
public sealed record TradePlanAnnotationCandidate(
    DirectionCandidate Direction,
    IReadOnlyList<TradePlanLevel> Levels,
    DateTime CreatedAt);
