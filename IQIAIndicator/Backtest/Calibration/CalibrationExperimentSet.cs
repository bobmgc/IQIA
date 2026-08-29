using System;
using System.Collections.Generic;
using System.Linq;

namespace IQIAIndicator.Backtest.Calibration;

/// <summary>
/// Sprint 15.25 (Lot 14.9, brief §28). Turns a deterministically-ordered list of
/// <see cref="CalibrationParameterSet"/> (typically from <see cref="CalibrationGrid.GenerateParameterSets"/>,
/// but any explicit list works - brief §27's single-experiment case is just a one-element list) into
/// <see cref="CalibrationExperiment"/>s sharing the SAME <see cref="CalibrationDataset"/>/
/// <see cref="CalibrationWindowSet"/>/<see cref="CalibrationExperimentSetup"/> - the dataset is loaded once
/// and reused across every experiment (brief §9), never re-downloaded per combination.
///
/// Preserves the input order exactly (brief §29: grid order is itself part of what must be deterministic) -
/// this method performs no sorting, filtering, or selection of its own.
/// </summary>
public static class CalibrationExperimentSet
{
    public static IReadOnlyList<CalibrationExperiment> Build(
        IReadOnlyList<CalibrationParameterSet> parameterSets,
        CalibrationDataset dataset,
        CalibrationWindowSet windows,
        CalibrationExperimentSetup setup)
    {
        ArgumentNullException.ThrowIfNull(parameterSets);

        return parameterSets
            .Select(parameterSet => CalibrationExperiment.Create(parameterSet, dataset, windows, setup))
            .ToArray();
    }
}
