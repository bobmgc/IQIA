using System;

namespace IQIAIndicator.Tests.Research.StopLossCalibration;

/// <summary>Sprint 15.11 Phase 4. Runs the targeted A2-only campaign (post-fix) and writes
/// A2_results_corrected.csv. Same structural sanity checks as the full campaign, scoped to A2.</summary>
public static class A2ScaleCampaignTests
{
    public static void RunAll()
    {
        CampaignResult result = CampaignRunner.RunA2Only();

        int expectedSeries = CampaignDatasetCatalog.Generators.Count * CampaignDatasetCatalog.Splits.Count * CampaignDatasetCatalog.Scales.Count;
        int expectedRows = expectedSeries * CampaignGrids.KGrid.Count;

        Assert(result.A2Rows.Count == expectedRows, $"A2 row count mismatch. Expected {expectedRows}, got {result.A2Rows.Count}.");
        Assert(result.TotalEntriesAnalyzed > 0, "A2-only campaign produced zero entries.");

        foreach (CandidateAggregateResult r in result.A2Rows)
        {
            string ctx = $"A2 {r.Dataset}/{r.Split}/scale={r.Scale}/k={r.K}";
            Assert(r.ApplicableEntries <= r.TotalEntries, $"{ctx}: ApplicableEntries must be <= TotalEntries.");
            Assert(r.DegenerateEntries + r.ApplicableEntries == r.TotalEntries, $"{ctx}: DegenerateEntries + ApplicableEntries must equal TotalEntries.");
            Assert(!double.IsInfinity(r.MeanStopRatio), $"{ctx}: MeanStopRatio must never be Infinity.");
            Assert(!double.IsInfinity(r.MeanStopDistance), $"{ctx}: MeanStopDistance must never be Infinity.");
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
