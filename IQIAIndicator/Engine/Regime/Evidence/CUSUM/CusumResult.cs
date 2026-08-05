namespace IQIAIndicator.Engine.Regime.Evidence.CUSUM;

/// <summary>
/// Observations statistiques produites par le détecteur CUSUM de Page.
/// </summary>
public sealed record CusumResult
{
    public required bool ChangeDetected { get; init; }

    public required int EstimatedBreakIndex { get; init; }

    public required double PositiveCusum { get; init; }

    public required double NegativeCusum { get; init; }

    public required double Threshold { get; init; }

    public required double Confidence { get; init; }

    public required int SampleSize { get; init; }

    public required bool IsValid { get; init; }

    public required string Explanation { get; init; }

    public static CusumResult Invalid(string reason, int sampleSize = 0) => new()
    {
        ChangeDetected = false,
        EstimatedBreakIndex = -1,
        PositiveCusum = 0.0,
        NegativeCusum = 0.0,
        Threshold = 0.0,
        Confidence = 0.0,
        SampleSize = sampleSize,
        IsValid = false,
        Explanation = reason
    };
}