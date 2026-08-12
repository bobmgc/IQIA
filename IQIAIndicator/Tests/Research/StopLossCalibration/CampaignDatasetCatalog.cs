using System;
using System.Collections.Generic;
using IQIAIndicator.Tests.GoldenDatasets;

namespace IQIAIndicator.Tests.Research.StopLossCalibration;

/// <summary>
/// Sprint 15.10 (QDE-012 §8/§9). All 11 dataset families, exactly as locked in the protocol - every
/// generator call is a direct, unmodified use of SyntheticSeriesCatalog (no new generator created).
/// Series length (600) and the TRAIN/VALIDATION/TEST seeds (42/43/44) are protocol-locked constants,
/// not tunable per-run parameters.
/// </summary>
public static class CampaignDatasetCatalog
{
    public const int SeriesLength = 600;
    public const ulong TrainSeed = 42UL;
    public const ulong ValidationSeed = 43UL;
    public const ulong TestSeed = 44UL;

    public static readonly IReadOnlyList<decimal> Scales = new[] { 0.01m, 0.1m, 1m, 10m, 100m, 1000m };
    public static readonly IReadOnlyList<string> Splits = new[] { "TRAIN", "VALIDATION", "TEST" };

    public static readonly IReadOnlyDictionary<string, Func<ulong, decimal[]>> Generators =
        new Dictionary<string, Func<ulong, decimal[]>>
        {
            ["WhiteNoise"] = seed => SyntheticSeriesCatalog.WhiteNoise(SeriesLength, seed),
            ["RandomWalk"] = seed => SyntheticSeriesCatalog.RandomWalk(SeriesLength, seed),
            ["AR1_phi0.5"] = seed => SyntheticSeriesCatalog.Ar1(SeriesLength, seed, 0.5m),
            ["AR1_phi0.95"] = seed => SyntheticSeriesCatalog.Ar1(SeriesLength, seed, 0.95m),
            ["MeanRevertingOu_k0.5"] = seed => SyntheticSeriesCatalog.MeanRevertingOu(SeriesLength, seed, 0.5m),
            ["MeanRevertingOu_k0.05"] = seed => SyntheticSeriesCatalog.MeanRevertingOu(SeriesLength, seed, 0.05m),
            ["Trending"] = seed => SyntheticSeriesCatalog.Trending(SeriesLength, seed),
            ["LowVolatility"] = seed => SyntheticSeriesCatalog.LowVolatility(SeriesLength, seed),
            ["HighVolatility"] = seed => SyntheticSeriesCatalog.HighVolatility(SeriesLength, seed),
            ["StructuralBreak"] = seed => SyntheticSeriesCatalog.StructuralBreak(SeriesLength, seed),
            ["VarianceBreak"] = seed => SyntheticSeriesCatalog.VarianceBreak(SeriesLength, seed),
        };

    public static ulong SeedFor(string split) => split switch
    {
        "TRAIN" => TrainSeed,
        "VALIDATION" => ValidationSeed,
        "TEST" => TestSeed,
        _ => throw new ArgumentException($"Unknown split '{split}'.", nameof(split))
    };

    public static decimal[] Build(string datasetName, string split, decimal scale)
    {
        decimal[] baseSeries = Generators[datasetName](SeedFor(split));
        if (scale == 1m)
        {
            return baseSeries;
        }

        var scaled = new decimal[baseSeries.Length];
        for (int i = 0; i < baseSeries.Length; i++)
        {
            scaled[i] = baseSeries[i] * scale;
        }

        return scaled;
    }
}
