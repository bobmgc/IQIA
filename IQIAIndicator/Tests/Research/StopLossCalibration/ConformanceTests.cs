using System;
using System.Collections.Generic;
using System.Linq;

namespace IQIAIndicator.Tests.Research.StopLossCalibration;

/// <summary>
/// Sprint 15.10 (Conformance Gate §12 - structural tests required before any campaign run). Verifies
/// the harness matches QDE-012's locked grids/datasets/splits/scales/horizon, that source series are
/// never mutated, and that Candidate D's formula (§14 amendment) is implemented exactly as locked -
/// including its degenerate condition, R² gate, and BUY/SELL symmetry. Does not run the campaign.
/// </summary>
public static class ConformanceTests
{
    public static void RunAll()
    {
        Test1_GridConformance();
        Test2_DatasetConformance();
        Test3_SplitConformance();
        Test4_ScaleConformance();
        Test5_HorizonConformance();
        Test6_SourceImmutability();
        Test7_CandidateDFormula();
        Test8_D2RSquaredGate();
        Test9_BuySellSymmetry();
    }

    // TEST 1 — Grid conformance
    private static void Test1_GridConformance()
    {
        Assert(CampaignGrids.KGrid.Count == 40, $"KGrid must have exactly 40 points. Actual={CampaignGrids.KGrid.Count}.");
        Assert(Math.Abs(CampaignGrids.KGrid[0] - 0.25) < 1e-9, $"KGrid must start at 0.25. Actual={CampaignGrids.KGrid[0]}.");
        Assert(Math.Abs(CampaignGrids.KGrid[^1] - 10.0) < 1e-9, $"KGrid must end at 10.00. Actual={CampaignGrids.KGrid[^1]}.");
        for (int i = 1; i < CampaignGrids.KGrid.Count; i++)
        {
            Assert(Math.Abs((CampaignGrids.KGrid[i] - CampaignGrids.KGrid[i - 1]) - 0.25) < 1e-9,
                $"KGrid step must be exactly 0.25 everywhere. Broke between index {i - 1} and {i}.");
        }

        Assert(CampaignGrids.RSquaredGrid.Count == 19, $"RSquaredGrid must have exactly 19 points. Actual={CampaignGrids.RSquaredGrid.Count}.");
        Assert(Math.Abs(CampaignGrids.RSquaredGrid[0] - 0.00) < 1e-9, $"RSquaredGrid must start at 0.00. Actual={CampaignGrids.RSquaredGrid[0]}.");
        Assert(Math.Abs(CampaignGrids.RSquaredGrid[^1] - 0.90) < 1e-9, $"RSquaredGrid must end at 0.90. Actual={CampaignGrids.RSquaredGrid[^1]}.");
        for (int i = 1; i < CampaignGrids.RSquaredGrid.Count; i++)
        {
            Assert(Math.Abs((CampaignGrids.RSquaredGrid[i] - CampaignGrids.RSquaredGrid[i - 1]) - 0.05) < 1e-9,
                $"RSquaredGrid step must be exactly 0.05 everywhere. Broke between index {i - 1} and {i}.");
        }
    }

    // TEST 2 — Dataset conformance
    private static void Test2_DatasetConformance()
    {
        string[] expected =
        {
            "WhiteNoise", "RandomWalk", "AR1_phi0.5", "AR1_phi0.95",
            "MeanRevertingOu_k0.5", "MeanRevertingOu_k0.05", "Trending",
            "LowVolatility", "HighVolatility", "StructuralBreak", "VarianceBreak"
        };

        Assert(CampaignDatasetCatalog.Generators.Count == 11, $"Must have exactly 11 datasets. Actual={CampaignDatasetCatalog.Generators.Count}.");
        foreach (string name in expected)
        {
            Assert(CampaignDatasetCatalog.Generators.ContainsKey(name), $"Missing required dataset '{name}'.");
        }

        foreach (string key in CampaignDatasetCatalog.Generators.Keys)
        {
            Assert(expected.Contains(key), $"Unexpected dataset '{key}' not in the QDE-012 §8 catalog.");
        }
    }

    // TEST 3 — Split conformance
    private static void Test3_SplitConformance()
    {
        Assert(CampaignDatasetCatalog.SeedFor("TRAIN") == 42UL, "TRAIN seed must be 42.");
        Assert(CampaignDatasetCatalog.SeedFor("VALIDATION") == 43UL, "VALIDATION seed must be 43.");
        Assert(CampaignDatasetCatalog.SeedFor("TEST") == 44UL, "TEST seed must be 44.");
        Assert(CampaignDatasetCatalog.Splits.Count == 3 && CampaignDatasetCatalog.Splits.SequenceEqual(new[] { "TRAIN", "VALIDATION", "TEST" }),
            "Splits must be exactly [TRAIN, VALIDATION, TEST] in that order.");
    }

