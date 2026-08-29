using System;
using System.Collections.Generic;
using IQIAIndicator.Backtest;
using IQIAIndicator.Backtest.Measurement;
using IQIAIndicator.Core.MarketData;
using IQIAIndicator.Engine.EntryTrigger;
using IQIAIndicator.Engine.TradePlan;
using Xunit;

namespace IQIAIndicator.Tests.BacktestTests.Measurement;

/// <summary>
/// Sprint 15.25 (Lot 14.4, brief §19-§28). Pure formula-level coverage of
/// <see cref="ScientificMeasurementEngine.MeasureCore"/> against hand-computed fixtures - deliberately
/// bypasses <see cref="HistoricalSeries.Create"/> so §25's invalid-future-bar fixture can exist at all
/// (see <see cref="MeasurementStatus.InvalidFutureData"/>'s doc comment).
/// </summary>
public sealed class ScientificMeasurementEngineFormulaTests
{
    private static readonly DateTime Anchor = new(2026, 1, 5, 14, 30, 0, DateTimeKind.Utc);

    private static HistoricalBar Bar(int minutesFromAnchor, decimal high, decimal low, decimal close, decimal? open = null) =>
        new(Anchor.AddMinutes(minutesFromAnchor), open ?? close, high, low, close, Volume: 100m);

    private static MeasurementConfiguration Config(int horizon, params double[] thresholds) =>
        MeasurementConfiguration.Create(horizon, thresholds);

    // ── §19: BUY MFE/MAE fixture ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Buy_MfeAndMae_MatchHandComputedValues()
    {
        var bars = new List<HistoricalBar>
        {
            Bar(0, high: 100.5m, low: 99.5m, close: 100m),   // signal bar (index 0) - never read for MFE/MAE
            Bar(5, high: 101m, low: 99m, close: 100.5m),      // i+1
            Bar(10, high: 102m, low: 99.5m, close: 101m),     // i+2
            Bar(15, high: 100.5m, low: 100m, close: 100.2m)   // i+3
        };

        MeasurementResult result = ScientificMeasurementEngine.MeasureCore(
            bars, signalBarIndex: 0, Anchor, DirectionCandidate.BUY_CANDIDATE, entryPrice: 100m, Config(3));

        Assert.Equal(MeasurementStatus.Measured, result.Status);
        // MFE = max((High-Entry)/Entry) = max(0.01, 0.02, 0.005) = 0.02
        Assert.Equal(0.02, result.Mfe!.Value, 9);
        // MAE = max((Entry-Low)/Entry) = max(0.01, 0.005, 0.0) = 0.01
        Assert.Equal(0.01, result.Mae!.Value, 9);
    }

    // ── §20: SELL MFE/MAE fixture (mirror of §19) ───────────────────────────────────────────────────

    [Fact]
    public void Sell_MfeAndMae_MatchHandComputedValues()
    {
        var bars = new List<HistoricalBar>
        {
            Bar(0, high: 100.5m, low: 99.5m, close: 100m),   // signal bar
            Bar(5, high: 101m, low: 99m, close: 99.5m),       // i+1
            Bar(10, high: 100.5m, low: 98m, close: 98.5m),    // i+2
            Bar(15, high: 100m, low: 99.5m, close: 99.8m)     // i+3
        };

        MeasurementResult result = ScientificMeasurementEngine.MeasureCore(
            bars, signalBarIndex: 0, Anchor, DirectionCandidate.SELL_CANDIDATE, entryPrice: 100m, Config(3));

        Assert.Equal(MeasurementStatus.Measured, result.Status);
        // MFE = max((Entry-Low)/Entry) = max(0.01, 0.02, 0.005) = 0.02
        Assert.Equal(0.02, result.Mfe!.Value, 9);
        // MAE = max((High-Entry)/Entry) = max(0.01, 0.005, 0.0) = 0.01
        Assert.Equal(0.01, result.Mae!.Value, 9);
    }

    // ── §21: Return sign fixtures ────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("BUY_WINS", 110.0, 0.10)]
    [InlineData("BUY_LOSES", 90.0, -0.10)]
    [InlineData("RETURN_ZERO_BUY", 100.0, 0.0)]
    public void Buy_FinalReturn_MatchesExpectedSign(string _, double finalClose, double expectedReturn)
    {
        var bars = new List<HistoricalBar>
        {
            Bar(0, 100m, 100m, 100m),
            Bar(5, (decimal)finalClose, (decimal)finalClose, (decimal)finalClose)
        };

        MeasurementResult result = ScientificMeasurementEngine.MeasureCore(
            bars, 0, Anchor, DirectionCandidate.BUY_CANDIDATE, 100m, Config(1));

        Assert.Equal(MeasurementStatus.Measured, result.Status);
        Assert.Equal(expectedReturn, result.Return!.Value, 9);
    }

