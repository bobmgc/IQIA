using System;
using System.Collections.Generic;

namespace IQIAIndicator.Tests.Research.StopLossCalibration;

/// <summary>
/// Sprint 15.10. Runs the full QDE-012 calibration campaign and writes its outputs (raw CSVs +
/// digest summary) to Tests/Research/StopLossCalibration/Output/. Asserts only structural invariants
/// (grid sizes, no exceptions) - this is a data-generating research run, not a pass/fail unit test of
/// production behavior.
/// </summary>
public static class CampaignExecutionTests
{
    public static void RunAll()
    {
        CampaignResult result = CampaignRunner.Run();

        int expectedSeries = CampaignDatasetCatalog.Generators.Count * CampaignDatasetCatalog.Splits.Count * CampaignDatasetCatalog.Scales.Count;
        int expectedGridRowsPerSeries = CampaignGrids.KGrid.Count;
        int expectedD2RowsPerSeries = CampaignGrids.KGrid.Count * CampaignGrids.RSquaredGrid.Count;

        Assert(result.A1Rows.Count == expectedSeries * expectedGridRowsPerSeries,
            $"A1 row count mismatch. Expected {expectedSeries * expectedGridRowsPerSeries}, got {result.A1Rows.Count}.");
        Assert(result.A2Rows.Count == expectedSeries * expectedGridRowsPerSeries,
            $"A2 row count mismatch. Expected {expectedSeries * expectedGridRowsPerSeries}, got {result.A2Rows.Count}.");
        Assert(result.D1Rows.Count == expectedSeries * expectedGridRowsPerSeries,
            $"D1 row count mismatch. Expected {expectedSeries * expectedGridRowsPerSeries}, got {result.D1Rows.Count}.");
        Assert(result.D2Rows.Count == expectedSeries * expectedD2RowsPerSeries,
            $"D2 row count mismatch. Expected {expectedSeries * expectedD2RowsPerSeries}, got {result.D2Rows.Count}.");

        Assert(result.TotalEntriesAnalyzed > 0, "Campaign produced zero entries - something is broken.");

        // Phase A sanity check (QDE-012 campaign authorization §6), enforced on every row of every
        // candidate rather than spot-checked after the fact.
        ValidateRows("A1", result.A1Rows);
        ValidateRows("A2", result.A2Rows);
        ValidateRows("D1", result.D1Rows);
        ValidateRows("D2", result.D2Rows);

        string summaryPath = CampaignSummaryGenerator.Write(result);
        Assert(System.IO.File.Exists(summaryPath), $"Summary file was not written to {summaryPath}.");
    }

    private static void ValidateRows(string candidate, IReadOnlyList<CandidateAggregateResult> rows)
    {
        foreach (CandidateAggregateResult r in rows)
        {
            string ctx = $"{candidate} {r.Dataset}/{r.Split}/scale={r.Scale}/k={r.K}/r2={r.RSquaredThreshold}";

            Assert(r.TotalEntries >= 0, $"{ctx}: TotalEntries must be >= 0.");
            Assert(r.ApplicableEntries >= 0, $"{ctx}: ApplicableEntries must be >= 0.");
            Assert(r.DegenerateEntries >= 0, $"{ctx}: DegenerateEntries must be >= 0.");
            Assert(r.ApplicableEntries <= r.TotalEntries, $"{ctx}: ApplicableEntries must be <= TotalEntries.");
            Assert(r.DegenerateEntries + r.ApplicableEntries == r.TotalEntries, $"{ctx}: DegenerateEntries + ApplicableEntries must equal TotalEntries.");

            Assert(r.StopHits >= 0 && r.StopHits <= r.ApplicableEntries, $"{ctx}: StopHits out of range.");
            Assert(r.StoppedOut + r.Reverted + r.Undetermined == r.ApplicableEntries, $"{ctx}: Fate counts must sum to ApplicableEntries.");

            AssertRateOrNaN(r.StopHitRate, ctx, nameof(r.StopHitRate));
            AssertRateOrNaN(r.ReversionRate, ctx, nameof(r.ReversionRate));
            AssertRateOrNaN(r.UndeterminedRate, ctx, nameof(r.UndeterminedRate));
            AssertRateOrNaN(r.StopHitBeforeEquilibriumRate, ctx, nameof(r.StopHitBeforeEquilibriumRate));
            AssertRateOrNaN(r.FalseInvalidationRate, ctx, nameof(r.FalseInvalidationRate));
            AssertRateOrNaN(r.TrueInvalidationRate, ctx, nameof(r.TrueInvalidationRate));

            AssertFiniteNonNegativeOrNaN(r.MaeMean, ctx, nameof(r.MaeMean));
            AssertFiniteNonNegativeOrNaN(r.MaeMedian, ctx, nameof(r.MaeMedian));
            AssertFiniteNonNegativeOrNaN(r.MaeP75, ctx, nameof(r.MaeP75));
            AssertFiniteNonNegativeOrNaN(r.MaeP90, ctx, nameof(r.MaeP90));
            AssertFiniteNonNegativeOrNaN(r.MfeMean, ctx, nameof(r.MfeMean));
            AssertFiniteNonNegativeOrNaN(r.MfeMedian, ctx, nameof(r.MfeMedian));
            AssertFiniteNonNegativeOrNaN(r.MfeP75, ctx, nameof(r.MfeP75));
            AssertFiniteNonNegativeOrNaN(r.MfeP90, ctx, nameof(r.MfeP90));

            if (r.MedianTimeToEquilibrium is double t)
            {
                Assert(double.IsFinite(t) && t >= 1, $"{ctx}: MedianTimeToEquilibrium must be finite and >= 1 when present.");
            }

            Assert(!double.IsInfinity(r.MeanStopDistance), $"{ctx}: MeanStopDistance must never be Infinity.");
        }
    }

    private static void AssertRateOrNaN(double value, string ctx, string field)
    {
        Assert(double.IsNaN(value) || (value >= 0.0 && value <= 1.0), $"{ctx}: {field} must be NaN or within [0,1]. Actual={value}.");
    }

    private static void AssertFiniteNonNegativeOrNaN(double value, string ctx, string field)
    {
        Assert(double.IsNaN(value) || (double.IsFinite(value) && value >= 0.0), $"{ctx}: {field} must be NaN or finite and >= 0. Actual={value}.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
