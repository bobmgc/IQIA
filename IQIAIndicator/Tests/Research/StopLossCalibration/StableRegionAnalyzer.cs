using System;
using System.Collections.Generic;
using System.Linq;

namespace IQIAIndicator.Tests.Research.StopLossCalibration;

/// <summary>
/// Sprint 15.10 (QDE-012 §11/§17, amended after the Conformance Gate). A run of >=5 consecutive
/// k-grid points where ReversionRate - the only protocol-defined metric used for gating - stays within
/// CvThreshold coefficient of variation, i.e. the curve is flat there, not merely passing through a
/// single good point.
///
/// MeanFalseInvalidationRate/FalseInvalidationRateCv are carried on this record for DESCRIPTIVE
/// reporting only (QDE-012 §17: FalseInvalidationRate is an exploratory metric). They play no role in
/// whether a window qualifies as stable - the Conformance Gate found an earlier version of this class
/// gating on both CV(ReversionRate) AND CV(FalseInvalidationRate), which silently turned an exploratory
/// metric into a selection criterion. Fixed here: gating uses ReversionRate alone.
/// </summary>
public sealed record StableWindow(
    int StartKIndex,
    int EndKIndex,
    double StartK,
    double EndK,
    double MeanReversionRate,
    double ReversionRateCv,
    double MeanFalseInvalidationRate,
    double FalseInvalidationRateCv);

/// <summary>
/// Sprint 15.10. Finds flat (low coefficient-of-variation) regions of a candidate's k-grid curve, per
/// the anti-overfitting instruction: a single excellent k surrounded by mediocre neighbors is evidence
/// of overfitting, not a finding.
///
/// This 10% CV threshold is an exploratory diagnostic criterion and is not a protocol-defined trading
/// parameter (QDE-012 §17). A window found "stable" here must be reported as "flat under a 10%
/// coefficient-of-variation diagnostic," never as an unqualified scientific proof of stability.
/// </summary>
public static class StableRegionAnalyzer
{
    private const int WindowSize = 5;
    private const double CvThreshold = 0.10;

    public static IReadOnlyList<StableWindow> FindStableWindows(IReadOnlyList<CandidateAggregateResult> curveOrderedByK)
    {
        var candidates = new List<StableWindow>();
        int n = curveOrderedByK.Count;

        for (int start = 0; start + WindowSize <= n; start++)
        {
            List<CandidateAggregateResult> slice = curveOrderedByK.Skip(start).Take(WindowSize).ToList();
            if (slice.Any(r => double.IsNaN(r.ReversionRate)))
            {
                continue;
            }

            double revCv = CoefficientOfVariation(slice.Select(r => r.ReversionRate));

            // FalseInvalidationRate is computed for the descriptive fields below ONLY - it never
            // participates in the stable/not-stable decision (QDE-012 §17).
            bool hasFalseInvalidationData = slice.All(r => !double.IsNaN(r.FalseInvalidationRate));
            double falseCv = hasFalseInvalidationData ? CoefficientOfVariation(slice.Select(r => r.FalseInvalidationRate)) : double.NaN;
            double meanFalseInvalidation = hasFalseInvalidationData ? slice.Average(r => r.FalseInvalidationRate) : double.NaN;

            if (revCv <= CvThreshold)
            {
                candidates.Add(new StableWindow(
                    start, start + WindowSize - 1, slice[0].K, slice[^1].K,
                    slice.Average(r => r.ReversionRate), revCv,
                    meanFalseInvalidation, falseCv));
            }
        }

        return MergeOverlapping(candidates);
    }

    private static double CoefficientOfVariation(IEnumerable<double> values)
    {
        List<double> list = values.ToList();
        double mean = list.Average();
        if (Math.Abs(mean) < 1e-9)
        {
            return list.All(v => Math.Abs(v) < 1e-9) ? 0.0 : double.PositiveInfinity;
        }

        double variance = list.Sum(v => (v - mean) * (v - mean)) / list.Count;
        return Math.Sqrt(variance) / Math.Abs(mean);
    }

    private static IReadOnlyList<StableWindow> MergeOverlapping(List<StableWindow> windows)
    {
        if (windows.Count == 0)
        {
            return windows;
        }

        windows = windows.OrderBy(w => w.StartKIndex).ToList();
        var merged = new List<StableWindow>();
        StableWindow current = windows[0];

        foreach (StableWindow w in windows.Skip(1))
        {
            if (w.StartKIndex <= current.EndKIndex + 1)
            {
                current = current with
                {
                    EndKIndex = Math.Max(current.EndKIndex, w.EndKIndex),
                    EndK = Math.Max(current.EndK, w.EndK),
                };
            }
            else
            {
                merged.Add(current);
                current = w;
            }
        }

        merged.Add(current);
        return merged;
    }
}
