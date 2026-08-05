using IQIAIndicator.Engine.Fusion.Core;
using IQIAIndicator.Engine.Regime.Evidence.HalfLife;

namespace IQIAIndicator.Engine.Fusion.Rules;

/// <summary>
/// Transforme l'estimation de demi-vie en évidence quantitative de retour à la moyenne.
/// </summary>
public sealed class MeanReversionRule : IFusionRule
{
    // Calibrages provisoires : dix unités de demi-vie et cinquante observations.
    private const double HalfLifeDecayScale = 10.0;
    private const double SampleSizeScale = 50.0;
    private const double InsufficientQualityLevel = 0.5;

    public string Name => nameof(MeanReversionRule);

    public void Evaluate(FusionContext context, FusionResultBuilder builder)
    {
        HalfLifeResult? halfLife = context.Evidence.HalfLife;
        FusionConfidence confidence = halfLife is null || !halfLife.IsValid
            ? new FusionConfidence
            {
                Value = 0.0,
                Explanation = "Missing Evidence"
            }
            : EvaluateEvidence(halfLife);

        builder.Dimensions[FusionDimension.MeanReversion] = confidence;
    }

    private static FusionConfidence EvaluateEvidence(HalfLifeResult halfLife)
    {
        double meanReversionSpeed = Math.Exp(-Math.Max(0.0, halfLife.HalfLife) / HalfLifeDecayScale);
        double estimationQuality = Math.Clamp(halfLife.Confidence, 0.0, 1.0) *
            Math.Clamp(halfLife.RSquared, 0.0, 1.0) *
            (1.0 - Math.Exp(-Math.Max(0, halfLife.SampleSize) / SampleSizeScale));
        double value = Math.Clamp(meanReversionSpeed * estimationQuality, 0.0, 1.0);

        string explanation = estimationQuality < InsufficientQualityLevel
            ? $"La qualité de l'estimation est insuffisante (qualité={estimationQuality:F3})."
            : $"Évidence de retour à la moyenne issue de la demi-vie " +
                $"(demi-vie={halfLife.HalfLife:F3}, qualité={estimationQuality:F3}).";

        return new FusionConfidence
        {
            Value = value,
            Explanation = explanation
        };
    }
}