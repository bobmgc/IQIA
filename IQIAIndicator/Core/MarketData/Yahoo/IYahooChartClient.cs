using System;
using System.Threading;

namespace IQIAIndicator.Core.MarketData.Yahoo;

/// <summary>
/// Sprint 15.25 (Lot 14.2). The one seam between <see cref="YahooHistoricalBarSource"/> and the network.
/// Exists so unit tests can inject a fixture-returning fake (deterministic, no network - brief §5)
/// while <see cref="HttpYahooChartClient"/> talks to the real Yahoo endpoint for the optional network
/// integration tests (brief §20) and real usage.
///
/// Sprint 15.25 (Lot 15.25-XX): a <see cref="CancellationToken"/> was added so a caller can abandon a
/// stalled load promptly. The parameter is defaulted, so every existing call site keeps compiling
/// unchanged; it is threaded no further than <c>Core/MarketData/Yahoo</c> (the shared
/// <see cref="IHistoricalBarSource"/> port is deliberately left alone).
/// </summary>
internal interface IYahooChartClient
{
    /// <summary>Fetches the raw chart JSON for one (ticker, interval, [periodStartUtc, periodEndUtc])
    /// request. Never parses the response - see <see cref="YahooChartParser"/> for that.</summary>
    /// <exception cref="YahooProviderException">The provider failed for a classified reason
    /// (rate limiting, network, 5xx after bounded retries, or a non-chart body). Bounded retries are
    /// already spent - the caller must not loop.</exception>
    string FetchChartJson(
        string yahooTicker,
        string yahooInterval,
        DateTime periodStartUtc,
        DateTime periodEndUtc,
        CancellationToken cancellationToken = default);
}
