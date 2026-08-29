using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace IQIAIndicator.Tests.Research.StopLossCalibration;

/// <summary>
/// Sprint 15.10. Reduces the full campaign grid down to the numbers actually needed to write the
/// scientific report: stable windows per (candidate, dataset, split), a scale-invariance spot check,
/// and the D2 R-squared ablation. Writes a single readable text file - the raw CSVs remain the
/// reproducible source of truth, this is the digest.
/// </summary>
public static class CampaignSummaryGenerator
{
    public static string Write(CampaignResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine("SPRINT 15.10 CAMPAIGN SUMMARY");
        sb.AppendLine($"TotalEntriesAnalyzed={result.TotalEntriesAnalyzed}");
        sb.AppendLine($"Elapsed={result.Elapsed}");
        sb.AppendLine();

        sb.AppendLine("=== Entry counts per (dataset, split), scale=1 ===");
        foreach (string dataset in CampaignDatasetCatalog.Generators.Keys)
        {
            foreach (string split in CampaignDatasetCatalog.Splits)
            {
                string key = $"{dataset}|{split}|1";
                int count = result.EntryCountsBySeriesKey.TryGetValue(key, out int c) ? c : -1;
                sb.AppendLine($"{dataset,-22} {split,-11} entries={count}");
            }
        }
        sb.AppendLine();

        WriteStableRegions(sb, "A1", result.A1Rows);
        WriteStableRegions(sb, "A2", result.A2Rows);
        WriteStableRegions(sb, "D1", result.D1Rows);

        WriteScaleInvarianceCheck(sb, "A1", result.A1Rows, representativeK: 2.0);
        WriteScaleInvarianceCheck(sb, "D1", result.D1Rows, representativeK: 5.0);

        WriteD2R2Ablation(sb, result.D2Rows, representativeK: 5.0);

        WriteDegenerateRates(sb, "D1", result.D1Rows);

        string path = Path.Combine(result.OutputDirectory, "campaign_summary.txt");
        File.WriteAllText(path, sb.ToString());
        return path;
    }

    private static void WriteStableRegions(StringBuilder sb, string candidate, IReadOnlyList<CandidateAggregateResult> rows)
    {
        sb.AppendLine($"=== Stable windows for {candidate} (TRAIN + VALIDATION, scale=1) ===");
        sb.AppendLine("Gated on ReversionRate CV<=10% only (protocol-defined metric, QDE-012 §17). " +
            "falseInvalidRate shown below is DESCRIPTIVE ONLY - it never affects whether a window qualifies as stable.");
        foreach (string dataset in CampaignDatasetCatalog.Generators.Keys)
        {
            foreach (string split in new[] { "TRAIN", "VALIDATION" })
            {
                List<CandidateAggregateResult> curve = rows
                    .Where(r => r.Dataset == dataset && r.Split == split && r.Scale == 1m)
                    .OrderBy(r => r.K)
                    .ToList();

                if (curve.Count == 0) continue;

                IReadOnlyList<StableWindow> windows = StableRegionAnalyzer.FindStableWindows(curve);
                string windowsText = windows.Count == 0
                    ? "NONE"
                    : string.Join("; ", windows.Select(w => $"[k={w.StartK:0.00}-{w.EndK:0.00}, revRate={w.MeanReversionRate:P1} (CV={w.ReversionRateCv:P1}), falseInvalidRate(descriptive)={w.MeanFalseInvalidationRate:P1}]"));

                sb.AppendLine($"{dataset,-22} {split,-11} {windowsText}");
            }
        }
        sb.AppendLine();
    }

    private static void WriteScaleInvarianceCheck(StringBuilder sb, string candidate, IReadOnlyList<CandidateAggregateResult> rows, double representativeK)
    {
        sb.AppendLine($"=== Scale invariance check for {candidate} at k={representativeK:0.00} (TRAIN) ===");
        foreach (string dataset in CampaignDatasetCatalog.Generators.Keys)
        {
            foreach (decimal scale in CampaignDatasetCatalog.Scales)
            {
                CandidateAggregateResult? row = rows.FirstOrDefault(r =>
                    r.Dataset == dataset && r.Split == "TRAIN" && r.Scale == scale && Math.Abs(r.K - representativeK) < 1e-9);

                if (row is null) continue;

                sb.AppendLine(
                    $"{dataset,-22} scale={scale.ToString(CultureInfo.InvariantCulture),-8} " +
                    $"applicable={row.ApplicableEntries,-5} revRate={FmtPct(row.ReversionRate)} " +
                    $"meanStopRatio={row.MeanStopRatio:0.0000} (expect ~{representativeK:0.00} if invariant) " +
                    $"meanStopDistance={row.MeanStopDistance:0.######}");
            }
        }
        sb.AppendLine();
    }

    private static void WriteD2R2Ablation(StringBuilder sb, IReadOnlyList<CandidateAggregateResult> d2Rows, double representativeK)
    {
        sb.AppendLine($"=== D2 R-squared ablation at k={representativeK:0.00} (TRAIN, scale=1, aggregated across datasets) ===");
        foreach (double r2 in CampaignGrids.RSquaredGrid)
        {
            List<CandidateAggregateResult> matches = d2Rows
                .Where(r => r.Split == "TRAIN" && r.Scale == 1m && Math.Abs(r.K - representativeK) < 1e-9 && r.RSquaredThreshold is double t && Math.Abs(t - r2) < 1e-9)
                .ToList();

            if (matches.Count == 0) continue;

            int totalApplicable = matches.Sum(r => r.ApplicableEntries);
            int totalStoppedOut = matches.Sum(r => r.StoppedOut);
            int totalReverted = matches.Sum(r => r.Reverted);
            int totalUndetermined = matches.Sum(r => r.Undetermined);

            double revRate = totalApplicable == 0 ? double.NaN : totalReverted / (double)totalApplicable;
            double stopRate = totalApplicable == 0 ? double.NaN : totalStoppedOut / (double)totalApplicable;
            double undeterminedRate = totalApplicable == 0 ? double.NaN : totalUndetermined / (double)totalApplicable;

            sb.AppendLine(
                $"R2>={r2:0.00,-5} totalApplicable={totalApplicable,-6} revRate={FmtPct(revRate)} " +
                $"stoppedOutRate={FmtPct(stopRate)} undeterminedRate={FmtPct(undeterminedRate)}");
        }
        sb.AppendLine();
    }

    private static void WriteDegenerateRates(StringBuilder sb, string candidate, IReadOnlyList<CandidateAggregateResult> rows)
    {
        sb.AppendLine($"=== {candidate} degenerate (k <= |entry z-score|) rate by k, TRAIN, scale=1, aggregated across datasets ===");
        foreach (double k in CampaignGrids.KGrid)
        {
            List<CandidateAggregateResult> matches = rows.Where(r => r.Split == "TRAIN" && r.Scale == 1m && Math.Abs(r.K - k) < 1e-9).ToList();
            if (matches.Count == 0) continue;

            int total = matches.Sum(r => r.TotalEntries);
            int degenerate = matches.Sum(r => r.DegenerateEntries);
            double rate = total == 0 ? double.NaN : degenerate / (double)total;
            sb.AppendLine($"k={k,-6:0.00} degenerateRate={FmtPct(rate)} ({degenerate}/{total})");
        }
        sb.AppendLine();
    }

    private static string FmtPct(double v) => double.IsNaN(v) ? "N/A" : v.ToString("P1", CultureInfo.InvariantCulture);
}
