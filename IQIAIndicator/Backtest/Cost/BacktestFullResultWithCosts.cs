using IQIAIndicator.Backtest.Pnl;

namespace IQIAIndicator.Backtest.Cost;

/// <summary>
/// Sprint 15.25 (Lot 14.7). Return type of <see cref="BacktestEngine.RunFullBacktestWithCosts"/>: the
/// complete, UNMODIFIED Lot 14.6 <see cref="BacktestFullResult"/> (Signal + Measurement + Execution +
/// Gross P&amp;L) paired with the new <see cref="BacktestCostResult"/> (Lot 14.7). Deliberately two
/// PARALLEL, responsibility-separated results - exactly like <see cref="BacktestFullResult"/> pairs its own
/// four stages - rather than folding cost fields into <see cref="BacktestFullResult"/> itself, so that type
/// (and every test/caller that already depends on its exact shape) never changes.
/// </summary>
public sealed record BacktestFullResultWithCosts(
    BacktestFullResult FullResult,
    BacktestCostResult CostResult);
