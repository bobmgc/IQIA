using System;
using IQIAIndicator.Engine.EntryTrigger;
using IQIAIndicator.Engine.Presentation;
using IQIAIndicator.Engine.TradePlan;

namespace IQIAIndicator.Tests.PresentationTests;

/// <summary>
/// Sprint 15.24 (Lot 1 - chart display, TEST A-I). Unit coverage of TradePlanAnnotationBuilder: proves
/// it only ever copies price fields TradePlanBuilder already produced (never a formula), that a null
/// field on TradePlan produces no level at all, and that Direction/prices reaching the chart are
/// bit-for-bit identical to what TradingDashboard already displays from the same TradePlan.
/// </summary>
public static class TradePlanAnnotationBuilderTests
{
    public static void RunAll()
    {
        AssertNullTradePlanProducesNoLevels();
        AssertNoTradeProducesNoLevels();
        AssertEntryPriceProducesEntryLevel();
        AssertTakeProfitProducesTakeProfitLevel();
        AssertStopLossPresentProducesStopLossLevel();
        AssertStopLossNullProducesNoStopLossLevel();
        AssertLongDirectionIsCarriedOnCandidate();
        AssertShortDirectionIsCarriedOnCandidate();
        AssertPricesMatchTradePlanExactly();
    }

    // TEST A
    private static void AssertNullTradePlanProducesNoLevels()
    {
        TradePlanAnnotationCandidate candidate = Populate(null);
        Assert(candidate.Levels.Count == 0, "A null TradePlan must produce zero levels - nothing to draw.");
    }

    // TEST B
    private static void AssertNoTradeProducesNoLevels()
    {
        TradePlan plan = BuildTradePlan(DirectionCandidate.NO_ACTION, entryPrice: null, stopLoss: null, takeProfit: null);
        TradePlanAnnotationCandidate candidate = Populate(plan);
        Assert(candidate.Levels.Count == 0, "NO_TRADE (no EntryPrice) must produce zero levels.");
    }

    // TEST C
    private static void AssertEntryPriceProducesEntryLevel()
    {
        TradePlan plan = BuildTradePlan(DirectionCandidate.BUY_CANDIDATE, entryPrice: 4523.25m, stopLoss: null, takeProfit: null);
        TradePlanAnnotationCandidate candidate = Populate(plan);

        Assert(candidate.Levels.Count == 1, $"EntryPrice alone must produce exactly one level. Actual count={candidate.Levels.Count}.");
        Assert(candidate.Levels[0].Kind == TradePlanLevelKind.Entry, "The single level must be Entry.");
        Assert(candidate.Levels[0].Price == 4523.25m, $"Entry level price must equal EntryPrice. Actual={candidate.Levels[0].Price}.");
    }

    // TEST D
    private static void AssertTakeProfitProducesTakeProfitLevel()
    {
        TradePlan plan = BuildTradePlan(DirectionCandidate.BUY_CANDIDATE, entryPrice: 4523.25m, stopLoss: null, takeProfit: 4550.00m);
        TradePlanAnnotationCandidate candidate = Populate(plan);

        TradePlanLevel? takeProfitLevel = Find(candidate, TradePlanLevelKind.TakeProfit);
        Assert(takeProfitLevel is not null, "TakeProfit non-null must produce a TakeProfit level.");
        Assert(takeProfitLevel!.Price == 4550.00m, $"TakeProfit level price must equal TradePlan.TakeProfit. Actual={takeProfitLevel.Price}.");
    }

    // TEST E
    private static void AssertStopLossPresentProducesStopLossLevel()
    {
        TradePlan plan = BuildTradePlan(DirectionCandidate.BUY_CANDIDATE, entryPrice: 4523.25m, stopLoss: 4510.75m, takeProfit: null);
        TradePlanAnnotationCandidate candidate = Populate(plan);

        TradePlanLevel? stopLossLevel = Find(candidate, TradePlanLevelKind.StopLoss);
        Assert(stopLossLevel is not null, "StopLoss non-null must produce a StopLoss level.");
        Assert(stopLossLevel!.Price == 4510.75m, $"StopLoss level price must equal TradePlan.StopLoss. Actual={stopLossLevel.Price}.");
    }

