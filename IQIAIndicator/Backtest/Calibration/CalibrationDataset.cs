using System;
using IQIAIndicator.Core.MarketData;

namespace IQIAIndicator.Backtest.Calibration;

/// <summary>
/// Sprint 15.25 (Lot 14.9, brief §9/§10). Pairs a declared <see cref="CalibrationDatasetSpecification"/>
/// with the actual, already-loaded <see cref="HistoricalSeries"/> it describes. Brief §9: "NE PAS
/// télécharger automatiquement de nouvelles données à chaque expérience si une série déjà fingerprintée
/// peut être réutilisée" - this type is exactly that reusable pairing: build it ONCE per loaded series,
/// then hand the SAME instance to every <see cref="CalibrationExperiment"/> that should share it (a grid of
/// N parameter sets over one dataset loads Yahoo/CSV exactly once, never N times).
///
/// <see cref="Fingerprint"/> reuses <see cref="HistoricalSeriesFingerprint.Compute"/> verbatim (brief §10:
/// "NE PAS créer un deuxième algorithme SHA inutilement"), computed once at construction so a dataset
/// mutation (brief §38 - a hand-edited copy of <see cref="Series"/> wrapped in a NEW
/// <see cref="CalibrationDataset"/>) naturally produces a different fingerprint without this type doing
/// anything extra.
/// </summary>
public sealed class CalibrationDataset
{
    public CalibrationDataset(CalibrationDatasetSpecification specification, HistoricalSeries series)
    {
        ArgumentNullException.ThrowIfNull(specification);
        ArgumentNullException.ThrowIfNull(series);

        Specification = specification;
        Series = series;
        Fingerprint = HistoricalSeriesFingerprint.Compute(series);
    }

    public CalibrationDatasetSpecification Specification { get; }

    public HistoricalSeries Series { get; }

    public string Fingerprint { get; }
}
