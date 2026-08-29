using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace IQIAIndicator.Tests.Research.StopLossCalibration.RealMarket;

/// <summary>
/// Sprint 15.18 (QDE-012 real-market data quality). One parsed row of a ScientificDatasetCollector
/// "_ohlcv.csv" export - the minimal OHLCV dataset defined by
/// ScientificDatasetCollector.ToOhlcvCsv()/ExportOhlcvCsv() (Core/Calibration/ScientificDatasetCollector.cs).
/// Field order/names mirror that CSV exactly: SessionId,Timestamp,Symbol,TimeFrame,Open,High,Low,Close,
/// Volume,CurrentBar.
/// </summary>
public sealed record RealMarketBar(
    Guid SessionId,
    DateTime Timestamp,
    string Symbol,
    string TimeFrame,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal Volume,
    int CurrentBar);

/// <summary>
/// Read-only parser for a ScientificDatasetCollector OHLCV CSV export. Never writes to the source
/// file, never fabricates a row it cannot parse - a malformed row throws rather than being silently
/// skipped or defaulted, per this sprint's "do not silently repair or alter the source dataset" and
/// "no fabricated-data fallback" rules (QDE-012 Sprint 15.18 brief, Sections 2 and 17.12).
/// </summary>
public static class RealMarketOhlcvCsvReader
{
    private static readonly string[] ExpectedHeader =
        { "SessionId", "Timestamp", "Symbol", "TimeFrame", "Open", "High", "Low", "Close", "Volume", "CurrentBar" };

    public static IReadOnlyList<RealMarketBar> Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Real-market OHLCV CSV not found: {path}. This reader never fabricates a substitute dataset.", path);

        string[] lines = File.ReadAllLines(path);
        if (lines.Length == 0)
            throw new InvalidOperationException($"Real-market OHLCV CSV is empty (no header): {path}");

        string[] header = SplitCsvLine(lines[0]);
        if (header.Length != ExpectedHeader.Length)
            throw new InvalidOperationException($"Unexpected OHLCV CSV header shape in {path}: got [{string.Join(",", header)}].");
        for (int i = 0; i < ExpectedHeader.Length; i++)
        {
            if (!string.Equals(header[i], ExpectedHeader[i], StringComparison.Ordinal))
                throw new InvalidOperationException($"Unexpected OHLCV CSV column at position {i} in {path}: expected '{ExpectedHeader[i]}', got '{header[i]}'.");
        }

        var rows = new List<RealMarketBar>(lines.Length - 1);
        for (int lineIndex = 1; lineIndex < lines.Length; lineIndex++)
        {
            if (string.IsNullOrEmpty(lines[lineIndex]))
                continue; // trailing blank line from AppendLine's final newline - not a data row

            string[] fields = SplitCsvLine(lines[lineIndex]);
            if (fields.Length != ExpectedHeader.Length)
                throw new InvalidOperationException($"Malformed OHLCV CSV row {lineIndex + 1} in {path}: expected {ExpectedHeader.Length} fields, got {fields.Length}.");

            rows.Add(new RealMarketBar(
                SessionId: Guid.Parse(fields[0]),
                Timestamp: DateTime.Parse(fields[1], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                Symbol: fields[2],
                TimeFrame: fields[3],
                Open: decimal.Parse(fields[4], CultureInfo.InvariantCulture),
                High: decimal.Parse(fields[5], CultureInfo.InvariantCulture),
                Low: decimal.Parse(fields[6], CultureInfo.InvariantCulture),
                Close: decimal.Parse(fields[7], CultureInfo.InvariantCulture),
                Volume: decimal.Parse(fields[8], CultureInfo.InvariantCulture),
                CurrentBar: int.Parse(fields[9], CultureInfo.InvariantCulture)));
        }

        return rows;
    }

    /// <summary>Minimal CSV field splitter matching ScientificDatasetCollector's own Escape() convention
    /// (quote a field only if it contains a comma/quote/CR/LF, double up embedded quotes). None of the
    /// OHLCV columns need quoting in practice (Symbol/TimeFrame are short exchange codes), but this
    /// stays correct if they ever did.</summary>
    private static string[] SplitCsvLine(string line)
    {
        var fields = new List<string>();
        var current = new System.Text.StringBuilder();
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
