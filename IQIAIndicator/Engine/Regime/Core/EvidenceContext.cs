namespace IQIAIndicator.Engine.Regime.Core;

/// <summary>
/// Données statistiques indépendantes de la plateforme fournies aux modèles d'évidence.
/// </summary>
public sealed record EvidenceContext
{
    public required IReadOnlyList<decimal> Series { get; init; }

    public required int SampleSize { get; init; }

    public required int MinimumSampleSize { get; init; }

    public required int WindowSize { get; init; }

    public required DateTime Timestamp { get; init; }

    public string? Symbol { get; init; }

    public string? TimeFrame { get; init; }
}