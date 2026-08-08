using IQIAIndicator.Engine.Decision.States;
using IQIAIndicator.Engine.Regime.Core;
using IQIAIndicator.Engine.Regime.Evidence.HalfLife;
using IQIAIndicator.Engine.ScientificModels.Abstractions;

namespace IQIAIndicator.Engine.ScientificModels.MeanReversion;

public sealed class OrnsteinUhlenbeckModel : IScientificModel
{
    public string Name => "OrnsteinUhlenbeckModel";

    public string Category => "MeanReversion";

    private readonly HalfLifeEvidence _halfLifeEvidence = new();

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
                "OrnsteinUhlenbeckModel requires at least four historical observations to estimate a half-life.");
        }

        var values = new double[history.Count];
        for (int i = 0; i < history.Count; i++)
        {
            values[i] = (double)history[i];
        }

        var evidenceContext = new EvidenceContext
        {
            Series = history,
            SampleSize = history.Count,
            MinimumSampleSize = 4,
            WindowSize = 30,
            Timestamp = context.MarketContext.Timestamp,
            Symbol = null,
            TimeFrame = null
        };

        HalfLifeResult halfLife = _halfLifeEvidence.Compute(evidenceContext);
        if (!halfLife.IsValid)
        {
            return new ScientificModelResult(
                Name,
                false,
                0.0,
                $"Half-life evidence is invalid for OU: {halfLife.Explanation}");
        }

        double normalizedScore = Math.Clamp(1.0 / (1.0 + Math.Max(halfLife.HalfLife, 1.0) / 10.0), 0.0, 1.0);

        return new ScientificModelResult(
            Name,
            true,
            normalizedScore,
            $"OU evidence from Half-Life: lambda={halfLife.Lambda:F6}, HL={halfLife.HalfLife:F4}, R2={halfLife.RSquared:F4}, confidence={halfLife.Confidence:F4}.");
    }
}
