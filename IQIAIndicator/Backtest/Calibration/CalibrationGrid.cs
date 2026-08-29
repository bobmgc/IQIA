using System;
using System.Collections.Generic;
using System.Linq;

namespace IQIAIndicator.Backtest.Calibration;

/// <summary>
/// Sprint 15.25 (Lot 14.9, brief §28/§29). One calibratable parameter's candidate values for a grid sweep -
/// e.g. Name="AmbiguityThreshold", Values=[0.90, 0.95, 1.00]. Every value must already share the same
/// <see cref="CalibrationParameter.Name"/>/<see cref="CalibrationParameter.Type"/>/
/// <see cref="CalibrationParameter.Unit"/> - an axis cannot silently mix two different parameters.
/// </summary>
public sealed record CalibrationGridAxis
{
    public required IReadOnlyList<CalibrationParameter> Values { get; init; }

    public static CalibrationGridAxis Create(IReadOnlyList<CalibrationParameter> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        if (values.Count == 0)
            throw new ArgumentException("A CalibrationGridAxis must have at least one candidate value.", nameof(values));

        string name = values[0].Name;
        CalibrationParameterType type = values[0].Type;
        string unit = values[0].Unit;

        foreach (CalibrationParameter value in values)
        {
            if (!string.Equals(value.Name, name, StringComparison.Ordinal) || value.Type != type || !string.Equals(value.Unit, unit, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"CalibrationGridAxis values must share the same Name/Type/Unit. Expected Name={name}/Type={type}/Unit={unit}, " +
                    $"found Name={value.Name}/Type={value.Type}/Unit={value.Unit}.");
            }
        }

        return new CalibrationGridAxis { Values = values.ToArray() };
    }
}

/// <summary>
/// Sprint 15.25 (Lot 14.9, brief §28/§29/§53). Generates the deterministic cartesian product of one or more
/// <see cref="CalibrationGridAxis"/> as <see cref="CalibrationParameterSet"/> instances - NEVER selects,
/// ranks, or discards a combination (brief §5/§28: "Ne pas choisir la meilleure combinaison").
///
/// ORDER (brief §29, worked example): the FIRST axis varies SLOWEST (outermost loop), the LAST axis varies
/// FASTEST (innermost loop) - A={0.90,0.95,1.00}, B={10,20} enumerates
/// (0.90,10)(0.90,20)(0.95,10)(0.95,20)(1.00,10)(1.00,20), exactly the brief's own example order.
/// </summary>
public static class CalibrationGrid
{
    public static IReadOnlyList<CalibrationParameterSet> GenerateParameterSets(IReadOnlyList<CalibrationGridAxis> axes)
    {
        ArgumentNullException.ThrowIfNull(axes);

        if (axes.Count == 0)
            return Array.Empty<CalibrationParameterSet>();

        var combinations = new List<IReadOnlyList<CalibrationParameter>>();
        Recurse(axes, 0, new List<CalibrationParameter>(axes.Count), combinations);

        return combinations.Select(CalibrationParameterSet.Create).ToArray();
    }

    /// <summary>Total combination count without materializing them - brief §53's own check
    /// (2 axes of 2 and 3 values must report exactly 6).</summary>
    public static long Count(IReadOnlyList<CalibrationGridAxis> axes) =>
        axes.Aggregate(1L, (product, axis) => product * axis.Values.Count);

    private static void Recurse(
        IReadOnlyList<CalibrationGridAxis> axes, int axisIndex, List<CalibrationParameter> current, List<IReadOnlyList<CalibrationParameter>> results)
    {
        if (axisIndex == axes.Count)
        {
            results.Add(current.ToArray());
            return;
        }

        foreach (CalibrationParameter value in axes[axisIndex].Values)
        {
            current.Add(value);
            Recurse(axes, axisIndex + 1, current, results);
            current.RemoveAt(current.Count - 1);
        }
    }
}
