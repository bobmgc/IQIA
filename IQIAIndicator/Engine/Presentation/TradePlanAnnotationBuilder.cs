using System;
using System.Collections.Generic;
using IQIAIndicator.Engine.EntryTrigger;

namespace IQIAIndicator.Engine.Presentation;

/// <summary>
/// Sprint 15.24 (Lot 1 - chart display). Turns a TradePlan into the set of price levels the chart
/// should draw. Deliberately dumb: for each of EntryPrice/TakeProfit/StopLoss, emits a level only when
/// the source field is non-null, copying the price unchanged. Never computes, rounds, offsets, or
/// invents a price - TradePlanBuilder remains the single source of truth (see TradePlanBuilder.cs).
/// </summary>
public sealed class TradePlanAnnotationBuilder
{
    private readonly List<TradePlanLevel> _levels = new();
    private DirectionCandidate _direction = DirectionCandidate.NO_ACTION;

    // TradePlan is fully qualified here for the same reason as TradePlanAnnotationContext.cs - see
    // that file's comment.
    public void Populate(global::IQIAIndicator.Engine.TradePlan.TradePlan? tradePlan)
    {
        _levels.Clear();
        _direction = tradePlan?.Direction ?? DirectionCandidate.NO_ACTION;

        if (tradePlan is null)
        {
            return;
        }

        AddLevel(TradePlanLevelKind.Entry, tradePlan.EntryPrice);
        AddLevel(TradePlanLevelKind.TakeProfit, tradePlan.TakeProfit);
        AddLevel(TradePlanLevelKind.StopLoss, tradePlan.StopLoss);
    }

    public TradePlanAnnotationCandidate Build()
        => new(_direction, _levels.ToArray(), DateTime.UtcNow);

    private void AddLevel(TradePlanLevelKind kind, decimal? price)
    {
        if (price is decimal value)
        {
            _levels.Add(new TradePlanLevel(kind, value));
        }
    }
}
