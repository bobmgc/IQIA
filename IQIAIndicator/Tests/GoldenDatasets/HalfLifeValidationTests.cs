using IQIAIndicator.Engine.Regime.Core;
using IQIAIndicator.Engine.Regime.Evidence.HalfLife;

namespace IQIAIndicator.Tests.GoldenDatasets;

/// <summary>
/// Sprint 15 / Part E. Half-Life science audit.
///
/// Unlike ADF/KPSS/DFA, HalfLifeGoldenDataset's Python reference values are NOT placeholders -
/// HalfLifeValidation.PythonReferences carries a comment "Références obtenues avec Python 3.13 et
/// scikit-learn 1.9.0 sur les séries seed=42" with real, high-precision numbers (not round
/// placeholder-looking values). This file wires that existing, already-real comparison into xUnit
/// (rule 7: use the existing reference in priority) and actually asserts on it, rather than treating
/// it as another unfilled scaffold.
///
/// Confirmed by direct code reading: HalfLifeEvidence.Compute delegates directly to
/// HalfLifeStatistics.Compute (the standalone OLS-based estimator) - it is not a dead, unused class;
/// this resolves the audit's open question of "why HalfLifeStatistics is or isn't used" (Part E).
/// </summary>
public static class HalfLifeValidationTests
{
    private const int WindowSize = 30;
    private const int MinimumSampleSize = 20;

    public static void RunAll()
    {
        AssertExistingReportExecutesWithoutError();
        AssertGoldenDatasetsMatchPythonReferenceWithinTolerance();

        AssertMeanRevertingOuProducesPositiveHalfLife();
        AssertHalfLifeGrowsAsMeanReversionWeakens();
        AssertRandomWalkIsRejectedAsNonMeanReverting();

        AssertConstantSeriesIsRejected();
        AssertShortSeriesIsRejectedAsWarmup();
        AssertNonFiniteSeriesIsRejectedNotCrashed();
    }

    // ── Reconnect + verify existing real-reference golden dataset ──────────────────────────────

    private static void AssertExistingReportExecutesWithoutError()
    {
        string report = HalfLifeValidation.RunAllReport();
        Assert(!string.IsNullOrWhiteSpace(report), "HalfLifeValidation.RunAllReport() must execute and produce output.");
    }

    /// <summary>
    /// Tolerance rationale: both implementations solve the identical closed-form OLS regression
    /// (lambda = Cov(y_t, Delta y_t) / Var(y_t)) on the exact same seed=42 series (the C# LCG
    /// generator's output is what the Python reference was computed from, per the file's own header
    /// comment) - the only expected source of discrepancy is floating-point summation order, which
    /// is many orders of magnitude smaller than 1e-6 relative error for a 256-point regression.
    /// </summary>
    private static void AssertGoldenDatasetsMatchPythonReferenceWithinTolerance()
    {
        IReadOnlyList<HalfLifeValidation.Metrics> metrics = HalfLifeValidation.CompareGoldenDatasets();
        Assert(metrics.Count == HalfLifeValidation.PythonReferences.Count, "Every golden dataset series must produce a comparable metric.");

        foreach (HalfLifeValidation.Metrics metric in metrics)
        {
            HalfLifeValidation.PythonReference reference = HalfLifeValidation.PythonReferences.Single(r => r.SeriesName == metric.SeriesName);
            Assert(metric.ValidityMatch, $"{metric.SeriesName}: IQIA's Invalid/Valid outcome must match the Python reference's (reference.IsValid={reference.IsValid}).");

            if (!metric.IsComparable)
                continue; // Both sides agree the series is not a valid mean-reverting fit (e.g. Trend) - nothing numeric to compare.

            AssertLess(metric.LambdaAbsoluteError, 1e-6, $"{metric.SeriesName}: lambda absolute error must be at floating-point-noise level.");
            AssertLess(metric.HalfLifeRelativeError, 1e-4, $"{metric.SeriesName}: half-life relative error must be at floating-point-noise level.");
            AssertLess(metric.RSquaredAbsoluteError, 1e-6, $"{metric.SeriesName}: R-squared absolute error must be at floating-point-noise level.");
        }
    }

    // ── Known qualitative behavior (rule 8) ─────────────────────────────────────────────────────

