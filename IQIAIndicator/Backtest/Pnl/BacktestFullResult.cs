using System.Collections.Generic;
using IQIAIndicator.Backtest.Execution;
using IQIAIndicator.Backtest.Measurement;

namespace IQIAIndicator.Backtest.Pnl;

/// <summary>
/// Sprint 15.25 (Lot 14.6). Full chain result: Signal (Lot 14.3) + Measurement (Lot 14.4) + Execution
/// (Lot 14.5) + P&amp;L (Lot 14.6), as four PARALLEL, responsibility-separated results - mirrors
/// <see cref="Backtest.Execution.BacktestSimulationResult"/>'s own pairing discipline, extended one stage
/// further. <see cref="PnLResult"/>.PositionPnLResults[i] corresponds to
/// <see cref="ExecutionResult"/>.Positions[i] by PositionId - never recomputed, never duplicated.
/// </summary>
public sealed record BacktestFullResult(
    BacktestSignalPipelineResult SignalResult,
    IReadOnlyList<MeasurementResult> Measurements,
    BacktestExecutionResult ExecutionResult,
    BacktestPnLResult PnLResult);
