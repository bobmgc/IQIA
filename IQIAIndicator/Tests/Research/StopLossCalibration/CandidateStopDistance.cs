using System;

namespace IQIAIndicator.Tests.Research.StopLossCalibration;

public enum CandidateKind
{
    A1,
    A2,
    D1,
    D2
}

/// <summary>
/// Sprint 15.10. Stop-distance formulas for each candidate, all measured from ENTRY PRICE (the
/// distance a StopLossEvaluator needs), computed from entry-time-only BarMetrics.
///
/// Correction to Sprint 15.9's chat report: that report characterized Candidate D's fully-worked-out
/// formula as "collapsing to the same formula as Candidate A1." That was only true under the
/// simplifying assumption that entry happens exactly at equilibrium. It does not: D anchors its
/// invalidation boundary to EQUILIBRIUM (EstimatedMean +/- k*InnovationStd), not to entry price, and a
/// real entry already has its own non-zero z-score (that's what makes it a signal at all). Once that
/// is accounted for, D1's distance-from-entry is (k - |z0|) * InnovationStd - genuinely different from
/// A1's k * InnovationStd, and undefined (degenerate) whenever k &lt;= |z0| (the "invalidation" level
/// would sit on the wrong side of, or exactly at, entry). This file implements the corrected version.
/// </summary>
public static class CandidateStopDistance
{
    /// <summary>Returns null when the candidate is not applicable to this entry: missing sigma, D2's
    /// R^2 filter rejecting the entry, or D1/D2's degenerate case (k &lt;= |entry z-score|).</summary>
    public static double? Compute(CandidateKind kind, CalibrationEntry entry, double k, double? rSquaredThreshold = null)
    {
        BarMetrics m = entry.Metrics;

        switch (kind)
        {
            case CandidateKind.A1:
                return m.InnovationStd is double std && std > 0.0 ? k * std : null;

            case CandidateKind.A2:
                return m.CurrentVolatility is double vol && vol > 0.0 ? k * vol : null;

            case CandidateKind.D1:
            case CandidateKind.D2:
                if (kind == CandidateKind.D2)
                {
                    if (rSquaredThreshold is null)
                    {
                        throw new ArgumentException("D2 requires an R-squared threshold.", nameof(rSquaredThreshold));
                    }

                    if (!m.HalfLifeValid || m.HalfLifeRSquared is not double r2 || r2 < rSquaredThreshold.Value)
                    {
                        return null;
                    }
                }

                if (m.InnovationStd is not double innovationStd || innovationStd <= 0.0 || m.DynamicZScore is not double z0)
                {
                    return null;
                }

                double absZ0 = Math.Abs(z0);
                if (k <= absZ0)
                {
                    return null; // degenerate: boundary not beyond entry's own deviation from equilibrium
                }

                return (k - absZ0) * innovationStd;

            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }
}
