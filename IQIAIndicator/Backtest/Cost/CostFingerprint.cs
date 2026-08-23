using System.Collections.Generic;
using System.Globalization;
using System.Text;
using IQIAIndicator.Backtest;

namespace IQIAIndicator.Backtest.Cost;

/// <summary>
/// Sprint 15.25 (Lot 14.7). Deterministic, wall-clock-free canonicalization of a
/// <see cref="NetEquityPoint"/> series - same reuse strategy as every prior lot's fingerprint (Lot 14.6's
/// <c>PnLFingerprint</c>, Lot 14.5's <c>ExecutionFingerprint</c>, ...): lives alongside (not inside)
/// <see cref="BacktestFingerprint"/> (Lot 14.1) and reuses its one public primitive,
/// <see cref="BacktestFingerprint.Sha256Hex"/>, without modifying that file.
/// </summary>
internal static class CostFingerprint
{
    public static string ComputeHash(IReadOnlyList<NetEquityPoint> netEquityCurve)
    {
        var accumulator = new StringBuilder();
        foreach (NetEquityPoint point in netEquityCurve)
        {
            accumulator.Append("NetEquityPoint[").Append(point.PositionId).Append(']')
                .Append(" ts=").Append(point.Timestamp.ToString("O", CultureInfo.InvariantCulture))
                .Append(" incr=").Append(point.IncrementalNetPnL.ToString(CultureInfo.InvariantCulture))
                .Append(" cum=").Append(point.CumulativeNetPnL.ToString(CultureInfo.InvariantCulture))
                .Append(" equity=").Append(point.NetEquity is decimal e ? e.ToString(CultureInfo.InvariantCulture) : "null")
                .Append(" dd=").Append(point.NetDrawdown.ToString(CultureInfo.InvariantCulture))
                .Append(" ddPct=").Append(point.NetDrawdownPercent is double p ? p.ToString(CultureInfo.InvariantCulture) : "null")
                .Append('\n');
        }

        return BacktestFingerprint.Sha256Hex(accumulator.ToString());
    }
}
