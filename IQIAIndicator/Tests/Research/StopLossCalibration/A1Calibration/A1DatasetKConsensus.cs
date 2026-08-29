using System.Globalization;
using System.IO;
using System.Linq;
using IQIAIndicator.Tests.Research.StopLossCalibration.Hybrid;

namespace IQIAIndicator.Tests.Research.StopLossCalibration.A1Calibration;

/// <summary>
/// Sprint 15.14 (brief §24, "K CONSENSUS"). Long-format dataset x k matrix (ReversionRate, StopHitRate)
/// for the 9 independent datasets, TRAIN, scale=1 - the raw material for judging whether a k-region is
/// good on one dataset only versus broadly agreed-upon across independent series. Pure reduction over
/// the already-computed grid, no recomputation.
/// </summary>
public static class A1DatasetKConsensus
{
    public static string Write(string outputDirectory, IReadOnlyList<CandidateAggregateResult> allRows)
    {
        string path = Path.Combine(outputDirectory, "A1_dataset_k_analysis.csv");
        using var writer = new StreamWriter(path, false);
        writer.WriteLine("Dataset,K,ApplicableEntries,DegenerateEntries,ReversionRate,StopHitRate,UndeterminedRate,MeanStopDistance,MeanStopRatio");

        var rows = allRows
            .Where(r => r.Split == "TRAIN" && r.Scale == 1m && IndependentDatasetCatalog.IsIndependent(r.Dataset))
            .OrderBy(r => r.Dataset).ThenBy(r => r.K);

        foreach (CandidateAggregateResult r in rows)
        {
            writer.WriteLine(string.Join(",",
                r.Dataset, Fmt(r.K), r.ApplicableEntries, r.DegenerateEntries,
                Fmt(r.ReversionRate), Fmt(r.StopHitRate), Fmt(r.UndeterminedRate), Fmt(r.MeanStopDistance), Fmt(r.MeanStopRatio)));
        }

        return path;
    }

    private static string Fmt(double v) => double.IsNaN(v) ? "" : v.ToString("G6", CultureInfo.InvariantCulture);
}
