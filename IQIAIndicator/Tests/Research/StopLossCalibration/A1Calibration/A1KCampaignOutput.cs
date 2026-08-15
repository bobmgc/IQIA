using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace IQIAIndicator.Tests.Research.StopLossCalibration.A1Calibration;

/// <summary>
/// Sprint 15.14. CSV writer for the full A1 k-grid, extending CandidateAggregateResult's existing
/// 30-column schema (reused unchanged) with IndependentDataset/AliasOf (brief §6). Reuses
/// CampaignOutputPaths.ResolveOutputDirectory() unchanged.
/// </summary>
internal static class A1KCampaignOutput
{
    private const string Header =
        "Candidate,Dataset,IndependentDataset,AliasOf,Split,Scale,K,TotalEntries,ApplicableEntries,DegenerateEntries," +
        "StopHits,StoppedOut,Reverted,Undetermined,StopHitRate,ReversionRate,UndeterminedRate,StopHitBeforeEquilibriumRate," +
        "FalseInvalidationRate,TrueInvalidationRate,MaeMean,MaeMedian,MaeP75,MaeP90,MfeMean,MfeMedian,MfeP75,MfeP90," +
        "MedianTimeToEquilibrium,MeanStopDistance,MeanStopRatio";

    public static void Write(string path, IEnumerable<(CandidateAggregateResult Row, bool IndependentDataset, string AliasOf)> rows)
    {
        using var writer = new StreamWriter(path, false);
        writer.WriteLine(Header);
        foreach ((CandidateAggregateResult r, bool independent, string aliasOf) in rows)
        {
            writer.WriteLine(string.Join(",",
                r.Candidate, r.Dataset, independent, aliasOf, r.Split,
                r.Scale.ToString(CultureInfo.InvariantCulture),
                r.K.ToString(CultureInfo.InvariantCulture),
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

    public static string Fmt(double v) => double.IsNaN(v) ? "" : v.ToString("G6", CultureInfo.InvariantCulture);
}
