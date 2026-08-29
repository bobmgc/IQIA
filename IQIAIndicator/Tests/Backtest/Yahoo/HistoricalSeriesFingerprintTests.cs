using System;
using System.Collections.Generic;
using IQIAIndicator.Backtest;
using IQIAIndicator.Core.MarketData;
using Xunit;

namespace IQIAIndicator.Tests.BacktestTests.Yahoo;

/// <summary>Sprint 15.25 (Lot 14.2, brief §18/§19 items 27-28). HistoricalSeriesFingerprint depends only
/// on the data actually ingested - never on download time, machine, user, or a request URL.</summary>
public sealed class HistoricalSeriesFingerprintTests
{
    private static readonly DateTime T0 = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static HistoricalSeries Series(decimal firstClose = 100.5m) => HistoricalSeries.Create(
        "MES", "M5", "UTC", "Yahoo(Continuous)",
        new List<HistoricalBar>
        {
            new(T0, 100m, 101m, 99m, firstClose, 10m),
            new(T0.AddMinutes(5), 100.5m, 102m, 100m, 101.5m, 12m)
        });

    [Fact]
    public void SameSeriesContent_ProducesTheSameFingerprint()
    {
        string a = HistoricalSeriesFingerprint.Compute(Series());
        string b = HistoricalSeriesFingerprint.Compute(Series());

        Assert.Equal(a, b);
    }

    [Fact]
    public void ChangedBarValue_ProducesADifferentFingerprint()
    {
        string a = HistoricalSeriesFingerprint.Compute(Series(firstClose: 100.5m));
        string b = HistoricalSeriesFingerprint.Compute(Series(firstClose: 100.6m));

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void DifferentProvider_ProducesADifferentFingerprint_SameOhlcv()
    {
        HistoricalSeries a = HistoricalSeries.Create("MES", "M5", "UTC", "Yahoo(Continuous)",
            new List<HistoricalBar> { new(T0, 100m, 101m, 99m, 100.5m, 10m) });
        HistoricalSeries b = HistoricalSeries.Create("MES", "M5", "UTC", "CsvFixture",
            new List<HistoricalBar> { new(T0, 100m, 101m, 99m, 100.5m, 10m) });

        Assert.NotEqual(HistoricalSeriesFingerprint.Compute(a), HistoricalSeriesFingerprint.Compute(b));
    }

    [Fact]
    public void Fingerprint_IsHexadecimalSha256Length()
    {
        string fp = HistoricalSeriesFingerprint.Compute(Series());

        Assert.Equal(64, fp.Length); // SHA-256 -> 32 bytes -> 64 hex chars
        Assert.Matches("^[0-9A-F]+$", fp);
    }
}
