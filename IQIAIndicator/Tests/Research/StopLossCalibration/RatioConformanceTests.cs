using System;
using System.Collections.Generic;

namespace IQIAIndicator.Tests.Research.StopLossCalibration;

/// <summary>
/// Sprint 15.11 (Objective A / Phase 3). Targeted tests for the MeanStopRatio denominator fix:
/// CandidateAggregator must use InnovationStd for A1/D1/D2 and CurrentVolatility for A2, never the
/// wrong one, and never fabricate a ratio when the relevant sigma is missing/zero/non-finite.
/// </summary>
public static class RatioConformanceTests
{
    public static void RunAll()
    {
        TestA_A2RatioUsesCurrentVolatility();
        TestB_A1RatioUsesInnovationStd();
        TestC_D1RatioUsesInnovationStdNotCurrentVolatility();
        TestD_A2WithZeroCurrentVolatilityProducesNoInvalidRatio();
        TestE_A2WithUnavailableCurrentVolatilityInventsNoValue();
        TestF_DenominatorDependsOnCandidateKind();
    }

    // TEST A: A2, CurrentVolatility=2, k=3 -> StopDistance=6, MeanStopRatio=3
    private static void TestA_A2RatioUsesCurrentVolatility()
    {
        var entries = new List<CalibrationEntry> { BuildEntry(innovationStd: 999.0, currentVolatility: 2.0, z0: -1.0) };
        CandidateAggregateResult result = CandidateAggregator.Aggregate("A2", "Fixture", "TRAIN", 1m, CandidateKind.A2, k: 3.0, null, entries);

        Assert(Math.Abs(result.MeanStopDistance - 6.0) < 1e-9, $"A2 StopDistance must be k*CurrentVolatility=6. Actual={result.MeanStopDistance}.");
        Assert(Math.Abs(result.MeanStopRatio - 3.0) < 1e-9, $"A2 MeanStopRatio must be StopDistance/CurrentVolatility=3, not using InnovationStd(999). Actual={result.MeanStopRatio}.");
    }

    // TEST B: A1, InnovationStd=2, k=3 -> ratio=3
    private static void TestB_A1RatioUsesInnovationStd()
    {
        var entries = new List<CalibrationEntry> { BuildEntry(innovationStd: 2.0, currentVolatility: 999.0, z0: -1.0) };
        CandidateAggregateResult result = CandidateAggregator.Aggregate("A1", "Fixture", "TRAIN", 1m, CandidateKind.A1, k: 3.0, null, entries);

        Assert(Math.Abs(result.MeanStopDistance - 6.0) < 1e-9, $"A1 StopDistance must be k*InnovationStd=6. Actual={result.MeanStopDistance}.");
        Assert(Math.Abs(result.MeanStopRatio - 3.0) < 1e-9, $"A1 MeanStopRatio must be StopDistance/InnovationStd=3, not using CurrentVolatility(999). Actual={result.MeanStopRatio}.");
    }

    // TEST C: D1, InnovationStd=2 -> ratio computed with InnovationStd, not CurrentVolatility
    private static void TestC_D1RatioUsesInnovationStdNotCurrentVolatility()
    {
        var entries = new List<CalibrationEntry> { BuildEntry(innovationStd: 2.0, currentVolatility: 999.0, z0: 0.0) };
        CandidateAggregateResult result = CandidateAggregator.Aggregate("D1", "Fixture", "TRAIN", 1m, CandidateKind.D1, k: 5.0, null, entries);

        // D1 StopDistance = (k - |z0|) * InnovationStd = (5-0)*2 = 10.
        Assert(Math.Abs(result.MeanStopDistance - 10.0) < 1e-9, $"D1 StopDistance must be (k-|z0|)*InnovationStd=10. Actual={result.MeanStopDistance}.");
        Assert(Math.Abs(result.MeanStopRatio - 5.0) < 1e-9, $"D1 MeanStopRatio must be StopDistance/InnovationStd=5, not using CurrentVolatility(999). Actual={result.MeanStopRatio}.");
    }

    // TEST D: A2, CurrentVolatility=0 -> no invalid division (no Infinity/NaN injected as if real)
    private static void TestD_A2WithZeroCurrentVolatilityProducesNoInvalidRatio()
    {
        var entries = new List<CalibrationEntry> { BuildEntry(innovationStd: 2.0, currentVolatility: 0.0, z0: -1.0) };
        CandidateAggregateResult result = CandidateAggregator.Aggregate("A2", "Fixture", "TRAIN", 1m, CandidateKind.A2, k: 3.0, null, entries);

        Assert(!double.IsInfinity(result.MeanStopRatio), "MeanStopRatio must never be Infinity.");
        Assert(double.IsNaN(result.MeanStopRatio), $"With CurrentVolatility=0 the ratio is not available and must be NaN (the harness's existing not-available convention), not a fabricated 0. Actual={result.MeanStopRatio}.");
    }

