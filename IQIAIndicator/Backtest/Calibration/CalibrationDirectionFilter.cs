namespace IQIAIndicator.Backtest.Calibration;

/// <summary>
/// Sprint 15.25 (Lot 14.9, brief §23). The per-direction breakdown every <see cref="CalibrationExperimentResult"/>
/// must preserve - never collapsed into a single ALL number that hides BUY/SELL asymmetry.
/// </summary>
public enum CalibrationDirectionFilter
{
    All,
    Buy,
    Sell
}
