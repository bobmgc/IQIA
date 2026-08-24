using System.Collections.Generic;
using System.Linq;
using IQIAIndicator.Backtest.Calibration;
using Xunit;

namespace IQIAIndicator.Tests.BacktestTests.Calibration;

/// <summary>Sprint 15.25 (Lot 14.9, brief §29/§30). Two calls to <see cref="CalibrationGrid.GenerateParameterSets"/>
/// with the same axes always produce the same order AND the same fingerprints - a grid is a pure function
/// of its axes, never re-randomized between calls.</summary>
public sealed class CalibrationGridDeterminismTests
{
    private static IReadOnlyList<CalibrationGridAxis> Axes() => new[]
    {
        CalibrationGridAxis.Create(new[]
        {
            CalibrationParameter.Decimal("A", 0.90m, "score"),
            CalibrationParameter.Decimal("A", 0.95m, "score")
        }),
        CalibrationGridAxis.Create(new[]
        {
            CalibrationParameter.Integer("B", 10, "bars"),
            CalibrationParameter.Integer("B", 20, "bars"),
            CalibrationParameter.Integer("B", 30, "bars")
        })
    };

    [Fact]
    public void RepeatedCalls_ProduceIdenticalFingerprintSequences()
    {
        List<string> first = CalibrationGrid.GenerateParameterSets(Axes()).Select(CalibrationFingerprint.ComputeParameterSetFingerprint).ToList();
        List<string> second = CalibrationGrid.GenerateParameterSets(Axes()).Select(CalibrationFingerprint.ComputeParameterSetFingerprint).ToList();

        Assert.Equal(first, second);
    }

    // ── §30: duplicate logical configuration -> same fingerprint, never two different identities ────

    [Fact]
    public void DuplicateParameterValues_AcrossTwoIndependentGridRuns_ProduceTheSameFingerprint()
    {
        CalibrationParameterSet a = CalibrationGrid.GenerateParameterSets(Axes())[0];
        CalibrationParameterSet b = CalibrationGrid.GenerateParameterSets(Axes())[0];

        Assert.NotSame(a, b);
        Assert.Equal(CalibrationFingerprint.ComputeParameterSetFingerprint(a), CalibrationFingerprint.ComputeParameterSetFingerprint(b));
    }
}
