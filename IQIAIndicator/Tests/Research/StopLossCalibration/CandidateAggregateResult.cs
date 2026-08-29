namespace IQIAIndicator.Tests.Research.StopLossCalibration;

/// <summary>Sprint 15.10 (QDE-012 Phase 11/12). One row of the campaign grid: a single
/// (candidate, dataset, split, scale, k[, R-squared threshold]) combination, aggregated over every
/// entry to which that combination applies.</summary>
public sealed record CandidateAggregateResult(
    string Candidate,
    string Dataset,
    string Split,
    decimal Scale,
    double K,
    double? RSquaredThreshold,
    int TotalEntries,
    int ApplicableEntries,
    int DegenerateEntries,
    int StopHits,
    int StoppedOut,
    int Reverted,
    int Undetermined,
    double StopHitRate,
    double ReversionRate,
    double UndeterminedRate,
    double StopHitBeforeEquilibriumRate,
    double FalseInvalidationRate,
    double TrueInvalidationRate,
    double MaeMean,
    double MaeMedian,
    double MaeP75,
    double MaeP90,
    double MfeMean,
    double MfeMedian,
    double MfeP75,
    double MfeP90,
    double? MedianTimeToEquilibrium,
    double MeanStopDistance,
    double MeanStopRatio);
