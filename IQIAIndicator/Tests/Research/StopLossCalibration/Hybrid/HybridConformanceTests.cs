using System;
using System.Collections.Generic;
using System.Linq;

namespace IQIAIndicator.Tests.Research.StopLossCalibration.Hybrid;

/// <summary>
/// Sprint 15.13 (brief §9, "write conformance tests before the campaign"). Structural gates on the
/// HYBRID definition itself - run BEFORE the full campaign, cheap, no k-grid sweep. If any of these
/// fail, the HYBRID definition is wrong and the campaign must not run.
/// </summary>
public static class HybridConformanceTests
{
    private const double TestK = 2.0;

    public static void RunAll()
    {
        AssertRegimeRoutingMatchesLockedRule();
        AssertStrictExcludesUnknownFromApplicablePopulation();
        AssertConservativeRoutesUnknownToA1();
        AssertA1A2LegsMatchBaselineExactly();
        AssertSharesSumToOneOverApplicablePopulation();
    }

    // A mix of datasets/scales so every regime (LOW/MEDIUM/HIGH, and UNKNOWN if the model ever produces
    // it) is plausibly represented.
    private static IReadOnlyList<CalibrationEntry> SampleEntries()
    {
        var entries = new List<CalibrationEntry>();
        foreach (string dataset in new[] { "WhiteNoise", "HighVolatility", "LowVolatility", "VarianceBreak", "Trending", "RandomWalk" })
        {
            foreach (decimal scale in new[] { 0.01m, 1m, 100m })
            {
                decimal[] series = CampaignDatasetCatalog.Build(dataset, "TRAIN", scale);
                entries.AddRange(CalibrationEntryBuilder.BuildEntries(dataset, series, CampaignGrids.Horizon));
            }
        }

        return entries;
    }

    private static void AssertRegimeRoutingMatchesLockedRule()
    {
        IReadOnlyList<CalibrationEntry> entries = SampleEntries();
        Assert(entries.Count > 0, "Expected a non-empty sample for the regime-routing conformance check.");

        foreach (HybridCandidateKind kind in new[] { HybridCandidateKind.HybridStrict, HybridCandidateKind.HybridConservative })
        {
            foreach (CalibrationEntry entry in entries)
            {
                (double? distance, HybridLeg leg) = HybridStopDistance.Compute(kind, entry, TestK);
                string? regime = entry.Metrics.VolatilityRegime;

                if (regime == "HIGH")
                {
                    Assert(leg == HybridLeg.A2, $"{kind}: HIGH regime must route to the A2 leg. Bar={entry.EntryBarIndex}, Dataset={entry.SeriesName}, Leg={leg}.");
                }
                else if (regime is "LOW" or "MEDIUM")
                {
                    Assert(leg == HybridLeg.A1, $"{kind}: {regime} regime must route to the A1 leg. Bar={entry.EntryBarIndex}, Dataset={entry.SeriesName}, Leg={leg}.");
                }
                else
                {
                    Assert(leg == HybridLeg.Unknown, $"{kind}: non-LOW/MEDIUM/HIGH regime must route to the Unknown leg. Bar={entry.EntryBarIndex}, Dataset={entry.SeriesName}, Regime={regime}, Leg={leg}.");
                    if (kind == HybridCandidateKind.HybridStrict)
                    {
                        Assert(distance is null, $"HybridStrict: non-LOW/MEDIUM/HIGH regime must be NOT_APPLICABLE (null stop distance). Bar={entry.EntryBarIndex}, Dataset={entry.SeriesName}.");
                    }
                }

                if (leg == HybridLeg.A2)
                {
                    Assert(regime == "HIGH", $"{kind}: A2 leg selected outside HIGH regime. Regime={regime}, Bar={entry.EntryBarIndex}, Dataset={entry.SeriesName}.");
                }
            }
        }
    }

