using IQIAIndicator.Engine.Regime.Core;
using IQIAIndicator.Engine.Regime.Evidence.VarianceRatio;

namespace IQIAIndicator.Tests.GoldenDatasets;

/// <summary>
/// Sprint 15 / Part F. Variance Ratio (Lo-MacKinlay) science audit.
///
/// VarianceRatioGoldenDataset's Python reference values are real (not placeholders) - the file's own
/// comment says "Références obtenues avec NumPy sur les séries déterministes seed=42 au lag 5."
/// Wired into xUnit here per rule 7 (use the existing reference).
///
/// Definition actually implemented (VarianceRatioStatistics.cs): VR(q) = Var(q-period return) /
/// (q * Var(1-period return)), tested at lag q=5 (VarianceRatioEvidence.TestLag), with the
/// heteroskedasticity-robust asymptotic variance of Lo &amp; MacKinlay (1988) used for the Z-statistic.
/// VR &gt; 1 indicates positive serial correlation (persistence/trending); VR &lt; 1 indicates negative
/// serial correlation (mean reversion); VR = 1 is consistent with the random walk null.
/// </summary>
public static class VarianceRatioValidationTests
{
    private const int WindowSize = 30;
    private const int MinimumSampleSize = 20;
    private const int TestLag = 5;

    public static void RunAll()
    {
        AssertExistingReportExecutesWithoutError();
        AssertGoldenDatasetsMatchPythonReferenceWithinTolerance();

        AssertMeanRevertingSeriesProducesVarianceRatioBelowOne();
        AssertPositiveAutocorrelationProducesVarianceRatioAboveOne();
        AssertScaleAndShiftInvarianceOfVarianceRatio();

        AssertConstantSeriesIsRejected();
        AssertShortSeriesIsRejectedAsWarmup();
        AssertNonPositivePriceIsRejectedNotCrashed();
    }

    private static void AssertExistingReportExecutesWithoutError()
    {
        string report = VarianceRatioValidation.RunAllReport();
        Assert(!string.IsNullOrWhiteSpace(report), "VarianceRatioValidation.RunAllReport() must execute and produce output.");
    }

    /// <summary>
    /// Tolerance rationale: identical closed-form variance-ratio formula computed on the same
    /// seed=42 series against a NumPy reference; expected discrepancy is floating-point summation
    /// order only.
    /// </summary>
    private static void AssertGoldenDatasetsMatchPythonReferenceWithinTolerance()
    {
        IReadOnlyList<VarianceRatioValidation.Metrics> metrics = VarianceRatioValidation.CompareGoldenDatasets();
        Assert(metrics.Count == VarianceRatioValidation.PythonReferences.Count, "Every golden dataset series must produce a comparable metric.");

        foreach (VarianceRatioValidation.Metrics metric in metrics)
        {
            AssertLess(metric.VarianceRatioRelativeError, 1e-6, $"{metric.SeriesName}: VarianceRatio relative error must be at floating-point-noise level.");
            AssertLess(metric.ZStatisticAbsoluteError, 1e-4, $"{metric.SeriesName}: Z-statistic absolute error must be small.");
            AssertLess(metric.PValueAbsoluteError, 1e-4, $"{metric.SeriesName}: p-value absolute error must be small.");
        }
    }

    // ── Known qualitative behavior (rule 8) ─────────────────────────────────────────────────────

    private static void AssertMeanRevertingSeriesProducesVarianceRatioBelowOne()
    {
        VarianceRatioResult result = RunVarianceRatio(SyntheticSeriesCatalog.MeanRevertingOu(300));
        Assert(result.IsValid, "A well-formed 300-bar mean-reverting series must produce a valid result.");
        Assert(result.VarianceRatio < 1.0,
            $"Negative serial correlation from mean reversion must produce VR({TestLag}) < 1 (textbook Lo-MacKinlay property); got {result.VarianceRatio:F6}.");
    }

