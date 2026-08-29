using System.Globalization;
using System.Text;
using IQIAIndicator.Tests.Research.StructuralBreakInformationAudit.Support;
using Xunit;

namespace IQIAIndicator.Tests.Research.StructuralBreakInformationAudit;

/// <summary>
/// Lot 15.9 ("StructuralBreak Evidence Quality &amp; Information Content Audit"), AUDIT-ONLY,
/// DESCRIPTIVE-ONLY. No production file is read here beyond what
/// <see cref="StructuralBreakObservationSetBuilder"/> already reads/reconstructs. No calibration, no
/// threshold selection, no P&amp;L/win-rate/Sharpe.
///
/// Covers brief Section A (observation-set construction + determinism check) and Section B.1/B.2: the
/// exact origin of the Lot 15.8 finding that Cusum.Confidence saturates at 1.0 for 100% of
/// Detected=true bars - where in the CUSUM -&gt; Contract -&gt; Fusion -&gt; FusionStateManager pipeline the
/// variance collapses.
///
/// Lot 16 update: B.2's outcome assertion was inverted. Pre-Lot-16 it pinned
/// <c>Contract.Strength == Cusum.Confidence</c> (the saturating pass-through). Lot 16 replaced
/// Strength with <c>r/(1+r)</c>, so B.2 now asserts the opposite: Strength is decoupled from the
/// clamp, carries non-zero variance among detected bars, and is not saturated. B.1 (which measures
/// <c>Cusum.Confidence</c> itself, still produced by the untouched <c>CusumStatistics.Compute</c>) is
/// unchanged.
/// </summary>
public sealed class StructuralBreakStrengthDistributionAuditTests
{
    private readonly ITestOutputHelper _output;

    public StructuralBreakStrengthDistributionAuditTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static readonly double[] QuantileLevels = { 0.01, 0.05, 0.10, 0.25, 0.50, 0.75, 0.90, 0.95, 0.99 };

    // ═══════════════════════════════════ Section A: determinism (brief §21) ═══════════════════════════════

    [Fact]
    public void Determinism_ObservationSet_BuiltTwice_FromSamePipelineResult_IsBitIdentical()
    {
        AuditDataset? dataset = StructuralBreakObservationSetBuilder.Instance;
        if (dataset is null)
        {
            Assert.Skip("Yahoo provider unavailable (not a code failure).");
        }

        // Rebuild TWICE from the same already-computed BacktestSignalPipelineResult, with fresh
        // FusionEngine/FusionStateManager instances each time - no network call, no shared mutable state.
        var firstBuild = StructuralBreakObservationSetBuilder.BuildObservations(
            dataset.PipelineResult, dataset.Series.Symbol, dataset.Series.TimeFrame);
        var secondBuild = StructuralBreakObservationSetBuilder.BuildObservations(
            dataset.PipelineResult, dataset.Series.Symbol, dataset.Series.TimeFrame);

        string hash1 = StructuralBreakObservationSetBuilder.HashObservations(firstBuild);
        string hash2 = StructuralBreakObservationSetBuilder.HashObservations(secondBuild);
        string hashShared = StructuralBreakObservationSetBuilder.HashObservations(dataset.Observations);

        _output.WriteLine("=== DETERMINISM CHECK (brief Section A / §21) ===");
        _output.WriteLine($"DatasetFingerprint={dataset.Fingerprint}");
        _output.WriteLine($"ObservationCount={dataset.Observations.Count}");
        _output.WriteLine($"Hash(build#1)={hash1}");
        _output.WriteLine($"Hash(build#2)={hash2}");
        _output.WriteLine($"Hash(sharedInstance)={hashShared}");

        Assert.Equal(firstBuild.Count, secondBuild.Count);
        Assert.Equal(hash1, hash2);
        Assert.Equal(hash1, hashShared);
    }

    // ═══════════════════════════════════ Section B.1: Cusum.Confidence distribution ═══════════════════════

