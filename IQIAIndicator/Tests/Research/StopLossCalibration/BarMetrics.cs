namespace IQIAIndicator.Tests.Research.StopLossCalibration;

/// <summary>
/// Sprint 15.10. Snapshot of every metric Sprint 15.9 identified as a Stop Loss candidate input,
/// computed at a single bar T using ONLY price data up to and including T (see BarMetricsComputer -
/// look-ahead safety is enforced there; this record is only the output shape).
///
/// EquilibriumDistance replaces both DistanceToEquilibrium (VolatilityModel) and
/// ExpectedReversionDistance (DynamicZScoreModel): Sprint 15.9's audit found these are the exact same
/// formula (abs(CurrentPrice - EstimatedEquilibrium)) computed twice under two names. Per Sprint 15.10
/// instruction, the harness treats them as one variable, not two independent factors.
/// </summary>
public sealed record BarMetrics(
    int BarIndex,
    double Price,
    bool ModelsValid,
    double? EstimatedEquilibrium,
    double? InnovationStd,
    double? DynamicZScore,
    double? CurrentVolatility,
    string? VolatilityRegime,
    double? VolatilityPercentile,
    double? EquilibriumDistance,
    bool HalfLifeValid,
    double? HalfLife,
    double? HalfLifeRSquared);
