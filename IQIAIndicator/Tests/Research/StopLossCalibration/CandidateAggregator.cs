using System;
using System.Collections.Generic;
using System.Linq;

namespace IQIAIndicator.Tests.Research.StopLossCalibration;

/// <summary>
/// Sprint 15.10. Aggregates a fixed set of CalibrationEntry (already built once per series by
/// CalibrationEntryBuilder) into one CandidateAggregateResult for a given (candidate, k[, R-squared])
/// combination. Uses StopLossEvaluator's precomputed excursion paths - never re-walks a price path.
///
/// MAE/MFE are reported over the APPLICABLE population for this exact row (entries where the
/// candidate's stop distance was computable) - not the full entry population. For A1/A2, nothing is
/// ever degenerate, so this equals the full population and stays constant across the whole k-grid
/// (expected, not a bug). For D1/D2, the applicable population shrinks as k approaches |z0| for more
/// entries, or as the R-squared filter rejects more entries - so MAE/MFE can genuinely vary with k/R2.
/// </summary>
public static class CandidateAggregator
{
    public static CandidateAggregateResult Aggregate(
        string candidateName,
        string dataset,
        string split,
        decimal scale,
        CandidateKind kind,
        double k,
        double? rSquaredThreshold,
        IReadOnlyList<CalibrationEntry> entries)
    {
        int total = entries.Count;
        int stopHits = 0, stoppedOut = 0, reverted = 0, undetermined = 0;
        var mae = new List<double>(total);
        var mfe = new List<double>(total);
        var timeToEquilibrium = new List<double>(total);
        var stopDistances = new List<double>(total);
        var stopRatios = new List<double>(total);

        // Ground truth (stop-free): would this entry have reached equilibrium at all within the
        // horizon, regardless of any candidate stop? Used to classify a StoppedOut fate as a false
        // invalidation (would have worked, got cut early) or a true invalidation (wasn't going to
        // revert within the horizon anyway) - Sprint 15.10 §Phase 8's "faux/vrais invalidations".
        int groundTruthReverts = 0;
        int falseInvalidations = 0;
        int trueInvalidations = 0;

        foreach (CalibrationEntry entry in entries)
        {
            double? stopDistance = CandidateStopDistance.Compute(kind, entry, k, rSquaredThreshold);
            if (stopDistance is null)
            {
                continue;
            }

            mae.Add(entry.Outcome.MaxAdverseExcursion);
            mfe.Add(entry.Outcome.MaxFavorableExcursion);
            stopDistances.Add(stopDistance.Value);

            // Sprint 15.11 fix: the ratio's denominator must match the sigma the candidate's own
            // formula actually uses - A2 is built on CurrentVolatility, not InnovationStd (A1/D1/D2
            // all use InnovationStd). Previously this was InnovationStd unconditionally, which made
            // A2's MeanStopRatio incomparable to its own k (Sprint 15.10 campaign report, Limitations
            // §19). CurrentVolatility can be null/zero/non-finite for a given entry - in that case the
            // entry is simply excluded from the ratio (never a fabricated 0/NaN/Infinity value mixed
            // into a real mean), matching the harness's existing not-available convention.
            double? ratioSigma = kind == CandidateKind.A2 ? entry.Metrics.CurrentVolatility : entry.Metrics.InnovationStd;
            if (ratioSigma is double sigma && double.IsFinite(sigma) && sigma > 0.0)
            {
                stopRatios.Add(stopDistance.Value / sigma);
            }

            (bool stopHit, int? stopHitBar, EntryFate fate) = StopLossEvaluator.Evaluate(entry.Outcome, stopDistance.Value);
            if (stopHit) stopHits++;
            switch (fate)
            {
                case EntryFate.StoppedOut: stoppedOut++; break;
                case EntryFate.Reverted: reverted++; break;
                case EntryFate.Undetermined: undetermined++; break;
            }

            bool wouldHaveReverted = entry.Outcome.EquilibriumBar.HasValue;
            if (wouldHaveReverted)
            {
                groundTruthReverts++;
                timeToEquilibrium.Add(entry.Outcome.EquilibriumBar!.Value);
            }

            if (fate == EntryFate.StoppedOut)
            {
                if (wouldHaveReverted) falseInvalidations++;
                else trueInvalidations++;
            }
        }

        int applicable = mae.Count;
        int degenerate = total - applicable;
        int groundTruthNonReverts = applicable - groundTruthReverts;

        return new CandidateAggregateResult(
            candidateName,
            dataset,
            split,
            scale,
            k,
            rSquaredThreshold,
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
            Rate(falseInvalidations, groundTruthReverts),
            Rate(trueInvalidations, groundTruthNonReverts),
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
            Mean(stopRatios));
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
