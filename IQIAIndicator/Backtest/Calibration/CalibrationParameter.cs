using System;

namespace IQIAIndicator.Backtest.Calibration;

/// <summary>
/// Sprint 15.25 (Lot 14.9, brief §4/§39/§40/§41). One named, typed, deterministic, calibratable input.
/// Deliberately generic (brief §4: "Commencer par une abstraction générique") rather than one bespoke
/// class per production constant - AmbiguityThreshold, RiskDistanceTicks, MeasurementHorizonBars etc. are
/// all just instances of this one shape, distinguished by <see cref="Name"/>/<see cref="Type"/>/
/// <see cref="Unit"/>.
///
/// Exactly one of <see cref="IntegerValue"/>/<see cref="DecimalValue"/>/<see cref="BooleanValue"/>/
/// <see cref="EnumValue"/> is populated, matching <see cref="Type"/> - never a bare <c>string</c>
/// catch-all (brief §39). <see cref="Min"/>/<see cref="Max"/>/<see cref="Step"/> are optional bounds
/// (brief §41): when present, a value outside [Min, Max] or a non-positive Step is REJECTED at
/// construction time, never silently clamped (brief §41: "Ne pas la clamp automatiquement").
///
/// Immutable (brief §8: once used by an experiment, never mutated) - every factory returns a brand-new
/// instance; there is no setter anywhere on this type. A "modified" parameter is always a fresh call to
/// one of the factories below, leaving any prior instance untouched.
/// </summary>
public sealed record CalibrationParameter
{
    public required string Name { get; init; }

    public required CalibrationParameterType Type { get; init; }

    /// <summary>Free-text unit documenting scale (brief §40): "score", "bars", "ratio", "points", "ticks" -
    /// never conflated (0.5% is not the same number as 0.5 - the unit is what disambiguates them).</summary>
    public required string Unit { get; init; }

    public long? IntegerValue { get; init; }

    public decimal? DecimalValue { get; init; }

    public bool? BooleanValue { get; init; }

    public string? EnumValue { get; init; }

    /// <summary>Optional inclusive lower bound (Integer/Decimal only, brief §41). Always stored as
    /// <see cref="decimal"/> regardless of <see cref="Type"/>, since it is a bound, not the value itself.</summary>
    public decimal? Min { get; init; }

    /// <summary>Optional inclusive upper bound (Integer/Decimal only, brief §41).</summary>
    public decimal? Max { get; init; }

    /// <summary>Optional grid step (Integer/Decimal only, brief §41/§42) - documentation/validation only in
    /// this lot; nothing here auto-generates a range from it (brief §5: no automatic sweep is implied by a
    /// single parameter carrying a Step).</summary>
    public decimal? Step { get; init; }

    public static CalibrationParameter Integer(string name, long value, string unit, long? min = null, long? max = null, long? step = null)
    {
        ValidateNameAndUnit(name, unit);
        decimal? minD = min is long lo ? lo : null;
        decimal? maxD = max is long hi ? hi : null;
        decimal? stepD = step is long s ? s : null;
        ValidateRange(value, minD, maxD, stepD);

        return new CalibrationParameter
        {
            Name = name, Type = CalibrationParameterType.Integer, Unit = unit,
            IntegerValue = value, Min = minD, Max = maxD, Step = stepD
        };
    }

    public static CalibrationParameter Decimal(string name, decimal value, string unit, decimal? min = null, decimal? max = null, decimal? step = null)
    {
        ValidateNameAndUnit(name, unit);
        ValidateRange(value, min, max, step);

        return new CalibrationParameter
        {
            Name = name, Type = CalibrationParameterType.Decimal, Unit = unit,
            DecimalValue = value, Min = min, Max = max, Step = step
        };
    }

    public static CalibrationParameter Boolean(string name, bool value, string unit)
    {
        ValidateNameAndUnit(name, unit);

        return new CalibrationParameter { Name = name, Type = CalibrationParameterType.Boolean, Unit = unit, BooleanValue = value };
    }

    public static CalibrationParameter Enum(string name, string value, string unit)
    {
        ValidateNameAndUnit(name, unit);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        return new CalibrationParameter { Name = name, Type = CalibrationParameterType.Enum, Unit = unit, EnumValue = value };
    }

    private static void ValidateNameAndUnit(string name, string unit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(unit);
    }

    private static void ValidateRange(decimal value, decimal? min, decimal? max, decimal? step)
    {
        if (min is decimal lo && max is decimal hi && lo > hi)
            throw new ArgumentException($"Min ({lo}) must not exceed Max ({hi}).");

        if (min is decimal loBound && value < loBound)
            throw new ArgumentOutOfRangeException(nameof(value), value, $"Value is below Min ({loBound}) - rejected, never clamped (brief §41).");

        if (max is decimal hiBound && value > hiBound)
            throw new ArgumentOutOfRangeException(nameof(value), value, $"Value is above Max ({hiBound}) - rejected, never clamped (brief §41).");

        if (step is decimal stepValue && stepValue <= 0m)
            throw new ArgumentOutOfRangeException(nameof(step), stepValue, "Step, when supplied, must be strictly positive (brief §41/§42).");
    }
}
