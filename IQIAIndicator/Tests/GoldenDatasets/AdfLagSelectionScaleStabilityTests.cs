using IQIAIndicator.Engine.Regime.Evidence.ADF;

namespace IQIAIndicator.Tests.GoldenDatasets;

/// <summary>
/// Sprint 15.2 — ADF Lag Selection Numerical Stability.
///
/// Fixes the scale-sensitivity finding from Sprint 15.1 (documented, at the time only as an
/// unexplained near-tie, in AdfKpssRobustnessTests.AssertAdfScaleStabilityWithinSafeRange):
/// AdfGoldenDataset.RandomWalk(100) selected lag=0 at scale=1/0.1 but lag=12 at scale=10/100.
///
/// Root cause (proven, not assumed - see AdfRegression02_AicCandidateDump_DocumentsFixedRootCause
/// below for the direct numeric proof): AdfStatistics.SelectLag compared AIC = nObs(p)*ln(SSR(p)/nObs(p))
/// + 2k across candidates p whose effective sample size nObs(p) = n-1-p is DIFFERENT for each p (a
/// larger lag consumes more leading observations). Under y' = c*y, OLS gives SSR'(p) = c^2*SSR(p)
/// exactly, so AIC'(p) = AIC(p) + nObs(p)*ln(c^2). Because nObs(p) varies with p, this additive shift
/// is different for each candidate, so it can (and, for this dataset, does) flip the argmin - a real
/// mathematical property of the comparison, not decimal/double precision loss.
///
/// Fix (AdfStatistics.SelectLag only - AdfRegression.cs, the OLS formula, critical values and
/// p-values are untouched): compare AIC across candidates using a common, p-independent sample size,
/// exactly like statsmodels' adfuller(autolag='AIC') does. This makes the shift nObs*ln(c^2) an
/// identical additive constant across every candidate, so it cancels exactly in the argmin for any
/// c > 0 - not an approximation or a forced tie-break, an exact algebraic consequence. The final
/// regression for the selected lag (AdfEvidence/AdfValidation) still uses the full, untrimmed series
/// exactly as before, so Statistic/PValue/SampleSize for a given lag are bit-for-bit unchanged.
/// </summary>
public static class AdfLagSelectionScaleStabilityTests
{
    public static void RunAll()
    {
        AdfScale01_RandomWalk_SameLagAcrossFourScales();
        AdfScale02_MultipleSeriesTypes_SameLagAcrossScales();
        AdfScale03_WideScaleSweep_SameLag();
        AdfScale04_ConstantShift_LagAndStatisticUnaffected();
        AdfScale05_ClearAicWinner_RemainsWinnerAfterFix();
        AdfRegression01_CanonicalDatasets_LagAndStatisticUnchanged();
        AdfRegression02_AicCandidateDump_DocumentsFixedRootCause();
        AdfRobustness01_ValidationPathExtremeValue_NoExceptionInvalid();
        AdfPerformance01_LagSelectionCost_RemainsReasonable();
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // ADF-SCALE-01..04 (sprint section 9)
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    private static void AdfScale01_RandomWalk_SameLagAcrossFourScales()
    {
        decimal[] baseSeries = AdfGoldenDataset.RandomWalk(100);
        decimal[] scales = [0.1m, 1m, 10m, 100m];

        AdfResult? reference = null;
        foreach (decimal scale in scales)
        {
            AdfResult result = AdfValidation.RunOnSeries(Scaled(baseSeries, scale));
            Assert(result.IsValid, $"ADF-SCALE-01: scale={scale} must remain within the safe, computable range.");

            if (reference is null)
            {
                reference = result;
                continue;
            }

            Assert(result.LagUsed == reference.LagUsed,
                $"ADF-SCALE-01: scale={scale} must select the same lag as scale=0.1 now that AIC candidates are compared on a fixed sample size (Sprint 15.2). Expected={reference.LagUsed}, Actual={result.LagUsed}.");
            AssertDecimalClose(reference.Statistic, result.Statistic,
                $"ADF-SCALE-01: t-statistic must match at scale={scale} given an identical selected lag.");
        }
    }

    private static void AdfScale02_MultipleSeriesTypes_SameLagAcrossScales()
    {
        (string Name, decimal[] Series)[] cases =
        [
            ("WhiteNoise", AdfGoldenDataset.WhiteNoise(100)),
            ("RandomWalk", AdfGoldenDataset.RandomWalk(100)),
            ("AR1_phi0.5", AdfGoldenDataset.Ar1Moderate(100)),
            ("MeanReversion", AdfGoldenDataset.MeanReversion(100)),
            ("Trending", SyntheticSeriesCatalog.Trending(120)),
        ];
        decimal[] scales = [0.1m, 1m, 10m, 100m];

        foreach ((string name, decimal[] series) in cases)
        {
            int? referenceLag = null;
            foreach (decimal scale in scales)
            {
                AdfResult result = AdfValidation.RunOnSeries(Scaled(series, scale));
                Assert(result.IsValid, $"ADF-SCALE-02/{name}: scale={scale} must remain within the safe, computable range.");
                referenceLag ??= result.LagUsed;
                Assert(result.LagUsed == referenceLag,
                    $"ADF-SCALE-02/{name}: scale={scale} selected lag={result.LagUsed}, expected {referenceLag} (same as scale=0.1).");
            }
        }
    }

    private static void AdfScale03_WideScaleSweep_SameLag()
    {
        decimal[] baseSeries = AdfGoldenDataset.RandomWalk(100);
        decimal[] scales = [0.01m, 0.1m, 1m, 10m, 100m, 1000m];

        int? referenceLag = null;
        foreach (decimal scale in scales)
        {
            AdfResult result = AdfValidation.RunOnSeries(Scaled(baseSeries, scale));
            Assert(result.IsValid, $"ADF-SCALE-03: scale={scale} must remain within the safe, computable range (well below AdfRegression.SafeMagnitudeBound).");
            referenceLag ??= result.LagUsed;
            Assert(result.LagUsed == referenceLag,
                $"ADF-SCALE-03: scale={scale} selected lag={result.LagUsed}, expected {referenceLag} (same as scale=0.01) across a 0.01x-1000x sweep.");
        }
    }

    /// <summary>
    /// ADF-SCALE-04: a pure additive shift (X + constant), not a rescaling. Mathematically applicable
    /// here (unlike an arbitrary invented invariance): Delta(y+a) = Delta(y) exactly, so the shift
    /// only ever touches the y_(t-1) level regressor's column, which is already spanned by the
    /// existing intercept column (an elementary column operation). The fitted subspace - and hence
    /// SSR and AIC for every lag candidate - is unaffected by construction, independent of this
    /// sprint's fix.
    /// </summary>
    private static void AdfScale04_ConstantShift_LagAndStatisticUnaffected()
    {
        decimal[] baseSeries = AdfGoldenDataset.RandomWalk(100);
        decimal[] shifts = [-500m, 0m, 500m, 10000m];

        AdfResult? reference = null;
        foreach (decimal shift in shifts)
        {
            var shifted = new decimal[baseSeries.Length];
            for (int i = 0; i < baseSeries.Length; i++)
                shifted[i] = baseSeries[i] + shift;

            AdfResult result = AdfValidation.RunOnSeries(shifted);
            Assert(result.IsValid, $"ADF-SCALE-04: shift={shift} must remain valid.");

            reference ??= result;
            Assert(result.LagUsed == reference.LagUsed,
                $"ADF-SCALE-04: shift={shift} selected lag={result.LagUsed}, expected {reference.LagUsed}.");
            AssertDecimalClose(reference.Statistic, result.Statistic,
                $"ADF-SCALE-04: t-statistic must be invariant to an additive shift at shift={shift}.");
        }
    }

    /// <summary>
    /// Section 10 - protection against false positives: the fix must not turn AIC selection into an
    /// arbitrary always-pick-lag-0 rule. DeterministicTrend(100) has a genuine, clear AIC winner at
    /// lag=2 (confirmed unchanged at scale=1 in AdfRegression01 below) - it must keep winning across
    /// scales, not collapse to lag=0 just because most other canonical series happen to prefer lag=0.
    /// </summary>
    private static void AdfScale05_ClearAicWinner_RemainsWinnerAfterFix()
    {
        decimal[] baseSeries = AdfGoldenDataset.DeterministicTrend(100);
        decimal[] scales = [0.1m, 1m, 10m, 100m];

        foreach (decimal scale in scales)
        {
            AdfResult result = AdfValidation.RunOnSeries(Scaled(baseSeries, scale));
            Assert(result.IsValid, $"ADF-SCALE-05: scale={scale} must remain valid.");
            Assert(result.LagUsed == 2,
                $"ADF-SCALE-05: DeterministicTrend must keep selecting its genuine AIC-clear winner lag=2 at scale={scale}, proving Sprint 15.2's fix does not suppress legitimate non-zero lag choices. Actual={result.LagUsed}.");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // Non-regression (sprint section 11) - reuses the exact values captured and verified in
    // AdfKpssRobustnessTests.AssertNominalResultsExactlyUnchangedAfterRobustnessFix (Sprint 15.1),
    // which remain the ground truth: the selected lag for every canonical dataset at scale=1 is
    // unchanged by Sprint 15.2 (verified directly against AdfStatistics.SelectLag before this fix
    // was implemented - see the sprint report), so the full-series regression re-run for that lag
    // must still reproduce the same Statistic bit-for-bit.
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    private static void AdfRegression01_CanonicalDatasets_LagAndStatisticUnchanged()
    {
        (string Name, decimal[] Series, decimal TStat, int Lag)[] cases =
        [
            ("WhiteNoise", AdfGoldenDataset.WhiteNoise(100), -9.665324089891304336828625345m, 0),
            ("RandomWalk", AdfGoldenDataset.RandomWalk(100), -1.4594013671293903652063280275m, 0),
            ("AR1_phi0.5", AdfGoldenDataset.Ar1Moderate(100), -5.2690355339831348530801709211m, 0),
            ("AR1_phi0.95", AdfGoldenDataset.Ar1NearUnitRoot(100), -1.6236531518466523055552884447m, 0),
            ("DeterministicTrend", AdfGoldenDataset.DeterministicTrend(100), -3.9703782065126444152661075879m, 2),
            ("MeanReversion", AdfGoldenDataset.MeanReversion(100), -5.2690355339831348530801709211m, 0),
        ];

        foreach ((string name, decimal[] series, decimal tStat, int lag) in cases)
        {
            AdfResult result = AdfValidation.RunOnSeries(series);
            Assert(result.IsValid, $"ADF-REGRESSION-01/{name}: must remain valid after Sprint 15.2.");
            Assert(result.LagUsed == lag, $"ADF-REGRESSION-01/{name}: LagUsed must be unchanged by the Sprint 15.2 fix. Expected={lag}, Actual={result.LagUsed}.");
            AssertDecimalClose(tStat, result.Statistic, $"ADF-REGRESSION-01/{name}: Statistic must be unchanged by the Sprint 15.2 fix.");
        }
    }

    /// <summary>
    /// Section 13 - direct numeric proof of the root cause and of the fix, using the exact
    /// RandomWalk(100)/scale=1 vs scale=10 pair that exposed the Sprint 15.1 finding. Reproduces the
    /// OLD (pre-15.2) comparison directly via AdfRegression.TryCompute on the natural, p-dependent
    /// sample, to document the actual old winner at each scale - then verifies AdfStatistics.SelectLag
    /// (post-fix) is immune.
    /// </summary>
    private static void AdfRegression02_AicCandidateDump_DocumentsFixedRootCause()
    {
        decimal[] baseSeries = AdfGoldenDataset.RandomWalk(100);
        const int n = 100;
        int maxLag = AdfStatistics.ComputeMaxLag(n);
        Assert(maxLag == 12, $"ADF-REGRESSION-02: expected maxLag=12 for n=100 (Schwert/conservative bound - unchanged by Sprint 15.2). Actual={maxLag}.");

        decimal oldAicScale1Lag0 = ComputeRawAic(baseSeries, n, 0);
        decimal oldAicScale1Lag12 = ComputeRawAic(baseSeries, n, maxLag);

        decimal[] scaled10 = Scaled(baseSeries, 10m);
        decimal oldAicScale10Lag0 = ComputeRawAic(scaled10, n, 0);
        decimal oldAicScale10Lag12 = ComputeRawAic(scaled10, n, maxLag);

        Assert(oldAicScale1Lag0 < oldAicScale1Lag12,
            "ADF-REGRESSION-02: at scale=1, the pre-15.2 raw-AIC comparison (natural, p-dependent sample) must prefer lag=0 over lag=12 - the actual old winner, reproduced directly.");
        Assert(oldAicScale10Lag12 < oldAicScale10Lag0,
            "ADF-REGRESSION-02: at scale=10, the pre-15.2 raw-AIC comparison must prefer lag=12 over lag=0 - documents the actual old bug (the ranking flips purely from rescaling the series, with no change in the underlying data-generating process).");

        decimal shiftLag0 = oldAicScale10Lag0 - oldAicScale1Lag0;
        decimal shiftLag12 = oldAicScale10Lag12 - oldAicScale1Lag12;
        Assert(shiftLag0 != shiftLag12,
            "ADF-REGRESSION-02: the two candidates' AIC shift under a 10x rescale must differ - proof that the pre-fix instability was a genuine per-lag effective-sample-size artifact (nObs(0)=99 vs nObs(12)=87), not floating-point noise (equal shifts would mean the ranking could never have flipped).");

        int newLagScale1 = AdfStatistics.SelectLag(baseSeries, n);
        int newLagScale10 = AdfStatistics.SelectLag(scaled10, n);
        Assert(newLagScale1 == newLagScale10,
            $"ADF-REGRESSION-02: post-15.2 SelectLag (fixed-sample AIC comparison) must choose the same lag at scale=1 and scale=10. Actual: {newLagScale1} vs {newLagScale10}.");
    }

    private static decimal ComputeRawAic(decimal[] y, int n, int p)
    {
        bool ok = AdfRegression.TryCompute(y, n, p, out _, out decimal aic, out _);
        Assert(ok, $"ADF-REGRESSION-02: TryCompute must succeed for p={p} on this well-formed dataset.");
        return aic;
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // Robustness (sprint section 12) - Sprint 15.1's SCI15-01 protections must remain intact even
    // though Sprint 15.2 makes SelectLag scan a DIFFERENT trimmed sub-array per lag candidate.
    // AdfValidation.RunOnSeries (unlike AdfEvidence.Compute) has no upfront full-array magnitude
    // guard, so this is the path most exposed to a per-candidate-window edge case.
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    private static void AdfRobustness01_ValidationPathExtremeValue_NoExceptionInvalid()
    {
        decimal[] series = SyntheticSeriesCatalog.WhiteNoise(100);
        series[5] = decimal.MaxValue; // Near the front: excluded from some candidates' trimmed window (small p, large trim), included in others (large p, small trim).

        Exception? exception = Record(() => AdfValidation.RunOnSeries(series));
        Assert(exception is null, $"ADF-ROBUSTNESS-01: must never throw regardless of which per-lag trimmed window includes the extreme value. Observed: {exception?.GetType().Name ?? "none"}.");

        AdfResult result = AdfValidation.RunOnSeries(series);
        Assert(!result.IsValid, "ADF-ROBUSTNESS-01: an out-of-range-magnitude value anywhere in the series must still yield an Invalid result end-to-end (the final full-series regression for the selected lag re-checks magnitude), never a silently-wrong Valid result.");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // Performance (sprint section 14) - measure only, soft ceiling, same style as
    // AdfKpssRobustnessTests.MeasureAndCompareAdfKpssPerformance.
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    private static void AdfPerformance01_LagSelectionCost_RemainsReasonable()
    {
        int[] windows = [60, 100, 128];
        const int iterations = 50;

        foreach (int w in windows)
        {
            decimal[] series = SyntheticSeriesCatalog.RandomWalk(w);
            AdfValidation.RunOnSeries(series); // JIT warm-up

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++)
                AdfValidation.RunOnSeries(series);
            stopwatch.Stop();

            double avgMs = stopwatch.Elapsed.TotalMilliseconds / iterations;

            // Not a hard SLA - Sprint 15.2 adds one O(n) array copy per lag candidate (at most
            // maxLag+1, a few dozen elements), negligible next to the O(nObs*k^2) regression it
            // precedes. This soft ceiling only catches a gross regression.
            Assert(avgMs < 100.0, $"ADF-PERF-01: lag selection at {w} bars must not regress to pathological cost after Sprint 15.2 (observed {avgMs:F4} ms/call).");
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────

    private static decimal[] Scaled(decimal[] series, decimal scale)
    {
        var scaled = new decimal[series.Length];
        for (int i = 0; i < series.Length; i++)
            scaled[i] = series[i] * scale;
        return scaled;
    }

    private static Exception? Record(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static void AssertDecimalClose(decimal expected, decimal actual, string message)
    {
        if (Math.Abs(expected - actual) > 0.0000000001m)
            throw new InvalidOperationException($"{message}: Expected={expected}, Actual={actual}.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
