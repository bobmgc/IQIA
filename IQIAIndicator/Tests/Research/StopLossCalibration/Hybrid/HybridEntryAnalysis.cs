using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace IQIAIndicator.Tests.Research.StopLossCalibration.Hybrid;

/// <summary>
/// Sprint 15.13 (brief §11, regime-analysis CSV). Entry-level (not k-swept) dump: for every entry in
/// the campaign's 11-dataset x 3-split x 6-scale population, records its VolatilityRegime and the
/// CurrentVolatility/InnovationStd ratio (Sprint 15.12's mechanism variable), plus a same-entry A1 vs
/// A2 vs HybridStrict vs HybridConservative outcome at the fixed ComparisonK=2.0 anchor already
/// established in Sprint 15.12 (A1VsA2EntryAnalysis.ComparisonK - reused directly, not a new choice).
/// This is what answers brief §11's questions (does HIGH actually correspond to A2 being better; does
/// the ratio separate cleanly by regime) directly, entry by entry.
///
/// Reuses CalibrationEntryBuilder/CandidateStopDistance/HybridStopDistance/StopLossEvaluator exactly as
/// built - recomputes no scientific metric.
/// </summary>
public static class HybridEntryAnalysis
{
    public static double ComparisonK => A1VsA2EntryAnalysis.ComparisonK;

    public static (string Path, int EntryCount) RunAndWrite()
    {
        string outputDirectory = CampaignOutputPaths.ResolveOutputDirectory();
        string path = Path.Combine(outputDirectory, "A1_A2_Hybrid_regime_analysis.csv");

        using var writer = new StreamWriter(path, false);
        writer.WriteLine(
            "Dataset,IndependentDataset,AliasOf,Split,Scale,EntryBarIndex,Direction,InnovationStd,CurrentVolatility,RatioCurVolOverInnovStd," +
            "VolatilityRegime,VolatilityPercentile,A1_StopDistance,A1_Fate,A2_StopDistance,A2_Fate," +
            "HybridStrict_Leg,HybridStrict_StopDistance,HybridStrict_Fate,HybridConservative_Leg,HybridConservative_StopDistance,HybridConservative_Fate");

        int entryCount = 0;

        foreach (string dataset in CampaignDatasetCatalog.Generators.Keys)
        {
            bool independent = IndependentDatasetCatalog.IsIndependent(dataset);
            string aliasOf = IndependentDatasetCatalog.AliasOf(dataset);

            foreach (string split in CampaignDatasetCatalog.Splits)
            {
                foreach (decimal scale in CampaignDatasetCatalog.Scales)
                {
                    decimal[] series = CampaignDatasetCatalog.Build(dataset, split, scale);
                    IReadOnlyList<CalibrationEntry> entries = CalibrationEntryBuilder.BuildEntries(dataset, series, CampaignGrids.Horizon);

                    foreach (CalibrationEntry entry in entries)
                    {
                        entryCount++;
                        WriteRow(writer, dataset, independent, aliasOf, split, scale, entry);
                    }
                }
            }
        }

        return (path, entryCount);
    }

    private static void WriteRow(StreamWriter writer, string dataset, bool independent, string aliasOf, string split, decimal scale, CalibrationEntry entry)
    {
        BarMetrics m = entry.Metrics;
        OutcomeMeasurement o = entry.Outcome;

        string ratio = m.InnovationStd is double istd && istd > 0.0 && m.CurrentVolatility is double cv && double.IsFinite(cv)
            ? (cv / istd).ToString("G8", CultureInfo.InvariantCulture)
            : "";

        double? a1Distance = CandidateStopDistance.Compute(CandidateKind.A1, entry, ComparisonK);
        double? a2Distance = CandidateStopDistance.Compute(CandidateKind.A2, entry, ComparisonK);
        EntryFate a1Fate = a1Distance is double d1 ? StopLossEvaluator.Evaluate(o, d1).Fate : EntryFate.Undetermined;
        EntryFate a2Fate = a2Distance is double d2 ? StopLossEvaluator.Evaluate(o, d2).Fate : EntryFate.Undetermined;

        (double? strictDistance, HybridLeg strictLeg) = HybridStopDistance.Compute(HybridCandidateKind.HybridStrict, entry, ComparisonK);
        (double? consDistance, HybridLeg consLeg) = HybridStopDistance.Compute(HybridCandidateKind.HybridConservative, entry, ComparisonK);
        EntryFate strictFate = strictDistance is double ds ? StopLossEvaluator.Evaluate(o, ds).Fate : EntryFate.Undetermined;
        EntryFate consFate = consDistance is double dc ? StopLossEvaluator.Evaluate(o, dc).Fate : EntryFate.Undetermined;

        writer.WriteLine(string.Join(",",
            dataset, independent, aliasOf, split, scale.ToString(CultureInfo.InvariantCulture), entry.EntryBarIndex, entry.Direction,
            Fmt(m.InnovationStd), Fmt(m.CurrentVolatility), ratio, m.VolatilityRegime ?? "", Fmt(m.VolatilityPercentile),
            a1Distance is double ad1 ? Fmt(ad1) : "", a1Distance is null ? "N/A" : a1Fate.ToString(),
            a2Distance is double ad2 ? Fmt(ad2) : "", a2Distance is null ? "N/A" : a2Fate.ToString(),
            strictLeg, strictDistance is double sd ? Fmt(sd) : "", strictDistance is null ? "N/A" : strictFate.ToString(),
            consLeg, consDistance is double cd ? Fmt(cd) : "", consDistance is null ? "N/A" : consFate.ToString()));
    }

    private static string Fmt(double? value) => value is double v && double.IsFinite(v) ? v.ToString("G8", CultureInfo.InvariantCulture) : "";
}
