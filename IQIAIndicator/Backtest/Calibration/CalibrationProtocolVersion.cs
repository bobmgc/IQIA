namespace IQIAIndicator.Backtest.Calibration;

/// <summary>
/// Sprint 15.25 (Lot 14.9, brief §21). Explicit protocol version for the calibration framework's
/// fingerprinting scheme - never derived from <see cref="System.DateTime"/> (brief §21: "Ne pas utiliser
/// DateTime pour identifier la version"). Bump this value whenever <see cref="CalibrationFingerprint"/>'s
/// canonicalization changes in a way that would otherwise silently change every existing
/// ConfigurationFingerprint/ExperimentId without the change being visible anywhere.
/// </summary>
public static class CalibrationProtocolVersion
{
    public const string Current = "14.9.1";
}