    // TEST 4 — Scale conformance
    private static void Test4_ScaleConformance()
    {
        decimal[] expected = { 0.01m, 0.1m, 1m, 10m, 100m, 1000m };
        Assert(CampaignDatasetCatalog.Scales.Count == 6, $"Must have exactly 6 scales. Actual={CampaignDatasetCatalog.Scales.Count}.");
        for (int i = 0; i < expected.Length; i++)
        {
            Assert(CampaignDatasetCatalog.Scales[i] == expected[i], $"Scale at index {i} must be {expected[i]}. Actual={CampaignDatasetCatalog.Scales[i]}.");
        }
    }

    // TEST 5 — Horizon conformance
    private static void Test5_HorizonConformance()
    {
        Assert(CampaignGrids.Horizon == 40, $"Horizon must be exactly 40. Actual={CampaignGrids.Horizon}.");
        Assert(CampaignDatasetCatalog.SeriesLength == 600, $"Series length must be exactly 600. Actual={CampaignDatasetCatalog.SeriesLength}.");
    }

    // TEST 6 — Source immutability
    private static void Test6_SourceImmutability()
    {
        decimal[] before = CampaignDatasetCatalog.Generators["WhiteNoise"](CampaignDatasetCatalog.TrainSeed);
        decimal[] beforeSnapshot = (decimal[])before.Clone();

        decimal[] scaled = CampaignDatasetCatalog.Build("WhiteNoise", "TRAIN", 1000m);
        Assert(scaled.Length == before.Length, "Scaled series must have the same length as the base series.");
        Assert(!ReferenceEquals(scaled, before), "Build must never return the same array instance it scales from - scaling must allocate a new array.");

        decimal[] after = CampaignDatasetCatalog.Generators["WhiteNoise"](CampaignDatasetCatalog.TrainSeed);
        Assert(after.SequenceEqual(beforeSnapshot),
            "Regenerating the same dataset/seed after a scaled Build() call must produce identical values - the generator must not have been mutated or perturbed by scaling.");

        for (int i = 0; i < before.Length; i++)
        {
            Assert(scaled[i] == before[i] * 1000m, $"Scaled value at index {i} must equal base*scale exactly.");
        }
    }

    // TEST 7 — Candidate D formula (QDE-012 §14)
    private static void Test7_CandidateDFormula()
    {
        const double std = 2.0;
        const double equilibrium = 100.0;

        // z0 = 0: entry exactly at equilibrium - StopDistance = k * std for every k > 0.
        AssertDFormula(z0: 0.0, std: std, equilibrium: equilibrium, k: 3.0, expectedDistance: 3.0 * std);

        // z0 = 1 (SELL side: price above equilibrium), k > |z0|
        AssertDFormula(z0: 1.0, std: std, equilibrium: equilibrium, k: 4.0, expectedDistance: (4.0 - 1.0) * std);

        // z0 = -1 (BUY side: price below equilibrium), k > |z0|
        AssertDFormula(z0: -1.0, std: std, equilibrium: equilibrium, k: 4.0, expectedDistance: (4.0 - 1.0) * std);

        // k == |z0| -> degenerate (NOT APPLICABLE)
        CalibrationEntry entryAtBoundary = BuildEntry(direction: SignalDirection.Buy, price: equilibrium - 1.0 * std, equilibrium, std, z0: -1.0);
        Assert(CandidateStopDistance.Compute(CandidateKind.D1, entryAtBoundary, k: 1.0) is null,
            "k == |z0| must be degenerate (NOT APPLICABLE), per QDE-012 §14.8.");

        // k < |z0| -> degenerate (NOT APPLICABLE)
        Assert(CandidateStopDistance.Compute(CandidateKind.D1, entryAtBoundary, k: 0.5) is null,
            "k < |z0| must be degenerate (NOT APPLICABLE), per QDE-012 §14.8.");
    }

    private static void AssertDFormula(double z0, double std, double equilibrium, double k, double expectedDistance)
    {
        double price = equilibrium + (z0 * std);
        SignalDirection direction = z0 < 0 ? SignalDirection.Buy : SignalDirection.Sell;
        CalibrationEntry entry = BuildEntry(direction, price, equilibrium, std, z0);

        double? actual = CandidateStopDistance.Compute(CandidateKind.D1, entry, k);
        Assert(actual is double d && Math.Abs(d - expectedDistance) < 1e-9,
            $"D1 StopDistance mismatch for z0={z0}, k={k}. Expected={expectedDistance}, Actual={actual}.");
    }

