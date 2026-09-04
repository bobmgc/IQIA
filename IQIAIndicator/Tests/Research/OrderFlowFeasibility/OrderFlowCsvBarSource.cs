using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using IQIAIndicator.Core.MarketData;

namespace IQIAIndicator.Tests.Research.OrderFlowFeasibility;

/// <summary>
/// QDE-017 — READ-ONLY research reader for the order-flow CSV produced by the throwaway ATAS indicator
/// <c>OrderFlowExport</c>. Modifies no production type. Demonstrates that the existing production model
/// (<see cref="HistoricalBar"/>'s BidVolume/AskVolume/Delta/OpenInterest slots,
/// <see cref="HistoricalSeries.Create"/>, both public) can carry per-bar order flow with ZERO production
/// change: <see cref="ToHistoricalSeries"/> fills those slots directly.
///
/// CSV columns (14): timestamp,open,high,low,close,volume,bid,ask,delta,maxDelta,minDelta,oi,ticks,betweens
/// </summary>
public sealed record OrderFlowBar(
    DateTime Timestamp,
    decimal Open, decimal High, decimal Low, decimal Close, decimal Volume,
    decimal Bid, decimal Ask, decimal Delta, decimal MaxDelta, decimal MinDelta,
    decimal OI, decimal Ticks, decimal Betweens);

public static class OrderFlowCsvBarSource
{
    private static readonly string[] ExpectedHeader =
    {
        "timestamp", "open", "high", "low", "close", "volume",
        "bid", "ask", "delta", "maxDelta", "minDelta", "oi", "ticks", "betweens"
    };

    /// <summary>Every row of the CSV, verbatim, in file order. No filtering.</summary>
    public static IReadOnlyList<OrderFlowBar> ReadRaw(string path)
    {
        string[] lines = File.ReadAllLines(path);
        if (lines.Length < 2)
            throw new InvalidDataException($"{path}: fewer than 2 lines.");

        string[] header = lines[0].Split(',');
        if (header.Length != ExpectedHeader.Length)
            throw new InvalidDataException(
                $"{path}: expected {ExpectedHeader.Length} columns, got {header.Length} ([{lines[0]}]).");
        for (int i = 0; i < ExpectedHeader.Length; i++)
            if (!string.Equals(header[i].Trim(), ExpectedHeader[i], StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"{path}: column {i} is '{header[i]}', expected '{ExpectedHeader[i]}'.");

        var inv = CultureInfo.InvariantCulture;
        var rows = new List<OrderFlowBar>(lines.Length - 1);
        for (int i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            string[] f = lines[i].Split(',');
            if (f.Length != ExpectedHeader.Length)
                throw new InvalidDataException($"{path}: row {i + 1} has {f.Length} fields, expected {ExpectedHeader.Length}.");

            rows.Add(new OrderFlowBar(
                DateTime.Parse(f[0], inv, DateTimeStyles.RoundtripKind),
                decimal.Parse(f[1], inv), decimal.Parse(f[2], inv), decimal.Parse(f[3], inv),
                decimal.Parse(f[4], inv), decimal.Parse(f[5], inv),
                decimal.Parse(f[6], inv), decimal.Parse(f[7], inv), decimal.Parse(f[8], inv),
                decimal.Parse(f[9], inv), decimal.Parse(f[10], inv),
                decimal.Parse(f[11], inv), decimal.Parse(f[12], inv), decimal.Parse(f[13], inv)));
        }
        return rows;
    }

    /// <summary>
    /// Analysis-ready rows: raw rows minus (a) the last row (the currently-forming bar), (b) rows with
    /// volume &lt;= 0 (empty session slots), (c) any row whose timestamp is not strictly greater than the
    /// previous kept row (dedup / out-of-order guard - same discipline as HistoricalSeries.TryCreate).
    /// </summary>
    public static IReadOnlyList<OrderFlowBar> ReadClean(string path)
    {
        IReadOnlyList<OrderFlowBar> raw = ReadRaw(path);
        var kept = new List<OrderFlowBar>(raw.Count);
        DateTime last = DateTime.MinValue;
        int upper = raw.Count - 1; // drop the forming bar
        for (int i = 0; i < upper; i++)
        {
            OrderFlowBar r = raw[i];
            if (r.Volume <= 0m || r.Open <= 0m || r.Close <= 0m) continue;
            if (r.Timestamp <= last) continue;
            kept.Add(r);
            last = r.Timestamp;
        }
        return kept;
    }

    /// <summary>Newest <c>orderflow_export_*.csv</c> under %LOCALAPPDATA%\IQIA\orderflow, or null.</summary>
    public static string? FindLatest()
    {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IQIA", "orderflow");
        if (!Directory.Exists(dir)) return null;
        return Directory.EnumerateFiles(dir, "orderflow_export_*.csv")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    /// <summary>
    /// Proves the production model carries order flow with zero production change: builds a real
    /// <see cref="HistoricalSeries"/> whose <see cref="HistoricalBar"/>s have BidVolume/AskVolume/Delta/
    /// OpenInterest populated from the CSV. MaxDelta/MinDelta/Ticks/Betweens have no HistoricalBar slot -
    /// the analysis consumes those straight from <see cref="OrderFlowBar"/>.
    /// </summary>
    public static HistoricalSeries ToHistoricalSeries(string symbol, string timeFrame, IReadOnlyList<OrderFlowBar> rows)
        => HistoricalSeries.Create(symbol, timeFrame, "UTC", "ATAS(orderflow-export)",
            rows.Select(r => new HistoricalBar(
                r.Timestamp, r.Open, r.High, r.Low, r.Close, r.Volume,
                BidVolume: r.Bid, AskVolume: r.Ask, Delta: r.Delta, OpenInterest: r.OI)).ToList());
}
