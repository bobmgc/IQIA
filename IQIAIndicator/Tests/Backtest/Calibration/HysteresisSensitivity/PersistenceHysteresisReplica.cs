namespace IQIAIndicator.Tests.BacktestTests.Calibration.HysteresisSensitivity;

/// <summary>
/// Sprint 15.25 (Lot 14.18). TEST-ONLY, LOCAL-TO-EXPERIMENT replica of
/// <c>Engine.Fusion.State.FusionStateManager</c>'s EMA+hysteresis recursion, isolated to a single scalar
/// series (used here exclusively for the Persistence dimension's Value).
///
/// WHY THIS CLASS EXISTS (see Lot 14.17 report §27): <c>FusionStateManager</c>'s
/// <c>StabilizationConfiguration</c> is a private nested record hard-bound to
/// <c>StabilizationConfiguration.Default</c> (Alpha=0.20, HysteresisThreshold=0.03) - there is no
/// constructor seam to inject a different threshold, and the brief (§26/§29) forbids adding one to the
/// production class ("no override without STOP+document if it requires touching protected production
/// logic") and forbids reflection/global state. This replica reproduces the EXACT recursion verbatim
/// (<c>FusionStateManager.Smooth</c> / <c>ClampScore</c> / the <c>&gt;=</c> hysteresis comparison /
/// the bit-identical carry-forward on freeze) as a plain, stateless-between-instances local object -
/// never referenced by any production code path.
///
/// FIDELITY: <c>HysteresisThresholdSensitivitySyntheticTests.ThresholdBindingMatchesRealFusionStateManager</c>
/// proves this replica produces BIT-IDENTICAL Persistence Value output to the real
/// <c>FusionStateManager</c> when constructed with the production Alpha/HysteresisThreshold pair, on the
/// same raw input sequence - so results collected via this replica at threshold=0.03 are traceable back to
/// production behaviour, not merely "similar".
///
/// Only <see cref="Update"/>'s Value recursion is replicated (Confidence/IsAvailable/Explanation carry no
/// role in this study - brief §5 studies Persistence's Value freeze only).
/// </summary>
public sealed class PersistenceHysteresisReplica
{
    private readonly double _alpha;
    private readonly double _hysteresisThreshold;
    private double? _previousStableValue;

    /// <param name="hysteresisThreshold">The EXPERIMENT OVERRIDE under study (brief §2). Production value is
    /// 0.03 - never mutated anywhere else by this study.</param>
    /// <param name="alpha">Fixed at production's 0.20 by every caller in this Lot (brief §20/§21: only
    /// HysteresisThreshold is isolated as a variable). Exposed as a parameter only so synthetic tests can
    /// exercise the recursion directly without depending on a magic constant.</param>
    public PersistenceHysteresisReplica(double hysteresisThreshold, double alpha = 0.20)
    {
        _alpha = alpha;
        _hysteresisThreshold = hysteresisThreshold;
    }

    /// <summary>
    /// Feeds one bar's RAW value through the EMA+hysteresis recursion and returns the new STABLE value.
    /// Mirrors <c>FusionStateManager.BuildInitialStableResult</c> (first call: stable = raw, no smoothing)
    /// and <c>FusionStateManager.BuildStableResult</c> (subsequent calls) verbatim.
    /// </summary>
    public double Update(double rawValue)
    {
        double clampedRaw = ClampScore(rawValue);

        if (_previousStableValue is null)
        {
            _previousStableValue = clampedRaw;
            return clampedRaw;
        }

        double smoothedValue = Smooth(clampedRaw, _previousStableValue.Value);
        bool valueChanged = Math.Abs(smoothedValue - _previousStableValue.Value) >= _hysteresisThreshold;
        double newStableValue = valueChanged ? smoothedValue : _previousStableValue.Value;

        _previousStableValue = newStableValue;
        return newStableValue;
    }

    private double Smooth(double newValue, double previousValue) =>
        _alpha * ClampScore(newValue) + (1.0 - _alpha) * ClampScore(previousValue);

    private static double ClampScore(double value) => Math.Clamp(value, 0.0, 1.0);
}
