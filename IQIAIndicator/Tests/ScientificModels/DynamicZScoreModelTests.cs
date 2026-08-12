using System;
using System.Collections.Generic;
using IQIAIndicator.Engine.Decision.Core;
using IQIAIndicator.Engine.Decision.States;
using IQIAIndicator.Engine.Methodology.Core;
using IQIAIndicator.Engine.ScientificModels.Abstractions;
using IQIAIndicator.Engine.ScientificModels.MeanReversion;

namespace IQIAIndicator.Tests.ScientificModels;

public static class DynamicZScoreModelTests
{
    public static void RunAll()
    {
        AssertPriceEqualToEquilibrium();
        AssertPriceAboveEquilibriumProducesPositiveZScore();
        AssertPriceBelowEquilibriumProducesNegativeZScore();
        AssertInnovationStdVerySmallProducesFiniteMetrics();
        AssertInnovationStdZeroIsHandled();
        AssertMissingScientificResultsIsRejected();
        AssertNoNaNNoInfinityInMetrics();
    }

    private static void AssertPriceEqualToEquilibrium()
    {
        var context = CreateContext(100.0, 2.0, 100m);
        var result = new DynamicZScoreModel().Evaluate(context);

        Assert(result.Success, "Equality with the estimated mean must be successful.");
        Assert(result.Score == 1.0, "Score must be maximal when the price equals the estimated mean.");
        var metrics = result.Metrics ?? throw new InvalidOperationException("Metrics must be present.");
        AssertMetric(metrics, "DynamicZScore", 0.0);
        AssertMetric(metrics, "NormalizedDistance", 0.0);
        AssertMetric(metrics, "ExpectedReversionDistance", 0.0);
        AssertMetric(metrics, "DynamicConfidence", 1.0);
        Assert(metrics.ContainsKey("Diagnostics"), "Diagnostics must be present.");
    }

    private static void AssertPriceAboveEquilibriumProducesPositiveZScore()
    {
        var context = CreateContext(100.0, 2.0, 102m);
        var result = new DynamicZScoreModel().Evaluate(context);

        Assert(result.Success, "Price above the estimated mean must be successful.");
        var metrics = result.Metrics ?? throw new InvalidOperationException("Metrics must be present.");

        if (!metrics.TryGetValue("DynamicZScore", out var zScoreObj) || zScoreObj is not double dynamicZScore)
        {
            throw new InvalidOperationException("DynamicZScore must be a double.");
        }

        Assert(dynamicZScore > 0.0, "DynamicZScore must be positive when current price is above the estimated mean.");

        if (!metrics.TryGetValue("NormalizedDistance", out var normalizedDistanceObj) || normalizedDistanceObj is not double normalizedDistance)
        {
            throw new InvalidOperationException("NormalizedDistance must be a double.");
        }

        Assert(normalizedDistance > 0.0, "NormalizedDistance must be positive.");

        if (!metrics.TryGetValue("ExpectedReversionDistance", out var expectedDistanceObj) || expectedDistanceObj is not double expectedDistance)
        {
            throw new InvalidOperationException("ExpectedReversionDistance must be a double.");
        }

        Assert(expectedDistance > 0.0, "ExpectedReversionDistance must be positive.");
    }

    private static void AssertPriceBelowEquilibriumProducesNegativeZScore()
    {
        var context = CreateContext(100.0, 2.0, 98m);
        var result = new DynamicZScoreModel().Evaluate(context);

        Assert(result.Success, "Price below the estimated mean must be successful.");
        var metrics = result.Metrics ?? throw new InvalidOperationException("Metrics must be present.");

        if (!metrics.TryGetValue("DynamicZScore", out var zScoreObj) || zScoreObj is not double dynamicZScore)
        {
            throw new InvalidOperationException("DynamicZScore must be a double.");
        }

        Assert(dynamicZScore < 0.0, "DynamicZScore must be negative when current price is below the estimated mean.");
    }

    private static void AssertInnovationStdVerySmallProducesFiniteMetrics()
    {
        var context = CreateContext(100.0, 1e-9, 100.1m);
        var result = new DynamicZScoreModel().Evaluate(context);

        Assert(result.Success, "Very small InnovationStd must be handled successfully.");
        Assert(result.Metrics is not null, "Metrics must be present.");
        AssertIsFinite(result.Score, "Score must be finite.");

        foreach (var metric in result.Metrics!)
        {
            // "Diagnostics" is documented (see AssertInnovationStdZeroIsHandled below) as an
            // intentional string field on this model's metrics contract, not a numeric one.
            if (metric.Key == "Diagnostics")
            {
                Assert(metric.Value is string, "Diagnostics must be a string.");
                continue;
            }

            Assert(metric.Value is double, $"Metric '{metric.Key}' must be a double.");
            AssertIsFinite((double)metric.Value, $"Metric '{metric.Key}' must be finite.");
        }
    }

