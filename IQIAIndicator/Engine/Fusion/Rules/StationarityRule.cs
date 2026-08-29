using IQIAIndicator.Engine.Fusion.Core;
using IQIAIndicator.Engine.Regime.Evidence.ADF;
using IQIAIndicator.Engine.Regime.Evidence.KPSS;

namespace IQIAIndicator.Engine.Fusion.Rules;

/// <summary>
/// Mesure l'accord scientifique entre les tests ADF et KPSS sur la stationnarité.
/// </summary>
public sealed class StationarityRule : IFusionRule
{
    private const double SignificanceLevel = 0.05;
    private const double LogisticSteepness = 50.0;
    private const double ThresholdSmoothingRadius = 0.01;
    // Pondérations provisoires : le comportement scientifique reste prépondérant.
    private const double ScientificScoreWeight = 0.80;
    private const double QualityScoreWeight = 0.20;
    private const double ConfidenceQualityWeight = 0.70;
    private const double SampleSizeQualityWeight = 0.30;
    private const double SampleSizeScale = 60.0;

    public string Name => nameof(StationarityRule);

    public void Evaluate(FusionContext context, FusionResultBuilder builder)
    {
        AdfResult? adf = context.Evidence.Adf;
        KpssResult? kpss = context.Evidence.Kpss;
        FusionConfidence confidence = adf is null || kpss is null || !adf.IsValid || !kpss.IsValid
            ? new FusionConfidence
            {
                Value = 0.0,
                Confidence = 0.0,
                Explanation = "Missing Evidence",
                IsAvailable = false
            }
            : EvaluateEvidence(adf, kpss);

        builder.Dimensions[FusionDimension.Stationarity] = confidence;
    }

    private static FusionConfidence EvaluateEvidence(AdfResult adf, KpssResult kpss)
    {
        double adfStrength = EvidenceStrength(adf.PValue);
        double kpssStrength = EvidenceStrength(kpss.PValue);
        double adfEvidenceDirection = adf.IsStationary ? adfStrength : -adfStrength;
        double kpssEvidenceDirection = kpss.IsStationary ? kpssStrength : -kpssStrength;
        double scientificScore = Math.Clamp(
            0.5 + 0.25 * (adfEvidenceDirection + kpssEvidenceDirection),
            0.0,
            1.0);
        double confidenceQuality = 0.5 * (
            Math.Clamp((double)adf.Confidence, 0.0, 1.0) +
            Math.Clamp((double)kpss.Confidence, 0.0, 1.0));
        double sampleSizeQuality = 0.5 * (SampleSizeStrength(adf.SampleSize) + SampleSizeStrength(kpss.SampleSize));
        double qualityScore = ConfidenceQualityWeight * confidenceQuality +
            SampleSizeQualityWeight * sampleSizeQuality;
        double value = Blend(scientificScore, qualityScore);
        bool agreement = adf.IsStationary == kpss.IsStationary;
        string scientificExplanation = agreement
            ? adf.IsStationary
                ? $"ADF and KPSS agreement: stationarity (ADF={adfStrength:F3}, KPSS={kpssStrength:F3})."
                : $"ADF and KPSS agreement: non-stationarity (ADF={adfStrength:F3}, KPSS={kpssStrength:F3})."
            : $"ADF and KPSS disagreement: ADF={(adf.IsStationary ? "stationary" : "non-stationary")}, " +
                $"KPSS={(kpss.IsStationary ? "stationary" : "non-stationary")} " +
                $"(ADF={adfStrength:F3}, KPSS={kpssStrength:F3}).";

        return new FusionConfidence
        {
            Value = scientificScore,
            Confidence = qualityScore,
            Explanation = ScoreExplanation(scientificScore, qualityScore, value, scientificExplanation)
        };
    }

    private static double EvidenceStrength(decimal pValue)
    {
        double normalizedPValue = Math.Clamp((double)pValue, 0.0, 1.0);
        double distanceFromThreshold = Math.Abs(normalizedPValue - SignificanceLevel);
        double smoothedDistance = Math.Sqrt(
            distanceFromThreshold * distanceFromThreshold +
            ThresholdSmoothingRadius * ThresholdSmoothingRadius) -
            ThresholdSmoothingRadius;
        double logisticStrength = 2.0 / (1.0 + Math.Exp(-LogisticSteepness * smoothedDistance)) - 1.0;

        return logisticStrength;
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
