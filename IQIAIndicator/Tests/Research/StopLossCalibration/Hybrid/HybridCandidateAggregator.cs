using System;
using System.Collections.Generic;
using System.Linq;

namespace IQIAIndicator.Tests.Research.StopLossCalibration.Hybrid;

/// <summary>
/// Sprint 15.13. Aggregates a fixed set of CalibrationEntry into one HybridAggregateResult for a given
/// (candidate, dataset, split, scale, k) combination. Reuses StopLossEvaluator.Evaluate and
/// entry.Outcome.MaxAdverseExcursion/MaxFavorableExcursion verbatim - never re-walks a price path,
/// never recomputes MAE/MFE. The Mean/Percentile/Rate helpers below are the same formulas as
/// CandidateAggregator.cs (Sprint 15.10) - re-declared locally rather than modifying that file's
/// private helpers or its CandidateKind-keyed signature (HYBRID needs a per-entry ratio denominator,
/// which that method cannot express without modification), to keep this sprint's diff purely additive.
/// </summary>
public static class HybridCandidateAggregator
{
    public static HybridAggregateResult Aggregate(
        string candidateName,
        string dataset,
        string split,
        decimal scale,
        HybridCandidateKind kind,
        double k,
        IReadOnlyList<CalibrationEntry> entries)
    {
        bool independent = IndependentDatasetCatalog.IsIndependent(dataset);
        string aliasOf = IndependentDatasetCatalog.AliasOf(dataset);
        bool isHybrid = kind is HybridCandidateKind.HybridStrict or HybridCandidateKind.HybridConservative;

        int total = entries.Count;
        int stopHits = 0, stoppedOut = 0, reverted = 0, undetermined = 0;
        var mae = new List<double>(total);
        var mfe = new List<double>(total);
        var timeToEquilibrium = new List<double>(total);
        var stopDistances = new List<double>(total);
        var stopRatios = new List<double>(total);
        int a1Leg = 0, a2Leg = 0, unknownLeg = 0;

        foreach (CalibrationEntry entry in entries)
        {
            (double? stopDistance, HybridLeg leg) = HybridStopDistance.Compute(kind, entry, k);
            if (stopDistance is null)
            {
                continue;
            }

            mae.Add(entry.Outcome.MaxAdverseExcursion);
            mfe.Add(entry.Outcome.MaxFavorableExcursion);
            stopDistances.Add(stopDistance.Value);

            switch (leg)
            {
                case HybridLeg.A1: a1Leg++; break;
                case HybridLeg.A2: a2Leg++; break;
                case HybridLeg.Unknown: unknownLeg++; break;
            }

            // Ratio denominator must match the sigma the leg actually used for THIS entry (Sprint
            // 15.11's A2 ratio-denominator fix, generalized per-entry rather than per-candidate, since a
            // single Hybrid row can contain entries that used either sigma).
            double? ratioSigma = leg == HybridLeg.A2 ? entry.Metrics.CurrentVolatility : entry.Metrics.InnovationStd;
            if (ratioSigma is double sigma && double.IsFinite(sigma) && sigma > 0.0)
            {
                stopRatios.Add(stopDistance.Value / sigma);
            }

            (bool stopHit, int? _, EntryFate fate) = StopLossEvaluator.Evaluate(entry.Outcome, stopDistance.Value);
            if (stopHit) stopHits++;
            switch (fate)
            {
                case EntryFate.StoppedOut: stoppedOut++; break;
                case EntryFate.Reverted: reverted++; break;
                case EntryFate.Undetermined: undetermined++; break;
            }

            if (entry.Outcome.EquilibriumBar.HasValue)
            {
                timeToEquilibrium.Add(entry.Outcome.EquilibriumBar.Value);
            }
        }

        int applicable = mae.Count;
        int degenerate = total - applicable;

        double? a1Share = null, a2Share = null, unknownShare = null;
        if (isHybrid && applicable > 0)
        {
            a1Share = a1Leg / (double)applicable;
            a2Share = a2Leg / (double)applicable;
            unknownShare = unknownLeg / (double)applicable;
        }

        return new HybridAggregateResult(
            candidateName,
            dataset,
            independent,
            aliasOf,
            split,
            scale,
            k,
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
            Mean(mae),
            Percentile(mae, 0.50),
            Percentile(mae, 0.75),
            Percentile(mae, 0.90),
            Mean(mfe),
            Percentile(mfe, 0.50),
            Percentile(mfe, 0.75),
            Percentile(mfe, 0.90),
            timeToEquilibrium.Count > 0 ? Percentile(timeToEquilibrium, 0.50) : null,
            Mean(stopDistances),
            Mean(stopRatios),
            a1Share,
            a2Share,
            unknownShare);
    }

    private static double Rate(int count, int total) => total == 0 ? double.NaN : count / (double)total;

    private static double Mean(List<double> values) => values.Count == 0 ? double.NaN : values.Average();

    private static double Percentile(List<double> values, double p)
    {
        if (values.Count == 0)
        {
            return double.NaN;
        }

        double[] sorted = values.OrderBy(v => v).ToArray();
        if (sorted.Length == 1)
        {
            return sorted[0];
        }

        double rank = p * (sorted.Length - 1);
        int lower = (int)Math.Floor(rank);
        int upper = (int)Math.Ceiling(rank);
        if (lower == upper)
        {
            return sorted[lower];
        }

        double fraction = rank - lower;
        return sorted[lower] + fraction * (sorted[upper] - sorted[lower]);
    }
}
