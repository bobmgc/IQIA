namespace IQIAIndicator.Backtest;

/// <summary>
/// Sprint 15.25 (Lot 14.10, P0-3). Explicit, typed set of production pipeline parameters a caller may
/// override for one <see cref="BacktestEngine"/> call - the ONLY mechanism by which a
/// <c>Backtest.Calibration.CalibrationParameterSet</c> can ever change pipeline BEHAVIOUR (see
/// <c>Backtest.Calibration.CalibrationParameterBinding</c>), never via reflection, a global/static
/// mutable, or a service locator.
///
/// Every field is nullable and means "use the production default" when null - <see cref="None"/> is
/// exactly that for every field at once, and passing it reproduces every pre-Lot-14.10
/// <see cref="BacktestEngine"/> call bit-for-bit (brief's "RÈGLE DE NON-RÉGRESSION").
///
/// Adding a new bindable parameter to a future lot means adding one more nullable field here plus one more
/// explicit resolution branch in <c>CalibrationParameterBinding</c> - never a dynamic/reflective lookup by
/// name.
/// </summary>
public sealed record PipelineParameterOverrides
{
    /// <summary>Overrides <c>Engine.EntryTrigger.EntryTriggerBuilder.AmbiguityGateThreshold</c> (production
    /// default: 0.95) for this one call only. Null (the default) means "use the production constant" -
    /// see <see cref="Engine.EntryTrigger.EntryTriggerBuilder"/>.</summary>
    public double? AmbiguityGateThreshold { get; init; }

    /// <summary>No override for anything - every pipeline parameter falls back to its production default.
    /// Use this constant (never a bare <c>new PipelineParameterOverrides()</c> scattered across call sites)
    /// so every non-calibration caller shares the exact same, obviously-named "no-op" instance.</summary>
    public static readonly PipelineParameterOverrides None = new();
}
