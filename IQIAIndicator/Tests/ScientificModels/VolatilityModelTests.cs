using System;
using System.Collections.Generic;
using IQIAIndicator.Engine.Decision.Core;
using IQIAIndicator.Engine.Decision.States;
using IQIAIndicator.Engine.Methodology.Core;
using IQIAIndicator.Engine.ScientificModels.Abstractions;
using IQIAIndicator.Engine.ScientificModels.Context;

namespace IQIAIndicator.Tests.ScientificModels;

public static class VolatilityModelTests
{
    public static void RunAll()
    {
        TestConstantHistoryProducesZeroVolatility();
        TestConstantHistoryClassifiesAsLowRegimeExplicitly();
        TestVeryVolatileHistoryProducesHighVolatility();
        TestLowVolatilityProducesLowRelativeVolatility();
        TestInsufficientHistoryIsRejected();
        TestInvalidDataIsRejected();
        TestNaNAndInfinityAreHandled();
        TestMetricsPresence();
    }

    private static void TestConstantHistoryProducesZeroVolatility()
    {
        var context = CreateContext(new[] { 100m, 100m, 100m, 100m });
        var result = new VolatilityModel().Evaluate(context);

        Assert(result.Success, "Constant history should still be processed.");
        Assert(result.Metrics is not null, "Metrics must be present.");
        var metrics = result.Metrics!;
        Assert(metrics["CurrentVolatility"] is double currentVolatility && Math.Abs(currentVolatility) < 1e-12, "CurrentVolatility should be zero for constant history.");
        Assert(metrics["VolatilityRegime"] is string regime && regime == "LOW", "Regime should be LOW for constant history.");
    }

    /// <summary>
    /// Sprint 14 regression guard for the constant-history bug: with CurrentVolatility and
    /// ReferenceVolatility both mathematically zero, RelativeVolatility is a 0/0 degeneracy whose
    /// previous fallback (1.0, "no change vs reference") fell into ClassifyVolatilityRegime's MEDIUM
    /// bucket instead of LOW. This test pins the explicit currentVolatility &lt;= epsilon -&gt; LOW guard
    /// directly, independent of TestConstantHistoryProducesZeroVolatility, so a future regression in
    /// either the guard or the fallback value is caught unambiguously.
    /// </summary>
    private static void TestConstantHistoryClassifiesAsLowRegimeExplicitly()
    {
        var context = CreateContext(new[] { 42.5m, 42.5m, 42.5m, 42.5m, 42.5m, 42.5m });
        var result = new VolatilityModel().Evaluate(context);

        Assert(result.Success, "Constant history should still be processed.");
        var metrics = result.Metrics ?? throw new InvalidOperationException("Metrics must be present.");
        Assert(metrics["CurrentVolatility"] is double zeroVolatility && zeroVolatility == 0.0, "CurrentVolatility must be exactly zero for a constant series.");
        Assert(metrics["VolatilityRegime"] is string regime && regime == "LOW", "A constant history (volatility == 0) must classify as LOW, never MEDIUM or HIGH.");
    }

    private static void TestVeryVolatileHistoryProducesHighVolatility()
    {
        var context = CreateContext(new[] { 100m, 120m, 80m, 130m, 70m });
        var result = new VolatilityModel().Evaluate(context);

        Assert(result.Success, "Very volatile history should be processed.");
        Assert(result.Metrics is not null, "Metrics must be present.");
        var metrics = result.Metrics!;
        Assert(metrics["CurrentVolatility"] is double currentVolatility && currentVolatility > 0.0, "CurrentVolatility should be positive.");
        Assert(metrics["VolatilityRegime"] is string regime && regime is "MEDIUM" or "HIGH", "Regime should be MEDIUM or HIGH for very volatile history.");
    }

    private static void TestLowVolatilityProducesLowRelativeVolatility()
    {
        var context = CreateContext(new[] { 100m, 100.5m, 100.4m, 100.6m, 100.45m, 100.5m });
        var result = new VolatilityModel().Evaluate(context);

        Assert(result.Success, "Low volatility history should be processed.");
        Assert(result.Metrics is not null, "Metrics must be present.");
        var metrics = result.Metrics!;
        Assert(metrics["RelativeVolatility"] is double relativeVolatility && relativeVolatility > 0.0, "RelativeVolatility should be finite and positive.");
        Assert(metrics["VolatilityRegime"] is string regime && regime is "LOW" or "MEDIUM", "Regime should be LOW or MEDIUM for low volatility history.");
    }

    private static void TestInsufficientHistoryIsRejected()
    {
        var context = CreateContext(new[] { 100m, 101m });
        var result = new VolatilityModel().Evaluate(context);

        Assert(!result.Success, "Insufficient history must be rejected.");
    }

    private static void TestInvalidDataIsRejected()
    {
        // includeInvalidPriorMetrics alone does not construct invalid data: invalidMetric defaults to
        // 0.0, which is a perfectly finite (if degenerate) value, so TryGetPriorMetrics' finiteness
        // guard never actually rejects it. This call was previously unreachable - VolatilityModelTests
        // aborted at TestConstantHistoryProducesZeroVolatility, the first test in RunAll - so this
        // authoring gap was never exercised. double.NaN is the value that makes the test's own name
        // and intent (invalid prior metrics must be rejected) true.
        var context = CreateContext(new[] { 100m, 100m, 100m }, includeInvalidPriorMetrics: true, invalidMetric: double.NaN);
        var result = new VolatilityModel().Evaluate(context);

        Assert(!result.Success, "Invalid data should be rejected.");
    }