    [Fact]
    public void B1_CusumConfidenceDistribution_AllDetected_NotDetected_Plateaus_SaturationLag()
    {
        AuditDataset? dataset = StructuralBreakObservationSetBuilder.Instance;
        if (dataset is null)
        {
            Assert.Skip("Yahoo provider unavailable (not a code failure).");
        }

        List<ObservationRecord> validBars = dataset.Observations.Where(o => o.Cusum is { IsValid: true }).ToList();
        List<double> confAll = validBars.Select(o => o.Cusum!.Confidence).ToList();
        List<double> confDetected = validBars.Where(o => o.Cusum!.ChangeDetected).Select(o => o.Cusum!.Confidence).ToList();
        List<double> confNotDetected = validBars.Where(o => !o.Cusum!.ChangeDetected).Select(o => o.Cusum!.Confidence).ToList();

        var rows = new List<object?[]>
        {
            BuildDistributionRow("ALL_VALID", confAll),
            BuildDistributionRow("CHANGE_DETECTED_TRUE", confDetected),
            BuildDistributionRow("CHANGE_DETECTED_FALSE", confNotDetected)
        };

        // ── Plateaus: runs of exactly-equal Confidence, on the chronological subsequence of Cusum.IsValid
        // bars (non-valid bars spliced out - same explicit convention as Lot 15.6's "by regime" run-length
        // section) ──────────────────────────────────────────────────────────────────────────────────────
        List<PlateauRun<double>> plateaus = AuditMath.ComputePlateaus(confAll);
        List<double> plateauLengths = plateaus.Select(p => (double)p.Length).ToList();
        rows.Add(new object?[]
        {
            "PLATEAUS", "AllValidBars_ConfidenceRuns", plateaus.Count,
            plateauLengths.Count > 0 ? Math.Round(plateauLengths.Average(), 6) : (double?)null,
            plateauLengths.Count > 0 ? Math.Round(AuditMath.Percentile(plateauLengths.OrderBy(v => v).ToList(), 0.5), 6) : (double?)null,
            plateauLengths.Count > 0 ? plateauLengths.Max() : (double?)null,
            null, null, null, null, null, null, null, null, null, null
        });

        // ── Saturation lag: first 10 plateaus where Value==1.0, each paired with the most recent
        // ChangeDetected false->true transition at or before the plateau's start (within the same valid-bar
        // subsequence). Lag==0 means saturation happens exactly on the bar ChangeDetected becomes true;
        // Lag>0 means a delay of that many valid-bars. ──────────────────────────────────────────────────
        var saturationEvents = new List<object?[]>();
        int reported = 0;
        foreach (PlateauRun<double> plateau in plateaus)
        {
            if (reported >= 10) break;
            if (plateau.Value != 1.0) continue;

            int startIdx = plateau.StartIndex;
            int transitionIdx = FindMostRecentChangeDetectedTrueTransition(validBars, startIdx);

            ObservationRecord startBar = validBars[startIdx];
            saturationEvents.Add(new object?[]
            {
                reported,
                startBar.BarIndex,
                startBar.Timestamp,
                transitionIdx >= 0 ? validBars[transitionIdx].BarIndex : (int?)null,
                transitionIdx >= 0 ? validBars[transitionIdx].Timestamp : (DateTime?)null,
                transitionIdx >= 0 ? startIdx - transitionIdx : (int?)null,
                transitionIdx >= 0 ? "OK" : "NO_PRIOR_TRANSITION_FOUND"
            });
            reported++;
        }

        string outputDir = AuditCsv.ResolveOutputDirectory();
        var allRows = new List<object?[]>();
        allRows.AddRange(rows);
        foreach (object?[] e in saturationEvents)
            allRows.Add(new object?[] { "SATURATION_LAG", e[0], e[1], e[2], e[3], e[4], e[5], e[6], null, null, null, null, null, null, null });

        AuditCsv.WriteCsv(Path.Combine(outputDir, "StrengthDistribution.csv"),
            dataset.Fingerprint,
            new[]
            {
                "RowType", "Label", "N", "MeanOrP01", "MedianOrP05", "StdDevOrP10", "P25", "P50", "P75", "P90", "P95", "P99", "Min", "Max", "DistinctCount"
            },
            allRows);

        _output.WriteLine("=== B.1: CUSUM.CONFIDENCE DISTRIBUTION (Cusum.IsValid==true) ===");
        _output.WriteLine("Label | N | Mean | Median | StdDev | P25 | P50 | P75 | P90 | P95 | P99 | Min | Max | DistinctCount");
        foreach (object?[] r in rows.Where(r => (string)r[0]! != "PLATEAUS"))
            _output.WriteLine(string.Join(" | ", r.Select(AuditCsv.FormatCell)));
        _output.WriteLine("");
        _output.WriteLine("=== B.1: PLATEAUS (runs of exactly-equal Confidence on the valid-bar subsequence) ===");
        _output.WriteLine(string.Join(" | ", rows.Last().Select(AuditCsv.FormatCell)));
        _output.WriteLine("");
        _output.WriteLine($"=== B.1: SATURATION LAG - first {saturationEvents.Count} plateaus with Value==1.0 (Index | PlateauStartBarIndex | PlateauStartTs | ChangeDetectedTrueBarIndex | ChangeDetectedTrueTs | LagValidBars | Flag) ===");
        foreach (object?[] e in saturationEvents)
            _output.WriteLine(string.Join(" | ", e.Select(AuditCsv.FormatCell)));

        AuditCsv.AssertNoNaNOrInfinity(rows);
        Assert.True(dataset.Observations.Count > 0);
    }

