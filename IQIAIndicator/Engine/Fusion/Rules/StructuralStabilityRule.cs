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
        double cusumQuality = Math.Clamp(cusum.Confidence, 0.0, 1.0) * SampleSizeStrength(cusum.SampleSize);
        double cusumStability = cusumQuality * Math.Exp(-CusumPressureScale * cusumPressure);

        double baiPerronQuality = Math.Clamp(baiPerron.Confidence, 0.0, 1.0) *
            SampleSizeStrength(baiPerron.SampleSize);
        double baiPerronStability = baiPerronQuality *
            Math.Exp(-Math.Max(0, baiPerron.BreakCount) / BreakCountDecayScale);

        double value = Math.Clamp(0.5 * (cusumStability + baiPerronStability), 0.0, 1.0);
        bool scientificDisagreement = cusum.ChangeDetected != (baiPerron.BreakCount > 0);
        string explanation = scientificDisagreement
            ? $"Scientific disagreement: CUSUM={cusumStability:F3}, Bai-Perron={baiPerronStability:F3}."
            : $"CUSUM and Bai-Perron structural stability evidence: CUSUM={cusumStability:F3}, " +
                $"Bai-Perron={baiPerronStability:F3}.";

        return new FusionConfidence
        {
            Value = value,
            Explanation = explanation
        };
    }

    private static double SampleSizeStrength(int sampleSize) =>
        1.0 - Math.Exp(-Math.Max(0, sampleSize) / SampleSizeScale);
}