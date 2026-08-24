using System;
using System.Collections.Generic;
using System.Linq;
using IQIAIndicator.Core.MarketData;

namespace IQIAIndicator.Backtest.Calibration;

/// <summary>
/// Sprint 15.25 (Lot 14.9, brief §15/§16). Makes explicit the distinction the brief demands between DATA
/// REQUIRED FOR WARMUP and DATA USED FOR CALIBRATION RESULT: the pipeline (Lot 14.3's RunSignalPipeline)
/// needs <see cref="RequiredWarmupBars"/> leading bars of history before it can produce a trustworthy
/// signal, and those leading bars must come from strictly BEFORE <see cref="CalibrationWindowRole.Train"/>
/// starts - never from VALIDATION or OOS (brief §16: "Ne jamais utiliser des données OOS comme warmup").
///
/// This type only VALIDATES that a dataset provides enough lead-in before TRAIN; it runs nothing itself.
/// <see cref="CalibrationExperimentRunner"/> still runs <see cref="BacktestEngine"/> over the WHOLE dataset
/// with a plain integer warmup count (exactly as every prior lot already does - the engine's own warmup
/// concept is untouched, brief §15/§62: no pipeline modification), then buckets results into
/// TRAIN/VALIDATION/OOS purely by calendar timestamp - any bar strictly before TRAIN.Start is, by
/// construction, excluded from every window's result regardless of the engine's own warmup flag.
/// </summary>
public sealed class CalibrationWarmupContract
{
    public CalibrationWarmupContract(int requiredWarmupBars)
    {
        if (requiredWarmupBars < 0)
            throw new ArgumentOutOfRangeException(nameof(requiredWarmupBars), requiredWarmupBars, "RequiredWarmupBars cannot be negative.");

        RequiredWarmupBars = requiredWarmupBars;
    }

    public int RequiredWarmupBars { get; }

    /// <summary>True when at least <see cref="RequiredWarmupBars"/> bars of <paramref name="series"/> lie
    /// strictly before <paramref name="train"/>.Start - i.e. the pipeline's warmup requirement is
    /// satisfiable entirely from CONTEXT bars, never from data that would otherwise count as a TRAIN
    /// result.</summary>
    public bool TryValidate(HistoricalSeries series, CalibrationWindow train, out IReadOnlyList<string> errors)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(train);

        int leadInBars = series.Bars.Count(b => b.Timestamp < train.Start);

        if (leadInBars < RequiredWarmupBars)
        {
            errors = new[]
            {
                $"Insufficient warmup: TRAIN starts at {train.Start:O} but only {leadInBars} dataset bar(s) " +
                $"precede it, while the pipeline requires {RequiredWarmupBars} (DATA REQUIRED FOR WARMUP). " +
                "Extend the dataset's lead-in before TRAIN.Start, or move TRAIN.Start later - never borrow " +
                "bars from VALIDATION/OOS, and never silently reduce the requirement (brief §15/§16)."
            };
            return false;
        }

        errors = Array.Empty<string>();
        return true;
    }
}
