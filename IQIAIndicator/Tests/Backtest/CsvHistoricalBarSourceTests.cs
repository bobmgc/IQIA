using System;
using System.IO;
using IQIAIndicator.Core.MarketData;
using Xunit;

namespace IQIAIndicator.Tests.BacktestTests;

/// <summary>Sprint 15.25 (Lot 14.1, brief §6). Exercises the development CSV adapter against the
/// already-committed real ATAS capture, proving IHistoricalBarSource can load a genuine dataset end to
/// end without touching Tests/Research/StopLossCalibration's own infrastructure.</summary>
public sealed class CsvHistoricalBarSourceTests
{
    [Fact]
    public void Load_RealCapture_ProducesFullyValidatedSeries()
    {
        string path = BacktestTestPaths.RealCaptureOhlcvCsv();
        var source = new CsvHistoricalBarSource(path, timeZone: "Unspecified (ATAS passthrough)", provider: "ATAS-Capture-Sprint15.18");

        HistoricalSeries series = source.Load("ES", "M5", DateTime.MinValue, DateTime.MaxValue);

        Assert.Equal("ES", series.Symbol);
        Assert.Equal("M5", series.TimeFrame);
        Assert.True(series.Count > 1000, $"Expected the real capture to contain over 1000 bars, got {series.Count}.");
        for (int i = 1; i < series.Count; i++)
            Assert.True(series.Bars[i].Timestamp > series.Bars[i - 1].Timestamp);
    }

    [Fact]
    public void Load_WindowedRange_ReturnsOnlyBarsInsideHalfOpenRange()
    {
        string path = BacktestTestPaths.RealCaptureOhlcvCsv();
        var source = new CsvHistoricalBarSource(path, "Unspecified", "ATAS-Capture-Sprint15.18");

        HistoricalSeries full = source.Load("ES", "M5", DateTime.MinValue, DateTime.MaxValue);
        DateTime from = full.Bars[10].Timestamp;
        DateTime to = full.Bars[20].Timestamp; // exclusive - bar[20] itself must NOT be included

        HistoricalSeries windowed = source.Load("ES", "M5", from, to);

        Assert.Equal(10, windowed.Count);
        Assert.Equal(full.Bars[10].Timestamp, windowed.FirstTimestamp);
        Assert.Equal(full.Bars[19].Timestamp, windowed.LastTimestamp);
    }

    [Fact]
    public void Load_RangeWithNoBars_Throws()
    {
        string path = BacktestTestPaths.RealCaptureOhlcvCsv();
        var source = new CsvHistoricalBarSource(path, "Unspecified", "ATAS-Capture-Sprint15.18");

        Assert.Throws<ArgumentException>(() =>
            source.Load("ES", "M5", new DateTime(1990, 1, 1), new DateTime(1990, 1, 2)));
    }

    [Fact]
    public void Load_WrongSymbol_Throws_NeverSilentlyFiltered()
    {
        string path = BacktestTestPaths.RealCaptureOhlcvCsv();
        var source = new CsvHistoricalBarSource(path, "Unspecified", "ATAS-Capture-Sprint15.18");

        Assert.Throws<InvalidOperationException>(() =>
            source.Load("MES", "M5", DateTime.MinValue, DateTime.MaxValue));
    }

    [Fact]
    public void Load_InvertedRange_Throws()
    {
        string path = BacktestTestPaths.RealCaptureOhlcvCsv();
        var source = new CsvHistoricalBarSource(path, "Unspecified", "ATAS-Capture-Sprint15.18");

        Assert.Throws<ArgumentException>(() =>
            source.Load("ES", "M5", new DateTime(2026, 1, 2), new DateTime(2026, 1, 1)));
    }

    [Fact]
    public void Constructor_RequiresTimeZone_NeverDefaulted()
    {
        string path = BacktestTestPaths.RealCaptureOhlcvCsv();
        Assert.Throws<ArgumentException>(() => new CsvHistoricalBarSource(path, "", "Provider"));
    }

    [Fact]
    public void MissingFile_Throws_NoSubstituteDataset()
    {
        var source = new CsvHistoricalBarSource(Path.Combine(Path.GetTempPath(), $"does-not-exist-{Guid.NewGuid():N}.csv"), "UTC", "Provider");
        Assert.Throws<FileNotFoundException>(() => source.Load("ES", "M5", DateTime.MinValue, DateTime.MaxValue));
    }
}
