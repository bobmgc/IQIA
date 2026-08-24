using System;
using IQIAIndicator.Backtest.Calibration;
using IQIAIndicator.Backtest.Cost;
using IQIAIndicator.Backtest.Execution;
using IQIAIndicator.Backtest.Measurement;
using IQIAIndicator.Backtest.Pnl;
using IQIAIndicator.Backtest.Risk;
using IQIAIndicator.Core;
using IQIAIndicator.Core.MarketData;
using IQIAIndicator.Engine.Risk;

namespace IQIAIndicator.Tests.BacktestTests.Calibration;

/// <summary>Sprint 15.25 (Lot 14.9). Shared, deterministic fixtures for the calibration test suite - same
/// synthetic-series discipline as <see cref="BacktestTestSeriesBuilder"/> (Lot 14.1): every value here is
/// fixed/seeded, never <see cref="System.Random"/> or wall-clock derived.</summary>
internal static class CalibrationTestFixtures
{
    public static InstrumentRiskSpecification Spec() => InstrumentRiskSpecification.FromInstrumentInfo(
        new InstrumentInfo("ES", TickSize: 0.25m, TickValue: 12.5m, PointValue: 50m, Decimals: 2),
        minQuantity: 1, maxQuantity: 50, quantityStep: 1);

    public static RiskPolicy Policy() => new(0.02m, null, null, null, null, null, null, null, null, null);

    public static CalibrationExperimentSetup Setup(int requiredWarmupBars = 128, int measurementHorizon = 10, int executionHorizon = 10) => new()
    {
        WarmupContract = new CalibrationWarmupContract(requiredWarmupBars),
        Instrument = Spec(),
        Policy = Policy(),
        InitialCapital = 50_000m,
        MeasurementConfiguration = MeasurementConfiguration.Create(measurementHorizon, new[] { 0.001, 0.002 }),
        ExecutionConfiguration = ExecutionConfiguration.Create(executionHorizon),
        PnLConfiguration = PnLConfiguration.Create(InstrumentPnLSpecification.Create("ES", priceUnitValue: 50m, currency: "USD"), quantity: 1),
        CostConfiguration = ExecutionCostConfiguration.Disabled(),
        RiskConfiguration = BacktestRiskConfiguration.Disabled()
    };

    public static CalibrationDataset Dataset(HistoricalSeries series, string provider = "SyntheticTestSeries")
    {
        CalibrationDatasetSpecification spec = CalibrationDatasetSpecification.Create(
            provider, series.Symbol, series.TimeFrame, series.FirstTimestamp, series.LastTimestamp.AddMinutes(1));
        return new CalibrationDataset(spec, series);
    }

    /// <summary>Builds a TRAIN/VALIDATION/OOS split from bar counts: bars [0..leadInBars) are warmup
    /// context (never inside any window), then TRAIN/VALIDATION/OOS consume the following bars in order.
    /// Requires <c>series.Count &gt;= leadInBars + trainBars + validationBars + oosBars + 1</c>.</summary>
    public static CalibrationWindowSet Windows(
        CalibrationDataset dataset, int leadInBars, int trainBars, int validationBars, int oosBars)
    {
        HistoricalSeries series = dataset.Series;
        DateTime trainStart = series.Bars[leadInBars].Timestamp;
        DateTime trainEnd = series.Bars[leadInBars + trainBars].Timestamp;
        DateTime validationEnd = series.Bars[leadInBars + trainBars + validationBars].Timestamp;
        DateTime oosEnd = series.Bars[leadInBars + trainBars + validationBars + oosBars].Timestamp;

        return CalibrationWindowSet.Create(
            dataset.Specification,
            new CalibrationWindow(CalibrationWindowRole.Train, trainStart, trainEnd),
            new CalibrationWindow(CalibrationWindowRole.Validation, trainEnd, validationEnd),
            new CalibrationWindow(CalibrationWindowRole.Oos, validationEnd, oosEnd));
    }
}
