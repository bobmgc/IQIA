using System.Collections.Generic;
using System.Linq;
using IQIAIndicator.Backtest.Calibration;
using IQIAIndicator.Core.MarketData;
using Xunit;

namespace IQIAIndicator.Tests.BacktestTests.Calibration;

/// <summary>
/// Sprint 15.25 (Lot 14.9, brief §17/§36 - "OOS Isolation Test"). Dataset complet (TRAIN+VALIDATION+OOS,
/// properly sized) versus a dataset shrunk to TRAIN plus only a minimal VALIDATION/OOS tail: TRAIN's own
/// result must be identical either way - VALIDATION/OOS existing at all, richly or minimally, must never
/// reach back into TRAIN and change its SIGNAL.
///
/// The "minimal" tail here still leaves at least <c>HorizonBars</c> (10) bars after TRAIN.End - fewer than
/// that would trip <c>MeasurementStatus.InsufficientFutureData</c>/<c>PositionStatus.InsufficientFutureData</c>
/// for TRAIN's own LAST signal bars, which is the well-documented, pre-existing "signal invariant,
/// measurement/execution can differ near a data boundary" behaviour (Lot 14.4 report,
/// <c>ScientificMeasurementEngineIntegrationTests</c>) - a legitimate property of the horizon model, not a
/// look-ahead violation, and not what this test is checking.
/// </summary>
public sealed class CalibrationWindowIsolationTests
{
    /// <summary>Compares the underlying METRICS, never <see cref="CalibrationExperimentResult.ResultFingerprint"/>
    /// - see <see cref="CalibrationLookAheadTests"/>'s own doc comment on why: that fingerprint
    /// deliberately incorporates <see cref="CalibrationExperimentResult.ExperimentId"/>, which differs here
    /// on purpose (a different dataset/OOS width), even though TRAIN's own metrics must not.</summary>
    private static List<object?> TrainSummary(CalibrationExperimentRunResult result) =>
        result.Results.Where(r => r.Window.Role == CalibrationWindowRole.Train)
            .OrderBy(r => r.Direction)
            .SelectMany(r => new object?[]
            {
                r.Direction, r.Status, r.SignalCount, r.PositionCount, r.GrossPnL, r.FinalEquity,
                r.MaximumDrawdown, r.WinRate, r.MedianReturn, r.MedianMfe, r.MedianMae,
                string.Join(',', r.HitRates.OrderBy(h => h.Key).Select(h => $"{h.Key}={h.Value}"))
            })
            .ToList();

    [Fact]
    public void ShrinkingValidationAndOosToAMinimalTail_NeverChangesTrainsResult()
    {
        HistoricalSeries full = BacktestTestSeriesBuilder.MeanRevertingOu(400, seed: 61UL);
        // leadIn(128) + train(100) + validation(10) + oos(10) + 1 index-safety bar: the 20 trailing bars
        // comfortably cover the default HorizonBars=10 needed to measure/execute TRAIN's own last signals.
        HistoricalSeries minimalTail = BacktestTestSeriesBuilder.Truncate(full, 249);

        const int leadIn = 128, train = 100;

        CalibrationDataset fullDataset = CalibrationTestFixtures.Dataset(full);
        CalibrationWindowSet fullWindows = CalibrationTestFixtures.Windows(fullDataset, leadIn, train, validationBars: 40, oosBars: 40);
        CalibrationExperiment fullExperiment = CalibrationExperiment.Create(
            CalibrationParameterSet.Empty(), fullDataset, fullWindows, CalibrationTestFixtures.Setup());

        CalibrationDataset minimalDataset = CalibrationTestFixtures.Dataset(minimalTail);
        CalibrationWindowSet minimalWindows = CalibrationTestFixtures.Windows(minimalDataset, leadIn, train, validationBars: 10, oosBars: 10);
        CalibrationExperiment minimalExperiment = CalibrationExperiment.Create(
            CalibrationParameterSet.Empty(), minimalDataset, minimalWindows, CalibrationTestFixtures.Setup());

        // TRAIN itself is byte-identical between the two runs (same Start/End, same underlying bars).
        Assert.Equal(fullWindows.Train.Start, minimalWindows.Train.Start);
        Assert.Equal(fullWindows.Train.End, minimalWindows.Train.End);

        CalibrationExperimentRunResult fullResult = CalibrationExperimentRunner.Run(fullExperiment);
        CalibrationExperimentRunResult minimalResult = CalibrationExperimentRunner.Run(minimalExperiment);

        Assert.Equal(TrainSummary(fullResult), TrainSummary(minimalResult));
    }
}
