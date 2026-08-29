using System;
using System.Collections.Generic;
using System.Linq;

namespace IQIAIndicator.Tests.Research.StopLossCalibration;

/// <summary>
/// Sprint 15.15. Reproduces and regression-guards the MergeOverlapping bug found during Sprint 15.14:
/// when two or more adjacent 5-point stable windows are merged, MergeOverlapping (pre-fix) only extends
/// EndKIndex/EndK - it never recomputes MeanReversionRate/ReversionRateCv/MeanFalseInvalidationRate for
/// the merged region, so those three fields kept reflecting only the FIRST 5-point sub-window in the
/// merge chain, not the full reported k-range. Window bounds (StartK/EndK/StartKIndex/EndKIndex) were
/// never affected - only those three descriptive fields.
///
/// TEST 3 below is the brief's own minimal example: three chained overlapping 5-point windows
/// ([0..4], [1..5], [2..6]) must merge into one [0..6] window whose statistics are computed over all 7
/// points - not the first window's 5.
/// </summary>
public static class StableRegionAnalyzerMergeTests
{
    public static void RunAll()
    {
        Test1_SingleWindowNoMerge_StatisticsUnchanged();
        Test2_TwoOverlappingWindows_BoundsAndStatsCorrect();
        Test3_ThreeChainedOverlappingWindows_MergeToFullRange();
        Test4_NonOverlappingWindows_StaySeparate();
        Test5_MergeDoesNotMutateSourceData();
        Test6_ReversionRateCvMatchesManualCalculation();
        Test7_FalseInvalidationRateCvMatchesManualCalculation();
        Test8_MeanNearZero_NonZeroValues_ProducesInfinity();
        Test9_AllValuesZero_ProducesCvZero();
        Test10_NoMergeNeeded_WindowUnaffectedByFix();
    }

    // ── TEST 1: single 5-point window, no adjacent window to merge with ────────────────────────────

    private static void Test1_SingleWindowNoMerge_StatisticsUnchanged()
    {
        double[] revRates = { 0.90, 0.91, 0.89, 0.90, 0.91 };
        List<CandidateAggregateResult> curve = BuildCurve(revRates, falseInvalidRates: null, startK: 1.0);

        IReadOnlyList<StableWindow> windows = StableRegionAnalyzer.FindStableWindows(curve);
        Assert(windows.Count == 1, $"Expected exactly one window. Actual={windows.Count}.");

        double expectedMean = revRates.Average();
        double expectedCv = ManualCv(revRates);
        Assert(Math.Abs(windows[0].MeanReversionRate - expectedMean) < 1e-9,
            $"Single-window mean must equal the direct average. Expected={expectedMean}, Actual={windows[0].MeanReversionRate}.");
        Assert(Math.Abs(windows[0].ReversionRateCv - expectedCv) < 1e-9,
            $"Single-window CV must equal the manual calculation. Expected={expectedCv}, Actual={windows[0].ReversionRateCv}.");
    }

    // ── TEST 2: two overlapping 5-point windows merge into one 6-point region ──────────────────────

    private static void Test2_TwoOverlappingWindows_BoundsAndStatsCorrect()
    {
        // index: 0     1     2     3     4     5
        double[] revRates = { 0.80, 0.81, 0.82, 0.83, 0.84, 0.86 };
        List<CandidateAggregateResult> curve = BuildCurve(revRates, falseInvalidRates: null, startK: 1.0);

        IReadOnlyList<StableWindow> windows = StableRegionAnalyzer.FindStableWindows(curve);
        Assert(windows.Count == 1, $"Two overlapping 5-point windows over 6 points must merge into exactly one. Actual={windows.Count}.");

        StableWindow w = windows[0];
        Assert(w.StartKIndex == 0 && w.EndKIndex == 5, $"Merged window must span the full [0,5] index range. Actual=[{w.StartKIndex},{w.EndKIndex}].");

        double expectedMean = revRates.Average();
        double expectedCv = ManualCv(revRates);
        Assert(Math.Abs(w.MeanReversionRate - expectedMean) < 1e-9,
            $"Merged window's mean must be recomputed over all 6 points, not just the first 5. Expected={expectedMean}, Actual={w.MeanReversionRate}.");
        Assert(Math.Abs(w.ReversionRateCv - expectedCv) < 1e-9,
            $"Merged window's CV must be recomputed over all 6 points. Expected={expectedCv}, Actual={w.ReversionRateCv}.");
    }

