using System;
using System.Collections.Generic;
using System.Linq;
using IQIAIndicator.Tests.GoldenDatasets;

namespace IQIAIndicator.Tests.Research.StopLossCalibration;

/// <summary>
/// Sprint 15.10 (§5 - POC). Small-scale, manually-checkable proof that the harness computes what it
/// claims to before any full calibration campaign runs. Not a grid search - just enough series to
/// verify BarMetrics/OutcomeMeasurement/StopLossEvaluator behave sanely and, critically, that the
/// look-ahead boundary between BarMetricsComputer and OutcomeSimulator actually holds (§7 was called
/// out as the most important constraint of this sprint).
/// </summary>
public static class StopLossCalibrationPocTests
{
    private const int PocSeriesLength = 200;
    private const int PocHorizon = 20;

    public static void RunAll()
    {
        AssertLookAheadSafety_TruncatingFutureBarsProducesIdenticalMetrics();
        AssertStrongMeanReversionProducesValidEntriesNearTrueMean();
        AssertExcursionsAreNonNegativeAndMonotonic();
        AssertStopHitInvariants_ZeroAndHugeDistance();
        AssertStrongMeanReversionRevertsMoreOftenThanRandomWalk();
    }

    // ── §7: look-ahead bias must be structurally impossible, not just documented ───────────────────

    private static void AssertLookAheadSafety_TruncatingFutureBarsProducesIdenticalMetrics()
    {
        decimal[] series = SyntheticSeriesCatalog.MeanRevertingOu(PocSeriesLength, seed: 42UL, kappa: 0.5m);

        foreach (int barIndex in new[] { 30, 60, 100, 150 })
        {
            BarMetrics fromFullSeries = BarMetricsComputer.Compute(series, barIndex);

            decimal[] truncated = series.Take(barIndex + 1).ToArray();
            BarMetrics fromTruncatedSeries = BarMetricsComputer.Compute(truncated, barIndex);

            Assert(fromFullSeries == fromTruncatedSeries,
                $"BarMetrics at bar {barIndex} must be identical whether computed against the full series or a copy truncated right after that bar - " +
                $"otherwise the harness is reading future data. Full={fromFullSeries}, Truncated={fromTruncatedSeries}.");
        }
    }

    // ── Sanity: strong mean reversion (kappa=0.5, price built around level 100) ─────────────────────

    private static void AssertStrongMeanReversionProducesValidEntriesNearTrueMean()
    {
        decimal[] series = SyntheticSeriesCatalog.MeanRevertingOu(PocSeriesLength, seed: 42UL, kappa: 0.5m);
        IReadOnlyList<CalibrationEntry> entries = CalibrationEntryBuilder.BuildEntries("MeanRevertingOu(k=0.5)", series, PocHorizon);

        Assert(entries.Count > 20, $"Expected a reasonable number of eligible entries on a {PocSeriesLength}-bar strongly mean-reverting series, got {entries.Count}.");

        foreach (CalibrationEntry entry in entries.Skip(entries.Count / 2)) // later entries: filter has had time to converge
        {
            Assert(entry.Metrics.EstimatedEquilibrium is double eq && eq is > 80 and < 120,
                $"EstimatedEquilibrium on a series built around level 100 should stay in a broad neighborhood of 100 once the filter has warmed up. " +
                $"Bar={entry.EntryBarIndex}, EstimatedEquilibrium={entry.Metrics.EstimatedEquilibrium}.");
            Assert(entry.Metrics.InnovationStd is double std && std > 0.0,
                $"InnovationStd must be strictly positive for every valid entry. Bar={entry.EntryBarIndex}, InnovationStd={entry.Metrics.InnovationStd}.");
        }
    }

    // ── Sanity: structural invariants that must hold for every entry, on any series ─────────────────

