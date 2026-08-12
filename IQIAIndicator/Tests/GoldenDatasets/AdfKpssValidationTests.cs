using IQIAIndicator.Engine.Regime.Core;
using IQIAIndicator.Engine.Regime.Evidence;
using IQIAIndicator.Engine.Regime.Evidence.ADF;
using IQIAIndicator.Engine.Regime.Evidence.KPSS;

namespace IQIAIndicator.Tests.GoldenDatasets;

/// <summary>
/// Sprint 15 / Part B + Part C. ADF and KPSS science audit.
///
/// IMPORTANT FINDING (documented, not fixed - documentation is the correction here): the existing
/// AdfGoldenDataset.StatsmodelsReference and KpssGoldenDataset.StatsmodelsReference values
/// (WhiteNoise_N100, RandomWalk_N100) are explicitly marked "placeholder — à remplacer par valeur
/// Python" in their own source comments and were NEVER filled in with real statsmodels output.
/// AdfValidation.RunAllReport()/KpssValidation.RunAllReport() do not even call Compare() against
/// them. Asserting IsAcceptable against these placeholders would mean validating against an invented
/// number - explicitly forbidden by this sprint's rule 6. This file therefore does NOT wire
/// AdfValidation/KpssValidation's Compare() machinery into a passing assertion; it reconnects
/// RunAllReport() to xUnit (so it executes and its output is visible, satisfying "reconnect the
/// existing validation to the runner") and separately validates ADF/KPSS via mathematically
/// demonstrable properties (rule 8): known qualitative behavior on canonical series, and an
/// independent re-derivation of the documented MacKinnon/KPSS critical-value formulas.
/// </summary>
public static class AdfKpssValidationTests
{
    private const int WindowSize = 60;
    private const int MinimumSampleSize = 30;

    public static void RunAll()
    {
        // Reconnects the existing (pre-Sprint-15) golden dataset report generators to the test
        // runner, per Part B/C's explicit instruction not to recreate a parallel validation.
        AssertExistingReportsExecuteWithoutError();

        AssertAdfCriticalValuesMatchIndependentlyRecomputedMacKinnonFormula();
        AssertKpssCriticalValuesMatchDocumentedKwiatkowskiTable();

        AssertAdfRejectsUnitRootForStationarySeries();
        AssertAdfDoesNotRejectUnitRootForRandomWalk();
        AssertKpssDoesNotRejectStationarityForStationarySeries();
        AssertKpssRejectsStationarityForRandomWalk();
        AssertAdfAndKpssAgreeOnMeanRevertingOu();
        AssertAdfAndKpssAgreeOnRandomWalk();

        AssertAdfConstantSeriesIsHandledWithoutCrashing();
        AssertKpssConstantSeriesIsInvalidWithExplicitZeroVarianceGuard();
        AssertAdfShortSeriesIsRejectedAsWarmup();
        AssertKpssShortSeriesIsRejectedAsWarmup();
        AssertAdfNaNAndInfinityDoNotCrash();
        AssertKpssNaNAndInfinityDoNotCrash();
        AssertAdfExtremeValuesDoNotCrash();
    }

    // ── Reconnect existing validation scaffolding (Part B/C explicit instruction) ──────────────

    private static void AssertExistingReportsExecuteWithoutError()
    {
        string adfReport = AdfValidation.RunAllReport();
        string kpssReport = KpssValidation.RunAllReport();
        Assert(!string.IsNullOrWhiteSpace(adfReport), "AdfValidation.RunAllReport() must execute and produce output.");
        Assert(!string.IsNullOrWhiteSpace(kpssReport), "KpssValidation.RunAllReport() must execute and produce output.");
    }

    // ── Critical value formula re-derivation (rule 7: reuse the project's own documented reference) ─

