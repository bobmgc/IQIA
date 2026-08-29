using System.Collections.Generic;
using System.Globalization;
using System.IO;
using IQIAIndicator.Tests.Research.StopLossCalibration.Hybrid;

namespace IQIAIndicator.Tests.Research.StopLossCalibration.A1Calibration;

/// <summary>
/// Sprint 15.14 (brief §20). VarianceBreak before/after the generator's own breakpoint, reusing
/// Hybrid.VarianceBreakInfo.BreakIndex (Sprint 15.13 - SeriesLength/2, re-derived from
/// SyntheticSeriesCatalog.VarianceBreak's own private local, not invented here, not a new breakpoint).
/// Full k-grid, 3 splits, scale=1: does any zone of k become particularly sensitive to the variance
/// change? Rebuilds only the VarianceBreak entries (3 splits, cheap) via CalibrationEntryBuilder,
/// unmodified; aggregates via CandidateAggregator.Aggregate(A1), unmodified.
/// </summary>
public static class A1VarianceBreakAnalysis
{
    private const string Dataset = "VarianceBreak";

    public static string Write(string outputDirectory)
    {
        string path = Path.Combine(outputDirectory, "A1_variance_break_analysis.csv");
        int breakIndex = VarianceBreakInfo.BreakIndex(CampaignDatasetCatalog.SeriesLength);

        using var writer = new StreamWriter(path, false);
        writer.WriteLine("Split,K,Period,ApplicableEntries,DegenerateEntries,ReversionRate,StopHitRate,MaeMedian,MfeMedian,MeanStopRatio");

        foreach (string split in CampaignDatasetCatalog.Splits)
        {
            decimal[] series = CampaignDatasetCatalog.Build(Dataset, split, 1m);
            IReadOnlyList<CalibrationEntry> entries = CalibrationEntryBuilder.BuildEntries(Dataset, series, CampaignGrids.Horizon);

            var before = new List<CalibrationEntry>();
            var after = new List<CalibrationEntry>();
            foreach (CalibrationEntry e in entries)
            {
                (e.EntryBarIndex < breakIndex ? before : after).Add(e);
            }

            foreach (double k in CampaignGrids.KGrid)
            {
                WriteRow(writer, split, k, "Before", before);
                WriteRow(writer, split, k, "After", after);
            }
        }

        return path;
    }

    private static void WriteRow(StreamWriter writer, string split, double k, string period, IReadOnlyList<CalibrationEntry> entries)
    {
        CandidateAggregateResult r = CandidateAggregator.Aggregate("A1", Dataset, split, 1m, CandidateKind.A1, k, null, entries);
        writer.WriteLine(string.Join(",",
            split, Fmt(k), period, r.ApplicableEntries, r.DegenerateEntries,
            Fmt(r.ReversionRate), Fmt(r.StopHitRate), Fmt(r.MaeMedian), Fmt(r.MfeMedian), Fmt(r.MeanStopRatio)));
    }

    private static string Fmt(double v) => double.IsNaN(v) ? "" : v.ToString("G6", CultureInfo.InvariantCulture);
}
