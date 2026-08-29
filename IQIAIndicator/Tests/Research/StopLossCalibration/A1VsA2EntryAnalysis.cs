using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace IQIAIndicator.Tests.Research.StopLossCalibration;

/// <summary>
/// Sprint 15.12. Entry-level (not aggregated) dump needed for Phase 2 (CurrentVolatility/InnovationStd
/// ratio distribution) and Phases 4-6 (Trending/persistent/VarianceBreak entry-by-entry comparison of
/// A1 vs A2 at a fixed k). The aggregated grid CSVs from Sprint 15.10/15.11 already cover the k-varying
/// analyses (Phase 7/9); this tool only produces what those aggregates cannot: per-entry sigma values
/// and a same-entry A1-vs-A2 side-by-side outcome at one representative k (k=2.0 - already used as an
/// illustrative anchor point in the 15.10/15.11 reports, not a production choice).
///
/// Reuses BarMetricsComputer/OutcomeSimulator/CalibrationEntryBuilder/CandidateStopDistance/
/// StopLossEvaluator exactly as built - no scientific metric is recomputed here.
/// </summary>
public static class A1VsA2EntryAnalysis
{
    public const double ComparisonK = 2.0;

    public static (string ComparisonPath, string RatioPath, int EntryCount) RunAndWrite()
    {
        string outputDirectory = CampaignOutputPaths.ResolveOutputDirectory();
        string comparisonPath = Path.Combine(outputDirectory, "A1_vs_A2_comparison.csv");
        string ratioPath = Path.Combine(outputDirectory, "A1_vs_A2_ratio_analysis.csv");

        using var comparisonWriter = new StreamWriter(comparisonPath, false);
        using var ratioWriter = new StreamWriter(ratioPath, false);

        comparisonWriter.WriteLine(
            "Dataset,Split,Scale,EntryBarIndex,Direction,InnovationStd,CurrentVolatility,DynamicZScore,MAE,MFE,BarsAvailable,ReturnedToEquilibrium,EquilibriumBar," +
            "A1_StopDistance,A1_StopHit,A1_StopHitBar,A1_Fate,A2_StopDistance,A2_StopHit,A2_StopHitBar,A2_Fate");

        ratioWriter.WriteLine(
            "Dataset,Split,Scale,EntryBarIndex,Direction,InnovationStd,CurrentVolatility,RatioCurVolOverInnovStd,DynamicZScore,AbsZ0,EquilibriumDistance,HalfLifeValid,HalfLifeRSquared");

        int entryCount = 0;

        foreach (string dataset in CampaignDatasetCatalog.Generators.Keys)
        {
            foreach (string split in CampaignDatasetCatalog.Splits)
            {
                foreach (decimal scale in CampaignDatasetCatalog.Scales)
                {
                    decimal[] series = CampaignDatasetCatalog.Build(dataset, split, scale);
                    IReadOnlyList<CalibrationEntry> entries = CalibrationEntryBuilder.BuildEntries(dataset, series, CampaignGrids.Horizon);

                    foreach (CalibrationEntry entry in entries)
                    {
                        entryCount++;
                        WriteRatioRow(ratioWriter, dataset, split, scale, entry);
                        WriteComparisonRow(comparisonWriter, dataset, split, scale, entry);
                    }
                }
            }
        }

        return (comparisonPath, ratioPath, entryCount);
    }

    private static void WriteRatioRow(StreamWriter writer, string dataset, string split, decimal scale, CalibrationEntry entry)
    {
        BarMetrics m = entry.Metrics;
        string ratio = m.InnovationStd is double istd && istd > 0.0 && m.CurrentVolatility is double cv && double.IsFinite(cv)
            ? (cv / istd).ToString("G8", CultureInfo.InvariantCulture)
            : "";

        writer.WriteLine(string.Join(",",
            dataset, split, scale.ToString(CultureInfo.InvariantCulture), entry.EntryBarIndex, entry.Direction,
            Fmt(m.InnovationStd), Fmt(m.CurrentVolatility), ratio,
            Fmt(m.DynamicZScore), m.DynamicZScore is double z ? Fmt(Math.Abs(z)) : "",
            Fmt(m.EquilibriumDistance), m.HalfLifeValid, Fmt(m.HalfLifeRSquared)));
    }

    private static void WriteComparisonRow(StreamWriter writer, string dataset, string split, decimal scale, CalibrationEntry entry)
    {
        BarMetrics m = entry.Metrics;
        OutcomeMeasurement o = entry.Outcome;

        double? a1Distance = CandidateStopDistance.Compute(CandidateKind.A1, entry, ComparisonK);
        double? a2Distance = CandidateStopDistance.Compute(CandidateKind.A2, entry, ComparisonK);

        (bool a1Hit, int? a1Bar, EntryFate a1Fate) = a1Distance is double d1 ? StopLossEvaluator.Evaluate(o, d1) : (false, (int?)null, EntryFate.Undetermined);
        (bool a2Hit, int? a2Bar, EntryFate a2Fate) = a2Distance is double d2 ? StopLossEvaluator.Evaluate(o, d2) : (false, (int?)null, EntryFate.Undetermined);

        writer.WriteLine(string.Join(",",
            dataset, split, scale.ToString(CultureInfo.InvariantCulture), entry.EntryBarIndex, entry.Direction,
            Fmt(m.InnovationStd), Fmt(m.CurrentVolatility), Fmt(m.DynamicZScore),
            Fmt(o.MaxAdverseExcursion), Fmt(o.MaxFavorableExcursion), o.BarsAvailable, o.EquilibriumBar.HasValue, o.EquilibriumBar?.ToString() ?? "",
            a1Distance is double ad1 ? Fmt(ad1) : "", a1Distance is null ? "" : a1Hit.ToString(), a1Bar?.ToString() ?? "", a1Distance is null ? "N/A" : a1Fate.ToString(),
            a2Distance is double ad2 ? Fmt(ad2) : "", a2Distance is null ? "" : a2Hit.ToString(), a2Bar?.ToString() ?? "", a2Distance is null ? "N/A" : a2Fate.ToString()));
    }

    private static string Fmt(double? value) => value is double v && double.IsFinite(v) ? v.ToString("G8", CultureInfo.InvariantCulture) : "";
}