    private static void AssertStrictExcludesUnknownFromApplicablePopulation()
    {
        IReadOnlyList<CalibrationEntry> entries = SampleEntries();
        var unknownEntries = entries.Where(e => e.Metrics.VolatilityRegime is not ("LOW" or "MEDIUM" or "HIGH")).ToList();

        // Not every sample necessarily contains a non-LOW/MEDIUM/HIGH entry - that's fine, the assertion
        // only needs to hold WHEN one exists; see VolatilityRegimeLookAheadAuditTests for the empirical
        // record of whether UNKNOWN is ever actually observed on this project's synthetic data.
        foreach (CalibrationEntry entry in unknownEntries)
        {
            (double? distance, HybridLeg leg) = HybridStopDistance.Compute(HybridCandidateKind.HybridStrict, entry, TestK);
            Assert(distance is null, $"HybridStrict must never produce a stop distance for a non-LOW/MEDIUM/HIGH regime. Bar={entry.EntryBarIndex}, Dataset={entry.SeriesName}, Regime={entry.Metrics.VolatilityRegime}.");
            Assert(leg == HybridLeg.Unknown, "Leg bookkeeping must mark this Unknown regardless.");
        }
    }

    private static void AssertConservativeRoutesUnknownToA1()
    {
        IReadOnlyList<CalibrationEntry> entries = SampleEntries();
        var unknownEntries = entries.Where(e => e.Metrics.VolatilityRegime is not ("LOW" or "MEDIUM" or "HIGH")).ToList();

        foreach (CalibrationEntry entry in unknownEntries)
        {
            (double? hybridDistance, HybridLeg leg) = HybridStopDistance.Compute(HybridCandidateKind.HybridConservative, entry, TestK);
            double? a1Distance = CandidateStopDistance.Compute(CandidateKind.A1, entry, TestK);
            Assert(hybridDistance == a1Distance, $"HybridConservative's fallback for a non-LOW/MEDIUM/HIGH regime must equal the plain A1 formula exactly. Bar={entry.EntryBarIndex}, Dataset={entry.SeriesName}, Hybrid={hybridDistance}, A1={a1Distance}.");
            Assert(leg == HybridLeg.Unknown, "Leg bookkeeping must still mark this Unknown, distinct from a genuine LOW/MEDIUM routing.");
        }
    }

    private static void AssertA1A2LegsMatchBaselineExactly()
    {
        IReadOnlyList<CalibrationEntry> entries = SampleEntries();
        foreach (CalibrationEntry entry in entries)
        {
            double? a1Hybrid = HybridStopDistance.Compute(HybridCandidateKind.A1, entry, TestK).StopDistance;
            double? a1Baseline = CandidateStopDistance.Compute(CandidateKind.A1, entry, TestK);
            Assert(a1Hybrid == a1Baseline, $"Hybrid's A1 leg must delegate to CandidateStopDistance exactly, never reimplement the formula. Bar={entry.EntryBarIndex}, Hybrid={a1Hybrid}, Baseline={a1Baseline}.");

            double? a2Hybrid = HybridStopDistance.Compute(HybridCandidateKind.A2, entry, TestK).StopDistance;
            double? a2Baseline = CandidateStopDistance.Compute(CandidateKind.A2, entry, TestK);
            Assert(a2Hybrid == a2Baseline, $"Hybrid's A2 leg must delegate to CandidateStopDistance exactly, never reimplement the formula. Bar={entry.EntryBarIndex}, Hybrid={a2Hybrid}, Baseline={a2Baseline}.");
        }
    }

    private static void AssertSharesSumToOneOverApplicablePopulation()
    {
        foreach (string dataset in new[] { "WhiteNoise", "VarianceBreak", "Trending" })
        {
            decimal[] series = CampaignDatasetCatalog.Build(dataset, "TRAIN", 1m);
            IReadOnlyList<CalibrationEntry> entries = CalibrationEntryBuilder.BuildEntries(dataset, series, CampaignGrids.Horizon);
            Assert(entries.Count > 0, $"Expected eligible entries on {dataset}.");

            foreach (HybridCandidateKind kind in new[] { HybridCandidateKind.HybridStrict, HybridCandidateKind.HybridConservative })
            {
                HybridAggregateResult result = HybridCandidateAggregator.Aggregate("Test", dataset, "TRAIN", 1m, kind, TestK, entries);
                if (result.ApplicableEntries == 0) continue;

                double sum = (result.HybridA1Share ?? 0) + (result.HybridA2Share ?? 0) + (result.HybridUnknownShare ?? 0);
                Assert(Math.Abs(sum - 1.0) < 1e-9, $"{kind} on {dataset}: HybridA1Share+HybridA2Share+HybridUnknownShare must sum to 1 over the applicable population. Sum={sum}.");
            }
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
