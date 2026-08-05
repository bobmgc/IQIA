namespace IQIAIndicator.Engine.Fusion.Core;

/// <summary>
/// Confiance associée à une dimension scientifique de fusion.
/// </summary>
public sealed record FusionConfidence
{
    public double Value { get; init; } = 0.0;

    public string Explanation { get; init; } = string.Empty;
}