    private static void AssertExcursionsAreNonNegativeAndMonotonic()
    {
        decimal[] series = SyntheticSeriesCatalog.HighVolatility(PocSeriesLength, seed: 7UL);
        IReadOnlyList<CalibrationEntry> entries = CalibrationEntryBuilder.BuildEntries("HighVolatility", series, PocHorizon);

        Assert(entries.Count > 0, "Expected at least one eligible entry on the HighVolatility dataset.");

        foreach (CalibrationEntry entry in entries)
        {
            OutcomeMeasurement outcome = entry.Outcome;
            Assert(outcome.MaxAdverseExcursion >= 0.0, $"MAE must be >= 0 (magnitude convention). Bar={entry.EntryBarIndex}, MAE={outcome.MaxAdverseExcursion}.");
            Assert(outcome.MaxFavorableExcursion >= 0.0, $"MFE must be >= 0 (magnitude convention). Bar={entry.EntryBarIndex}, MFE={outcome.MaxFavorableExcursion}.");

            double runningAdverse = 0.0;
            double runningFavorable = 0.0;
            for (int i = 0; i < outcome.AdverseExcursionPath.Count; i++)
            {
                Assert(outcome.AdverseExcursionPath[i] >= runningAdverse - 1e-9, $"AdverseExcursionPath must be monotonically non-decreasing. Bar={entry.EntryBarIndex}, index={i}.");
                Assert(outcome.FavorableExcursionPath[i] >= runningFavorable - 1e-9, $"FavorableExcursionPath must be monotonically non-decreasing. Bar={entry.EntryBarIndex}, index={i}.");
                runningAdverse = outcome.AdverseExcursionPath[i];
                runningFavorable = outcome.FavorableExcursionPath[i];
            }

            if (outcome.BarsAvailable > 0)
            {
                Assert(Math.Abs(outcome.MaxAdverseExcursion - outcome.AdverseExcursionPath[^1]) < 1e-9, "MAE must equal the final element of AdverseExcursionPath.");
                Assert(Math.Abs(outcome.MaxFavorableExcursion - outcome.FavorableExcursionPath[^1]) < 1e-9, "MFE must equal the final element of FavorableExcursionPath.");
            }
        }
    }

    // ── Sanity: StopLossEvaluator boundary behavior ──────────────────────────────────────────────────

    private static void AssertStopHitInvariants_ZeroAndHugeDistance()
    {
        decimal[] series = SyntheticSeriesCatalog.Ar1(PocSeriesLength, seed: 11UL, phi: 0.5m);
        IReadOnlyList<CalibrationEntry> entries = CalibrationEntryBuilder.BuildEntries("Ar1(phi=0.5)", series, PocHorizon);
        Assert(entries.Count > 0, "Expected at least one eligible entry on the AR(1) dataset.");

        foreach (CalibrationEntry entry in entries.Take(20))
        {
            if (entry.Outcome.BarsAvailable == 0) continue;

            var (zeroStopHit, zeroStopBar, _) = StopLossEvaluator.Evaluate(entry.Outcome, stopDistance: 0.0);
            Assert(zeroStopHit && zeroStopBar == 1, $"A zero stop distance must always be hit on the very first bar. Bar={entry.EntryBarIndex}, StopHit={zeroStopHit}, StopHitBar={zeroStopBar}.");

            var (hugeStopHit, _, hugeFate) = StopLossEvaluator.Evaluate(entry.Outcome, stopDistance: 1_000_000.0);
            Assert(!hugeStopHit, $"An enormous stop distance must never be hit. Bar={entry.EntryBarIndex}, StopHit={hugeStopHit}.");
            Assert(hugeFate != EntryFate.StoppedOut, $"An enormous stop distance must never classify as StoppedOut. Bar={entry.EntryBarIndex}, Fate={hugeFate}.");
        }
    }

    // ── Sanity: relative comparison (no exact numbers asserted, only direction of the effect) ───────

    private static void AssertStrongMeanReversionRevertsMoreOftenThanRandomWalk()
    {
        decimal[] meanReverting = SyntheticSeriesCatalog.MeanRevertingOu(PocSeriesLength, seed: 42UL, kappa: 0.5m);
        decimal[] randomWalk = SyntheticSeriesCatalog.RandomWalk(PocSeriesLength, seed: 42UL);

        IReadOnlyList<CalibrationEntry> meanRevertingEntries = CalibrationEntryBuilder.BuildEntries("MeanRevertingOu(k=0.5)", meanReverting, PocHorizon);
        IReadOnlyList<CalibrationEntry> randomWalkEntries = CalibrationEntryBuilder.BuildEntries("RandomWalk", randomWalk, PocHorizon);

        Assert(meanRevertingEntries.Count > 0, "Expected eligible entries on the MeanRevertingOu dataset.");
        Assert(randomWalkEntries.Count > 0, "Expected eligible entries on the RandomWalk dataset.");

        double meanRevertingRate = meanRevertingEntries.Count(e => e.Outcome.EquilibriumBar.HasValue) / (double)meanRevertingEntries.Count;
        double randomWalkRate = randomWalkEntries.Count(e => e.Outcome.EquilibriumBar.HasValue) / (double)randomWalkEntries.Count;

        Assert(meanRevertingRate > randomWalkRate,
            $"A strongly mean-reverting series must revert to its entry-time equilibrium more often within {PocHorizon} bars than a random walk does - " +
            $"this is a sanity check on the harness, not yet a methodology conclusion. MeanReverting rate={meanRevertingRate:P1}, RandomWalk rate={randomWalkRate:P1}.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
