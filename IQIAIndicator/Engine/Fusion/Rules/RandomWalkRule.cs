using IQIAIndicator.Engine.Fusion.Core;
using IQIAIndicator.Engine.Regime.Evidence.VarianceRatio;

namespace IQIAIndicator.Engine.Fusion.Rules;

/// <summary>
/// Transforme l'évidence Variance Ratio en confiance quantitative de marche aléatoire.
/// </summary>
public sealed class RandomWalkRule : IFusionRule
{
    // Calibrages provisoires : écart logarithmique VR, statistique Z et taille d'échantillon.
    private const double VarianceRatioLogScale = 0.25;
    private const double ZStatisticScale = 2.0;
    private const double SampleSizeScale = 50.0;
    private const double InsufficientQualityLevel = 0.5;
    private const double MinimumPositiveVarianceRatio = 1e-12;
    // Pondérations provisoires : la compatibilité scientifique avec la marche aléatoire domine.
    private const double ScientificScoreWeight = 0.80;
    private const double QualityScoreWeight = 0.20;

    public string Name => nameof(RandomWalkRule);

    public void Evaluate(FusionContext context, FusionResultBuilder builder)
    {
        VarianceRatioResult? varianceRatio = context.Evidence.VarianceRatio;
        FusionConfidence confidence = varianceRatio is null || !varianceRatio.IsValid
            ? new FusionConfidence
            {
                Value = 0.0,
                Confidence = 0.0,
                Explanation = "Missing Evidence",
                IsAvailable = false
            }
            : EvaluateEvidence(varianceRatio);

        builder.Dimensions[FusionDimension.RandomWalk] = confidence;
    }

    private static FusionConfidence EvaluateEvidence(VarianceRatioResult varianceRatio)
    {
        double ratioCompatibility = Math.Exp(-Math.Abs(Math.Log(
            Math.Max(varianceRatio.VarianceRatio, MinimumPositiveVarianceRatio))) / VarianceRatioLogScale);
        double statisticCompatibility = Math.Exp(-Math.Abs(varianceRatio.ZStatistic) / ZStatisticScale);
        double pValueCompatibility = Math.Clamp(varianceRatio.PValue, 0.0, 1.0);
        double scientificScore = ratioCompatibility * statisticCompatibility * pValueCompatibility;
        double qualityScore = 0.5 * (
            Math.Clamp(varianceRatio.Confidence, 0.0, 1.0) +
            SampleSizeStrength(varianceRatio.SampleSize));
        double value = Blend(scientificScore, qualityScore);

        string scientificExplanation = qualityScore < InsufficientQualityLevel
            ? $"La faible qualité de l'estimation réduit la confiance (qualité={qualityScore:F3})."
            : $"Évidence compatible avec une marche aléatoire " +
                $"(VR={varianceRatio.VarianceRatio:F3}, Z={varianceRatio.ZStatistic:F3}, " +
                $"qualité={qualityScore:F3}).";

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
