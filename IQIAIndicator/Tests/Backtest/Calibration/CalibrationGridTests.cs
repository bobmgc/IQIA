using System.Collections.Generic;
using System.Linq;
using IQIAIndicator.Backtest.Calibration;
using Xunit;

namespace IQIAIndicator.Tests.BacktestTests.Calibration;

/// <summary>Sprint 15.25 (Lot 14.9, brief §28/§29/§53). <see cref="CalibrationGrid"/>: exact combination
/// count, deterministic order, and axis validation.</summary>
public sealed class CalibrationGridTests
{
    private static CalibrationGridAxis AxisA() => CalibrationGridAxis.Create(new[]
    {
        CalibrationParameter.Decimal("A", 0.90m, "score"),
        CalibrationParameter.Decimal("A", 0.95m, "score"),
        CalibrationParameter.Decimal("A", 1.00m, "score")
    });

    private static CalibrationGridAxis AxisB() => CalibrationGridAxis.Create(new[]
    {
        CalibrationParameter.Integer("B", 10, "bars"),
        CalibrationParameter.Integer("B", 20, "bars")
    });

    // ── §53: 2 x 3 = 6 ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void GenerateParameterSets_TwoAxesOfTwoAndThree_Produces6Combinations()
    {
        IReadOnlyList<CalibrationParameterSet> sets = CalibrationGrid.GenerateParameterSets(new[] { AxisB(), AxisA() });

        Assert.Equal(6, sets.Count);
        Assert.Equal(6, CalibrationGrid.Count(new[] { AxisB(), AxisA() }));
    }

    // ── §29: A outer/slowest, B inner/fastest ───────────────────────────────────────────────────────

    [Fact]
    public void GenerateParameterSets_FirstAxisVariesSlowest_LastAxisVariesFastest()
    {
        IReadOnlyList<CalibrationParameterSet> sets = CalibrationGrid.GenerateParameterSets(new[] { AxisA(), AxisB() });

        var actual = sets.Select(s => (A: s.TryGet("A")!.DecimalValue, B: s.TryGet("B")!.IntegerValue)).ToList();
        var expected = new (decimal? A, long? B)[]
        {
            (0.90m, 10), (0.90m, 20),
            (0.95m, 10), (0.95m, 20),
            (1.00m, 10), (1.00m, 20)
        };

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void GenerateParameterSets_EmptyAxisList_ProducesNoSets()
    {
        Assert.Empty(CalibrationGrid.GenerateParameterSets(System.Array.Empty<CalibrationGridAxis>()));
    }

    [Fact]
    public void Axis_MixingDifferentNames_IsRejected()
    {
        Assert.Throws<System.ArgumentException>(() => CalibrationGridAxis.Create(new[]
        {
            CalibrationParameter.Decimal("A", 0.90m, "score"),
            CalibrationParameter.Decimal("B", 0.95m, "score")
        }));
    }

    [Fact]
    public void Axis_EmptyValueList_IsRejected()
    {
        Assert.Throws<System.ArgumentException>(() => CalibrationGridAxis.Create(System.Array.Empty<CalibrationParameter>()));
    }
}
