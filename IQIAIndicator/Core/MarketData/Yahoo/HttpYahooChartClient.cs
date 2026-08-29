using System;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace IQIAIndicator.Core.MarketData.Yahoo;

/// <summary>
/// Sprint 15.25 (Lot 14.2, brief §4). Real HTTP implementation of <see cref="IYahooChartClient"/>, using
/// only <c>System.Net.Http.HttpClient</c> and <c>System.Text.Json</c> (in <see cref="YahooChartParser"/>)
/// - both part of the .NET SDK's shared framework for net10.0-windows already referenced by this project.
/// NO NuGet package was added for this lot: the main project (IQIAIndicator.csproj) had zero package
/// dependencies before this lot (verified by inspection - only local ATAS <c>HintPath</c> references),
/// and a full-featured Yahoo Finance client library would be exactly the "dépendance lourde" the brief
/// explicitly forbids (§4) for what is, at its core, one GET request and one JSON parse.
///
/// Endpoint (undocumented, no official API, no API key - verified empirically 2026-08-22 via live calls):
/// <c>https://query2.finance.yahoo.com/v8/finance/chart/{ticker}?period1={unix}&amp;period2={unix}&amp;interval={interval}</c>.
/// <c>query1.finance.yahoo.com</c> was also tried and returned HTTP 429 under a generic User-Agent even
/// for a single request; <c>query2</c> with a realistic browser User-Agent succeeded consistently and
/// needed no cookie/crumb handshake (unlike some other, newer Yahoo Finance endpoints) - so this class
/// sets one static User-Agent header and issues a single, direct GET per chunk.
///
/// STATUS CODE IS ONLY PARTLY HOW ERRORS ARE DETECTED HERE. Verified empirically: a 422 (unsupported
/// range) and a 404 (unknown symbol) both return a well-formed JSON body with Yahoo's own
/// <c>chart.error</c> field populated - discarding that body would throw away the exact reason Yahoo
/// already provides. So for 2xx/4xx this class still reads the body and lets <see cref="YahooChartParser"/>
/// decide success vs. failure.
///
/// Sprint 15.25 (Lot 15.25-XX) added a bounded resilience layer AROUND that, and ONLY for the failure
/// modes that were previously able to hang a long test session or masquerade as a scientific regression:
/// <list type="bullet">
///   <item>HTTP <b>429</b> (and Yahoo's non-standard 999) is detected explicitly and retried a strictly
///     bounded number of times with exponential backoff, honouring <c>Retry-After</c> but never letting
///     it exceed <see cref="YahooRetryPolicy.MaximumTotalWait"/>.</item>
///   <item>HTTP <b>5xx</b> is retried within the same bounded budget, then surfaces as
///     <see cref="YahooFailureKind.ProviderUnavailable"/>.</item>
///   <item>A transport failure / per-request timeout surfaces as
///     <see cref="YahooFailureKind.NetworkFailure"/> after the same bounded budget.</item>
///   <item>A 2xx body that is plainly not the chart envelope (an HTML edge/WAF block page) surfaces as
///     <see cref="YahooFailureKind.InvalidResponse"/> - a PROVIDER problem, distinct from a genuine
///     <c>chart.error</c> JSON body, which still flows to <see cref="YahooChartParser"/> unchanged.</item>
/// </list>
/// Everything else - a real <c>chart.error</c>, a malformed series, "no usable bar" - keeps throwing its
/// original <see cref="InvalidOperationException"/>/<see cref="ArgumentException"/> so a scientific
/// regression is never hidden behind the provider-outage path.
/// </summary>
internal sealed class HttpYahooChartClient : IYahooChartClient
{
    private const string BaseUrl = "https://query2.finance.yahoo.com/v8/finance/chart/";

    private static readonly HttpClient SharedClient = CreateClient();

    private readonly HttpClient _httpClient;
    private readonly YahooRetryPolicy _policy;
    private readonly Func<TimeSpan, CancellationToken, Task> _sleep;

    /// <param name="httpClient">Optional externally-owned client (its lifetime remains the caller's
    /// responsibility). Defaults to a shared, static instance - the standard .NET guidance for avoiding
    /// socket exhaustion from short-lived HttpClient instances.</param>
    public HttpYahooChartClient(HttpClient? httpClient = null)
        : this(httpClient ?? SharedClient, YahooRetryPolicy.Default, sleep: null)
    {
    }

