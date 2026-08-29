using System.Collections.Generic;
using IQIAIndicator.Backtest.Execution;
using IQIAIndicator.Backtest.Measurement;

namespace IQIAIndicator.Backtest.Risk;

/// <summary>
/// Sprint 15.25 (Lot 14.8). Return type of <see cref="BacktestEngine.RunFullBacktestWithRisk"/>: Signal +
/// Measurement + Execution (Lots 14.3-14.5, unchanged) paired with the new <see cref="BacktestRiskResult"/>
/// - the same four-parallel-results discipline <see cref="Pnl.BacktestFullResult"/> (Lot 14.6) and
/// <see cref="Cost.BacktestFullResultWithCosts"/> (Lot 14.7) already established. Deliberately does NOT
/// wrap <see cref="Pnl.BacktestFullResult"/>/<see cref="Cost.BacktestFullResultWithCosts"/> themselves
/// (brief §14/§15): both of those compute Gross/Net PnL at a SINGLE uniform quantity
/// (<c>PnLConfiguration.Quantity</c>), which this lot's risk-varying-per-position quantity would make
/// meaningless to compute and then discard - <see cref="BacktestRiskResult"/> is built directly from
/// <see cref="ExecutionResult"/>.Positions instead (see <see cref="BacktestRiskResultBuilder"/>).
/// </summary>
public sealed record BacktestFullResultWithRisk(
    BacktestSignalPipelineResult SignalResult,
    IReadOnlyList<MeasurementResult> Measurements,
    BacktestExecutionResult ExecutionResult,
    BacktestRiskResult RiskResult);
