using System;
using System.Collections.Generic;
using System.Linq;
using IQIAIndicator.Tests.GoldenDatasets;

namespace IQIAIndicator.Tests.Research.StopLossCalibration.Hybrid;

/// <summary>
/// Sprint 15.13 (brief §5, mandatory before any campaign). StopLossCalibrationPocTests already proves
/// BarMetrics as a WHOLE is look-ahead safe (byte-identical full-vs-truncated). This test isolates that
/// same proof specifically to the three fields this sprint's HYBRID definition depends on -
/// VolatilityRegime, CurrentVolatility, VolatilityPercentile - across more datasets/bars than the
/// original POC covered, including VarianceBreak (where the volatility regime changes mid-series, the
/// case most likely to expose a look-ahead leak if one existed) and Trending. Reuses
/// BarMetricsComputer.Compute unmodified; this file computes nothing itself.
///
/// If AssertVolatilityFieldsIdenticalUnderTruncation ever fails, the brief requires stopping immediately
/// and reporting the problem, not working around it - this method throws, it does not silently skip.
/// </summary>
public static class VolatilityRegimeLookAheadAuditTests
{
    private static readonly Dictionary<string, int> ObservedRegimeCounts = new();

    public static void RunAll()
    {
        AssertVolatilityFieldsIdenticalUnderTruncation();
        ReportObservedRegimeDistribution();
    }

    private static void AssertVolatilityFieldsIdenticalUnderTruncation()
    {
        var datasets = new (string Name, decimal[] Series)[]
        {
            ("WhiteNoise", SyntheticSeriesCatalog.WhiteNoise(600, 42UL)),
            ("VarianceBreak", SyntheticSeriesCatalog.VarianceBreak(600, 42UL)),
            ("Trending", SyntheticSeriesCatalog.Trending(600, 42UL)),
            ("RandomWalk", SyntheticSeriesCatalog.RandomWalk(600, 42UL)),
            ("HighVolatility", SyntheticSeriesCatalog.HighVolatility(600, 42UL)),
        };

        int[] barIndexes = { 30, 60, 100, 150, 250, 299, 300, 301, 350, 450, 598 };

        foreach ((string name, decimal[] series) in datasets)
        {
            foreach (int barIndex in barIndexes)
            {
                if (barIndex >= series.Length) continue;

                BarMetrics full = BarMetricsComputer.Compute(series, barIndex);
                decimal[] truncated = series.Take(barIndex + 1).ToArray();
                BarMetrics fromTruncated = BarMetricsComputer.Compute(truncated, barIndex);

                Assert(full.VolatilityRegime == fromTruncated.VolatilityRegime,
                    $"VolatilityRegime must be identical whether computed against the full series or a series truncated right after this bar - a difference means future data leaked in. Dataset={name}, Bar={barIndex}, Full={full.VolatilityRegime}, Truncated={fromTruncated.VolatilityRegime}.");

                Assert(full.CurrentVolatility == fromTruncated.CurrentVolatility,
                    $"CurrentVolatility must be identical under truncation. Dataset={name}, Bar={barIndex}, Full={full.CurrentVolatility}, Truncated={fromTruncated.CurrentVolatility}.");

                Assert(full.VolatilityPercentile == fromTruncated.VolatilityPercentile,
                    $"VolatilityPercentile must be identical under truncation. Dataset={name}, Bar={barIndex}, Full={full.VolatilityPercentile}, Truncated={fromTruncated.VolatilityPercentile}.");
            }
        }
    }

    // Not an assertion - an honest empirical record of which regimes synthetic data actually produces,
    // for the report's audit section. ClassifyVolatilityRegime's UNKNOWN branch requires a non-finite
    // relativeVolatility/percentile, which the current VolatilityModel formula may never actually
    // produce (both are guarded against division by zero/empty windows) - this loop checks whether that
    // is true in practice rather than assuming it from reading the code alone.
    private static void ReportObservedRegimeDistribution()
    {
        ObservedRegimeCounts.Clear();
        foreach (string dataset in CampaignDatasetCatalog.Generators.Keys)
        {
            foreach (string split in CampaignDatasetCatalog.Splits)
            {
                decimal[] series = CampaignDatasetCatalog.Build(dataset, split, 1m);
                IReadOnlyList<CalibrationEntry> entries = CalibrationEntryBuilder.BuildEntries(dataset, series, CampaignGrids.Horizon);
                foreach (CalibrationEntry entry in entries)
                {
                    string key = entry.Metrics.VolatilityRegime ?? "NULL";
                    ObservedRegimeCounts[key] = ObservedRegimeCounts.GetValueOrDefault(key) + 1;
                }
            }
        }

        Console.WriteLine("Observed VolatilityRegime distribution (all 11 dataset labels, TRAIN/VALIDATION/TEST, scale=1):");
        foreach (KeyValuePair<string, int> kvp in ObservedRegimeCounts.OrderByDescending(k => k.Value))
        {
            Console.WriteLine($"  {kvp.Key}: {kvp.Value}");
        }
    }

    public static IReadOnlyDictionary<string, int> LastObservedRegimeCounts => ObservedRegimeCounts;

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
