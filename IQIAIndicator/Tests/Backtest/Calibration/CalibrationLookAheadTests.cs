using System.Collections.Generic;
using System.Linq;
using IQIAIndicator.Backtest.Calibration;
using IQIAIndicator.Core.MarketData;
using Xunit;

namespace IQIAIndicator.Tests.BacktestTests.Calibration;

/// <summary>
/// Sprint 15.25 (Lot 14.9, brief §17/§35 - MANDATORY "Future Data Test"). Procedure exactly as specified:
/// (1) run an experiment whose dataset ends shortly after OOS, (2) append MORE future data (a wider OOS),
/// (3) re-run, (4) compare - TRAIN and VALIDATION results must be identical in both runs.
///
/// Relies on the SAME look-ahead guarantee <see cref="BacktestFoundationLookAheadTests"/>/
/// <see cref="BacktestSignalPipelineLookAheadTests"/> already prove at the engine level (Lot 14.1/14.3):
/// bar i's computation never reads bars beyond i. This test verifies that guarantee survives
/// <see cref="CalibrationExperimentRunner"/>'s calendar-window slicing on top of it - see that class's own
/// doc comment for why one full run + timestamp filtering is equivalent to re-running per window.
/// </summary>
public sealed class CalibrationLookAheadTests
{
    /// <summary>Compares the underlying METRICS, never <see cref="CalibrationExperimentResult.ResultFingerprint"/> -
    /// that fingerprint deliberately incorporates <see cref="CalibrationExperimentResult.ExperimentId"/>
    /// (brief §45), so two DIFFERENT experiments (here: differing only in how much OOS data exists) always
    /// get different fingerprints even when a shared window's own metrics are identical. The metrics
    /// themselves are what this look-ahead guarantee is actually about.</summary>
    private static List<object?> Summary(CalibrationExperimentRunResult result, CalibrationWindowRole role) =>
        result.Results.Where(r => r.Window.Role == role)
            .OrderBy(r => r.Direction)
            .SelectMany(r => new object?[]
            {
                r.Direction, r.Status, r.SignalCount, r.PositionCount, r.GrossPnL, r.FinalEquity,
                r.MaximumDrawdown, r.WinRate, r.MedianReturn, r.MedianMfe, r.MedianMae,
                string.Join(',', r.HitRates.OrderBy(h => h.Key).Select(h => $"{h.Key}={h.Value}"))
            })
            .ToList();

    [Fact]
    public void AppendingMoreOosData_NeverChangesTrainOrValidationResults()
    {
        HistoricalSeries full = BacktestTestSeriesBuilder.MeanRevertingOu(400, seed: 51UL);
        HistoricalSeries shortSeries = BacktestTestSeriesBuilder.Truncate(full, 320);

        const int leadIn = 128, train = 100, validation = 40;

        CalibrationDataset shortDataset = CalibrationTestFixtures.Dataset(shortSeries);
        CalibrationWindowSet shortWindows = CalibrationTestFixtures.Windows(shortDataset, leadIn, train, validation, oosBars: 51);
        CalibrationExperiment shortExperiment = CalibrationExperiment.Create(
            CalibrationParameterSet.Empty(), shortDataset, shortWindows, CalibrationTestFixtures.Setup());

        CalibrationDataset fullDataset = CalibrationTestFixtures.Dataset(full);
        CalibrationWindowSet fullWindows = CalibrationTestFixtures.Windows(fullDataset, leadIn, train, validation, oosBars: 131);
        CalibrationExperiment fullExperiment = CalibrationExperiment.Create(
            CalibrationParameterSet.Empty(), fullDataset, fullWindows, CalibrationTestFixtures.Setup());

        // TRAIN/VALIDATION windows are IDENTICAL (same Start/End) in both runs - only OOS was widened.
        Assert.Equal(shortWindows.Train.Start, fullWindows.Train.Start);
        Assert.Equal(shortWindows.Train.End, fullWindows.Train.End);
        Assert.Equal(shortWindows.Validation.Start, fullWindows.Validation.Start);
        Assert.Equal(shortWindows.Validation.End, fullWindows.Validation.End);
        Assert.True(fullWindows.Oos.End > shortWindows.Oos.End, "Sanity: the full run's OOS window must genuinely be wider.");

        CalibrationExperimentRunResult shortResult = CalibrationExperimentRunner.Run(shortExperiment);
        CalibrationExperimentRunResult fullResult = CalibrationExperimentRunner.Run(fullExperiment);

        Assert.Equal(Summary(shortResult, CalibrationWindowRole.Train), Summary(fullResult, CalibrationWindowRole.Train));
        Assert.Equal(Summary(shortResult, CalibrationWindowRole.Validation), Summary(fullResult, CalibrationWindowRole.Validation));
    }
}
