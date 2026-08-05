namespace IQIAIndicator.Engine.Regime.Evidence.HalfLife;

/// <summary>
/// Observations statistiques issues de l'estimation de la demi-vie.
/// </summary>
public sealed record HalfLifeResult
{
    public required double HalfLife { get; init; }

    public required double Lambda { get; init; }

    public required double Intercept { get; init; }

    public required double StandardError { get; init; }

    public required double RSquared { get; init; }

    public required double Confidence { get; init; }

    public required int SampleSize { get; init; }

    public required bool IsValid { get; init; }

    public required string Explanation { get; init; }

    public static HalfLifeResult Invalid(string reason, int sampleSize = 0) => new()
    {
        HalfLife = 0.0,
        Lambda = 0.0,
        Intercept = 0.0,
        StandardError = 0.0,
        RSquared = 0.0,
        Confidence = 0.0,
        SampleSize = sampleSize,
        IsValid = false,
        Explanation = reason
    };
}