    [Theory]
    [InlineData("SELL_WINS", 90.0, 0.10)]
    [InlineData("SELL_LOSES", 110.0, -0.10)]
    [InlineData("RETURN_ZERO_SELL", 100.0, 0.0)]
    public void Sell_FinalReturn_MatchesExpectedSign(string _, double finalClose, double expectedReturn)
    {
        var bars = new List<HistoricalBar>
        {
            Bar(0, 100m, 100m, 100m),
            Bar(5, (decimal)finalClose, (decimal)finalClose, (decimal)finalClose)
        };

        MeasurementResult result = ScientificMeasurementEngine.MeasureCore(
            bars, 0, Anchor, DirectionCandidate.SELL_CANDIDATE, 100m, Config(1));

        Assert.Equal(MeasurementStatus.Measured, result.Status);
        Assert.Equal(expectedReturn, result.Return!.Value, 9);
    }

    // ── §22: Hit boundary (>=) ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void Hit_Boundary_UsesGreaterThanOrEqual()
    {
        // Threshold is derived from the SAME arithmetic MeasureCore itself performs on these exact
        // decimal inputs, rather than a hard-coded double literal like 0.001 - this is what makes the
        // "exactly at the boundary" case deterministic under floating point (brief §22's illustrative
        // 0.001 example is a decimal ROUNDED value; testing bit-exact equality against a literal would be
        // fragile to double rounding, not to the boundary RULE itself, which is what this test verifies).
        var bars = new List<HistoricalBar>
        {
            Bar(0, 100m, 100m, 100m),
            Bar(5, high: 100.25m, low: 99.9m, close: 100.1m)
        };

        const decimal entry = 100m;
        double exactMfe = (double)(100.25m - entry) / (double)entry;

        MeasurementResult atBoundary = ScientificMeasurementEngine.MeasureCore(
            bars, 0, Anchor, DirectionCandidate.BUY_CANDIDATE, entry, Config(1, exactMfe));
        Assert.True(atBoundary.HitResults[0].Hit, "MFE exactly equal to the threshold must count as a Hit (>=).");

        MeasurementResult justAbove = ScientificMeasurementEngine.MeasureCore(
            bars, 0, Anchor, DirectionCandidate.BUY_CANDIDATE, entry, Config(1, exactMfe + 0.0001));
        Assert.False(justAbove.HitResults[0].Hit, "A threshold strictly above MFE must never be a Hit.");

        MeasurementResult justBelow = ScientificMeasurementEngine.MeasureCore(
            bars, 0, Anchor, DirectionCandidate.BUY_CANDIDATE, entry, Config(1, exactMfe - 0.0001));
        Assert.True(justBelow.HitResults[0].Hit, "A threshold strictly below MFE must always be a Hit.");
    }

    [Theory]
    [InlineData(DirectionCandidateKind.Buy)]
    [InlineData(DirectionCandidateKind.Sell)]
    public void Hit_NotReached_IsFalse(DirectionCandidateKind kind)
    {
        DirectionCandidate direction = kind == DirectionCandidateKind.Buy ? DirectionCandidate.BUY_CANDIDATE : DirectionCandidate.SELL_CANDIDATE;
        var bars = new List<HistoricalBar>
        {
            Bar(0, 100m, 100m, 100m),
            Bar(5, high: 100.05m, low: 99.95m, close: 100m) // tiny move either way
        };

        MeasurementResult result = ScientificMeasurementEngine.MeasureCore(
            bars, 0, Anchor, direction, 100m, Config(1, 0.01)); // 1% threshold, far from reach

        Assert.False(result.HitResults[0].Hit);
    }

    public enum DirectionCandidateKind { Buy, Sell }

    // ── §23: Horizon boundary - bars beyond the horizon must never influence the result ─────────────