    private static int FindMostRecentChangeDetectedTrueTransition(List<ObservationRecord> validBars, int atOrBeforeIndex)
    {
        for (int k = atOrBeforeIndex; k >= 0; k--)
        {
            bool cur = validBars[k].Cusum!.ChangeDetected;
            bool prev = k > 0 && validBars[k - 1].Cusum!.ChangeDetected;
            if (cur && !prev) return k;
        }
        return -1;
    }

    private static object?[] BuildDistributionRow(string label, List<double> values)
    {
        if (values.Count == 0)
            return new object?[] { "DISTRIBUTION", label, 0, null, null, null, null, null, null, null, null, null, null, null, 0 };

        List<double> sorted = values.OrderBy(v => v).ToList();
        return new object?[]
        {
            "DISTRIBUTION", label, sorted.Count,
            Math.Round(sorted.Average(), 6),
            Math.Round(AuditMath.Percentile(sorted, 0.5), 6),
            Math.Round(AuditMath.StdDev(sorted), 6),
            Math.Round(AuditMath.Percentile(sorted, 0.25), 6),
            Math.Round(AuditMath.Percentile(sorted, 0.50), 6),
            Math.Round(AuditMath.Percentile(sorted, 0.75), 6),
            Math.Round(AuditMath.Percentile(sorted, 0.90), 6),
            Math.Round(AuditMath.Percentile(sorted, 0.95), 6),
            Math.Round(AuditMath.Percentile(sorted, 0.99), 6),
            sorted.Min(),
            sorted.Max(),
            sorted.Distinct().Count()
        };
    }

    // ═══════════════════════════════════ Section B.2: pipeline trace stages ═══════════════════════════════

