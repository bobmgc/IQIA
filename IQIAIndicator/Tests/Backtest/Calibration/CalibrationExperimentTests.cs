using IQIAIndicator.Backtest.Calibration;
using IQIAIndicator.Core.MarketData;
using Xunit;

namespace IQIAIndicator.Tests.BacktestTests.Calibration;

/// <summary>Sprint 15.25 (Lot 14.9, brief §15/§18/§19/§20/§51). <see cref="CalibrationExperiment"/>:
/// construction, deterministic identity, and warmup-contract rejection (never silently satisfied by
/// borrowing VALIDATION/OOS bars).</summary>
public sealed class CalibrationExperimentTests
{
    private static HistoricalSeries Series() => BacktestTestSeriesBuilder.MeanRevertingOu(320, seed: 11UL);

    [Fact]
    public void Create_WithSufficientWarmup_Succeeds()
    {
        HistoricalSeries series = Series();
        CalibrationDataset dataset = CalibrationTestFixtures.Dataset(series);
        CalibrationWindowSet windows = CalibrationTestFixtures.Windows(dataset, leadInBars: 128, trainBars: 100, validationBars: 40, oosBars: 40);
        CalibrationParameterSet parameterSet = CalibrationParameterSet.Empty();

        CalibrationExperiment experiment = CalibrationExperiment.Create(parameterSet, dataset, windows, CalibrationTestFixtures.Setup(requiredWarmupBars: 128));

        Assert.NotNull(experiment.ExperimentId);
        Assert.StartsWith("CAL-", experiment.ExperimentId);
        Assert.Equal("14.9.1", experiment.ProtocolVersion);
    }

    [Fact]
    public void Create_WithInsufficientWarmup_IsRejected_NotSilentlySatisfied()
    {
        HistoricalSeries series = Series();
        CalibrationDataset dataset = CalibrationTestFixtures.Dataset(series);
        // Only 50 bars precede TRAIN, but the setup below requires 128 - must be rejected, never borrowed
        // from VALIDATION/OOS and never silently reduced (brief §15/§16/§56).
        CalibrationWindowSet windows = CalibrationTestFixtures.Windows(dataset, leadInBars: 50, trainBars: 100, validationBars: 40, oosBars: 40);

        bool ok = CalibrationExperiment.TryCreate(
            CalibrationParameterSet.Empty(), dataset, windows, CalibrationTestFixtures.Setup(requiredWarmupBars: 128),
            out _, out var errors);

        Assert.False(ok);
        Assert.Contains(errors, e => e.Contains("Insufficient warmup"));
    }

    [Fact]
    public void TwoExperiments_WithIdenticalInputs_ProduceIdenticalIdAndFingerprint()
    {
        HistoricalSeries series = Series();
        CalibrationDataset dataset = CalibrationTestFixtures.Dataset(series);
        CalibrationWindowSet windows = CalibrationTestFixtures.Windows(dataset, leadInBars: 128, trainBars: 100, validationBars: 40, oosBars: 40);
        CalibrationExperimentSetup setup = CalibrationTestFixtures.Setup();
        CalibrationParameterSet parameterSet = CalibrationParameterSet.Create(new[] { CalibrationParameter.Decimal("AmbiguityThreshold", 0.95m, "score") });

        CalibrationExperiment a = CalibrationExperiment.Create(parameterSet, dataset, windows, setup);
        CalibrationExperiment b = CalibrationExperiment.Create(parameterSet, dataset, windows, setup);

        Assert.Equal(a.ExperimentId, b.ExperimentId);
        Assert.Equal(a.ConfigurationFingerprint, b.ConfigurationFingerprint);
    }

    // ── §56: different parameter values -> different identity, no global state involved ────────────

    [Fact]
    public void ConfigurationIsolation_DifferentThresholdParameter_ProducesDifferentIdentity()
    {
        HistoricalSeries series = Series();
        CalibrationDataset dataset = CalibrationTestFixtures.Dataset(series);
        CalibrationWindowSet windows = CalibrationTestFixtures.Windows(dataset, leadInBars: 128, trainBars: 100, validationBars: 40, oosBars: 40);
        CalibrationExperimentSetup setup = CalibrationTestFixtures.Setup();

        CalibrationExperiment experimentA = CalibrationExperiment.Create(
            CalibrationParameterSet.Create(new[] { CalibrationParameter.Decimal("AmbiguityThreshold", 0.95m, "score") }), dataset, windows, setup);
        CalibrationExperiment experimentB = CalibrationExperiment.Create(
            CalibrationParameterSet.Create(new[] { CalibrationParameter.Decimal("AmbiguityThreshold", 0.90m, "score") }), dataset, windows, setup);

        Assert.NotEqual(experimentA.ExperimentId, experimentB.ExperimentId);
        Assert.NotEqual(experimentA.ConfigurationFingerprint, experimentB.ConfigurationFingerprint);
    }
}
