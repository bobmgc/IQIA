using System;
using System.Linq;
using System.Collections.Generic;
using IQIAIndicator.Engine.Decision.States;
using IQIAIndicator.Engine.ScientificModels.Abstractions;

namespace IQIAIndicator.Engine.ScientificModels.MeanReversion;

public sealed class KalmanFilterModel : IScientificModel
{
    public string Name => "KalmanFilterModel";

    public string Category => "MeanReversion";

    private const double MinimumVariance = 1e-6;
    private const double MinimumNoise = 1e-6;
    private const double InnovationScale = 3.0;

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
        if (history.Count < 2)
        {
            return new ScientificModelResult(
                Name,
                false,
                0.0,
                "KalmanFilterModel requires at least two historical observations to estimate an equilibrium.");
        }

        double currentPrice = (double)context.MarketContext.CurrentBar;
        double[] observations = history.Select(x => (double)x).ToArray();
        if (observations.Any(double.IsNaN) || observations.Any(double.IsInfinity) || double.IsNaN(currentPrice) || double.IsInfinity(currentPrice))
        {
            return new ScientificModelResult(
                Name,
                false,
                0.0,
                "KalmanFilterModel received invalid numeric values in the market context.");
        }

        double measurementNoise = EstimateMeasurementNoise(observations);
        double processNoise = Math.Max(measurementNoise * 0.1, MinimumNoise);

        double stateMean = observations[0];
        double stateCovariance = Math.Max(EstimateInitialVariance(observations), MinimumVariance);
        double lastInnovation = 0.0;
        double lastInnovationCovariance = stateCovariance + measurementNoise;
        double lastKalmanGain = 0.0;

        for (int i = 1; i < observations.Length; i++)
        {
            double predictedMean = stateMean;
            double predictedCovariance = stateCovariance + processNoise;

            double observation = observations[i];
            double innovation = observation - predictedMean;
            double innovationCovariance = predictedCovariance + measurementNoise;
            double kalmanGain = innovationCovariance <= 0.0
                ? 0.0
                : predictedCovariance / innovationCovariance;

            kalmanGain = Math.Clamp(kalmanGain, 0.0, 1.0);
            stateMean = predictedMean + kalmanGain * innovation;
            stateCovariance = Math.Max((1.0 - kalmanGain) * predictedCovariance, MinimumVariance);

            lastInnovation = innovation;
            lastInnovationCovariance = Math.Max(innovationCovariance, MinimumVariance);
            lastKalmanGain = kalmanGain;
        }

        double innovationAtCurrent = currentPrice - stateMean;
        double innovationCovarianceAtCurrent = stateCovariance + measurementNoise;
        double innovationStd = Math.Sqrt(Math.Max(innovationCovarianceAtCurrent, MinimumVariance));
        double normalizedInnovation = innovationStd <= 0.0
            ? 0.0
            : Math.Abs(innovationAtCurrent) / innovationStd;

        double score = 1.0 - Math.Clamp(normalizedInnovation / InnovationScale, 0.0, 1.0);

        string explanation =
            $"Estimated Mean={stateMean:F6}; " +
            $"Innovation={innovationAtCurrent:F6}; " +
            $"InnovationVariance={innovationCovarianceAtCurrent:F6}; " +
            $"KalmanGain={lastKalmanGain:F6}; " +
            $"FilterCovariance={stateCovariance:F6}.";

        var metrics = new Dictionary<string, object>
        {
            ["EstimatedMean"] = stateMean,
            ["CurrentPrice"] = currentPrice,
            ["Innovation"] = innovationAtCurrent,
            ["InnovationVariance"] = innovationCovarianceAtCurrent,
            ["InnovationStd"] = innovationStd,
            ["NormalizedInnovation"] = normalizedInnovation,
            ["KalmanGain"] = lastKalmanGain,
            ["FilterCovariance"] = stateCovariance,
            ["MeasurementNoise"] = measurementNoise,
            ["ProcessNoise"] = processNoise
        };

        return new ScientificModelResult(
            Name,
            true,
            score,
            explanation,
            metrics);
    }

    private static double EstimateMeasurementNoise(double[] observations)
    {
        if (observations.Length < 2)
            return 1.0;

        double mean = observations.Average();
        double variance = 0.0;
        foreach (double value in observations)
        {
            double delta = value - mean;
            variance += delta * delta;
        }

        variance /= observations.Length;
        return Math.Max(variance, MinimumNoise);
    }

    private static double EstimateInitialVariance(double[] observations)
    {
        if (observations.Length < 2)
            return 1.0;

        double sum = 0.0;
        double mean = observations.Average();
        foreach (double value in observations)
        {
            double delta = value - mean;
            sum += delta * delta;
        }

        return Math.Max(sum / (observations.Length - 1), MinimumVariance);
    }
}
