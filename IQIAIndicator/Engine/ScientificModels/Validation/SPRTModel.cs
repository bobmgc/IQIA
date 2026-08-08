using IQIAIndicator.Engine.Decision.States;
using IQIAIndicator.Engine.ScientificModels.Abstractions;

namespace IQIAIndicator.Engine.ScientificModels.Validation;

public sealed class SPRTModel : IScientificModel
{
    public string Name => "SPRTModel";

    public string Category => "Validation";

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
                "SPRTModel requires at least four observations to validate a Mean Reversion evidence set.");
        }

        double[] returns = new double[history.Count - 1];
        for (int i = 1; i < history.Count; i++)
        {
            returns[i - 1] = (double)history[i] - (double)history[i - 1];
        }

        double mean = returns.Average();
        double variance = 0.0;
        foreach (double value in returns)
        {
            variance += (value - mean) * (value - mean);
        }

        variance /= Math.Max(1, returns.Length - 1);
        double volatility = Math.Sqrt(Math.Max(variance, 1e-12));
        double spread = Math.Abs((double)context.MarketContext.CurrentBar - returns.Average());
        double z = volatility <= 1e-12 ? 0.0 : spread / volatility;

        double evidenceScore = Math.Clamp(1.0 - z / 10.0, 0.0, 1.0);
        bool accepted = evidenceScore >= 0.2 && context.DecisionResult.Confidence >= 0.0;

        return new ScientificModelResult(
            Name,
            accepted,
            evidenceScore,
            $"SPRTModel accepted={accepted}, evidenceScore={evidenceScore:F6}, z={z:F6}, volatility={volatility:F6}, decisionConfidence={context.DecisionResult.Confidence:F6}.");
    }
}
