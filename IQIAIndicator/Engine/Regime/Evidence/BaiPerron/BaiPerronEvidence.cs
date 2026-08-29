using IQIAIndicator.Engine.Regime.Core;

namespace IQIAIndicator.Engine.Regime.Evidence.BaiPerron;

/// <summary>
/// Producteur d'evidence de ruptures structurelles, sans decision de regime.
/// </summary>
public sealed class BaiPerronEvidence
{
    public BaiPerronResult Compute(EvidenceContext context)
    {
        if (context.SampleSize < context.MinimumSampleSize)
        {
            return BaiPerronResult.Invalid(
                $"Warmup Bai-Perron ({context.SampleSize}/{context.MinimumSampleSize} bars).",
                context.SampleSize);
        }

        if (context.Series.Count < context.SampleSize)
            return BaiPerronResult.Invalid("Serie EvidenceContext incomplete.", context.SampleSize);

        var series = new double[context.SampleSize];
        for (int i = 0; i < series.Length; i++)
            series[i] = (double)context.Series[i];

        return BaiPerronStatistics.Compute(series);
    }
}