    // TEST F
    private static void AssertStopLossNullProducesNoStopLossLevel()
    {
        TradePlan plan = BuildTradePlan(DirectionCandidate.BUY_CANDIDATE, entryPrice: 4523.25m, stopLoss: null, takeProfit: 4550.00m);
        TradePlanAnnotationCandidate candidate = Populate(plan);

        Assert(Find(candidate, TradePlanLevelKind.StopLoss) is null,
            "StopLoss == null (today's production reality, no Risk Engine yet) must never produce a StopLoss level - nothing invented.");
    }

    // TEST G
    private static void AssertLongDirectionIsCarriedOnCandidate()
    {
        TradePlan plan = BuildTradePlan(DirectionCandidate.BUY_CANDIDATE, entryPrice: 100m, stopLoss: null, takeProfit: null);
        TradePlanAnnotationCandidate candidate = Populate(plan);
        Assert(candidate.Direction == DirectionCandidate.BUY_CANDIDATE,
            $"Long TradePlan must carry BUY_CANDIDATE onto the candidate (the renderer draws the up-marker from this). Actual={candidate.Direction}.");
    }

    // TEST H
    private static void AssertShortDirectionIsCarriedOnCandidate()
    {
        TradePlan plan = BuildTradePlan(DirectionCandidate.SELL_CANDIDATE, entryPrice: 100m, stopLoss: null, takeProfit: null);
        TradePlanAnnotationCandidate candidate = Populate(plan);
        Assert(candidate.Direction == DirectionCandidate.SELL_CANDIDATE,
            $"Short TradePlan must carry SELL_CANDIDATE onto the candidate (the renderer draws the down-marker from this). Actual={candidate.Direction}.");
    }

    // TEST I - covers steps 7 (HUD/chart consistency) and 8 (no price formula in the renderer path) of
    // the lot's test list: deliberately non-round prices, so any rounding/tick-snapping/offset slipped
    // in anywhere would show up as an inequality.
    private static void AssertPricesMatchTradePlanExactly()
    {
        TradePlan plan = BuildTradePlan(
            DirectionCandidate.SELL_CANDIDATE,
            entryPrice: 4523.371m,
            stopLoss: 4530.129m,
            takeProfit: 4501.008m);

        TradePlanAnnotationCandidate candidate = Populate(plan);

        Assert(Find(candidate, TradePlanLevelKind.Entry)!.Price == plan.EntryPrice,
            "Entry level price must be bit-for-bit identical to TradePlan.EntryPrice - the same value TradingDashboard displays - no formula applied.");
        Assert(Find(candidate, TradePlanLevelKind.StopLoss)!.Price == plan.StopLoss,
            "StopLoss level price must be bit-for-bit identical to TradePlan.StopLoss.");
        Assert(Find(candidate, TradePlanLevelKind.TakeProfit)!.Price == plan.TakeProfit,
            "TakeProfit level price must be bit-for-bit identical to TradePlan.TakeProfit.");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────

    private static TradePlanAnnotationCandidate Populate(TradePlan? plan)
    {
        var builder = new TradePlanAnnotationBuilder();
        builder.Populate(plan);
        return builder.Build();
    }

    private static TradePlanLevel? Find(TradePlanAnnotationCandidate candidate, TradePlanLevelKind kind)
    {
        foreach (TradePlanLevel level in candidate.Levels)
        {
            if (level.Kind == kind)
            {
                return level;
            }
        }

        return null;
    }

    private static TradePlan BuildTradePlan(DirectionCandidate direction, decimal? entryPrice, decimal? stopLoss, decimal? takeProfit)
        => new(
            IsValid: false,
            Status: TradePlanStatus.SIGNAL_ONLY,
            Direction: direction,
            EntryPrice: entryPrice,
            StopLoss: stopLoss,
            TakeProfit: takeProfit,
            RiskPerUnit: null,
            RiskAmount: null,
            PositionSize: null,
            RiskRewardRatio: null,
            InvalidationReason: null,
            Diagnostics: Array.Empty<string>(),
            Timestamp: DateTime.UtcNow);

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
