using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using IQIAIndicator.Tests.Research.StopLossCalibration.A1Calibration;
using IQIAIndicator.Tests.Research.StopLossCalibration.Hybrid;

namespace IQIAIndicator.Tests.Research.StopLossCalibration;

/// <summary>
/// Sprint 15.15 (brief §11, Phase E/F). Verifies that Sprint 15.14's own workaround (manually
/// recomputing MeanReversionRate/ReversionRateCv from the correct k-range in
/// A1StableRegionDiscovery.ToRegionCandidate, rather than trusting StableRegionAnalyzer's then-buggy
/// merged-window fields) was mathematically equivalent to the fix now applied to
/// StableRegionAnalyzer.cs itself. Rebuilds the same TRAIN, scale=1 curves Sprint 15.14 used (reusing
/// A1Calibration/Hybrid infrastructure unchanged, never reimplementing the simulation), calls the
/// now-fixed StableRegionAnalyzer.FindStableWindows DIRECTLY (not via A1StableRegionDiscovery's
/// workaround), and compares the result field-by-field against Sprint 15.14's already-published
/// A1_stable_regions.csv. Does not modify any A1Calibration file - purely additive verification.
///
/// RETIRED as of Sprint 15.22 (QDE-012 InnovationStd correction) - see the xunit wrapper's
/// [Fact(Skip=...)] for the precise reason. Left fully intact (not deleted, not weakened) as a
/// historical record of what Sprint 15.15 verified about the PRE-15.22 model; do not delete this file
/// or "fix" it to pass again by loosening its tolerance - its premise (the current code reproduces
/// Sprint 15.14's published numbers exactly) is now permanently false by design, not a bug.
/// </summary>
public static class Sprint1515HistoricalReconciliation
{
    public static void VerifySprint1514Unaffected()
    {
        string outputDirectory = CampaignOutputPaths.ResolveOutputDirectory();
        string publishedPath = Path.Combine(outputDirectory, "A1_stable_regions.csv");
        Assert(File.Exists(publishedPath), $"Sprint 15.14's published A1_stable_regions.csv must still exist at {publishedPath}.");

        Dictionary<string, (double Mean, double Cv)> published = ParsePublishedRegions(publishedPath);

        var rows = new List<CandidateAggregateResult>();
        foreach (string dataset in IndependentDatasetCatalog.IndependentDatasets)
        {
            decimal[] series = CampaignDatasetCatalog.Build(dataset, "TRAIN", 1m);
            IReadOnlyList<CalibrationEntry> entries = CalibrationEntryBuilder.BuildEntries(dataset, series, CampaignGrids.Horizon);
            foreach (double k in CampaignGrids.KGrid)
            {
                rows.Add(CandidateAggregator.Aggregate("A1", dataset, "TRAIN", 1m, CandidateKind.A1, k, null, entries));
            }
        }

        foreach (string dataset in IndependentDatasetCatalog.IndependentDatasets)
        {
            List<CandidateAggregateResult> curve = rows.Where(r => r.Dataset == dataset).OrderBy(r => r.K).ToList();
            VerifyScope(dataset, curve, published);
        }

        List<CandidateAggregateResult> pooledCurve = A1PooledCurve.Build(rows).ToList();
        VerifyScope(A1PooledCurve.PooledScope, pooledCurve, published);
    }

    private static void VerifyScope(string scope, List<CandidateAggregateResult> curve, Dictionary<string, (double Mean, double Cv)> published)
    {
        Assert(published.ContainsKey(scope), $"No published Sprint 15.14 region found for scope '{scope}' - cannot verify.");

        IReadOnlyList<StableWindow> windows = StableRegionAnalyzer.FindStableWindows(curve);

        if (scope == "Trending")
        {
            // Trending's window is degenerate (flat at zero) - both mean and CV are exactly 0 either way.
            Assert(windows.Count == 1, $"Expected exactly one window for Trending. Actual={windows.Count}.");
            Assert(windows[0].MeanReversionRate == 0.0, "Trending's directly-computed mean must be exactly 0.");
            return;
        }

        Assert(windows.Count == 1, $"Expected exactly one merged window for '{scope}' (matching Sprint 15.14's own single-region finding). Actual={windows.Count}.");

        StableWindow w = windows[0];
        (double publishedMean, double publishedCv) = published[scope];

        Assert(Math.Abs(w.MeanReversionRate - publishedMean) < 1e-6,
            $"'{scope}': the now-fixed StableRegionAnalyzer must reproduce Sprint 15.14's published TrainMeanReversionRate exactly (proving the sprint's workaround was equivalent to this fix). Fixed-analyzer={w.MeanReversionRate:F6}, Published={publishedMean:F6}.");
        Assert(Math.Abs(w.ReversionRateCv - publishedCv) < 1e-6,
            $"'{scope}': the now-fixed StableRegionAnalyzer must reproduce Sprint 15.14's published TrainReversionRateCv exactly. Fixed-analyzer={w.ReversionRateCv:F6}, Published={publishedCv:F6}.");
    }

    private static Dictionary<string, (double Mean, double Cv)> ParsePublishedRegions(string path)
    {
        var result = new Dictionary<string, (double, double)>();
        string[] lines = File.ReadAllLines(path);
        for (int i = 1; i < lines.Length; i++) // skip header
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            string[] fields = lines[i].Split(',');
            string scope = fields[0];
            double mean = double.Parse(fields[4], CultureInfo.InvariantCulture);
            double cv = double.Parse(fields[5], CultureInfo.InvariantCulture);
            result[scope] = (mean, cv);
        }

        return result;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
