using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using ATAS.DataFeedsCore;
using ATAS.DataFeedsCore.Statistics;
using IQIAIndicator.Engine.Risk;
using IQIAIndicator.Infrastructure.ATAS;
using Utils.Common.Collections;
using Xunit;

namespace IQIAIndicator.Tests.Infrastructure.ATAS;

/// <summary>
/// Sprint 15.25 (Lot 12.6). Coverage for ATASEquityReplayDetector - the new, pure, ATAS-independent
/// signal that replaces context.Execution.IsReplay (Core.MarketContextBuilder's in-house bar-index
/// heuristic) for Equity source selection only. A real MES Replay capture
/// (ScientificDataset_MES_M5_20260817_175446) proved that heuristic misclassifies genuinely-replayed
/// bars as "Realtime" for 1104 of 2304 bars, causing a single stale Realtime.Equity point (105.50,
/// timestamped 2026-08-10T14:02:10 - days after the replayed bars, 2026-07-27..2026-08-07) to be read as
/// if it were current. These tests prove the fix directly against that scenario, and that
/// ATASAccountStateAdapter.cs itself (Lot 12.2, protected) needs no change - only the isReplay argument
/// handed to its unchanged TryGetCurrentEquity is corrected.
/// </summary>
public sealed class ATASEquityReplayDetectorTests
{
    // ── Pure logic (no ATAS types involved) ──────────────────────────────────────────────────────

    [Fact]
    public void IsReplayContext_HeuristicAlreadyTrue_ReturnsTrue_RegardlessOfBarTime()
    {
        var now = new DateTime(2026, 8, 17, 12, 0, 0, DateTimeKind.Utc);

        bool result = ATASEquityReplayDetector.IsReplayContext(
            heuristicIsReplay: true, barTime: now, utcNow: now);

        Assert.True(result, "Never regresses below the existing heuristic's own correct detections.");
    }

    [Fact]
    public void IsReplayContext_HeuristicFalse_BarTimeIsNow_ReturnsFalse()
    {
        // Genuine live trading: the newest bar's timestamp is essentially "now".
        var now = new DateTime(2026, 8, 17, 12, 0, 0, DateTimeKind.Utc);

        bool result = ATASEquityReplayDetector.IsReplayContext(
            heuristicIsReplay: false, barTime: now, utcNow: now);

        Assert.False(result);
    }

    [Fact]
    public void IsReplayContext_HeuristicFalse_BarTimeMinutesOld_ReturnsFalse()
    {
        // A still-forming live bar: its own Time (typically bar-open) can trail "now" by up to one
        // bar period - must never be misclassified as Replay for any realistic intraday timeframe.
        var utcNow = new DateTime(2026, 8, 17, 12, 4, 0, DateTimeKind.Utc);
        var barTime = new DateTime(2026, 8, 17, 12, 0, 0, DateTimeKind.Utc); // M5 bar, 4 min into the bar

        bool result = ATASEquityReplayDetector.IsReplayContext(
            heuristicIsReplay: false, barTime: barTime, utcNow: utcNow);

        Assert.False(result);
    }

    [Fact]
    public void IsReplayContext_HeuristicFalse_BarTimeThirteenDaysOld_ReturnsTrue()
    {
        // Mirrors the exact real MES capture gap: equity dated 2026-08-10, bars replayed from 2026-07-28.
        var utcNow = new DateTime(2026, 8, 10, 14, 2, 10, DateTimeKind.Utc);
        var barTime = new DateTime(2026, 7, 28, 4, 55, 0, DateTimeKind.Utc);

        bool result = ATASEquityReplayDetector.IsReplayContext(
            heuristicIsReplay: false, barTime: barTime, utcNow: utcNow);

        Assert.True(result, "A 13-day-old bar cannot be today's live bar - this is exactly the real capture's failure mode.");
    }

