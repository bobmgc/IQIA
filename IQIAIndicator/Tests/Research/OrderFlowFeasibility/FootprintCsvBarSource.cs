using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using IQIAIndicator.Core.MarketData;

namespace IQIAIndicator.Tests.Research.OrderFlowFeasibility;

/// <summary>
/// QDE-019 — READ-ONLY reader for the footprint-feature CSV produced by the throwaway ATAS indicator
/// <c>OrderFlowFootprintExport</c>. Modifies no production type. 18 columns:
/// timestamp,open,high,low,close,volume,delta,fpLevels,pocOffTicks,pocPosInRange,pocConc,top3Conc,
/// maxPosDeltaPosInRange,maxNegDeltaPosInRange,stackedBuyImbTop,stackedSellImbBot,aggSkewTopBot,closePosInRange
/// </summary>
public sealed record FootprintBar(
    DateTime Timestamp, decimal Open, decimal High, decimal Low, decimal Close, decimal Volume, decimal Delta,
    int FpLevels, double PocOffTicks, double PocPosInRange, double PocConc, double Top3Conc,
    double MaxPosDeltaPosInRange, double MaxNegDeltaPosInRange,
    int StackedBuyImbTop, int StackedSellImbBot, double AggSkewTopBot, double ClosePosInRange);

public static class FootprintCsvBarSource
{
    private static readonly string[] Header =
    {
        "timestamp","open","high","low","close","volume","delta","fpLevels",
        "pocOffTicks","pocPosInRange","pocConc","top3Conc",
        "maxPosDeltaPosInRange","maxNegDeltaPosInRange",
        "stackedBuyImbTop","stackedSellImbBot","aggSkewTopBot","closePosInRange"
    };

    public static string? FindLatest()
    {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IQIA", "orderflow");
        if (!Directory.Exists(dir)) return null;
        return Directory.EnumerateFiles(dir, "orderflow_footprint_*.csv")
            .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
    }

    public static IReadOnlyList<FootprintBar> ReadRaw(string path)
    {
        string[] lines = File.ReadAllLines(path);
        if (lines.Length < 2) throw new InvalidDataException($"{path}: <2 lines.");
        string[] h = lines[0].Split(',');
        if (h.Length != Header.Length)
            throw new InvalidDataException($"{path}: expected {Header.Length} cols, got {h.Length}.");
        for (int i = 0; i < Header.Length; i++)
            if (!string.Equals(h[i].Trim(), Header[i], StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"{path}: col {i} '{h[i]}' != '{Header[i]}'.");

        var inv = CultureInfo.InvariantCulture;
        var rows = new List<FootprintBar>(lines.Length - 1);
        for (int i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            string[] f = lines[i].Split(',');
            if (f.Length != Header.Length)
                throw new InvalidDataException($"{path}: row {i + 1} has {f.Length} fields.");
            double D(int j) => double.Parse(f[j], inv);
            rows.Add(new FootprintBar(
                DateTime.Parse(f[0], inv, DateTimeStyles.RoundtripKind),
                decimal.Parse(f[1], inv), decimal.Parse(f[2], inv), decimal.Parse(f[3], inv),
                decimal.Parse(f[4], inv), decimal.Parse(f[5], inv), decimal.Parse(f[6], inv),
                int.Parse(f[7], inv), D(8), D(9), D(10), D(11), D(12), D(13),
                int.Parse(f[14], inv), int.Parse(f[15], inv), D(16), D(17)));
        }
        return rows;
    }

    /// <summary>Raw minus the forming last bar, volume &lt;= 0 rows, and non-increasing timestamps.</summary>
    public static IReadOnlyList<FootprintBar> ReadClean(string path)
    {
        IReadOnlyList<FootprintBar> raw = ReadRaw(path);
        var kept = new List<FootprintBar>(raw.Count);
        DateTime last = DateTime.MinValue;
        for (int i = 0; i < raw.Count - 1; i++)
        {
            FootprintBar r = raw[i];
            if (r.Volume <= 0m || r.Open <= 0m || r.Close <= 0m) continue;
            if (r.Timestamp <= last) continue;
            kept.Add(r);
            last = r.Timestamp;
        }
        return kept;
    }

    public static HistoricalSeries ToHistoricalSeries(string symbol, string timeFrame, IReadOnlyList<FootprintBar> rows)
        => HistoricalSeries.Create(symbol, timeFrame, "UTC", "ATAS(footprint-export)",
            rows.Select(r => new HistoricalBar(
                r.Timestamp, r.Open, r.High, r.Low, r.Close, r.Volume, Delta: r.Delta)).ToList());
}
