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
    // Pondérations provisoires : la vitesse scientifique de retour à la moyenne domine.
    private const double ScientificScoreWeight = 0.80;
    private const double QualityScoreWeight = 0.20;

    public string Name => nameof(MeanReversionRule);

    public void Evaluate(FusionContext context, FusionResultBuilder builder)
    {
        HalfLifeResult? halfLife = context.Evidence.HalfLife;
        FusionConfidence confidence = halfLife is null || !halfLife.IsValid
            ? new FusionConfidence
            {
                Value = 0.0,
                Confidence = 0.0,
                Explanation = "Missing Evidence",
                IsAvailable = false
            }
            : EvaluateEvidence(halfLife);

        builder.Dimensions[FusionDimension.MeanReversion] = confidence;
    }

    private static FusionConfidence EvaluateEvidence(HalfLifeResult halfLife)
    {
        double scientificScore = Math.Exp(-Math.Max(0.0, halfLife.HalfLife) / HalfLifeDecayScale);
        double qualityScore = (
            Math.Clamp(halfLife.Confidence, 0.0, 1.0) +
            Math.Clamp(halfLife.RSquared, 0.0, 1.0) +
            SampleSizeStrength(halfLife.SampleSize)) / 3.0;
        double value = Blend(scientificScore, qualityScore);

        string scientificExplanation = qualityScore < InsufficientQualityLevel
            ? $"La qualité de l'estimation est insuffisante (qualité={qualityScore:F3})."
            : $"Évidence de retour à la moyenne issue de la demi-vie " +
                $"(demi-vie={halfLife.HalfLife:F3}, qualité={qualityScore:F3}).";

        return new FusionConfidence
        {
            Value = scientificScore,
            Confidence = qualityScore,
            Explanation = ScoreExplanation(scientificScore, qualityScore, value, scientificExplanation)
        };
    }

    private static double SampleSizeStrength(int sampleSize) =>
        1.0 - Math.Exp(-Math.Max(0, sampleSize) / SampleSizeScale);

    private static double Blend(double scientificScore, double qualityScore) =>
        Math.Clamp(ScientificScoreWeight * scientificScore + QualityScoreWeight * qualityScore, 0.0, 1.0);

    private static string ScoreExplanation(
        double scientificScore,
        double qualityScore,
        double finalScore,
        string scientificExplanation) =>
        $"Scientific Score={scientificScore:F3}; Quality Score={qualityScore:F3}; Final Score={finalScore:F3}. " +
        scientificExplanation;
}
