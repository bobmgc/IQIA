using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using IQIAIndicator.Tests.Research.StopLossCalibration.Hybrid;

namespace IQIAIndicator.Tests.Research.StopLossCalibration.A1Calibration;

/// <summary>
/// Sprint 15.14 (brief §23). Classifies each TRAIN, scale=1 ReversionRate(k) curve (9 independent
/// datasets + POOLED) into one of the brief's 7 shapes, using simple, documented, non-composite rules -
/// no invented weighted score (brief §25). "DatasetDependent" is reported separately, once, as the
/// cross-dataset agreement level across the 9 individual shapes (brief §19's instruction not to hide
/// disagreement behind a single average) - not as a per-curve label, since a single curve cannot itself
/// disagree with other datasets.
///
/// Rules (in priority order), all reusing StableRegionAnalyzer.FindStableWindows (>=5 consecutive k,
/// ReversionRate CV&lt;=10%, the same exploratory diagnostic threshold used everywhere else this sprint):
///   NoSignal        - mean ReversionRate over the whole grid &lt; 5% (a flat-at-zero curve, e.g. Trending,
///                      is not a usable finding even though it trivially has CV=0).
///   PlateauRobust   - a stable window exists and its width is >=50% of the 40-point grid, OR its mean
///                      is within 2pp of the curve's own maximum.
///   PlateauWithDrift- a stable window exists but is narrower than that and not near the curve's ceiling.
///   Monotone        - no stable window, but the curve has at most 3 sign changes in consecutive
///                      differences (i.e. it is close to monotonic, just not flat enough to count as a
///                      Sprint-15.10-style plateau).
///   LocalOptimum    - no stable window, not monotone, but overall CV is moderate (&lt;25%) - a real peak
///                      or dip shape, not noise.
///   Unstable        - no stable window, not monotone, overall CV >=25%.
/// </summary>
public enum CurveShape { NoSignal, PlateauRobust, PlateauWithDrift, Monotone, LocalOptimum, Unstable }

public sealed record CurveShapeResult(
    string Scope,
    CurveShape Shape,
    double OverallMeanReversionRate,
    double OverallReversionRateCv,
    double MaxReversionRate,
    double MinReversionRate,
    int WidestStableWindowPoints,
    double WidestStableWindowStartK,
    double WidestStableWindowEndK);

public static class A1CurveShapeAnalysis
{
    private const double NoSignalMeanThreshold = 0.05;
    private const double PlateauCeilingTolerance = 0.02;
    private const double UnstableCvThreshold = 0.25;

    public static IReadOnlyList<CurveShapeResult> Analyze(IReadOnlyList<CandidateAggregateResult> allRows)
    {
        var results = new List<CurveShapeResult>();
        foreach (string dataset in IndependentDatasetCatalog.IndependentDatasets)
        {
            List<CandidateAggregateResult> curve = A1CurveBuilder.GetCurve(allRows, dataset, "TRAIN", 1m);
            if (curve.Count > 0) results.Add(Classify(dataset, curve));
        }

        List<CandidateAggregateResult> pooled = A1CurveBuilder.GetCurve(allRows, A1PooledCurve.PooledScope, "TRAIN", 1m);
        if (pooled.Count > 0) results.Add(Classify(A1PooledCurve.PooledScope, pooled));

        return results;
    }

    private static CurveShapeResult Classify(string scope, List<CandidateAggregateResult> curve)
    {
        double[] revRates = curve.Select(r => r.ReversionRate).ToArray();
        double mean = revRates.Where(v => !double.IsNaN(v)).DefaultIfEmpty(double.NaN).Average();
        double cv = revRates.Any(double.IsNaN) ? double.NaN : A1CurveBuilder.CoefficientOfVariation(revRates);
        double max = revRates.Where(v => !double.IsNaN(v)).DefaultIfEmpty(double.NaN).Max();
        double min = revRates.Where(v => !double.IsNaN(v)).DefaultIfEmpty(double.NaN).Min();

        IReadOnlyList<StableWindow> windows = StableRegionAnalyzer.FindStableWindows(curve);
        StableWindow? widest = windows.Count == 0 ? null : windows.OrderByDescending(w => w.EndKIndex - w.StartKIndex).First();

        CurveShape shape;
        if (double.IsNaN(mean) || mean < NoSignalMeanThreshold)
        {
            shape = CurveShape.NoSignal;
        }
        else if (widest is not null)
        {
            int widthPoints = widest.EndKIndex - widest.StartKIndex + 1;
            bool halfOrMoreOfGrid = widthPoints >= curve.Count / 2;

            // Recomputed directly from the window's own slice rather than trusting
            // widest.MeanReversionRate, which StableRegionAnalyzer.MergeOverlapping does not update when
            // merging - see the identical note in A1StableRegionDiscovery.ToRegionCandidate.
            double widestWindowMean = curve.Skip(widest.StartKIndex).Take(widthPoints).Average(r => r.ReversionRate);
            bool nearCeiling = Math.Abs(widestWindowMean - max) <= PlateauCeilingTolerance;
            shape = (halfOrMoreOfGrid || nearCeiling) ? CurveShape.PlateauRobust : CurveShape.PlateauWithDrift;
        }
        else if (IsApproximatelyMonotone(revRates))
        {
            shape = CurveShape.Monotone;
        }
        else if (cv < UnstableCvThreshold)
        {
            shape = CurveShape.LocalOptimum;
        }
        else
        {
            shape = CurveShape.Unstable;
        }

        return new CurveShapeResult(
            scope, shape, mean, cv, max, min,
            widest is not null ? widest.EndKIndex - widest.StartKIndex + 1 : 0,
            widest?.StartK ?? double.NaN,
            widest?.EndK ?? double.NaN);
    }

    private static bool IsApproximatelyMonotone(double[] values)
    {
        int signChanges = 0;
        int lastSign = 0;
        for (int i = 1; i < values.Length; i++)
        {
            double diff = values[i] - values[i - 1];
            int sign = Math.Abs(diff) < 1e-6 ? 0 : Math.Sign(diff);
            if (sign != 0 && lastSign != 0 && sign != lastSign) signChanges++;
            if (sign != 0) lastSign = sign;
        }

        return signChanges <= 3;
    }

    public static string Write(string outputDirectory, IReadOnlyList<CurveShapeResult> results)
    {
        string path = Path.Combine(outputDirectory, "A1_k_curve_summary.csv");
        using var writer = new StreamWriter(path, false);
        writer.WriteLine("Scope,Shape,OverallMeanReversionRate,OverallReversionRateCv,MaxReversionRate,MinReversionRate,WidestStableWindowPoints,WidestStableWindowStartK,WidestStableWindowEndK");
        foreach (CurveShapeResult r in results)
        {
            writer.WriteLine(string.Join(",",
                r.Scope, r.Shape, Fmt(r.OverallMeanReversionRate), Fmt(r.OverallReversionRateCv), Fmt(r.MaxReversionRate), Fmt(r.MinReversionRate),
                r.WidestStableWindowPoints, Fmt(r.WidestStableWindowStartK), Fmt(r.WidestStableWindowEndK)));
        }

        return path;
    }

    private static string Fmt(double v) => double.IsNaN(v) ? "" : v.ToString("G6", CultureInfo.InvariantCulture);
}
