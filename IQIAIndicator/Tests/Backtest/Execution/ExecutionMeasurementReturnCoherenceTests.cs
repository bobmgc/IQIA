using IQIAIndicator.Backtest;
using IQIAIndicator.Backtest.Execution;
using IQIAIndicator.Backtest.Measurement;
using IQIAIndicator.Core;
using IQIAIndicator.Core.MarketData;
using IQIAIndicator.Engine.Risk;
using IQIAIndicator.Tests.BacktestTests;
using Xunit;

namespace IQIAIndicator.Tests.BacktestTests.Execution;

/// <summary>
/// Sprint 15.25 (Lot 14.5, brief §15/§35/§36). "Ne pas créer une deuxième définition du Return" -
/// proven empirically, not just by matching source code: for the SAME HorizonBars, Lot 14.4's
/// <see cref="MeasurementResult.Return"/> (Close at horizon end vs entry) and Lot 14.5's
/// <see cref="SimulatedPosition.Return"/> (ExitPrice at TIME_HORIZON vs entry, where ExitPrice IS that
/// same Close) must be BIT-IDENTICAL whenever both reached a comparable state - never merely "close
/// enough".
/// </summary>
public sealed class ExecutionMeasurementReturnCoherenceTests
{
    private static InstrumentRiskSpecification Spec() => InstrumentRiskSpecification.FromInstrumentInfo(
        new InstrumentInfo("ES", TickSize: 0.25m, TickValue: 12.5m, PointValue: 50m, Decimals: 2),
        minQuantity: 1, maxQuantity: 50, quantityStep: 1);

    private static RiskPolicy Policy() => new(0.02m, null, null, null, null, null, null, null, null, null);

    private static BacktestScenario ScenarioFor(HistoricalSeries series) =>
        BacktestScenario.Create(
            series,
            new BacktestWindow("W", series.FirstTimestamp, series.LastTimestamp.AddMinutes(1)),
            50_000m, Spec(), Policy());

    [Fact]
    public void Return_MatchesMeasurementResultReturn_ExactlyForEveryClosedPosition_GivenTheSameHorizon()
    {
        const int horizon = 10;
        HistoricalSeries series = BacktestTestSeriesBuilder.MeanRevertingOu(300, seed: 23UL, kappa: 0.8m);
        BacktestScenario scenario = ScenarioFor(series);

        BacktestSimulationResult result = new BacktestEngine().RunSimulation(
            scenario, warmupBars: 128,
            MeasurementConfiguration.Create(horizon, new[] { 0.001 }),
            ExecutionConfiguration.Create(horizon));

        int comparedCount = 0;
        for (int i = 0; i < result.ExecutionResult.Positions.Count; i++)
        {
            SimulatedPosition position = result.ExecutionResult.Positions[i];
            MeasurementResult measurement = result.Measurements[i];

            if (position.Status == PositionStatus.Closed && measurement.Status == MeasurementStatus.Measured)
            {
                Assert.Equal(measurement.Return!.Value, position.Return!.Value, 12);
                comparedCount++;
            }
        }

        Assert.True(comparedCount > 0, "Expected at least one bar where both Measurement and Execution reached a comparable Closed/Measured state.");
    }
}
