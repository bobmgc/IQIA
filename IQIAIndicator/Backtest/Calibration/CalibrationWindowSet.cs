using System;
using System.Collections.Generic;

namespace IQIAIndicator.Backtest.Calibration;

/// <summary>
/// Sprint 15.25 (Lot 14.9, brief §11/§12/§14). The three <see cref="CalibrationWindow"/>s (TRAIN,
/// VALIDATION, OOS) one experiment is evaluated against, validated together so the temporal-order rule
/// (brief §12: "TRAIN &lt; VALIDATION &lt; OOS dans le temps") is enforced once, at construction, rather
/// than trusted at every call site.
///
/// Rejects, never auto-corrects (brief §14): Start&gt;=End is already impossible per-window (the
/// <see cref="CalibrationWindow"/> constructor), so this type additionally rejects a window lying outside
/// the dataset's own [Start, End) range, TRAIN not preceding VALIDATION, VALIDATION not preceding OOS, and
/// (by the same three inequalities, given each window's own Start&lt;End) any pair of windows that overlap
/// or an OOS/VALIDATION window placed before TRAIN.
/// </summary>
public sealed class CalibrationWindowSet
{
    private CalibrationWindowSet(CalibrationWindow train, CalibrationWindow validation, CalibrationWindow oos)
    {
        Train = train;
        Validation = validation;
        Oos = oos;
    }

    public CalibrationWindow Train { get; }

    public CalibrationWindow Validation { get; }

    public CalibrationWindow Oos { get; }

    public static CalibrationWindowSet Create(CalibrationDatasetSpecification dataset, CalibrationWindow train, CalibrationWindow validation, CalibrationWindow oos)
    {
        if (!TryCreate(dataset, train, validation, oos, out CalibrationWindowSet? set, out IReadOnlyList<string> errors))
            throw new ArgumentException($"CalibrationWindowSet is invalid: {string.Join(" | ", errors)}");

        return set;
    }

    public static bool TryCreate(
        CalibrationDatasetSpecification dataset,
        CalibrationWindow train,
        CalibrationWindow validation,
        CalibrationWindow oos,
        out CalibrationWindowSet set,
        out IReadOnlyList<string> errors)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentNullException.ThrowIfNull(train);
        ArgumentNullException.ThrowIfNull(validation);
        ArgumentNullException.ThrowIfNull(oos);

        var violations = new List<string>();

        CheckWithinDataset(dataset, train, "TRAIN", violations);
        CheckWithinDataset(dataset, validation, "VALIDATION", violations);
        CheckWithinDataset(dataset, oos, "OOS", violations);

        if (train.End > validation.Start)
            violations.Add($"TRAIN must end at or before VALIDATION starts (brief §12). TRAIN.End={train.End:O}, VALIDATION.Start={validation.Start:O}.");

        if (validation.End > oos.Start)
            violations.Add($"VALIDATION must end at or before OOS starts (brief §12). VALIDATION.End={validation.End:O}, OOS.Start={oos.Start:O}.");

        if (train.End > oos.Start)
            violations.Add($"TRAIN must end at or before OOS starts (brief §12/§17: never OOS before TRAIN). TRAIN.End={train.End:O}, OOS.Start={oos.Start:O}.");

        if (violations.Count > 0)
        {
            set = null!;
            errors = violations;
            return false;
        }

        set = new CalibrationWindowSet(train, validation, oos);
        errors = Array.Empty<string>();
        return true;
    }

    private static void CheckWithinDataset(CalibrationDatasetSpecification dataset, CalibrationWindow window, string label, List<string> violations)
    {
        if (window.Start < dataset.Start || window.End > dataset.End)
        {
            violations.Add(
                $"{label} window [{window.Start:O}..{window.End:O}) lies outside the dataset range " +
                $"[{dataset.Start:O}..{dataset.End:O}).");
        }
    }

    public IEnumerable<CalibrationWindow> All()
    {
        yield return Train;
        yield return Validation;
        yield return Oos;
    }
}
