using System;
using System.Linq;
using IQIAIndicator.Backtest.Calibration;
using Xunit;

namespace IQIAIndicator.Tests.BacktestTests.Calibration;

/// <summary>Sprint 15.25 (Lot 14.9, brief §11/§12/§14/§54). <see cref="CalibrationWindow"/>/
/// <see cref="CalibrationWindowSet"/>: half-open range validity, dataset-bounds checking, and the
/// TRAIN &lt; VALIDATION &lt; OOS ordering rule (rejected, never auto-corrected).</summary>
public sealed class CalibrationWindowTests
{
    private static readonly DateTime T0 = new(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);

    private static CalibrationDatasetSpecification Dataset() =>
        CalibrationDatasetSpecification.Create("Yahoo", "MES=F", "M5", T0, T0.AddDays(45));

    private static CalibrationWindow Win(CalibrationWindowRole role, int startDay, int endDay) =>
        new(role, T0.AddDays(startDay), T0.AddDays(endDay));

    [Fact]
    public void Window_EndNotAfterStart_Throws()
    {
        Assert.Throws<ArgumentException>(() => new CalibrationWindow(CalibrationWindowRole.Train, T0, T0));
        Assert.Throws<ArgumentException>(() => new CalibrationWindow(CalibrationWindowRole.Train, T0.AddDays(1), T0));
    }

    [Fact]
    public void Contains_RespectsHalfOpenRange()
    {
        var window = Win(CalibrationWindowRole.Train, 0, 10);

        Assert.True(window.Contains(T0));
        Assert.True(window.Contains(T0.AddDays(9.999)));
        Assert.False(window.Contains(T0.AddDays(10)));
        Assert.False(window.Contains(T0.AddDays(-0.001)));
    }

    [Fact]
    public void WindowSet_ValidOrdering_Succeeds()
    {
        CalibrationWindowSet set = CalibrationWindowSet.Create(
            Dataset(), Win(CalibrationWindowRole.Train, 0, 30), Win(CalibrationWindowRole.Validation, 30, 37), Win(CalibrationWindowRole.Oos, 37, 45));

        Assert.Equal(CalibrationWindowRole.Train, set.Train.Role);
        Assert.Equal(CalibrationWindowRole.Validation, set.Validation.Role);
        Assert.Equal(CalibrationWindowRole.Oos, set.Oos.Role);
        Assert.Equal(3, set.All().Count());
    }

    [Fact]
    public void WindowSet_OosBeforeTrain_IsRejected()
    {
        bool ok = CalibrationWindowSet.TryCreate(
            Dataset(), Win(CalibrationWindowRole.Train, 10, 30), Win(CalibrationWindowRole.Validation, 30, 37),
            Win(CalibrationWindowRole.Oos, 0, 5), out _, out var errors);

        Assert.False(ok);
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void WindowSet_ValidationBeforeTrain_IsRejected()
    {
        bool ok = CalibrationWindowSet.TryCreate(
            Dataset(), Win(CalibrationWindowRole.Train, 20, 30), Win(CalibrationWindowRole.Validation, 0, 10),
            Win(CalibrationWindowRole.Oos, 30, 40), out _, out var errors);

        Assert.False(ok);
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void WindowSet_OverlappingWindows_IsRejected()
    {
        bool ok = CalibrationWindowSet.TryCreate(
            Dataset(), Win(CalibrationWindowRole.Train, 0, 20), Win(CalibrationWindowRole.Validation, 15, 30),
            Win(CalibrationWindowRole.Oos, 30, 40), out _, out var errors);

        Assert.False(ok);
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void WindowSet_WindowOutsideDatasetRange_IsRejected()
    {
        bool ok = CalibrationWindowSet.TryCreate(
            Dataset(), Win(CalibrationWindowRole.Train, -5, 20), Win(CalibrationWindowRole.Validation, 20, 30),
            Win(CalibrationWindowRole.Oos, 30, 40), out _, out var errors);

        Assert.False(ok);
        Assert.Contains(errors, e => e.Contains("outside the dataset range"));
    }

    [Fact]
    public void WindowSet_NeverAutoCorrects_ThrowingFormThrowsOnTheSameViolation()
    {
        Assert.Throws<ArgumentException>(() => CalibrationWindowSet.Create(
            Dataset(), Win(CalibrationWindowRole.Train, 10, 30), Win(CalibrationWindowRole.Validation, 30, 37),
            Win(CalibrationWindowRole.Oos, 0, 5)));
    }
}
