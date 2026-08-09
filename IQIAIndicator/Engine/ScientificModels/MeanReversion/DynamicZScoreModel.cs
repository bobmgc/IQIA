using System;
using System.Collections.Generic;
using System.Linq;
using IQIAIndicator.Engine.Decision.States;
using IQIAIndicator.Engine.ScientificModels.Abstractions;

namespace IQIAIndicator.Engine.ScientificModels.MeanReversion;

public sealed class DynamicZScoreModel : IScientificModel
{
    public string Name => "DynamicZScoreModel";

    public string Category => "MeanReversion";

    private const double MaxZScoreForConfidence = 6.0;

    public ScientificModelResult Evaluate(ScientificModelContext context)
    {
        bool compatible =
            context.DecisionResult.Winner == MarketState.MeanReverting &&
            context.MethodologySelection.SelectedMethodology.Name == "MeanReversionMethodology";

        if (!compatible)
        {
            return new ScientificModelResult(
                Name,
                false,
                0.0,
                "The selected methodology and decision winner are not compatible with the Mean Reversion scientific stack.");
        }

        ScientificModelResult? kalmanResult = context.ScientificResults?.FirstOrDefault(result => result.ModelName == "KalmanFilterModel");
        if (kalmanResult is null || !kalmanResult.Success || kalmanResult.Metrics is null)
        {
            return new ScientificModelResult(
                Name,
                false,
                0.0,
                "DynamicZScoreModel requires valid KalmanFilterModel metrics from prior scientific execution.");
        }

        if (!TryGetFiniteDouble(kalmanResult.Metrics, "EstimatedMean", out double estimatedMean) ||
            !TryGetFiniteDouble(kalmanResult.Metrics, "InnovationStd", out double innovationStd))
        {
            return new ScientificModelResult(
                Name,
                false,
                0.0,
                "DynamicZScoreModel requires finite EstimatedMean and InnovationStd from KalmanFilterModel.");
        }

        double currentPrice = (double)context.MarketContext.CurrentBar;
        if (!double.IsFinite(currentPrice))
        {
            return new ScientificModelResult(
                Name,
                false,
                0.0,
                "DynamicZScoreModel received an invalid current price.");
        }

        double dynamicZScore;
        bool usedInnovationStdFallback = false;

        if (innovationStd <= 0.0)
        {
            usedInnovationStdFallback = true;
            dynamicZScore = currentPrice == estimatedMean
                ? 0.0
                : Math.Sign(currentPrice - estimatedMean) * MaxZScoreForConfidence;
        }
        else
        {
            dynamicZScore = (currentPrice - estimatedMean) / innovationStd;
        }

        double normalizedDistance = Math.Abs(dynamicZScore);
        double expectedReversionDistance = Math.Abs(currentPrice - estimatedMean);
        double dynamicConfidence = innovationStd > 0.0
            ? 1.0 - Math.Clamp(normalizedDistance / MaxZScoreForConfidence, 0.0, 1.0)
            : 0.0;

        if (!double.IsFinite(dynamicConfidence))
        {
            dynamicConfidence = 0.0;
        }

        string diagnostics = usedInnovationStdFallback
            ? "InnovationStd was zero or negative. Dynamic Z-Score returned a bounded fallback value."
            : "Dynamic Z-Score computed from Kalman estimated mean and innovation standard deviation.";

        var metrics = new Dictionary<string, object>
        {
            ["DynamicZScore"] = dynamicZScore,
            ["NormalizedDistance"] = normalizedDistance,
            ["ExpectedReversionDistance"] = expectedReversionDistance,
            ["DynamicConfidence"] = dynamicConfidence,
            ["Diagnostics"] = diagnostics
        };

        return new ScientificModelResult(
            Name,
            true,
            dynamicConfidence,
            "Dynamic Z-Score evaluation completed using scientific pipeline results.",
            metrics);
    }

    private static bool TryGetFiniteDouble(IReadOnlyDictionary<string, object> metrics, string key, out double value)
    {
        if (metrics.TryGetValue(key, out object? raw) && raw is double typed && double.IsFinite(typed))
        {
            value = typed;
            return true;
        }

        value = 0.0;
        return false;
    }
}