    // ── TEST 3: the brief's own minimal reproduction - three chained overlapping windows ───────────

    private static void Test3_ThreeChainedOverlappingWindows_MergeToFullRange()
    {
        // 7 points, index 0..6. Three separately-qualifying 5-point sub-windows: [0..4], [1..5], [2..6].
        double[] revRates = { 0.80, 0.81, 0.82, 0.83, 0.84, 0.86, 0.88 };
        double[] falseInvalid = { 0.30, 0.29, 0.28, 0.27, 0.26, 0.20, 0.15 };
        List<CandidateAggregateResult> curve = BuildCurve(revRates, falseInvalid, startK: 1.0);

        IReadOnlyList<StableWindow> windows = StableRegionAnalyzer.FindStableWindows(curve);
        Assert(windows.Count == 1, $"Three chained overlapping windows must merge into exactly one. Actual={windows.Count}.");

        StableWindow w = windows[0];
        Assert(w.StartKIndex == 0 && w.EndKIndex == 6, $"Merged window must span the full [0,6] index range (k=1..7), not stop at the first sub-window. Actual=[{w.StartKIndex},{w.EndKIndex}].");
        Assert(Math.Abs(w.StartK - 1.0) < 1e-9 && Math.Abs(w.EndK - 7.0) < 1e-9, $"Merged window's k bounds must be [1.0, 7.0]. Actual=[{w.StartK},{w.EndK}].");

        double expectedMean = revRates.Average();
        double expectedCv = ManualCv(revRates);
        double expectedFalseInvalid = falseInvalid.Average();

        // The bug: pre-fix, these three would instead equal the FIRST 5-point sub-window's statistics
        // (mean=0.82, cv=1.72%, falseInvalid=0.28) - the exact discrepancy Sprint 15.14 found.
        Assert(Math.Abs(w.MeanReversionRate - expectedMean) < 1e-6,
            $"MeanReversionRate must be computed over all 7 points (expected {expectedMean:F6}), not the first 5-point sub-window (which would give ~0.82). Actual={w.MeanReversionRate:F6}.");
        Assert(Math.Abs(w.ReversionRateCv - expectedCv) < 1e-6,
            $"ReversionRateCv must be computed over all 7 points (expected {expectedCv:F6}), not the first 5-point sub-window (which would give ~0.0172). Actual={w.ReversionRateCv:F6}.");
        Assert(Math.Abs(w.MeanFalseInvalidationRate - expectedFalseInvalid) < 1e-6,
            $"MeanFalseInvalidationRate must be computed over all 7 points (expected {expectedFalseInvalid:F6}), not the first 5-point sub-window (which would give 0.28). Actual={w.MeanFalseInvalidationRate:F6}.");
    }

    // ── TEST 4: non-overlapping windows must stay separate ──────────────────────────────────────────

    private static void Test4_NonOverlappingWindows_StaySeparate()
    {
        // Two flat 5-point plateaus (indices 0-4 and 6-10) separated by one volatile point (index 5)
        // that breaks every 5-point window straddling it.
        double[] revRates =
        {
            0.90, 0.90, 0.90, 0.90, 0.90, // 0-4: flat
            0.10,                          // 5: breaks any window containing it
            0.90, 0.90, 0.90, 0.90, 0.90  // 6-10: flat
        };
        List<CandidateAggregateResult> curve = BuildCurve(revRates, falseInvalidRates: null, startK: 1.0);

        IReadOnlyList<StableWindow> windows = StableRegionAnalyzer.FindStableWindows(curve);
        Assert(windows.Count == 2, $"Two plateaus separated by a breaking point must stay as two separate windows. Actual={windows.Count}.");
        Assert(windows[0].EndKIndex == 4, $"First window must end at index 4. Actual={windows[0].EndKIndex}.");
        Assert(windows[1].StartKIndex == 6, $"Second window must start at index 6. Actual={windows[1].StartKIndex}.");
    }

