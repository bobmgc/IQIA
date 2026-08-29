namespace IQIAIndicator.Backtest.Calibration;

/// <summary>
/// Sprint 15.25 (Lot 14.9, brief §11). The three roles a <see cref="CalibrationWindow"/> can play. Unlike
/// <see cref="BacktestWindow.Name"/> (free text, Lot 14.1 brief §9), these are enforced constants: the
/// ordering/look-ahead rules of brief §12/§17 need a closed, comparable set to validate against, not
/// arbitrary caller-supplied strings.
/// </summary>
public enum CalibrationWindowRole
{
    Train,
    Validation,
    Oos
}