    private static void AssertPositiveAutocorrelationProducesVarianceRatioAboveOne()
    {
        // Dataset 04 (AR(1), phi=0.5) has positive serial correlation in its LEVEL, but
        // VarianceRatioEvidence operates on LOG-RETURNS of the price series (TryLogReturns), so this
        // needs a series whose *returns* (not level) carry positive autocorrelation. The catalog's
        // Trending dataset's returns are dominated by a small constant drift plus i.i.d. noise
        // (near-zero return autocorrelation), so it is not suitable here either; construct a
        // return-autocorrelated price series directly, mirroring VarianceRatioGoldenDataset.PositiveAr1's
        // proven construction (returns[i] = 0.6*returns[i-1] + noise), which the existing, real Python
        // reference already confirms produces VR(5) = 2.907 > 1.
        VarianceRatioResult result = RunVarianceRatio(VarianceRatioGoldenDataset.PositiveAr1(256));
        Assert(result.IsValid, "PositiveAr1 must produce a valid result.");
        Assert(result.VarianceRatio > 1.0,
            $"Positive return autocorrelation must produce VR({TestLag}) > 1 (textbook Lo-MacKinlay property); got {result.VarianceRatio:F6}.");
    }

    /// <summary>
    /// Demonstrable mathematical property (not an invented reference number): VarianceRatio is a
    /// ratio of variances of sums of returns, so it is invariant to (a) a uniform multiplicative
    /// rescaling of returns and (b) an additive shift of returns (variance is shift-invariant).
    /// VarianceRatioGoldenDataset.WhiteNoise (returns = 0.01*z) and .Trend (returns = 0.002 +
    /// 0.002*z, i.e. the same z scaled by 0.2 and shifted by 0.002) are, by construction, exactly
    /// this kind of affine transform of each other - their real NumPy references already confirm
    /// this (1.165509225640102 vs 1.165509225640095, equal to 11 decimal places), which is itself
    /// strong evidence those Python numbers are genuine rather than fabricated. This test proves the
    /// same invariance holds for the IQIA implementation independent of the Python reference.
    /// </summary>
    private static void AssertScaleAndShiftInvarianceOfVarianceRatio()
    {
        VarianceRatioResult whiteNoise = RunVarianceRatio(VarianceRatioGoldenDataset.WhiteNoise(256));
        VarianceRatioResult trend = RunVarianceRatio(VarianceRatioGoldenDataset.Trend(256));
        Assert(whiteNoise.IsValid && trend.IsValid, "Both series must produce valid results.");
        Assert(Math.Abs(whiteNoise.VarianceRatio - trend.VarianceRatio) < 1e-6,
            $"VarianceRatio must be invariant to an affine (scale+shift) transform of returns; WhiteNoise={whiteNoise.VarianceRatio:F10}, Trend={trend.VarianceRatio:F10}.");
    }

    // ── Edge cases ───────────────────────────────────────────────────────────────────────────────

    private static void AssertConstantSeriesIsRejected()
    {
        VarianceRatioResult result = RunVarianceRatio(SyntheticSeriesCatalog.Constant(60));
        // Constant price -> log-returns all exactly 0 -> one-step variance is 0 -> explicit rejection.
        Assert(!result.IsValid, "A constant price series (zero one-step variance) must be rejected explicitly.");
    }

    private static void AssertShortSeriesIsRejectedAsWarmup()
    {
        VarianceRatioResult result = RunVarianceRatio(SyntheticSeriesCatalog.WhiteNoise(10));
        Assert(!result.IsValid, "A series shorter than MinimumSampleSize (20) must be rejected as warmup.");
        Assert(result.Explanation.Contains("Warmup", StringComparison.OrdinalIgnoreCase), "The warmup rejection must be explicit.");
    }

    private static void AssertNonPositivePriceIsRejectedNotCrashed()
    {
        decimal[] series = SyntheticSeriesCatalog.WhiteNoise(60);
        series[30] = -1m;
        VarianceRatioResult result = RunVarianceRatio(series);
        Assert(!result.IsValid, "A negative price must be rejected explicitly (log-return undefined for price <= 0), not throw.");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────

    private static VarianceRatioResult RunVarianceRatio(decimal[] series) =>
        RunVarianceRatioContext(new EvidenceContext
        {
            Series = series,
            SampleSize = series.Length,
            MinimumSampleSize = MinimumSampleSize,
            WindowSize = WindowSize,
            Timestamp = DateTime.UnixEpoch
        });

    private static VarianceRatioResult RunVarianceRatioContext(EvidenceContext context) =>
        new VarianceRatioEvidence().Compute(context);

    private static VarianceRatioResult RunVarianceRatio(double[] prices) => VarianceRatioValidation.RunOnPrices(prices, TestLag);

    private static void AssertLess(double actual, double bound, string message)
    {
        if (!(actual < bound))
            throw new InvalidOperationException($"{message} Actual={actual:E6}, Bound={bound:E6}.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
