using System.Collections.Immutable;

namespace IQIAIndicator.Engine.Fusion.Core;

/// <summary>
/// Résultat immuable construit exclusivement par FusionResultBuilder.
/// </summary>
public sealed record FusionResult
{
    internal FusionResult(ImmutableDictionary<FusionDimension, FusionConfidence> dimensions)
    {
        Dimensions = dimensions;
    }

    public ImmutableDictionary<FusionDimension, FusionConfidence> Dimensions { get; init; }
}