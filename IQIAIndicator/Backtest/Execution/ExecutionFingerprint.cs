using System.Collections.Generic;
using System.Globalization;
using System.Text;
using IQIAIndicator.Backtest;

namespace IQIAIndicator.Backtest.Execution;

/// <summary>
/// Sprint 15.25 (Lot 14.5, brief §29). Deterministic, wall-clock-free canonicalization of a
/// <see cref="SimulatedPosition"/> population - same reuse strategy as Lot 14.3's
/// <c>BacktestSignalFingerprint</c> and Lot 14.4's <c>MeasurementFingerprint</c>: lives alongside (not
/// inside) <see cref="BacktestFingerprint"/> (Lot 14.1) and reuses its one public primitive,
/// <see cref="BacktestFingerprint.Sha256Hex"/>, without modifying that file.
///
/// Like <c>MeasurementResult</c>, <see cref="SimulatedPosition"/> carries no wall-clock field -
/// EntryTimestamp/ExitTimestamp are both derived from real bar timestamps, never <c>DateTime.UtcNow</c> -
/// so every field below is hashed directly, nothing needs to be excluded.
/// </summary>
internal static class ExecutionFingerprint
{
    public static string ComputeHash(IReadOnlyList<SimulatedPosition> positions)
    {
        var accumulator = new StringBuilder();
        foreach (SimulatedPosition position in positions)
            AppendPosition(accumulator, position);

        return BacktestFingerprint.Sha256Hex(accumulator.ToString());
    }

    private static void AppendPosition(StringBuilder accumulator, SimulatedPosition position)
    {
        accumulator.Append("Position[").Append(position.PositionId).Append(']')
            .Append(" status=").Append(position.Status)
            .Append(" dir=").Append(position.Direction)
            .Append(" entryTs=").Append(Dt(position.EntryTimestamp))
            .Append(" entryPrice=").Append(N(position.EntryPrice))
            .Append(" entryBar=").Append(position.EntryBarIndex)
            .Append(" exitTs=").Append(position.ExitTimestamp is DateTime dt ? Dt(dt) : "null")
            .Append(" exitPrice=").Append(N(position.ExitPrice))
            .Append(" exitBar=").Append(position.ExitBarIndex?.ToString(CultureInfo.InvariantCulture) ?? "null")
            .Append(" exitReason=").Append(position.ExitReason?.ToString() ?? "null")
            .Append(" holdingBars=").Append(position.HoldingBars?.ToString(CultureInfo.InvariantCulture) ?? "null")
            .Append(" grossMove=").Append(N(position.GrossPriceMove))
            .Append(" return=").Append(N(position.Return))
            .Append('\n');
    }

    private static string Dt(System.DateTime value) => value.ToString("O", CultureInfo.InvariantCulture);

    private static string N(double? value) => value is double v ? v.ToString(CultureInfo.InvariantCulture) : "null";

    private static string N(decimal? value) => value is decimal v ? v.ToString(CultureInfo.InvariantCulture) : "null";
}
