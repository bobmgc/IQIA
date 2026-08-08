using IQIAIndicator.Engine.Decision.States;
using IQIAIndicator.Engine.ScientificModels.Abstractions;

namespace IQIAIndicator.Engine.ScientificModels.MeanReversion;

public sealed class KalmanFilterModel : IScientificModel
{
    public string Name => "KalmanFilterModel";

    public string Category => "MeanReversion";

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

        if (context.MarketContext.History.Count < 2)
        {
            return new ScientificModelResult(
                Name,
                false,
                0.0,
                "KalmanFilterModel requires at least two historical observations to estimate an equilibrium.");
        }

        double[] history = context.MarketContext.History.Select(x => (double)x).ToArray();
        double measurementNoise = 0.05;
        double processNoise = 0.01;
        double priorMean = history[0];
        double priorVariance = 1.0;

        for (int i = 1; i < history.Length; i++)
        {
            double predictedMean = priorMean;
            double predictedVariance = priorVariance + processNoise;
            double kalmanGain = predictedVariance / (predictedVariance + measurementNoise);
            double observation = history[i];
            double updatedMean = predictedMean + kalmanGain * (observation - predictedMean);
            double updatedVariance = (1.0 - kalmanGain) * predictedVariance;

            priorMean = updatedMean;
            priorVariance = updatedVariance;
        }

        double last = history[^1];
        double deviation = Math.Abs(last - priorMean);
        double normalizedDeviation = Math.Clamp(deviation / Math.Max(1.0, Math.Abs(priorMean)), 0.0, 1.0);
        double score = Math.Clamp(1.0 - normalizedDeviation, 0.0, 1.0);

        return new ScientificModelResult(
            Name,
            true,
            score,
            $"KalmanFilterModel estimated equilibrium={priorMean:F6}, priorVariance={priorVariance:F6}, latestObservation={last:F6}, normalizedDeviation={normalizedDeviation:F6}.");
    }
}
