using System.Globalization;
using System.IO;
using System.Linq;
using IQIAIndicator.Tests.Research.StopLossCalibration.Hybrid;

namespace IQIAIndicator.Tests.Research.StopLossCalibration.A1Calibration;

/// <summary>
/// Sprint 15.14 (brief §21). Trending analyzed on its own terms, without trying to "fix" it - A1's
/// behavior here (near-zero ReversionRate at every k, per Sprint 15.12/15.13) is reported as-is. If
/// Trending destroys a candidate region (brief §21: "le rapport doit le dire"), that is a finding, not
/// a reason to drop the dataset. Pure reduction over the already-computed grid.
/// </summary>
public static class A1TrendingAnalysis
{
    private const string Dataset = "Trending";

    public static string Write(string outputDirectory, System.Collections.Generic.IReadOnlyList<CandidateAggregateResult> allRows)
    {
        string path = Path.Combine(outputDirectory, "A1_trending_analysis.csv");
        var rows = allRows
            .Where(r => r.Dataset == Dataset)
            .OrderBy(r => r.Split).ThenBy(r => r.Scale).ThenBy(r => r.K);

        using var writer = new StreamWriter(path, false);
        writer.WriteLine("Split,Scale,K,ApplicableEntries,DegenerateEntries,ReversionRate,StopHitRate,UndeterminedRate,MeanStopDistance,MeanStopRatio");
        foreach (CandidateAggregateResult r in rows)
        {
            writer.WriteLine(string.Join(",",
                r.Split, r.Scale.ToString(CultureInfo.InvariantCulture), Fmt(r.K), r.ApplicableEntries, r.DegenerateEntries,
                Fmt(r.ReversionRate), Fmt(r.StopHitRate), Fmt(r.UndeterminedRate), Fmt(r.MeanStopDistance), Fmt(r.MeanStopRatio)));
        }

        return path;
    }

    private static string Fmt(double v) => double.IsNaN(v) ? "" : v.ToString("G6", CultureInfo.InvariantCulture);
}
