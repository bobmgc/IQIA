using System.Collections.Generic;
using IQIAIndicator.Backtest.Measurement;

namespace IQIAIndicator.Backtest.Execution;

/// <summary>
/// Sprint 15.25 (Lot 14.5, brief §36). Full chain result: Signal (Lot 14.3) + Measurement (Lot 14.4) +
/// Execution (Lot 14.5), as three PARALLEL, index-aligned collections - never merged into one shape.
/// <see cref="SignalResult"/>.Bars[i], <see cref="Measurements"/>[i] and
/// <see cref="ExecutionResult"/>.Positions[i] all describe the SAME bar i (same
/// BarIndex/SignalBarIndex/PositionId), so a caller can relate SignalId/Measurement/Position by index
/// without any field being recopied between the three (brief §36: "sans recopier inutilement les
/// données"). Responsibilities stay strictly separated - this type is pure pairing, it computes nothing
/// itself.
/// </summary>
public sealed record BacktestSimulationResult(
    BacktestSignalPipelineResult SignalResult,
    IReadOnlyList<MeasurementResult> Measurements,
    BacktestExecutionResult ExecutionResult);
