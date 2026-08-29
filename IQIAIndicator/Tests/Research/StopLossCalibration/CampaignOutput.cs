using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace IQIAIndicator.Tests.Research.StopLossCalibration;

/// <summary>Sprint 15.10. Resolves an output directory relative to the Tests project regardless of
/// the test runner's working directory, by walking up from the running assembly's location until the
/// .csproj marker is found.</summary>
internal static class CampaignOutputPaths
{
    public static string ResolveOutputDirectory()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "IQIAIndicator.Tests.csproj")))
        {
            dir = Directory.GetParent(dir)?.FullName;
        }

        if (dir is null)
        {
            throw new InvalidOperationException("Could not locate IQIAIndicator.Tests.csproj to resolve the campaign output directory.");
        }

        string output = Path.Combine(dir, "Research", "StopLossCalibration", "Output");
        Directory.CreateDirectory(output);
        return output;
    }
}

internal static class CampaignCsvWriter
{
    private const string Header =
        "Candidate,Dataset,Split,Scale,K,RSquaredThreshold,TotalEntries,ApplicableEntries,DegenerateEntries," +
        "StopHits,StoppedOut,Reverted,Undetermined,StopHitRate,ReversionRate,UndeterminedRate,StopHitBeforeEquilibriumRate," +
        "FalseInvalidationRate,TrueInvalidationRate,MaeMean,MaeMedian,MaeP75,MaeP90,MfeMean,MfeMedian,MfeP75,MfeP90," +
        "MedianTimeToEquilibrium,MeanStopDistance,MeanStopRatio";

    public static void Write(string path, IEnumerable<CandidateAggregateResult> rows)
    {
        using var writer = new StreamWriter(path, false);
        writer.WriteLine(Header);
        foreach (CandidateAggregateResult r in rows)
        {
            writer.WriteLine(string.Join(",",
                r.Candidate, r.Dataset, r.Split,
                r.Scale.ToString(CultureInfo.InvariantCulture),
                r.K.ToString(CultureInfo.InvariantCulture),
                r.RSquaredThreshold?.ToString(CultureInfo.InvariantCulture) ?? "",
                r.TotalEntries, r.ApplicableEntries, r.DegenerateEntries,
                r.StopHits, r.StoppedOut, r.Reverted, r.Undetermined,
                Fmt(r.StopHitRate), Fmt(r.ReversionRate), Fmt(r.UndeterminedRate), Fmt(r.StopHitBeforeEquilibriumRate),
                Fmt(r.FalseInvalidationRate), Fmt(r.TrueInvalidationRate),
                Fmt(r.MaeMean), Fmt(r.MaeMedian), Fmt(r.MaeP75), Fmt(r.MaeP90),
                Fmt(r.MfeMean), Fmt(r.MfeMedian), Fmt(r.MfeP75), Fmt(r.MfeP90),
                r.MedianTimeToEquilibrium is double t ? Fmt(t) : "",
                Fmt(r.MeanStopDistance), Fmt(r.MeanStopRatio)));
        }
    }

    private static string Fmt(double v) => double.IsNaN(v) ? "" : v.ToString("G6", CultureInfo.InvariantCulture);
}