    [Fact]
    public void Horizon_StrictlyBoundsWhichBarsAreRead_AHugeMoveJustBeyondItIsInvisible()
    {
        var bars = new List<HistoricalBar> { Bar(0, 100m, 100m, 100m) };
        for (int j = 1; j <= 10; j++)
            bars.Add(Bar(j * 5, high: 100.1m, low: 99.9m, close: 100m)); // modest bars i+1..i+10
        bars.Add(Bar(55, high: 1000m, low: 1m, close: 500m)); // i+11: enormous, must be invisible at Horizon=10

        MeasurementResult horizon10 = ScientificMeasurementEngine.MeasureCore(
            bars, 0, Anchor, DirectionCandidate.BUY_CANDIDATE, 100m, Config(10, 0.001));

        Assert.Equal(MeasurementStatus.Measured, horizon10.Status);
        Assert.Equal(0.001, horizon10.Mfe!.Value, 9); // (100.1-100)/100, not influenced by bar 11's spike
        Assert.Equal(0.001, horizon10.Mae!.Value, 9);
        Assert.Equal(0.0, horizon10.Return!.Value, 9); // final close at i+10 is 100 -> flat
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(10)]
    public void Horizon_OnlyReadsExactlyHorizonBarsAfterTheSignal(int horizonBars)
    {
        var bars = new List<HistoricalBar> { Bar(0, 100m, 100m, 100m) };
        for (int j = 1; j <= 20; j++)
            bars.Add(Bar(j * 5, high: 100m + j, low: 100m - j, close: 100m)); // strictly increasing excursion per bar

        MeasurementResult result = ScientificMeasurementEngine.MeasureCore(
            bars, 0, Anchor, DirectionCandidate.BUY_CANDIDATE, 100m, Config(horizonBars, 0.001));

        // With strictly increasing excursion, MFE must equal exactly bar (0+horizonBars)'s contribution:
        // (High[horizonBars] - 100)/100 = horizonBars/100.
        Assert.Equal(horizonBars / 100.0, result.Mfe!.Value, 9);
    }

    // ── §24: end of series -> InsufficientFutureData, never an artificial complete result ───────────

    [Fact]
    public void SignalNearEndOfSeries_WithHorizonExceedingAvailableBars_IsInsufficientFutureData()
    {
        var bars = new List<HistoricalBar>
        {
            Bar(0, 100m, 100m, 100m),
            Bar(5, 100m, 100m, 100m),
            Bar(10, 100m, 100m, 100m),
            Bar(15, 100m, 100m, 100m), // signal at index 3 = N-2 (N=5)
            Bar(20, 100m, 100m, 100m)
        };

        MeasurementResult result = ScientificMeasurementEngine.MeasureCore(
            bars, signalBarIndex: 3, Anchor, DirectionCandidate.BUY_CANDIDATE, 100m, Config(10, 0.001));

        Assert.Equal(MeasurementStatus.InsufficientFutureData, result.Status);
        Assert.Null(result.Return);
        Assert.Null(result.Mfe);
        Assert.Null(result.Mae);
        Assert.Empty(result.HitResults);
        Assert.False(string.IsNullOrWhiteSpace(result.Reason));
    }

    // ── §25: invalid future OHLC -> rejected explicitly, never computed silently ────────────────────

    [Fact]
    public void InvalidFutureBar_HighBelowLow_IsRejectedExplicitly_NeverComputedSilently()
    {
        var bars = new List<HistoricalBar>
        {
            Bar(0, 100m, 100m, 100m),
            Bar(5, high: 101m, low: 99m, close: 100m),      // i+1, valid
            new HistoricalBar(Anchor.AddMinutes(10), Open: 100m, High: 90m, Low: 95m, Close: 100m, Volume: 10m) // i+2: High<Low, invalid
        };

        MeasurementResult result = ScientificMeasurementEngine.MeasureCore(
            bars, 0, Anchor, DirectionCandidate.BUY_CANDIDATE, 100m, Config(2, 0.001));

        Assert.Equal(MeasurementStatus.InvalidFutureData, result.Status);
        Assert.Null(result.Return);
        Assert.Null(result.Mfe);
        Assert.Null(result.Mae);
        Assert.False(string.IsNullOrWhiteSpace(result.Reason));
    }

    // ── §26: invalid entry price -> NOT_MEASURABLE/INVALID_ENTRY_PRICE, no division by zero ─────────

    [Theory]
    [InlineData(0.0)]
    [InlineData(-5.0)]
    public void InvalidEntryPrice_NeverDividesByZero_ReportsInvalidEntryPriceStatus(double invalidEntryPrice)
    {
        var plan = new TradePlan(
            IsValid: false, Status: TradePlanStatus.SIGNAL_ONLY, Direction: DirectionCandidate.BUY_CANDIDATE,
            EntryPrice: invalidEntryPrice == 0.0 ? 0m : (decimal)invalidEntryPrice,
            StopLoss: null, TakeProfit: null, RiskPerUnit: null, RiskAmount: null, PositionSize: null,
            RiskRewardRatio: null, InvalidationReason: null, Diagnostics: Array.Empty<string>(), Timestamp: DateTime.UtcNow);

        var signal = new BacktestSignalResult(
            BarIndex: 0, Timestamp: Anchor, Status: BacktestSignalStatus.Ready, Reason: null,
            Context: null, Regime: null, Decision: null, Methodology: null, Signal: null, Entry: null,
            EntryTrigger: null, TradePlan: plan, Exception: null);

        HistoricalSeries series = HistoricalSeries.Create("ES", "M5", "UTC", "Test", new[]
        {
            new HistoricalBar(Anchor, 100m, 101m, 99m, 100m, 10m),
            new HistoricalBar(Anchor.AddMinutes(5), 100m, 101m, 99m, 100m, 10m)
        });

        MeasurementResult result = ScientificMeasurementEngine.Measure(series, signal, Config(1, 0.001));

        Assert.Equal(MeasurementStatus.InvalidEntryPrice, result.Status);
        Assert.False(string.IsNullOrWhiteSpace(result.Reason));
    }

