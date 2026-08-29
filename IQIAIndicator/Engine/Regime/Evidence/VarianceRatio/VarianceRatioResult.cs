namespace IQIAIndicator.Engine.Regime.Evidence.VarianceRatio;

/// <summary>
/// Observations statistiques du test de Variance Ratio de Lo-MacKinlay.
/// </summary>
public sealed record VarianceRatioResult
{
    public required double VarianceRatio { get; init; }

    public required double ZStatistic { get; init; }

    public required double PValue { get; init; }

    public required double Confidence { get; init; }

    public required int Lag { get; init; }

    public required int SampleSize { get; init; }

    public required bool IsValid { get; init; }

    public required string Explanation { get; init; }

    public static VarianceRatioResult Invalid(string reason, int lag = 0, int sampleSize = 0) => new()
    {
        VarianceRatio = 0.0,
        ZStatistic = 0.0,
        PValue = 1.0,
        Confidence = 0.0,
        Lag = lag,
        SampleSize = sampleSize,
        IsValid = false,
        Explanation = reason
    };
}