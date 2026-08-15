using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace IQIAIndicator.Tests.Research.StopLossCalibration.A1Calibration;

/// <summary>
/// Sprint 15.14 (brief §26.11). Human-readable digest of the whole sprint, in the same spirit as
/// Sprint 15.10's CampaignSummaryGenerator - the raw CSVs remain the reproducible source of truth, this
/// is only a readable summary for quick review.
/// </summary>
public static class A1CalibrationSummaryWriter
{
    public static string Write(
        string outputDirectory,
        A1CampaignResult campaign,
        System.Collections.Generic.IReadOnlyList<RegionCandidate> trainRegions,
        System.Collections.Generic.IReadOnlyList<RegionSplitCheck> validationChecks,
        System.Collections.Generic.IReadOnlyList<RegionTestResult> testResults,
        System.Collections.Generic.IReadOnlyList<CurveShapeResult> curveShapes,
        double maxScaleDeviation)
    {
        var sb = new StringBuilder();
        sb.AppendLine("SPRINT 15.14 — A1 K-CALIBRATION SUMMARY");
        sb.AppendLine($"TotalEntriesAnalyzed={campaign.TotalEntriesAnalyzed}");
        sb.AppendLine($"Elapsed={campaign.Elapsed}");
        sb.AppendLine();

        sb.AppendLine("=== TRAIN-discovered candidate regions (StableRegionAnalyzer, CV<=10%, exploratory diagnostic) ===");
        foreach (RegionCandidate r in trainRegions.OrderBy(r => r.Scope).ThenBy(r => r.StartK))
        {
            sb.AppendLine($"{r.Scope,-16} k=[{r.StartK:0.00}-{r.EndK:0.00}] TRAIN revRate={r.TrainMeanReversionRate:P1} (CV={r.TrainReversionRateCv:P1}) stopHitRate={r.TrainMeanStopHitRate:P1} degenerateNearZero={r.DegenerateNearZero}");
        }
        sb.AppendLine();

        sb.AppendLine("=== VALIDATION check (same k-range, no re-discovery) ===");
        foreach (RegionSplitCheck c in validationChecks.OrderBy(c => c.Scope).ThenBy(c => c.StartK))
        {
            sb.AppendLine($"{c.Scope,-16} k=[{c.StartK:0.00}-{c.EndK:0.00}] trainMean={c.TrainMean:P1} validationMean={FmtPct(c.SplitMean)} cv={FmtPct(c.SplitCv)} cvHolds={c.CvHolds} meanHolds={c.MeanHolds}");
        }
        sb.AppendLine();

        sb.AppendLine("=== TEST confirmation (evaluated last, no region created from TEST) ===");
        foreach (RegionTestResult r in testResults.OrderBy(r => r.Scope).ThenBy(r => r.StartK))
        {
            sb.AppendLine($"{r.Scope,-16} k=[{r.StartK:0.00}-{r.EndK:0.00}] train={r.TrainMean:P1} validation={FmtPct(r.ValidationMean)} test={FmtPct(r.TestMean)} verdict={r.Verdict}");
        }
        sb.AppendLine();

        sb.AppendLine("=== Curve shapes (TRAIN, scale=1) ===");
        foreach (CurveShapeResult c in curveShapes)
        {
            sb.AppendLine($"{c.Scope,-16} shape={c.Shape,-16} meanRevRate={FmtPct(c.OverallMeanReversionRate)} cv={FmtPct(c.OverallReversionRateCv)} widestStableWindow=[{c.WidestStableWindowStartK:0.00}-{c.WidestStableWindowEndK:0.00}] ({c.WidestStableWindowPoints}pts)");
        }
        sb.AppendLine();

        sb.AppendLine($"=== Scale invariance ===");
        sb.AppendLine($"Max |MeanStopRatio - K| observed across all (dataset, split, k, scale) combinations: {maxScaleDeviation:G10}");
        sb.AppendLine();

        string path = Path.Combine(outputDirectory, "A1_calibration_summary.txt");
        File.WriteAllText(path, sb.ToString());
        return path;
    }

    private static string FmtPct(double v) => double.IsNaN(v) ? "N/A" : v.ToString("P1", CultureInfo.InvariantCulture);
}
