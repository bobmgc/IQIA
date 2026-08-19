using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using ATAS.DataFeedsCore;
using ATAS.DataFeedsCore.Statistics;
using IQIAIndicator.Core;
using IQIAIndicator.Engine.Risk;
using IQIAIndicator.Infrastructure.ATAS;
using Utils.Common.Collections;
using Xunit;

namespace IQIAIndicator.Tests.Infrastructure.ATAS;

/// <summary>
/// Sprint 15.25 (Lot 12.12, Problem A/C). Coverage for AtasDataContextResolver - the pure mapping from
/// ATASEquityReplayDetector.IsReplayContext's boolean (Lot 12.6/12.11, unchanged) to the explicit,
/// dashboard-facing AtasDataContext every consumer now reads (DashboardManager/DebugDashboard/
/// DatasetDashboard - see the Lot 12.12 report, Problem A/C: before this lot, those consumers read the
/// raw, independently unreliable ExecutionContext.IsReplay heuristic directly, completely disconnected
/// from the corrected signal already used to select Equity's source).
/// </summary>
public sealed class AtasDataContextResolverTests
{
    [Fact]
    public void Resolve_NotYetResolved_ReturnsUnknown()
    {
        Assert.Equal(AtasDataContext.Unknown, AtasDataContextResolver.Resolve(hasBeenResolved: false, isReplay: false));
        Assert.Equal(AtasDataContext.Unknown, AtasDataContextResolver.Resolve(hasBeenResolved: false, isReplay: true));
    }

    [Fact]
    public void Resolve_ResolvedReplay_ReturnsReplay()
    {
        Assert.Equal(AtasDataContext.Replay, AtasDataContextResolver.Resolve(hasBeenResolved: true, isReplay: true));
    }

    [Fact]
    public void Resolve_ResolvedLive_ReturnsLive()
    {
        Assert.Equal(AtasDataContext.Live, AtasDataContextResolver.Resolve(hasBeenResolved: true, isReplay: false));
    }

    // ── Section 12 (Lot 12.12 brief) - mandatory integration test ────────────────────────────────────
    // "Créer un test qui simule exactement les deux situations observées dans les captures." Reuses
    // ATASEquityReplayDetector (Lot 12.6/12.11, unchanged) end-to-end with AtasDataContextResolver -
    // never a new decision, only the explicit Context label these two scenarios must resolve to.

    private static Portfolio BuildPortfolio(string accountId) => new() { AccountID = accountId, IsRealAccount = false };

    private sealed class FakeMutableEnumerable<T> : IMutableEnumerable<T>
    {
        private readonly List<T> _values;
        public FakeMutableEnumerable(params T[] values) => _values = new List<T>(values);
        public IEnumerator<T> GetEnumerator() => _values.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        public event Action<T>? Added { add { } remove { } }
        public event Action<T>? Changed { add { } remove { } }
        public event Action<T>? Removed { add { } remove { } }
        public event Action? Cleared { add { } remove { } }
    }

    private sealed class FakeTradingStatistics : ITradingStatistics
    {
        public required IMutableEnumerable<EquityValue> Equity { get; init; }
        public IMutableEnumerable<Order> Orders => new FakeMutableEnumerable<Order>();
        public IMutableEnumerable<MyTrade> MyTrades => new FakeMutableEnumerable<MyTrade>();
        public IMutableEnumerable<HistoryMyTrade> HistoryMyTrades => new FakeMutableEnumerable<HistoryMyTrade>();
        public IMutableEnumerable<IStatisticsParameterGroup> Statistics => new FakeMutableEnumerable<IStatisticsParameterGroup>();
    }

    private sealed class FakeTradingStatisticsProvider : ITradingStatisticsProvider
    {
        public required ITradingStatistics Realtime { get; init; }
        public required ITradingStatistics Replay { get; init; }
        public Task<ITradingStatistics> LoadHistoryAsync(DateTime from, DateTime to, ICollection<string>? accounts = null, ICollection<string>? securities = null) =>
            throw new NotSupportedException("Not used by these tests.");
    }

    [Fact]
    public void ScenarioA_Replay_PortfolioReplayTrue_ExecutionReplayFalse_ExecutionRealtimeTrue()
    {
        // Portfolio.IsReplay=true, Execution.IsReplay=false, Execution.IsRealtime=true (brief, Section
        // 12). Expected: Context=Replay, EquitySource=Replay, Realtime Equity never consulted.
        var barTime = new DateTime(2026, 8, 2, 22, 0, 0, DateTimeKind.Utc);
        var utcNow = new DateTime(2026, 8, 18, 14, 0, 0, DateTimeKind.Utc);
        var provider = new FakeTradingStatisticsProvider
        {
            Realtime = new FakeTradingStatistics { Equity = new FakeMutableEnumerable<EquityValue>(new EquityValue("MES", utcNow, 999999m, 999999m)) },
            Replay = new FakeTradingStatistics { Equity = new FakeMutableEnumerable<EquityValue>(new EquityValue("MES", barTime, 25010m, 25010m)) }
        };

        bool isReplay = ATASEquityReplayDetector.IsReplayContext(
            heuristicIsReplay: false, barTime, utcNow, portfolioIsReplay: true);
        AtasDataContext context = AtasDataContextResolver.Resolve(hasBeenResolved: true, isReplay);

        Assert.Equal(AtasDataContext.Replay, context);

        decimal? equity = ATASAccountStateAdapter.TryGetCurrentEquity(provider, isReplay);
        Assert.Equal(25010m, equity);
        Assert.NotEqual(999999m, equity); // Realtime Equity never consulted/used.
    }

    [Fact]
    public void ScenarioB_Live_PortfolioReplayFalse_ExecutionReplayFalse_ExecutionRealtimeTrue()
    {
        // Portfolio.IsReplay=false, Execution.IsReplay=false, Execution.IsRealtime=true (brief, Section
        // 12). Expected: Context=Live, EquitySource=Realtime, Replay Equity never consulted.
        var barTime = new DateTime(2026, 8, 18, 13, 55, 0, DateTimeKind.Utc);
        var utcNow = new DateTime(2026, 8, 18, 14, 0, 0, DateTimeKind.Utc);
        var provider = new FakeTradingStatisticsProvider
        {
            Realtime = new FakeTradingStatistics { Equity = new FakeMutableEnumerable<EquityValue>(new EquityValue("MES", utcNow, 25103.92m, 25103.92m)) },
            Replay = new FakeTradingStatistics { Equity = new FakeMutableEnumerable<EquityValue>(new EquityValue("MES", barTime, 111111m, 111111m)) }
        };

        bool isReplay = ATASEquityReplayDetector.IsReplayContext(
            heuristicIsReplay: false, barTime, utcNow, portfolioIsReplay: false);
        AtasDataContext context = AtasDataContextResolver.Resolve(hasBeenResolved: true, isReplay);

        Assert.Equal(AtasDataContext.Live, context);

        decimal? equity = ATASAccountStateAdapter.TryGetCurrentEquity(provider, isReplay);
        Assert.Equal(25103.92m, equity);
        Assert.NotEqual(111111m, equity); // Replay Equity never consulted/used.
    }
}
