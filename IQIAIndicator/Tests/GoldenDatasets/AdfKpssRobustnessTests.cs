using IQIAIndicator.Engine.Regime.Core;
using IQIAIndicator.Engine.Regime.Evidence;
using IQIAIndicator.Engine.Regime.Evidence.ADF;
using IQIAIndicator.Engine.Regime.Evidence.KPSS;

namespace IQIAIndicator.Tests.GoldenDatasets;

/// <summary>
/// Sprint 15.1 — Scientific Robustness &amp; Extreme-Value Handling.
///
/// Fixes SCI15-01 (ADF) and SCI15-02 (KPSS): both models previously threw an uncaught
/// System.OverflowException on an extreme-magnitude decimal input (AdfRegression.TryCompute's X'X
/// accumulation, KpssLongRunVariance.Compute's residual-squaring accumulation). The fix is purely
/// numeric-robustness: an upfront magnitude guard (AdfRegression.SafeMagnitudeBound /
/// KpssRegression.SafeMagnitudeBound, both 1e12 - see the doc comments on those constants for the
/// full derivation) plus a try/catch(OverflowException) backstop at every call site reachable from
/// both AdfEvidence/KpssEvidence (production) and AdfValidation.RunOnSeries/KpssValidation.RunOnSeries
/// (the pre-existing test/validation harness, which calls the regression/variance code directly,
/// bypassing the Evidence layer). No ADF/KPSS formula, lag-selection rule, critical value, or
/// stationarity threshold was touched - see AdfKpssRegressionAnalysisTests below (Part 10) for the
/// full before/after numeric proof.
/// </summary>
public static class AdfKpssRobustnessTests
{
    private const int WindowSize = 60;
    private const int MinimumSampleSize = 30;