    /// <summary>
    /// AdfCriticalValues.cs documents cv(T) = B_inf + B1/T + B2/T^2 with MacKinnon (1994) Table 1
    /// coefficients inline. This independently re-implements that exact formula from the same
    /// documented coefficients and checks AdfCriticalValues.Get produces identical output - this
    /// catches a transcription/arithmetic bug in the *implementation* (it cannot catch a wrong
    /// coefficient copied from the paper itself, since it uses the same coefficients as documented in
    /// the source file).
    /// </summary>
    private static void AssertAdfCriticalValuesMatchIndependentlyRecomputedMacKinnonFormula()
    {
        // MacKinnon (1994) Table 1, "constant only" (T2/nreg=1) - copied from AdfCriticalValues.cs's own doc comment.
        (decimal BInf, decimal B1, decimal B2)[] table =
        [
            (-3.43035m, -6.5393m, -16.786m),
            (-2.86154m, -2.8645m, -4.234m),
            (-2.56677m, -1.5384m, -2.809m),
        ];

        foreach (int t in new[] { 30, 60, 100, 250 })
        {
            var expected = table.Select(c => c.BInf + c.B1 / t + c.B2 / (t * (decimal)t)).ToArray();
            (decimal cv1, decimal cv5, decimal cv10) = AdfCriticalValues.Get(t);
            AssertClose(expected[0], cv1, $"ADF cv1% mismatch at T={t}");
            AssertClose(expected[1], cv5, $"ADF cv5% mismatch at T={t}");
            AssertClose(expected[2], cv10, $"ADF cv10% mismatch at T={t}");
        }
    }

    private static void AssertKpssCriticalValuesMatchDocumentedKwiatkowskiTable()
    {
        // Kwiatkowski et al. (1992) Table 1, eta_mu row - copied from KpssCriticalValues.cs's own doc comment.
        var (cv1, cv25, cv5, cv10) = KpssCriticalValues.Get(withTrend: false);
        AssertClose(0.739m, cv1, "KPSS cv1% must match Kwiatkowski et al. (1992) Table 1.");
        AssertClose(0.574m, cv25, "KPSS cv2.5% must match Kwiatkowski et al. (1992) Table 1.");
        AssertClose(0.463m, cv5, "KPSS cv5% must match Kwiatkowski et al. (1992) Table 1.");
        AssertClose(0.347m, cv10, "KPSS cv10% must match Kwiatkowski et al. (1992) Table 1.");
    }

    // ── Known qualitative behavior on canonical series (rule 8) ─────────────────────────────────

    private static void AssertAdfRejectsUnitRootForStationarySeries()
    {
        AdfResult result = RunAdf(SyntheticSeriesCatalog.MeanRevertingOu(200));
        Assert(result.IsValid, "ADF must produce a valid result for a well-formed 200-bar series.");
        Assert(result.IsStationary, "ADF must reject the unit-root null for a strongly mean-reverting OU series (textbook property of ADF).");
    }

    /// <summary>
    /// Uses AdfGoldenDataset.RandomWalk (N=100) rather than SyntheticSeriesCatalog.RandomWalk: this
    /// sprint found empirically that KPSS's Newey-West bandwidth correction (l=ceil(12*(T/100)^0.25))
    /// grows with T and measurably reduces power against unit-root alternatives, so a single fixed
    /// seed's realized path that clears cv5% at T=100 is not guaranteed to still clear it at T=200
    /// or T=300 (see AssertKpssRejectsStationarityForRandomWalk for the specific finding). Reusing
    /// the project's own existing, already-documented-as-reliable N=100 dataset here (rule 7) avoids
    /// re-deriving a new fixture parameter by trial and error.
    /// </summary>
    private static void AssertAdfDoesNotRejectUnitRootForRandomWalk()
    {
        AdfResult result = RunAdf(AdfGoldenDataset.RandomWalk(100));
        Assert(result.IsValid, "ADF must produce a valid result for a well-formed 100-bar series.");
        Assert(!result.IsStationary, "ADF must fail to reject the unit-root null for a genuine random walk (textbook property of ADF).");
    }

    private static void AssertKpssDoesNotRejectStationarityForStationarySeries()
    {
        KpssResult result = RunKpss(SyntheticSeriesCatalog.MeanRevertingOu(200));
        Assert(result.IsValid, "KPSS must produce a valid result for a well-formed 200-bar series.");
        Assert(result.IsStationary, "KPSS must fail to reject the stationarity null for a strongly mean-reverting OU series (textbook property of KPSS).");
    }

