using System.Collections.Generic;

namespace IQIAIndicator.Backtest.Execution;

/// <summary>
/// Sprint 15.25 (Lot 14.5, brief §37/§38). Aggregate result of one full execution simulation run. Every
/// counter is a TECHNICAL, capital-independent count (brief §38) - no net P&amp;L, no equity curve, no
/// drawdown, no Sharpe (brief §37 explicitly forbids all four; verified by inspection - none of those
/// words appear anywhere in this file or in <see cref="ExecutionSimulator"/>).
/// </summary>
/// <param name="TotalCount">Total candidates simulated - one per input <see cref="BacktestSignalResult"/>.</param>
/// <param name="ClosedCount">Positions that reached <see cref="PositionStatus.Closed"/>.</param>
/// <param name="NotExecutableCount">Positions rejected as <see cref="PositionStatus.NotExecutable"/>
/// (NO_ACTION/WATCH or no TradePlan).</param>
/// <param name="InvalidEntryCount">Positions rejected as <see cref="PositionStatus.InvalidEntry"/>.</param>
/// <param name="InsufficientFutureDataCount">Positions rejected as
/// <see cref="PositionStatus.InsufficientFutureData"/>.</param>
/// <param name="InvalidExitCount">Positions rejected as <see cref="PositionStatus.InvalidExit"/>.</param>
/// <param name="InvalidStopTargetCount">Sprint 15.25 (Lot 15.4). Positions rejected as
/// <see cref="PositionStatus.InvalidStopTarget"/>.</param>
/// <param name="BuyCount">Closed positions whose Direction is BUY_CANDIDATE.</param>
/// <param name="SellCount">Closed positions whose Direction is SELL_CANDIDATE.</param>
/// <param name="DeterministicHash">SHA-256 of every position, in order (brief §29) - see
/// <see cref="ExecutionFingerprint.ComputeHash"/>.</param>
public sealed record BacktestExecutionResult(
    int TotalCount,
    int ClosedCount,
    int NotExecutableCount,
    int InvalidEntryCount,
    int InsufficientFutureDataCount,
    int InvalidExitCount,
    int InvalidStopTargetCount,
    int BuyCount,
    int SellCount,
    string DeterministicHash,
    IReadOnlyList<SimulatedPosition> Positions);
