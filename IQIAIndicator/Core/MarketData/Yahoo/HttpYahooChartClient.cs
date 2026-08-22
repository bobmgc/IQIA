using System;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;

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
/// STATUS CODE IS NOT HOW ERRORS ARE DETECTED HERE. Verified empirically: a 422 (unsupported range) and
/// a 404 (unknown symbol) both return a well-formed JSON body with Yahoo's own <c>chart.error</c> field
/// populated - discarding that body via <c>EnsureSuccessStatusCode</c>/<c>GetStringAsync</c> would throw
/// away the exact, descriptive reason Yahoo already provides. This class therefore always reads the
/// response body (via <c>GetAsync</c> + <c>ReadAsStringAsync</c>, deliberately not <c>GetStringAsync</c>)
/// regardless of status code, and lets <see cref="YahooChartParser"/> - the single place that interprets
/// <c>chart.error</c> - decide whether the body describes success or failure. Only a genuine transport
/// failure (DNS, timeout, connection refused, or a non-JSON body such as an edge/WAF block page) surfaces
/// as an exception from this layer, via the un-wrapped, real .NET exception type
/// (<see cref="HttpRequestException"/>/<see cref="TaskCanceledException"/>/a JSON parse failure from
/// <see cref="YahooChartParser"/>) - never masked or translated into a guess.
/// </summary>
internal sealed class HttpYahooChartClient : IYahooChartClient
{
    private const string BaseUrl = "https://query2.finance.yahoo.com/v8/finance/chart/";

    private static readonly HttpClient SharedClient = CreateClient();

    private readonly HttpClient _httpClient;

    /// <param name="httpClient">Optional externally-owned client (its lifetime remains the caller's
    /// responsibility). Defaults to a shared, static instance - the standard .NET guidance for avoiding
    /// socket exhaustion from short-lived HttpClient instances.</param>
    public HttpYahooChartClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? SharedClient;
    }

    public string FetchChartJson(string yahooTicker, string yahooInterval, DateTime periodStartUtc, DateTime periodEndUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(yahooTicker);
        ArgumentException.ThrowIfNullOrWhiteSpace(yahooInterval);

        long period1 = new DateTimeOffset(periodStartUtc, TimeSpan.Zero).ToUnixTimeSeconds();
        long period2 = new DateTimeOffset(periodEndUtc, TimeSpan.Zero).ToUnixTimeSeconds();

        string url = string.Create(
            CultureInfo.InvariantCulture,
            $"{BaseUrl}{Uri.EscapeDataString(yahooTicker)}?period1={period1}&period2={period2}&interval={Uri.EscapeDataString(yahooInterval)}");

        using HttpResponseMessage response = _httpClient.GetAsync(url).GetAwaiter().GetResult();
        return response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        // A realistic browser User-Agent is required - verified empirically: the default HttpClient/curl
        // User-Agent was rejected with HTTP 429 ("Too Many Requests") on the very first request.
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
        return client;
    }
}