    /// <summary>Test seam: inject a fake <see cref="HttpMessageHandler"/>-backed client, a tighter
    /// <see cref="YahooRetryPolicy"/>, and a <paramref name="sleep"/> that records durations instead of
    /// actually blocking - so the retry/backoff bounds can be asserted without the suite ever sleeping
    /// for real.</summary>
    internal HttpYahooChartClient(HttpClient httpClient, YahooRetryPolicy policy, Func<TimeSpan, CancellationToken, Task>? sleep)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(policy);

        _httpClient = httpClient;
        _policy = policy;
        _sleep = sleep ?? ((delay, token) => Task.Delay(delay, token));
    }

    public string FetchChartJson(
        string yahooTicker,
        string yahooInterval,
        DateTime periodStartUtc,
        DateTime periodEndUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(yahooTicker);
        ArgumentException.ThrowIfNullOrWhiteSpace(yahooInterval);

        try
        {
            return FetchChartJsonAsync(yahooTicker, yahooInterval, periodStartUtc, periodEndUtc, cancellationToken)
                .GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
    }

    private async Task<string> FetchChartJsonAsync(
        string yahooTicker,
        string yahooInterval,
        DateTime periodStartUtc,
        DateTime periodEndUtc,
        CancellationToken cancellationToken)
    {
        long period1 = new DateTimeOffset(periodStartUtc, TimeSpan.Zero).ToUnixTimeSeconds();
        long period2 = new DateTimeOffset(periodEndUtc, TimeSpan.Zero).ToUnixTimeSeconds();

        string url = string.Create(
            CultureInfo.InvariantCulture,
            $"{BaseUrl}{Uri.EscapeDataString(yahooTicker)}?period1={period1}&period2={period2}&interval={Uri.EscapeDataString(yahooInterval)}");

        int attemptsMade = 0;
        TimeSpan totalWaited = TimeSpan.Zero;
        int? lastStatus = null;
        TimeSpan? lastRetryAfter = null;
        Exception? lastTransport = null;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            attemptsMade++;

            YahooFailureKind kind;
            try
            {
                using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                attemptCts.CancelAfter(_policy.PerRequestTimeout);

                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                using HttpResponseMessage response = await _httpClient
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, attemptCts.Token)
                    .ConfigureAwait(false);

                int status = (int)response.StatusCode;
                lastStatus = status;

                if (status is 429 or 999)
                {
                    kind = YahooFailureKind.RateLimited;
                    lastRetryAfter = ReadRetryAfter(response);
                }
                else if (status is >= 500 and <= 599)
                {
                    kind = YahooFailureKind.ProviderUnavailable;
                    lastRetryAfter = null;
                }
                else
                {
                    string body = await response.Content.ReadAsStringAsync(attemptCts.Token).ConfigureAwait(false);

                    if (status is >= 200 and <= 299 && LooksLikeNonChartBody(response, body))
                    {
                        kind = YahooFailureKind.InvalidResponse;
                        lastRetryAfter = null;
                    }
                    else
                    {
                        // The ONLY success path. The returned string is byte-for-byte what the previous
                        // implementation returned (same HttpContent decoding) - Phase 9 determinism.
                        return body;
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw; // the CALLER cancelled - propagate, don't classify as our failure
            }
            catch (OperationCanceledException oce)
            {
                kind = YahooFailureKind.NetworkFailure; // per-request timeout fired
                lastTransport = oce;
                lastStatus = null;
                lastRetryAfter = null;
            }
            catch (HttpRequestException hre)
            {
                kind = YahooFailureKind.NetworkFailure;
                lastTransport = hre;
                lastStatus = (int?)hre.StatusCode;
                lastRetryAfter = null;
            }

            YahooRetryDecision decision = _policy.Decide(attemptsMade, kind, lastRetryAfter, totalWaited);
            if (!decision.ShouldRetry)
            {
                throw new YahooProviderException(
                    kind,
                    BuildFailureMessage(kind, url, attemptsMade, totalWaited, lastStatus, lastRetryAfter, lastTransport),
                    httpStatusCode: lastStatus,
                    retryAfter: lastRetryAfter,
                    attemptsMade: attemptsMade,
                    totalWaited: totalWaited,
                    innerException: lastTransport);
            }

            await _sleep(decision.Delay, cancellationToken).ConfigureAwait(false);
            totalWaited += decision.Delay;
        }
    }

    /// <summary>Reads <c>Retry-After</c> as a positive <see cref="TimeSpan"/> (delta form or HTTP-date
    /// form), or <c>null</c> when absent / non-positive. Advisory only - <see cref="YahooRetryPolicy"/>
    /// clamps it to the remaining wait budget.</summary>
    private static TimeSpan? ReadRetryAfter(HttpResponseMessage response)
    {
        RetryConditionHeaderValue? header = response.Headers.RetryAfter;
        if (header is null)
            return null;

        if (header.Delta is { } delta)
            return delta > TimeSpan.Zero ? delta : null;

        if (header.Date is { } date)
        {
            TimeSpan diff = date - DateTimeOffset.UtcNow;
            return diff > TimeSpan.Zero ? diff : null;
        }

        return null;
    }

    /// <summary>A genuine Yahoo chart response - success OR <c>chart.error</c> - is a JSON object/array.
    /// Anything else on a 2xx (an HTML block page, an empty body) is a provider problem, not a chart
    /// error, and must NOT be handed to <see cref="YahooChartParser"/> as if it were scientific data.</summary>
    private static bool LooksLikeNonChartBody(HttpResponseMessage response, string body)
    {
        ReadOnlySpan<char> trimmed = body.AsSpan().TrimStart();
        if (trimmed.Length > 0 && (trimmed[0] == '{' || trimmed[0] == '['))
            return false; // could be a real chart.error envelope - let the parser judge it

        string? mediaType = response.Content.Headers.ContentType?.MediaType;
        bool htmlish = mediaType is not null && mediaType.Contains("html", StringComparison.OrdinalIgnoreCase);
        bool markupStart = trimmed.Length > 0 && trimmed[0] == '<';
        return htmlish || markupStart || trimmed.Length == 0;
    }

    private static string BuildFailureMessage(
        YahooFailureKind kind,
        string url,
        int attemptsMade,
        TimeSpan totalWaited,
        int? lastStatus,
        TimeSpan? retryAfter,
        Exception? transport)
    {
        string reason = kind switch
        {
            YahooFailureKind.RateLimited => $"Yahoo rate-limited the request (HTTP {lastStatus?.ToString(CultureInfo.InvariantCulture) ?? "429"})",
            YahooFailureKind.ProviderUnavailable => $"Yahoo returned a server error (HTTP {lastStatus?.ToString(CultureInfo.InvariantCulture) ?? "5xx"})",
            YahooFailureKind.NetworkFailure => $"the request to Yahoo failed at transport level ({transport?.GetType().Name ?? "unknown"})",
            YahooFailureKind.InvalidResponse => "Yahoo returned a 2xx body that is not a chart envelope (edge/WAF block page or empty body)",
            _ => "Yahoo request failed",
        };

        string retryAfterText = retryAfter is { } ra
            ? $", last Retry-After={ra.TotalSeconds:0.#}s (clamped to the application wait budget)"
            : string.Empty;

        return $"{reason}. Bounded retry policy exhausted after {attemptsMade} attempt(s) and "
            + $"{totalWaited.TotalSeconds:0.#}s of total backoff{retryAfterText}. URL={url}. "
            + "This is a PROVIDER failure, not a scientific/data-quality failure of the historical series.";
    }

    private static HttpClient CreateClient()
    {
        // Timeout.InfiniteTimeSpan: the per-request bound is enforced by a CancellationTokenSource in
        // FetchChartJsonAsync (YahooRetryPolicy.PerRequestTimeout), so there is exactly ONE timeout
        // system, not two overlapping ones. The old fixed 30 s HttpClient.Timeout is gone.
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        // A realistic browser User-Agent is required - verified empirically: the default HttpClient/curl
        // User-Agent was rejected with HTTP 429 ("Too Many Requests") on the very first request.
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
        return client;
    }
}
