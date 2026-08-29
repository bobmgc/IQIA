using IQIAIndicator.Backtest.Cost;
using IQIAIndicator.Backtest.Execution;
using IQIAIndicator.Backtest.Measurement;
using IQIAIndicator.Backtest.Pnl;
using IQIAIndicator.Backtest.Risk;
using IQIAIndicator.Engine.Risk;

namespace IQIAIndicator.Backtest.Calibration;

/// <summary>
/// Sprint 15.25 (Lot 14.9, brief §18). Everything an experiment needs to actually run
/// <see cref="BacktestEngine"/> BESIDES the thing being calibrated (<see cref="CalibrationParameterSet"/>)
/// and the data (<see cref="CalibrationDataset"/>/<see cref="CalibrationWindowSet"/>) - grouped so a grid of
/// many parameter sets can share one setup instance instead of repeating eight constructor arguments per
/// experiment. Every field here is one of the SAME, unmodified Lot 14.1/14.3-14.8 configuration types
/// (brief §18: "Pipeline configuration, Measurement configuration, Execution configuration, Risk
/// configuration") - nothing new is invented; this type only groups what already exists.
/// </summary>
public sealed record CalibrationExperimentSetup
{
    public required CalibrationWarmupContract WarmupContract { get; init; }

    public required InstrumentRiskSpecification Instrument { get; init; }

    public required RiskPolicy Policy { get; init; }

    public required decimal InitialCapital { get; init; }

    public required MeasurementConfiguration MeasurementConfiguration { get; init; }

    public required ExecutionConfiguration ExecutionConfiguration { get; init; }

    public required PnLConfiguration PnLConfiguration { get; init; }

    public required ExecutionCostConfiguration CostConfiguration { get; init; }

    public required BacktestRiskConfiguration RiskConfiguration { get; init; }
}
