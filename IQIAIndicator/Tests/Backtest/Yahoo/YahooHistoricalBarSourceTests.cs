using System;
using System.Linq;
using IQIAIndicator.Core.MarketData;
using IQIAIndicator.Core.MarketData.Yahoo;
using Xunit;

namespace IQIAIndicator.Tests.BacktestTests.Yahoo;

/// <summary>
/// Sprint 15.25 (Lot 14.2, brief §19). End-to-end YahooHistoricalBarSource tests against a
/// FakeYahooChartClient - deterministic, no network, exercising mapping, timezone, chunking, gap
/// reporting, and the fact that the resulting HistoricalSeries goes through the exact same,
/// unmodified HistoricalSeries.Create validation as every other source.
/// </summary>
public sealed class YahooHistoricalBarSourceTests
{
    private static readonly DateTime T0 = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private const string TwoBarsDay1 =
        """
        {"chart":{"result":[{"meta":{"symbol":"MES=F"},"timestamp":[1735689600,1735689900],"indicators":{"quote":[{"open":[100,100.25],"high":[100.5,100.5],"low":[99.75,100],"close":[100.25,100.5],"volume":[10,20]}]}}],"error":null}}
        """;

    private const string TwoBarsDay2 =
        """
        {"chart":{"result":[{"meta":{"symbol":"MES=F"},"timestamp":[1735776000,1735776300],"indicators":{"quote":[{"open":[101,101.25],"high":[101.5,101.5],"low":[100.75,101],"close":[101.25,101.5],"volume":[15,25]}]}}],"error":null}}
        """;

    private const string OneBarWithOneGap =
        """
        {"chart":{"result":[{"meta":{"symbol":"MES=F"},"timestamp":[1735689600,1735689900],"indicators":{"quote":[{"open":[100,null],"high":[100.5,null],"low":[99.75,null],"close":[100.25,null],"volume":[10,0]}]}}],"error":null}}
        """;

    private const string DuplicateTimestampAcrossChunks =
        """
        {"chart":{"result":[{"meta":{"symbol":"MES=F"},"timestamp":[1735689600],"indicators":{"quote":[{"open":[100],"high":[100.5],"low":[99.75],"close":[100.25],"volume":[10]}]}}],"error":null}}
        """;

    // ── Mapping (brief §19 items 13-17) ─────────────────────────────────────────────────────────────

    [Fact]
    public void Load_ResolvesSymbolAndTimeFrame_AndSendsThemToTheClient()
    {
        var fake = new FakeYahooChartClient(TwoBarsDay1);
        var source = new YahooHistoricalBarSource(fake, maxChunkSpanDays: 59);

        source.Load("MES", "M5", T0, T0.AddDays(1));

        Assert.Single(fake.Calls);
        Assert.Equal("MES=F", fake.Calls[0].Ticker);
        Assert.Equal("5m", fake.Calls[0].Interval);
    }

    [Fact]
    public void Load_UnsupportedTimeFrame_ThrowsBeforeEverCallingTheClient()
    {
        var fake = new FakeYahooChartClient();
        var source = new YahooHistoricalBarSource(fake, maxChunkSpanDays: 59);

        Assert.Throws<NotSupportedException>(() => source.Load("MES", "M1", T0, T0.AddDays(1)));
        Assert.Empty(fake.Calls);
    }

    [Fact]
    public void Load_UnverifiedSymbol_ThrowsBeforeEverCallingTheClient()
    {
        var fake = new FakeYahooChartClient();
        var source = new YahooHistoricalBarSource(fake, maxChunkSpanDays: 59);

        Assert.Throws<NotSupportedException>(() => source.Load("NQ", "M5", T0, T0.AddDays(1)));
        Assert.Empty(fake.Calls);
    }

