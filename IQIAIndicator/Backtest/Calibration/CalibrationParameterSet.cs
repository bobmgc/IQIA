using System;
using System.Collections.Generic;
using System.Linq;

namespace IQIAIndicator.Backtest.Calibration;

/// <summary>
/// Sprint 15.25 (Lot 14.9, brief §4/§8). An immutable, named collection of <see cref="CalibrationParameter"/>
/// - the unit of "what varies between two experiments". Deliberately does not enforce which parameter
/// names must be present (brief §6: not every candidate parameter needs to be exposed by this lot) - only
/// that, whatever IS present, names are unique (a set cannot claim two different values for the same named
/// parameter at once) and every parameter's own construction-time validation already passed (see
/// <see cref="CalibrationParameter"/>).
///
/// Once built, NEVER mutated (brief §8: "CalibrationParameterSet ne doit plus être muté") - there is no
/// method here that changes an existing instance. A "modified copy" is always a brand-new
/// <see cref="Create"/> call with a replaced parameter list, leaving the original instance and its own
/// fingerprint untouched (brief §37).
/// </summary>
public sealed record CalibrationParameterSet
{
    public required IReadOnlyList<CalibrationParameter> Parameters { get; init; }

    public static CalibrationParameterSet Create(IReadOnlyList<CalibrationParameter> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        List<string> duplicates = parameters
            .GroupBy(p => p.Name, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicates.Count > 0)
            throw new ArgumentException($"CalibrationParameterSet cannot contain duplicate parameter names: {string.Join(", ", duplicates)}.");

        return new CalibrationParameterSet { Parameters = parameters.ToArray() };
    }

    public static CalibrationParameterSet Empty() => new() { Parameters = Array.Empty<CalibrationParameter>() };

    public CalibrationParameter? TryGet(string name) =>
        Parameters.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal));
}
