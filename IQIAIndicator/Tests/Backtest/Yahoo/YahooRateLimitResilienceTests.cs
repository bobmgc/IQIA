using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using IQIAIndicator.Core.MarketData;
using IQIAIndicator.Core.MarketData.Yahoo;
using Xunit;

namespace IQIAIndicator.Tests.BacktestTests.Yahoo;

/// <summary>
/// Sprint 15.25 (Lot 15.25-XX, brief Phase 8). <see cref="HttpYahooChartClient"/> against a scripted
/// <see cref="StubHttpMessageHandler"/> and a FAKE sleeper (records requested delays, never actually
/// blocks). Proves: 429 is detected, retries are bounded by count AND by cumulative wait, Retry-After
/// cannot exceed the budget, a stalled provider terminates, transient 5xx still recovers, permanent
/// failure terminates with a classified <see cref="YahooProviderException"/>, and a successful body is
/// returned byte-identical regardless of how many retries preceded it.
/// </summary>
public sealed class YahooRateLimitResilienceTests
{
    private const string TwoBarsJson =
        """
        {"chart":{"result":[{"meta":{"symbol":"MES=F"},"timestamp":[1735689600,1735689900],"indicators":{"quote":[{"open":[100,100.25],"high":[100.5,100.5],"low":[99.75,100],"close":[100.25,100.5],"volume":[10,20]}]}}],"error":null}}
        """;

    private static readonly DateTime T0 = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static YahooRetryPolicy FastPolicy(int maxRetries = 3, double maxTotalWaitSeconds = 20, double perRequestTimeoutSeconds = 15)
        => new(
            maximumRetryCount: maxRetries,
            maximumTotalWait: TimeSpan.FromSeconds(maxTotalWaitSeconds),
            perRequestTimeout: TimeSpan.FromSeconds(perRequestTimeoutSeconds),
            baseDelay: TimeSpan.FromSeconds(1),
            maximumDelayPerWait: TimeSpan.FromSeconds(8));

    private static (HttpYahooChartClient Client, List<TimeSpan> Sleeps) Build(StubHttpMessageHandler handler, YahooRetryPolicy policy)
    {
        var sleeps = new List<TimeSpan>();
        Func<TimeSpan, CancellationToken, Task> fakeSleep = (delay, _) =>
        {
            sleeps.Add(delay);
            return Task.CompletedTask;
        };
        var httpClient = new HttpClient(handler);
        return (new HttpYahooChartClient(httpClient, policy, fakeSleep), sleeps);
    }

    private static string Fetch(HttpYahooChartClient client)
        => client.FetchChartJson("MES=F", "5m", T0, T0.AddDays(1));

    // ── HTTP 429 ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Http429_ThenSuccess_ReturnsBody_WithBoundedRetries()
    {
        var handler = new StubHttpMessageHandler(
            StubHttpMessageHandler.Respond(HttpStatusCode.TooManyRequests),
            StubHttpMessageHandler.Respond((HttpStatusCode)429),
            StubHttpMessageHandler.Respond(HttpStatusCode.OK, TwoBarsJson));
        (HttpYahooChartClient client, List<TimeSpan> sleeps) = Build(handler, FastPolicy());

        string body = Fetch(client);

        Assert.Equal(TwoBarsJson, body);
        Assert.Equal(3, handler.CallCount);
        Assert.Equal(2, sleeps.Count); // one sleep before each retry
    }

    [Fact]
    public void Http429_Exhausted_ThrowsRateLimited_WithBoundedCountAndWait()
    {
        var handler = new StubHttpMessageHandler(
            StubHttpMessageHandler.Respond((HttpStatusCode)429),
            StubHttpMessageHandler.Respond((HttpStatusCode)429),
            StubHttpMessageHandler.Respond((HttpStatusCode)429),
            StubHttpMessageHandler.Respond((HttpStatusCode)429));
        YahooRetryPolicy policy = FastPolicy(maxRetries: 3, maxTotalWaitSeconds: 20);
        (HttpYahooChartClient client, List<TimeSpan> sleeps) = Build(handler, policy);

        var ex = Assert.Throws<YahooProviderException>(() => Fetch(client));

        Assert.Equal(YahooFailureKind.RateLimited, ex.Kind);
        Assert.Equal(429, ex.HttpStatusCode);
        Assert.Equal(4, ex.AttemptsMade);                 // 1 initial + 3 retries, never more
        Assert.Equal(4, handler.CallCount);
        Assert.Equal(3, sleeps.Count);
        TimeSpan total = TimeSpan.Zero;
        foreach (TimeSpan s in sleeps) total += s;
        Assert.True(total <= policy.MaximumTotalWait, $"total backoff {total} exceeded {policy.MaximumTotalWait}");
        Assert.Equal(total, ex.TotalWaited);
    }

