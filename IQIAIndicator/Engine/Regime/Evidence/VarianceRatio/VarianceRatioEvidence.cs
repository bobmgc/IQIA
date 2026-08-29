using IQIAIndicator.Engine.Regime.Core;

namespace IQIAIndicator.Engine.Regime.Evidence.VarianceRatio;

/// <summary>
/// Producteur d'evidence du test de Variance Ratio, sans decision de regime.
/// </summary>
public sealed class VarianceRatioEvidence
{
    private const int TestLag = 5;

    public VarianceRatioResult Compute(EvidenceContext context)
    {
        if (context.SampleSize < context.MinimumSampleSize)
        {
            return VarianceRatioResult.Invalid(
                $"Warmup Variance Ratio ({context.SampleSize}/{context.MinimumSampleSize} bars).",
                TestLag,
                context.SampleSize);
        }

        if (context.Series.Count < context.SampleSize)
            return VarianceRatioResult.Invalid("Serie EvidenceContext incomplete.", TestLag, context.SampleSize);

        var prices = new double[context.SampleSize];
        for (int i = 0; i < prices.Length; i++)
            prices[i] = (double)context.Series[i];

        if (!VarianceRatioMath.TryLogReturns(prices, out double[] returns))
            return VarianceRatioResult.Invalid("Prix invalides pour les rendements logarithmiques.", TestLag, prices.Length);

        return VarianceRatioStatistics.Compute(returns, TestLag);
    }
}