    // TEST E: A2, CurrentVolatility unavailable (null) -> no invented value
    private static void TestE_A2WithUnavailableCurrentVolatilityInventsNoValue()
    {
        var entries = new List<CalibrationEntry> { BuildEntry(innovationStd: 2.0, currentVolatility: null, z0: -1.0) };
        CandidateAggregateResult result = CandidateAggregator.Aggregate("A2", "Fixture", "TRAIN", 1m, CandidateKind.A2, k: 3.0, null, entries);

        Assert(double.IsNaN(result.MeanStopRatio), $"With CurrentVolatility unavailable, MeanStopRatio must be NaN, never invented. Actual={result.MeanStopRatio}.");
        // StopDistance itself still comes from A2's own formula (k*CurrentVolatility) and is
        // independently null/not-applicable when CurrentVolatility is unavailable - verified via
        // ApplicableEntries dropping to 0, not via a fabricated distance.
        Assert(result.ApplicableEntries == 0, $"A2 with no CurrentVolatility must have zero applicable entries. Actual={result.ApplicableEntries}.");
    }

    // TEST F: denominator depends explicitly on CandidateKind, holding metrics fixed
    private static void TestF_DenominatorDependsOnCandidateKind()
    {
        var entries = new List<CalibrationEntry> { BuildEntry(innovationStd: 4.0, currentVolatility: 2.0, z0: 0.0) };

        CandidateAggregateResult a1 = CandidateAggregator.Aggregate("A1", "Fixture", "TRAIN", 1m, CandidateKind.A1, k: 1.0, null, entries);
        CandidateAggregateResult a2 = CandidateAggregator.Aggregate("A2", "Fixture", "TRAIN", 1m, CandidateKind.A2, k: 1.0, null, entries);

        // Same k, same entry, DIFFERENT sigma values (InnovationStd=4 vs CurrentVolatility=2) ->
        // StopDistance and MeanStopRatio must differ between A1 and A2, proving the denominator (and
        // numerator) genuinely tracks CandidateKind rather than being hardcoded to one field.
        Assert(Math.Abs(a1.MeanStopDistance - 4.0) < 1e-9, $"A1 StopDistance must use InnovationStd=4. Actual={a1.MeanStopDistance}.");
        Assert(Math.Abs(a2.MeanStopDistance - 2.0) < 1e-9, $"A2 StopDistance must use CurrentVolatility=2. Actual={a2.MeanStopDistance}.");
        Assert(Math.Abs(a1.MeanStopRatio - 1.0) < 1e-9, $"A1 MeanStopRatio must be 1 (4/4). Actual={a1.MeanStopRatio}.");
        Assert(Math.Abs(a2.MeanStopRatio - 1.0) < 1e-9, $"A2 MeanStopRatio must be 1 (2/2) - same ratio value from DIFFERENT sigmas, proving each candidate is self-consistent. Actual={a2.MeanStopRatio}.");
        Assert(Math.Abs(a1.MeanStopDistance - a2.MeanStopDistance) > 1e-9, "A1 and A2 StopDistance must differ here since InnovationStd != CurrentVolatility for this fixture.");
    }

    private static CalibrationEntry BuildEntry(double innovationStd, double? currentVolatility, double z0)
    {
        const double equilibrium = 100.0;
        double price = equilibrium + (z0 * innovationStd);

        var metrics = new BarMetrics(
            BarIndex: 0,
            Price: price,
            ModelsValid: true,
            EstimatedEquilibrium: equilibrium,
            InnovationStd: innovationStd,
            DynamicZScore: z0,
            CurrentVolatility: currentVolatility,
            VolatilityRegime: "MEDIUM",
            VolatilityPercentile: 0.5,
            EquilibriumDistance: Math.Abs(price - equilibrium),
            HalfLifeValid: false,
            HalfLife: null,
            HalfLifeRSquared: null);

        var outcome = new OutcomeMeasurement(
            EntryBarIndex: 0,
            Horizon: CampaignGrids.Horizon,
            BarsAvailable: 0,
            AdverseExcursionPath: Array.Empty<double>(),
            FavorableExcursionPath: Array.Empty<double>(),
            EquilibriumBar: null,
            MaxAdverseExcursion: 0.0,
            MaxFavorableExcursion: 0.0);

        SignalDirection direction = z0 <= 0 ? SignalDirection.Buy : SignalDirection.Sell;
        return new CalibrationEntry("RatioConformanceFixture", 0, direction, metrics, outcome);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
