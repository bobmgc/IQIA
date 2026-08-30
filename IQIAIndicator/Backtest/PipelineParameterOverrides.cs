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

    /// <summary>
    /// Sprint 15.25 (Lot 18 - QDE-012 StructuralBreak cost study, RESEARCH-ONLY). When <c>true</c>, the
    /// <see cref="BacktestEngine.RunSignalPipeline(BacktestScenario, int, PipelineParameterOverrides)"/>
    /// call omits <c>Engine.Decision.Rules.StructuralBreakRule</c> from the DecisionEngine rule list for
    /// this one call - the other four rules and their order are UNCHANGED. Exists only so the Lot 17/18
    /// audit can measure the end-to-end economic effect of that regime rule (which currently routes ~31%
    /// of bars to NO_ACTION / UNSUPPORTED_REGIME) by running the exact production pipeline with and
    /// without it, on the same dataset. Null / <c>false</c> (the default, and every non-research caller)
    /// means "use the production 5-rule list" - bit-for-bit unchanged (brief's "RÈGLE DE NON-RÉGRESSION").
    /// It is NOT a calibration parameter: <c>Backtest.Calibration.CalibrationParameterBinding</c> never
    /// sets it, so no <c>CalibrationParameterSet</c> can reach it.
    /// </summary>
    public bool? AblateStructuralBreakRegimeRule { get; init; }

    // Audit 2026-08-30 (P0-2 Trending calibration). Four trend-following parameters, each null = "use
    // the production default". These reach the pipeline through the same single seam as
    // AmbiguityGateThreshold: BacktestEngine.RunSignalPipeline(scenario, warmup, overrides) threads
    // them into the SignalEngine constructor (MomentumLookbacks/MinMomentumConfidence) and into its own
    // TradePlan-stage wiring (StopVolatilityMultiplier/TakeProfitRMultiple) - never a static mutable.

    /// <summary>Overrides <c>TimeSeriesMomentumModel</c>'s multi-horizon lookback set (production
    /// default: {12, 36, 72, 144} bars). Null or empty = production default.</summary>
    public int[]? MomentumLookbacks { get; init; }

    /// <summary>Overrides <c>EntryTriggerBuilder.DefaultMinMomentumConfidence</c> (0.10) - the floor a
    /// trending bar's momentum confidence must clear before a BUY/SELL is emitted. Null = default.</summary>
    public double? MinMomentumConfidence { get; init; }

    /// <summary>Overrides <c>VolatilityStopLossModel.DefaultVolatilityMultiplier</c> (2.0) for this
    /// call's TradePlan stop-loss distance (Entry ∓ multiplier × CurrentVolatility). Null = default.</summary>
    public double? StopVolatilityMultiplier { get; init; }

    /// <summary>Overrides the R-multiple TakeProfit applied to a trending trade (production default:
    /// 2.0 × stop distance). Null = default. Ignored for mean-reversion trades (they use the
    /// equilibrium target).</summary>
    public double? TakeProfitRMultiple { get; init; }

    /// <summary>No override for anything - every pipeline parameter falls back to its production default.
    /// Use this constant (never a bare <c>new PipelineParameterOverrides()</c> scattered across call sites)
    /// so every non-calibration caller shares the exact same, obviously-named "no-op" instance.</summary>
    public static readonly PipelineParameterOverrides None = new();
}
