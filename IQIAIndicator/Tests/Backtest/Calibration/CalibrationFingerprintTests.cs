using System.Collections.Generic;
using System.Linq;
using IQIAIndicator.Backtest.Calibration;
using IQIAIndicator.Backtest.Cost;
using IQIAIndicator.Backtest.Measurement;
using IQIAIndicator.Core.MarketData;
using Xunit;

namespace IQIAIndicator.Tests.BacktestTests.Calibration;

/// <summary>Sprint 15.25 (Lot 14.9, brief §20/§37/§38/§45/§51). <see cref="CalibrationFingerprint"/>:
/// configuration-level determinism and sensitivity to every input it claims to depend on.</summary>
public sealed class CalibrationFingerprintTests
{
    private static HistoricalSeries Series() => BacktestTestSeriesBuilder.MeanRevertingOu(320, seed: 21UL);

    private static (CalibrationDataset Dataset, CalibrationWindowSet Windows, CalibrationExperimentSetup Setup) Baseline(HistoricalSeries series)
    {
        CalibrationDataset dataset = CalibrationTestFixtures.Dataset(series);
        CalibrationWindowSet windows = CalibrationTestFixtures.Windows(dataset, leadInBars: 128, trainBars: 100, validationBars: 40, oosBars: 40);
        return (dataset, windows, CalibrationTestFixtures.Setup());
    }

    // ── §38: dataset mutation propagates through Dataset.Fingerprint into the configuration fingerprint ─

    [Fact]
    public void MutatingOneBar_ChangesDatasetFingerprint_AndConfigurationFingerprint()
    {
        HistoricalSeries original = Series();
        var (datasetA, windows, setup) = Baseline(original);
        CalibrationParameterSet parameters = CalibrationParameterSet.Empty();
        string fingerprintA = CalibrationFingerprint.ComputeConfigurationFingerprint(parameters, datasetA, windows, setup);

        List<HistoricalBar> mutatedBars = original.Bars.ToList();
        mutatedBars[200] = mutatedBars[200] with { High = mutatedBars[200].High + 1m };
        HistoricalSeries mutated = HistoricalSeries.Create(original.Symbol, original.TimeFrame, original.TimeZone, original.Provider, mutatedBars);

        CalibrationDataset datasetB = CalibrationTestFixtures.Dataset(mutated);
        CalibrationWindowSet windowsB = CalibrationTestFixtures.Windows(datasetB, leadInBars: 128, trainBars: 100, validationBars: 40, oosBars: 40);
        string fingerprintB = CalibrationFingerprint.ComputeConfigurationFingerprint(parameters, datasetB, windowsB, setup);

        Assert.NotEqual(datasetA.Fingerprint, datasetB.Fingerprint);
        Assert.NotEqual(fingerprintA, fingerprintB);
    }

    [Fact]
    public void ChangingParameterSet_ChangesConfigurationFingerprint()
    {
        var (dataset, windows, setup) = Baseline(Series());

        string a = CalibrationFingerprint.ComputeConfigurationFingerprint(CalibrationParameterSet.Empty(), dataset, windows, setup);
        string b = CalibrationFingerprint.ComputeConfigurationFingerprint(
            CalibrationParameterSet.Create(new[] { CalibrationParameter.Decimal("X", 1m, "ratio") }), dataset, windows, setup);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void ChangingMeasurementConfiguration_ChangesConfigurationFingerprint()
    {
        HistoricalSeries series = Series();
        var (dataset, windows, setup) = Baseline(series);
        CalibrationParameterSet parameters = CalibrationParameterSet.Empty();

        string a = CalibrationFingerprint.ComputeConfigurationFingerprint(parameters, dataset, windows, setup);
        CalibrationExperimentSetup changed = setup with { MeasurementConfiguration = MeasurementConfiguration.Create(20, new[] { 0.001, 0.002 }) };
        string b = CalibrationFingerprint.ComputeConfigurationFingerprint(parameters, dataset, windows, changed);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void ChangingCostConfiguration_ChangesConfigurationFingerprint()
    {
        HistoricalSeries series = Series();
        var (dataset, windows, setup) = Baseline(series);
        CalibrationParameterSet parameters = CalibrationParameterSet.Empty();

        string a = CalibrationFingerprint.ComputeConfigurationFingerprint(parameters, dataset, windows, setup);
        CalibrationExperimentSetup changed = setup with { CostConfiguration = ExecutionCostConfiguration.Create(true, slippage: SlippageConfiguration.Fixed(0.25m)) };
        string b = CalibrationFingerprint.ComputeConfigurationFingerprint(parameters, dataset, windows, changed);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void ChangingWindowBoundary_ChangesConfigurationFingerprint()
    {
        HistoricalSeries series = Series();
        CalibrationDataset dataset = CalibrationTestFixtures.Dataset(series);
        CalibrationExperimentSetup setup = CalibrationTestFixtures.Setup();
        CalibrationParameterSet parameters = CalibrationParameterSet.Empty();

        CalibrationWindowSet windowsA = CalibrationTestFixtures.Windows(dataset, leadInBars: 128, trainBars: 100, validationBars: 40, oosBars: 40);
        CalibrationWindowSet windowsB = CalibrationTestFixtures.Windows(dataset, leadInBars: 128, trainBars: 90, validationBars: 40, oosBars: 40);

        string a = CalibrationFingerprint.ComputeConfigurationFingerprint(parameters, dataset, windowsA, setup);
        string b = CalibrationFingerprint.ComputeConfigurationFingerprint(parameters, dataset, windowsB, setup);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void ExperimentId_IsDerivedFromFingerprint_NeverARandomGuid()
    {
        var (dataset, windows, setup) = Baseline(Series());
        string fingerprint = CalibrationFingerprint.ComputeConfigurationFingerprint(CalibrationParameterSet.Empty(), dataset, windows, setup);

        string idA = CalibrationFingerprint.ComputeExperimentId(fingerprint);
        string idB = CalibrationFingerprint.ComputeExperimentId(fingerprint);

        Assert.Equal(idA, idB);
        Assert.Equal($"CAL-{fingerprint[..16]}", idA);
    }
}
