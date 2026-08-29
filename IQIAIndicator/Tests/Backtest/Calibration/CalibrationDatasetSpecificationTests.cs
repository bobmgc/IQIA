using System;
using IQIAIndicator.Backtest.Calibration;
using Xunit;

namespace IQIAIndicator.Tests.BacktestTests.Calibration;

/// <summary>Sprint 15.25 (Lot 14.9, brief §9/§51). <see cref="CalibrationDatasetSpecification"/> construction
/// and validation.</summary>
public sealed class CalibrationDatasetSpecificationTests
{
    private static readonly DateTime Start = new(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = new(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_WithValidInputs_Succeeds()
    {
        CalibrationDatasetSpecification spec = CalibrationDatasetSpecification.Create("Yahoo", "MES=F", "M5", Start, End);

        Assert.Equal("Yahoo", spec.Provider);
        Assert.Equal("MES=F", spec.Symbol);
        Assert.Equal("M5", spec.Timeframe);
        Assert.Equal(Start, spec.Start);
        Assert.Equal(End, spec.End);
    }

    [Fact]
    public void Create_EndNotAfterStart_Throws()
    {
        Assert.Throws<ArgumentException>(() => CalibrationDatasetSpecification.Create("Yahoo", "MES=F", "M5", End, Start));
        Assert.Throws<ArgumentException>(() => CalibrationDatasetSpecification.Create("Yahoo", "MES=F", "M5", Start, Start));
    }

    [Theory]
    [InlineData("", "MES=F", "M5")]
    [InlineData("Yahoo", "", "M5")]
    [InlineData("Yahoo", "MES=F", "")]
    public void Create_MissingRequiredField_Throws(string provider, string symbol, string timeframe)
    {
        Assert.Throws<ArgumentException>(() => CalibrationDatasetSpecification.Create(provider, symbol, timeframe, Start, End));
    }
}
