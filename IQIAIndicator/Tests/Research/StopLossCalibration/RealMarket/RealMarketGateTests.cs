using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace IQIAIndicator.Tests.Research.StopLossCalibration.RealMarket;

/// <summary>
/// Sprint 15.16 (QDE-012 Real Market Validation - Phase A gate). This sprint found no real market
/// OHLC data anywhere in the repository (see real_market_data_inventory.csv) and therefore never ran a
/// real-data campaign - verdict INSUFFICIENT REAL DATA. These tests cover exactly what CAN be verified
/// without any real data: that the invariants a future real-data campaign would depend on are locked
/// and unbroken (horizon, k-grid, the A1 formula, BUY/SELL symmetry, scale-ratio consistency), that the
/// harness genuinely does not require the synthetic generators to run, and that this sprint's own
/// BLOCKED verdict is honestly reflected on disk (no fabricated real-campaign result files, no
/// SYNTHETIC/REAL blending). They do not, and cannot, validate anything about real market behavior.
/// </summary>
public static class RealMarketGateTests
{
    public static void RunAll()
    {
        Test1_HorizonStillLockedAt40();
        Test2_KGridStillLocked_40PointsQuarterStep();
        Test3_A1Formula_ScaleRatioConsistency();
        Test4_A1_BuySellSymmetry();
        Test5_HarnessAcceptsArbitraryNonCatalogSeries_EndToEnd();
        Test6_RealMarketGateArtifactsAreHonest();
    }

    // ── §9: horizon must not be recalibrated after observing results ────────────────────────────────

    private static void Test1_HorizonStillLockedAt40()
    {
        Assert(CampaignGrids.Horizon == 40, $"QDE-012 Sprint 15.16 §9 requires Horizon=40 (locked, not recalibrated). Actual={CampaignGrids.Horizon}.");
    }

    // ── §10: k-grid must be 0.25..10.0 step 0.25 (40 points), unreduced before results are seen ─────

    private static void Test2_KGridStillLocked_40PointsQuarterStep()
    {
        IReadOnlyList<double> grid = CampaignGrids.KGrid;
        Assert(grid.Count == 40, $"QDE-012 Sprint 15.16 §10 requires exactly 40 k-grid points. Actual={grid.Count}.");
        Assert(Math.Abs(grid[0] - 0.25) < 1e-9, $"k-grid must start at 0.25. Actual={grid[0]}.");
        Assert(Math.Abs(grid[^1] - 10.0) < 1e-9, $"k-grid must end at 10.00. Actual={grid[^1]}.");
        for (int i = 1; i < grid.Count; i++)
        {
            Assert(Math.Abs((grid[i] - grid[i - 1]) - 0.25) < 1e-9, $"k-grid step must be exactly 0.25 everywhere. Broke between index {i - 1} and {i}.");
        }
    }

    // ── §18: StopDistance / InnovationStd ≈ k, independent of trading performance ───────────────────

    private static void Test3_A1Formula_ScaleRatioConsistency()
    {
        foreach (double innovationStd in new[] { 0.5, 1.0, 2.0, 7.3 })
        {
            CalibrationEntry entry = BuildFixtureEntry(SignalDirection.Buy, price: 100.0 - innovationStd, equilibrium: 100.0, innovationStd, z0: -1.0);
            foreach (double k in new[] { 0.25, 1.0, 3.0, 10.0 })
            {
                double? distance = CandidateStopDistance.Compute(CandidateKind.A1, entry, k);
                Assert(distance is double d && Math.Abs(d - (k * innovationStd)) < 1e-9,
                    $"A1 StopDistance must equal k*InnovationStd exactly. k={k}, InnovationStd={innovationStd}, Actual={distance}.");
                Assert(distance is double d2 && Math.Abs((d2 / innovationStd) - k) < 1e-9,
                    $"StopDistance/InnovationStd must equal k within numerical tolerance (QDE-012 §18 scale invariance). k={k}, InnovationStd={innovationStd}.");
            }
        }
    }

    // ── BUY/SELL symmetry for A1 specifically (ConformanceTests Test9 covers D1; A1 has no |z0| term) ─

    private static void Test4_A1_BuySellSymmetry()
    {
        const double innovationStd = 2.0;
        const double equilibrium = 100.0;
        const double k = 3.0;

        double buyEntryPrice = equilibrium - innovationStd;
        CalibrationEntry buyEntry = BuildFixtureEntry(SignalDirection.Buy, buyEntryPrice, equilibrium, innovationStd, z0: -1.0);
        double buyDistance = CandidateStopDistance.Compute(CandidateKind.A1, buyEntry, k)!.Value;
        double buySl = buyEntryPrice - buyDistance;
        Assert(Math.Abs(buySl - (buyEntryPrice - (k * innovationStd))) < 1e-9,
            $"BUY: SL must equal EntryPrice - k*InnovationStd. Actual SL={buySl}.");

        double sellEntryPrice = equilibrium + innovationStd;
        CalibrationEntry sellEntry = BuildFixtureEntry(SignalDirection.Sell, sellEntryPrice, equilibrium, innovationStd, z0: 1.0);
        double sellDistance = CandidateStopDistance.Compute(CandidateKind.A1, sellEntry, k)!.Value;
        double sellSl = sellEntryPrice + sellDistance;
        Assert(Math.Abs(sellSl - (sellEntryPrice + (k * innovationStd))) < 1e-9,
            $"SELL: SL must equal EntryPrice + k*InnovationStd. Actual SL={sellSl}.");

        Assert(Math.Abs(buyDistance - sellDistance) < 1e-9,
            $"A1's StopDistance must be identical in magnitude for BUY and SELL given the same InnovationStd/k - only the SL side differs. Buy={buyDistance}, Sell={sellDistance}.");
    }

