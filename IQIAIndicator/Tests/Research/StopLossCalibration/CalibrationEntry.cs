namespace IQIAIndicator.Tests.Research.StopLossCalibration;

/// <summary>Sprint 15.10. One signal-eligible bar in a series, paired with its (past-only) entry
/// metrics and its (future-only) outcome. See CalibrationEntryBuilder for eligibility rules.</summary>
public sealed record CalibrationEntry(
    string SeriesName,
    int EntryBarIndex,
    SignalDirection Direction,
    BarMetrics Metrics,
    OutcomeMeasurement Outcome);
