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

    public string Name => nameof(StationarityRule);

    public void Evaluate(FusionContext context, FusionResultBuilder builder)
    {
        AdfResult? adf = context.Evidence.Adf;
        KpssResult? kpss = context.Evidence.Kpss;
        FusionConfidence confidence = adf is null || kpss is null || !adf.IsValid || !kpss.IsValid
            ? new FusionConfidence
            {
                Value = 0.0,
                Explanation = "Missing Evidence"
            }
            : EvaluateEvidence(adf, kpss);

        builder.Dimensions[FusionDimension.Stationarity] = confidence;
    }

    private static FusionConfidence EvaluateEvidence(AdfResult adf, KpssResult kpss)
    {
        double adfStrength = EvidenceStrength(adf.PValue, adf.Confidence);
        double kpssStrength = EvidenceStrength(kpss.PValue, kpss.Confidence);
        double adfEvidenceDirection = adf.IsStationary ? adfStrength : -adfStrength;
        double kpssEvidenceDirection = kpss.IsStationary ? kpssStrength : -kpssStrength;
        double value = Math.Clamp(0.5 + 0.25 * (adfEvidenceDirection + kpssEvidenceDirection), 0.0, 1.0);
        bool agreement = adf.IsStationary == kpss.IsStationary;
        string explanation = agreement
            ? adf.IsStationary
                ? $"ADF and KPSS agreement: stationarity (ADF={adfStrength:F3}, KPSS={kpssStrength:F3})."
                : $"ADF and KPSS agreement: non-stationarity (ADF={adfStrength:F3}, KPSS={kpssStrength:F3})."
            : $"ADF and KPSS disagreement: ADF={(adf.IsStationary ? "stationary" : "non-stationary")}, " +
                $"KPSS={(kpss.IsStationary ? "stationary" : "non-stationary")} " +
                $"(ADF={adfStrength:F3}, KPSS={kpssStrength:F3}).";

        return new FusionConfidence
        {
            Value = value,
            Explanation = explanation
        };
    }

    private static double EvidenceStrength(decimal pValue, decimal modelConfidence)
    {
        double normalizedPValue = Math.Clamp((double)pValue, 0.0, 1.0);
        double normalizedConfidence = Math.Clamp((double)modelConfidence, 0.0, 1.0);
        double distanceFromThreshold = Math.Abs(normalizedPValue - SignificanceLevel);
        double smoothedDistance = Math.Sqrt(
            distanceFromThreshold * distanceFromThreshold +
            ThresholdSmoothingRadius * ThresholdSmoothingRadius) -
            ThresholdSmoothingRadius;
        double logisticStrength = 2.0 / (1.0 + Math.Exp(-LogisticSteepness * smoothedDistance)) - 1.0;

        return normalizedConfidence * logisticStrength;
    }
}