    public static void RunAll()
    {
        // ── Part 10: the regression analysis is the most important test in this file ──
        AssertNominalResultsExactlyUnchangedAfterRobustnessFix();

        // ── ADF-01..09 ──
        Adf01_NormalSeries_ComputesNormally();
        Adf02_StationarySeries_StillDetectsStationarity();
        Adf03_RandomWalk_BehaviorConserved();
        Adf04_ExtremeValueThatPreviouslyOverflowed_NoExceptionInvalidWithExplanation();
        Adf05_MultipleExtremeValues_NoException();
        Adf06_SingleIsolatedExtremeValue_NoException();
        Adf07_NearBoundaryValue_StillComputesNormally();
        Adf08_ConstantSeries_BehaviorConserved();
        Adf09_ShortSeries_BehaviorConserved();

        // ── KPSS-01..09 ──
        Kpss01_NormalSeries_ComputesNormally();
        Kpss02_StationarySeries_StillFailsToReject();
        Kpss03_RandomWalk_BehaviorConserved();
        Kpss04_ExtremeValueThatPreviouslyOverflowed_NoExceptionInvalidWithExplanation();
        Kpss05_MultipleExtremeValues_NoException();
        Kpss06_SingleIsolatedExtremeValue_NoException();
        Kpss07_NearBoundaryValue_StillComputesNormally();
        Kpss08_ConstantSeries_BehaviorConserved();
        Kpss09_ShortSeries_BehaviorConserved();

        // ── Part 11: numeric scale stability ──
        AssertAdfScaleStabilityWithinSafeRange();
        AssertKpssScaleStabilityWithinSafeRange();

        // ── Part 13: lightweight before/after performance check ──
        MeasureAndCompareAdfKpssPerformance();
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // Part 10 — Regression analysis. Values captured from the ACTUAL pre-fix build (documented in
    // this sprint's report Part 6/baseline), re-verified bit-for-bit against the post-fix build
    // before being checked in here. Reuses AdfGoldenDataset/KpssGoldenDataset's own canonical 6
    // series (rule: do not invent new arbitrary references) at N=100, the size those datasets
    // default to and the size AdfValidation/KpssValidation's own RunAllReport() already uses.
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    private sealed record ExpectedAdf(string Name, decimal[] Series, decimal TStat, decimal PValue, int Lag, bool Stationary);
    private sealed record ExpectedKpss(string Name, decimal[] Series, decimal Stat, decimal PValue, int Bandwidth, bool Stationary);

    private static void AssertNominalResultsExactlyUnchangedAfterRobustnessFix()
    {
        ExpectedAdf[] adfCases =
        [
            new("WhiteNoise", AdfGoldenDataset.WhiteNoise(100), -9.665324089891304336828625345m, 0.01m, 0, true),
            new("RandomWalk", AdfGoldenDataset.RandomWalk(100), -1.4594013671293903652063280275m, 0.413610992156247757132582957m, 0, false),
            new("AR1_phi0.5", AdfGoldenDataset.Ar1Moderate(100), -5.2690355339831348530801709211m, 0.01m, 0, true),
            new("AR1_phi0.95", AdfGoldenDataset.Ar1NearUnitRoot(100), -1.6236531518466523055552884447m, 0.3677496928027906786329095126m, 0, false),
            new("DeterministicTrend", AdfGoldenDataset.DeterministicTrend(100), -3.9703782065126444152661075879m, 0.01m, 2, true),
            new("MeanReversion", AdfGoldenDataset.MeanReversion(100), -5.2690355339831348530801709211m, 0.01m, 0, true),
        ];

        foreach (ExpectedAdf expected in adfCases)
        {
            AdfResult actual = AdfValidation.RunOnSeries(expected.Series);
            Assert(actual.IsValid, $"ADF/{expected.Name}: must remain valid after the robustness fix.");
            AssertDecimalClose(expected.TStat, actual.Statistic, $"ADF/{expected.Name} Statistic");
            AssertDecimalClose(expected.PValue, actual.PValue, $"ADF/{expected.Name} PValue");
            Assert(actual.LagUsed == expected.Lag, $"ADF/{expected.Name}: LagUsed must be unchanged. Expected={expected.Lag}, Actual={actual.LagUsed}.");
            Assert(actual.IsStationary == expected.Stationary, $"ADF/{expected.Name}: IsStationary decision must be unchanged. Expected={expected.Stationary}, Actual={actual.IsStationary}.");
        }

        ExpectedKpss[] kpssCases =
        [
            new("WhiteNoise", KpssGoldenDataset.WhiteNoise(100), 0.1244968050133186638454078199m, 0.6706854280638224471976571766m, 12, true),
            new("RandomWalk", KpssGoldenDataset.RandomWalk(100), 0.3766239410243669023557391049m, 0.087231059903290128294940041m, 12, true),
            new("AR1_phi0.5", KpssGoldenDataset.Ar1Moderate(100), 0.1242740383877171283637156845m, 0.6712567891496592384907580427m, 12, true),
            new("AR1_phi0.95", KpssGoldenDataset.Ar1NearUnitRoot(100), 0.1422452223892944244440204506m, 0.6251635506441728018582760777m, 12, true),
            new("DeterministicTrend", KpssGoldenDataset.DeterministicTrend(100), 0.5273114733136589940322217482m, 0.0355154339383650914341842909m, 12, false),
            new("MeanReversion", KpssGoldenDataset.MeanReversion(100), 0.1242740383877171283637156845m, 0.6712567891496592384907580427m, 12, true),
        ];

        foreach (ExpectedKpss expected in kpssCases)
        {
            KpssResult actual = KpssValidation.RunOnSeries(expected.Series);
            Assert(actual.IsValid, $"KPSS/{expected.Name}: must remain valid after the robustness fix.");
            AssertDecimalClose(expected.Stat, actual.Statistic, $"KPSS/{expected.Name} Statistic");
            AssertDecimalClose(expected.PValue, actual.PValue, $"KPSS/{expected.Name} PValue");
            Assert(actual.Bandwidth == expected.Bandwidth, $"KPSS/{expected.Name}: Bandwidth must be unchanged. Expected={expected.Bandwidth}, Actual={actual.Bandwidth}.");
            Assert(actual.IsStationary == expected.Stationary, $"KPSS/{expected.Name}: IsStationary decision must be unchanged. Expected={expected.Stationary}, Actual={actual.IsStationary}.");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // ADF-01..09
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    private static void Adf01_NormalSeries_ComputesNormally()
    {
        AdfResult result = RunAdf(SyntheticSeriesCatalog.WhiteNoise(120));
        Assert(result.IsValid, "ADF-01: a normal, well-formed series must compute normally.");
        Assert(double.IsFinite((double)result.Statistic), "ADF-01: Statistic must be finite.");
    }

    private static void Adf02_StationarySeries_StillDetectsStationarity()
    {
        AdfResult result = RunAdf(SyntheticSeriesCatalog.MeanRevertingOu(200));
        Assert(result.IsValid && result.IsStationary, "ADF-02: a strongly mean-reverting series must still be detected as stationary after the fix.");
    }

    private static void Adf03_RandomWalk_BehaviorConserved()
    {
        AdfResult result = RunAdf(AdfGoldenDataset.RandomWalk(100));
        Assert(result.IsValid && !result.IsStationary, "ADF-03: a genuine random walk must still fail to reject the unit-root null after the fix.");
    }

    private static void Adf04_ExtremeValueThatPreviouslyOverflowed_NoExceptionInvalidWithExplanation()
    {
        decimal[] series = SyntheticSeriesCatalog.WhiteNoise(60);
        series[30] = decimal.MaxValue; // The exact value that reproducibly threw OverflowException before this sprint's fix.
        var exception = Record(() => RunAdf(series));
        Assert(exception is null, $"ADF-04: must never throw on the exact input that previously crashed. Observed: {exception?.GetType().Name ?? "none"}.");

        AdfResult result = RunAdf(series);
        Assert(!result.IsValid, "ADF-04: Success must be false for an out-of-range-magnitude series.");
        Assert(!string.IsNullOrWhiteSpace(result.Explanation), "ADF-04: Explanation must be present.");
        Assert(result.Explanation.Contains("hors limites", StringComparison.OrdinalIgnoreCase), "ADF-04: Explanation must name the real cause (out-of-range magnitude), not a generic failure message.");
    }

    private static void Adf05_MultipleExtremeValues_NoException()
    {
        decimal[] series = SyntheticSeriesCatalog.WhiteNoise(60);
        series[10] = decimal.MaxValue;
        series[30] = -decimal.MaxValue;
        series[50] = decimal.MaxValue / 2m;
        var exception = Record(() => RunAdf(series));
        Assert(exception is null, $"ADF-05: must never throw with multiple extreme values present. Observed: {exception?.GetType().Name ?? "none"}.");
        Assert(!RunAdf(series).IsValid, "ADF-05: multiple out-of-range values must still be reported Invalid.");
    }

    private static void Adf06_SingleIsolatedExtremeValue_NoException()
    {
        decimal[] series = SyntheticSeriesCatalog.MeanRevertingOu(200);
        series[100] = decimal.MaxValue;
        var exception = Record(() => RunAdf(series));
        Assert(exception is null, $"ADF-06: a single isolated extreme value amid otherwise well-behaved data must never throw. Observed: {exception?.GetType().Name ?? "none"}.");
        Assert(!RunAdf(series).IsValid, "ADF-06: the isolated extreme value must still be caught and reported Invalid.");
    }

    /// <summary>Category B (Part 4): a value that is large but well within the safe, mathematically representable range must compute NORMALLY, not be rejected - the guard must not be over-broad.</summary>
    private static void Adf07_NearBoundaryValue_StillComputesNormally()
    {
        decimal[] series = SyntheticSeriesCatalog.RandomWalk(60);
        for (int i = 0; i < series.Length; i++)
            series[i] *= 1_000_000m; // ~1e8 scale, four orders of magnitude below SafeMagnitudeBound (1e12).
        AdfResult result = RunAdf(series);
        Assert(result.IsValid, "ADF-07: a large-but-safely-representable series must compute normally, not be rejected by the magnitude guard.");
    }

    private static void Adf08_ConstantSeries_BehaviorConserved()
    {
        AdfResult result = RunAdf(SyntheticSeriesCatalog.Constant(60));
        Assert(!result.IsValid, "ADF-08: a constant series must still be rejected (singular regression), exactly as before this sprint's fix.");
    }

    private static void Adf09_ShortSeries_BehaviorConserved()
    {
        AdfResult result = RunAdf(SyntheticSeriesCatalog.WhiteNoise(10));
        Assert(!result.IsValid, "ADF-09: a series shorter than MinimumSampleSize must still be rejected as warmup, exactly as before this sprint's fix.");
        Assert(result.Explanation.Contains("Warmup", StringComparison.OrdinalIgnoreCase), "ADF-09: warmup rejection must remain explicit.");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // KPSS-01..09
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    private static void Kpss01_NormalSeries_ComputesNormally()
    {
        KpssResult result = RunKpss(SyntheticSeriesCatalog.WhiteNoise(120));
        Assert(result.IsValid, "KPSS-01: a normal, well-formed series must compute normally.");
        Assert(double.IsFinite((double)result.Statistic), "KPSS-01: Statistic must be finite.");
    }

    private static void Kpss02_StationarySeries_StillFailsToReject()
    {
        KpssResult result = RunKpss(SyntheticSeriesCatalog.MeanRevertingOu(200));
        Assert(result.IsValid && result.IsStationary, "KPSS-02: a strongly mean-reverting series must still fail to reject stationarity after the fix.");
    }

    private static void Kpss03_RandomWalk_BehaviorConserved()
    {
        KpssResult result = RunKpss(SyntheticSeriesCatalog.RandomWalk(500));
        Assert(result.IsValid && !result.IsStationary, "KPSS-03: a genuine 500-bar random walk must still reject stationarity after the fix.");
    }

    private static void Kpss04_ExtremeValueThatPreviouslyOverflowed_NoExceptionInvalidWithExplanation()
    {
        decimal[] series = SyntheticSeriesCatalog.WhiteNoise(60);
        series[30] = decimal.MaxValue; // The exact value that reproducibly threw OverflowException before this sprint's fix.
        var exception = Record(() => RunKpss(series));
        Assert(exception is null, $"KPSS-04: must never throw on the exact input that previously crashed. Observed: {exception?.GetType().Name ?? "none"}.");

        KpssResult result = RunKpss(series);
        Assert(!result.IsValid, "KPSS-04: Success must be false for an out-of-range-magnitude series.");
        Assert(!string.IsNullOrWhiteSpace(result.Explanation), "KPSS-04: Explanation must be present.");
        Assert(result.Explanation.Contains("hors limites", StringComparison.OrdinalIgnoreCase), "KPSS-04: Explanation must name the real cause (out-of-range magnitude), not a generic failure message.");
    }

    private static void Kpss05_MultipleExtremeValues_NoException()
    {
        decimal[] series = SyntheticSeriesCatalog.WhiteNoise(60);
        series[10] = decimal.MaxValue;
        series[30] = -decimal.MaxValue;
        series[50] = decimal.MaxValue / 2m;
        var exception = Record(() => RunKpss(series));
        Assert(exception is null, $"KPSS-05: must never throw with multiple extreme values present. Observed: {exception?.GetType().Name ?? "none"}.");
        Assert(!RunKpss(series).IsValid, "KPSS-05: multiple out-of-range values must still be reported Invalid.");
    }

    private static void Kpss06_SingleIsolatedExtremeValue_NoException()
    {
        decimal[] series = SyntheticSeriesCatalog.MeanRevertingOu(200);
        series[100] = decimal.MaxValue;
        var exception = Record(() => RunKpss(series));
        Assert(exception is null, $"KPSS-06: a single isolated extreme value amid otherwise well-behaved data must never throw. Observed: {exception?.GetType().Name ?? "none"}.");
        Assert(!RunKpss(series).IsValid, "KPSS-06: the isolated extreme value must still be caught and reported Invalid.");
    }

    private static void Kpss07_NearBoundaryValue_StillComputesNormally()
    {
        decimal[] series = SyntheticSeriesCatalog.RandomWalk(60);
        for (int i = 0; i < series.Length; i++)
            series[i] *= 1_000_000m;
        KpssResult result = RunKpss(series);
        Assert(result.IsValid, "KPSS-07: a large-but-safely-representable series must compute normally, not be rejected by the magnitude guard.");
    }

    private static void Kpss08_ConstantSeries_BehaviorConserved()
    {
        KpssResult result = RunKpss(SyntheticSeriesCatalog.Constant(60));
        Assert(!result.IsValid, "KPSS-08: a constant series must still be rejected (zero long-run variance), exactly as before this sprint's fix.");
    }

    private static void Kpss09_ShortSeries_BehaviorConserved()
    {
        KpssResult result = RunKpss(SyntheticSeriesCatalog.WhiteNoise(10));
        Assert(!result.IsValid, "KPSS-09: a series shorter than MinimumSampleSize must still be rejected as warmup, exactly as before this sprint's fix.");
        Assert(result.Explanation.Contains("Warmup", StringComparison.OrdinalIgnoreCase), "KPSS-09: warmup rejection must remain explicit.");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // Part 11 — Numeric scale stability. Documents the property actually expected rather than
    // imposing an unjustified invariance: ADF's t-statistic (beta-hat / SE(beta-hat)) is a ratio of
    // two quantities that both scale linearly with the series' level under an additive random walk,
    // so it IS expected to be scale-invariant for X/10X/100X - verified directly, not assumed.
    // 0.1X is also checked; it remains comfortably within the safe range so no rejection is expected.
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// FINDING (Part 11, documented per the sprint's own explicit instruction not to impose
    /// invariance the implementation doesn't actually guarantee - discovered empirically, not
    /// assumed): the naive hypothesis "t-statistic is scale-invariant for X/10X/100X/0.1X" is WRONG
    /// as originally stated. Measured directly: scale=1 selects lag=0 (t=-1.45940136712939...),
    /// scale=0.1 ALSO selects lag=0 and reproduces the exact same t-statistic bit-for-bit; but
    /// scale=10 and scale=100 both select lag=12 instead (t=-2.14511953064405...), matching each
    /// other to 25+ significant digits (residual difference attributable to the
    /// (decimal)Math.Sqrt((double)...) conversion in SE's computation, not to the fix).
    ///
    /// Root cause (verified, not guessed): AdfStatistics.SelectLag's AIC comparison between lag=0 and
    /// lag=12 for this specific 100-bar series is a near-tie (the SSR reduction from adding lags very
    /// nearly offsets the 2k parameter penalty). AIC = n*ln(SSR/n) is computed via
    /// `(decimal)(nObs * Math.Log((double)(ssr/nObs)))` - the decimal-to-double cast of the raw SSR
    /// (not a scale-invariant ratio) loses relative precision differently at different absolute SSR
    /// magnitudes, and for a near-tied comparison this is enough to flip which lag wins. This is a
    /// PRE-EXISTING property of the AIC lag-selection code (AdfStatistics.SelectLag /
    /// AdfRegression.TryCompute's AIC output), completely unrelated to and unaffected by this
    /// sprint's SCI15-01 fix (which touches only the X'X accumulation and the upfront magnitude
    /// guard, neither used by AIC computation) - and it is explicitly out of this sprint's scope to
    /// address (Part 2.2/14 protect "sélection AIC des lags").
    ///
    /// The REAL, verified property this test asserts instead: the t-statistic IS exactly
    /// scale-invariant GIVEN a fixed selected lag - proven both by the closed-form OLS argument (beta
    /// and SE(beta) both derive from ratios of quantities that scale identically under a uniform
    /// rescaling with an intercept term present) and by these two direct measurements.
    /// </summary>
    private static void AssertAdfScaleStabilityWithinSafeRange()
    {
        decimal[] baseSeries = AdfGoldenDataset.RandomWalk(100);

        AdfResult scale1 = RunAdf(Scaled(baseSeries, 1m));
        AdfResult scale01 = RunAdf(Scaled(baseSeries, 0.1m));
        Assert(scale1.IsValid && scale01.IsValid, "ADF scale stability: scale=1 and scale=0.1 must both remain within the safe, computable range.");
        Assert(scale1.LagUsed == scale01.LagUsed, "ADF scale stability: this specific pair (1x, 0.1x) is expected to select the same lag (pre-verified) - if this now fails, the AIC near-tie has shifted and this test's grouping needs re-verification, not a wider tolerance.");
        AssertDecimalClose(scale1.Statistic, scale01.Statistic, "ADF scale stability (1x vs 0.1x, same lag): t-statistic must be exactly invariant to a uniform level rescaling when the same lag is selected.");

        AdfResult scale10 = RunAdf(Scaled(baseSeries, 10m));
        AdfResult scale100 = RunAdf(Scaled(baseSeries, 100m));
        Assert(scale10.IsValid && scale100.IsValid, "ADF scale stability: scale=10 and scale=100 must both remain within the safe, computable range.");
        Assert(scale10.LagUsed == scale100.LagUsed, "ADF scale stability: this specific pair (10x, 100x) is expected to select the same lag (pre-verified).");
        AssertDecimalClose(scale10.Statistic, scale100.Statistic, "ADF scale stability (10x vs 100x, same lag): t-statistic must be exactly invariant to a uniform level rescaling when the same lag is selected.");
    }

    private static decimal[] Scaled(decimal[] series, decimal scale)
    {
        var scaled = new decimal[series.Length];
        for (int i = 0; i < series.Length; i++)
            scaled[i] = series[i] * scale;
        return scaled;
    }

    /// <summary>KPSS's eta statistic is a ratio of two quantities (numerator: sum of squared cumulative-residual sums; denominator: long-run variance) that BOTH scale as the square of the series' level under a uniform rescaling, so eta itself is also expected to be scale-invariant - verified directly.</summary>
    private static void AssertKpssScaleStabilityWithinSafeRange()
    {
        decimal[] baseSeries = KpssGoldenDataset.RandomWalk(100);
        decimal[] scales = [1m, 10m, 100m, 0.1m];
        decimal? referenceStat = null;

        foreach (decimal scale in scales)
        {
            var scaled = new decimal[baseSeries.Length];
            for (int i = 0; i < baseSeries.Length; i++)
                scaled[i] = baseSeries[i] * scale;

            KpssResult result = RunKpss(scaled);
            Assert(result.IsValid, $"KPSS scale stability: scale={scale} must remain within the safe, computable range.");

            if (referenceStat is null)
            {
                referenceStat = result.Statistic;
            }
            else
            {
                AssertDecimalClose(referenceStat.Value, result.Statistic, $"KPSS scale stability at scale={scale}: eta statistic must be invariant to a uniform level rescaling.");
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // Part 13 — Lightweight before/after performance check (measure only, no optimization).
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    private static void MeasureAndCompareAdfKpssPerformance()
    {
        decimal[] adfSeries = AdfGoldenDataset.RandomWalk(WindowSize);
        decimal[] kpssSeries = KpssGoldenDataset.RandomWalk(WindowSize);
        const int iterations = 50;

        RunAdf(adfSeries); // JIT warm-up
        RunKpss(kpssSeries);

        var adfStopwatch = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
            RunAdf(adfSeries);
        adfStopwatch.Stop();

        var kpssStopwatch = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
            RunKpss(kpssSeries);
        kpssStopwatch.Stop();

        double adfAvgMs = adfStopwatch.Elapsed.TotalMilliseconds / iterations;
        double kpssAvgMs = kpssStopwatch.Elapsed.TotalMilliseconds / iterations;

        // Not a hard performance SLA - the added guard is a single O(n) scan (negligible next to the
        // O(n*k^2) regression it precedes) and the try/catch costs ~nothing when no exception is
        // thrown (standard .NET JIT behavior). This soft ceiling only catches a gross regression.
        Assert(adfAvgMs < 100.0, $"ADF at the 60-bar production window must not regress to pathological cost after the robustness fix (observed {adfAvgMs:F4} ms/call).");
        Assert(kpssAvgMs < 100.0, $"KPSS at the 60-bar production window must not regress to pathological cost after the robustness fix (observed {kpssAvgMs:F4} ms/call).");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────

    private static AdfResult RunAdf(decimal[] series) => new AdfEvidence().Compute(BuildContext(series));

    private static KpssResult RunKpss(decimal[] series) => new KpssEvidence().Compute(BuildContext(series));

    private static EvidenceContext BuildContext(decimal[] series) => new()
    {
        Series = series,
        SampleSize = series.Length,
        MinimumSampleSize = MinimumSampleSize,
        WindowSize = WindowSize,
        Timestamp = DateTime.UnixEpoch
    };

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
