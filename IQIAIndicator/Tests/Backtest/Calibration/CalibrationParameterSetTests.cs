using System;
using IQIAIndicator.Backtest.Calibration;
using Xunit;

namespace IQIAIndicator.Tests.BacktestTests.Calibration;

/// <summary>Sprint 15.25 (Lot 14.9, brief §8/§37/§51/§52). <see cref="CalibrationParameterSet"/>: uniqueness,
/// immutability, and fingerprint identity/mutation behaviour.</summary>
public sealed class CalibrationParameterSetTests
{
    [Fact]
    public void Create_WithUniqueNames_Succeeds()
    {
        CalibrationParameterSet set = CalibrationParameterSet.Create(new[]
        {
            CalibrationParameter.Decimal("A", 0.95m, "score"),
            CalibrationParameter.Integer("B", 10, "bars")
        });

        Assert.Equal(2, set.Parameters.Count);
    }

    [Fact]
    public void Create_WithDuplicateNames_Throws()
    {
        Assert.Throws<ArgumentException>(() => CalibrationParameterSet.Create(new[]
        {
            CalibrationParameter.Decimal("A", 0.90m, "score"),
            CalibrationParameter.Decimal("A", 0.95m, "score")
        }));
    }

    [Fact]
    public void TryGet_FindsParameterByName_CaseSensitive()
    {
        CalibrationParameterSet set = CalibrationParameterSet.Create(new[] { CalibrationParameter.Decimal("Alpha", 1m, "ratio") });

        Assert.NotNull(set.TryGet("Alpha"));
        Assert.Null(set.TryGet("alpha"));
        Assert.Null(set.TryGet("Missing"));
    }

    // ── §52: same logical set, two instances -> same fingerprint ───────────────────────────────────

    [Fact]
    public void TwoInstances_WithIdenticalParameters_ProduceIdenticalFingerprint()
    {
        CalibrationParameterSet a = CalibrationParameterSet.Create(new[] { CalibrationParameter.Decimal("A", 0.95m, "score") });
        CalibrationParameterSet b = CalibrationParameterSet.Create(new[] { CalibrationParameter.Decimal("A", 0.95m, "score") });

        Assert.Equal(CalibrationFingerprint.ComputeParameterSetFingerprint(a), CalibrationFingerprint.ComputeParameterSetFingerprint(b));
    }

    [Fact]
    public void ParameterOrder_DoesNotAffectFingerprint()
    {
        CalibrationParameterSet a = CalibrationParameterSet.Create(new[]
        {
            CalibrationParameter.Decimal("A", 0.95m, "score"),
            CalibrationParameter.Integer("B", 10, "bars")
        });
        CalibrationParameterSet b = CalibrationParameterSet.Create(new[]
        {
            CalibrationParameter.Integer("B", 10, "bars"),
            CalibrationParameter.Decimal("A", 0.95m, "score")
        });

        Assert.Equal(CalibrationFingerprint.ComputeParameterSetFingerprint(a), CalibrationFingerprint.ComputeParameterSetFingerprint(b));
    }

    // ── §37: modifying a "copy" never mutates the original, and produces a different fingerprint ────

    [Fact]
    public void ConfigurationMutationTest_ModifiedCopy_HasDifferentFingerprint_OriginalUnchanged()
    {
        CalibrationParameterSet original = CalibrationParameterSet.Create(new[] { CalibrationParameter.Decimal("A", 0.95m, "score") });
        string originalFingerprint = CalibrationFingerprint.ComputeParameterSetFingerprint(original);

        CalibrationParameterSet modified = CalibrationParameterSet.Create(new[] { CalibrationParameter.Decimal("A", 0.90m, "score") });
        string modifiedFingerprint = CalibrationFingerprint.ComputeParameterSetFingerprint(modified);

        Assert.NotEqual(originalFingerprint, modifiedFingerprint);
        // The "original" instance itself is untouched - re-reading its fingerprint gives the same value.
        Assert.Equal(originalFingerprint, CalibrationFingerprint.ComputeParameterSetFingerprint(original));
        Assert.Equal(0.95m, original.TryGet("A")!.DecimalValue);
    }
}