    private static void AssertInnovationStdZeroIsHandled()
    {
        var context = CreateContext(100.0, 0.0, 101m);
        var result = new DynamicZScoreModel().Evaluate(context);

        Assert(result.Success, "InnovationStd equal to zero must be handled successfully.");
        var metrics = result.Metrics ?? throw new InvalidOperationException("Metrics must be present.");

        if (!metrics.TryGetValue("DynamicConfidence", out var confidenceObj) || confidenceObj is not double confidence)
        {
            throw new InvalidOperationException("DynamicConfidence must be present as a double.");
        }

        Assert(confidence == 0.0, "DynamicConfidence must be zero when InnovationStd is zero.");

        if (!metrics.TryGetValue("Diagnostics", out var diagnosticsObj) || diagnosticsObj is not string)
        {
            throw new InvalidOperationException("Diagnostics must be a string.");
        }
    }

    private static void AssertMissingScientificResultsIsRejected()
    {
        var context = CreateContextWithoutScientificResults(100m);
        var result = new DynamicZScoreModel().Evaluate(context);

        Assert(!result.Success, "Missing ScientificResults must cause rejection.");
        Assert(result.Score == 0.0, "Rejected result must have score 0.");
    }

    private static void AssertNoNaNNoInfinityInMetrics()
    {
        var context = CreateContext(100.0, 2.0, 102.5m);
        var result = new DynamicZScoreModel().Evaluate(context);

        Assert(result.Success, "Valid metrics must return a successful result.");
        Assert(result.Metrics is not null, "Metrics must be present.");

        foreach (var metric in result.Metrics!)
        {
            if (metric.Key == "Diagnostics")
            {
                Assert(metric.Value is string, "Diagnostics must be a string.");
                continue;
            }

            Assert(metric.Value is double, $"Metric '{metric.Key}' must be a double.");
            AssertIsFinite((double)metric.Value, $"Metric '{metric.Key}' must be finite.");
        }
    }

    private static ScientificModelContext CreateContext(double estimatedMean, double innovationStd, decimal currentBar)
    {
        var kalmanMetrics = new Dictionary<string, object>
        {
            ["EstimatedMean"] = estimatedMean,
            ["InnovationStd"] = innovationStd
        };

        var scientificResult = new ScientificModelResult(
            "KalmanFilterModel",
            true,
            1.0,
            "Synthetic Kalman result for DynamicZScoreModel tests.",
            kalmanMetrics);

        // The model's real contract (DynamicZScoreModel.cs) derives the current price from
        // History[History.Count - 1], not from CurrentBar. A market history containing only the
        // intended current price as its last (and only) element is the minimal, realistic input
        // that actually reaches the model's Z-score computation instead of tripping its
        // "non-empty history required" guard.
        return new ScientificModelContext(
            new Engine.ScientificModels.Abstractions.MarketContext(DateTime.UtcNow, currentBar, new[] { currentBar }),
            new DecisionResult { Winner = MarketState.MeanReverting, Confidence = 0.9 },
            new MethodologySelection(
                new DecisionResult { Winner = MarketState.MeanReverting, Confidence = 0.9 },
                new QuantitativeMethodology(
                    "MeanReversionMethodology",
                    "Mean Reversion Methodology",
                    "Dynamic Z-Score",
                    new[] { "Kalman Filter", "Ornstein-Uhlenbeck", "Dynamic Z-Score" },
                    "SPRT",
                    new[] { "MeanReverting" },
                    "1.0",
                    Array.Empty<string>()),
                DateTime.UtcNow,
                "1.0",
                "Mean Reversion selected."),
            new[] { scientificResult });
    }

    private static ScientificModelContext CreateContextWithoutScientificResults(decimal currentBar)
    {
        return new ScientificModelContext(
            new Engine.ScientificModels.Abstractions.MarketContext(DateTime.UtcNow, currentBar, Array.Empty<decimal>()),
            new DecisionResult { Winner = MarketState.MeanReverting, Confidence = 0.9 },
            new MethodologySelection(
                new DecisionResult { Winner = MarketState.MeanReverting, Confidence = 0.9 },
                new QuantitativeMethodology(
                    "MeanReversionMethodology",
                    "Mean Reversion Methodology",
                    "Dynamic Z-Score",
                    new[] { "Kalman Filter", "Ornstein-Uhlenbeck", "Dynamic Z-Score" },
                    "SPRT",
                    new[] { "MeanReverting" },
                    "1.0",
                    Array.Empty<string>()),
                DateTime.UtcNow,
                "1.0",
                "Mean Reversion selected."),
            null);
    }

    private static void AssertMetric(IReadOnlyDictionary<string, object> metrics, string key, double expected)
    {
        if (!metrics.TryGetValue(key, out var valueObj) || valueObj is not double value)
        {
            throw new InvalidOperationException($"Metric '{key}' must be a double.");
        }

        Assert(value == expected, $"Metric '{key}' must equal {expected}.");
    }

    private static void AssertIsFinite(double value, string message)
    {
        Assert(!double.IsNaN(value) && !double.IsInfinity(value), message);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
