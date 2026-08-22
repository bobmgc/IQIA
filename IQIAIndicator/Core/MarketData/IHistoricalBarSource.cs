using System;

namespace IQIAIndicator.Core.MarketData;

/// <summary>
/// Sprint 15.25 (Lot 14.1, brief §5). The single port through which the Backtest Engine obtains
/// historical data. Deliberately provider-agnostic: Yahoo, a CSV export, Databento or any future source
/// is an ADAPTER of this interface, never the abstraction itself (brief §5 - "NE PAS créer directement
/// IYahooProvider comme abstraction centrale").
///
/// Contract:
/// - The returned <see cref="HistoricalSeries"/> is always fully validated (a source never returns a
///   series it could not validate - it throws instead, naming the defect).
/// - <paramref name="from"/> is INCLUSIVE, <paramref name="to"/> is EXCLUSIVE - the same half-open
///   convention <c>Backtest.BacktestWindow</c> uses, so a window boundary can never include the same bar
///   twice across two adjacent windows. (Written as plain text, not a cref: the root namespace
///   IQIAIndicator is shadowed by the class of the same name - see ScientificDatasetRecord.cs for the
///   same pre-existing constraint.)
/// - The source never repairs, sorts, de-duplicates or gap-fills. A defective source dataset surfaces as
///   an exception, never as a quietly corrected series.
/// - Timestamps are returned exactly as the underlying source expresses them; the declared
///   <see cref="HistoricalSeries.TimeZone"/> records which convention that is. No conversion happens here.
/// </summary>
public interface IHistoricalBarSource
{
    /// <summary>Loads the bars for one instrument/timeframe within [<paramref name="from"/>, <paramref name="to"/>).</summary>
    /// <exception cref="ArgumentException">The requested range is empty/inverted, or the resulting series fails validation.</exception>
    HistoricalSeries Load(string symbol, string timeFrame, DateTime from, DateTime to);
}
