using System.Collections.Generic;

namespace IQIAIndicator.Backtest.Measurement;

/// <summary>
/// Sprint 15.25 (Lot 14.4, brief §39). Minimal pairing of a Lot 14.3 signal run with its Lot 14.4
/// measurements - NOT a merged/re-shaped object. <see cref="SignalResult"/> is exactly what
/// <see cref="BacktestEngine.RunSignalPipeline"/> already produces, untouched; <see cref="Measurements"/>
/// is exactly what <see cref="ScientificMeasurementEngine.MeasureAll"/> produces from it, in the same bar
/// order. Keeping them as two parallel lists (rather than embedding a MeasurementResult inside
/// BacktestSignalResult) is what makes the signal/measurement separation explicit in the architecture,
/// not just in behaviour (brief §2: "Cette séparation doit être explicite dans l'architecture").
/// </summary>
/// <param name="DeterministicHash">SHA-256 of every measurement, in order (brief §29) - see
/// <see cref="MeasurementFingerprint.ComputeHash"/>. Independent of
/// <see cref="BacktestSignalPipelineResult.DeterministicHash"/> (Lot 14.3, still separately available on
/// <see cref="SignalResult"/>) - two distinct hashes for two distinct concerns, never merged into one.</param>
public sealed record BacktestMeasuredSignalPipelineResult(
    BacktestSignalPipelineResult SignalResult,
    IReadOnlyList<MeasurementResult> Measurements,
    string DeterministicHash);
