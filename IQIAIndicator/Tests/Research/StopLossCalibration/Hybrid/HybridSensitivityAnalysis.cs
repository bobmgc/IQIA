using System;
using System.Globalization;
using System.IO;
using System.Linq;

namespace IQIAIndicator.Tests.Research.StopLossCalibration.Hybrid;

/// <summary>
/// Sprint 15.13 (brief §18/§19). Leave-one-independent-series-out: pools the per-dataset
/// (HybridStrict.ReversionRate - A1.ReversionRate) delta across the 9 independent series (TRAIN split,
/// scale=1, ComparisonK=2.0), once with every series included and once for each single series excluded.
///
/// Method: simple (unweighted) mean of the per-dataset deltas, so no single dataset's larger entry
/// count dominates the pooled figure - documented here as the exact method used, not fabricated after
/// the fact. Reports whether the sign of the pooled delta ever flips - the brief's criterion for
/// "directionally robust" vs "fragile."
///
/// Pure reduction over the already-computed grid - no recomputation.
/// </summary>
public static class HybridSensitivityAnalysis
{
    private static readonly string[] LeaveOutCandidates = { "RandomWalk", "AR1_phi0.95", "VarianceBreak", "Trending", "StructuralBreak" };

    public static string Write(HybridCampaignResult campaign)
    {
        string path = Path.Combine(campaign.OutputDirectory, "A1_A2_Hybrid_sensitivity.csv");

        var perDataset = campaign.Rows
            .Where(r => r.IndependentDataset && r.Split == "TRAIN" && r.Scale == 1m && Math.Abs(r.K - HybridEntryAnalysis.ComparisonK) < 1e-9
                        && (r.Candidate == "A1" || r.Candidate == "HybridStrict"))
            .GroupBy(r => r.Dataset)
            .ToDictionary(g => g.Key, g =>
            {
                double a1 = g.First(x => x.Candidate == "A1").ReversionRate;
                double hybrid = g.First(x => x.Candidate == "HybridStrict").ReversionRate;
                return hybrid - a1;
            });

        using var writer = new StreamWriter(path, false);
        writer.WriteLine("ExcludedDataset,IncludedDatasetCount,MeanDeltaHybridStrictMinusA1,SignFlippedVsFullSet");

        double fullSetMean = perDataset.Count > 0 ? perDataset.Values.Average() : double.NaN;
        writer.WriteLine(string.Join(",", "None", perDataset.Count, Fmt(fullSetMean), false));

        foreach (string excluded in LeaveOutCandidates)
        {
            if (!perDataset.ContainsKey(excluded)) continue;

            var included = perDataset.Where(kv => kv.Key != excluded).Select(kv => kv.Value).ToList();
            double mean = included.Count > 0 ? included.Average() : double.NaN;
            bool flipped = Math.Sign(mean) != Math.Sign(fullSetMean);
            writer.WriteLine(string.Join(",", excluded, included.Count, Fmt(mean), flipped));
        }

        return path;
    }

    private static string Fmt(double v) => double.IsNaN(v) ? "" : v.ToString("G6", CultureInfo.InvariantCulture);
}
