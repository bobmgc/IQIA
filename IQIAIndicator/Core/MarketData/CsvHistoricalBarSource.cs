using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace IQIAIndicator.Core.MarketData;

/// <summary>
/// Sprint 15.25 (Lot 14.1, brief §6). Development-time <see cref="IHistoricalBarSource"/> reading the
/// OHLCV CSV that <c>ScientificDatasetCollector.ExportOhlcvCsv()</c> already produces - i.e. the exact
/// format of the real ATAS captures already committed under
/// <c>Tests/Research/StopLossCalibration/RealMarket/RawCapture/</c>. This is what makes the Lot 13 §15.4
/// parity test (R-02: replay a real ATAS capture through the Backtest Engine and compare against what
/// ATAS itself recorded) possible in a later lot.
///
/// WHY THIS DOES NOT REUSE <c>RealMarketOhlcvCsvReader</c> DIRECTLY (brief §6): that reader lives in the
/// TEST assembly (<c>IQIAIndicator.Tests</c>), which references this production project - not the other
/// way round. Calling it from here would invert the dependency. Per the brief's own fallback rule
/// ("Si la réutilisation directe crée un couplage inutile avec les tests : créer un petit adapter plutôt
/// que refactorer massivement"), this is a small, self-contained adapter rather than a migration of the
/// existing Tests-side infrastructure, which is left completely untouched.
///
/// Format read (header validated column by column, exactly as the existing reader does):
///   SessionId,Timestamp,Symbol,TimeFrame,Open,High,Low,Close,Volume,CurrentBar
/// SessionId and CurrentBar are export artifacts and are deliberately DISCARDED - they are not part of
/// the <see cref="HistoricalBar"/> contract (Lot 14.1 brief §3). Symbol/TimeFrame are read only to be
/// VERIFIED against the requested ones; they are never used to override the caller's request.
///
/// Bid/Ask/Delta/OpenInterest are absent from this format and therefore stay null - never zero-filled.
/// </summary>
public sealed class CsvHistoricalBarSource : IHistoricalBarSource
{
    private static readonly string[] ExpectedHeader =
    {
        "SessionId", "Timestamp", "Symbol", "TimeFrame", "Open", "High", "Low", "Close", "Volume", "CurrentBar"
    };

    private readonly string _path;
    private readonly string _timeZone;
    private readonly string _provider;

    /// <param name="path">CSV file to read. Never written to.</param>
    /// <param name="timeZone">The timezone/session convention these timestamps follow. REQUIRED - see
    /// <see cref="HistoricalSeries.TimeZone"/> for why this is never defaulted. For a capture produced by
    /// ScientificDatasetCollector, the honest value is the one that export's own metadata records:
    /// "Unspecified (ATAS IndicatorCandle.Time passthrough, no conversion applied)".</param>
    /// <param name="provider">Provenance label recorded on the resulting series. REQUIRED.</param>
    public CsvHistoricalBarSource(string path, string timeZone, string provider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(timeZone);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);

        _path = path;
        _timeZone = timeZone;
        _provider = provider;
    }

    public HistoricalSeries Load(string symbol, string timeFrame, DateTime from, DateTime to)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ArgumentException.ThrowIfNullOrWhiteSpace(timeFrame);

        if (to <= from)
            throw new ArgumentException($"Requested range is empty or inverted: from={from:O}, to={to:O} (to is exclusive).", nameof(to));

        if (!File.Exists(_path))
            throw new FileNotFoundException($"Historical CSV not found: {_path}. No substitute dataset is ever fabricated.", _path);

        IReadOnlyList<HistoricalBar> allBars = ReadAll(symbol, timeFrame);

        // The WHOLE file is validated first, then the window is applied - not the reverse. A duplicate or
        // out-of-order row outside the requested window still means the source dataset is defective, and
        // that must surface rather than be hidden by a lucky window choice.
        HistoricalSeries full = HistoricalSeries.Create(symbol, timeFrame, _timeZone, _provider, allBars);

        var windowed = new List<HistoricalBar>();
        foreach (HistoricalBar bar in full.Bars)
        {
            if (bar.Timestamp >= from && bar.Timestamp < to)
                windowed.Add(bar);
        }

        if (windowed.Count == 0)
        {
            throw new ArgumentException(
                $"No bar of {_path} falls in [{from:O}, {to:O}) for {symbol}/{timeFrame}. " +
                $"The file covers [{full.FirstTimestamp:O}, {full.LastTimestamp:O}].");
        }

        return HistoricalSeries.Create(symbol, timeFrame, _timeZone, _provider, windowed);
    }

    private IReadOnlyList<HistoricalBar> ReadAll(string requestedSymbol, string requestedTimeFrame)
    {
        string[] lines = File.ReadAllLines(_path, Encoding.UTF8);

        if (lines.Length == 0)
            throw new InvalidOperationException($"Historical CSV is empty (no header): {_path}");

        string[] header = SplitCsvLine(lines[0]);
        if (header.Length != ExpectedHeader.Length)
        {
            throw new InvalidOperationException(
                $"Unexpected CSV header shape in {_path}: expected {ExpectedHeader.Length} columns, got [{string.Join(",", header)}].");
        }

        for (int i = 0; i < ExpectedHeader.Length; i++)
        {
            if (!string.Equals(header[i], ExpectedHeader[i], StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Unexpected CSV column at position {i} in {_path}: expected '{ExpectedHeader[i]}', got '{header[i]}'.");
            }
        }

        var bars = new List<HistoricalBar>(lines.Length - 1);

        for (int lineIndex = 1; lineIndex < lines.Length; lineIndex++)
        {
            if (string.IsNullOrEmpty(lines[lineIndex]))
                continue; // trailing newline from the exporter's AppendLine - not a data row

            string[] fields = SplitCsvLine(lines[lineIndex]);
            if (fields.Length != ExpectedHeader.Length)
            {
                throw new InvalidOperationException(
                    $"Malformed CSV row {lineIndex + 1} in {_path}: expected {ExpectedHeader.Length} fields, got {fields.Length}.");
            }

            string rowSymbol = fields[2];
            string rowTimeFrame = fields[3];

            if (!string.Equals(rowSymbol, requestedSymbol, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"CSV row {lineIndex + 1} in {_path} is for symbol '{rowSymbol}', but '{requestedSymbol}' was requested. A file is never filtered down to a subset of symbols implicitly.");
            }

            if (!string.Equals(rowTimeFrame, requestedTimeFrame, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"CSV row {lineIndex + 1} in {_path} is for timeframe '{rowTimeFrame}', but '{requestedTimeFrame}' was requested.");
            }

            bars.Add(new HistoricalBar(
                Timestamp: DateTime.Parse(fields[1], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                Open: decimal.Parse(fields[4], CultureInfo.InvariantCulture),
                High: decimal.Parse(fields[5], CultureInfo.InvariantCulture),
                Low: decimal.Parse(fields[6], CultureInfo.InvariantCulture),
                Close: decimal.Parse(fields[7], CultureInfo.InvariantCulture),
                Volume: decimal.Parse(fields[8], CultureInfo.InvariantCulture)));
        }

        return bars;
    }

    /// <summary>Matches ScientificDatasetCollector's own Escape() convention (a field is quoted only if
    /// it contains a comma/quote/CR/LF, embedded quotes doubled).</summary>
    private static string[] SplitCsvLine(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(c);
                }
            }
            else if (c == '"')
            {
                inQuotes = true;
            }
            else if (c == ',')
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        fields.Add(current.ToString());
        return fields.ToArray();
    }
}
