using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace IQIAIndicator.Tests.Research.StopLossCalibration.Hybrid;

/// <summary>
/// Sprint 15.13 (brief §14). Trending specifically: Sprint 15.12 found A2 stops out ~100% of Trending
/// entries on bar 1 (CurrentVolatility/InnovationStd ~ 0.005, an artificially tiny stop, ruled a metric
/// artifact, not a genuine advantage). This file checks, rather than assumes, whether HYBRID reproduces
/// that defect: what VolatilityRegime Trending actually falls into, HybridA2Share, and a direct
/// A2-vs-Hybrid StopDistance/StopHitRate comparison at ComparisonK=2.0. If HybridA2Share is high and
/// Hybrid's StopHitRate/StopDistance mirror A2's on Trending, that is reported as a defect inherited
/// from A2, not framed as HYBRID "correctly detecting a regime change."
/// </summary>
public static class HybridTrendingAnalysis
{
    private const string Dataset = "Trending";

    public static string Write(string outputDirectory)
    {
        string path = Path.Combine(outputDirectory, "A1_A2_Hybrid_trending.csv");

        using var writer = new StreamWriter(path, false);
        writer.WriteLine(
            "Candidate,Split,Scale,ApplicableEntries,DegenerateEntries,StopHitRate,ReversionRate,MeanStopDistance,MeanStopRatio," +
            "HybridA1Share,HybridA2Share,HybridUnknownShare,LowCount,MediumCount,HighCount,UnknownCount");

        foreach (string split in CampaignDatasetCatalog.Splits)
        {
            foreach (decimal scale in CampaignDatasetCatalog.Scales)
            {
                decimal[] series = CampaignDatasetCatalog.Build(Dataset, split, scale);
                IReadOnlyList<CalibrationEntry> entries = CalibrationEntryBuilder.BuildEntries(Dataset, series, CampaignGrids.Horizon);

                var regimeCounts = new Dictionary<string, int> { ["LOW"] = 0, ["MEDIUM"] = 0, ["HIGH"] = 0, ["UNKNOWN"] = 0 };
                foreach (CalibrationEntry e in entries)
                {
                    string key = e.Metrics.VolatilityRegime is "LOW" or "MEDIUM" or "HIGH" ? e.Metrics.VolatilityRegime : "UNKNOWN";
                    regimeCounts[key]++;
                }

                foreach (HybridCandidateKind kind in new[] { HybridCandidateKind.A1, HybridCandidateKind.A2, HybridCandidateKind.HybridStrict, HybridCandidateKind.HybridConservative })
                {
                    string name = kind.ToString();
                    HybridAggregateResult r = HybridCandidateAggregator.Aggregate(name, Dataset, split, scale, kind, HybridEntryAnalysis.ComparisonK, entries);
                    writer.WriteLine(string.Join(",", name, split, scale.ToString(CultureInfo.InvariantCulture),
                        r.ApplicableEntries, r.DegenerateEntries, Fmt(r.StopHitRate), Fmt(r.ReversionRate), Fmt(r.MeanStopDistance), Fmt(r.MeanStopRatio),
                        r.HybridA1Share is double a1 ? Fmt(a1) : "", r.HybridA2Share is double a2 ? Fmt(a2) : "", r.HybridUnknownShare is double u ? Fmt(u) : "",
                        regimeCounts["LOW"], regimeCounts["MEDIUM"], regimeCounts["HIGH"], regimeCounts["UNKNOWN"]));
                }
            }
        }

        return path;
    }

    private static string Fmt(double v) => double.IsNaN(v) ? "" : v.ToString("G6", CultureInfo.InvariantCulture);
}
