namespace IQIAIndicator.Tests.Research.StopLossCalibration.Hybrid;

/// <summary>
/// Sprint 15.13. Local to this sprint's research harness - does not touch the existing CandidateKind
/// enum (A1/A2/D1/D2) in CandidateStopDistance.cs. HybridStrict/HybridConservative are the two
/// regime-conditional variants defined by the brief (§7): they differ only in how they treat the
/// UNKNOWN VolatilityRegime case.
/// </summary>
public enum HybridCandidateKind
{
    A1,
    A2,
    HybridStrict,
    HybridConservative
}