    // ── TEST 5: merging must not mutate the input list ──────────────────────────────────────────────

    private static void Test5_MergeDoesNotMutateSourceData()
    {
        double[] revRates = { 0.80, 0.81, 0.82, 0.83, 0.84, 0.86, 0.88 };
        List<CandidateAggregateResult> curve = BuildCurve(revRates, falseInvalidRates: null, startK: 1.0);
        List<CandidateAggregateResult> snapshot = curve.ToList();

        StableRegionAnalyzer.FindStableWindows(curve);

        Assert(curve.Count == snapshot.Count, "FindStableWindows must not add or remove rows from the source curve.");
        for (int i = 0; i < curve.Count; i++)
        {
            Assert(curve[i] == snapshot[i], $"FindStableWindows must not mutate any source row. Row {i} changed.");
        }
    }

    // ── TEST 6 / 7: manual CV cross-checks ──────────────────────────────────────────────────────────

    private static void Test6_ReversionRateCvMatchesManualCalculation()
    {
        double[] revRates = { 0.70, 0.75, 0.80, 0.85, 0.90 };
        List<CandidateAggregateResult> curve = BuildCurve(revRates, falseInvalidRates: null, startK: 1.0);
        IReadOnlyList<StableWindow> windows = StableRegionAnalyzer.FindStableWindows(curve);

        // This particular curve's CV exceeds 10% (manually: mean=0.80, stdev~0.0707, cv~8.84% - actually
        // let's not assume; assert directly against the manual formula regardless of whether it
        // qualifies as "stable", to test the CV computation itself in isolation).
        double expectedCv = ManualCv(revRates);
        if (windows.Count > 0)
        {
            Assert(Math.Abs(windows[0].ReversionRateCv - expectedCv) < 1e-9,
                $"ReversionRateCv must match the manual coefficient-of-variation formula. Expected={expectedCv}, Actual={windows[0].ReversionRateCv}.");
        }
        else
        {
            Assert(expectedCv > 0.10, $"If no window was found, the manual CV must exceed the 10% threshold. Manual CV={expectedCv}.");
        }
    }

    private static void Test7_FalseInvalidationRateCvMatchesManualCalculation()
    {
        double[] revRates = { 0.90, 0.91, 0.90, 0.91, 0.90 };
        double[] falseInvalid = { 0.20, 0.22, 0.21, 0.23, 0.20 };
        List<CandidateAggregateResult> curve = BuildCurve(revRates, falseInvalid, startK: 1.0);
        IReadOnlyList<StableWindow> windows = StableRegionAnalyzer.FindStableWindows(curve);

        Assert(windows.Count == 1, $"Expected exactly one stable window for this flat curve. Actual={windows.Count}.");
        double expectedFalseInvalidCv = ManualCv(falseInvalid);
        Assert(Math.Abs(windows[0].FalseInvalidationRateCv - expectedFalseInvalidCv) < 1e-9,
            $"FalseInvalidationRateCv must match the manual coefficient-of-variation formula. Expected={expectedFalseInvalidCv}, Actual={windows[0].FalseInvalidationRateCv}.");
    }

    // ── TEST 8 / 9: CoefficientOfVariation edge cases (unchanged behavior, not part of this fix) ────

    private static void Test8_MeanNearZero_NonZeroValues_ProducesInfinity()
    {
        // Mean very close to zero (values cancel out) but the values themselves are NOT all within
        // the function's own zero-epsilon (1e-9) - historical behavior (pre-existing, untouched by
        // this sprint's fix) must return +Infinity, not a fabricated finite CV.
        double[] revRates = { 0.50, -0.50, 0.30, -0.30, 1e-10 };
        List<CandidateAggregateResult> curve = BuildCurve(revRates, falseInvalidRates: null, startK: 1.0);
        IReadOnlyList<StableWindow> windows = StableRegionAnalyzer.FindStableWindows(curve);

        // A window can only be reported if CV<=10%; +Infinity never qualifies, so no window is expected.
        Assert(windows.Count == 0, $"A near-zero-mean, non-all-zero curve must never qualify as stable (CV=+Infinity). Actual window count={windows.Count}.");
    }

