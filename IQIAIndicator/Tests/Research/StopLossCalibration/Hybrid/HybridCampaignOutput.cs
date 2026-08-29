using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace IQIAIndicator.Tests.Research.StopLossCalibration.Hybrid;

/// <summary>
/// Sprint 15.13. CSV writer for HybridAggregateResult rows, and the HYBRID definition-lock file (the
/// brief's pre-campaign gate requirement - the exact routing rule, logged before any campaign entry is
/// processed, both to console and to a file so it's captured either way). Reuses
/// CampaignOutputPaths.ResolveOutputDirectory() unchanged - writes into the same
/// Tests/Research/StopLossCalibration/Output/ folder as every prior sprint's CSVs.
/// </summary>
internal static class HybridCampaignOutput
{
    private const string Header =
        "Candidate,Dataset,IndependentDataset,AliasOf,Split,Scale,K,TotalEntries,ApplicableEntries,DegenerateEntries," +
        "StopHits,StoppedOut,Reverted,Undetermined,StopHitRate,ReversionRate,UndeterminedRate,StopHitBeforeEquilibriumRate," +
        "MaeMean,MaeMedian,MaeP75,MaeP90,MfeMean,MfeMedian,MfeP75,MfeP90," +
        "MedianTimeToEquilibrium,MeanStopDistance,MeanStopRatio,HybridA1Share,HybridA2Share,HybridUnknownShare";

    public static void Write(string path, IEnumerable<HybridAggregateResult> rows)
    {
        using var writer = new StreamWriter(path, false);
        writer.WriteLine(Header);
        foreach (HybridAggregateResult r in rows)
        {
            writer.WriteLine(string.Join(",",
                r.Candidate, r.Dataset, r.IndependentDataset, r.AliasOf, r.Split,
                r.Scale.ToString(CultureInfo.InvariantCulture),
                r.K.ToString(CultureInfo.InvariantCulture),
                r.TotalEntries, r.ApplicableEntries, r.DegenerateEntries,
                r.StopHits, r.StoppedOut, r.Reverted, r.Undetermined,
                Fmt(r.StopHitRate), Fmt(r.ReversionRate), Fmt(r.UndeterminedRate), Fmt(r.StopHitBeforeEquilibriumRate),
                Fmt(r.MaeMean), Fmt(r.MaeMedian), Fmt(r.MaeP75), Fmt(r.MaeP90),
                Fmt(r.MfeMean), Fmt(r.MfeMedian), Fmt(r.MfeP75), Fmt(r.MfeP90),
                r.MedianTimeToEquilibrium is double t ? Fmt(t) : "",
                Fmt(r.MeanStopDistance), Fmt(r.MeanStopRatio),
                r.HybridA1Share is double a1 ? Fmt(a1) : "",
                r.HybridA2Share is double a2 ? Fmt(a2) : "",
                r.HybridUnknownShare is double u ? Fmt(u) : ""));
        }
    }

    private static string Fmt(double v) => double.IsNaN(v) ? "" : v.ToString("G6", CultureInfo.InvariantCulture);

    public const string DefinitionLock =
        "HYBRID_STRICT:\n" +
        "HIGH -> A2\n" +
        "LOW -> A1\n" +
        "MEDIUM -> A1\n" +
        "UNKNOWN -> NOT_APPLICABLE\n" +
        "\n" +
        "HYBRID_CONSERVATIVE:\n" +
        "HIGH -> A2\n" +
        "LOW -> A1\n" +
        "MEDIUM -> A1\n" +
        "UNKNOWN -> A1\n";

    /// <summary>Writes the locked HYBRID rule to both console and a file, BEFORE any campaign entry is
    /// processed. No other rule is implemented; this text is never edited after seeing a result.</summary>
    public static string WriteDefinitionLock()
    {
        string outputDirectory = CampaignOutputPaths.ResolveOutputDirectory();
        string path = Path.Combine(outputDirectory, "hybrid_definition_lock.txt");
        File.WriteAllText(path, DefinitionLock);
        Console.WriteLine(DefinitionLock);
        return path;
    }
}
