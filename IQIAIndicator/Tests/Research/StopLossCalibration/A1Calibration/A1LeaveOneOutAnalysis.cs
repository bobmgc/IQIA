using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using IQIAIndicator.Tests.Research.StopLossCalibration.Hybrid;

namespace IQIAIndicator.Tests.Research.StopLossCalibration.A1Calibration;

/// <summary>
/// Sprint 15.14 (brief §18). For each POOLED candidate region found on TRAIN, and for each of the 9
/// independent datasets in turn, recompute the pooled ReversionRate mean/CV over that region's k-range
/// EXCLUDING that one dataset. A region whose pooled stability disappears whenever any single dataset is
/// removed is FRAGILE (brief §18); one that survives every exclusion is robust to any single dataset's
/// influence. Pure reduction over the already-computed grid - no recomputation, no re-simulation.
/// </summary>
public sealed record LeaveOneOutResult(
    double StartK,
    double EndK,
    string ExcludedDataset,
    int IncludedDatasetCount,
    double MeanReversionRate,
    double ReversionRateCv,
    bool StillStable);

public static class A1LeaveOneOutAnalysis
{
    public static IReadOnlyList<LeaveOneOutResult> Run(IReadOnlyList<CandidateAggregateResult> allRows, IReadOnlyList<RegionCandidate> pooledRegions)
    {
        var results = new List<LeaveOneOutResult>();
        List<CandidateAggregateResult> trainRows = allRows.Where(r => r.Split == "TRAIN" && r.Scale == 1m && IndependentDatasetCatalog.IsIndependent(r.Dataset)).ToList();

        foreach (RegionCandidate region in pooledRegions.Where(r => r.Scope == A1PooledCurve.PooledScope))
        {
            // Baseline: every independent dataset included.
            results.Add(Evaluate(trainRows, region, excluded: null));

            foreach (string dataset in IndependentDatasetCatalog.IndependentDatasets)
            {
                List<CandidateAggregateResult> subset = trainRows.Where(r => r.Dataset != dataset).ToList();
                results.Add(Evaluate(subset, region, excluded: dataset));
            }
        }

        return results;
    }

    private static LeaveOneOutResult Evaluate(List<CandidateAggregateResult> rows, RegionCandidate region, string? excluded)
    {
        List<CandidateAggregateResult> pooledCurve = A1PooledCurve.Build(rows).ToList();
        List<CandidateAggregateResult> slice = pooledCurve.Where(r => r.K >= region.StartK - 1e-9 && r.K <= region.EndK + 1e-9).ToList();

        if (slice.Count == 0 || slice.Any(r => double.IsNaN(r.ReversionRate)))
        {
            return new LeaveOneOutResult(region.StartK, region.EndK, excluded ?? "None", 0, double.NaN, double.NaN, false);
        }

        double mean = slice.Average(r => r.ReversionRate);
        double cv = A1CurveBuilder.CoefficientOfVariation(slice.Select(r => r.ReversionRate));
        int includedCount = rows.Select(r => r.Dataset).Distinct().Count();

        return new LeaveOneOutResult(region.StartK, region.EndK, excluded ?? "None", includedCount, mean, cv, cv <= A1ValidationConfirmation.CvThreshold);
    }

    public static string Write(string outputDirectory, IReadOnlyList<LeaveOneOutResult> results)
    {
        string path = Path.Combine(outputDirectory, "A1_leave_one_out.csv");
        using var writer = new StreamWriter(path, false);
        writer.WriteLine("StartK,EndK,ExcludedDataset,IncludedDatasetCount,MeanReversionRate,ReversionRateCv,StillStable(CV<=10%25)");
        foreach (LeaveOneOutResult r in results)
        {
            writer.WriteLine(string.Join(",", Fmt(r.StartK), Fmt(r.EndK), r.ExcludedDataset, r.IncludedDatasetCount, Fmt(r.MeanReversionRate), Fmt(r.ReversionRateCv), r.StillStable));
        }

        return path;
    }

    private static string Fmt(double v) => double.IsNaN(v) ? "" : v.ToString("G6", CultureInfo.InvariantCulture);
}