    [Fact]
    public void B2_PipelineTraceStages_SevenStageTable_WhereVarianceCollapses()
    {
        AuditDataset? dataset = StructuralBreakObservationSetBuilder.Instance;
        if (dataset is null)
        {
            Assert.Skip("Yahoo provider unavailable (not a code failure).");
        }

        int sbIndex = StructuralBreakObservationSetBuilder.StructuralBreakDimensionIndex;
        List<ObservationRecord> detected = dataset.Observations
            .Where(o => o.Cusum is { IsValid: true, ChangeDetected: true })
            .ToList();

        List<double> peakMagnitude = detected.Select(o => Math.Max(o.Cusum!.PositiveCusum, Math.Abs(o.Cusum!.NegativeCusum))).ToList();
        List<double> threshold = detected.Select(o => o.Cusum!.Threshold).ToList();

        List<(ObservationRecord Bar, double RawRatio)> rawRatioPairs = detected
            .Where(o => o.Cusum!.Threshold > 0.0)
            .Select(o => (o, Math.Max(o.Cusum!.PositiveCusum, Math.Abs(o.Cusum!.NegativeCusum)) / o.Cusum!.Threshold))
            .ToList();
        int thresholdNonPositiveCount = detected.Count(o => o.Cusum!.Threshold <= 0.0);
        List<double> rawRatio = rawRatioPairs.Select(p => p.RawRatio).ToList();

        List<double> confidence = detected.Select(o => o.Cusum!.Confidence).ToList();
        List<double> strength = detected.Select(o => o.Contract.Strength).ToList();
        // Lot 16 ("Strength Contract Repair"): Contract.Strength is no longer a verbatim pass-through of
        // Cusum.Confidence - it is now r/(1+r), r = peakCusum/threshold. So among detected bars the two
        // series are DECOUPLED (this count is now ~= N, was asserted == 0 pre-Lot-16). The saturating
        // Math.Clamp still lives in CusumStatistics.Compute (protected, untouched) - stage 4 below still
        // reports 100% saturation - but it no longer reaches the Fusion dimension (stages 5-7).
        int strengthNotEqualConfidenceCount = detected.Count(o => o.Contract.Strength != o.Cusum!.Confidence);
        int strengthSaturatedCount = strength.Count(v => v >= 0.9999);
        double strengthVariance = strength.Count > 1 ? AuditMath.Variance(strength) : 0.0;

        List<double> rawSb = detected.Select(o => o.RawValue[sbIndex]).ToList();
        List<double> stableSb = detected.Select(o => o.StableValue[sbIndex]).ToList();

        var rows = new List<object?[]>
        {
            BuildStageRow("1_PeakMagnitude", peakMagnitude, null),
            BuildStageRow("2_Threshold", threshold, null),
            BuildStageRow("3_RawRatio_PeakOverThreshold", rawRatio, v => v >= 1.0),
            BuildStageRow("4_Cusum_Confidence", confidence, v => v == 1.0),
            BuildStageRow("5_Contract_Strength_Lot16_rOver1plusR", strength, v => v >= 0.9999),
            BuildStageRow("6_RawValue_StructuralBreak_PreFusionState", rawSb, v => v >= 0.9999),
            BuildStageRow("7_StableValue_StructuralBreak_PostFusionState", stableSb, v => v >= 0.9999)
        };

        string outputDir = AuditCsv.ResolveOutputDirectory();
        AuditCsv.WriteCsv(Path.Combine(outputDir, "PipelineTraceStages.csv"),
            dataset.Fingerprint,
            new[] { "Stage", "N", "Min", "Max", "Mean", "Variance", "DistinctValues", "SaturationRatePct" },
            rows);

        _output.WriteLine("=== B.2: PIPELINE TRACE STAGES (bars where Cusum.IsValid && ChangeDetected==true) ===");
        _output.WriteLine($"N_DetectedValidBars={detected.Count}, ExcludedFromRawRatio_ThresholdNonPositive={thresholdNonPositiveCount}");
        _output.WriteLine($"[Lot 16] Contract.Strength != Cusum.Confidence count={strengthNotEqualConfidenceCount} (now ~=N: Strength = r/(1+r), decoupled from the saturating Clamp)");
        _output.WriteLine($"[Lot 16] Contract.Strength saturated (>=0.9999) count={strengthSaturatedCount}/{strength.Count}, variance={strengthVariance:G6}");
        _output.WriteLine("Stage | N | Min | Max | Mean | Variance | DistinctValues | SaturationRate%");
        foreach (object?[] r in rows) _output.WriteLine(string.Join(" | ", r.Select(AuditCsv.FormatCell)));

        AuditCsv.AssertNoNaNOrInfinity(rows);
        // Lot 16 outcome assertions (replacing the pre-Lot-16 Assert.Equal(0, strengthNotEqualConfidenceCount)):
        Assert.True(strengthNotEqualConfidenceCount > 0,
            "Lot 16: Contract.Strength must no longer be a verbatim copy of the saturating Cusum.Confidence.");
        Assert.True(strengthVariance > 0.0,
            "Lot 16: Contract.Strength must carry non-zero variance among detected bars (de-saturated).");
        Assert.True(strengthSaturatedCount < detected.Count,
            "Lot 16: Contract.Strength must not be saturated at ~1.0 for every detected bar.");
    }

    private static object?[] BuildStageRow(string stage, List<double> values, Func<double, bool>? isSaturated)
    {
        if (values.Count == 0)
            return new object?[] { stage, 0, null, null, null, null, 0, null };

        double? saturationRate = isSaturated is null
            ? null
            : Math.Round(100.0 * values.Count(isSaturated) / values.Count, 3);

        return new object?[]
        {
            stage, values.Count, values.Min(), values.Max(),
            Math.Round(values.Average(), 9),
            Math.Round(AuditMath.Variance(values), 9),
            values.Distinct().Count(),
            saturationRate
        };
    }
}
