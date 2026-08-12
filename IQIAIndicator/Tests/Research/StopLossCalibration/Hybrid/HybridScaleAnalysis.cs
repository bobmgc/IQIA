using System;
using System.Globalization;
using System.IO;
using System.Linq;

namespace IQIAIndicator.Tests.Research.StopLossCalibration.Hybrid;

/// <summary>
/// Sprint 15.13 (brief §15). Scale invariance check at ComparisonK=2.0 across
/// {0.01,0.1,1,10,100,1000}, independent series only: MeanStopRatio should stay ~= K regardless of
/// scale, and (the check specific to this sprint) HybridA1Share/HybridA2Share/HybridUnknownShare - i.e.
/// the VolatilityRegime distribution over the applicable population - should also stay stable. A share
/// that moves with scale alone (not with the underlying series shape) would mean VolatilityRegime
/// classification is not scale-invariant, which brief §15 requires flagging and documenting, not
/// absorbing silently.
///
/// Pure reduction over the already-computed grid - no recomputation.
/// </summary>
public static class HybridScaleAnalysis
{
    public static string Write(HybridCampaignResult campaign)
    {
        string path = Path.Combine(campaign.OutputDirectory, "A1_A2_Hybrid_scale_analysis.csv");
        var rows = campaign.Rows
            .Where(r => r.IndependentDataset && Math.Abs(r.K - HybridEntryAnalysis.ComparisonK) < 1e-9)
            .OrderBy(r => r.Dataset).ThenBy(r => r.Split).ThenBy(r => r.Candidate).ThenBy(r => r.Scale);

        using var writer = new StreamWriter(path, false);
        writer.WriteLine("Candidate,Dataset,Split,Scale,MeanStopRatio,RatioMinusK,ApplicableEntries,DegenerateEntries,HybridA1Share,HybridA2Share,HybridUnknownShare");

        foreach (var r in rows)
        {
            writer.WriteLine(string.Join(",",
                r.Candidate, r.Dataset, r.Split, r.Scale.ToString(CultureInfo.InvariantCulture),
                Fmt(r.MeanStopRatio), Fmt(r.MeanStopRatio - r.K), r.ApplicableEntries, r.DegenerateEntries,
                r.HybridA1Share is double a1 ? Fmt(a1) : "", r.HybridA2Share is double a2 ? Fmt(a2) : "", r.HybridUnknownShare is double u ? Fmt(u) : ""));
        }

        return path;
    }

    private static string Fmt(double v) => double.IsNaN(v) ? "" : v.ToString("G6", CultureInfo.InvariantCulture);
}
