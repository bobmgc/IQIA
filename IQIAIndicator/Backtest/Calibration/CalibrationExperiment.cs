using System;
using System.Collections.Generic;

namespace IQIAIndicator.Backtest.Calibration;

/// <summary>
/// Sprint 15.25 (Lot 14.9, brief §3/§18/§19/§20). One fully-specified, reproducible calibration
/// experiment: <see cref="ParameterSet"/> + <see cref="Dataset"/> + <see cref="Windows"/> +
/// <see cref="Setup"/> (pipeline/measurement/execution/risk configuration) + <see cref="ProtocolVersion"/>.
///
/// <see cref="ExperimentId"/> and <see cref="ConfigurationFingerprint"/> are DERIVED, never supplied by the
/// caller (brief §19: "Ne pas utiliser GUID aléatoire comme seule identité") - two experiments built from
/// identical inputs always compute the identical Id/fingerprint (brief §20/§31), and any change to any
/// input (a parameter value, the dataset, a window boundary, any configuration) changes both (brief
/// §37/§38).
///
/// Rejected, never silently accepted (brief §15/§62 "STOP et documenter"): a dataset that cannot satisfy
/// <see cref="CalibrationExperimentSetup.WarmupContract"/> ahead of TRAIN fails <see cref="TryCreate"/>
/// with an explicit reason instead of producing an experiment that would look-ahead into VALIDATION/OOS to
/// find its warmup.
/// </summary>
public sealed class CalibrationExperiment
{
    private CalibrationExperiment(
        CalibrationParameterSet parameterSet,
        CalibrationDataset dataset,
        CalibrationWindowSet windows,
        CalibrationExperimentSetup setup,
        string configurationFingerprint,
        string experimentId)
    {
        ParameterSet = parameterSet;
        Dataset = dataset;
        Windows = windows;
        Setup = setup;
        ConfigurationFingerprint = configurationFingerprint;
        ExperimentId = experimentId;
    }

    public CalibrationParameterSet ParameterSet { get; }

    public CalibrationDataset Dataset { get; }

    public CalibrationWindowSet Windows { get; }

    public CalibrationExperimentSetup Setup { get; }

    public string ProtocolVersion => CalibrationProtocolVersion.Current;

    public string ConfigurationFingerprint { get; }

    public string ExperimentId { get; }

    public static CalibrationExperiment Create(
        CalibrationParameterSet parameterSet, CalibrationDataset dataset, CalibrationWindowSet windows, CalibrationExperimentSetup setup)
    {
        if (!TryCreate(parameterSet, dataset, windows, setup, out CalibrationExperiment? experiment, out IReadOnlyList<string> errors))
            throw new ArgumentException($"CalibrationExperiment is invalid: {string.Join(" | ", errors)}");

        return experiment;
    }

    public static bool TryCreate(
        CalibrationParameterSet parameterSet,
        CalibrationDataset dataset,
        CalibrationWindowSet windows,
        CalibrationExperimentSetup setup,
        out CalibrationExperiment experiment,
        out IReadOnlyList<string> errors)
    {
        ArgumentNullException.ThrowIfNull(parameterSet);
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(setup);

        // Brief §15/§16: the pipeline's warmup requirement must be satisfiable entirely from bars strictly
        // before TRAIN - never checked-and-ignored, never satisfied by borrowing VALIDATION/OOS bars.
        if (!setup.WarmupContract.TryValidate(dataset.Series, windows.Train, out errors))
        {
            experiment = null!;
            return false;
        }

        string configurationFingerprint = CalibrationFingerprint.ComputeConfigurationFingerprint(parameterSet, dataset, windows, setup);
        string experimentId = CalibrationFingerprint.ComputeExperimentId(configurationFingerprint);

        experiment = new CalibrationExperiment(parameterSet, dataset, windows, setup, configurationFingerprint, experimentId);
        errors = Array.Empty<string>();
        return true;
    }
}
