using System;

namespace IQIAIndicator.Core.MarketData.Yahoo;

/// <summary>
/// Sprint 15.25 (Lot 14.2). The one seam between <see cref="YahooHistoricalBarSource"/> and the network.
/// Exists so unit tests can inject a fixture-returning fake (deterministic, no network - brief §5)
/// while <see cref="HttpYahooChartClient"/> talks to the real Yahoo endpoint for the optional network
/// integration tests (brief §20) and real usage.
/// </summary>
internal interface IYahooChartClient
{
    /// <summary>Fetches the raw chart JSON for one (ticker, interval, [periodStartUtc, periodEndUtc])
    /// request. Never parses the response - see <see cref="YahooChartParser"/> for that.</summary>
    string FetchChartJson(string yahooTicker, string yahooInterval, DateTime periodStartUtc, DateTime periodEndUtc);
}