    private static void Test9_AllValuesZero_ProducesCvZero()
    {
        double[] revRates = { 0.0, 0.0, 0.0, 0.0, 0.0, 0.0 };
        List<CandidateAggregateResult> curve = BuildCurve(revRates, falseInvalidRates: null, startK: 1.0);
        IReadOnlyList<StableWindow> windows = StableRegionAnalyzer.FindStableWindows(curve);

        Assert(windows.Count == 1, $"An all-zero curve must be reported as one (degenerate) stable window - CV of an all-zero series is defined as 0. Actual={windows.Count}.");
        Assert(windows[0].ReversionRateCv == 0.0, $"CV of an all-zero series must be exactly 0. Actual={windows[0].ReversionRateCv}.");
        Assert(windows[0].MeanReversionRate == 0.0, $"Mean of an all-zero series must be exactly 0. Actual={windows[0].MeanReversionRate}.");
    }

    // ── TEST 10: fix must not change anything when no merge is needed ──────────────────────────────

    private static void Test10_NoMergeNeeded_WindowUnaffectedByFix()
    {
        // A single isolated 5-point window (identical to TEST 1, restated to make explicit that the
        // fix - which only changes MERGED-window statistics - is a no-op here).
        double[] revRates = { 0.60, 0.62, 0.61, 0.63, 0.60 };
        List<CandidateAggregateResult> curve = BuildCurve(revRates, falseInvalidRates: null, startK: 5.0);
        IReadOnlyList<StableWindow> windows = StableRegionAnalyzer.FindStableWindows(curve);

        Assert(windows.Count == 1, $"Expected exactly one window. Actual={windows.Count}.");
        Assert(windows[0].StartKIndex == 0 && windows[0].EndKIndex == 4, "No merge should occur for a single 5-point curve.");
        Assert(Math.Abs(windows[0].MeanReversionRate - revRates.Average()) < 1e-9, "Unmerged window statistics must be unaffected by the fix.");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────

    private static List<CandidateAggregateResult> BuildCurve(double[] reversionRates, double[]? falseInvalidRates, double startK)
    {
        var curve = new List<CandidateAggregateResult>();
        for (int i = 0; i < reversionRates.Length; i++)
        {
            double falseInvalid = falseInvalidRates is not null ? falseInvalidRates[i] : double.NaN;
            curve.Add(new CandidateAggregateResult(
                "A1", "SyntheticTestCurve", "TRAIN", 1m, startK + i, null,
                TotalEntries: 100, ApplicableEntries: 100, DegenerateEntries: 0,
                StopHits: 0, StoppedOut: 0, Reverted: 0, Undetermined: 0,
                StopHitRate: 0.0, ReversionRate: reversionRates[i], UndeterminedRate: 0.0, StopHitBeforeEquilibriumRate: 0.0,
                FalseInvalidationRate: falseInvalid, TrueInvalidationRate: double.NaN,
                MaeMean: 0.0, MaeMedian: 0.0, MaeP75: 0.0, MaeP90: 0.0,
                MfeMean: 0.0, MfeMedian: 0.0, MfeP75: 0.0, MfeP90: 0.0,
                MedianTimeToEquilibrium: null, MeanStopDistance: 0.0, MeanStopRatio: startK + i));
        }

        return curve;
    }

    private static double ManualCv(double[] values)
    {
        double mean = values.Average();
        if (Math.Abs(mean) < 1e-9)
        {
            return values.All(v => Math.Abs(v) < 1e-9) ? 0.0 : double.PositiveInfinity;
        }

        double variance = values.Sum(v => (v - mean) * (v - mean)) / values.Length;
        return Math.Sqrt(variance) / Math.Abs(mean);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
