using IQIAIndicator.Engine.Regime.Core;

namespace IQIAIndicator.Engine.Regime.Evidence.CUSUM;

/// <summary>
/// Producteur d'evidence de rupture CUSUM, sans decision de regime.
/// </summary>
public sealed class CusumEvidence
{
    public CusumResult Compute(EvidenceContext context)
    {
        if (context.SampleSize < context.MinimumSampleSize)
        {
            return CusumResult.Invalid(
                $"Warmup CUSUM ({context.SampleSize}/{context.MinimumSampleSize} bars).",
                context.SampleSize);
        }

        if (context.Series.Count < context.SampleSize)
            return CusumResult.Invalid("Serie EvidenceContext incomplete.", context.SampleSize);

        var series = new double[context.SampleSize];
        for (int i = 0; i < series.Length; i++)
            series[i] = (double)context.Series[i];

        return CusumStatistics.Compute(series);
    }
}