    /// <summary>
    /// FINDING (Sprint 15, documented per Part P - a real, root-caused statistical property, not a
    /// code defect; the fix here is dataset length, never the model): at T=100 (both this project's
    /// own KpssGoldenDataset.RandomWalk(100) and SyntheticSeriesCatalog.RandomWalk(100), seed=42),
    /// KPSS produced eta-hat=0.3766 against cv5%=0.463, cv10%=0.347 - a borderline realization that
    /// rejects the stationarity null at the 10% level but NOT at 5%, with p=0.0872. This was verified
    /// to be numerically correct, not a bug, by independently re-deriving the p-value from
    /// KpssStatistics.ApproximatePValue's own documented interpolation formula:
    /// 0.10 - (0.3766-0.347)/(0.463-0.347)*0.05 = 0.0872, matching exactly. Root cause: the Newey-West
    /// bandwidth l=ceil(12*(T/100)^0.25)=12 at T=100 already meaningfully inflates the long-run
    /// variance denominator; combined with this specific seed's realized path not wandering far
    /// enough from its own noise floor in only 100 steps, KPSS's power against the unit-root
    /// alternative is genuinely reduced (a documented, expected property of Newey-West-corrected
    /// KPSS, not specific to this implementation). KpssGoldenDataset.cs's own doc comment claiming
    /// "Statsmodels attendu : stat &gt; 1.0" for this series was never actually verified against real
    /// Python output (consistent with this sprint's broader finding that these reference comments are
    /// unvalidated placeholders) and is itself optimistically wrong for this realization - a
    /// documentation-accuracy finding, not an implementation one. A longer series (T=500) gives KPSS's
    /// eta statistic - which is asymptotically unbounded under the unit-root alternative, growing
    /// faster than the O(T^0.25) bandwidth correction - enough length to clear cv5% unambiguously,
    /// which is what this test now uses.
    /// </summary>
    private static void AssertKpssRejectsStationarityForRandomWalk()
    {
        KpssResult result = RunKpss(SyntheticSeriesCatalog.RandomWalk(500));
        Assert(result.IsValid, "KPSS must produce a valid result for a well-formed 500-bar series.");
        Assert(!result.IsStationary,
            $"KPSS must reject the stationarity null for a genuine 500-bar random walk (textbook property of KPSS). " +
            $"stat={result.Statistic:F4}, cv5={result.CriticalValue5:F4}, p={result.PValue:F4}.");
    }

    /// <summary>
    /// The sprint explicitly requires testing the ADF/KPSS combination logic: "ADF stationnaire +
    /// KPSS stationnaire" is one of the expected agreement cases. A strongly mean-reverting OU
    /// process is the textbook case both tests should agree is stationary.
    /// </summary>
    private static void AssertAdfAndKpssAgreeOnMeanRevertingOu()
    {
        decimal[] series = SyntheticSeriesCatalog.MeanRevertingOu(200);
        AdfResult adf = RunAdf(series);
        KpssResult kpss = RunKpss(series);
        Assert(adf.IsValid && kpss.IsValid, "Both tests must produce valid results.");
        Assert(adf.IsStationary && kpss.IsStationary,
            "ADF-stationary + KPSS-stationary is the expected agreement case for a strongly mean-reverting series.");
    }

    /// <summary>"ADF non-stationnaire + KPSS non-stationnaire" - the other expected agreement case. Uses a 500-bar series for the same reason as AssertKpssRejectsStationarityForRandomWalk (KPSS's power against a unit-root alternative needs a longer run than 100-200 bars to clear cv5% reliably for this generator/seed).</summary>
    private static void AssertAdfAndKpssAgreeOnRandomWalk()
    {
        decimal[] series = SyntheticSeriesCatalog.RandomWalk(500);
        AdfResult adf = RunAdf(series);
        KpssResult kpss = RunKpss(series);
        Assert(adf.IsValid && kpss.IsValid, "Both tests must produce valid results.");
        Assert(!adf.IsStationary && !kpss.IsStationary,
            "ADF-non-stationary + KPSS-non-stationary is the expected agreement case for a genuine random walk.");
    }

    // ── Edge cases ───────────────────────────────────────────────────────────────────────────────

    private static void AssertAdfConstantSeriesIsHandledWithoutCrashing()
    {
        AdfResult result = RunAdf(SyntheticSeriesCatalog.Constant(60));
        // A constant series has zero variance in y_{t-1} and zero variance in every regressor
        // (all differences are exactly 0), so the OLS system is singular by construction.
        // AdfRegression.TryCompute correctly detects this (AdfMath.SolveLinearSystem's pivot check)
        // and AdfEvidence returns an explicit Invalid result rather than NaN/exception.
        Assert(!result.IsValid, "A constant series must be reported Invalid (singular regression), not silently produce a misleading statistic.");
        Assert(!double.IsNaN((double)result.Statistic) && !double.IsInfinity((double)result.Statistic), "Invalid result's Statistic placeholder must still be finite.");
    }

