using System.Collections.Generic;
using System.Globalization;
using System.Text;
using IQIAIndicator.Backtest;

namespace IQIAIndicator.Backtest.Risk;

/// <summary>
/// Sprint 15.25 (Lot 14.8). Deterministic, wall-clock-free canonicalization of a
/// <see cref="PositionRiskOutcome"/> series - same reuse strategy as every prior lot's fingerprint: lives
/// alongside (not inside) <see cref="BacktestFingerprint"/> (Lot 14.1) and reuses its one public primitive,
/// <see cref="BacktestFingerprint.Sha256Hex"/>, without modifying that file.
/// </summary>
internal static class RiskResultFingerprint
{
    public static string ComputeHash(IReadOnlyList<PositionRiskOutcome> outcomes)
    {
        var accumulator = new StringBuilder();
        foreach (PositionRiskOutcome outcome in outcomes)
        {
            accumulator.Append("RiskOutcome[").Append(outcome.PositionId).Append(']')
                .Append(" status=").Append(outcome.Status)
                .Append(" dir=").Append(outcome.Direction)
                .Append(" allowed=").Append(outcome.RiskEvaluation.IsAllowed)
                .Append(" reqQty=").Append(outcome.RiskEvaluation.RequestedQuantity)
                .Append(" allowQty=").Append(outcome.RiskEvaluation.AllowedQuantity)
                .Append(" reason=").Append(outcome.RiskEvaluation.Reason)
                .Append(" netPnL=").Append(N(outcome.NetPnL))
                .Append(" equityBefore=").Append(N(outcome.EquityBefore))
                .Append(" equityAfter=").Append(N(outcome.EquityAfter))
                .Append(" drawdown=").Append(N(outcome.Drawdown))
                .Append('\n');
        }

        return BacktestFingerprint.Sha256Hex(accumulator.ToString());
    }

    private static string N(decimal? value) => value is decimal v ? v.ToString(CultureInfo.InvariantCulture) : "null";
}
