using System;
using System.Linq;
using IQIAIndicator.Core.MarketData;
using IQIAIndicator.Core.MarketData.Yahoo;
using Xunit;

namespace IQIAIndicator.Tests.BacktestTests.Yahoo;

/// <summary>
/// Sprint 15.25 (Lot 14.2, brief §5/§19). Deterministic, network-free parser tests. Every fixture below
/// is a small, hand-trimmed JSON literal shaped exactly like a real Yahoo chart response captured live
/// on 2026-08-22 (MES=F/ES=F, 5m interval) - never a downloaded dataset committed to the repo (brief §5).
/// </summary>
public sealed class YahooChartParserTests
{
    // 5 slots, 5 minutes apart starting at 2025-01-01T00:00:00Z (Unix 1735689600). Index 2 is a full gap
    // (all OHLC null, Volume=0) - the exact shape observed for a CME daily maintenance break.
    private const string ValidWithGap =
        """
        {"chart":{"result":[{"meta":{"symbol":"MES=F","instrumentType":"FUTURE","exchangeName":"CME","timezone":"EDT","exchangeTimezoneName":"America/New_York","gmtoffset":-14400,"dataGranularity":"5m"},"timestamp":[1735689600,1735689900,1735690200,1735690500,1735690800],"indicators":{"quote":[{"open":[100.00,100.25,null,100.75,101.00],"high":[100.50,100.50,null,101.00,101.25],"low":[99.75,100.00,null,100.50,100.75],"close":[100.25,100.50,null,101.00,101.10],"volume":[10,20,0,30,40]}]}}],"error":null}}
        """;

    // Real shape for a fully non-trading period (e.g. a weekend day): no "timestamp" key at all, and
    // indicators.quote[0] is an empty object - captured live against MES=F for a Saturday.
    private const string EmptyPeriod =
        """
        {"chart":{"result":[{"meta":{"symbol":"MES=F","instrumentType":"FUTURE","exchangeName":"CME"},"indicators":{"quote":[{}]}}],"error":null}}
        """;

    // Verbatim text of the real error Yahoo returned (HTTP 422) for a >60-day-old 5m request.
    private const string ErrorRangeTooLarge =
        """
        {"chart":{"result":null,"error":{"code":"Unprocessable Entity","description":"5m data not available for startTime=1770111238 and endTime=1770975238. The requested range must be within the last 60 days."}}}
        """;

    // Verbatim text of the real error Yahoo returned (HTTP 404) for an unrecognized symbol.
    private const string ErrorUnknownSymbol =
        """
        {"chart":{"result":null,"error":{"code":"Not Found","description":"No data found, symbol may be delisted"}}}
        """;

    // Real body observed for an un-authenticated / blocked request (plain text, not JSON at all).
    private const string NonJsonBody = "Edge: Too Many Requests";

