using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace IQIAIndicator.Tests.Research.StopLossCalibration.Hybrid;

/// <summary>
/// Sprint 15.13 (brief §16). TRAIN/VALIDATION/TEST reported side by side, never averaged together, at
/// ComparisonK=2.0, scale=1, independent series pooled per split (entry-count-weighted mean across the
/// 9 independent datasets, since their population sizes differ - documented explicitly here rather than
/// an unweighted mean). Pure reduction over the already-computed grid - this file does not decide
/// anything, it only presents TEST last and separately so a human (or the report) can compare without a
/// rule ever having been re-tuned after seeing it.
/// </summary>
public static class HybridTrainValidationTestAnalysis
{
    public static string Write(HybridCampaignResult campaign)
    {
        string path = Path.Combine(campaign.OutputDirectory, "A1_A2_Hybrid_train_validation_test.csv");

        var grouped = campaign.Rows
            .Where(r => r.IndependentDataset && r.Scale == 1m && Math.Abs(r.K - HybridEntryAnalysis.ComparisonK) < 1e-9)
            .GroupBy(r => (r.Candidate, r.Split))
            .Select(g => new
            {
                g.Key.Candidate,
                g.Key.Split,
                ApplicableEntries = g.Sum(x => x.ApplicableEntries),
                DegenerateEntries = g.Sum(x => x.DegenerateEntries),
                ReversionRate = WeightedMean(g.Select(x => (x.ReversionRate, x.ApplicableEntries))),
                StopHitRate = WeightedMean(g.Select(x => (x.StopHitRate, x.ApplicableEntries))),
                UndeterminedRate = WeightedMean(g.Select(x => (x.UndeterminedRate, x.ApplicableEntries))),
                MaeMedianAvg = g.Average(x => x.MaeMedian),
                MfeMedianAvg = g.Average(x => x.MfeMedian),
            })
            .OrderBy(r => r.Candidate).ThenBy(r => SplitOrder(r.Split));

        using var writer = new StreamWriter(path, false);
        writer.WriteLine("Candidate,Split,ApplicableEntries,DegenerateEntries,ReversionRate,StopHitRate,UndeterminedRate,MaeMedianAvg,MfeMedianAvg");
        foreach (var r in grouped)
        {
            writer.WriteLine(string.Join(",", r.Candidate, r.Split, r.ApplicableEntries, r.DegenerateEntries,
                Fmt(r.ReversionRate), Fmt(r.StopHitRate), Fmt(r.UndeterminedRate), Fmt(r.MaeMedianAvg), Fmt(r.MfeMedianAvg)));
        }

        return path;
    }

    private static double WeightedMean(IEnumerable<(double Rate, int Weight)> values)
    {
        var list = values.Where(v => !double.IsNaN(v.Rate)).ToList();
        int totalWeight = list.Sum(v => v.Weight);
        return totalWeight == 0 ? double.NaN : list.Sum(v => v.Rate * v.Weight) / totalWeight;
    }

    private static int SplitOrder(string split) => split switch { "TRAIN" => 0, "VALIDATION" => 1, "TEST" => 2, _ => 3 };

    private static string Fmt(double v) => double.IsNaN(v) ? "" : v.ToString("G6", CultureInfo.InvariantCulture);
}
