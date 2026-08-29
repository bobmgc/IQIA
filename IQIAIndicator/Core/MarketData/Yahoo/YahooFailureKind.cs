namespace IQIAIndicator.Core.MarketData.Yahoo;

/// <summary>
/// Sprint 15.25 (Lot 15.25-XX). Deterministic classification of a Yahoo historical-data load failure.
///
/// The whole point of this enum is that a caller (and a test guard) can tell a TEMPORARY PROVIDER
/// problem apart from a SCIENTIFIC problem with the historical series. Before this lot both surfaced as
/// the same <see cref="System.InvalidOperationException"/> type, so a rate-limited Yahoo response was
/// indistinguishable from a genuinely malformed dataset - and the network-test guards, which caught
/// <see cref="System.InvalidOperationException"/> wholesale, would silently mask a real regression in
/// <c>HistoricalSeries.Create</c> exactly as readily as they masked an HTTP 429.
///
/// A value of this enum only ever rides on a <see cref="YahooProviderException"/>. Genuine
/// data-quality failures (empty/duplicate/out-of-order series, "no usable bar in range") keep throwing
/// their original <see cref="System.InvalidOperationException"/>/<see cref="System.ArgumentException"/>
/// unchanged and are NOT represented here.
/// </summary>
public enum YahooFailureKind
{
    /// <summary>HTTP 429 (or Yahoo's non-standard 999). The provider explicitly refused the request
    /// because of rate limiting / quota. Bounded retries were attempted and exhausted, or the retry
    /// budget was already spent.</summary>
    RateLimited,

    /// <summary>DNS failure, connection refused/reset, a socket-level read timeout, or the per-request
    /// timeout fired. The request never produced an HTTP response.</summary>
    NetworkFailure,

    /// <summary>HTTP 5xx (500/502/503/504) that persisted after the bounded retry policy was
    /// exhausted.</summary>
    ProviderUnavailable,

    /// <summary>An HTTP 2xx response whose body is not the expected Yahoo chart envelope at all
    /// (for example an HTML edge/WAF block page served with status 200). This is a PROVIDER problem,
    /// not a data-quality one - a genuine <c>chart.error</c> JSON body is still handled by
    /// <see cref="YahooChartParser"/> and still throws <see cref="System.InvalidOperationException"/>.</summary>
    InvalidResponse,
}
