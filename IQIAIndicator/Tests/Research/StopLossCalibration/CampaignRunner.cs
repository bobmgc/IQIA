using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace IQIAIndicator.Tests.Research.StopLossCalibration;

public sealed record CampaignResult(
    int TotalEntriesAnalyzed,
    IReadOnlyList<CandidateAggregateResult> A1Rows,
    IReadOnlyList<CandidateAggregateResult> A2Rows,
    IReadOnlyList<CandidateAggregateResult> D1Rows,
    IReadOnlyList<CandidateAggregateResult> D2Rows,
    IReadOnlyDictionary<string, int> EntryCountsBySeriesKey,
    TimeSpan Elapsed,
    string OutputDirectory);

/// <summary>
/// Sprint 15.10 (QDE-012 Phases 8-12). Orchestrates the full campaign using only the already-validated
/// harness components (BarMetricsComputer, OutcomeSimulator, StopLossEvaluator, CalibrationEntryBuilder)
/// - reimplements no scientific metric, modifies no production class.
/// </summary>
public static class CampaignRunner
{
    public static CampaignResult Run()
    {
        var stopwatch = Stopwatch.StartNew();
        var a1Rows = new List<CandidateAggregateResult>();
        var a2Rows = new List<CandidateAggregateResult>();
        var d1Rows = new List<CandidateAggregateResult>();
        var d2Rows = new List<CandidateAggregateResult>();
        var entryCounts = new Dictionary<string, int>();
        int totalEntries = 0;

        foreach (string dataset in CampaignDatasetCatalog.Generators.Keys)
        {
            foreach (string split in CampaignDatasetCatalog.Splits)
            {
                foreach (decimal scale in CampaignDatasetCatalog.Scales)
                {
                    decimal[] series = CampaignDatasetCatalog.Build(dataset, split, scale);
                    IReadOnlyList<CalibrationEntry> entries = CalibrationEntryBuilder.BuildEntries(dataset, series, CampaignGrids.Horizon);

                    string key = $"{dataset}|{split}|{scale}";
                    entryCounts[key] = entries.Count;
                    totalEntries += entries.Count;

                    foreach (double k in CampaignGrids.KGrid)
                    {
                        a1Rows.Add(CandidateAggregator.Aggregate("A1", dataset, split, scale, CandidateKind.A1, k, null, entries));
                        a2Rows.Add(CandidateAggregator.Aggregate("A2", dataset, split, scale, CandidateKind.A2, k, null, entries));
                        d1Rows.Add(CandidateAggregator.Aggregate("D1", dataset, split, scale, CandidateKind.D1, k, null, entries));

                        foreach (double r2 in CampaignGrids.RSquaredGrid)
                        {
                            d2Rows.Add(CandidateAggregator.Aggregate("D2", dataset, split, scale, CandidateKind.D2, k, r2, entries));
                        }
                    }
                }
            }
        }

        stopwatch.Stop();

        string outputDirectory = CampaignOutputPaths.ResolveOutputDirectory();
        CampaignCsvWriter.Write(Path.Combine(outputDirectory, "A1_results.csv"), a1Rows);
        CampaignCsvWriter.Write(Path.Combine(outputDirectory, "A2_results.csv"), a2Rows);
        CampaignCsvWriter.Write(Path.Combine(outputDirectory, "D1_results.csv"), d1Rows);
        CampaignCsvWriter.Write(Path.Combine(outputDirectory, "D2_results.csv"), d2Rows);

        return new CampaignResult(totalEntries, a1Rows, a2Rows, d1Rows, d2Rows, entryCounts, stopwatch.Elapsed, outputDirectory);
    }

    /// <summary>
    /// Sprint 15.11 Phase 4. Targeted re-run of Candidate A2 only, after the MeanStopRatio
    /// denominator fix - same datasets/splits/scales/k-grid/horizon/length as the full QDE-012
    /// campaign, but skips A1/D1/D2 entirely (their formulas/results are unaffected by this fix, so
    /// re-running them would be wasted work, per the instruction not to redo the full campaign
    /// unnecessarily). Writes to a distinctly-named file so the original A2_results.csv from the
    /// QDE-012 campaign is preserved for before/after comparison.
    /// </summary>
    public static CampaignResult RunA2Only()
    {
        var stopwatch = Stopwatch.StartNew();
        var a2Rows = new List<CandidateAggregateResult>();
        var entryCounts = new Dictionary<string, int>();
        int totalEntries = 0;

        foreach (string dataset in CampaignDatasetCatalog.Generators.Keys)
        {
            foreach (string split in CampaignDatasetCatalog.Splits)
            {
                foreach (decimal scale in CampaignDatasetCatalog.Scales)
                {
                    decimal[] series = CampaignDatasetCatalog.Build(dataset, split, scale);
                    IReadOnlyList<CalibrationEntry> entries = CalibrationEntryBuilder.BuildEntries(dataset, series, CampaignGrids.Horizon);

                    string key = $"{dataset}|{split}|{scale}";
                    entryCounts[key] = entries.Count;
                    totalEntries += entries.Count;

                    foreach (double k in CampaignGrids.KGrid)
                    {
                        a2Rows.Add(CandidateAggregator.Aggregate("A2", dataset, split, scale, CandidateKind.A2, k, null, entries));
                    }
                }
            }
        }

        stopwatch.Stop();

        string outputDirectory = CampaignOutputPaths.ResolveOutputDirectory();
        CampaignCsvWriter.Write(Path.Combine(outputDirectory, "A2_results_corrected.csv"), a2Rows);

        return new CampaignResult(
            totalEntries,
            Array.Empty<CandidateAggregateResult>(),
            a2Rows,
            Array.Empty<CandidateAggregateResult>(),
            Array.Empty<CandidateAggregateResult>(),
            entryCounts,
            stopwatch.Elapsed,
            outputDirectory);
    }
}
