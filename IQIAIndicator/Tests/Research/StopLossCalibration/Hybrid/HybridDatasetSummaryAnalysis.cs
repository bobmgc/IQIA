using System;
using System.Globalization;
using System.IO;
using System.Linq;

namespace IQIAIndicator.Tests.Research.StopLossCalibration.Hybrid;

/// <summary>
/// Sprint 15.13 (brief §9/§19, dataset-summary CSV). Per-INDEPENDENT-series (9, not 11) roll-up of
/// A1/A2/HybridStrict/HybridConservative, at the ComparisonK=2.0 anchor (A1VsA2EntryAnalysis.ComparisonK),
/// scale=1, split by split. Pure reduction over HybridCampaignRunner's already-computed grid - no new
/// simulation, no recomputation of any entry.
/// </summary>
public static class HybridDatasetSummaryAnalysis
{
    public static string Write(HybridCampaignResult campaign)
    {
        string path = Path.Combine(campaign.OutputDirectory, "A1_A2_Hybrid_dataset_summary.csv");
        var rows = campaign.Rows
            .Where(r => r.IndependentDataset && r.Scale == 1m && Math.Abs(r.K - HybridEntryAnalysis.ComparisonK) < 1e-9)
            .OrderBy(r => r.Dataset).ThenBy(r => r.Split).ThenBy(r => r.Candidate);

        using var writer = new StreamWriter(path, false);
        writer.WriteLine(
            "Candidate,Dataset,IndependentDataset,Split,ApplicableEntries,DegenerateEntries,StopHitRate,ReversionRate," +
            "UndeterminedRate,StopHitBeforeEquilibriumRate,MaeMedian,MfeMedian,MeanStopDistance,MeanStopRatio," +
            "HybridA1Share,HybridA2Share,HybridUnknownShare");

        foreach (var r in rows)
        {
            writer.WriteLine(string.Join(",",
                r.Candidate, r.Dataset, r.IndependentDataset, r.Split, r.ApplicableEntries, r.DegenerateEntries,
                Fmt(r.StopHitRate), Fmt(r.ReversionRate), Fmt(r.UndeterminedRate), Fmt(r.StopHitBeforeEquilibriumRate),
                Fmt(r.MaeMedian), Fmt(r.MfeMedian), Fmt(r.MeanStopDistance), Fmt(r.MeanStopRatio),
                r.HybridA1Share is double a1 ? Fmt(a1) : "", r.HybridA2Share is double a2 ? Fmt(a2) : "", r.HybridUnknownShare is double u ? Fmt(u) : ""));
        }

        return path;
    }

    private static string Fmt(double v) => double.IsNaN(v) ? "" : v.ToString("G6", CultureInfo.InvariantCulture);
}
