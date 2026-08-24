namespace IQIAIndicator.Backtest.Calibration;

/// <summary>
/// Sprint 15.25 (Lot 14.9, brief §22 "status"). Never a silently-empty result: a window/direction slice
/// that produced zero signals is explicitly labelled <see cref="NoData"/>, not a zeroed-out
/// <see cref="Succeeded"/> result indistinguishable from "we measured zero and it was zero".
/// </summary>
public enum CalibrationExperimentResultStatus
{
    /// <summary>At least one signal fell in this window/direction slice.</summary>
    Succeeded,

    /// <summary>No signal at all fell in this window/direction slice - every metric is null, not zero.</summary>
    NoData
}
