using System;
using System.Collections.Generic;
using IQIAIndicator.Engine.EntryTrigger;

namespace IQIAIndicator.Backtest.Measurement;

/// <summary>
/// Sprint 15.25 (Lot 14.4, brief §5/§9/§25/§26). Every reason a signal at bar i did not produce a
/// complete, trustworthy measurement over bars i+1..i+HorizonBars - never a silent
/// <c>catch { return null; }</c> equivalent (same discipline as Lot 14.3's
/// <see cref="BacktestSignalStatus"/>).
/// </summary>
public enum MeasurementStatus
{
    /// <summary>Full horizon available, entry price valid, every future bar in the horizon structurally
    /// valid. <see cref="MeasurementResult.Return"/>/<see cref="MeasurementResult.Mfe"/>/
    /// <see cref="MeasurementResult.Mae"/>/<see cref="MeasurementResult.HitResults"/> are all populated.</summary>
    Measured,

    /// <summary>No TradePlan was produced this bar, or its Direction is not a directional trade
    /// candidate (WATCH/NO_ACTION - brief §6/§27: "NO_ACTION ne doit pas être mesuré comme un trade").</summary>
    NotMeasurable,

    /// <summary>TradePlan.EntryPrice is missing or not strictly positive (brief §26).</summary>
    InvalidEntryPrice,

    /// <summary>Bar index i+HorizonBars exceeds the series' last index - the full horizon is not
    /// available. Brief §9/§24: never inferred/interpolated, never reported as a complete measurement.</summary>
    InsufficientFutureData,

    /// <summary>At least one bar in [i+1, i+HorizonBars] fails <c>HistoricalBar.Validate()</c> (brief
    /// §25). Unreachable through a <see cref="HistoricalSeries"/> built via its own validating
    /// constructors (every bar is already guaranteed valid there - same situation as Lot 14.1's
    /// BarsRejected) - kept as defence in depth and exercised directly against a hand-built bar list in
    /// tests (see <see cref="ScientificMeasurementEngine.MeasureCore"/>).</summary>
    InvalidFutureData
}

/// <summary>One threshold's hit outcome for one signal (brief §13/§14/§22). <see cref="Threshold"/> is
/// kept in decimal form (0.001, never 0.1%).</summary>
public sealed record MeasurementHitResult(double Threshold, bool Hit);

/// <summary>
/// Sprint 15.25 (Lot 14.4, brief §3). Output of <see cref="ScientificMeasurementEngine"/> for exactly
/// one signal bar. Deliberately does NOT recompute or reference the signal itself beyond the primitives
/// needed to report the measurement (BarIndex/Timestamp/Direction/EntryPrice) - the signal stays owned by
/// <see cref="BacktestSignalResult"/> (Lot 14.3), never duplicated or reinterpreted here (brief §3: "Le
/// moteur de mesure ne doit PAS recalculer le signal").
///
/// Carries no wall-clock field (no <c>DateTime.UtcNow</c> anywhere) - it is a pure function of
/// (HistoricalSeries, BacktestSignalResult, MeasurementConfiguration), so two calls with the same inputs
/// are bit-identical by construction, not by having to exclude a field from a fingerprint afterward
/// (contrast with Lot 14.3's <c>BacktestSignalFingerprint</c>, which does have to exclude several).
/// </summary>
public sealed record MeasurementResult(
    int SignalBarIndex,
    DateTime SignalTimestamp,
    DirectionCandidate Direction,
    decimal? EntryPrice,
    MeasurementStatus Status,
    string? Reason,
    int HorizonBars,
    double? Return,
    double? Mfe,
    double? Mae,
    IReadOnlyList<MeasurementHitResult> HitResults);
