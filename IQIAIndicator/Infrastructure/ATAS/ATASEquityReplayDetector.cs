using System;

namespace IQIAIndicator.Infrastructure.ATAS;

/// <summary>
/// Sprint 15.25 (Lot 12.6, extended Lot 12.11). ATASAccountStateAdapter.TryGetCurrentEquity (Lot 12.2,
/// protected, unchanged) already implements the correct rule - consult ITradingStatistics.Replay.Equity
/// when replaying, .Realtime.Equity otherwise - but the "isReplay" flag it is handed
/// (IQIAIndicator.cs -&gt; Core.ExecutionContext.IsReplay, Core.MarketContextBuilder.cs) is an in-house
/// heuristic derived purely from bar indices, documented there as such: ATAS's public SDK exposes no
/// "chart is replaying" flag on the bar/indicator level. A real MES Replay capture (Lot 12.6 brief)
/// proved this heuristic wrong for 1104 of 2304 bars in a single session - bars genuinely being replayed
/// were misclassified as "Realtime" because, from the indicator's point of view, ATAS feeds a Market
/// Replay bar-by-bar exactly like a live feed (bar == currentBar - 1 becomes true for each newly
/// delivered bar either way). The practical symptom: a single stale point sitting in
/// TradingStatisticsProvider.Realtime.Equity (dated days after the replayed bars) got read as if it were
/// this bar's current equity.
///
/// The Lot 12.6 fix added one more, ATAS-independent signal: a bar whose own timestamp sits more than
/// <paramref name="maxLiveGap"/> in the past relative to the real wall-clock instant cannot possibly be
/// today's live/forming bar. That signal is OR-ed with the heuristic, so it can only ever ADD
/// correctly-detected Replay bars, never remove one the heuristic already got right - safe, but strictly
/// one-directional: it can never turn a heuristic false positive back into "live".
///
/// Lot 12.10 captured exactly that residual failure mode against a real, non-"Replay" ATAS account: the
/// bar-index heuristic reported Replay (heuristicIsReplay=true) on a bar collected at real-time cadence
/// against a genuinely connected account (Portfolio.IsReplay()==false, a real AccountID, a non-zero,
/// distinct-from-InitialCapital Balance) - the OR-only design could not let that native ATAS signal
/// override the heuristic's false positive, so Equity stayed routed to the (empty) Replay series.
///
/// Lot 12.11 adds <paramref name="portfolioIsReplay"/> - Portfolio.IsReplay() (ATAS.DataFeedsCore
/// .Extensions, confirmed by reflection to check Portfolio.AccountID == "Replay" exactly) - as the
/// highest-priority signal, evidence hierarchy per the Lot 12.11 brief: (1) Portfolio.IsReplay() when
/// available, ATAS-native, wins outright when it says Replay; (2) the wall-clock gap remains a
/// last-resort guard that still fires even when Portfolio says "not Replay" - this is deliberate, not an
/// oversight: it is exactly what stops a real/demo account that stays selected while the CHART replays
/// old historical bars from having "today's" Realtime.Equity misread as if it were that old bar's
/// equity, the same look-ahead risk Lot 12.6 exists to prevent, just from the opposite direction; (3)
/// only once Portfolio.IsReplay() says "not Replay" AND the bar is recent does that native signal
/// override the old heuristic (the Lot 12.10 fix itself); (4) when Portfolio.IsReplay() is unavailable
/// (null - TradingManager/Portfolio inaccessible) the method falls back to the exact Lot 12.6 formula,
/// unchanged - provable bit-for-bit equivalence, see ATASEquityReplayDetectorTests
/// .NonRegression_PortfolioSignalAbsent_MatchesExactPriorFormula (Lot 12.11).
///
/// Pure, deterministic, no ATAS types involved beyond the DateTime/TimeSpan/bool?/bool primitives -
/// independently testable without instantiating IQIAIndicator (which requires a live ATAS host and
/// cannot be unit-tested).
/// </summary>
public static class ATASEquityReplayDetector
{
    public static readonly TimeSpan DefaultMaxLiveGap = TimeSpan.FromHours(24);

    /// <summary>Resolves whether Equity should be read from the Replay or the Realtime statistics
    /// stream. Evidence hierarchy (highest to lowest trust - see the class doc comment for the full
    /// rationale): (1) <paramref name="portfolioIsReplay"/> == true -&gt; Replay, unconditionally; (2)
    /// the bar is older than <paramref name="maxLiveGap"/> (default 24h) relative to
    /// <paramref name="utcNow"/> -&gt; Replay, unconditionally (this guard is never overridden by any
    /// other signal, including a "not Replay" Portfolio reading - see the class doc comment); (3)
    /// <paramref name="portfolioIsReplay"/> == false -&gt; Realtime (the Lot 12.11 fix: the old
    /// <paramref name="heuristicIsReplay"/> heuristic can no longer force Replay once ATAS's own
    /// Portfolio.IsReplay() has confirmed a live, non-Replay account on a recent bar); (4)
    /// <paramref name="portfolioIsReplay"/> == null (unavailable) -&gt; falls back to the original Lot
    /// 12.6 formula (<paramref name="heuristicIsReplay"/> OR wall-clock gap), unchanged.</summary>
    public static bool IsReplayContext(
        bool heuristicIsReplay,
        DateTime barTime,
        DateTime utcNow,
        bool? portfolioIsReplay = null,
        TimeSpan? maxLiveGap = null)
    {
        if (portfolioIsReplay == true)
            return true;

        TimeSpan margin = maxLiveGap ?? DefaultMaxLiveGap;
        if ((utcNow - barTime) > margin)
            return true;

        if (portfolioIsReplay == false)
            return false;

        return heuristicIsReplay;
    }
}