    // ── Retry-After ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RetryAfterHeader_NeverExceeds_ApplicationWaitBudget()
    {
        var handler = new StubHttpMessageHandler(
            StubHttpMessageHandler.Respond((HttpStatusCode)429, retryAfter: "600"),  // provider wants 10 minutes
            StubHttpMessageHandler.Respond((HttpStatusCode)429, retryAfter: "600"));
        YahooRetryPolicy policy = FastPolicy(maxRetries: 3, maxTotalWaitSeconds: 2);
        (HttpYahooChartClient client, List<TimeSpan> sleeps) = Build(handler, policy);

        var ex = Assert.Throws<YahooProviderException>(() => Fetch(client));

        Assert.Equal(YahooFailureKind.RateLimited, ex.Kind);
        Assert.All(sleeps, s => Assert.True(s <= TimeSpan.FromSeconds(2), $"a single sleep {s} exceeded the 2s budget"));
        TimeSpan total = TimeSpan.Zero;
        foreach (TimeSpan s in sleeps) total += s;
        Assert.True(total <= TimeSpan.FromSeconds(2));
        Assert.Equal(TimeSpan.FromSeconds(600), ex.RetryAfter); // recorded verbatim, but not obeyed
    }

    // ── HTTP 5xx ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Http500_ThenSuccess_TransientRecoveryStillWorks()
    {
        var handler = new StubHttpMessageHandler(
            StubHttpMessageHandler.Respond(HttpStatusCode.InternalServerError, "<html>oops</html>", "text/html"),
            StubHttpMessageHandler.Respond(HttpStatusCode.OK, TwoBarsJson));
        (HttpYahooChartClient client, List<TimeSpan> sleeps) = Build(handler, FastPolicy());

        string body = Fetch(client);

        Assert.Equal(TwoBarsJson, body);
        Assert.Equal(2, handler.CallCount);
        Assert.Single(sleeps);
    }

    [Fact]
    public void Http500_Permanent_ThrowsProviderUnavailable_Bounded()
    {
        var handler = new StubHttpMessageHandler(
            StubHttpMessageHandler.Respond(HttpStatusCode.BadGateway),
            StubHttpMessageHandler.Respond(HttpStatusCode.BadGateway),
            StubHttpMessageHandler.Respond(HttpStatusCode.BadGateway),
            StubHttpMessageHandler.Respond(HttpStatusCode.BadGateway));
        (HttpYahooChartClient client, _) = Build(handler, FastPolicy(maxRetries: 3));

        var ex = Assert.Throws<YahooProviderException>(() => Fetch(client));

        Assert.Equal(YahooFailureKind.ProviderUnavailable, ex.Kind);
        Assert.Equal(502, ex.HttpStatusCode);
        Assert.Equal(4, ex.AttemptsMade);
    }

    // ── Stalled provider / per-request timeout ─────────────────────────────────────────────────────

    [Fact]
    public void StalledProvider_TerminatesAtPerRequestTimeout_ClassifiedNetworkFailure()
    {
        var handler = new StubHttpMessageHandler(
            StubHttpMessageHandler.Stall(),
            StubHttpMessageHandler.Stall(),
            StubHttpMessageHandler.Stall(),
            StubHttpMessageHandler.Stall());
        // Real (tiny) per-request timeout so this actually exercises the CancellationTokenSource path;
        // backoff is still faked so the whole test is sub-second.
        YahooRetryPolicy policy = FastPolicy(maxRetries: 3, perRequestTimeoutSeconds: 0.1);
        (HttpYahooChartClient client, _) = Build(handler, policy);

        var ex = Assert.Throws<YahooProviderException>(() => Fetch(client));

        Assert.Equal(YahooFailureKind.NetworkFailure, ex.Kind);
        Assert.Equal(4, ex.AttemptsMade);
        Assert.Equal(4, handler.CallCount);
    }

    [Fact]
    public void TransportException_IsClassifiedNetworkFailure_AndBounded()
    {
        var handler = new StubHttpMessageHandler(
            StubHttpMessageHandler.Throw(new HttpRequestException("connection refused")),
            StubHttpMessageHandler.Throw(new HttpRequestException("connection refused")),
            StubHttpMessageHandler.Throw(new HttpRequestException("connection refused")),
            StubHttpMessageHandler.Throw(new HttpRequestException("connection refused")));
        (HttpYahooChartClient client, _) = Build(handler, FastPolicy(maxRetries: 3));

        var ex = Assert.Throws<YahooProviderException>(() => Fetch(client));

        Assert.Equal(YahooFailureKind.NetworkFailure, ex.Kind);
        Assert.Equal(4, ex.AttemptsMade);
    }

