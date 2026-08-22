using System;
using System.Collections.Generic;
using System.Linq;

namespace IQIAIndicator.Backtest.Measurement;

/// <summary>
/// Sprint 15.25 (Lot 14.4, brief §4/§14). Explicit configuration for
/// <see cref="ScientificMeasurementEngine"/> - never a hard-coded 10/20/0.001 inside the formulas
/// themselves (brief §4: "Ne pas coder 10 ou 20 en dur dans les formules").
/// </summary>
public sealed record MeasurementConfiguration
{
    /// <summary>Number of future bars measured after the signal bar: for a signal at index i, bars
    /// i+1..i+HorizonBars. Never i itself (brief §8).</summary>
    public required int HorizonBars { get; init; }

    /// <summary>Hit thresholds, kept in DECIMAL form throughout the engine (0.001, never 0.1% or
    /// 0.001*100 - brief §15). Presentation-layer percentage formatting belongs outside this type.</summary>
    public required IReadOnlyList<double> HitThresholds { get; init; }

    /// <summary>Validates and builds. Throws rather than silently accepting a configuration that could
    /// never produce a meaningful measurement (brief §4/§14: thresholds/horizon are inputs, never
    /// invented defaults).</summary>
    public static MeasurementConfiguration Create(int horizonBars, IReadOnlyList<double> hitThresholds)
    {
        if (horizonBars <= 0)
            throw new ArgumentOutOfRangeException(nameof(horizonBars), horizonBars, "HorizonBars must be strictly positive.");

        ArgumentNullException.ThrowIfNull(hitThresholds);

        foreach (double threshold in hitThresholds)
        {
            if (!double.IsFinite(threshold) || threshold <= 0.0)
                throw new ArgumentException($"Every HitThreshold must be finite and strictly positive. Found {threshold}.", nameof(hitThresholds));
        }

        return new MeasurementConfiguration { HorizonBars = horizonBars, HitThresholds = hitThresholds.ToArray() };
    }
}
