using System;
using System.Collections.Generic;
using System.Linq;
using IQIAIndicator.Engine.Decision.States;
using IQIAIndicator.Engine.ScientificModels.Abstractions;

namespace IQIAIndicator.Engine.ScientificModels.MeanReversion;

public sealed class OrnsteinUhlenbeckModel : IScientificModel
{
    public string Name => "OrnsteinUhlenbeckModel";

    public string Category => "MeanReversion";

    private const double InnovationScale = 3.0;
    private const double InfiniteHalfLifeCap = 1_000_000.0;

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

        IReadOnlyList<decimal> history = context.MarketContext.History;
        if (history.Count < 4)
        {
            return new ScientificModelResult(
                Name,
                false,
                0.0,
                "OrnsteinUhlenbeckModel requires at least four historical observations to compute OU metrics.");
        }

        ScientificModelResult? kalmanResult = context.ScientificResults?.FirstOrDefault(result => result.ModelName == "KalmanFilterModel");
        if (kalmanResult is null || !kalmanResult.Success || kalmanResult.Metrics is null)
        {
            return new ScientificModelResult(
                Name,
                false,
                0.0,
                "OrnsteinUhlenbeckModel requires valid KalmanFilterModel metrics from prior model execution.");
        }

        if (!TryGetFiniteDouble(kalmanResult.Metrics, "EstimatedMean", out double estimatedMean) ||
            !TryGetFiniteDouble(kalmanResult.Metrics, "KalmanGain", out double kalmanGain) ||
            !TryGetFiniteDouble(kalmanResult.Metrics, "InnovationStd", out double innovationStd) ||
            !TryGetFiniteDouble(kalmanResult.Metrics, "NormalizedInnovation", out double normalizedInnovation) ||
            !TryGetFiniteDouble(kalmanResult.Metrics, "Innovation", out double innovation))
        {
            return new ScientificModelResult(
                Name,
                false,
                0.0,
                "OrnsteinUhlenbeckModel received invalid or incomplete Kalman metrics.");
        }

        kalmanGain = Math.Clamp(kalmanGain, 0.0, 1.0);
        double estimatedTheta = 1.0 - Math.Clamp(normalizedInnovation / InnovationScale, -1.0, 1.0);
        double halfLife = estimatedTheta > 0.0
            ? Math.Log(2.0) / estimatedTheta
            : InfiniteHalfLifeCap;

        if (!double.IsFinite(halfLife) || halfLife < 0.0)
        {
            halfLife = InfiniteHalfLifeCap;
        }

        double meanReversionStrength = estimatedTheta > 0.0
            ? Math.Clamp(estimatedTheta, 0.0, 1.0)
            : 0.0;

        double expectedDeviation = innovationStd;
        double score = Math.Clamp(meanReversionStrength, 0.0, 1.0);

        string explanation =
            $"OU diagnostics from Kalman metrics: EstimatedMean={estimatedMean:F6}; " +
            $"Theta={estimatedTheta:F6}; HalfLife={halfLife:F4}; " +
            $"MeanReversionStrength={meanReversionStrength:F6}; ExpectedDeviation={expectedDeviation:F6}.";

        var metrics = new Dictionary<string, object>
        {
            [ScientificMetricKeys.EstimatedMean] = estimatedMean,
            [ScientificMetricKeys.KalmanGain] = kalmanGain,
            [ScientificMetricKeys.Innovation] = innovation,
            [ScientificMetricKeys.InnovationStd] = innovationStd,
            [ScientificMetricKeys.NormalizedInnovation] = normalizedInnovation,
            [ScientificMetricKeys.EstimatedTheta] = estimatedTheta,
            [ScientificMetricKeys.HalfLife] = halfLife,
            [ScientificMetricKeys.MeanReversionStrength] = meanReversionStrength,
            [ScientificMetricKeys.ExpectedDeviation] = expectedDeviation,
            [ScientificMetricKeys.OUScore] = score
        };

        if (kalmanResult.Metrics.TryGetValue("FilterCovariance", out object? covarianceValue) && covarianceValue is double covariance)
        {
            metrics["FilterCovariance"] = covariance;
        }

        return new ScientificModelResult(
            Name,
            true,
            score,
            explanation,
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
