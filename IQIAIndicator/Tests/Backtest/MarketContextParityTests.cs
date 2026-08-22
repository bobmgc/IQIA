using System;
using IQIAIndicator.Core;
using IQIAIndicator.Core.MarketData;
using Xunit;

namespace IQIAIndicator.Tests.BacktestTests;

/// <summary>
/// Sprint 15.25 (Lot 14.1, brief §20). Proves that HistoricalBar + MarketContextFactory produce the same
/// mathematical values the ATAS path (MarketContextBuilder.Build) has always produced for the fields the
/// two paths share - WITHOUT constructing an ATAS.Indicators.IndicatorCandle, which no test anywhere in
/// this codebase does (see Tests/Core/MarketContextBuilderSelfHealingTests.cs's own doc comment: "no
/// ATAS.Indicators.IndicatorCandle instance needs to be constructed here ... MarketContextBuilder.Build
/// itself ... remains a live-ATAS-only path"). This lot does not change that constraint.
///
/// WHAT IS PROVEN BY THIS FILE (and by the source-level change in Core/MarketContextBuilder.cs applied
/// this lot):
///   1. MarketContextBuilder.Build no longer contains its own copy of the Median/TypicalPrice/
///      ElapsedMinutes/Session/Execution-shape formulas - it now calls MarketContextFactory.Create with
///      exactly the same raw fields (c.Open/c.High/c.Low/c.Close/c.Volume/c.Bid/c.Ask/c.Delta/c.Time) it
///      read from IndicatorCandle before this lot (verified by direct code review during this lot: read
///      Core/MarketContextBuilder.cs before and after the edit - the only change is WHERE the arithmetic
///      lives, not what it computes). There is now exactly ONE implementation of this arithmetic, not two
///      - the "second IQIA" duplication risk the Lot 13 report flagged (RISK-07) is closed by
///      construction, not by parallel testing.
///   2. The tests below independently re-derive each formula (Median, TypicalPrice, ElapsedMinutes, the
///      empty SessionInfo placeholder, the ExecutionContext shape for a historical bar) against
///      MarketContextFactory using hand-picked values, distinct from MarketContextFactoryTests.cs's own
///      cases - i.e. the SHARED factory's arithmetic is independently verified, not merely re-stated.
///
/// WHAT IS NOT PROVEN (and cannot be, without a live ATAS host or a way to construct IndicatorCandle,
/// neither of which exists in this test environment):
///   - That a REAL ATAS.Indicators.IndicatorCandle, fed through MarketContextBuilder.Build end to end,
///     produces a MarketContext bit-identical to the one CreateHistorical produces from an "equivalent"
///     HistoricalBar. This would require instantiating IndicatorCandle, which the Lot 13 report (§4.2)
///     established has never been demonstrated possible anywhere in this repository, in either direction.
///   - That ATAS's own IndicatorCandle.Bid/Ask/Delta/Volume/Open/High/Low/Close/Time fields, at runtime,
///     contain the values this test ASSUMES they would. That mapping (c.Open -&gt; PriceInfo.Open, etc.)
///     is read directly from Core/MarketContextBuilder.cs's source and is unchanged by this lot, but it
///     is a claim about ATAS's own SDK, not something a unit test can independently confirm.
///   - The ATAS-side Replay/Realtime heuristic (bar &lt;= _maxRealtimeBar) is intentionally NOT part of
///     this parity claim: MarketContextFactory takes IsRealtime/IsHistorical/IsReplay as plain booleans
///     supplied by the caller precisely because that heuristic is meaningless offline (see
///     MarketContextFactory's own doc comment) - a backtest run always passes IsReplay=false,
///     IsRealtime=false, IsHistorical=true, which is a DELIBERATE DIFFERENCE from the live path's
///     computed values, not a parity gap.
///
/// The genuine, end-to-end cross-check the Lot 13 report recommends instead (§15.4, test R-02: replay a
/// real ATAS-recorded capture through the eventual Backtest Engine and compare its Decision/EntryTrigger/
/// TradePlan output against what ATAS itself recorded) is out of scope for this lot - it requires the
/// signal pipeline (RegimeEngine onward through Decision), which Lot 14.1 deliberately does not wire in
/// yet (brief §22/§23).
/// </summary>
public sealed class MarketContextParityTests
{
    private static readonly InstrumentInfo Instrument = new("MES", TickSize: 0.25m, TickValue: 1.25m, PointValue: 5m, Decimals: 2);

