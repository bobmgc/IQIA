using System;

namespace IQIAIndicator.Core.MarketData.Yahoo;

/// <summary>
/// Sprint 15.25 (Lot 15.25-XX). Raised by <see cref="HttpYahooChartClient"/> / <see cref="YahooHistoricalBarSource"/>
/// when a historical-data load fails for a reason attributable to the PROVIDER (rate limiting, network,
/// 5xx, or a non-chart body) rather than to the scientific content of the series.
///
/// This type deliberately derives from <see cref="Exception"/> directly, NOT from
/// <see cref="InvalidOperationException"/>: the network-integration tests must be able to catch a
/// provider outage (this type) while letting a genuine <see cref="InvalidOperationException"/> from
/// <c>HistoricalSeries.Create</c> or "no usable bar in range" propagate as a real, red failure.
///
/// Every retry decision that led here is already spent by the time this is thrown - a caller never
/// needs to (and must not) loop on it. <see cref="AttemptsMade"/> and <see cref="TotalWaited"/> record
/// what the bounded policy actually did, for the test log / report.
/// </summary>
public sealed class YahooProviderException : Exception
{
    public YahooProviderException(
        YahooFailureKind kind,
        string message,
        int? httpStatusCode = null,
        TimeSpan? retryAfter = null,
        int attemptsMade = 0,
        TimeSpan totalWaited = default,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        HttpStatusCode = httpStatusCode;
        RetryAfter = retryAfter;
        AttemptsMade = attemptsMade;
        TotalWaited = totalWaited;
    }

    /// <summary>Deterministic failure classification - see <see cref="YahooFailureKind"/>.</summary>
    public YahooFailureKind Kind { get; }

    /// <summary>The HTTP status code of the last response, when there was one (429, 503, ...).
    /// <c>null</c> for a pure transport failure that never produced a response.</summary>
    public int? HttpStatusCode { get; }

    /// <summary>The <c>Retry-After</c> the provider asked for on the last rate-limited response, if any.
    /// Recorded verbatim for diagnostics; it never overrode the application's bounded wait budget.</summary>
    public TimeSpan? RetryAfter { get; }

    /// <summary>How many HTTP attempts were made for the failed chunk (initial try + bounded retries).</summary>
    public int AttemptsMade { get; }

    /// <summary>Total time the bounded policy actually spent sleeping between attempts before giving up.</summary>
    public TimeSpan TotalWaited { get; }

    /// <summary><c>true</c> for <see cref="YahooFailureKind.RateLimited"/> - the single most common
    /// cause during a long test session, called out so guards read cleanly.</summary>
    public bool IsRateLimited => Kind == YahooFailureKind.RateLimited;
}
