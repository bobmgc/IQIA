using System;
using System.Collections.Generic;
using IQIAIndicator.Core.MarketData;
using IQIAIndicator.Tests.GoldenDatasets;

namespace IQIAIndicator.Tests.BacktestTests;

/// <summary>Sprint 15.25 (Lot 14.1). Turns the pre-existing, already-deterministic
/// Tests/GoldenDatasets/SyntheticSeriesCatalog closes (LCG + Box-Muller, always seeded - "no unseeded
/// System.Random is used anywhere in this file", per that catalog's own doc comment) into a synthetic
/// HistoricalSeries usable by BacktestEngine. No new randomness source is introduced by this lot.</summary>
internal static class BacktestTestSeriesBuilder
{
    public static readonly DateTime Anchor = new(2026, 1, 5, 14, 30, 0, DateTimeKind.Utc);

    public static HistoricalSeries BuildFromCloses(
        IReadOnlyList<decimal> closes,
        string symbol = "ES",
        string timeFrame = "M5",
        string timeZone = "UTC",
        string provider = "SyntheticTestSeries")
    {
        var bars = new List<HistoricalBar>(closes.Count);
        for (int i = 0; i < closes.Count; i++)
        {
            decimal close = closes[i];
            decimal open = i == 0 ? close : closes[i - 1];
            decimal high = Math.Max(open, close) + 0.25m;
            decimal low = Math.Min(open, close) - 0.25m;
            bars.Add(new HistoricalBar(Anchor.AddMinutes(5 * i), open, high, low, close, Volume: 100m + i));
        }

        return HistoricalSeries.Create(symbol, timeFrame, timeZone, provider, bars);
    }

    /// <summary>Truncates an already-built series to its first <paramref name="count"/> bars, re-validated
    /// as its own independent HistoricalSeries (used by the look-ahead test to build the "shorter run").</summary>
    public static HistoricalSeries Truncate(HistoricalSeries series, int count)
    {
        var bars = new List<HistoricalBar>(count);
        for (int i = 0; i < count; i++)
            bars.Add(series.Bars[i]);

        return HistoricalSeries.Create(series.Symbol, series.TimeFrame, series.TimeZone, series.Provider, bars);
    }

    public static HistoricalSeries WhiteNoise(int length, ulong seed = 42UL) =>
        BuildFromCloses(SyntheticSeriesCatalog.WhiteNoise(length, seed));

    /// <summary>Sprint 15.25 (Lot 14.3). Ornstein-Uhlenbeck mean-reverting closes - unlike
    /// <see cref="WhiteNoise"/>, this shape gives the real, unmodified Decision/Fusion rules a realistic
    /// chance of arbitrating MarketState.MeanReverting, which is the only regime the current
    /// ScientificModelRegistry/EntryTriggerBuilder pipeline can produce a directional (BUY/SELL) read
    /// for. Still fully deterministic (same LCG/Box-Muller machinery as every other generator in
    /// SyntheticSeriesCatalog - see that class's own doc comment).</summary>
    public static HistoricalSeries MeanRevertingOu(int length, ulong seed = 42UL, decimal kappa = 0.5m) =>
        BuildFromCloses(SyntheticSeriesCatalog.MeanRevertingOu(length, seed, kappa));
}
