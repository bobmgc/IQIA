using System.Collections.Generic;
using System.Globalization;
using System.Text;
using IQIAIndicator.Backtest;

namespace IQIAIndicator.Backtest.Measurement;

/// <summary>
/// Sprint 15.25 (Lot 14.4, brief §29). Deterministic, wall-clock-free canonicalization of a
/// <see cref="MeasurementResult"/> population - same reuse strategy as Lot 14.3's
/// <c>BacktestSignalFingerprint</c>: lives alongside (not inside) <see cref="BacktestFingerprint"/>
/// (Lot 14.1) and reuses its one public primitive, <see cref="BacktestFingerprint.Sha256Hex"/>, without
/// modifying that file.
///
/// Unlike Lot 14.3's fingerprint, nothing here needs to be EXCLUDED for determinism -
/// <see cref="MeasurementResult"/> carries no wall-clock field at all (see its own doc comment), so every
/// field below can be hashed directly.
/// </summary>
internal static class MeasurementFingerprint
{
    /// <summary>Canonicalizes every measurement, in order, into one SHA-256 hash - the sole public
    /// primitive callers (e.g. <see cref="BacktestEngine.RunMeasuredSignalPipeline"/>) need.</summary>
    public static string ComputeHash(IReadOnlyList<MeasurementResult> measurements)
    {
        var accumulator = new StringBuilder();
        foreach (MeasurementResult result in measurements)
            AppendMeasurement(accumulator, result);

        return BacktestFingerprint.Sha256Hex(accumulator.ToString());
    }

    public static void AppendMeasurement(StringBuilder accumulator, MeasurementResult result)
    {
        accumulator.Append("Measurement[").Append(result.SignalBarIndex).Append(']')
            .Append(" ts=").Append(Dt(result.SignalTimestamp))
            .Append(" dir=").Append(result.Direction)
            .Append(" entry=").Append(N(result.EntryPrice))
            .Append(" status=").Append(result.Status)
            .Append(" horizon=").Append(result.HorizonBars)
            .Append(" return=").Append(N(result.Return))
            .Append(" mfe=").Append(N(result.Mfe))
            .Append(" mae=").Append(N(result.Mae))
            .Append(" hits=[");

        for (int i = 0; i < result.HitResults.Count; i++)
        {
            if (i > 0)
                accumulator.Append(',');

            MeasurementHitResult hit = result.HitResults[i];
            accumulator.Append(hit.Threshold.ToString(CultureInfo.InvariantCulture)).Append(':').Append(hit.Hit);
        }

        accumulator.Append(']').Append('\n');
    }

    private static string Dt(System.DateTime value) => value.ToString("O", CultureInfo.InvariantCulture);

    private static string N(double? value) => value is double v ? v.ToString(CultureInfo.InvariantCulture) : "null";

    private static string N(decimal? value) => value is decimal v ? v.ToString(CultureInfo.InvariantCulture) : "null";
}