    [Fact]
    public void Load_ResultingBars_NeverPopulateBidAskDeltaOpenInterest()
    {
        var fake = new FakeYahooChartClient(TwoBarsDay1);
        var source = new YahooHistoricalBarSource(fake, maxChunkSpanDays: 59);

        HistoricalSeries series = source.Load("MES", "M5", T0, T0.AddDays(1));

        Assert.All(series.Bars, bar =>
        {
            Assert.Null(bar.BidVolume);
            Assert.Null(bar.AskVolume);
            Assert.Null(bar.Delta);
            Assert.Null(bar.OpenInterest);
        });
    }

    // ── Timezone (brief §19 items 21-22) ────────────────────────────────────────────────────────────

    [Fact]
    public void Load_ResultingSeries_TimeZoneIsAlwaysUtc()
    {
        var fake = new FakeYahooChartClient(TwoBarsDay1);
        var source = new YahooHistoricalBarSource(fake, maxChunkSpanDays: 59);

        HistoricalSeries series = source.Load("MES", "M5", T0, T0.AddDays(1));

        Assert.Equal("UTC", series.TimeZone);
    }

    [Fact]
    public void Load_ResultingSeries_ProviderNamesYahooAndTheContinuousFuturesCaveat()
    {
        var fake = new FakeYahooChartClient(TwoBarsDay1);
        var source = new YahooHistoricalBarSource(fake, maxChunkSpanDays: 59);

        HistoricalSeries series = source.Load("MES", "M5", T0, T0.AddDays(1));

        Assert.Equal("Yahoo(Continuous)", series.Provider);
    }

    // ── Series validation reused unmodified (brief §19 items 23-26) ────────────────────────────────

    [Fact]
    public void Load_ProducesAFullyValidatedHistoricalSeries_SameContractAsAnyOtherSource()
    {
        var fake = new FakeYahooChartClient(TwoBarsDay1);
        var source = new YahooHistoricalBarSource(fake, maxChunkSpanDays: 59);

        HistoricalSeries series = source.Load("MES", "M5", T0, T0.AddDays(1));

        Assert.Equal(2, series.Count);
        for (int i = 1; i < series.Count; i++)
            Assert.True(series.Bars[i].Timestamp > series.Bars[i - 1].Timestamp);
    }