    // TEST 8 — D2 R-squared gate
    private static void Test8_D2RSquaredGate()
    {
        const double std = 2.0;
        const double equilibrium = 100.0;
        const double z0 = -1.0;
        const double k = 5.0;

        CalibrationEntry validHalfLife = BuildEntry(SignalDirection.Buy, equilibrium + (z0 * std), equilibrium, std, z0, halfLifeValid: true, halfLifeRSquared: 0.5);
        CalibrationEntry invalidHalfLife = BuildEntry(SignalDirection.Buy, equilibrium + (z0 * std), equilibrium, std, z0, halfLifeValid: false, halfLifeRSquared: null);

        Assert(CandidateStopDistance.Compute(CandidateKind.D2, validHalfLife, k, rSquaredThreshold: 0.60) is null,
            "R2 < threshold must be NOT APPLICABLE for D2.");
        Assert(CandidateStopDistance.Compute(CandidateKind.D2, validHalfLife, k, rSquaredThreshold: 0.50) is not null,
            "R2 >= threshold with HalfLifeValid=true must be applicable for D2.");
        Assert(CandidateStopDistance.Compute(CandidateKind.D2, invalidHalfLife, k, rSquaredThreshold: 0.00) is null,
            "HalfLifeValid=false must be NOT APPLICABLE for D2 regardless of threshold.");
    }

    // TEST 9 — BUY/SELL symmetry (QDE-012 §14.4/14.5)
    private static void Test9_BuySellSymmetry()
    {
        const double std = 2.0;
        const double equilibrium = 100.0;
        const double k = 3.0;

        // BUY: z0 = -1, entry below equilibrium. SL = equilibrium - k*std.
        double buyZ0 = -1.0;
        double buyEntryPrice = equilibrium + (buyZ0 * std);
        CalibrationEntry buyEntry = BuildEntry(SignalDirection.Buy, buyEntryPrice, equilibrium, std, buyZ0);
        double buyDistance = CandidateStopDistance.Compute(CandidateKind.D1, buyEntry, k)!.Value;
        double buySlPrice = buyEntryPrice - buyDistance;
        double buyExpectedSl = equilibrium - (k * std);
        Assert(Math.Abs(buySlPrice - buyExpectedSl) < 1e-9,
            $"BUY: entryPrice - StopDistance must equal EstimatedEquilibrium - k*InnovationStd. Actual SL={buySlPrice}, Expected={buyExpectedSl}.");

        // SELL: z0 = +1, entry above equilibrium. SL = equilibrium + k*std.
        double sellZ0 = 1.0;
        double sellEntryPrice = equilibrium + (sellZ0 * std);
        CalibrationEntry sellEntry = BuildEntry(SignalDirection.Sell, sellEntryPrice, equilibrium, std, sellZ0);
        double sellDistance = CandidateStopDistance.Compute(CandidateKind.D1, sellEntry, k)!.Value;
        double sellSlPrice = sellEntryPrice + sellDistance;
        double sellExpectedSl = equilibrium + (k * std);
        Assert(Math.Abs(sellSlPrice - sellExpectedSl) < 1e-9,
            $"SELL: entryPrice + StopDistance must equal EstimatedEquilibrium + k*InnovationStd. Actual SL={sellSlPrice}, Expected={sellExpectedSl}.");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────

    private static CalibrationEntry BuildEntry(
        SignalDirection direction,
        double price,
        double equilibrium,
        double innovationStd,
        double z0,
        bool halfLifeValid = false,
        double? halfLifeRSquared = null)
    {
        var metrics = new BarMetrics(
            BarIndex: 0,
            Price: price,
            ModelsValid: true,
            EstimatedEquilibrium: equilibrium,
            InnovationStd: innovationStd,
            DynamicZScore: z0,
            CurrentVolatility: innovationStd,
            VolatilityRegime: "MEDIUM",
            VolatilityPercentile: 0.5,
            EquilibriumDistance: Math.Abs(price - equilibrium),
            HalfLifeValid: halfLifeValid,
            HalfLife: halfLifeValid ? 5.0 : null,
            HalfLifeRSquared: halfLifeRSquared);

        var outcome = new OutcomeMeasurement(
            EntryBarIndex: 0,
            Horizon: CampaignGrids.Horizon,
            BarsAvailable: 0,
            AdverseExcursionPath: Array.Empty<double>(),
            FavorableExcursionPath: Array.Empty<double>(),
            EquilibriumBar: null,
            MaxAdverseExcursion: 0.0,
            MaxFavorableExcursion: 0.0);

        return new CalibrationEntry("ConformanceTestFixture", 0, direction, metrics, outcome);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