    [Fact]
    public void ValidResponse_MapsEveryNonGapBar_TimestampOpenHighLowCloseVolume()
    {
        YahooChartParser.ParseResult result = YahooChartParser.Parse(ValidWithGap);

        Assert.Equal(4, result.Bars.Count); // 5 slots minus the 1 gap
        Assert.Equal(1, result.GapCount);

        HistoricalBar first = result.Bars[0];
        Assert.Equal(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), first.Timestamp);
        Assert.Equal(100.00m, first.Open);
        Assert.Equal(100.50m, first.High);
        Assert.Equal(99.75m, first.Low);
        Assert.Equal(100.25m, first.Close);
        Assert.Equal(10m, first.Volume);
    }

    [Fact]
    public void ValidResponse_SeveralBars_PreservesChronologicalOrder()
    {
        YahooChartParser.ParseResult result = YahooChartParser.Parse(ValidWithGap);

        for (int i = 1; i < result.Bars.Count; i++)
            Assert.True(result.Bars[i].Timestamp > result.Bars[i - 1].Timestamp);
    }

    [Fact]
    public void ValidResponse_Timestamp_ConvertsUnixSecondsToUtc()
    {
        YahooChartParser.ParseResult result = YahooChartParser.Parse(ValidWithGap);

        Assert.All(result.Bars, bar => Assert.Equal(DateTimeKind.Utc, bar.Timestamp.Kind));
        Assert.Equal(new DateTime(2025, 1, 1, 0, 5, 0, DateTimeKind.Utc), result.Bars[1].Timestamp);
    }

    [Fact]
    public void ValidResponse_Volume_IsMappedVerbatim_NeverTransformed()
    {
        YahooChartParser.ParseResult result = YahooChartParser.Parse(ValidWithGap);

        Assert.Equal([10m, 20m, 30m, 40m], result.Bars.Select(b => b.Volume));
    }

    [Fact]
    public void GapSlot_NullOhlc_IsOmitted_NeverFabricatedOrInterpolated()
    {
        YahooChartParser.ParseResult result = YahooChartParser.Parse(ValidWithGap);

        // The 3rd slot (index 2, ts=1735690200) must not appear at all - not as a zero bar, not as a
        // repeated copy of its neighbor.
        Assert.DoesNotContain(result.Bars, b => b.Timestamp == new DateTime(2025, 1, 1, 0, 10, 0, DateTimeKind.Utc));
        Assert.Equal(1, result.GapCount);
    }

    [Fact]
    public void NullVolume_WithOhlcPresent_IsAlsoTreatedAsAGap_NeverDefaultedToZero()
    {
        const string json =
            """
            {"chart":{"result":[{"meta":{},"timestamp":[1735689600,1735689900],"indicators":{"quote":[{"open":[100,101],"high":[101,102],"low":[99,100],"close":[100.5,null],"volume":[10,null]}]}}],"error":null}}
            """;

        YahooChartParser.ParseResult result = YahooChartParser.Parse(json);

        Assert.Single(result.Bars);
        Assert.Equal(1, result.GapCount);
    }

    [Fact]
    public void EmptyPeriod_NoTimestampKey_ReturnsEmptyResult_NotAnError()
    {
        YahooChartParser.ParseResult result = YahooChartParser.Parse(EmptyPeriod);

        Assert.Empty(result.Bars);
        Assert.Equal(0, result.GapCount);
    }

    [Fact]
    public void ErrorResponse_RangeTooLarge_ThrowsWithYahoosOwnDescriptionVerbatim()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => YahooChartParser.Parse(ErrorRangeTooLarge));
        Assert.Contains("must be within the last 60 days", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Unprocessable Entity", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ErrorResponse_UnknownSymbol_ThrowsWithYahoosOwnDescriptionVerbatim()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => YahooChartParser.Parse(ErrorUnknownSymbol));
        Assert.Contains("No data found", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NonJsonBody_ThrowsRatherThanFabricatingAnEmptyResult()
    {
        Assert.Throws<InvalidOperationException>(() => YahooChartParser.Parse(NonJsonBody));
    }

    [Fact]
    public void MissingChartEnvelope_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => YahooChartParser.Parse("""{"foo":"bar"}"""));
    }

    [Fact]
    public void EmptyResultArray_NoError_Throws_UnexpectedShapeNeverSilentlyEmpty()
    {
        Assert.Throws<InvalidOperationException>(() => YahooChartParser.Parse("""{"chart":{"result":[],"error":null}}"""));
    }

    [Fact]
    public void QuoteArraysShorterThanTimestamp_Throws_NeverSilentlyTruncates()
    {
        const string json =
            """
            {"chart":{"result":[{"meta":{},"timestamp":[1735689600,1735689900,1735690200],"indicators":{"quote":[{"open":[100,101],"high":[101,102],"low":[99,100],"close":[100.5,101.5],"volume":[10,20]}]}}],"error":null}}
            """;

        Assert.Throws<InvalidOperationException>(() => YahooChartParser.Parse(json));
    }

    [Fact]
    public void EmptyOrWhitespaceJson_Throws()
    {
        Assert.Throws<ArgumentException>(() => YahooChartParser.Parse(""));
        Assert.Throws<ArgumentException>(() => YahooChartParser.Parse("   "));
    }
}
