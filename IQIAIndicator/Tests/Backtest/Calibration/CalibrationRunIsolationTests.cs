using System.Collections.Generic;
using System.Linq;
using IQIAIndicator.Backtest.Calibration;
using IQIAIndicator.Core.MarketData;
using Xunit;

namespace IQIAIndicator.Tests.BacktestTests.Calibration;

/// <summary>Sprint 15.25 (Lot 14.9, brief §31/§32/§51). Reproducibility and run isolation: the same
/// experiment run twice is bit-identical, and running a DIFFERENT experiment in between leaves no residual
/// state (<see cref="CalibrationExperimentRunner"/> is a static, stateless method - no engine/field is
/// ever reused across calls, same discipline as <see cref="BacktestEngine"/> itself, Lot 14.1 brief §12).</summary>
public sealed class CalibrationRunIsolationTests
{
    private static CalibrationExperiment Experiment(ulong seed, int measurementHorizon = 10)
    {
        HistoricalSeries series = BacktestTestSeriesBuilder.MeanRevertingOu(320, seed);
        CalibrationDataset dataset = CalibrationTestFixtures.Dataset(series);
        CalibrationWindowSet windows = CalibrationTestFixtures.Windows(dataset, leadInBars: 128, trainBars: 100, validationBars: 40, oosBars: 40);
        return CalibrationExperiment.Create(
            CalibrationParameterSet.Empty(), dataset, windows, CalibrationTestFixtures.Setup(measurementHorizon: measurementHorizon));
    }

    private static List<(CalibrationWindowRole Role, CalibrationDirectionFilter Direction, string Fingerprint)> Summary(CalibrationExperimentRunResult result) =>
        result.Results.Select(r => (r.Window.Role, r.Direction, r.ResultFingerprint)).OrderBy(t => t.Role).ThenBy(t => t.Direction).ToList();

    [Fact]
    public void RunningTheSameExperimentTwice_ProducesIdenticalResults()
    {
        CalibrationExperiment experiment = Experiment(41UL);

        CalibrationExperimentRunResult resultA = CalibrationExperimentRunner.Run(experiment);
        CalibrationExperimentRunResult resultB = CalibrationExperimentRunner.Run(experiment);

        Assert.Equal(resultA.ExperimentId, resultB.ExperimentId);
        Assert.Equal(Summary(resultA), Summary(resultB));
    }

    [Fact]
    public void RunningADifferentExperimentInBetween_DoesNotAffectTheOriginalExperimentsResult()
    {
        CalibrationExperiment experimentA = Experiment(41UL);
        CalibrationExperiment experimentB = Experiment(41UL, measurementHorizon: 20);

        CalibrationExperimentRunResult a1 = CalibrationExperimentRunner.Run(experimentA);
        CalibrationExperimentRunner.Run(experimentB); // interleaved run - must not contaminate A
        CalibrationExperimentRunResult a2 = CalibrationExperimentRunner.Run(experimentA);

        Assert.Equal(Summary(a1), Summary(a2));
    }
}
