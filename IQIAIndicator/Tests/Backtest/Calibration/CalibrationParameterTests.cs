using System;
using IQIAIndicator.Backtest.Calibration;
using Xunit;

namespace IQIAIndicator.Tests.BacktestTests.Calibration;

/// <summary>Sprint 15.25 (Lot 14.9, brief §39/§40/§41/§51). <see cref="CalibrationParameter"/>: typed
/// construction, unit documentation, and range/step rejection (never clamped).</summary>
public sealed class CalibrationParameterTests
{
    [Fact]
    public void Integer_StoresValueAndUnit_TypedNotString()
    {
        CalibrationParameter parameter = CalibrationParameter.Integer("MeasurementHorizonBars", 10, "bars");

        Assert.Equal(CalibrationParameterType.Integer, parameter.Type);
        Assert.Equal(10, parameter.IntegerValue);
        Assert.Null(parameter.DecimalValue);
        Assert.Equal("bars", parameter.Unit);
    }

    [Fact]
    public void Decimal_StoresValueAndUnit()
    {
        CalibrationParameter parameter = CalibrationParameter.Decimal("AmbiguityThreshold", 0.95m, "score");

        Assert.Equal(CalibrationParameterType.Decimal, parameter.Type);
        Assert.Equal(0.95m, parameter.DecimalValue);
        Assert.Equal("score", parameter.Unit);
    }

    [Fact]
    public void Boolean_StoresValue()
    {
        CalibrationParameter parameter = CalibrationParameter.Boolean("EnableRiskControls", true, "flag");

        Assert.Equal(CalibrationParameterType.Boolean, parameter.Type);
        Assert.True(parameter.BooleanValue);
    }

    [Fact]
    public void Enum_StoresValue()
    {
        CalibrationParameter parameter = CalibrationParameter.Enum("Regime", "Trending", "label");

        Assert.Equal(CalibrationParameterType.Enum, parameter.Type);
        Assert.Equal("Trending", parameter.EnumValue);
    }

    [Fact]
    public void Decimal_ValueBelowMin_IsRejected_NeverClamped()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => CalibrationParameter.Decimal("X", 0.5m, "score", min: 0.9m, max: 1.0m));
        Assert.Contains("never clamped", exception.Message);
    }

    [Fact]
    public void Decimal_ValueAboveMax_IsRejected_NeverClamped()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CalibrationParameter.Decimal("X", 1.5m, "score", min: 0.9m, max: 1.0m));
    }

    [Fact]
    public void Decimal_MinGreaterThanMax_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => CalibrationParameter.Decimal("X", 0.95m, "score", min: 1.0m, max: 0.9m));
    }

    [Fact]
    public void Decimal_NonPositiveStep_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CalibrationParameter.Decimal("X", 0.95m, "score", step: 0m));
        Assert.Throws<ArgumentOutOfRangeException>(() => CalibrationParameter.Decimal("X", 0.95m, "score", step: -1m));
    }

    [Fact]
    public void Integer_ValueWithinRange_IsAccepted()
    {
        CalibrationParameter parameter = CalibrationParameter.Integer("HorizonBars", 15, "bars", min: 5, max: 20, step: 5);
        Assert.Equal(15, parameter.IntegerValue);
        Assert.Equal(5m, parameter.Min);
        Assert.Equal(20m, parameter.Max);
        Assert.Equal(5m, parameter.Step);
    }

    [Fact]
    public void Integer_ValueOutsideRange_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CalibrationParameter.Integer("HorizonBars", 25, "bars", min: 5, max: 20));
    }

    [Fact]
    public void EmptyName_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => CalibrationParameter.Decimal("", 0.95m, "score"));
        Assert.Throws<ArgumentException>(() => CalibrationParameter.Decimal("   ", 0.95m, "score"));
    }

    [Fact]
    public void EmptyEnumValue_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => CalibrationParameter.Enum("Regime", "", "label"));
    }
}
