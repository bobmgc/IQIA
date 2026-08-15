using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace IQIAIndicator.Tests.Research.StopLossCalibration.A1Calibration;

/// <summary>
/// Sprint 15.14 (brief §16, Phase D). VALIDATION never discovers a new region - it only re-evaluates
/// the EXACT k-range each TRAIN-discovered RegionCandidate already has, on VALIDATION-split data for
/// the same scope. Two separately-reported diagnostic checks (never blended into one score, per brief
/// §25): CvHolds reuses StableRegionAnalyzer's own 10% coefficient-of-variation threshold; MeanHolds
/// checks the region's ReversionRate level didn't drift by more than 5 percentage points - a second,
/// independent, exploratory diagnostic tolerance, documented here, not a QDE-012-locked criterion. A
/// region that "works only on TRAIN" is exactly the case CvHolds/MeanHolds=false is meant to catch
/// (brief §16: "Une région qui fonctionne uniquement sur TRAIN doit être rejetée").
/// </summary>
public sealed record RegionSplitCheck(
    string Scope,
    double StartK,
    double EndK,
    double TrainMean,
    double SplitMean,
    double SplitCv,
    double MeanDrift,
    bool CvHolds,
    bool MeanHolds);

public static class A1ValidationConfirmation
{
    public const double CvThreshold = 0.10;
    public const double MeanDriftTolerance = 0.05;

    public static IReadOnlyList<RegionSplitCheck> Check(IReadOnlyList<CandidateAggregateResult> allRows, IReadOnlyList<RegionCandidate> trainRegions, string split)
    {
        var results = new List<RegionSplitCheck>();

        foreach (RegionCandidate region in trainRegions)
        {
            List<CandidateAggregateResult> curve = A1CurveBuilder.GetCurve(allRows, region.Scope, split, 1m);
            List<CandidateAggregateResult> slice = curve.Where(r => r.K >= region.StartK - 1e-9 && r.K <= region.EndK + 1e-9).ToList();

            if (slice.Count == 0 || slice.Any(r => double.IsNaN(r.ReversionRate)))
            {
                results.Add(new RegionSplitCheck(region.Scope, region.StartK, region.EndK, region.TrainMeanReversionRate, double.NaN, double.NaN, double.NaN, false, false));
                continue;
            }

            double splitMean = slice.Average(r => r.ReversionRate);
            double splitCv = A1CurveBuilder.CoefficientOfVariation(slice.Select(r => r.ReversionRate));
            double drift = splitMean - region.TrainMeanReversionRate;

            results.Add(new RegionSplitCheck(
                region.Scope, region.StartK, region.EndK, region.TrainMeanReversionRate,
                splitMean, splitCv, drift,
                splitCv <= CvThreshold,
                Math.Abs(drift) <= MeanDriftTolerance));
        }

        return results;
    }

    public static string Write(string outputDirectory, IReadOnlyList<RegionSplitCheck> checks, string fileName)
    {
        string path = Path.Combine(outputDirectory, fileName);
        using var writer = new StreamWriter(path, false);
        writer.WriteLine("Scope,StartK,EndK,TrainMeanReversionRate,SplitMeanReversionRate,SplitReversionRateCv,MeanDrift,CvHolds(<=10%25),MeanHolds(<=5pp)");
        foreach (RegionSplitCheck c in checks.OrderBy(c => c.Scope).ThenBy(c => c.StartK))
        {
            writer.WriteLine(string.Join(",",
                c.Scope, Fmt(c.StartK), Fmt(c.EndK), Fmt(c.TrainMean), Fmt(c.SplitMean), Fmt(c.SplitCv), Fmt(c.MeanDrift), c.CvHolds, c.MeanHolds));
        }

        return path;
    }

    private static string Fmt(double v) => double.IsNaN(v) ? "" : v.ToString("G6", CultureInfo.InvariantCulture);
}
