using System;
using System.Collections.Generic;
using System.Linq;
using IQIAIndicator.Engine.Decision.States;
using IQIAIndicator.Engine.ScientificModels.Abstractions;

namespace IQIAIndicator.Engine.ScientificModels.Context;

public sealed class VolatilityModel : IScientificModel
{
    public string Name => "VolatilityModel";

    public string Category => "Context";

    private const int MinimumHistoryCount = 3;
    private const int CurrentVolatilityWindow = 20;
    private const int PercentileWindow = 20;

    public ScientificModelResult Evaluate(ScientificModelContext context)
    {
        bool compatible =
            context.DecisionResult.Winner == MarketState.MeanReverting &&
            context.MethodologySelection.SelectedMethodology.Name == "MeanReversionMethodology";

        if (!compatible)
        {
            return FailureResult("The selected methodology and decision winner are not compatible with the Mean Reversion scientific stack.");
        }

        IReadOnlyList<decimal> history = context.MarketContext.History;
        if (history is null || history.Count < MinimumHistoryCount)
        {
            return FailureResult("VolatilityModel requires at least three historical points for volatility estimation.");
        }

        // Derive current price from the provided history rather than using CurrentBar (which may be an index).
        double currentPrice = (double)history[history.Count - 1];

        if (context.ScientificResults is null)
        {
            return FailureResult("VolatilityModel requires prior ScientificResults for context validation.");
        }

        var scientificResults = context.ScientificResults;
        if (!TryGetPriorMetrics(scientificResults, (decimal)history[history.Count - 1], out var priorMetrics, out var priorExplanation))
        {
            return FailureResult(priorExplanation);
        }

        var currentVolatility = ComputeCurrentVolatility(history);
        if (!double.IsFinite(currentVolatility))
        {
            return FailureResult("CurrentVolatility computation produced an invalid numeric result.");
        }

        var referenceVolatility = ComputeReferenceVolatility(history);
        if (!double.IsFinite(referenceVolatility) || referenceVolatility <= 0.0)
        {
            referenceVolatility = currentVolatility;
        }

        double relativeVolatility = referenceVolatility > 0.0
            ? currentVolatility / referenceVolatility
            : 1.0;

        var volatilityPercentile = ComputeVolatilityPercentile(history, currentVolatility);
        var volatilityRegime = ClassifyVolatilityRegime(relativeVolatility, volatilityPercentile);
        double volatilityConfidence = ComputeVolatilityConfidence(relativeVolatility);

        var diagnostics = new Dictionary<string, object>(priorMetrics)
        {
            ["HistoryCount"] = history.Count,
            ["CurrentPrice"] = currentPrice,
            ["CurrentVolatility"] = currentVolatility,
            ["ReferenceVolatility"] = referenceVolatility,
            ["RelativeVolatility"] = relativeVolatility,
            ["VolatilityPercentile"] = volatilityPercentile,
            ["VolatilityRegime"] = volatilityRegime,
            ["VolatilityConfidence"] = volatilityConfidence
        };

        var metrics = new Dictionary<string, object>
        {
            ["CurrentVolatility"] = currentVolatility,
            ["ReferenceVolatility"] = referenceVolatility,
            ["RelativeVolatility"] = relativeVolatility,
            ["VolatilityPercentile"] = volatilityPercentile,
            ["VolatilityRegime"] = volatilityRegime,
            ["VolatilityConfidence"] = volatilityConfidence,
            ["Diagnostics"] = diagnostics
        };

        return new ScientificModelResult(
            Name,
            true,
            volatilityConfidence,
            "VolatilityModel completed volatility context evaluation.",
            metrics);
    }

    private static ScientificModelResult FailureResult(string explanation)
        => new ScientificModelResult("VolatilityModel", false, 0.0, explanation);

    private static bool TryGetPriorMetrics(IReadOnlyList<ScientificModelResult> scientificResults, decimal currentBar, out Dictionary<string, object> metrics, out string explanation)
    {
        metrics = new Dictionary<string, object>();
        explanation = string.Empty;

        ScientificModelResult? kalmanResult = scientificResults.FirstOrDefault(result => result.ModelName == "KalmanFilterModel");
        ScientificModelResult? ouResult = scientificResults.FirstOrDefault(result => result.ModelName == "OrnsteinUhlenbeckModel");
        ScientificModelResult? dynamicZScoreResult = scientificResults.FirstOrDefault(result => result.ModelName == "DynamicZScoreModel");

        if (kalmanResult is null || !kalmanResult.Success || kalmanResult.Metrics is null)
        {
            explanation = "VolatilityModel requires valid KalmanFilterModel metrics from prior scientific execution.";
            return false;
        }

        if (ouResult is null || !ouResult.Success || ouResult.Metrics is null)
        {
            explanation = "VolatilityModel requires valid OrnsteinUhlenbeckModel metrics from prior scientific execution.";
            return false;
        }

        if (dynamicZScoreResult is null || !dynamicZScoreResult.Success || dynamicZScoreResult.Metrics is null)
        {
            explanation = "VolatilityModel requires valid DynamicZScoreModel metrics from prior scientific execution.";
            return false;
        }

        if (!TryGetFiniteDouble(kalmanResult.Metrics, ScientificMetricKeys.EstimatedMean, out double estimatedMean) ||
            !TryGetFiniteDouble(kalmanResult.Metrics, ScientificMetricKeys.InnovationStd, out double innovationStd) ||
            !TryGetFiniteDouble(kalmanResult.Metrics, ScientificMetricKeys.KalmanGain, out double kalmanGain))
        {
            explanation = "VolatilityModel requires finite KalmanFilterModel metrics EstimatedMean, InnovationStd and KalmanGain.";
            return false;
        }

        if (!TryGetFiniteDouble(ouResult.Metrics, ScientificMetricKeys.EstimatedTheta, out double estimatedTheta) ||
            !TryGetFiniteDouble(ouResult.Metrics, ScientificMetricKeys.HalfLife, out double halfLife) ||
            !TryGetFiniteDouble(ouResult.Metrics, ScientificMetricKeys.MeanReversionStrength, out double meanReversionStrength))
        {
            explanation = "VolatilityModel requires finite OrnsteinUhlenbeckModel metrics EstimatedTheta, HalfLife and MeanReversionStrength.";
            return false;
        }

        if (!TryGetFiniteDouble(dynamicZScoreResult.Metrics, ScientificMetricKeys.DynamicZScore, out double dynamicZScore) ||
            !TryGetFiniteDouble(dynamicZScoreResult.Metrics, ScientificMetricKeys.NormalizedDistance, out double normalizedDistance))
        {
            explanation = "VolatilityModel requires finite DynamicZScoreModel metrics DynamicZScore and NormalizedDistance.";
            return false;
        }

        metrics[ScientificMetricKeys.EstimatedMean] = estimatedMean;
        metrics[ScientificMetricKeys.InnovationStd] = innovationStd;
        metrics[ScientificMetricKeys.KalmanGain] = kalmanGain;
        metrics[ScientificMetricKeys.EstimatedTheta] = estimatedTheta;
        metrics[ScientificMetricKeys.HalfLife] = halfLife;
        metrics[ScientificMetricKeys.MeanReversionStrength] = meanReversionStrength;
        metrics[ScientificMetricKeys.DynamicZScore] = dynamicZScore;
        metrics[ScientificMetricKeys.NormalizedDistance] = normalizedDistance;
        metrics["DistanceToEquilibrium"] = Math.Abs((double)currentBar - estimatedMean);

        return true;
    }

