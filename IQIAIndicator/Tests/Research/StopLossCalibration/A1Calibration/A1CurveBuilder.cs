using System.Collections.Generic;
using System.Linq;
using IQIAIndicator.Tests.Research.StopLossCalibration.Hybrid;

namespace IQIAIndicator.Tests.Research.StopLossCalibration.A1Calibration;

/// <summary>
/// Sprint 15.14. Extracts a k-ordered curve for one "scope" (an independent dataset name, or the
/// synthetic "POOLED" scope built by A1PooledCurve) at a given split/scale. Centralized here so
/// discovery/validation/test/curve-shape analyses all read the exact same curve for the exact same
/// scope - no risk of the pooling or filtering logic drifting between phases.
/// </summary>
public static class A1CurveBuilder
{
    public static List<CandidateAggregateResult> GetCurve(IReadOnlyList<CandidateAggregateResult> allRows, string scope, string split, decimal scale)
    {
        if (scope == A1PooledCurve.PooledScope)
        {
            List<CandidateAggregateResult> filtered = allRows
                .Where(r => r.Split == split && r.Scale == scale && IndependentDatasetCatalog.IsIndependent(r.Dataset))
                .ToList();
            return A1PooledCurve.Build(filtered).ToList();
        }

        return allRows
            .Where(r => r.Dataset == scope && r.Split == split && r.Scale == scale)
            .OrderBy(r => r.K)
            .ToList();
    }

    /// <summary>Same formula as StableRegionAnalyzer's private CoefficientOfVariation - re-declared here
    /// rather than modifying that file's visibility, to keep this sprint's diff purely additive.</summary>
    public static double CoefficientOfVariation(IEnumerable<double> values)
    {
        List<double> list = values.ToList();
        double mean = list.Average();
        if (System.Math.Abs(mean) < 1e-9)
        {
            return list.All(v => System.Math.Abs(v) < 1e-9) ? 0.0 : double.PositiveInfinity;
        }

        double variance = list.Sum(v => (v - mean) * (v - mean)) / list.Count;
        return System.Math.Sqrt(variance) / System.Math.Abs(mean);
    }
}
