using System.Linq;
using ATAS.DataFeedsCore;
using ATAS.DataFeedsCore.Statistics;
using IQIAIndicator.Engine.Risk;

namespace IQIAIndicator.Infrastructure.ATAS;

/// <summary>
/// Sprint 15.25 (Lot 12.2). Read-only mapping from ATAS's own trading-account APIs (confirmed by the
/// Lot 12.1 reflection audit: <c>Indicator.TradingManager.Portfolio</c> and
/// <c>Indicator.TradingStatisticsProvider.Realtime/.Replay.Equity</c>) to <see cref="AccountState"/>
/// (Lot 10, unchanged). Contains no strategy/risk logic - pure field mapping.
///
/// Equity is the one field this lot treats as CRITICAL (Lot 12.2, Section 5): ATAS never exposes a
/// scalar Portfolio.Equity (Lot 12.1 finding - never invented here), only a growing time series
/// (ITradingStatistics.Equity, an EquityValue per update); TryGetCurrentEquity reads its latest point.
/// When that series is empty/unavailable, Build must NEVER fall back to Balance, InitialCapital, a
/// stale prior reading, or a hardcoded number - it resolves CurrentEquity to 0m, the exact same
/// "unconfigured" sentinel AccountState/RiskEngine already use everywhere else (Lot 10/11): RiskEngine's
/// existing CurrentEquity &gt; 0 check (unchanged) then rejects with INVALID_EQUITY, never ACCEPTED. This
/// is a deliberate, minimal reuse of an already-existing gate, not a new fallback value - see the Lot
/// 12.2 report for the full reasoning (AccountState.CurrentEquity is a non-nullable decimal and is a
/// protected file this lot must not change).
///
/// InitialCapital/PeakEquity/DailyStartingEquity/DailyPnL/RiskUsedToday/OpenRisk have no ATAS equivalent
/// (Lot 12.1) and remain explicit user configuration (Lot 11's Risk* parameters), passed through
/// unchanged by the caller - this adapter never invents or overrides them.
/// </summary>
public static class ATASAccountStateAdapter
{
    /// <summary>Latest point on ATAS's own equity curve for the given statistics stream (Realtime or
    /// Replay, selected by the caller via <paramref name="isReplay"/>), or null if genuinely
    /// unavailable (no provider, no stream for this mode, or an empty curve so far).</summary>
    public static decimal? TryGetCurrentEquity(ITradingStatisticsProvider? statisticsProvider, bool isReplay)
    {
        ITradingStatistics? statistics = statisticsProvider is null
            ? null
            : isReplay ? statisticsProvider.Replay : statisticsProvider.Realtime;

        // EquityValue is a value type (struct) - LastOrDefault() would otherwise return a
        // default(EquityValue) indistinguishable from a genuine reading, so emptiness is checked
        // explicitly via Any() rather than relying on a default-value sentinel.
        if (statistics is null || !statistics.Equity.Any())
            return null;

        return statistics.Equity.Last().Equity;
    }

    public static AccountState Build(
        Portfolio? portfolio,
        decimal? currentEquity,
        decimal initialCapital,
        decimal peakEquity,
        decimal dailyStartingEquity,
        decimal dailyPnL,
        decimal riskUsedToday,
        decimal openRisk)
    {
        decimal resolvedEquity = currentEquity ?? 0m;
        decimal? currentBalance = portfolio?.Balance;

        return new AccountState(
            initialCapital,
            resolvedEquity,
            currentBalance,
            peakEquity,
            dailyStartingEquity,
            dailyPnL,
            riskUsedToday,
            openRisk);
    }
}
