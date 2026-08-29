using System;
using IQIAIndicator.Core;
using IQIAIndicator.Core.MarketData;
using Xunit;

namespace IQIAIndicator.Tests.BacktestTests;

/// <summary>Sprint 15.25 (Lot 14.1, brief §19). Exact, hand-computed checks for every field
/// MarketContextFactory derives, using simple values chosen so the expected result can be verified by
/// hand rather than trusted from the implementation itself.</summary>
public sealed class MarketContextFactoryTests
{
    private static readonly InstrumentInfo Instrument = new("ES", TickSize: 0.25m, TickValue: 12.5m, PointValue: 50m, Decimals: 2);

    [Fact]
    public void Median_IsAverageOfHighAndLow()
    {
        Assert.Equal(101m, MarketContextFactory.Median(high: 102m, low: 100m));
    }

    [Fact]
    public void TypicalPrice_IsAverageOfHighLowClose()
    {
        // (102 + 100 + 101) / 3 = 101
        Assert.Equal(101m, MarketContextFactory.TypicalPrice(high: 102m, low: 100m, close: 101m));
    }

    [Fact]
    public void ElapsedMinutes_IsZero_OnFirstBar_RegardlessOfTimestamp()
    {
        var barTime = new DateTime(2026, 1, 5, 14, 35, 0, DateTimeKind.Utc);
        var firstBarTime = new DateTime(2026, 1, 5, 14, 30, 0, DateTimeKind.Utc);

        Assert.Equal(0, MarketContextFactory.ElapsedMinutes(barTime, firstBarTime, isFirstBar: true));
    }

    [Fact]
    public void ElapsedMinutes_IsWholeMinutesSinceFirstBar()
    {
        var firstBarTime = new DateTime(2026, 1, 5, 14, 30, 0, DateTimeKind.Utc);
        var barTime = firstBarTime.AddMinutes(37);

        Assert.Equal(37, MarketContextFactory.ElapsedMinutes(barTime, firstBarTime, isFirstBar: false));
    }

    [Fact]
    public void ElapsedMinutes_TruncatesTowardZero_NeverRounds()
    {
        var firstBarTime = new DateTime(2026, 1, 5, 14, 30, 0, DateTimeKind.Utc);
        var barTime = firstBarTime.AddSeconds(179); // 2 min 59 s -> 2, not 3

        Assert.Equal(2, MarketContextFactory.ElapsedMinutes(barTime, firstBarTime, isFirstBar: false));
    }

    [Fact]
    public void UnknownSession_MatchesTheExactPlaceholderMarketContextBuilderHasAlwaysUsed()
    {
        SessionInfo session = MarketContextFactory.UnknownSession();

        Assert.Equal(string.Empty, session.Name);
        Assert.Equal(DateTime.MinValue, session.MarketOpen);
        Assert.Equal(DateTime.MaxValue, session.MarketClose);
    }

    [Fact]
    public void CreateHistorical_FirstBar_FlagsMatch()
    {
        HistoricalBar bar = new(new DateTime(2026, 1, 5, 14, 30, 0, DateTimeKind.Utc), 100m, 101m, 99m, 100.5m, 10m);

        MarketContext context = MarketContextFactory.CreateHistorical(bar, index: 0, barCount: 5, "M5", Instrument, bar.Timestamp);

        Assert.True(context.Clock.IsFirstBar);
        Assert.False(context.Clock.IsLastBar);
        Assert.Equal(0, context.Clock.ElapsedMinutes);
        Assert.Equal(0, context.BarIndex);
        Assert.Equal(1, context.Execution.CurrentBar); // index + 1
        Assert.False(context.Execution.IsRealtime);
        Assert.True(context.Execution.IsHistorical);
        Assert.False(context.Execution.IsReplay);
        Assert.Equal("M5", context.TimeFrame);
        Assert.Equal(Instrument, context.Instrument);
    }

    [Fact]
    public void CreateHistorical_LastBar_FlagsMatch()
    {
        var t0 = new DateTime(2026, 1, 5, 14, 30, 0, DateTimeKind.Utc);
        HistoricalBar bar = new(t0.AddMinutes(20), 100m, 101m, 99m, 100.5m, 10m);

        MarketContext context = MarketContextFactory.CreateHistorical(bar, index: 4, barCount: 5, "M5", Instrument, t0);

        Assert.False(context.Clock.IsFirstBar);
        Assert.True(context.Clock.IsLastBar);
        Assert.Equal(20, context.Clock.ElapsedMinutes);
        Assert.Equal(4, context.BarIndex);
        Assert.Equal(5, context.Execution.CurrentBar);
    }

    [Fact]
    public void CreateHistorical_MiddleBar_IsNeitherFirstNorLast()
    {
        var t0 = new DateTime(2026, 1, 5, 14, 30, 0, DateTimeKind.Utc);
        HistoricalBar bar = new(t0.AddMinutes(10), 100m, 101m, 99m, 100.5m, 10m);

        MarketContext context = MarketContextFactory.CreateHistorical(bar, index: 2, barCount: 5, "M5", Instrument, t0);

        Assert.False(context.Clock.IsFirstBar);
        Assert.False(context.Clock.IsLastBar);
    }

    [Fact]
    public void CreateHistorical_PriceAndVolume_MapAndDeriveCorrectly()
    {
        // High/Low/Close chosen so both derived formulas land on an exact (non-repeating) decimal,
        // keeping this a hand-verifiable check rather than a re-statement of the implementation.
        HistoricalBar bar = new(new DateTime(2026, 1, 5), Open: 100m, High: 105m, Low: 99m, Close: 102m, Volume: 55m);

        MarketContext context = MarketContextFactory.CreateHistorical(bar, 0, 1, "M5", Instrument, bar.Timestamp);

        Assert.Equal(100m, context.Price.Open);
        Assert.Equal(105m, context.Price.High);
        Assert.Equal(99m, context.Price.Low);
        Assert.Equal(102m, context.Price.Close);
        Assert.Equal(102m, context.Price.Median);       // (105+99)/2
        Assert.Equal(102m, context.Price.TypicalPrice); // (105+99+102)/3
        Assert.Equal(55m, context.Volume.Volume);
    }

    [Fact]
    public void CreateHistorical_AbsentMicrostructureFields_ProjectToZero_NeverNull()
    {
        HistoricalBar bar = new(new DateTime(2026, 1, 5), 100m, 101m, 99m, 100.5m, 10m); // Bid/Ask/Delta absent

        MarketContext context = MarketContextFactory.CreateHistorical(bar, 0, 1, "M5", Instrument, bar.Timestamp);

        Assert.Equal(0m, context.Volume.BidVolume);
        Assert.Equal(0m, context.Volume.AskVolume);
        Assert.Equal(0m, context.Volume.Delta);
    }

    [Fact]
    public void CreateHistorical_NegativeIndex_Throws()
    {
        HistoricalBar bar = new(new DateTime(2026, 1, 5), 100m, 101m, 99m, 100.5m, 10m);
        Assert.Throws<ArgumentOutOfRangeException>(() => MarketContextFactory.CreateHistorical(bar, -1, 1, "M5", Instrument, bar.Timestamp));
    }

    [Fact]
    public void CreateHistorical_IndexBeyondBarCount_Throws()
    {
        HistoricalBar bar = new(new DateTime(2026, 1, 5), 100m, 101m, 99m, 100.5m, 10m);
        Assert.Throws<ArgumentOutOfRangeException>(() => MarketContextFactory.CreateHistorical(bar, 5, 5, "M5", Instrument, bar.Timestamp));
    }
}
