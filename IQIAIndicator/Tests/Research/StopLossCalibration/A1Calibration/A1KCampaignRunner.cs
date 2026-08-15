using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using IQIAIndicator.Tests.Research.StopLossCalibration.Hybrid;

namespace IQIAIndicator.Tests.Research.StopLossCalibration.A1Calibration;

public sealed record A1CampaignResult(
    int TotalEntriesAnalyzed,
    IReadOnlyList<CandidateAggregateResult> Rows,
    TimeSpan Elapsed,
    string OutputDirectory,
    string GridCsvPath);

/// <summary>
/// Sprint 15.14. Single candidate: A1 (k x InnovationStd) only - no A2/D1/D2/HYBRID. Full grid: 11
/// dataset labels (CampaignDatasetCatalog, unchanged) x 3 splits x 6 scales x 40 k
/// (CampaignGrids, unchanged). Every row is produced by CandidateAggregator.Aggregate(..., A1, ...) -
/// the existing, unmodified Sprint 15.10 aggregator - called directly, not reimplemented. Dataset
/// independence labels reuse Hybrid.IndependentDatasetCatalog (Sprint 15.13) rather than duplicating
/// the same 9-independent/2-alias list a second time.
/// </summary>
public static class A1KCampaignRunner
{
    public static A1CampaignResult Run()
    {
        var stopwatch = Stopwatch.StartNew();
        var rows = new List<CandidateAggregateResult>();
        var labeledRows = new List<(CandidateAggregateResult Row, bool Independent, string AliasOf)>();
        int totalEntries = 0;

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
                    totalEntries += entries.Count;

                    foreach (double k in CampaignGrids.KGrid)
                    {
                        CandidateAggregateResult row = CandidateAggregator.Aggregate("A1", dataset, split, scale, CandidateKind.A1, k, null, entries);
                        rows.Add(row);
                        labeledRows.Add((row, independent, aliasOf));
                    }
                }
            }
        }

        stopwatch.Stop();

        string outputDirectory = CampaignOutputPaths.ResolveOutputDirectory();
        string gridPath = Path.Combine(outputDirectory, "A1_k_grid_results.csv");
        A1KCampaignOutput.Write(gridPath, labeledRows);

        return new A1CampaignResult(totalEntries, rows, stopwatch.Elapsed, outputDirectory, gridPath);
    }
}