    // ── Harness must not secretly require SyntheticSeriesCatalog to function ────────────────────────
    // The array below is a hand-authored fixture for THIS structural test only - it is not a stand-in
    // for real market data and must never be reported as SYNTHETIC or REAL_MARKET in any campaign
    // output. Its only purpose is to prove CalibrationEntryBuilder/BarMetricsComputer/OutcomeSimulator/
    // StopLossEvaluator work end-to-end on an arbitrary decimal[] that did not come from
    // SyntheticSeriesCatalog or CampaignDatasetCatalog - exactly the property a real-data loader would
    // need to rely on once real data exists.

    private static void Test5_HarnessAcceptsArbitraryNonCatalogSeries_EndToEnd()
    {
        decimal[] handAuthoredFixture = BuildHandAuthoredFixtureSeries(length: 80);

        IReadOnlyList<CalibrationEntry> entries = CalibrationEntryBuilder.BuildEntries("HandAuthoredFixture", handAuthoredFixture, CampaignGrids.Horizon);
        Assert(entries.Count > 0, "Harness must produce at least one eligible entry on a hand-authored, non-catalog series.");

        foreach (CalibrationEntry entry in entries.Take(10))
        {
            double? distance = CandidateStopDistance.Compute(CandidateKind.A1, entry, k: 3.0);
            if (distance is null)
            {
                continue; // degenerate entry (InnovationStd missing/zero) is a valid outcome, not a harness failure
            }

            var (stopHit, stopHitBar, fate) = StopLossEvaluator.Evaluate(entry.Outcome, distance.Value);
            Assert(stopHitBar is null or > 0, $"StopHitBar, when set, must be a positive bar offset. Entry={entry.EntryBarIndex}, StopHitBar={stopHitBar}.");
            Assert(Enum.IsDefined(typeof(EntryFate), fate), $"Evaluate must return a defined EntryFate. Entry={entry.EntryBarIndex}, Fate={fate}.");
            _ = stopHit;
        }
    }

    private static decimal[] BuildHandAuthoredFixtureSeries(int length)
    {
        var series = new decimal[length];
        for (int i = 0; i < length; i++)
        {
            // Deterministic, non-generator oscillation around 100 - just enough structure to exercise
            // the Kalman/OU/DynamicZScore/Volatility pipeline without depending on any RNG or catalog.
            int phase = i % 7;
            decimal offset = phase switch
            {
                0 => 0.0m,
                1 => 1.5m,
                2 => 2.5m,
                3 => 1.0m,
                4 => -1.0m,
                5 => -2.5m,
                _ => -1.5m
            };
            series[i] = 100.0m + offset + (0.01m * i);
        }

        return series;
    }

    // ── §16/§20: this sprint's BLOCKED verdict must be honestly reflected on disk, and SYNTHETIC vs
    //    REAL_MARKET results must never be combined or fabricated ──────────────────────────────────

    private static void Test6_RealMarketGateArtifactsAreHonest()
    {
        string dir = RealMarketOutputPaths.ResolveOutputDirectory();

        string[] requiredAuditFiles =
        {
            "real_market_data_inventory.csv",
            "real_market_data_quality.csv",
            "A1_real_calibration_summary.txt"
        };
        foreach (string file in requiredAuditFiles)
        {
            Assert(File.Exists(Path.Combine(dir, file)), $"Required Phase A audit artifact is missing: {file}.");
        }

        string summary = File.ReadAllText(Path.Combine(dir, "A1_real_calibration_summary.txt"));
        Assert(summary.Contains("INSUFFICIENT REAL DATA", StringComparison.Ordinal),
            "A1_real_calibration_summary.txt must record the sprint's actual verdict (INSUFFICIENT REAL DATA) - this guards against a future edit silently overwriting the verdict without re-running the campaign gate.");

        // These are the campaign-result files Sprint 15.16 would have produced IF a real-data campaign
        // had actually run. None of them may exist while the verdict above is INSUFFICIENT REAL DATA -
        // their presence would mean real-looking results were fabricated from no real data.
        string[] forbiddenWhileBlocked =
        {
            "A1_real_k_grid_results.csv",
            "A1_real_train_validation.csv",
            "A1_real_test_confirmation.csv",
            "A1_real_stable_regions.csv",
            "A1_real_dataset_analysis.csv",
            "A1_real_scale_analysis.csv",
            "A1_real_regime_analysis.csv",
            "A1_synthetic_vs_real_comparison.csv",
            "A1_real_sensitivity.csv"
        };
        foreach (string file in forbiddenWhileBlocked)
        {
            Assert(!File.Exists(Path.Combine(dir, file)),
                $"'{file}' must not exist while this sprint's verdict is INSUFFICIENT REAL DATA - its presence would mean a real-market campaign result was fabricated with no real data behind it.");
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────

    private static CalibrationEntry BuildFixtureEntry(SignalDirection direction, double price, double equilibrium, double innovationStd, double z0)
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

        return new CalibrationEntry("RealMarketGateTestFixture", 0, direction, metrics, outcome);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
