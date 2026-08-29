using IQIAIndicator.Engine.Regime.Core;

namespace IQIAIndicator.Engine.Regime.Evidence.HalfLife;

/// <summary>
/// Producteur d'evidence statistique de demi-vie, sans decision de regime.
/// </summary>
public sealed class HalfLifeEvidence
{
    public HalfLifeResult Compute(EvidenceContext context)
    {
        if (context.SampleSize < context.MinimumSampleSize)
        {
            return HalfLifeResult.Invalid(
                $"Warmup Half-Life ({context.SampleSize}/{context.MinimumSampleSize} bars).",
                context.SampleSize);
        }

        if (context.Series.Count < context.SampleSize)
            return HalfLifeResult.Invalid("Serie EvidenceContext incomplete.", context.SampleSize);

        var series = new double[context.SampleSize];
        for (int i = 0; i < series.Length; i++)
            series[i] = (double)context.Series[i];

        return HalfLifeStatistics.Compute(series);
    }
}