    // ── §27: NO_ACTION must never produce a trade measurement ───────────────────────────────────────

    [Fact]
    public void NoAction_IsNeverMeasuredAsATrade()
    {
        var plan = new TradePlan(
            IsValid: false, Status: TradePlanStatus.NO_TRADE, Direction: DirectionCandidate.NO_ACTION,
            EntryPrice: null, StopLoss: null, TakeProfit: null, RiskPerUnit: null, RiskAmount: null,
            PositionSize: null, RiskRewardRatio: null, InvalidationReason: "no candidate",
            Diagnostics: Array.Empty<string>(), Timestamp: DateTime.UtcNow);

        var signal = new BacktestSignalResult(
            0, Anchor, BacktestSignalStatus.Ready, null, null, null, null, null, null, null, null, plan, null);

        HistoricalSeries series = HistoricalSeries.Create("ES", "M5", "UTC", "Test", new[]
        {
            new HistoricalBar(Anchor, 100m, 101m, 99m, 100m, 10m),
            new HistoricalBar(Anchor.AddMinutes(5), 100m, 101m, 99m, 100m, 10m)
        });

        MeasurementResult result = ScientificMeasurementEngine.Measure(series, signal, Config(1, 0.001));

        Assert.Equal(MeasurementStatus.NotMeasurable, result.Status);
        Assert.Null(result.Return);
    }

    // ── §28: SIGNAL_ONLY must still be measured from EntryPrice - no fabricated SL needed ───────────

    [Fact]
    public void SignalOnly_WithAnEntryPrice_IsStillMeasured_NeverFabricatesAStopLoss()
    {
        var plan = new TradePlan(
            IsValid: false, Status: TradePlanStatus.SIGNAL_ONLY, Direction: DirectionCandidate.BUY_CANDIDATE,
            EntryPrice: 100m, StopLoss: null, TakeProfit: null, RiskPerUnit: null, RiskAmount: null,
            PositionSize: null, RiskRewardRatio: null, InvalidationReason: "Stop loss unavailable",
            Diagnostics: Array.Empty<string>(), Timestamp: DateTime.UtcNow);

        var signal = new BacktestSignalResult(
            0, Anchor, BacktestSignalStatus.Ready, null, null, null, null, null, null, null, null, plan, null);

        HistoricalSeries series = HistoricalSeries.Create("ES", "M5", "UTC", "Test", new[]
        {
            new HistoricalBar(Anchor, 100m, 101m, 99m, 100m, 10m),
            new HistoricalBar(Anchor.AddMinutes(5), 105m, 106m, 104m, 105m, 10m)
        });

        MeasurementResult result = ScientificMeasurementEngine.Measure(series, signal, Config(1, 0.001));

        Assert.Equal(MeasurementStatus.Measured, result.Status);
        Assert.Equal(0.05, result.Return!.Value, 9); // (105-100)/100
    }

    // ── Argument validation ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void MeasurementConfiguration_RejectsNonPositiveHorizon()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MeasurementConfiguration.Create(0, new[] { 0.001 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => MeasurementConfiguration.Create(-1, new[] { 0.001 }));
    }

    [Fact]
    public void MeasurementConfiguration_RejectsNonPositiveThresholds()
    {
        Assert.Throws<ArgumentException>(() => MeasurementConfiguration.Create(1, new[] { 0.0 }));
        Assert.Throws<ArgumentException>(() => MeasurementConfiguration.Create(1, new[] { -0.001 }));
    }

    [Fact]
    public void MeasureCore_RejectsWatchOrNoActionDirection()
    {
        var bars = new List<HistoricalBar> { Bar(0, 100m, 100m, 100m), Bar(5, 101m, 99m, 100m) };
        Assert.Throws<ArgumentException>(() =>
            ScientificMeasurementEngine.MeasureCore(bars, 0, Anchor, DirectionCandidate.WATCH, 100m, Config(1)));
        Assert.Throws<ArgumentException>(() =>
            ScientificMeasurementEngine.MeasureCore(bars, 0, Anchor, DirectionCandidate.NO_ACTION, 100m, Config(1)));
    }
}