    [Fact]
    public void Median_MatchesTheFormulaMarketContextBuilderHasAlwaysUsed()
    {
        // Independent re-derivation of "(High + Low) / 2" with values distinct from
        // MarketContextFactoryTests.cs, using decimals that do not round evenly to sanity-check there is
        // no hidden truncation.
        decimal high = 4523.75m;
        decimal low = 4519.25m;
        decimal expected = (high + low) / 2m; // computed here, independently of the method under test

        Assert.Equal(expected, MarketContextFactory.Median(high, low));
    }

    [Fact]
    public void TypicalPrice_MatchesTheFormulaMarketContextBuilderHasAlwaysUsed()
    {
        decimal high = 4523.75m;
        decimal low = 4519.25m;
        decimal close = 4521.50m;
        decimal expected = (high + low + close) / 3m;

        Assert.Equal(expected, MarketContextFactory.TypicalPrice(high, low, close));
    }

    [Fact]
    public void ElapsedMinutes_MatchesTheFormulaMarketContextBuilderHasAlwaysUsed()
    {
        var firstBarTime = new DateTime(2026, 3, 2, 9, 30, 0, DateTimeKind.Utc);
        var barTime = firstBarTime.AddMinutes(123);
        int expected = (int)(barTime - firstBarTime).TotalMinutes; // pre-extraction expression, restated independently

        Assert.Equal(expected, MarketContextFactory.ElapsedMinutes(barTime, firstBarTime, isFirstBar: false));
    }

    [Fact]
    public void Session_MatchesTheEmptyPlaceholder_NeverInventingSessionLogic()
    {
        SessionInfo expected = new(string.Empty, DateTime.MinValue, DateTime.MaxValue);
        Assert.Equal(expected, MarketContextFactory.UnknownSession());
    }

    [Fact]
    public void CreateHistorical_ExecutionShape_MatchesTheHistoricalConventionFixedByTheBrief()
    {
        HistoricalBar bar = new(new DateTime(2026, 3, 2), 4520m, 4525m, 4518m, 4522m, 1000m);

        MarketContext context = MarketContextFactory.CreateHistorical(bar, index: 7, barCount: 50, "M1", Instrument, bar.Timestamp);

        // Lot 14.1 brief §7: CurrentBar = index + 1, IsRealtime = false, IsHistorical = true, IsReplay = false.
        Assert.Equal(8, context.Execution.CurrentBar);
        Assert.Equal(7, context.Execution.LastCalculatedBar);
        Assert.False(context.Execution.IsRealtime);
        Assert.True(context.Execution.IsHistorical);
        Assert.False(context.Execution.IsReplay);
    }

    [Fact]
    public void CreateHistorical_And_Create_ProduceTheSameContextForTheSameInputs()
    {
        // The high-level, backtest-facing overload (CreateHistorical) must not silently diverge from the
        // low-level primitive (Create) both MarketContextBuilder.Build and CreateHistorical itself call -
        // there is exactly one code path computing the derived fields, exercised here from both entry
        // points with equivalent inputs.
        var t0 = new DateTime(2026, 3, 2, 9, 30, 0, DateTimeKind.Utc);
        HistoricalBar bar = new(t0.AddMinutes(15), 4520m, 4525m, 4518m, 4522m, 1000m, BidVolume: 400m, AskVolume: 600m, Delta: 200m);

        MarketContext viaHistorical = MarketContextFactory.CreateHistorical(bar, index: 3, barCount: 10, "M1", Instrument, t0);
        MarketContext viaCreate = MarketContextFactory.Create(
            barIndex: 3, currentBar: 4, timeFrame: "M1",
            open: bar.Open, high: bar.High, low: bar.Low, close: bar.Close, volume: bar.Volume,
            bidVolume: bar.BidVolume!.Value, askVolume: bar.AskVolume!.Value, delta: bar.Delta!.Value,
            instrument: Instrument, barTime: bar.Timestamp, firstBarTime: t0,
            isFirstBar: false, isLastBar: false, isRealtime: false, isHistorical: true, isReplay: false);

        Assert.Equal(viaCreate.Price, viaHistorical.Price);
        Assert.Equal(viaCreate.Volume, viaHistorical.Volume);
        Assert.Equal(viaCreate.Clock.ElapsedMinutes, viaHistorical.Clock.ElapsedMinutes);
        Assert.Equal(viaCreate.Execution.CurrentBar, viaHistorical.Execution.CurrentBar);
    }
}
