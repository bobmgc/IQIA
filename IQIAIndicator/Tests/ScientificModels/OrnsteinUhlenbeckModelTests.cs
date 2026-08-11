using System;
using System.Collections.Generic;
using IQIAIndicator.Engine.Decision.Core;
using IQIAIndicator.Engine.Decision.States;
using IQIAIndicator.Engine.Methodology.Core;
using IQIAIndicator.Engine.ScientificModels.Abstractions;
using IQIAIndicator.Engine.ScientificModels.MeanReversion;

namespace IQIAIndicator.Tests.ScientificModels;

public static class OrnsteinUhlenbeckModelTests
{
    public static void RunAll()
    {
        AssertConstantSeriesProducesStrongReversion();
        AssertStationarySeriesProducesPositiveTheta();
        AssertNoisySeriesCanProduceNegativeTheta();
        AssertRandomSeriesRemainsNumericallyStable();
        AssertOutputMetricsAreKalmanCompatible();
    }

    private static void AssertConstantSeriesProducesStrongReversion()
    {
        var context = CreateContext(new decimal[] { 100m, 100m, 100m, 100m, 100m }, 100m);
        var result = new OrnsteinUhlenbeckModel().Evaluate(context);

        Assert(result.Success, "Constant series must return a successful result.");
        Assert(result.Score >= 0.95, "Constant series must produce a strong OU score.");
        if (result.Metrics is null)
            throw new InvalidOperationException("Metrics must be present.");

        if (!result.Metrics.TryGetValue(ScientificMetricKeys.EstimatedTheta, out var estimatedThetaValueObj) || estimatedThetaValueObj is not double estimatedThetaValue)
            throw new InvalidOperationException("Metrics must contain EstimatedTheta as a double.");
        if (!result.Metrics.TryGetValue("HalfLife", out var halfLifeValueObj) || halfLifeValueObj is not double halfLifeValue)
            throw new InvalidOperationException("Metrics must contain HalfLife as a double.");
        if (!result.Metrics.TryGetValue("MeanReversionStrength", out var strengthValueObj) || strengthValueObj is not double strengthValue)
            throw new InvalidOperationException("Metrics must contain MeanReversionStrength as a double.");
        Assert(halfLifeValue < 10.0, "Half-Life must be short for a constant series.");
    }

    private static void AssertStationarySeriesProducesPositiveTheta()
    {
        var history = new decimal[] { 100m, 101m, 99m, 100m, 101m, 100m };
        var context = CreateContext(history, 100m);
        var result = new OrnsteinUhlenbeckModel().Evaluate(context);

        Assert(result.Success, "Stationary series must return a successful result.");
        if (result.Metrics is null)
            throw new InvalidOperationException("Metrics must be present.");

        if (!result.Metrics.TryGetValue("EstimatedTheta", out var thetaValueObj) || thetaValueObj is not double thetaValue)
            throw new InvalidOperationException("Metrics must contain EstimatedTheta as a double.");
        Assert(thetaValue > 0.0, "Stationary series must produce positive theta.");
        Assert(result.Explanation.Contains("Theta="), "Explanation must contain the theta diagnostics.");
    }

    private static void AssertNoisySeriesCanProduceNegativeTheta()
    {
        var history = new decimal[] { 100m, 110m, 90m, 115m, 85m, 120m };
        var context = CreateContext(history, 90m);
        var result = new OrnsteinUhlenbeckModel().Evaluate(context);

        Assert(result.Success, "Noisy series must return a successful result.");
        if (result.Metrics is null)
            throw new InvalidOperationException("Metrics must be present.");

        if (!result.Metrics.TryGetValue("EstimatedTheta", out var thetaValueObj) || thetaValueObj is not double thetaValue)
            throw new InvalidOperationException("Metrics must contain EstimatedTheta as a double.");
        Assert(thetaValue < 0.0, "Noisy series should be able to produce a negative theta.");
        if (!result.Metrics.TryGetValue("MeanReversionStrength", out var strengthValueObj) || strengthValueObj is not double strengthValue)
            throw new InvalidOperationException("Metrics must contain MeanReversionStrength as a double.");
        Assert(strengthValue == 0.0, "Negative theta should produce zero mean reversion strength.");
    }

