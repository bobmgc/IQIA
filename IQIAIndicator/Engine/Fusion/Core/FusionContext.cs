using IQIAIndicator.Engine.Regime.Core;

namespace IQIAIndicator.Engine.Fusion.Core;

/// <summary>
/// Contexte immuable transmis aux futures règles de fusion.
/// </summary>
public sealed record FusionContext
{
    public required EvidenceSet Evidence { get; init; }

    public required DateTime Timestamp { get; init; }

    public required string Symbol { get; init; }

    public required string TimeFrame { get; init; }

    public Guid EvaluationId { get; init; }
}