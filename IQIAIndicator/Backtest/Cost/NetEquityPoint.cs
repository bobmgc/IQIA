using System;

namespace IQIAIndicator.Backtest.Cost;

/// <summary>
/// Sprint 15.25 (Lot 14.7). Net-of-cost twin of <see cref="Pnl.EquityPoint"/> (Lot 14.6, unmodified) - one
/// point per Closed position, built from <see cref="PositionCostResult.NetPnL"/> instead of
/// <c>PositionPnLResult.GrossPnL</c>, using the exact same running-peak/drawdown convention (see
/// <see cref="BacktestCostResultBuilder"/>). When every NetPnL equals its GrossPnL (zero-cost /
/// costs-disabled), this series is numerically IDENTICAL to <see cref="Pnl.EquityPoint"/>'s own curve for
/// the same run - the whole point of the Lot 14.6 zero-cost-equivalence guarantee (brief §8/§9).
/// </summary>
public sealed record NetEquityPoint(
    DateTime Timestamp,
    int PositionId,
    decimal IncrementalNetPnL,
    decimal CumulativeNetPnL,
    decimal? NetEquity,
    decimal NetDrawdown,
    double? NetDrawdownPercent);