    [Theory]
    [InlineData(23, 59, false)] // just under the 24h margin
    [InlineData(24, 1, true)]   // just over the 24h margin
    public void IsReplayContext_RespectsTheConfiguredMargin(int hours, int minutes, bool expected)
    {
        var utcNow = new DateTime(2026, 8, 17, 12, 0, 0, DateTimeKind.Utc);
        DateTime barTime = utcNow - new TimeSpan(hours, minutes, 0);

        bool result = ATASEquityReplayDetector.IsReplayContext(
            heuristicIsReplay: false, barTime: barTime, utcNow: utcNow);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void IsReplayContext_CustomMargin_IsHonored()
    {
        var utcNow = new DateTime(2026, 8, 17, 12, 0, 0, DateTimeKind.Utc);
        var barTime = utcNow - TimeSpan.FromHours(2);

        Assert.False(ATASEquityReplayDetector.IsReplayContext(false, barTime, utcNow, maxLiveGap: TimeSpan.FromHours(6)));
        Assert.True(ATASEquityReplayDetector.IsReplayContext(false, barTime, utcNow, maxLiveGap: TimeSpan.FromHours(1)));
    }

    // ── Integration with ATASAccountStateAdapter.TryGetCurrentEquity (Lot 12.2, protected, unchanged) ──
    // Mirrors ATASRuntimeDiagnosticsTests.cs's fixture pattern (Lot 12.3).

    private static Portfolio BuildPortfolio(string accountId, decimal balance) =>
        new() { AccountID = accountId, Balance = balance, IsRealAccount = false };

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
    public void RealCaptureScenario_StaleRealtimeEquity_IsNotUsed_WhenBarIsHistorical()
    {
        // Exact shape of the real MES capture: Replay.Equity empty, Realtime.Equity has one stale point
        // (105.50) dated days after the bar being processed. The OLD heuristic said "Realtime"
        // (heuristicIsReplay: false) for this bar - reproducing the bug before the fix.
        var provider = new FakeTradingStatisticsProvider
        {
            Realtime = new FakeTradingStatistics { Equity = new FakeMutableEnumerable<EquityValue>(new EquityValue("MES", new DateTime(2026, 8, 10, 14, 2, 10, DateTimeKind.Utc), 105.50m, 105.50m)) },
            Replay = new FakeTradingStatistics { Equity = new FakeMutableEnumerable<EquityValue>() }
        };
        var barTime = new DateTime(2026, 7, 28, 4, 55, 0, DateTimeKind.Utc);
        var utcNow = new DateTime(2026, 8, 10, 14, 2, 10, DateTimeKind.Utc);

        bool correctedIsReplay = ATASEquityReplayDetector.IsReplayContext(heuristicIsReplay: false, barTime, utcNow);
        decimal? equity = ATASAccountStateAdapter.TryGetCurrentEquity(provider, correctedIsReplay);

        Assert.True(correctedIsReplay, "The wall-clock signal must detect this bar as Replay even though the old heuristic did not.");
        // Once correctly routed to Replay.Equity (empty), TryGetCurrentEquity must return null - never
        // the stale 105.50 Realtime point.
        Assert.Null(equity);

        AccountState account = ATASAccountStateAdapter.Build(BuildPortfolio("ACC-1", 25000m), equity,
            initialCapital: 25000m, peakEquity: 25000m, dailyStartingEquity: 25000m, dailyPnL: 0m, riskUsedToday: 0m, openRisk: 0m);
        Assert.Equal(0m, account.CurrentEquity);
    }

    [Fact]
    public void RealCaptureScenario_WithoutTheFix_TheStaleEquityWouldHaveBeenUsed()
    {
        // Same fixture as above, but using the OLD (uncorrected) heuristic directly - documents exactly
        // what the bug looked like, so a future reader can see the before/after side by side.
        var provider = new FakeTradingStatisticsProvider
        {
            Realtime = new FakeTradingStatistics { Equity = new FakeMutableEnumerable<EquityValue>(new EquityValue("MES", new DateTime(2026, 8, 10, 14, 2, 10, DateTimeKind.Utc), 105.50m, 105.50m)) },
            Replay = new FakeTradingStatistics { Equity = new FakeMutableEnumerable<EquityValue>() }
        };

        decimal? equityWithOldHeuristic = ATASAccountStateAdapter.TryGetCurrentEquity(provider, isReplay: false);

        Assert.Equal(105.50m, equityWithOldHeuristic);
    }

    [Fact]
    public void GenuineLiveEquity_IsStillUsedCorrectly()
    {
        var utcNow = new DateTime(2026, 8, 17, 12, 5, 0, DateTimeKind.Utc);
        var barTime = new DateTime(2026, 8, 17, 12, 0, 0, DateTimeKind.Utc);
        var provider = new FakeTradingStatisticsProvider
        {
            Realtime = new FakeTradingStatistics { Equity = new FakeMutableEnumerable<EquityValue>(new EquityValue("MES", utcNow, 24850m, 24850m)) },
            Replay = new FakeTradingStatistics { Equity = new FakeMutableEnumerable<EquityValue>() }
        };

        bool correctedIsReplay = ATASEquityReplayDetector.IsReplayContext(heuristicIsReplay: false, barTime, utcNow);
        decimal? equity = ATASAccountStateAdapter.TryGetCurrentEquity(provider, correctedIsReplay);

        Assert.False(correctedIsReplay);
        Assert.Equal(24850m, equity);
    }

    [Fact]
    public void GenuineReplayEquity_WhenPopulated_IsUsedCorrectly()
    {
        // Replay.Equity genuinely populated with a point contemporaneous with the replayed bar.
        var barTime = new DateTime(2026, 7, 28, 4, 55, 0, DateTimeKind.Utc);
        var utcNow = new DateTime(2026, 8, 17, 12, 0, 0, DateTimeKind.Utc);
        var provider = new FakeTradingStatisticsProvider
        {
            Realtime = new FakeTradingStatistics { Equity = new FakeMutableEnumerable<EquityValue>() },
            Replay = new FakeTradingStatistics { Equity = new FakeMutableEnumerable<EquityValue>(new EquityValue("MES", barTime, 25010m, 25010m)) }
        };

        bool correctedIsReplay = ATASEquityReplayDetector.IsReplayContext(heuristicIsReplay: true, barTime, utcNow);
        decimal? equity = ATASAccountStateAdapter.TryGetCurrentEquity(provider, correctedIsReplay);

        Assert.True(correctedIsReplay);
        Assert.Equal(25010m, equity);
    }

    [Fact]
    public void NoFallbackFromReplayToRealtime_WhenReplayIsEmpty()
    {
        // Genuinely in Replay (heuristic agrees), Replay.Equity empty, Realtime.Equity has a value -
        // must NOT silently use Realtime.
        var barTime = new DateTime(2026, 7, 28, 4, 55, 0, DateTimeKind.Utc);
        var utcNow = new DateTime(2026, 8, 17, 12, 0, 0, DateTimeKind.Utc);
        var provider = new FakeTradingStatisticsProvider
        {
            Realtime = new FakeTradingStatistics { Equity = new FakeMutableEnumerable<EquityValue>(new EquityValue("MES", utcNow, 999999m, 999999m)) },
            Replay = new FakeTradingStatistics { Equity = new FakeMutableEnumerable<EquityValue>() }
        };

        bool correctedIsReplay = ATASEquityReplayDetector.IsReplayContext(heuristicIsReplay: true, barTime, utcNow);
        decimal? equity = ATASAccountStateAdapter.TryGetCurrentEquity(provider, correctedIsReplay);

        Assert.Null(equity);
        Assert.NotEqual(999999m, equity);
    }

    // ── Lot 12.11: Portfolio.IsReplay() wired into the decision (previously observability-only) ──────
    // Evidence hierarchy: Portfolio.IsReplay()==true > wall-clock gap > Portfolio.IsReplay()==false >
    // heuristicIsReplay (only consulted when Portfolio.IsReplay() is unavailable). See
    // ATASEquityReplayDetector.cs's doc comment for the full rationale, including why the wall-clock
    // guard is deliberately never overridden by a "live" Portfolio reading.

    [Fact]
    public void Test1_PortfolioConfirmsReplay_BothSignalsAgree_ReturnsTrue()
    {
        var now = new DateTime(2026, 8, 18, 12, 0, 0, DateTimeKind.Utc);

        bool result = ATASEquityReplayDetector.IsReplayContext(
            heuristicIsReplay: true, barTime: now, utcNow: now, portfolioIsReplay: true);

        Assert.True(result);
    }

    [Fact]
    public void Test2_PortfolioConfirmsLive_RecentBar_ReturnsFalse()
    {
        var now = new DateTime(2026, 8, 18, 12, 0, 0, DateTimeKind.Utc);

        bool result = ATASEquityReplayDetector.IsReplayContext(
            heuristicIsReplay: false, barTime: now, utcNow: now, portfolioIsReplay: false);

        Assert.False(result);
    }

    [Fact]
    public void Test3_Lot1210Scenario_PortfolioLiveOverridesHeuristicFalsePositive()
    {
        // Exact shape of the LOT 12.10 capture: Portfolio.IsReplay()==false (real, non-"Replay" account,
        // a genuine AccountID and a non-zero Balance distinct from InitialCapital were observed), a
        // recent bar (barTime/utcNow ~1h12m apart, matching the capture's own collection window), but
        // the old bar-index heuristic (context.Execution.IsReplay) still reported Replay - this is
        // precisely the bug this lot fixes.
        var barTime = new DateTime(2026, 8, 18, 13, 50, 0, DateTimeKind.Utc);
        var utcNow = new DateTime(2026, 8, 18, 14, 52, 33, DateTimeKind.Utc);

        bool result = ATASEquityReplayDetector.IsReplayContext(
            heuristicIsReplay: true, barTime: barTime, utcNow: utcNow, portfolioIsReplay: false);

        Assert.False(result, "A live, non-Replay Portfolio confirmed by ATAS must override the old heuristic's false positive (Lot 12.10 capture).");
    }

    [Fact]
    public void Test4_PortfolioConfirmsReplay_HeuristicDisagrees_ReplayStaysPriority()
    {
        var now = new DateTime(2026, 8, 18, 12, 0, 0, DateTimeKind.Utc);

        bool result = ATASEquityReplayDetector.IsReplayContext(
            heuristicIsReplay: false, barTime: now, utcNow: now, portfolioIsReplay: true);

        Assert.True(result, "Portfolio.IsReplay()==true must win even when the heuristic disagrees - the symmetric case to Test 3.");
    }

    [Fact]
    public void Test5_PortfolioUnavailable_OldBar_NeverInventsLive_WallClockGuardStillFires()
    {
        // Ambiguous case: no native Portfolio signal at all. Must never default to "confidently live" -
        // the pre-existing wall-clock guard (Lot 12.6) still protects against a stale-equity look-ahead.
        var utcNow = new DateTime(2026, 8, 18, 12, 0, 0, DateTimeKind.Utc);
        var barTime = utcNow - TimeSpan.FromDays(13);

        bool result = ATASEquityReplayDetector.IsReplayContext(
            heuristicIsReplay: false, barTime: barTime, utcNow: utcNow, portfolioIsReplay: null);

        Assert.True(result, "With no native signal available, an old bar must still classify as Replay - no equity is ever invented as 'live' by default.");
    }

    [Fact]
    public void WallClockGuard_FiresEvenWhenPortfolioConfirmsLive_OnAnOldBar()
    {
        // Deliberate design property, not covered by the brief's 8 numbered tests but essential to the
        // fix's safety: a real/demo account can remain selected while the CHART replays old historical
        // bars. Portfolio.IsReplay()==false alone must NOT be enough to route an old bar to
        // Realtime.Equity - that would silently reintroduce exactly the look-ahead bug Lot 12.6 exists to
        // prevent, just from the opposite direction (a live account's present-day balance misread as an
        // old bar's equity).
        var utcNow = new DateTime(2026, 8, 18, 12, 0, 0, DateTimeKind.Utc);
        var barTime = utcNow - TimeSpan.FromDays(13);

        bool result = ATASEquityReplayDetector.IsReplayContext(
            heuristicIsReplay: false, barTime: barTime, utcNow: utcNow, portfolioIsReplay: false);

        Assert.True(result, "The wall-clock guard must never be overridden by a 'live' Portfolio reading on an old bar.");
    }

    [Fact]
    public void Test6_PortfolioConfirmsLive_RealtimeEquityAbsent_ResolvesFailClosed()
    {
        var utcNow = new DateTime(2026, 8, 18, 14, 0, 0, DateTimeKind.Utc);
        var barTime = new DateTime(2026, 8, 18, 13, 55, 0, DateTimeKind.Utc);
        var provider = new FakeTradingStatisticsProvider
        {
            Realtime = new FakeTradingStatistics { Equity = new FakeMutableEnumerable<EquityValue>() },
            Replay = new FakeTradingStatistics { Equity = new FakeMutableEnumerable<EquityValue>() }
        };

        bool correctedIsReplay = ATASEquityReplayDetector.IsReplayContext(
            heuristicIsReplay: true, barTime, utcNow, portfolioIsReplay: false);
        decimal? equity = ATASAccountStateAdapter.TryGetCurrentEquity(provider, correctedIsReplay);

        Assert.False(correctedIsReplay, "LIVE must still be selected even though Realtime.Equity turns out empty.");
        Assert.Null(equity);

        AccountState account = ATASAccountStateAdapter.Build(BuildPortfolio("REAL-ACC-1", 25103.92m), equity,
            initialCapital: 25000m, peakEquity: 25000m, dailyStartingEquity: 25000m, dailyPnL: 0m, riskUsedToday: 0m, openRisk: 0m);
        Assert.Equal(0m, account.CurrentEquity);
    }

    [Fact]
    public void Test7_PortfolioConfirmsLive_RealtimeEquityAvailable_ValueFlowsThrough()
    {
        var utcNow = new DateTime(2026, 8, 18, 14, 0, 0, DateTimeKind.Utc);
        var barTime = new DateTime(2026, 8, 18, 13, 55, 0, DateTimeKind.Utc);
        var provider = new FakeTradingStatisticsProvider
        {
            Realtime = new FakeTradingStatistics { Equity = new FakeMutableEnumerable<EquityValue>(new EquityValue("MES", utcNow, 25103.92m, 25103.92m)) },
            Replay = new FakeTradingStatistics { Equity = new FakeMutableEnumerable<EquityValue>() }
        };

        bool correctedIsReplay = ATASEquityReplayDetector.IsReplayContext(
            heuristicIsReplay: true, barTime, utcNow, portfolioIsReplay: false);
        decimal? equity = ATASAccountStateAdapter.TryGetCurrentEquity(provider, correctedIsReplay);

        Assert.False(correctedIsReplay);
        Assert.Equal(25103.92m, equity);

        AccountState account = ATASAccountStateAdapter.Build(BuildPortfolio("REAL-ACC-1", 25103.92m), equity,
            initialCapital: 25000m, peakEquity: 25103.92m, dailyStartingEquity: 25000m, dailyPnL: 0m, riskUsedToday: 0m, openRisk: 0m);
        Assert.Equal(25103.92m, account.CurrentEquity);
    }

    [Theory]
    [InlineData(true, 0, 0, true)]     // heuristic true, bar is "now" -> old formula: true
    [InlineData(false, 0, 0, false)]   // heuristic false, bar is "now" -> old formula: false
    [InlineData(true, 25 * 24, 0, true)]   // heuristic true, bar 25 days old -> old formula: true (short-circuit)
    [InlineData(false, 25 * 24, 0, true)]  // heuristic false, bar 25 days old -> old formula: true (gap wins)
    [InlineData(false, 23, 59, false)]     // heuristic false, just under 24h -> old formula: false
    [InlineData(false, 24, 1, true)]       // heuristic false, just over 24h -> old formula: true
    public void Test8_NonRegression_PortfolioSignalAbsent_MatchesExactPriorFormula(
        bool heuristicIsReplay, int gapHours, int gapMinutes, bool expected)
    {
        var utcNow = new DateTime(2026, 8, 18, 12, 0, 0, DateTimeKind.Utc);
        DateTime barTime = utcNow - new TimeSpan(gapHours, gapMinutes, 0);

        // Old (Lot 12.6) formula, computed independently here for comparison, not reused from production
        // code: heuristicIsReplay || (utcNow - barTime) > DefaultMaxLiveGap.
        bool oldFormula = heuristicIsReplay || (utcNow - barTime) > ATASEquityReplayDetector.DefaultMaxLiveGap;
        Assert.Equal(expected, oldFormula); // sanity-check the test data itself

        bool result = ATASEquityReplayDetector.IsReplayContext(
            heuristicIsReplay, barTime, utcNow, portfolioIsReplay: null);

        Assert.Equal(oldFormula, result);
        Assert.Equal(expected, result);
    }

    // ── Phase G (Lot 12.11): full chain ATAS context -> Equity source selection ->
    // ATASAccountStateAdapter -> RiskEngineRequest -> RiskEngine.Evaluate ────────────────────────────

    [Fact]
    public void Integration_LiveConfirmed_NoLongerForcedToReplay_JustBecauseHeuristicIsTrue()
    {
        // Reproduces the LOT 12.10 scenario end-to-end: a real, non-Replay account confirmed by ATAS
        // (Portfolio.IsReplay()==false), a recent bar, a populated Realtime.Equity series, but the old
        // heuristic still reports Replay. Before this lot, SourceMode would stay "Replay" and
        // CurrentEquity would resolve to 0 (INVALID_EQUITY) regardless of the real account. After this
        // lot, with everything else nominally valid, RiskEngine must no longer reject on INVALID_EQUITY.
        var utcNow = new DateTime(2026, 8, 18, 14, 0, 0, DateTimeKind.Utc);
        var barTime = new DateTime(2026, 8, 18, 13, 55, 0, DateTimeKind.Utc);
        var provider = new FakeTradingStatisticsProvider
        {
            Realtime = new FakeTradingStatistics { Equity = new FakeMutableEnumerable<EquityValue>(new EquityValue("MES", utcNow, 25103.92m, 25103.92m)) },
            Replay = new FakeTradingStatistics { Equity = new FakeMutableEnumerable<EquityValue>() }
        };

        bool sourceIsReplay = ATASEquityReplayDetector.IsReplayContext(
            heuristicIsReplay: true, barTime, utcNow, portfolioIsReplay: false);
        Assert.False(sourceIsReplay, "SourceMode must resolve to Realtime, not Replay, in this scenario.");

        decimal? atasEquity = ATASAccountStateAdapter.TryGetCurrentEquity(provider, sourceIsReplay);
        AccountState account = ATASAccountStateAdapter.Build(BuildPortfolio("REAL-ACC-1", 25103.92m), atasEquity,
            initialCapital: 25000m, peakEquity: 25103.92m, dailyStartingEquity: 25000m, dailyPnL: 0m, riskUsedToday: 0m, openRisk: 0m);
        Assert.Equal(25103.92m, account.CurrentEquity);

        var instrument = new InstrumentRiskSpecification("MES", 0.25m, 1.25m, 5m, MinQuantity: 1, MaxQuantity: 10, QuantityStep: 1);
        var policy = new RiskPolicy(
            MaxRiskPerTradePercent: 0.01m, MaxRiskPerTradeAmount: null,
            MaxDailyLossPercent: null, MaxDailyLossAmount: null,
            MaxDrawdownPercent: null, MaxDrawdownAmount: null,
            MaxOpenRiskPercent: null, MaxOpenRiskAmount: null,
            MinRiskReward: null, MaxPositionSize: null);
        var request = new RiskEngineRequest(
            Direction: global::IQIAIndicator.Engine.Risk.TradeDirection.Buy,
            EntryPrice: 7727.50m,
            StopLoss: 7720.00m,
            TakeProfit: 7745.00m,
            Instrument: instrument,
            Account: account,
            Policy: policy);

        RiskAssessment assessment = new RiskEngine().Evaluate(request);

        Assert.DoesNotContain(RiskRejectionReason.INVALID_EQUITY, assessment.RejectionReasons);
    }

    [Fact]
    public void Integration_ReplayStillProtected_NoRegressionForAGenuineReplaySession()
    {
        // Non-regression companion to the test above: when Portfolio genuinely confirms Replay, the
        // empty Replay.Equity series must still resolve CurrentEquity to 0 and RiskEngine must still
        // reject with INVALID_EQUITY - exactly the LOT 12.5-12.9 behavior, unchanged.
        var utcNow = new DateTime(2026, 8, 18, 14, 0, 0, DateTimeKind.Utc);
        var barTime = new DateTime(2026, 8, 2, 22, 0, 0, DateTimeKind.Utc);
        var provider = new FakeTradingStatisticsProvider
        {
            Realtime = new FakeTradingStatistics { Equity = new FakeMutableEnumerable<EquityValue>(new EquityValue("MES", utcNow, 105.50m, 105.50m)) },
            Replay = new FakeTradingStatistics { Equity = new FakeMutableEnumerable<EquityValue>() }
        };

        bool sourceIsReplay = ATASEquityReplayDetector.IsReplayContext(
            heuristicIsReplay: true, barTime, utcNow, portfolioIsReplay: true);
        Assert.True(sourceIsReplay);

        decimal? atasEquity = ATASAccountStateAdapter.TryGetCurrentEquity(provider, sourceIsReplay);
        AccountState account = ATASAccountStateAdapter.Build(BuildPortfolio("Replay", 0m), atasEquity,
            initialCapital: 25000m, peakEquity: 25000m, dailyStartingEquity: 25000m, dailyPnL: 0m, riskUsedToday: 0m, openRisk: 0m);
        Assert.Equal(0m, account.CurrentEquity);

        var instrument = new InstrumentRiskSpecification("MES", 0.25m, 1.25m, 5m, MinQuantity: 0, MaxQuantity: 0, QuantityStep: 0);
        var policy = new RiskPolicy(
            MaxRiskPerTradePercent: null, MaxRiskPerTradeAmount: null,
            MaxDailyLossPercent: null, MaxDailyLossAmount: null,
            MaxDrawdownPercent: null, MaxDrawdownAmount: null,
            MaxOpenRiskPercent: null, MaxOpenRiskAmount: null,
            MinRiskReward: null, MaxPositionSize: null);
        var request = new RiskEngineRequest(
            Direction: global::IQIAIndicator.Engine.Risk.TradeDirection.Sell,
            EntryPrice: 7552.25m,
            StopLoss: null,
            TakeProfit: null,
            Instrument: instrument,
            Account: account,
            Policy: policy);

        RiskAssessment assessment = new RiskEngine().Evaluate(request);

        Assert.Contains(RiskRejectionReason.INVALID_EQUITY, assessment.RejectionReasons);
        Assert.Contains(RiskRejectionReason.INSTRUMENT_SPEC_INVALID, assessment.RejectionReasons);
        Assert.Contains(RiskRejectionReason.INVALID_STOP_LOSS, assessment.RejectionReasons);
    }
}
