using System.Collections.Generic;
using System.Globalization;
using System.Text;
using IQIAIndicator.Backtest;

namespace IQIAIndicator.Backtest.Pnl;

/// <summary>
/// Sprint 15.25 (Lot 14.6, brief §32/§46). Deterministic, wall-clock-free canonicalization of an
/// <see cref="EquityPoint"/> series - same reuse strategy as every prior lot's fingerprint: lives
/// alongside (not inside) <see cref="BacktestFingerprint"/> (Lot 14.1) and reuses its one public
/// primitive, <see cref="BacktestFingerprint.Sha256Hex"/>, without modifying that file.
///
/// Depends on exactly what brief §46 names: timestamps, P&amp;L, cumulative P&amp;L, drawdown - hashing the
/// EquityCurve directly covers all four, since <see cref="EquityPoint"/> already carries them.
/// </summary>
internal static class PnLFingerprint
{
    public static string ComputeHash(IReadOnlyList<EquityPoint> equityCurve)
    {
        var accumulator = new StringBuilder();
        foreach (EquityPoint point in equityCurve)
        {
            accumulator.Append("EquityPoint[").Append(point.PositionId).Append(']')
                .Append(" ts=").Append(point.Timestamp.ToString("O", CultureInfo.InvariantCulture))
                .Append(" incr=").Append(point.IncrementalPnL.ToString(CultureInfo.InvariantCulture))
                .Append(" cum=").Append(point.CumulativePnL.ToString(CultureInfo.InvariantCulture))
                .Append(" equity=").Append(point.Equity is decimal e ? e.ToString(CultureInfo.InvariantCulture) : "null")
                .Append(" dd=").Append(point.Drawdown.ToString(CultureInfo.InvariantCulture))
                .Append(" ddPct=").Append(point.DrawdownPercent is double p ? p.ToString(CultureInfo.InvariantCulture) : "null")
                .Append('\n');
        }

        return BacktestFingerprint.Sha256Hex(accumulator.ToString());
    }
}