    private static void AssertKpssConstantSeriesIsInvalidWithExplicitZeroVarianceGuard()
    {
        KpssResult result = RunKpss(SyntheticSeriesCatalog.Constant(60));
        Assert(!result.IsValid, "A constant series has zero long-run variance; KPSS must report Invalid explicitly (KpssLongRunVariance.Compute's gamma0==0 guard), not divide by zero.");
        Assert(result.Explanation.Contains("nulle", StringComparison.OrdinalIgnoreCase) || result.Explanation.Length > 0,
            "The invalid explanation must be present and non-empty.");
    }

    private static void AssertAdfShortSeriesIsRejectedAsWarmup()
    {
        AdfResult result = RunAdf(SyntheticSeriesCatalog.WhiteNoise(10));
        Assert(!result.IsValid, "A series shorter than MinimumSampleSize must be rejected as warmup, not processed.");
        Assert(result.Explanation.Contains("Warmup", StringComparison.OrdinalIgnoreCase), "The warmup rejection must be explicit in the explanation.");
    }

    private static void AssertKpssShortSeriesIsRejectedAsWarmup()
    {
        KpssResult result = RunKpss(SyntheticSeriesCatalog.WhiteNoise(10));
        Assert(!result.IsValid, "A series shorter than MinimumSampleSize must be rejected as warmup, not processed.");
        Assert(result.Explanation.Contains("Warmup", StringComparison.OrdinalIgnoreCase), "The warmup rejection must be explicit in the explanation.");
    }

    /// <summary>
    /// FIXED in Sprint 15.1 (SCI15-01). Was: injecting decimal.MaxValue reproducibly threw an
    /// uncaught System.OverflowException from AdfRegression.TryCompute. Now: AdfRegression's upfront
    /// magnitude guard (and try/catch backstop) converts this into a graceful Invalid result with an
    /// explicit explanation - see Tests/GoldenDatasets/AdfKpssRobustnessTests.cs (ADF-04..07) for the
    /// full extreme-value/NaN-analogue/scale-stability regression suite this sprint added. This test
    /// remains as the direct regression guard against the exact crash that was originally found.
    /// </summary>
    private static void AssertAdfNaNAndInfinityDoNotCrash()
    {
        decimal[] series = SyntheticSeriesCatalog.WhiteNoise(60);
        series[30] = decimal.MaxValue;
        var exception = Record(() => RunAdf(series));
        Assert(exception is null, $"AdfEvidence.Compute must never throw, even on an extreme-magnitude input (Sprint 15.1 fix for SCI15-01) - observed: {exception?.GetType().Name ?? "none"}.");

        AdfResult result = RunAdf(series);
        Assert(!result.IsValid, "An out-of-range-magnitude series must be reported as Invalid, not silently accepted.");
        Assert(!string.IsNullOrWhiteSpace(result.Explanation), "The Invalid result must carry an explicit explanation.");
    }

    /// <summary>FIXED in Sprint 15.1 (SCI15-02), same class of defect as ADF's, same fix pattern. See AssertAdfNaNAndInfinityDoNotCrash and AdfKpssRobustnessTests.cs (KPSS-04..07).</summary>
    private static void AssertKpssNaNAndInfinityDoNotCrash()
    {
        decimal[] series = SyntheticSeriesCatalog.WhiteNoise(60);
        series[30] = decimal.MaxValue;
        var exception = Record(() => RunKpss(series));
        Assert(exception is null, $"KpssEvidence.Compute must never throw, even on an extreme-magnitude input (Sprint 15.1 fix for SCI15-02) - observed: {exception?.GetType().Name ?? "none"}.");

        KpssResult result = RunKpss(series);
        Assert(!result.IsValid, "An out-of-range-magnitude series must be reported as Invalid, not silently accepted.");
        Assert(!string.IsNullOrWhiteSpace(result.Explanation), "The Invalid result must carry an explicit explanation.");
    }

    private static void AssertAdfExtremeValuesDoNotCrash()
    {
        decimal[] series = SyntheticSeriesCatalog.RandomWalk(60);
        for (int i = 0; i < series.Length; i++)
            series[i] *= 1_000_000m;
        AdfResult result = RunAdf(series);
        Assert(result.IsValid, "ADF must remain numerically well-behaved under a uniform scale change (t-statistic is scale-invariant).");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────

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

    private static void AssertClose(decimal expected, decimal actual, string message)
    {
        if (Math.Abs(expected - actual) > 0.0001m)
            throw new InvalidOperationException($"{message} Expected={expected}, Actual={actual}.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
