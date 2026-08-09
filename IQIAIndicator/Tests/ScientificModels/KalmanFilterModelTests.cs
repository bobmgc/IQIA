using System;
using System.Collections.Generic;
using System.Linq;
using IQIAIndicator.Engine.Decision.Core;
using IQIAIndicator.Engine.Decision.States;
using IQIAIndicator.Engine.Methodology.Core;
using IQIAIndicator.Engine.ScientificModels.Abstractions;
using IQIAIndicator.Engine.ScientificModels.MeanReversion;

namespace IQIAIndicator.Tests.ScientificModels;

public static class KalmanFilterModelTests
{
    public static void RunAll()
    {
        AssertSeriesConstantProducesStableEstimate();
        AssertSeriesIncreasingProducesSmoothEstimate();
        AssertSeriesNoisyProducesValidResult();
        AssertInsufficientDataIsRejected();
        AssertNoNaNInOutput();
    }

    private static void AssertSeriesConstantProducesStableEstimate()
    {
        var context = CreateContext(new decimal[] { 100m, 100m, 100m, 100m, 100m }, 100m);
        var result = new KalmanFilterModel().Evaluate(context);

        Assert(result.Success, "Constant series must return a successful result.");
        Assert(result.Score > 0.9, "Constant series must produce a high scientific score.");
        Assert(result.Explanation.Contains("Estimated Mean=100.000000"), "Explanation must include the estimated mean.");
    }

    private static void AssertSeriesIncreasingProducesSmoothEstimate()
    {
        var context = CreateContext(new decimal[] { 100m, 101m, 102m, 103m, 104m }, 104m);
        var result = new KalmanFilterModel().Evaluate(context);

        Assert(result.Success, "Increasing series must return a successful result.");
        Assert(result.Score >= 0.0 && result.Score <= 1.0, "Score must remain in [0,1].");
        Assert(result.Explanation.Contains("KalmanGain"), "Explanation must include Kalman gain diagnostics.");
    }

    private static void AssertSeriesNoisyProducesValidResult()
    {
        var values = new decimal[] { 100m, 101m, 99m, 102m, 98m, 101m, 99m };
        var context = CreateContext(values, values[^1]);
        var result = new KalmanFilterModel().Evaluate(context);

        Assert(result.Success, "Noisy series must return a successful result.");
        Assert(result.Score >= 0.0 && result.Score <= 1.0, "Score must remain bounded.");
        Assert(!string.IsNullOrWhiteSpace(result.Explanation), "Explanation must not be empty.");
        Assert(result.Metrics is not null, "Result metrics must be available.");
        Assert(result.Metrics!.ContainsKey("EstimatedMean"), "Metrics must contain EstimatedMean.");
        Assert(result.Metrics.ContainsKey("Innovation"), "Metrics must contain Innovation.");
        Assert(result.Metrics.ContainsKey("KalmanGain"), "Metrics must contain KalmanGain.");
        Assert(result.Metrics.ContainsKey("FilterCovariance"), "Metrics must contain FilterCovariance.");

        Assert(result.Metrics["EstimatedMean"] is double, "EstimatedMean must be a double.");
        Assert(result.Metrics["Innovation"] is double, "Innovation must be a double.");
        Assert(result.Metrics["KalmanGain"] is double, "KalmanGain must be a double.");
        Assert(result.Metrics["FilterCovariance"] is double, "FilterCovariance must be a double.");
    }

    private static void AssertInsufficientDataIsRejected()
    {
        var context = CreateContext(new decimal[] { 100m }, 100m);
        var result = new KalmanFilterModel().Evaluate(context);

        Assert(!result.Success, "Insufficient data must be rejected.");
        Assert(result.Score == 0.0, "Rejected result must have score 0.");
    }

    private static void AssertNoNaNInOutput()
    {
        var values = new decimal[] { 100m, 100m, 101m, 99m, 102m };
        var context = CreateContext(values, values[^1]);
        var result = new KalmanFilterModel().Evaluate(context);

        Assert(!double.IsNaN(result.Score), "Score must not be NaN.");
        Assert(!double.IsInfinity(result.Score), "Score must not be Infinity.");
        Assert(result.Explanation.Contains("Estimated Mean="), "Diagnostic output must include estimated mean.");
        Assert(result.Metrics is not null, "Metrics must be present for compatibility.");

        foreach (var metric in result.Metrics!)
        {
            Assert(metric.Value is double, $"Metric '{metric.Key}' must be a double.");
            Assert(!double.IsNaN((double)metric.Value), $"Metric '{metric.Key}' must not be NaN.");
            Assert(!double.IsInfinity((double)metric.Value), $"Metric '{metric.Key}' must not be Infinity.");
        }
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
