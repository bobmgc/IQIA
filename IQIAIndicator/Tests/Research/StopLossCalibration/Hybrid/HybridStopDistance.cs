using System;

namespace IQIAIndicator.Tests.Research.StopLossCalibration.Hybrid;

/// <summary>
/// Sprint 15.13. Which regime-routing bucket an entry fell into. Distinct from the sigma source itself
/// so HybridConservative's UNKNOWN-&gt;A1 fallback can still be told apart from a genuine LOW/MEDIUM-&gt;A1
/// routing in the HybridA1Share/HybridUnknownShare descriptive metrics (brief §10/§14).
/// </summary>
public enum HybridLeg
{
    A1,
    A2,
    Unknown
}

/// <summary>
/// Sprint 15.13 (brief §6/§7). THE experimental definition under test this sprint - everything else in
/// the harness measures this. A1/A2 legs delegate straight to CandidateStopDistance.Compute (the
/// existing, already-validated Sprint 15.10 formula) - this class adds only the VolatilityRegime
/// routing on top, it never reimplements the sigma math itself.
///
/// Locked rule (brief's pre-campaign gate - not modified after observing any TRAIN/VALIDATION/TEST
/// result):
///   HybridStrict:        HIGH -&gt; A2, LOW -&gt; A1, MEDIUM -&gt; A1, UNKNOWN -&gt; NOT_APPLICABLE
///   HybridConservative:   HIGH -&gt; A2, LOW -&gt; A1, MEDIUM -&gt; A1, UNKNOWN -&gt; A1
/// No other rule is implemented.
/// </summary>
public static class HybridStopDistance
{
    public static (double? StopDistance, HybridLeg Leg) Compute(HybridCandidateKind kind, CalibrationEntry entry, double k)
    {
        switch (kind)
        {
            case HybridCandidateKind.A1:
                return (CandidateStopDistance.Compute(CandidateKind.A1, entry, k), HybridLeg.A1);

            case HybridCandidateKind.A2:
                return (CandidateStopDistance.Compute(CandidateKind.A2, entry, k), HybridLeg.A2);

            case HybridCandidateKind.HybridStrict:
            case HybridCandidateKind.HybridConservative:
                return RouteByRegime(kind, entry, k);

            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    private static (double?, HybridLeg) RouteByRegime(HybridCandidateKind kind, CalibrationEntry entry, double k)
    {
        string? regime = entry.Metrics.VolatilityRegime;

        switch (regime)
        {
            case "HIGH":
                return (CandidateStopDistance.Compute(CandidateKind.A2, entry, k), HybridLeg.A2);

            case "LOW":
            case "MEDIUM":
                return (CandidateStopDistance.Compute(CandidateKind.A1, entry, k), HybridLeg.A1);

            default: // "UNKNOWN" or null - treated identically, both mean "no usable regime signal"
                if (kind == HybridCandidateKind.HybridConservative)
                {
                    return (CandidateStopDistance.Compute(CandidateKind.A1, entry, k), HybridLeg.Unknown);
                }

                return (null, HybridLeg.Unknown); // HybridStrict: NOT_APPLICABLE, degenerate for this entry
        }
    }
}