    private static double ComputeCurrentVolatility(IReadOnlyList<decimal> history)
    {
        int returnWindow = Math.Min(CurrentVolatilityWindow, history.Count - 1);
        double[] returns = GetTrailingReturns(history, returnWindow);
        return StandardDeviation(returns);
    }

    private static double ComputeReferenceVolatility(IReadOnlyList<decimal> history)
    {
        int window = Math.Min(CurrentVolatilityWindow, history.Count - 1);
        if (history.Count - 1 < window)
        {
            return 0.0;
        }

        var volatilities = new List<double>();
        for (int start = 0; start + window < history.Count; start++)
        {
            double[] returns = GetTrailingReturns(history, window, start);
            volatilities.Add(StandardDeviation(returns));
        }

        return volatilities.Count > 0 ? volatilities.Average() : 0.0;
    }

    private static double ComputeVolatilityPercentile(IReadOnlyList<decimal> history, double currentVolatility)
    {
        int window = Math.Min(PercentileWindow, history.Count - 1);
        if (history.Count - 1 < window || !double.IsFinite(currentVolatility) || currentVolatility < 0.0)
        {
            return 0.0;
        }

        var pastVolatilities = new List<double>();
        for (int start = 0; start + window < history.Count - 1; start++)
        {
            double[] returns = GetTrailingReturns(history, window, start);
            pastVolatilities.Add(StandardDeviation(returns));
        }

        if (pastVolatilities.Count == 0)
        {
            return 0.0;
        }

        int smallerCount = pastVolatilities.Count(value => value < currentVolatility);
        return smallerCount / (double)pastVolatilities.Count;
    }

    private static double[] GetTrailingReturns(IReadOnlyList<decimal> history, int count, int startIndex = -1)
    {
        if (count <= 0)
        {
            return Array.Empty<double>();
        }

        int endIndex = startIndex < 0 ? history.Count - 1 : startIndex + count;
        double[] returns = new double[count];
        for (int i = 0; i < count; i++)
        {
            int index = startIndex < 0 ? history.Count - count + i : startIndex + i + 1;
            returns[i] = (double)(history[index] - history[index - 1]);
        }

        return returns;
    }

    private static string ClassifyVolatilityRegime(double relativeVolatility, double volatilityPercentile)
    {
        if (!double.IsFinite(relativeVolatility) || !double.IsFinite(volatilityPercentile))
        {
            return "UNKNOWN";
        }

        if (relativeVolatility < 0.9 && volatilityPercentile < 0.33)
        {
            return "LOW";
        }

        if (relativeVolatility > 1.1 && volatilityPercentile > 0.66)
        {
            return "HIGH";
        }

        return "MEDIUM";
    }

    private static double ComputeVolatilityConfidence(double relativeVolatility)
    {
        if (!double.IsFinite(relativeVolatility))
        {
            return 0.0;
        }

        double confidence = 1.0 - Math.Min(Math.Abs(relativeVolatility - 1.0), 1.0);
        return Math.Clamp(confidence, 0.0, 1.0);
    }

    private static double StandardDeviation(double[] values)
    {
        if (values.Length == 0)
        {
            return 0.0;
        }

        double mean = values.Average();
        double sumSquared = 0.0;
        foreach (double value in values)
        {
            double diff = value - mean;
            sumSquared += diff * diff;
        }

        double variance = sumSquared / values.Length;
        return Math.Sqrt(Math.Max(variance, 0.0));
    }

    private static bool TryGetFiniteDouble(decimal input, out double value)
    {
        value = (double)input;
        return double.IsFinite(value);
    }

    private static bool TryGetFiniteDouble(IReadOnlyDictionary<string, object>? metrics, string key, out double value)
    {
        if (metrics?.TryGetValue(key, out object? raw) == true && raw is double typed && double.IsFinite(typed))
        {
            value = typed;
            return true;
        }

        value = 0.0;
        return false;
    }
}
