using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace IQIAIndicator.Tests.Research.StopLossCalibration.Hybrid;

/// <summary>
/// Sprint 15.13 (brief §12). VarianceBreak before/after the generator's own breakpoint
/// (VarianceBreakInfo.BreakIndex = SeriesLength/2, re-derived the same way Sprint 15.12 did - not
/// exposed by SyntheticSeriesCatalog, not invented here). For A1/A2/HybridStrict/HybridConservative, at
/// ComparisonK=2.0: ReversionRate, StopHitRate, MAE/MFE median, MeanStopRatio, plus the VolatilityRegime
/// distribution before/after - the direct test of whether HYBRID's regime routing reacts fast enough to
/// earn A2's break-adaptation advantage, or reacts too late to matter.
///
/// Rebuilds only the VarianceBreak entries (3 splits x 6 scales, cheap) via CalibrationEntryBuilder,
/// unmodified; aggregates via HybridCandidateAggregator, unmodified.
/// </summary>
public static class HybridVarianceBreakAnalysis
{
    private const string Dataset = "VarianceBreak";

    public static string Write(string outputDirectory)
    {
        string path = Path.Combine(outputDirectory, "A1_A2_Hybrid_variance_break.csv");
        int breakIndex = VarianceBreakInfo.BreakIndex(CampaignDatasetCatalog.SeriesLength);

        using var writer = new StreamWriter(path, false);
        writer.WriteLine(
            "Candidate,Split,Scale,Period,ApplicableEntries,DegenerateEntries,ReversionRate,StopHitRate," +
            "MaeMedian,MfeMedian,MeanStopRatio,LowCount,MediumCount,HighCount,UnknownCount");

        foreach (string split in CampaignDatasetCatalog.Splits)
        {
            foreach (decimal scale in CampaignDatasetCatalog.Scales)
            {
                decimal[] series = CampaignDatasetCatalog.Build(Dataset, split, scale);
                IReadOnlyList<CalibrationEntry> entries = CalibrationEntryBuilder.BuildEntries(Dataset, series, CampaignGrids.Horizon);

                IReadOnlyList<CalibrationEntry> before = entries.Where(e => e.EntryBarIndex < breakIndex).ToList();
                IReadOnlyList<CalibrationEntry> after = entries.Where(e => e.EntryBarIndex >= breakIndex).ToList();

                WritePeriod(writer, split, scale, "Before", before);
                WritePeriod(writer, split, scale, "After", after);
            }
        }

        return path;
    }

    private static void WritePeriod(StreamWriter writer, string split, decimal scale, string period, IReadOnlyList<CalibrationEntry> entries)
    {
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
            writer.WriteLine(string.Join(",", name, split, scale.ToString(CultureInfo.InvariantCulture), period,
                r.ApplicableEntries, r.DegenerateEntries, Fmt(r.ReversionRate), Fmt(r.StopHitRate), Fmt(r.MaeMedian), Fmt(r.MfeMedian), Fmt(r.MeanStopRatio),
                regimeCounts["LOW"], regimeCounts["MEDIUM"], regimeCounts["HIGH"], regimeCounts["UNKNOWN"]));
        }
    }

    private static string Fmt(double v) => double.IsNaN(v) ? "" : v.ToString("G6", CultureInfo.InvariantCulture);
}
