using System.Linq;
using IQIAIndicator.Backtest.Calibration;
using IQIAIndicator.Core.MarketData;
using Xunit;

namespace IQIAIndicator.Tests.BacktestTests.Calibration;

/// <summary>Sprint 15.25 (Lot 14.9, brief §44/§51). Plain-JSON export/import round-trips without loss -
/// no custom binary format, see <see cref="CalibrationSerialization"/>'s own doc comment.</summary>
public sealed class CalibrationSerializationTests
{
    private static CalibrationExperimentRunResult RunSample()
    {
        HistoricalSeries series = BacktestTestSeriesBuilder.MeanRevertingOu(320, seed: 71UL);
        CalibrationDataset dataset = CalibrationTestFixtures.Dataset(series);
        CalibrationWindowSet windows = CalibrationTestFixtures.Windows(dataset, leadInBars: 128, trainBars: 100, validationBars: 40, oosBars: 40);
        CalibrationExperiment experiment = CalibrationExperiment.Create(
            CalibrationParameterSet.Create(new[] { CalibrationParameter.Decimal("AmbiguityThreshold", 0.95m, "score") }),
            dataset, windows, CalibrationTestFixtures.Setup());

        return CalibrationExperimentRunner.Run(experiment);
    }

    [Fact]
    public void RunResult_RoundTripsThroughJson_WithoutLoss()
    {
        CalibrationExperimentRunResult original = RunSample();

        string json = CalibrationSerialization.ToJson(original);
        CalibrationExperimentRunResult restored = CalibrationSerialization.FromJson(json);

        Assert.Equal(original.ExperimentId, restored.ExperimentId);
        Assert.Equal(original.ConfigurationFingerprint, restored.ConfigurationFingerprint);
        Assert.Equal(original.Results.Count, restored.Results.Count);

        for (int i = 0; i < original.Results.Count; i++)
        {
            CalibrationExperimentResult a = original.Results[i];
            CalibrationExperimentResult b = restored.Results[i];

            Assert.Equal(a.Window.Role, b.Window.Role);
            Assert.Equal(a.Window.Start, b.Window.Start);
            Assert.Equal(a.Window.End, b.Window.End);
            Assert.Equal(a.Direction, b.Direction);
            Assert.Equal(a.Status, b.Status);
            Assert.Equal(a.SignalCount, b.SignalCount);
            Assert.Equal(a.PositionCount, b.PositionCount);
            Assert.Equal(a.GrossPnL, b.GrossPnL);
            Assert.Equal(a.FinalEquity, b.FinalEquity);
            Assert.Equal(a.MaximumDrawdown, b.MaximumDrawdown);
            Assert.Equal(a.WinRate, b.WinRate);
            Assert.Equal(a.MedianReturn, b.MedianReturn);
            Assert.Equal(a.MedianMfe, b.MedianMfe);
            Assert.Equal(a.MedianMae, b.MedianMae);
            Assert.Equal(a.HitRates.OrderBy(h => h.Key), b.HitRates.OrderBy(h => h.Key));
            Assert.Equal(a.ResultFingerprint, b.ResultFingerprint);
        }
    }

    [Fact]
    public void ResultsList_RoundTripsThroughJson()
    {
        var results = RunSample().Results;

        string json = CalibrationSerialization.ToJson(results);
        var restored = CalibrationSerialization.ResultsFromJson(json);

        Assert.Equal(results.Count, restored.Count);
        Assert.Equal(results.Select(r => r.ResultFingerprint), restored.Select(r => r.ResultFingerprint));
    }
}
