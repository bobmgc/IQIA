using System.Collections.Immutable;

namespace IQIAIndicator.Engine.Fusion.Core;

/// <summary>
/// Point unique de construction d'un résultat de fusion immuable.
/// </summary>
public sealed class FusionResultBuilder
{
    public Dictionary<FusionDimension, FusionConfidence> Dimensions { get; } = new();

    public FusionResult Build() => new(ImmutableDictionary.CreateRange(Dimensions));
}