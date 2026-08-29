using System;

namespace IQIAIndicator.Backtest.Execution;

/// <summary>
/// Sprint 15.25 (Lot 14.5, brief §10/§29). Explicit configuration for <see cref="ExecutionSimulator"/> -
/// never a hard-coded 10 inside the TIME_HORIZON formula. Mirrors
/// <see cref="Backtest.Measurement.MeasurementConfiguration"/>'s shape (minus HitThresholds, which have
/// no meaning for a single theoretical exit) - deliberately NOT the same type, since Execution and
/// Measurement must stay architecturally separate (brief §4).
/// </summary>
public sealed record ExecutionConfiguration
{
    /// <summary>Bars held before the TIME_HORIZON exit: a position opened at bar i closes at bar
    /// i+HorizonBars (brief §10). The only exit rule this lot implements - see
    /// <see cref="ExitReason"/>'s doc comment.</summary>
    public required int HorizonBars { get; init; }

    public static ExecutionConfiguration Create(int horizonBars)
    {
        if (horizonBars <= 0)
            throw new ArgumentOutOfRangeException(nameof(horizonBars), horizonBars, "HorizonBars must be strictly positive.");

        return new ExecutionConfiguration { HorizonBars = horizonBars };
    }
}
