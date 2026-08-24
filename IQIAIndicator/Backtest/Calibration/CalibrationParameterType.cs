namespace IQIAIndicator.Backtest.Calibration;

/// <summary>
/// Sprint 15.25 (Lot 14.9, brief §39). The only value kinds a <see cref="CalibrationParameter"/> may
/// carry - real, typed values, never a bare string-encoded catch-all (brief §39: "Éviter de stocker tout
/// en string si le typage peut être conservé").
/// </summary>
public enum CalibrationParameterType
{
    Integer,
    Decimal,
    Boolean,
    Enum
}