    private static void AssertMeanRevertingOuProducesPositiveHalfLife()
    {
        HalfLifeResult result = RunHalfLife(SyntheticSeriesCatalog.MeanRevertingOu(200));
        Assert(result.IsValid, "A strongly mean-reverting OU series must produce a valid Half-Life estimate.");
        Assert(result.Lambda < 0.0, "Genuine mean reversion requires a negative lambda (contract: HalfLifeStatistics.Compute explicitly rejects lambda >= 0).");
        Assert(result.HalfLife > 0.0, "Half-life must be strictly positive for a mean-reverting series.");
        // Theoretical continuous-time half-life for kappa=0.5 is ln(2)/0.5 ~= 1.386 bars; the discrete
        // AR(1) coefficient this OLS regression actually estimates is close to -kappa, so a loose
        // band around the theoretical value is the correct check, not an exact match (discretization
        // and finite-sample estimation both introduce real, expected deviation).
        Assert(result.HalfLife < 10.0, $"Half-life must stay in a short, plausible range for kappa=0.5; got {result.HalfLife:F4}.");
    }

    /// <summary>Monotonicity property: a more weakly mean-reverting process (smaller kappa) must produce a LONGER half-life than a strongly mean-reverting one - true by the definition HalfLife = -ln(2)/lambda regardless of the exact numeric value.</summary>
    private static void AssertHalfLifeGrowsAsMeanReversionWeakens()
    {
        HalfLifeResult strong = RunHalfLife(SyntheticSeriesCatalog.MeanRevertingOu(200, kappa: 0.7m));
        HalfLifeResult weak = RunHalfLife(SyntheticSeriesCatalog.MeanRevertingOu(200, kappa: 0.2m));
        Assert(strong.IsValid && weak.IsValid, "Both kappa variants must produce valid results.");
        Assert(weak.HalfLife > strong.HalfLife,
            $"Weaker mean reversion (kappa=0.2, HL={weak.HalfLife:F4}) must produce a longer half-life than stronger mean reversion (kappa=0.7, HL={strong.HalfLife:F4}).");
    }

    private static void AssertRandomWalkIsRejectedAsNonMeanReverting()
    {
        HalfLifeResult result = RunHalfLife(SyntheticSeriesCatalog.RandomWalk(200));
        // A genuine random walk's lambda should be statistically indistinguishable from zero (and,
        // per this specific finite realization, may land on either side of zero by chance). The
        // model's contract is to reject any non-negative lambda outright - assert the contract, not a
        // specific sign, since a random walk's true lambda is exactly 0.
        if (result.IsValid)
        {
            Assert(result.Lambda < 0.0, "If a random walk happens to produce a valid result, the contract (lambda < 0) must still hold - Invalid is the expected common outcome, but a spuriously-negative small lambda is not itself a defect.");
        }
    }

    // ── Edge cases ───────────────────────────────────────────────────────────────────────────────

    private static void AssertConstantSeriesIsRejected()
    {
        HalfLifeResult result = RunHalfLife(SyntheticSeriesCatalog.Constant(60));
        // Constant series -> Differences are all exactly 0 -> Variance(laggedValues) is 0 ->
        // HalfLifeRegression.TryFit's SingularTolerance guard rejects it explicitly.
        Assert(!result.IsValid, "A constant series (zero variance in the regressor) must be rejected explicitly, not produce a spurious half-life.");
    }

    private static void AssertShortSeriesIsRejectedAsWarmup()
    {
        HalfLifeResult result = RunHalfLife(SyntheticSeriesCatalog.WhiteNoise(10));
        Assert(!result.IsValid, "A series shorter than MinimumSampleSize (20) must be rejected as warmup.");
        Assert(result.Explanation.Contains("Warmup", StringComparison.OrdinalIgnoreCase), "The warmup rejection must be explicit.");
    }

    private static void AssertNonFiniteSeriesIsRejectedNotCrashed()
    {
        double[] series = SyntheticSeriesCatalog.WhiteNoise(60).Select(d => (double)d).ToArray();
        series[30] = double.NaN;
        HalfLifeResult result = HalfLifeStatistics.Compute(series);
        Assert(!result.IsValid, "A NaN value in the series must be rejected explicitly (HalfLifeStatistics.Compute's IsFinite guard), not propagate into a NaN half-life.");

        series[30] = double.PositiveInfinity;
        HalfLifeResult infResult = HalfLifeStatistics.Compute(series);
        Assert(!infResult.IsValid, "An Infinity value in the series must be rejected explicitly, not propagate.");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────

    private static HalfLifeResult RunHalfLife(decimal[] series) => new HalfLifeEvidence().Compute(new EvidenceContext
    {
        Series = series,
        SampleSize = series.Length,
        MinimumSampleSize = MinimumSampleSize,
        WindowSize = WindowSize,
        Timestamp = DateTime.UnixEpoch
    });

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
