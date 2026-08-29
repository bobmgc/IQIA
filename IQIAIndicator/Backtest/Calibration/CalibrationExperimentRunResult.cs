using System.Collections.Generic;

namespace IQIAIndicator.Backtest.Calibration;

/// <summary>
/// Sprint 15.25 (Lot 14.9). One <see cref="CalibrationExperiment"/>'s complete set of
/// <see cref="CalibrationExperimentResult"/> (TRAIN/VALIDATION/OOS x ALL/BUY/SELL, brief §23 - up to nine
/// entries, fewer only when a window/direction combination has no candidate positions at all and is still
/// represented with <see cref="CalibrationExperimentResultStatus.NoData"/> rather than omitted).
/// </summary>
public sealed record CalibrationExperimentRunResult(
    string ExperimentId,
    string ConfigurationFingerprint,
    IReadOnlyList<CalibrationExperimentResult> Results);
