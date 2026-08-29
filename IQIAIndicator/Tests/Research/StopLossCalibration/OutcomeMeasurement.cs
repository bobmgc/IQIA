using System.Collections.Generic;

namespace IQIAIndicator.Tests.Research.StopLossCalibration;

/// <summary>
/// Sprint 15.10. Result of walking the price path AFTER an entry bar. Every value here is direction-
/// adjusted at construction time (OutcomeSimulator), so a BUY's downside and a SELL's upside both read
/// as "adverse" - consumers never re-flip a sign.
///
/// Sign/unit convention (locked before any calibration run, per Sprint 15.10 §6):
/// - AdverseExcursionPath[h-1] / FavorableExcursionPath[h-1]: price-unit magnitude, ALWAYS >= 0, the
///   running (monotonically non-decreasing) worst/best move against/for the position up to and
///   including bar (EntryBarIndex + h).
/// - MaxAdverseExcursion (MAE) / MaxFavorableExcursion (MFE): the final value of each path
///   (h = BarsAvailable), i.e. the worst/best excursion over the whole measured horizon. Both >= 0.
/// - EquilibriumBar: the smallest h (1-based) at which price reached the entry-time
///   EstimatedEquilibrium in the favorable direction; null if never reached within BarsAvailable.
/// - BarsAvailable may be less than Horizon near the end of a series - this is never silently padded;
///   callers must read BarsAvailable, not assume it equals Horizon.
/// </summary>
public sealed record OutcomeMeasurement(
    int EntryBarIndex,
    int Horizon,
    int BarsAvailable,
    IReadOnlyList<double> AdverseExcursionPath,
    IReadOnlyList<double> FavorableExcursionPath,
    int? EquilibriumBar,
    double MaxAdverseExcursion,
    double MaxFavorableExcursion);
