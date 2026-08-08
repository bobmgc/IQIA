using IQIAIndicator.Engine.Decision.States;
using IQIAIndicator.Engine.ScientificModels.Abstractions;

namespace IQIAIndicator.Engine.ScientificModels.MeanReversion;

public sealed class DynamicZScoreModel : IScientificModel
{
    public string Name => "DynamicZScoreModel";

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

        IReadOnlyList<decimal> history = context.MarketContext.History;
        if (history.Count < 2)
        {
            return new ScientificModelResult(
                Name,
                false,
                0.0,
                "DynamicZScoreModel needs at least two points to evaluate a dynamic Z-score.");
        }

        int window = Math.Min(30, history.Count);
        double[] windowValues = history.Skip(history.Count - window).Select(x => (double)x).ToArray();
        double mean = windowValues.Average();
        double variance = 0.0;
        foreach (double value in windowValues)
        {
            double delta = value - mean;
            variance += delta * delta;
        }

        variance /= Math.Max(1, windowValues.Length - 1);
        double std = Math.Sqrt(Math.Max(variance, 1e-12));
        double current = (double)context.MarketContext.CurrentBar;
        double z = (current - mean) / std;

        double zScore = Math.Clamp(1.0 - Math.Abs(z) / 6.0, 0.0, 1.0);

        return new ScientificModelResult(
            Name,
            true,
            zScore,
            $"Dynamic Z-Score computed on {window} price points: mean={mean:F6}, std={std:F6}, current={current:F6}, z={z:F6}.");
    }
}