    private static void AssertRandomSeriesRemainsNumericallyStable()
    {
        var history = new decimal[] { 108m, 92m, 113m, 89m, 105m, 95m, 110m, 90m };
        var context = CreateContext(history, 107m);
        var result = new OrnsteinUhlenbeckModel().Evaluate(context);

        Assert(result.Success, "Random series must return a successful result.");
        Assert(!double.IsNaN(result.Score), "Score must not be NaN.");
        Assert(!double.IsInfinity(result.Score), "Score must not be Infinity.");
        Assert(result.Metrics is not null, "Metrics must be present.");

        foreach (var metric in result.Metrics!)
        {
            Assert(metric.Value is double, $"Metric '{metric.Key}' must be a double.");
            Assert(!double.IsNaN((double)metric.Value), $"Metric '{metric.Key}' must not be NaN.");
            Assert(!double.IsInfinity((double)metric.Value), $"Metric '{metric.Key}' must not be Infinity.");
        }
    }

    private static void AssertOutputMetricsAreKalmanCompatible()
    {
        var history = new decimal[] { 100m, 102m, 98m, 101m, 99m, 100m };
        var context = CreateContext(history, 100m);
        var result = new OrnsteinUhlenbeckModel().Evaluate(context);

        Assert(result.Success, "OU model must return a successful result when Kalman metrics are valid.");
        if (result.Metrics is null)
            throw new InvalidOperationException("Metrics must be present.");

        Assert(result.Metrics.TryGetValue("EstimatedMean", out var estimatedMeanValueObj) && estimatedMeanValueObj is double estimatedMeanValue, "Metrics must contain EstimatedMean as a double.");
        Assert(result.Metrics.TryGetValue("KalmanGain", out var kalmanGainValueObj) && kalmanGainValueObj is double kalmanGainValue, "Metrics must contain KalmanGain as a double.");
        Assert(result.Metrics.TryGetValue("HalfLife", out var halfLifeValueObj) && halfLifeValueObj is double halfLifeValue, "Metrics must contain HalfLife as a double.");
        Assert(result.Metrics.TryGetValue("EstimatedTheta", out var thetaValueObj) && thetaValueObj is double thetaValue, "Metrics must contain EstimatedTheta as a double.");
        Assert(result.Metrics.TryGetValue("MeanReversionStrength", out var strengthValueObj) && strengthValueObj is double strengthValue, "Metrics must contain MeanReversionStrength as a double.");
        Assert(result.Metrics.TryGetValue("ExpectedDeviation", out var expectedDeviationValueObj) && expectedDeviationValueObj is double expectedDeviationValue, "Metrics must contain ExpectedDeviation as a double.");
        Assert(result.Metrics.TryGetValue("OUScore", out var ouScoreValueObj) && ouScoreValueObj is double ouScoreValue, "Metrics must contain OUScore as a double.");
    }

    private static ScientificModelContext CreateContext(IReadOnlyList<decimal> history, decimal currentBar)
    {
        return new ScientificModelContext(
            new Engine.ScientificModels.Abstractions.MarketContext(DateTime.UtcNow, currentBar, history),
            new DecisionResult { Winner = MarketState.MeanReverting, Confidence = 0.75 },
            new MethodologySelection(
                new DecisionResult { Winner = MarketState.MeanReverting, Confidence = 0.75 },
                new QuantitativeMethodology(
                    "MeanReversionMethodology",
                    "Mean Reversion Methodology",
                    "OrnsteinUhlenbeck",
                    new[] { "Kalman Filter", "Dynamic Z-Score", "Volatility Models", "BOCPD" },
                    "SPRT",
                    new[] { "MeanReverting" },
                    "1.0",
                    Array.Empty<string>()),
                DateTime.UtcNow,
                "1.0",
                "Mean Reversion selected."));
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
