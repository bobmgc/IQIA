using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using IQIAIndicator.Tests.Research.StopLossCalibration.Hybrid;

namespace IQIAIndicator.Tests.Research.StopLossCalibration.A1Calibration;

/// <summary>
/// Sprint 15.14 (brief §15, Phase C). TRAIN discovers candidate regions - VALIDATION/TEST only ever
/// confirm or reject the exact k-ranges found here, never redefine them. Reuses
/// StableRegionAnalyzer.FindStableWindows unmodified (>=5 consecutive k, ReversionRate CV&lt;=10% - an
/// exploratory diagnostic threshold, not a locked scientific criterion, per QDE-012 §17 and this
/// sprint's brief §13).
///
/// DegenerateNearZero flags a window whose MeanReversionRate is itself near zero: a flat-at-zero curve
/// (e.g. Trending, where A1 essentially never reverts within the horizon regardless of k) trivially
/// satisfies CV&lt;=10% (CV of an all-zero series is defined as 0), which would otherwise look identical
/// to a genuinely useful flat-and-high region. This flag exists so that distinction is never lost
/// silently (brief §21: Trending must not be hidden behind an average).
/// </summary>
public sealed record RegionCandidate(
    string Scope,
    double StartK,
    double EndK,
    double TrainMeanReversionRate,
    double TrainReversionRateCv,
    double TrainMeanStopHitRate,
    double TrainMeanFalseInvalidationRate,
    bool DegenerateNearZero);

public static class A1StableRegionDiscovery
{
    private const double DegenerateNearZeroThreshold = 0.05;

    public static IReadOnlyList<RegionCandidate> DiscoverFromTrain(IReadOnlyList<CandidateAggregateResult> allRows)
    {
        var result = new List<RegionCandidate>();

        foreach (string dataset in IndependentDatasetCatalog.IndependentDatasets)
        {
            List<CandidateAggregateResult> curve = A1CurveBuilder.GetCurve(allRows, dataset, "TRAIN", 1m);
            if (curve.Count == 0) continue;

            foreach (StableWindow w in StableRegionAnalyzer.FindStableWindows(curve))
            {
                result.Add(ToRegionCandidate(dataset, w, curve));
            }
        }

        List<CandidateAggregateResult> pooledCurve = A1CurveBuilder.GetCurve(allRows, A1PooledCurve.PooledScope, "TRAIN", 1m);
        foreach (StableWindow w in StableRegionAnalyzer.FindStableWindows(pooledCurve))
        {
            result.Add(ToRegionCandidate(A1PooledCurve.PooledScope, w, pooledCurve));
        }

        return result;
    }

    private static RegionCandidate ToRegionCandidate(string scope, StableWindow w, List<CandidateAggregateResult> curve)
    {
        // IMPORTANT (found during this sprint's analysis, not present before): StableRegionAnalyzer's
        // MergeOverlapping only extends EndKIndex/EndK when merging adjacent 5-point windows - it never
        // recomputes MeanReversionRate/ReversionRateCv/MeanFalseInvalidationRate, so for any MERGED
        // (wider-than-5-point) window those three fields are stale, reflecting only the FIRST 5-point
        // sub-window in the merge chain, not the full reported k-range. The window BOUNDS
        // (StartK/EndK/StartKIndex/EndKIndex) are correct - only those three descriptive fields are not.
        // This sprint recomputes them directly from the full [StartKIndex, EndKIndex] slice instead of
        // trusting w.MeanReversionRate/w.ReversionRateCv/w.MeanFalseInvalidationRate, and reports this
        // finding explicitly (see the Sprint 15.14 report's Limitations section) rather than silently
        // working around it - StableRegionAnalyzer.cs itself is left unmodified (out of this sprint's
        // scope; the bug affects a shared, previously-"validated" Sprint 15.10 utility, not this
        // sprint's own harness).
        List<CandidateAggregateResult> slice = curve.Skip(w.StartKIndex).Take(w.EndKIndex - w.StartKIndex + 1).ToList();
        double meanReversionRate = slice.Average(r => r.ReversionRate);
        double reversionRateCv = A1CurveBuilder.CoefficientOfVariation(slice.Select(r => r.ReversionRate));
        double meanStopHit = slice.Average(r => r.StopHitRate);
        double meanFalseInvalidation = slice.Where(r => !double.IsNaN(r.FalseInvalidationRate)).Select(r => r.FalseInvalidationRate).DefaultIfEmpty(double.NaN).Average();

        return new RegionCandidate(
            scope, w.StartK, w.EndK, meanReversionRate, reversionRateCv,
            meanStopHit, meanFalseInvalidation,
            meanReversionRate < DegenerateNearZeroThreshold);
    }

    public static string Write(string outputDirectory, IReadOnlyList<RegionCandidate> regions)
    {
        string path = Path.Combine(outputDirectory, "A1_stable_regions.csv");
        using var writer = new StreamWriter(path, false);
        writer.WriteLine("Scope,StartK,EndK,WidthKPoints,TrainMeanReversionRate,TrainReversionRateCv,TrainMeanStopHitRate,TrainMeanFalseInvalidationRate_Descriptive,DegenerateNearZero");
        foreach (RegionCandidate r in regions.OrderBy(r => r.Scope).ThenBy(r => r.StartK))
        {
            int widthPoints = (int)System.Math.Round((r.EndK - r.StartK) / 0.25) + 1;
            writer.WriteLine(string.Join(",",
                r.Scope, Fmt(r.StartK), Fmt(r.EndK), widthPoints,
                Fmt(r.TrainMeanReversionRate), Fmt(r.TrainReversionRateCv), Fmt(r.TrainMeanStopHitRate),
                Fmt(r.TrainMeanFalseInvalidationRate), r.DegenerateNearZero));
        }

        return path;
    }

    private static string Fmt(double v) => double.IsNaN(v) ? "" : v.ToString("G6", CultureInfo.InvariantCulture);
}
