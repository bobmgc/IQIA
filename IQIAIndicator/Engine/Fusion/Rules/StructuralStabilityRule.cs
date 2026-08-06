using IQIAIndicator.Engine.Fusion.Core;
using IQIAIndicator.Engine.Regime.Evidence.BaiPerron;
using IQIAIndicator.Engine.Regime.Evidence.CUSUM;

namespace IQIAIndicator.Engine.Fusion.Rules;

/// <summary>
/// Mesure l'accord scientifique entre CUSUM et Bai-Perron sur la stabilité structurelle.
/// </summary>
public sealed class StructuralStabilityRule : IFusionRule
{
    // Calibrages provisoires : deux unités de pression CUSUM et cinquante observations.
    private const double CusumPressureScale = 2.0;
    private const double BreakCountDecayScale = 1.0;
    private const double SampleSizeScale = 50.0;
    private const double MinimumThreshold = 1e-12;
    // Pondérations provisoires : l'absence scientifique de rupture reste déterminante.
    private const double ScientificScoreWeight = 0.90;
    private const double QualityScoreWeight = 0.10;

    public string Name => nameof(StructuralStabilityRule);

    public void Evaluate(FusionContext context, FusionResultBuilder builder)
    {
        CusumResult? cusum = context.Evidence.Cusum;
        BaiPerronResult? baiPerron = context.Evidence.BaiPerron;
        FusionConfidence confidence = cusum is null || baiPerron is null || !cusum.IsValid || !baiPerron.IsValid
            ? new FusionConfidence
            {
                Value = 0.0,
                Explanation = "Missing Evidence"
            }
            : EvaluateEvidence(cusum, baiPerron);

        builder.Dimensions[FusionDimension.StructuralStability] = confidence;
    }

    private static FusionConfidence EvaluateEvidence(CusumResult cusum, BaiPerronResult baiPerron)
    {
        double cusumMagnitude = Math.Max(Math.Abs(cusum.PositiveCusum), Math.Abs(cusum.NegativeCusum));
        double cusumPressure = cusumMagnitude / Math.Max(Math.Abs(cusum.Threshold), MinimumThreshold);
        double cusumStability = Math.Exp(-CusumPressureScale * cusumPressure);

        double baiPerronStability = Math.Exp(-Math.Max(0, baiPerron.BreakCount) / BreakCountDecayScale);

        double scientificScore = Math.Clamp(0.5 * (cusumStability + baiPerronStability), 0.0, 1.0);
        double cusumQuality = 0.5 * (
            Math.Clamp(cusum.Confidence, 0.0, 1.0) + SampleSizeStrength(cusum.SampleSize));
        double baiPerronQuality = 0.5 * (
            Math.Clamp(baiPerron.Confidence, 0.0, 1.0) + SampleSizeStrength(baiPerron.SampleSize));
        double qualityScore = 0.5 * (cusumQuality + baiPerronQuality);
        double value = Blend(scientificScore, qualityScore);
        bool scientificDisagreement = cusum.ChangeDetected != (baiPerron.BreakCount > 0);
        string scientificExplanation = scientificDisagreement
            ? $"Scientific disagreement: CUSUM={cusumStability:F3}, Bai-Perron={baiPerronStability:F3}."
            : $"CUSUM and Bai-Perron structural stability evidence: CUSUM={cusumStability:F3}, " +
                $"Bai-Perron={baiPerronStability:F3}.";

        return new FusionConfidence
        {
            Value = value,
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