using IQIAIndicator.Engine.Fusion.Core;
using IQIAIndicator.Engine.Regime.Evidence.DFA;
using IQIAIndicator.Engine.Regime.Evidence.VarianceRatio;

namespace IQIAIndicator.Engine.Fusion.Rules;

/// <summary>
/// Mesure l'accord scientifique entre DFA et Variance Ratio sur la persistance.
/// </summary>
public sealed class PersistenceRule : IFusionRule
{
    // Calibrage provisoire continu autour de H=0.5 et VR=1.0.
    private const double HurstRandomWalkLevel = 0.5;
    private const double HurstDirectionSteepness = 6.0;
    private const double VarianceRatioDirectionSteepness = 2.0;
    private const double PersistenceDirectionSteepness = 4.0;
    private const double PValueSignificanceLevel = 0.05;
    private const double PValueLogisticSteepness = 50.0;
    private const double ZStatisticScale = 2.0;
    private const double WindowCountScale = 4.0;
    private const double MinimumPositiveVarianceRatio = 1e-12;

    public string Name => nameof(PersistenceRule);

    public void Evaluate(FusionContext context, FusionResultBuilder builder)
    {
        DfaResult? dfa = context.Evidence.Dfa;
        VarianceRatioResult? varianceRatio = context.Evidence.VarianceRatio;
        FusionConfidence confidence = dfa is null || varianceRatio is null || !dfa.IsValid || !varianceRatio.IsValid
            ? new FusionConfidence
            {
                Value = 0.0,
                Explanation = "Missing Evidence"
            }
            : EvaluateEvidence(dfa, varianceRatio);

        builder.Dimensions[FusionDimension.Persistence] = confidence;
    }

    private static FusionConfidence EvaluateEvidence(DfaResult dfa, VarianceRatioResult varianceRatio)
    {
        double dfaDirection = Math.Tanh(HurstDirectionSteepness *
            (Math.Clamp(dfa.Hurst, 0.0, 2.0) - HurstRandomWalkLevel));
        double dfaQuality = Math.Clamp(dfa.Confidence, 0.0, 1.0) *
            Math.Clamp(dfa.RSquared, 0.0, 1.0) *
            (1.0 - Math.Exp(-Math.Max(0, dfa.WindowCount) / WindowCountScale));
        double dfaPersistence = dfaQuality * PersistenceSupport(dfaDirection);

        double varianceRatioDirection = Math.Tanh(VarianceRatioDirectionSteepness *
            Math.Log(Math.Max(varianceRatio.VarianceRatio, MinimumPositiveVarianceRatio)));
        double varianceRatioQuality = Math.Clamp(varianceRatio.Confidence, 0.0, 1.0) *
            PValueStrength(varianceRatio.PValue) *
            Math.Tanh(Math.Abs(varianceRatio.ZStatistic) / ZStatisticScale);
        double varianceRatioPersistence = varianceRatioQuality * PersistenceSupport(varianceRatioDirection);

        double value = Math.Clamp(0.5 * (dfaPersistence + varianceRatioPersistence), 0.0, 1.0);
        bool scientificDisagreement = dfaDirection * varianceRatioDirection < 0.0;
        string explanation = scientificDisagreement
            ? $"Scientific disagreement: DFA={dfaPersistence:F3}, VarianceRatio={varianceRatioPersistence:F3}."
            : $"DFA and Variance Ratio persistence evidence: DFA={dfaPersistence:F3}, " +
                $"VarianceRatio={varianceRatioPersistence:F3}.";

        return new FusionConfidence
        {
            Value = value,
            Explanation = explanation
        };
    }

    private static double PersistenceSupport(double direction)
    {
        double positiveDirectionProbability = 1.0 / (1.0 + Math.Exp(-PersistenceDirectionSteepness * direction));
        return direction * direction * positiveDirectionProbability;
    }

    private static double PValueStrength(double pValue)
    {
        double normalizedPValue = Math.Clamp(pValue, 0.0, 1.0);
        return 1.0 / (1.0 + Math.Exp(PValueLogisticSteepness *
            (normalizedPValue - PValueSignificanceLevel)));
    }
}