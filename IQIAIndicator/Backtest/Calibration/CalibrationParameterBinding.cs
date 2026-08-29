using System;

namespace IQIAIndicator.Backtest.Calibration;

/// <summary>
/// Sprint 15.25 (Lot 14.10, P0-3). The ONLY place a <see cref="CalibrationParameterSet"/> is ever
/// translated into pipeline BEHAVIOUR (a <see cref="PipelineParameterOverrides"/>). Explicit and typed:
/// each bindable parameter is read by its own exact, documented name/type from
/// <see cref="CalibrationParameterSet.TryGet"/>, never by iterating properties dynamically, never by
/// reflection, never through a service locator or shared mutable state - see the Lot 14.10 report §5 for
/// why that constraint matters (a grid axis must produce a real, observable, per-experiment behavioural
/// difference, never merely a different <see cref="CalibrationExperiment.ConfigurationFingerprint"/>).
///
/// BINDABLE TODAY: <see cref="AmbiguityGateThresholdParameterName"/> (Decimal, unit "score") -&gt;
/// <c>Engine.EntryTrigger.EntryTriggerBuilder</c>'s ambiguity gate, via
/// <c>Engine.Signal.SignalEngine</c> -&gt; <c>Engine.EntryTrigger.EntryTriggerEngine</c> -&gt;
/// <c>Engine.EntryTrigger.EntryTriggerBuilder</c> (see
/// <see cref="Backtest.BacktestEngine.RunSignalPipeline(BacktestScenario, int, PipelineParameterOverrides)"/>).
/// Absent from the set (or the set is <see cref="CalibrationParameterSet.Empty"/>) -&gt; a null override on
/// <see cref="PipelineParameterOverrides.AmbiguityGateThreshold"/> -&gt; <see cref="BacktestEngine"/> falls
/// back to the unchanged production constant (0.95).
///
/// NOT BINDABLE YET (documented explicitly, per the Lot 14.10 brief's own instruction, rather than a
/// silent gap or an ad hoc hack): every OTHER production constant a future calibration might want to sweep
/// (Regime window sizes, Fusion/Decision rule weights, Entry/EntryTrigger READY thresholds, StopLoss/Risk
/// parameters) has no corresponding field on <see cref="PipelineParameterOverrides"/> and is therefore
/// silently ignored if a caller puts a <see cref="CalibrationParameter"/> with that name into a
/// <see cref="CalibrationParameterSet"/> - the field would still be part of the experiment's
/// <see cref="CalibrationExperiment.ConfigurationFingerprint"/> (so two such experiments would still get
/// different fingerprints/ids) but would produce IDENTICAL pipeline behaviour, exactly the failure mode the
/// Lot 14.9 audit (Defect D4) warned against. Building the wiring for any of those parameters is out of
/// scope for this lot (see the Lot 14.10 report §8 "NOT BINDABLE YET").
///
/// Measurement/Execution/PnL/Cost/Risk configuration are NOT candidates for this binding at all - they are
/// not swept per-experiment through a <see cref="CalibrationParameterSet"/>/<see cref="CalibrationGrid"/>
/// axis; they are supplied directly, once per grid, via <see cref="CalibrationExperimentSetup"/>'s own
/// already-existing configuration objects, which <see cref="CalibrationExperimentRunner"/> already passes
/// to <see cref="BacktestEngine"/> unmodified.
/// </summary>
public static class CalibrationParameterBinding
{
    public const string AmbiguityGateThresholdParameterName = "AmbiguityGateThreshold";

    public static PipelineParameterOverrides Resolve(CalibrationParameterSet parameterSet)
    {
        ArgumentNullException.ThrowIfNull(parameterSet);

        CalibrationParameter? ambiguityGateThreshold = parameterSet.TryGet(AmbiguityGateThresholdParameterName);
        double? ambiguityGateThresholdValue = ambiguityGateThreshold?.DecimalValue is decimal d ? (double)d : null;

        return new PipelineParameterOverrides { AmbiguityGateThreshold = ambiguityGateThresholdValue };
    }
}
