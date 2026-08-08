using IQIAIndicator.Engine.Decision.States;
using IQIAIndicator.Engine.ScientificModels.Abstractions;

namespace IQIAIndicator.Engine.ScientificModels.Context;

public sealed class VolatilityModel : IScientificModel
{
    public string Name => "VolatilityModel";

    public string Category => "Context";

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
        if (history.Count < 3)
        {
            return new ScientificModelResult(
                Name,
                false,
                0.0,
                "VolatilityModel requires at least three historical points for clustering estimation.");
        }

        double[] returns = new double[history.Count - 1];
        for (int i = 1; i < history.Count; i++)
        {
            returns[i - 1] = (double)history[i] - (double)history[i - 1];
        }

        if (returns.Length < 2)
        {
            return new ScientificModelResult(
                Name,
                false,
                0.0,
                "VolatilityModel cannot estimate clustering from fewer than two returns.");
        }

        double mean = returns.Average();
        double numerator = 0.0;
        double denominator = 0.0;
        for (int i = 0; i < returns.Length; i++)
        {
            double delta = returns[i] - mean;
            denominator += delta * delta;
        }

        if (denominator <= 1e-12)
        {
            return new ScientificModelResult(
                Name,
                false,
                0.0,
                "VolatilityModel encountered a degenerate return variance for clustering inference.");
        }

        for (int i = 0; i < returns.Length - 1; i++)
        {
            double a = Math.Abs(returns[i]) - mean;
            double b = Math.Abs(returns[i + 1]) - mean;
            numerator += a * b;
        }

        double acf = numerator / denominator;
        bool clustering = acf > 0.05;
        double confidence = Math.Clamp(Math.Abs(acf), 0.0, 1.0);

        return new ScientificModelResult(
            Name,
            true,
            confidence,
            $"Volatility clustering estimate: acf(|r|)={acf:F6}, clustering={clustering}, confidence={confidence:F6}.");
    }
}