    // ── Non-chart 2xx body ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TwoHundredWithHtmlBlockPage_IsClassifiedInvalidResponse_NotAChartError()
    {
        var handler = new StubHttpMessageHandler(
            StubHttpMessageHandler.Respond(HttpStatusCode.OK, "<html><body>Rate limited</body></html>", "text/html"),
            StubHttpMessageHandler.Respond(HttpStatusCode.OK, "<html><body>Rate limited</body></html>", "text/html"));
        (HttpYahooChartClient client, _) = Build(handler, FastPolicy(maxRetries: 1));

        var ex = Assert.Throws<YahooProviderException>(() => Fetch(client));

        Assert.Equal(YahooFailureKind.InvalidResponse, ex.Kind);
    }

    [Fact]
    public void TwoHundredWithChartErrorJson_IsLeftToTheParser_NotWrappedAsProviderFailure()
    {
        const string chartError =
            """{"chart":{"result":null,"error":{"code":"Not Found","description":"No data found, symbol may be delisted"}}}""";
        var handler = new StubHttpMessageHandler(StubHttpMessageHandler.Respond(HttpStatusCode.NotFound, chartError));
        (HttpYahooChartClient client, _) = Build(handler, FastPolicy());

        // The client returns the body verbatim; YahooChartParser (unchanged) is what turns chart.error
        // into an InvalidOperationException - i.e. a genuine data problem, not a YahooProviderException.
        string body = Fetch(client);
        Assert.Equal(chartError, body);
        Assert.Throws<InvalidOperationException>(() => YahooChartParser.Parse(body));
    }

    // ── Caller cancellation ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CallerCancellation_PropagatesOperationCanceled_NotWrappedAsProviderFailure()
    {
        var handler = new StubHttpMessageHandler(StubHttpMessageHandler.Respond(HttpStatusCode.OK, TwoBarsJson));
        (HttpYahooChartClient client, _) = Build(handler, FastPolicy());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(
            () => client.FetchChartJson("MES=F", "5m", T0, T0.AddDays(1), cts.Token));
    }

    // ── Success path unchanged ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void FirstTrySuccess_NoRetry_NoSleep()
    {
        var handler = new StubHttpMessageHandler(StubHttpMessageHandler.Respond(HttpStatusCode.OK, TwoBarsJson));
        (HttpYahooChartClient client, List<TimeSpan> sleeps) = Build(handler, FastPolicy());

        string body = Fetch(client);

        Assert.Equal(TwoBarsJson, body);
        Assert.Equal(1, handler.CallCount);
        Assert.Empty(sleeps);
    }

    // ── Phase 9: determinism of successful content across the resilience path ───────────────────────

    [Fact]
    public void IdenticalResponse_ProducesIdenticalSeries_WhetherOrNotRetriesPreceded()
    {
        var direct = new StubHttpMessageHandler(StubHttpMessageHandler.Respond(HttpStatusCode.OK, TwoBarsJson));
        var afterRetry = new StubHttpMessageHandler(
            StubHttpMessageHandler.Respond((HttpStatusCode)429),
            StubHttpMessageHandler.Respond(HttpStatusCode.InternalServerError),
            StubHttpMessageHandler.Respond(HttpStatusCode.OK, TwoBarsJson));

        (HttpYahooChartClient directClient, _) = Build(direct, FastPolicy());
        (HttpYahooChartClient retryClient, _) = Build(afterRetry, FastPolicy());

        var sourceDirect = new YahooHistoricalBarSource(directClient, maxChunkSpanDays: 59);
        var sourceRetry = new YahooHistoricalBarSource(retryClient, maxChunkSpanDays: 59);

        HistoricalSeries a = sourceDirect.Load("MES", "M5", T0, T0.AddDays(1));
        HistoricalSeries b = sourceRetry.Load("MES", "M5", T0, T0.AddDays(1));

        Assert.Equal(a.Count, b.Count);
        for (int i = 0; i < a.Count; i++)
        {
            Assert.Equal(a.Bars[i].Timestamp, b.Bars[i].Timestamp);
            Assert.Equal(a.Bars[i].Open, b.Bars[i].Open);
            Assert.Equal(a.Bars[i].High, b.Bars[i].High);
            Assert.Equal(a.Bars[i].Low, b.Bars[i].Low);
            Assert.Equal(a.Bars[i].Close, b.Bars[i].Close);
            Assert.Equal(a.Bars[i].Volume, b.Bars[i].Volume);
        }
        Assert.Equal(a.TimeZone, b.TimeZone);
        Assert.Equal(a.Provider, b.Provider);
    }
}
