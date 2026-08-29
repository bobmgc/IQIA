namespace IQIAIndicator.Tests.Research.StopLossCalibration.Hybrid;

/// <summary>
/// Sprint 15.13. One row of the Hybrid campaign grid: a single (candidate, dataset, split, scale, k)
/// combination aggregated over every entry it applies to. Mirrors CandidateAggregateResult's shape
/// (Sprint 15.10) but keyed on HybridCandidateKind and carrying the dataset-independence label (brief
/// §3: IndependentDataset/AliasOf) plus the three regime-routing share metrics (brief §10, populated
/// only for HybridStrict/HybridConservative rows - null for plain A1/A2 rows).
///
/// Deliberately omits FalseInvalidationRate/TrueInvalidationRate: not in this sprint's required
/// measures list (brief §10) and EXPLORATORY ONLY per QDE-012 protocol §17 regardless of sprint.
/// </summary>
public sealed record HybridAggregateResult(
    string Candidate,
    string Dataset,
    bool IndependentDataset,
    string AliasOf,
    string Split,
    decimal Scale,
    double K,
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
    double MeanStopRatio,
    double? HybridA1Share,
    double? HybridA2Share,
    double? HybridUnknownShare);
