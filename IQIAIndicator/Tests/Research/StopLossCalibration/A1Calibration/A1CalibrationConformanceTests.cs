using System;
using System.Linq;
using IQIAIndicator.Tests.GoldenDatasets;
using IQIAIndicator.Tests.Research.StopLossCalibration.Hybrid;

namespace IQIAIndicator.Tests.Research.StopLossCalibration.A1Calibration;

/// <summary>
/// Sprint 15.14 (brief §32, run before the campaign). Reuses ConformanceTests.RunAll() (Sprint 15.10)
/// unmodified for grid/dataset/split/scale/horizon/source-immutability - never re-verifies those with
/// new code. Adds only what that suite doesn't already cover: the 9-independent/2-alias dataset labels
/// this sprint's reductions depend on, TRAIN/VALIDATION/TEST non-leakage, that A1 here delegates to the
/// exact same CandidateStopDistance.Compute(A1,...) used since Sprint 15.10, and that MeanStopRatio==K
/// exactly for a spot-checked entry set.
/// </summary>
public static class A1CalibrationConformanceTests
{
    public static void RunAll()
    {
        ConformanceTests.RunAll();

        AssertIndependentDatasetCatalogMatchesSpec();
        AssertNoTrainValidationTestLeakage();
        AssertA1DelegatesToCandidateStopDistance();
        AssertMeanStopRatioEqualsKExactly();
    }

    private static void AssertIndependentDatasetCatalogMatchesSpec()
    {
        string[] expectedIndependent =
        {
            "WhiteNoise", "RandomWalk", "AR1_phi0.5", "AR1_phi0.95", "Trending",
            "LowVolatility", "HighVolatility", "StructuralBreak", "VarianceBreak"
        };

        Assert(IndependentDatasetCatalog.IndependentDatasets.Count == 9,
            $"Must have exactly 9 independent datasets. Actual={IndependentDatasetCatalog.IndependentDatasets.Count}.");
        foreach (string name in expectedIndependent)
        {
            Assert(IndependentDatasetCatalog.IndependentDatasets.Contains(name), $"Missing required independent dataset '{name}'.");
        }

        Assert(IndependentDatasetCatalog.AliasDatasets.Count == 2, $"Must have exactly 2 aliases. Actual={IndependentDatasetCatalog.AliasDatasets.Count}.");
        Assert(IndependentDatasetCatalog.AliasOf("MeanRevertingOu_k0.5") == "AR1_phi0.5", "MeanRevertingOu_k0.5 must alias AR1_phi0.5.");
        Assert(IndependentDatasetCatalog.AliasOf("MeanRevertingOu_k0.05") == "AR1_phi0.95", "MeanRevertingOu_k0.05 must alias AR1_phi0.95.");

        foreach (string dataset in CampaignDatasetCatalog.Generators.Keys)
        {
            bool isIndependent = IndependentDatasetCatalog.IsIndependent(dataset);
            bool isAlias = IndependentDatasetCatalog.AliasDatasets.ContainsKey(dataset);
            Assert(isIndependent != isAlias, $"Dataset '{dataset}' must be exactly one of independent/alias.");
        }
    }

    private static void AssertNoTrainValidationTestLeakage()
    {
        foreach (string dataset in IndependentDatasetCatalog.IndependentDatasets)
        {
            decimal[] train = CampaignDatasetCatalog.Build(dataset, "TRAIN", 1m);
            decimal[] validation = CampaignDatasetCatalog.Build(dataset, "VALIDATION", 1m);
            decimal[] test = CampaignDatasetCatalog.Build(dataset, "TEST", 1m);

            Assert(!train.SequenceEqual(validation), $"{dataset}: TRAIN and VALIDATION series must differ (different seeds) - identical series would mean a split leak.");
            Assert(!train.SequenceEqual(test), $"{dataset}: TRAIN and TEST series must differ.");
            Assert(!validation.SequenceEqual(test), $"{dataset}: VALIDATION and TEST series must differ.");
        }
    }

    private static void AssertA1DelegatesToCandidateStopDistance()
    {
        decimal[] series = SyntheticSeriesCatalog.WhiteNoise(600, CampaignDatasetCatalog.TrainSeed);
        var entries = CalibrationEntryBuilder.BuildEntries("WhiteNoise", series, CampaignGrids.Horizon);
        Assert(entries.Count > 0, "Expected eligible entries on the WhiteNoise sample.");

        foreach (var entry in entries.Take(20))
        {
            foreach (double k in new[] { 0.25, 2.0, 5.0, 10.0 })
            {
                double? viaDirect = CandidateStopDistance.Compute(CandidateKind.A1, entry, k);
                double? expected = entry.Metrics.InnovationStd is double std && std > 0.0 ? k * std : (double?)null;
                Assert(viaDirect == expected, $"A1 StopDistance must equal k*InnovationStd exactly. k={k}, Actual={viaDirect}, Expected={expected}.");
            }
        }
    }

    private static void AssertMeanStopRatioEqualsKExactly()
    {
        decimal[] series = SyntheticSeriesCatalog.HighVolatility(600, CampaignDatasetCatalog.TrainSeed);
        var entries = CalibrationEntryBuilder.BuildEntries("HighVolatility", series, CampaignGrids.Horizon);
        Assert(entries.Count > 0, "Expected eligible entries on the HighVolatility sample.");

        foreach (double k in new[] { 0.25, 1.0, 2.75, 10.0 })
        {
            CandidateAggregateResult result = CandidateAggregator.Aggregate("A1", "HighVolatility", "TRAIN", 1m, CandidateKind.A1, k, null, entries);
            Assert(!double.IsNaN(result.MeanStopRatio), $"MeanStopRatio must be computable for A1 at k={k}.");
            Assert(Math.Abs(result.MeanStopRatio - k) < 1e-9, $"MeanStopRatio must equal K exactly for A1 (InnovationStd-based, no scale artifact). k={k}, Actual={result.MeanStopRatio}.");
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
