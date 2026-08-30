using System;
using System.Collections.Generic;
using IQIAIndicator.Engine.Decision.Core;
using IQIAIndicator.Engine.Decision.States;
using IQIAIndicator.Engine.Methodology.Core;
using IQIAIndicator.Engine.ScientificModels.Abstractions;
using IQIAIndicator.Engine.ScientificModels.Trend;

namespace IQIAIndicator.Tests.ScientificModels;

/// <summary>
/// Audit 2026-08-30 (P0-2). Isolation coverage of the real <see cref="TimeSeriesMomentumModel"/> - same
/// static-class / RunAll pattern as <see cref="DynamicZScoreModelTests"/>. Synthetic price histories
/// with a known drift + a deterministic oscillation (so per-bar return variance is non-zero and the
/// volatility-scaled t-stat is defined).
/// </summary>
public static class TimeSeriesMomentumModelTests
{
    public static void RunAll()
    {
        AssertUptrendProducesLongMomentum();
        AssertDowntrendProducesShortMomentum();
        AssertFlatMarketProducesLowConfidence();
        AssertIncompatibleRegimeIsRejected();
        AssertTooShortHistoryIsRejected();
        AssertMetricsAreFiniteAndConfidenceInRange();
    }

    private static void AssertUptrendProducesLongMomentum()
    {
        ScientificModelResult result = new TimeSeriesMomentumModel().Evaluate(Context(drift: 0.0006, count: 220));

        Assert(result.Success, "A clear uptrend must be evaluated successfully.");
        Assert(GetMetric(result, ScientificMetricKeys.MomentumScore) > 0.0, "Uptrend MomentumScore must be positive.");
        Assert(Equals(result.Metrics!["MomentumDirection"], "LONG"), "Uptrend direction must be LONG.");
        Assert(result.Score > 0.0 && result.Score <= 1.0, "Confidence must be a positive [0,1] value for a clear trend.");
    }

    private static void AssertDowntrendProducesShortMomentum()
    {
        ScientificModelResult result = new TimeSeriesMomentumModel().Evaluate(Context(drift: -0.0006, count: 220));

        Assert(result.Success, "A clear downtrend must be evaluated successfully.");
        Assert(GetMetric(result, ScientificMetricKeys.MomentumScore) < 0.0, "Downtrend MomentumScore must be negative.");
        Assert(Equals(result.Metrics!["MomentumDirection"], "SHORT"), "Downtrend direction must be SHORT.");
    }

    private static void AssertFlatMarketProducesLowConfidence()
    {
        ScientificModelResult result = new TimeSeriesMomentumModel().Evaluate(Context(drift: 0.0, count: 220));

        Assert(result.Success, "A flat market is still a successful (low-confidence) evaluation.");
        Assert(Math.Abs(GetMetric(result, ScientificMetricKeys.MomentumScore)) < 0.5, "Flat-market |MomentumScore| must be small.");
        Assert(result.Score < 0.5, "Flat-market confidence must be low.");
    }

    private static void AssertIncompatibleRegimeIsRejected()
    {
        ScientificModelContext context = Context(drift: 0.0006, count: 220) with
        {
            DecisionResult = new DecisionResult { Winner = MarketState.MeanReverting, Confidence = 0.9 }
        };
        ScientificModelResult result = new TimeSeriesMomentumModel().Evaluate(context);

        Assert(!result.Success, "A non-Trending winner must be rejected.");
        Assert(result.Score == 0.0, "A rejected result must have score 0.");
    }

    private static void AssertTooShortHistoryIsRejected()
    {
        ScientificModelResult result = new TimeSeriesMomentumModel().Evaluate(Context(drift: 0.0006, count: 10));

        Assert(!result.Success, "A history shorter than the smallest lookback must be rejected.");
    }

    private static void AssertMetricsAreFiniteAndConfidenceInRange()
    {
        ScientificModelResult result = new TimeSeriesMomentumModel().Evaluate(Context(drift: 0.0004, count: 300));

        Assert(result.Success, "Valid input must return a successful result.");
        Assert(result.Score is >= 0.0 and <= 1.0, "Confidence must be within [0,1].");
        foreach (KeyValuePair<string, object> metric in result.Metrics!)
        {
            if (metric.Value is double d)
            {
                Assert(double.IsFinite(d), $"Metric '{metric.Key}' must be finite.");
            }
        }
    }

    private static ScientificModelContext Context(double drift, int count)
    {
        var prices = new decimal[count];
        for (int i = 0; i < count; i++)
        {
            double value = 100.0 * Math.Exp(drift * i) * (1.0 + 0.003 * Math.Sin(i * 0.7));
            prices[i] = (decimal)value;
        }

        var decision = new DecisionResult { Winner = MarketState.Trending, Confidence = 0.8 };
        return new ScientificModelContext(
            new MarketContext(DateTime.UtcNow, count, prices),
            decision,
            new MethodologySelection(
                decision,
                new QuantitativeMethodology(
                    "TrendFollowingMethodology",
                    "Trend Following Methodology",
                    "Time Series Momentum",
                    new[] { "Volatility Models", "BOCPD" },
                    "SPRT",
                    new[] { "Trending" },
                    "1.0",
                    Array.Empty<string>()),
                DateTime.UtcNow,
                "1.0",
                "Trend following selected."),
            Array.Empty<ScientificModelResult>());
    }

    private static double GetMetric(ScientificModelResult result, string key)
    {
        if (result.Metrics is null || !result.Metrics.TryGetValue(key, out object? raw) || raw is not double value)
        {
            throw new InvalidOperationException($"Metric '{key}' missing or not a double.");
        }

        return value;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
