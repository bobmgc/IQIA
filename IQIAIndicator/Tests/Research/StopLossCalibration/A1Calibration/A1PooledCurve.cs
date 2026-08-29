using System;
using System.Collections.Generic;
using System.Linq;

namespace IQIAIndicator.Tests.Research.StopLossCalibration.A1Calibration;

/// <summary>
/// Sprint 15.14. Builds one synthetic "POOLED" CandidateAggregateResult per k by summing counts across
/// several per-dataset rows at the same (split, scale, k) and recomputing rates from the sums (never
/// averaging rates directly - a dataset with more applicable entries must weigh more). MAE/MFE
/// percentiles cannot be re-derived from already-reduced percentiles, so they are reported as the
/// entry-count-weighted MEAN of the per-dataset percentiles - documented here as an approximation, not
/// a re-computed pooled percentile, exactly as Sprint 15.13's HybridTrainValidationTestAnalysis did for
/// the same reason.
///
/// Only reuses the existing CandidateAggregateResult shape (no new record) so the pooled curve can be
/// fed straight into StableRegionAnalyzer.FindStableWindows unmodified - that method only reads
/// ReversionRate, FalseInvalidationRate and K from each row, all of which are populated correctly here.
/// </summary>
public static class A1PooledCurve
{
    public const string PooledScope = "POOLED";

    /// <summary>Pools every row in <paramref name="rows"/> (already filtered to the desired split/scale
    /// and to independent datasets only) into one row per distinct K, ordered by K.</summary>
    public static IReadOnlyList<CandidateAggregateResult> Build(IReadOnlyList<CandidateAggregateResult> rows)
    {
        return rows
            .GroupBy(r => r.K)
            .OrderBy(g => g.Key)
            .Select(PoolOneK)
            .ToList();
    }

    private static CandidateAggregateResult PoolOneK(IGrouping<double, CandidateAggregateResult> group)
    {
        List<CandidateAggregateResult> list = group.ToList();
        int total = list.Sum(r => r.TotalEntries);
        int applicable = list.Sum(r => r.ApplicableEntries);
        int degenerate = list.Sum(r => r.DegenerateEntries);
        int stopHits = list.Sum(r => r.StopHits);
        int stoppedOut = list.Sum(r => r.StoppedOut);
        int reverted = list.Sum(r => r.Reverted);
        int undetermined = list.Sum(r => r.Undetermined);

        // Ground-truth denominators for False/TrueInvalidationRate are not carried individually on
        // CandidateAggregateResult, so the pooled versions of those two EXPLORATORY metrics are
        // themselves recomputed as the entry-weighted mean of the per-dataset rates (never used for
        // stability gating regardless - see StableRegionAnalyzer).
        double falseInvalid = WeightedMean(list.Select(r => (r.FalseInvalidationRate, r.ApplicableEntries)));
        double trueInvalid = WeightedMean(list.Select(r => (r.TrueInvalidationRate, r.ApplicableEntries)));

        return new CandidateAggregateResult(
            "A1",
            PooledScope,
            list[0].Split,
            list[0].Scale,
            group.Key,
            null,
            total,
            applicable,
            degenerate,
            stopHits,
            stoppedOut,
            reverted,
            undetermined,
            Rate(stopHits, applicable),
            Rate(reverted, applicable),
            Rate(undetermined, applicable),
            Rate(stoppedOut, applicable),
            falseInvalid,
            trueInvalid,
            WeightedMean(list.Select(r => (r.MaeMean, r.ApplicableEntries))),
            WeightedMean(list.Select(r => (r.MaeMedian, r.ApplicableEntries))),
            WeightedMean(list.Select(r => (r.MaeP75, r.ApplicableEntries))),
            WeightedMean(list.Select(r => (r.MaeP90, r.ApplicableEntries))),
            WeightedMean(list.Select(r => (r.MfeMean, r.ApplicableEntries))),
            WeightedMean(list.Select(r => (r.MfeMedian, r.ApplicableEntries))),
            WeightedMean(list.Select(r => (r.MfeP75, r.ApplicableEntries))),
            WeightedMean(list.Select(r => (r.MfeP90, r.ApplicableEntries))),
            list.Any(r => r.MedianTimeToEquilibrium.HasValue)
                ? WeightedMean(list.Where(r => r.MedianTimeToEquilibrium.HasValue).Select(r => (r.MedianTimeToEquilibrium!.Value, r.ApplicableEntries)))
                : null,
            WeightedMean(list.Select(r => (r.MeanStopDistance, r.ApplicableEntries))),
            WeightedMean(list.Select(r => (r.MeanStopRatio, r.ApplicableEntries))));
    }

    private static double Rate(int count, int total) => total == 0 ? double.NaN : count / (double)total;

    private static double WeightedMean(IEnumerable<(double Value, int Weight)> values)
    {
        List<(double Value, int Weight)> list = values.Where(v => !double.IsNaN(v.Value)).ToList();
        int totalWeight = list.Sum(v => v.Weight);
        return totalWeight == 0 ? double.NaN : list.Sum(v => v.Value * v.Weight) / totalWeight;
    }
}
