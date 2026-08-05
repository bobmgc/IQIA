using System.Collections.Immutable;

namespace IQIAIndicator.Engine.Fusion.Core;

/// <summary>
/// Options prévues pour les futures règles et calibrations de fusion.
/// </summary>
public sealed record FusionConfiguration
{
    public ImmutableDictionary<FusionDimension, double> Weights { get; init; } =
        ImmutableDictionary<FusionDimension, double>.Empty;

    public ImmutableDictionary<string, double> Thresholds { get; init; } =
        ImmutableDictionary<string, double>.Empty;

    public bool EnableCalibration { get; init; } = false;
}