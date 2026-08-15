using System;
using System.Globalization;
using System.IO;
using System.Linq;
using IQIAIndicator.Tests.Research.StopLossCalibration.Hybrid;

namespace IQIAIndicator.Tests.Research.StopLossCalibration.A1Calibration;

/// <summary>
/// Sprint 15.14 (brief §8/§22). Full k-grid x 6 scales x 9 independent datasets scale-invariance check:
/// MeanStopRatio should equal K exactly (Sprint 15.11's exact-invariance result for A1), and
/// ReversionRate/StopHitRate should be identical across scale for the same (dataset, split, k). Pure
/// reduction over the already-computed grid.
/// </summary>
public static class A1ScaleAnalysis
{
    public static (string Path, double MaxAbsRatioDeviation) Write(string outputDirectory, IReadOnlyList<CandidateAggregateResult> allRows)
    {
        string path = Path.Combine(outputDirectory, "A1_scale_analysis.csv");
        var rows = allRows
            .Where(r => IndependentDatasetCatalog.IsIndependent(r.Dataset))
            .OrderBy(r => r.Dataset).ThenBy(r => r.Split).ThenBy(r => r.K).ThenBy(r => r.Scale)
            .ToList();

        double maxDeviation = 0.0;
        using var writer = new StreamWriter(path, false);
        writer.WriteLine("Dataset,Split,K,Scale,MeanStopRatio,RatioMinusK,ApplicableEntries,ReversionRate,StopHitRate");
        foreach (CandidateAggregateResult r in rows)
        {
            double deviation = double.IsNaN(r.MeanStopRatio) ? 0.0 : Math.Abs(r.MeanStopRatio - r.K);
            maxDeviation = Math.Max(maxDeviation, deviation);

            writer.WriteLine(string.Join(",",
                r.Dataset, r.Split, Fmt(r.K), r.Scale.ToString(CultureInfo.InvariantCulture),
                Fmt(r.MeanStopRatio), Fmt(r.MeanStopRatio - r.K), r.ApplicableEntries, Fmt(r.ReversionRate), Fmt(r.StopHitRate)));
        }

        return (path, maxDeviation);
    }

    private static string Fmt(double v) => double.IsNaN(v) ? "" : v.ToString("G10", CultureInfo.InvariantCulture);
}
