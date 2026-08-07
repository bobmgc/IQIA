using IQIAIndicator.Engine.Fusion.Core;
using IQIAIndicator.Engine.Regime.Evidence.BaiPerron;
using IQIAIndicator.Engine.Regime.Evidence.CUSUM;

namespace IQIAIndicator.Engine.Fusion.Rules;

/// <summary>
/// Estimates current structural coherence from transition pressure and historical instability.
/// </summary>
public sealed class StructuralStabilityRule : IFusionRule
{
    // Provisional calibration: CUSUM pressure is a temporary transition penalty.
    private const double TransitionPressureDecayScale = 1.25;
    // Provisional calibration: historical instability is capped so old breaks cannot dominate current stability.
    private const double HistoricalBreakDecayScale = 2.0;
    private const double MinimumHistoricalStability = 0.50;
    private const double SampleSizeScale = 50.0;
    private const double MinimumThreshold = 1e-12;
    // Provisional structural coherence weights: recent behavioural consistency dominates.
    private const double BehaviourConsistencyWeight = 0.50;
    private const double TransitionPressureWeight = 0.30;
    private const double HistoricalInstabilityWeight = 0.20;
    // Provisional final blend weights: current coherence remains the primary output.
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
                Confidence = 0.0,
                Explanation = "Missing Evidence"
            }
            : EvaluateEvidence(cusum, baiPerron);

        builder.Dimensions[FusionDimension.StructuralStability] = confidence;
    }

    private static FusionConfidence EvaluateEvidence(CusumResult cusum, BaiPerronResult baiPerron)
    {
        double cusumMagnitude = Math.Max(Math.Abs(cusum.PositiveCusum), Math.Abs(cusum.NegativeCusum));
        double normalizedCusumPressure = cusumMagnitude / Math.Max(Math.Abs(cusum.Threshold), MinimumThreshold);
        double transitionPressure = Math.Max(normalizedCusumPressure, cusum.ChangeDetected ? 1.0 : 0.0);
        double transitionStability = Math.Exp(-TransitionPressureDecayScale * transitionPressure);

        double rawHistoricalStability = Math.Exp(-Math.Max(0, baiPerron.BreakCount) / HistoricalBreakDecayScale);
        double historicalStability = MinimumHistoricalStability +
            (1.0 - MinimumHistoricalStability) * rawHistoricalStability;
        double behaviourConsistency = BehaviourConsistency(cusum, transitionStability, historicalStability);

        double scientificScore = Math.Clamp(
            BehaviourConsistencyWeight * behaviourConsistency +
            TransitionPressureWeight * transitionStability +
            HistoricalInstabilityWeight * historicalStability,
            0.0,
            1.0);
        double cusumQuality = 0.5 * (
            Math.Clamp(cusum.Confidence, 0.0, 1.0) + SampleSizeStrength(cusum.SampleSize));
        double baiPerronQuality = 0.5 * (
            Math.Clamp(baiPerron.Confidence, 0.0, 1.0) + SampleSizeStrength(baiPerron.SampleSize));
        double qualityScore = 0.5 * (cusumQuality + baiPerronQuality);
        double value = Blend(scientificScore, qualityScore);
        string scientificExplanation = cusum.ChangeDetected
            ? $"Current structural coherence under transition pressure: Behaviour={behaviourConsistency:F3}, " +
                $"Transition={transitionStability:F3}, Historical={historicalStability:F3}."
            : $"Current structural coherence with recovery: Behaviour={behaviourConsistency:F3}, " +
                $"Transition={transitionStability:F3}, Historical={historicalStability:F3}.";

        return new FusionConfidence
        {
            Value = scientificScore,
            Confidence = qualityScore,
            Explanation = ScoreExplanation(scientificScore, qualityScore, value, scientificExplanation)
        };
    }

    private static double BehaviourConsistency(
        CusumResult cusum,
        double transitionStability,
        double historicalStability)
    {
        double currentCoherence = cusum.ChangeDetected ? transitionStability : 1.0;
        double recovery = cusum.ChangeDetected ? transitionStability : 1.0;

        return Math.Clamp(
            0.60 * currentCoherence +
            0.30 * recovery +
            0.10 * historicalStability,
            0.0,
            1.0);
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