    private static void TestNaNAndInfinityAreHandled()
    {
        // includeInvalidPriorMetrics must be explicitly set to true, or CreateScientificResults
        // silently falls back to the normal (valid) metrics dictionary and invalidMetric is never
        // actually used - previously unreachable (masked by TestConstantHistoryProducesZeroVolatility
        // failing first), so this omission was never exercised until this sprint's guard fix let
        // RunAll() proceed past the first test.
        var contextNaN = CreateContext(new[] { 100m, 101m, 102m, 103m }, includeInvalidPriorMetrics: true, invalidMetric: double.NaN);
        var resultNaN = new VolatilityModel().Evaluate(contextNaN);

        Assert(!resultNaN.Success, "NaN prior metric should be rejected.");

        var contextInf = CreateContext(new[] { 100m, 101m, 102m, 103m }, includeInvalidPriorMetrics: true, invalidMetric: double.PositiveInfinity);
        var resultInf = new VolatilityModel().Evaluate(contextInf);

        Assert(!resultInf.Success, "Infinity prior metric should be rejected.");
    }

    private static void TestMetricsPresence()
    {
        var context = CreateContext(new[] { 100m, 101m, 102m, 103m, 104m });
        var result = new VolatilityModel().Evaluate(context);

        Assert(result.Success, "Metrics presence test should pass.");
        Assert(result.Metrics is not null, "Metrics must be present.");
        var metrics = result.Metrics!;
        Assert(metrics.ContainsKey("CurrentVolatility"), "CurrentVolatility must be present.");
        Assert(metrics.ContainsKey("RelativeVolatility"), "RelativeVolatility must be present.");
        Assert(metrics.ContainsKey("VolatilityPercentile"), "VolatilityPercentile must be present.");
        Assert(metrics.ContainsKey("VolatilityRegime"), "VolatilityRegime must be present.");
        Assert(metrics.ContainsKey("VolatilityConfidence"), "VolatilityConfidence must be present.");
        Assert(metrics.ContainsKey("Diagnostics"), "Diagnostics must be present.");
    }

    private static ScientificModelContext CreateContext(decimal[]? history, decimal currentBar = 100m, bool includeInvalidPriorMetrics = false, double invalidMetric = 0.0)
    {
        var marketHistory = history ?? Array.Empty<decimal>();

        var marketContext = new MarketContext(DateTime.UtcNow, currentBar, marketHistory);
        var decisionResult = new DecisionResult { Winner = MarketState.MeanReverting, Confidence = 1.0 };
        var methodologySelection = new MethodologySelection(
            decisionResult,
            new QuantitativeMethodology(
                "MeanReversionMethodology",
                "Mean Reversion Methodology",
                "VolatilityModel",
                new[] { "KalmanFilterModel", "OrnsteinUhlenbeckModel", "DynamicZScoreModel" },
                "SPRT",
                new[] { "MeanReverting" },
                "1.0",
                Array.Empty<string>()),
            DateTime.UtcNow,
            "1.0",
            "Mean Reversion selected.");

        return new ScientificModelContext(
            marketContext,
            decisionResult,
            methodologySelection,
            CreateScientificResults(includeInvalidPriorMetrics, invalidMetric));
    }

    private static IReadOnlyList<ScientificModelResult> CreateScientificResults(bool includeInvalidPriorMetrics, double invalidMetric)
    {
        var metrics = includeInvalidPriorMetrics
            ? new Dictionary<string, object>
            {
                [ScientificMetricKeys.EstimatedMean] = invalidMetric,
                [ScientificMetricKeys.InnovationStd] = invalidMetric,
                [ScientificMetricKeys.KalmanGain] = invalidMetric,
                [ScientificMetricKeys.EstimatedTheta] = invalidMetric,
                [ScientificMetricKeys.HalfLife] = invalidMetric,
                [ScientificMetricKeys.MeanReversionStrength] = invalidMetric,
                [ScientificMetricKeys.DynamicZScore] = invalidMetric,
                [ScientificMetricKeys.NormalizedDistance] = invalidMetric
            }
            : new Dictionary<string, object>
            {
                [ScientificMetricKeys.EstimatedMean] = 100.0,
                [ScientificMetricKeys.InnovationStd] = 2.0,
                [ScientificMetricKeys.KalmanGain] = 0.8,
                [ScientificMetricKeys.EstimatedTheta] = 0.2,
                [ScientificMetricKeys.HalfLife] = 5.0,
                [ScientificMetricKeys.MeanReversionStrength] = 0.9,
                [ScientificMetricKeys.DynamicZScore] = 0.5,
                [ScientificMetricKeys.NormalizedDistance] = 0.5
            };

        var kalman = new ScientificModelResult("KalmanFilterModel", true, 1.0, "Synthetic Kalman result.", metrics);
        var ou = new ScientificModelResult("OrnsteinUhlenbeckModel", true, 1.0, "Synthetic OU result.", metrics);
        var dynamicZ = new ScientificModelResult("DynamicZScoreModel", true, 1.0, "Synthetic Dynamic Z-Score result.", metrics);

        return new[] { kalman, ou, dynamicZ };
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
