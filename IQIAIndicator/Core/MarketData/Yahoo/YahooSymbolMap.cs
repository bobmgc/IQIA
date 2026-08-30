using System;
using System.Collections.Generic;

namespace IQIAIndicator.Core.MarketData.Yahoo;

/// <summary>
/// Sprint 15.25 (Lot 14.2, brief §14). Explicit, data-driven mapping from an IQIA instrument symbol to
/// the Yahoo Finance ticker that carries it - deliberately a lookup table, never an
/// <c>if (symbol == "MES")</c>-style branch, and confined entirely to this Yahoo-specific adapter (never
/// touching RiskEngine/TradePlanBuilder/the generic pipeline, per brief §14's explicit rule).
///
/// Every entry below was verified empirically on 2026-08-22 by a real, live HTTP call to
/// <c>https://query2.finance.yahoo.com/v8/finance/chart/{ticker}</c> and confirming the response's
/// <c>chart.result[0].meta</c> actually describes the intended instrument (InstrumentType=FUTURE,
/// ExchangeName=CME) - never invented by pattern-guessing a "SYMBOL=F" convention for symbols that were
/// not individually checked. A symbol with no verified entry here throws rather than guessing.
///
/// CONTINUOUS FUTURES WARNING (brief §15): both tickers below are Yahoo's CONTINUOUS/front-month futures
/// series - Yahoo itself splices them across contract rolls. Neither is a single expiring contract
/// (e.g. ESZ25). <see cref="YahooHistoricalBarSource"/> reflects this in every HistoricalSeries.Provider
/// value it produces ("Yahoo(Continuous)"), so the caveat travels with the data rather than needing to be
/// remembered separately - see that class's doc comment for the full rationale.
/// </summary>
internal static class YahooSymbolMap
{
    private static readonly IReadOnlyDictionary<string, string> Map = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // Verified 2026-08-22: chart.result[0].meta = { symbol: "ES=F", instrumentType: "FUTURE", exchangeName: "CME", fullExchangeName: "CME" }.
        ["ES"] = "ES=F",
        // Verified 2026-08-22: chart.result[0].meta = { symbol: "MES=F", instrumentType: "FUTURE", exchangeName: "CME", fullExchangeName: "CME", shortName: "MICRO E-MINI S&P 500 INDEX FUTU" }.
        ["MES"] = "MES=F",
        // Audit 2026-08-30 (P0-2 Trending calibration): five more index/commodity futures, each verified
        // by a live HTTP call to query2.finance.yahoo.com/v8/finance/chart/{ticker}?interval=5m&range=5d
        // returning instrumentType="FUTURE" with usable 5-minute OHLC bars. Added only to widen the
        // cross-market calibration set - the generic pipeline never sees these strings (brief §14).
        // Verified 2026-08-30: meta = { symbol: "NQ=F", instrumentType: "FUTURE", exchangeName: "CME", shortName: "Nasdaq 100 Sep 26" }.
        ["NQ"] = "NQ=F",
        // Verified 2026-08-30: meta = { symbol: "YM=F", instrumentType: "FUTURE", exchangeName: "CBT", fullExchangeName: "CBOT", shortName: "Mini Dow Jones Indus.-$5 Sep 26" }.
        ["YM"] = "YM=F",
        // Verified 2026-08-30: meta = { symbol: "RTY=F", instrumentType: "FUTURE", exchangeName: "CME", shortName: "E-mini Russell 2000 Index Futur" }.
        ["RTY"] = "RTY=F",
        // Verified 2026-08-30: meta = { symbol: "GC=F", instrumentType: "FUTURE", exchangeName: "CMX" (COMEX), shortName: "Gold December 26" }.
        ["GC"] = "GC=F",
        // Verified 2026-08-30: meta = { symbol: "CL=F", instrumentType: "FUTURE", exchangeName: "NYM" (NYMEX), shortName: "Crude Oil Oct 26" }.
        ["CL"] = "CL=F",
    };

    public static string Resolve(string iqiaSymbol)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(iqiaSymbol);

        if (Map.TryGetValue(iqiaSymbol, out string? ticker))
            return ticker;

        throw new NotSupportedException(
            $"IQIA symbol '{iqiaSymbol}' has no verified Yahoo ticker mapping. " +
            $"Only {string.Join(", ", Map.Keys)} have been individually confirmed against Yahoo's live chart API - " +
            "no ticker is ever guessed from a naming pattern.");
    }

    /// <summary>Every IQIA symbol this map currently accepts - exposed for tests/diagnostics, never used
    /// to guess an unmapped value.</summary>
    public static IReadOnlyCollection<string> SupportedSymbols => (IReadOnlyCollection<string>)Map.Keys;
}
