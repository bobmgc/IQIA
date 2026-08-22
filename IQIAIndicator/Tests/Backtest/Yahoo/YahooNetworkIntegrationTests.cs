using System;
using System.Net.Http;
using System.Threading.Tasks;
using IQIAIndicator.Core.MarketData;
using IQIAIndicator.Core.MarketData.Yahoo;
using Xunit;
using Xunit.Abstractions;

namespace IQIAIndicator.Tests.BacktestTests.Yahoo;

/// <summary>
/// Sprint 15.25 (Lot 14.2, brief §20). INTEGRATION / NETWORK - these tests make REAL HTTP calls to Yahoo
/// Finance's live chart endpoint. They are best-effort confidence checks, not the primary proof of
/// correctness (that is <c>YahooChartParserTests</c>/<c>YahooHistoricalBarSourceTests</c>, both fully
/// deterministic and network-free).
///
/// EVERY test below wraps its network call in a try/catch for connectivity-shaped failures
/// (<see cref="HttpRequestException"/>, <see cref="TaskCanceledException"/>, and - because Yahoo's own
/// rate limiting/maintenance responses surface through this project's error path as
/// <see cref="InvalidOperationException"/>, see YahooChartParser - that type too) and returns early,
/// reporting via <see cref="ITestOutputHelper"/>, rather than failing (brief §20: "aucun impact sur les
/// tests unitaires" / a network test must never fail the suite when Internet/Yahoo is unreachable).
///
/// KNOWN LIMITATION, STATED EXPLICITLY: the xunit version this project references (2.5.3) has no
/// <c>Assert.Skip</c>/dynamic-skip facility, and a `[Fact(Skip=...)]` reason must be a compile-time
/// constant, so a connectivity failure cannot be surfaced as a "Skipped" result in the test runner UI -
/// only as an early return from a still-"Passed" test, with the reason written to test output. This is a
/// deliberate, documented trade-off given the installed tooling, not an oversight.
///
/// Every request here is small - a couple of days of 5-minute bars (at most a few hundred rows), never a
/// bulk historical download (brief §20: "NE PAS lancer une année de données dans les tests").
/// </summary>
public sealed class YahooNetworkIntegrationTests
{
    private readonly ITestOutputHelper _output;

    public YahooNetworkIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Integration_Network_LoadRecentMesFiveMinuteBars_FromLiveYahooEndpoint()
    {
        try
        {
            var source = new YahooHistoricalBarSource();
            DateTime to = DateTime.UtcNow;
            DateTime from = to.AddDays(-2);

            HistoricalSeries series = source.Load("MES", "M5", from, to);

            Assert.True(series.Count > 0);
            Assert.Equal("UTC", series.TimeZone);
            Assert.Equal("Yahoo(Continuous)", series.Provider);
            Assert.All(series.Bars, bar => Assert.Equal(DateTimeKind.Utc, bar.Timestamp.Kind));
            _output.WriteLine($"OK: loaded {series.Count} bars, gaps={source.LastRequestGapCount}, chunks={source.LastRequestChunkCount}.");
        }
        catch (Exception exception) when (IsConnectivityOrProviderIssue(exception))
        {
            _output.WriteLine($"SKIPPED (network/Yahoo unavailable, not a code failure): {exception.GetType().Name}: {exception.Message}");
        }
    }

    [Fact]
    public void Integration_Network_LoadRecentEsFiveMinuteBars_FromLiveYahooEndpoint()
    {
        try
        {
            var source = new YahooHistoricalBarSource();
            DateTime to = DateTime.UtcNow;
            DateTime from = to.AddDays(-2);

            HistoricalSeries series = source.Load("ES", "M5", from, to);

            Assert.True(series.Count > 0);
        }
        catch (Exception exception) when (IsConnectivityOrProviderIssue(exception))
        {
            _output.WriteLine($"SKIPPED (network/Yahoo unavailable, not a code failure): {exception.GetType().Name}: {exception.Message}");
        }
    }

    [Fact]
    public void Integration_Network_UnverifiedSymbol_StillThrowsBeforeAnyNetworkCall()
    {
        // No try/catch needed: YahooSymbolMap.Resolve rejects before any HTTP call is made, so this test
        // has no network dependency despite living in this file.
        var source = new YahooHistoricalBarSource();
        Assert.Throws<NotSupportedException>(() => source.Load("SPY", "M5", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow));
    }

    private static bool IsConnectivityOrProviderIssue(Exception exception) =>
        exception is HttpRequestException or TaskCanceledException or InvalidOperationException;
}
