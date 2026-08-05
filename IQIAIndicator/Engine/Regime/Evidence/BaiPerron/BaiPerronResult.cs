namespace IQIAIndicator.Engine.Regime.Evidence.BaiPerron;

/// <summary>
/// Observations statistiques issues de la segmentation multiple Bai-Perron.
/// </summary>
public sealed record BaiPerronResult
{
    public required IReadOnlyList<int> Breakpoints { get; init; }

    public required int BreakCount { get; init; }

    public required double Confidence { get; init; }

    public required double GlobalRSS { get; init; }

    public required double BicScore { get; init; }

    public required int SampleSize { get; init; }

    public required bool IsValid { get; init; }

    public required string Explanation { get; init; }

    public static BaiPerronResult Invalid(string reason, int sampleSize = 0) => new()
    {
        Breakpoints = [],
        BreakCount = 0,
        Confidence = 0.0,
        GlobalRSS = 0.0,
        BicScore = 0.0,
        SampleSize = sampleSize,
        IsValid = false,
        Explanation = reason
    };
}