using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace IQIAIndicator.Tests.Research.StopLossCalibration.Hybrid;

public sealed record HybridCampaignResult(
    int TotalEntriesAnalyzed,
    IReadOnlyList<HybridAggregateResult> Rows,
    TimeSpan Elapsed,
    string OutputDirectory,
    string ComparisonCsvPath,
    string DefinitionLockPath);

/// <summary>
/// Sprint 15.13. Full grid: 11 dataset labels (CampaignDatasetCatalog, unchanged) x 3 splits x 6 scales
/// x 40 k (CampaignGrids, unchanged) x {A1, A2, HybridStrict, HybridConservative}. Writes
/// hybrid_definition_lock.txt FIRST (before any entry is processed), then A1_A2_Hybrid_comparison.csv.
/// Builds each (dataset,split,scale)'s CalibrationEntry list exactly once via
/// CalibrationEntryBuilder.BuildEntries (reused, not reimplemented).
/// </summary>
public static class HybridCampaignRunner
{
    public static HybridCampaignResult Run()
    {
        string definitionLockPath = HybridCampaignOutput.WriteDefinitionLock();

        var stopwatch = Stopwatch.StartNew();
        var rows = new List<HybridAggregateResult>();
        int totalEntries = 0;

        foreach (string dataset in CampaignDatasetCatalog.Generators.Keys)
        {
            foreach (string split in CampaignDatasetCatalog.Splits)
            {
                foreach (decimal scale in CampaignDatasetCatalog.Scales)
                {
                    decimal[] series = CampaignDatasetCatalog.Build(dataset, split, scale);
                    IReadOnlyList<CalibrationEntry> entries = CalibrationEntryBuilder.BuildEntries(dataset, series, CampaignGrids.Horizon);
                    totalEntries += entries.Count;

                    foreach (double k in CampaignGrids.KGrid)
                    {
                        rows.Add(HybridCandidateAggregator.Aggregate("A1", dataset, split, scale, HybridCandidateKind.A1, k, entries));
                        rows.Add(HybridCandidateAggregator.Aggregate("A2", dataset, split, scale, HybridCandidateKind.A2, k, entries));
                        rows.Add(HybridCandidateAggregator.Aggregate("HybridStrict", dataset, split, scale, HybridCandidateKind.HybridStrict, k, entries));
                        rows.Add(HybridCandidateAggregator.Aggregate("HybridConservative", dataset, split, scale, HybridCandidateKind.HybridConservative, k, entries));
                    }
                }
            }
        }

        stopwatch.Stop();

        string outputDirectory = CampaignOutputPaths.ResolveOutputDirectory();
        string comparisonPath = Path.Combine(outputDirectory, "A1_A2_Hybrid_comparison.csv");
        HybridCampaignOutput.Write(comparisonPath, rows);

        return new HybridCampaignResult(totalEntries, rows, stopwatch.Elapsed, outputDirectory, comparisonPath, definitionLockPath);
    }
}
