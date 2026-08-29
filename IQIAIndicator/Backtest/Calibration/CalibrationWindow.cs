using System;

namespace IQIAIndicator.Backtest.Calibration;

/// <summary>
/// Sprint 15.25 (Lot 14.9, brief §11). An immutable, role-tagged time range - the calibration-specific
/// sibling of <see cref="BacktestWindow"/> (Lot 14.1), whose <see cref="Role"/> is a closed enum instead of
/// free text (see <see cref="CalibrationWindowRole"/>'s own doc comment). Same half-open convention as
/// every other window type in this codebase: <see cref="Start"/> inclusive, <see cref="End"/> exclusive.
/// </summary>
public sealed class CalibrationWindow
{
    public CalibrationWindow(CalibrationWindowRole role, DateTime start, DateTime end)
    {
        if (end <= start)
            throw new ArgumentException($"CalibrationWindow '{role}': End ({end:O}) must be strictly after Start ({start:O}).", nameof(end));

        Role = role;
        Start = start;
        End = end;
    }

    public CalibrationWindowRole Role { get; }

    public DateTime Start { get; }

    public DateTime End { get; }

    /// <summary>True when <paramref name="timestamp"/> falls in [Start, End).</summary>
    public bool Contains(DateTime timestamp) => timestamp >= Start && timestamp < End;

    /// <summary>Adapter to the pre-existing <see cref="BacktestWindow"/> shape, for any code path that still
    /// wants the Lot 14.1 type. Never the reverse: <see cref="BacktestEngine"/> does not itself filter bars
    /// by <see cref="BacktestScenario.Window"/> (verified by inspection - it walks the whole series), so
    /// this adapter exists for identity/labelling purposes only, never as the mechanism that actually
    /// separates TRAIN/VALIDATION/OOS results (see <see cref="CalibrationExperimentRunner"/> for that).</summary>
    public BacktestWindow ToBacktestWindow() => new(Role.ToString().ToUpperInvariant(), Start, End);

    public override string ToString() => $"{Role} [{Start:O} .. {End:O})";
}