    [Fact]
    public void Load_DuplicateTimestampAcrossChunks_Throws_HistoricalSeriesValidationStillApplies()
    {
        // Deliberately misconfigured maxChunkSpanDays so both chunks query overlapping data and the fake
        // client returns the SAME bar twice - proving HistoricalSeries.Create's existing duplicate
        // detection is still the final safety net even when the defect originates from Yahoo/chunking.
        var fake = new FakeYahooChartClient(DuplicateTimestampAcrossChunks, DuplicateTimestampAcrossChunks);
        var source = new YahooHistoricalBarSource(fake, maxChunkSpanDays: 1);

        var ex = Assert.Throws<ArgumentException>(() => source.Load("MES", "M5", T0, T0.AddDays(2)));
        Assert.Contains("duplicate timestamp", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_EmptyOrWhitespaceSymbolOrTimeFrame_Throws()
    {
        var fake = new FakeYahooChartClient();
        var source = new YahooHistoricalBarSource(fake, maxChunkSpanDays: 59);

        Assert.Throws<ArgumentException>(() => source.Load("", "M5", T0, T0.AddDays(1)));
        Assert.Throws<ArgumentException>(() => source.Load("MES", "", T0, T0.AddDays(1)));
    }

    [Fact]
    public void Load_InvertedRange_Throws()
    {
        var fake = new FakeYahooChartClient();
        var source = new YahooHistoricalBarSource(fake, maxChunkSpanDays: 59);

        Assert.Throws<ArgumentException>(() => source.Load("MES", "M5", T0.AddDays(1), T0));
    }

    // ── Chunking wiring (brief §10) ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Load_SpanOverTheLimit_IssuesOneChunkedCallPerSubRange()
    {
        var fake = new FakeYahooChartClient(TwoBarsDay1, TwoBarsDay2);
        var source = new YahooHistoricalBarSource(fake, maxChunkSpanDays: 1);

        HistoricalSeries series = source.Load("MES", "M5", T0, T0.AddDays(2));

        Assert.Equal(2, fake.Calls.Count);
        Assert.Equal(4, series.Count);
        Assert.Equal(2, source.LastRequestChunkCount);
    }

    [Fact]
    public void Load_ChunkedCalls_NeverRequestTheSameInstantTwice()
    {
        var fake = new FakeYahooChartClient(TwoBarsDay1, TwoBarsDay2);
        var source = new YahooHistoricalBarSource(fake, maxChunkSpanDays: 1);

        source.Load("MES", "M5", T0, T0.AddDays(2));

        Assert.Equal(T0, fake.Calls[0].PeriodStartUtc);
        Assert.Equal(T0.AddDays(1), fake.Calls[0].PeriodEndUtc);
        // Second chunk's query starts one second AFTER the logical boundary - see YahooHistoricalBarSource.
        Assert.Equal(T0.AddDays(1).AddSeconds(1), fake.Calls[1].PeriodStartUtc);
        Assert.Equal(T0.AddDays(2), fake.Calls[1].PeriodEndUtc);
    }

    // ── Gap reporting (brief §9/§12) ────────────────────────────────────────────────────────────────

    [Fact]
    public void Load_GapSlots_AreCountedOnLastRequestGapCount_NeverSilentlyLost()
    {
        var fake = new FakeYahooChartClient(OneBarWithOneGap);
        var source = new YahooHistoricalBarSource(fake, maxChunkSpanDays: 59);

        HistoricalSeries series = source.Load("MES", "M5", T0, T0.AddDays(1));

        Assert.Single(series.Bars);
        Assert.Equal(1, source.LastRequestGapCount);
    }

    [Fact]
    public void Load_GapCounters_ResetAcrossCalls_NeverAccumulate()
    {
        var fake = new FakeYahooChartClient(OneBarWithOneGap, TwoBarsDay1);
        var source = new YahooHistoricalBarSource(fake, maxChunkSpanDays: 59);

        source.Load("MES", "M5", T0, T0.AddDays(1));
        Assert.Equal(1, source.LastRequestGapCount);

        source.Load("MES", "M5", T0.AddDays(3), T0.AddDays(4));
        Assert.Equal(0, source.LastRequestGapCount);
    }

    [Fact]
    public void Load_NoUsableBarsAnywhereInRange_Throws_NeverReturnsAnEmptySeries()
    {
        const string allGaps =
            """
            {"chart":{"result":[{"meta":{"symbol":"MES=F"},"timestamp":[1735689600,1735689900],"indicators":{"quote":[{"open":[null,null],"high":[null,null],"low":[null,null],"close":[null,null],"volume":[0,0]}]}}],"error":null}}
            """;
        var fake = new FakeYahooChartClient(allGaps);
        var source = new YahooHistoricalBarSource(fake, maxChunkSpanDays: 59);

        Assert.Throws<InvalidOperationException>(() => source.Load("MES", "M5", T0, T0.AddDays(1)));
    }

    // ── Constructor validation ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullClient_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new YahooHistoricalBarSource(null!, maxChunkSpanDays: 59));
    }

    [Fact]
    public void Constructor_NonPositiveMaxChunkSpanDays_Throws()
    {
        var fake = new FakeYahooChartClient();
        Assert.Throws<ArgumentOutOfRangeException>(() => new YahooHistoricalBarSource(fake, maxChunkSpanDays: 0));
    }

    [Fact]
    public void DefaultMaxChunkSpanDays_LeavesASafetyMarginUnderYahoosSixtyDayLimit()
    {
        Assert.True(YahooHistoricalBarSource.DefaultMaxChunkSpanDays < 60);
    }
}
