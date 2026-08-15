using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IQIAIndicator.Tests.Calibration;
using Xunit;

namespace IQIAIndicator.Tests.Research.StopLossCalibration.RealMarket;

/// <summary>
/// Sprint 15.18 (QDE-012 real-market data quality). Unit tests for RealMarketQualityAnalyzer /
/// RealMarketOhlcvCsvReader using small, HAND-AUTHORED synthetic fixtures - clearly labelled as such
/// throughout this file. These fixtures exist only to exercise the validator's own logic in isolation
/// (e.g. "does the gap detector correctly flag a 3-interval hole") and must never be reported anywhere
/// as a real-market campaign or real ATAS capture - see Sprint1518RealCaptureQualityTests.cs for the
/// tests that run against the actual supplied real dataset.
/// </summary>
public sealed class RealMarketQualityAnalyzerTests
{
    private static readonly Guid FixtureSessionId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTime FixtureStart = new(2026, 1, 5, 22, 0, 0, DateTimeKind.Unspecified); // synthetic fixture only

    // ── TEST 1: OHLC invariant validation ────────────────────────────────────────────────────────────

    [Fact]
    public void ValidateOhlc_DetectsAllThreeStructuralViolationKinds()
    {
        var bars = new[]
        {
            Bar(0, open: 100m, high: 99m, low: 98m, close: 100m, volume: 10m),   // High < max(O,C)
            Bar(1, open: 100m, high: 105m, low: 101m, close: 102m, volume: 10m), // Low > min(O,C)
            Bar(2, open: 100m, high: 95m, low: 96m, close: 100m, volume: 10m),   // High < Low
            Bar(3, open: 100m, high: 105m, low: 95m, close: 102m, volume: 10m),  // valid
        };

        OhlcValidationFacts facts = RealMarketQualityAnalyzer.ValidateOhlc(bars);

        Assert.Equal(4, facts.TotalBars);
        Assert.Equal(3, facts.StructuralViolations);
        Assert.Equal(0, facts.NaNOrInfiniteCount);
    }

    [Fact]
    public void ValidateOhlc_FlagsNonPositivePriceAndNegativeVolumeSeparatelyFromStructural()
    {
        var bars = new[]
        {
            Bar(0, open: 0m, high: 0m, low: 0m, close: 0m, volume: 10m),     // non-positive price, structurally "valid" (all equal)
            Bar(1, open: 100m, high: 101m, low: 99m, close: 100m, volume: -5m), // negative volume
        };

        OhlcValidationFacts facts = RealMarketQualityAnalyzer.ValidateOhlc(bars);

        Assert.Equal(0, facts.StructuralViolations);
        Assert.Equal(1, facts.NonPositivePriceCount);
        Assert.Equal(1, facts.NegativeVolumeCount);
    }

    // ── TEST 2: timestamp ordering ────────────────────────────────────────────────────────────────────

    [Fact]
    public void AnalyzeTimestamps_StrictlyIncreasingSeries_ReportsStrictlyIncreasingTrue()
    {
        var bars = RegularSeries(count: 5, intervalMinutes: 5);
        TimestampFacts facts = RealMarketQualityAnalyzer.AnalyzeTimestamps(bars);

        Assert.True(facts.StrictlyIncreasing);
        Assert.True(facts.NonDecreasing);
        Assert.Equal(0, facts.DuplicateTimestampCount);
    }

    [Fact]
    public void AnalyzeTimestamps_OutOfOrderRow_ReportsStrictlyIncreasingFalse()
    {
        var bars = new List<RealMarketBar>(RegularSeries(count: 3, intervalMinutes: 5));
        bars[2] = bars[2] with { Timestamp = bars[0].Timestamp }; // rewinds instead of advancing

        TimestampFacts facts = RealMarketQualityAnalyzer.AnalyzeTimestamps(bars);

        Assert.False(facts.StrictlyIncreasing);
        Assert.Equal(1, facts.DuplicateTimestampCount);
    }

    // ── TEST 3: duplicate / forming-bar-capture classification (zero-range segment detection) ─────────

    [Fact]
    public void DetectZeroRangeSegments_ContiguousSingleTickBars_FormsOneSegment()
    {
        var bars = new List<RealMarketBar>(RegularSeries(count: 6, intervalMinutes: 5));
        // Overwrite bars 2..4 (inclusive) to be zero-range, single-print snapshots - synthetic stand-in
        // for the exact pattern found in the real capture's tail (Sprint 15.18 report, Section 6).
        for (int i = 2; i <= 4; i++)
            bars[i] = bars[i] with { Open = 7500m, High = 7500m, Low = 7500m, Close = 7500m, Volume = 1m };

        IReadOnlyList<FormingBarSegment> segments = RealMarketQualityAnalyzer.DetectZeroRangeSegments(bars);

        FormingBarSegment segment = Assert.Single(segments);
        Assert.Equal(2, segment.StartIndex);
        Assert.Equal(4, segment.EndIndex);
        Assert.Equal(3, segment.BarCount);
        Assert.Equal(1m, segment.MedianVolumeInSegment);
    }

    [Fact]
    public void DetectZeroRangeSegments_NoZeroRangeBars_ReturnsEmpty()
    {
        var bars = RegularSeries(count: 5, intervalMinutes: 5);
        Assert.Empty(RealMarketQualityAnalyzer.DetectZeroRangeSegments(bars));
    }

    // ── TEST 4: 5-minute (and other) interval detection from the TimeFrame column ───────────────────────

    [Theory]
    [InlineData("M5", 5 * 60)]
    [InlineData("M1", 60)]
    [InlineData("H1", 3600)]
    [InlineData("D1", 86400)]
    public void TryParseExpectedInterval_RecognizedTimeFrames_ReturnsExpectedSpan(string timeFrame, int expectedSeconds)
    {
        TimeSpan? interval = RealMarketQualityAnalyzer.TryParseExpectedInterval(timeFrame);
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), interval);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Tick")]
    [InlineData("X5")]
    public void TryParseExpectedInterval_UnrecognizedTimeFrame_ReturnsNullRatherThanGuess(string timeFrame)
    {
        Assert.Null(RealMarketQualityAnalyzer.TryParseExpectedInterval(timeFrame));
    }

    // ── TEST 5: gap detection and classification ────────────────────────────────────────────────────

    [Fact]
    public void ClassifyGaps_RegularSeries_FindsNoGaps()
    {
        var bars = RegularSeries(count: 10, intervalMinutes: 5);
        Assert.Empty(RealMarketQualityAnalyzer.ClassifyGaps(bars, TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void ClassifyGaps_WeekendSizedGap_ClassifiedAsLargeGap()
    {
        var bars = new List<RealMarketBar>(RegularSeries(count: 3, intervalMinutes: 5));
        bars.Add(bars[^1] with { Timestamp = bars[^1].Timestamp.AddHours(50), CurrentBar = bars[^1].CurrentBar + 1 }); // ~50h hole

        IReadOnlyList<GapRecord> gaps = RealMarketQualityAnalyzer.ClassifyGaps(bars, TimeSpan.FromMinutes(5));

        GapRecord gap = Assert.Single(gaps);
        Assert.Equal("LARGE_GAP_PLAUSIBLE_MULTI_DAY_CLOSURE", gap.Classification);
    }

    [Fact]
    public void ClassifyGaps_SixtyFiveMinuteSizedGap_ClassifiedAsMediumGap()
    {
        var bars = new List<RealMarketBar>(RegularSeries(count: 3, intervalMinutes: 5));
        bars.Add(bars[^1] with { Timestamp = bars[^1].Timestamp.AddMinutes(65), CurrentBar = bars[^1].CurrentBar + 1 });

        IReadOnlyList<GapRecord> gaps = RealMarketQualityAnalyzer.ClassifyGaps(bars, TimeSpan.FromMinutes(5));

        GapRecord gap = Assert.Single(gaps);
        Assert.Equal("MEDIUM_GAP_PLAUSIBLE_INTRADAY_CLOSURE", gap.Classification);
    }

    [Fact]
    public void ClassifyGaps_NonMultipleInterval_ClassifiedIrregularUnresolved_NotGuessed()
    {
        var bars = new List<RealMarketBar>(RegularSeries(count: 3, intervalMinutes: 5));
        bars.Add(bars[^1] with { Timestamp = bars[^1].Timestamp.AddMinutes(7), CurrentBar = bars[^1].CurrentBar + 1 }); // not a multiple of 5

        IReadOnlyList<GapRecord> gaps = RealMarketQualityAnalyzer.ClassifyGaps(bars, TimeSpan.FromMinutes(5));

        GapRecord gap = Assert.Single(gaps);
        Assert.Equal("IRREGULAR_INTERVAL_UNRESOLVED", gap.Classification);
    }

    // ── TEST 6: volume validation ────────────────────────────────────────────────────────────────────

    [Fact]
    public void AnalyzeVolume_ComputesMinMaxMedianMean_AndCountsZeroWithoutRejecting()
    {
        var bars = new[]
        {
            Bar(0, 100m, 101m, 99m, 100m, volume: 0m),
            Bar(1, 100m, 101m, 99m, 100m, volume: 10m),
            Bar(2, 100m, 101m, 99m, 100m, volume: 20m),
            Bar(3, 100m, 101m, 99m, 100m, volume: 30m),
        };

        VolumeFacts facts = RealMarketQualityAnalyzer.AnalyzeVolume(bars);

        Assert.Equal(0m, facts.Min);
        Assert.Equal(30m, facts.Max);
        Assert.Equal(15m, facts.Median);
        Assert.Equal(15m, facts.Mean);
        Assert.Equal(1, facts.ZeroCount);
        Assert.Equal(0, facts.NegativeCount);
    }

    // ── TEST 7 + 8: session / Symbol / TimeFrame consistency ────────────────────────────────────────

    [Fact]
    public void AnalyzeSessionIntegrity_SingleSessionSymbolTimeFrame_AllTrue()
    {
        var bars = RegularSeries(count: 4, intervalMinutes: 5);
        SessionIntegrityFacts facts = RealMarketQualityAnalyzer.AnalyzeSessionIntegrity(bars);

        Assert.True(facts.SingleSession);
        Assert.True(facts.SingleSymbol);
        Assert.True(facts.SingleTimeFrame);
    }

    [Fact]
    public void AnalyzeSessionIntegrity_MixedSymbol_DetectsContamination()
    {
        var bars = new List<RealMarketBar>(RegularSeries(count: 3, intervalMinutes: 5));
        bars[2] = bars[2] with { Symbol = "NQ" }; // simulated cross-session contamination

        SessionIntegrityFacts facts = RealMarketQualityAnalyzer.AnalyzeSessionIntegrity(bars);

        Assert.True(facts.SingleSession);
        Assert.False(facts.SingleSymbol);
        Assert.Equal(2, facts.DistinctSymbols.Count);
    }

    // ── TEST 9: 40-bar horizon availability ─────────────────────────────────────────────────────────

    [Fact]
    public void AnalyzeHorizonAdequacy_SingleRunLongerThanHorizon_ComputesUsableWindowsCorrectly()
    {
        var bars = RegularSeries(count: 50, intervalMinutes: 5); // one continuous run of 50 bars
        ContinuityFacts continuity = RealMarketQualityAnalyzer.AnalyzeContinuity(bars, TimeSpan.FromMinutes(5));

        HorizonAdequacyFacts facts = RealMarketQualityAnalyzer.AnalyzeHorizonAdequacy(bars.Count, continuity.Runs, horizon: 40);

        Assert.Equal(1, facts.RunCount);
        Assert.Equal(50, facts.LongestRunLength);
        Assert.Equal(10, facts.TheoreticalWindows); // 50 - 40
        Assert.Equal(10, facts.UsableWindows);       // single run, no gap loss
        Assert.Equal(0, facts.BlockedByGaps);
    }

    [Fact]
    public void AnalyzeHorizonAdequacy_GapSplitsRunsBelowHorizon_LosesWindowsToGaps()
    {
        // Two runs of 30 bars each (60 total), split by one gap. Neither run alone reaches horizon=40,
        // so ALL windows are blocked even though the naive (gap-ignoring) count would suggest 20.
        var first = RegularSeries(count: 30, intervalMinutes: 5);
        var second = RegularSeries(count: 30, intervalMinutes: 5, start: first[^1].Timestamp.AddHours(50), startBar: first[^1].CurrentBar + 1);
        var bars = first.Concat(second).ToList();

        ContinuityFacts continuity = RealMarketQualityAnalyzer.AnalyzeContinuity(bars, TimeSpan.FromMinutes(5));
        HorizonAdequacyFacts facts = RealMarketQualityAnalyzer.AnalyzeHorizonAdequacy(bars.Count, continuity.Runs, horizon: 40);

        Assert.Equal(2, facts.RunCount);
        Assert.Equal(20, facts.TheoreticalWindows); // 60 - 40, naive
        Assert.Equal(0, facts.UsableWindows);        // neither 30-bar run reaches 41 bars
        Assert.Equal(20, facts.BlockedByGaps);
    }

    // ── TEST 10: timezone-unresolved behavior ───────────────────────────────────────────────────────

    [Fact]
    public void ClassifyTimezone_UnspecifiedSelfReport_IsUnresolved_NeverGuessed()
    {
        QualityStatus status = RealMarketQualityAnalyzer.ClassifyTimezone(
            "Unspecified (ATAS IndicatorCandle.Time passthrough, no conversion applied)");
        Assert.Equal(QualityStatus.Unresolved, status);
    }

    [Fact]
    public void ClassifyTimezone_ExplicitIanaIdentifier_IsNotUnresolved()
    {
        QualityStatus status = RealMarketQualityAnalyzer.ClassifyTimezone("America/Chicago");
        Assert.Equal(QualityStatus.Pass, status);
    }

    // ── TEST 12: no fabricated-data fallback ────────────────────────────────────────────────────────

    [Fact]
    public void RealMarketOhlcvCsvReader_MissingFile_ThrowsRatherThanFabricatingRows()
    {
        string missingPath = Path.Combine(Path.GetTempPath(), "iqia_sprint1518_missing_" + Guid.NewGuid().ToString("N") + ".csv");
        Assert.Throws<FileNotFoundException>(() => RealMarketOhlcvCsvReader.Read(missingPath));
    }

    [Fact]
    public void RealMarketOhlcvCsvReader_MalformedRow_ThrowsRatherThanSkippingOrDefaulting()
    {
        using var tempDir = new TemporaryDirectory("iqia_sprint1518_malformed_");
        Directory.CreateDirectory(tempDir.Path);
        string path = Path.Combine(tempDir.Path, "malformed.csv");
        File.WriteAllText(path,
            "SessionId,Timestamp,Symbol,TimeFrame,Open,High,Low,Close,Volume,CurrentBar\n" +
            "not-a-guid,2026-01-01T00:00:00.0000000,ES,M5,100,101,99,100,10,0\n");

        Assert.ThrowsAny<FormatException>(() => RealMarketOhlcvCsvReader.Read(path));
    }

    [Fact]
    public void RealMarketOhlcvCsvReader_ValidFixture_RoundTripsExactValues()
    {
        using var tempDir = new TemporaryDirectory("iqia_sprint1518_roundtrip_");
        Directory.CreateDirectory(tempDir.Path);
        string path = Path.Combine(tempDir.Path, "fixture_ohlcv.csv");
        File.WriteAllText(path,
            "SessionId,Timestamp,Symbol,TimeFrame,Open,High,Low,Close,Volume,CurrentBar\n" +
            $"{FixtureSessionId:D},2026-01-05T22:00:00.0000000,ES,M5,100.25,101.5,99.75,100.5,42,0\n");

        IReadOnlyList<RealMarketBar> bars = RealMarketOhlcvCsvReader.Read(path);

        RealMarketBar bar = Assert.Single(bars);
        Assert.Equal(FixtureSessionId, bar.SessionId);
        Assert.Equal("ES", bar.Symbol);
        Assert.Equal("M5", bar.TimeFrame);
        Assert.Equal(100.25m, bar.Open);
        Assert.Equal(101.5m, bar.High);
        Assert.Equal(99.75m, bar.Low);
        Assert.Equal(100.5m, bar.Close);
        Assert.Equal(42m, bar.Volume);
        Assert.Equal(0, bar.CurrentBar);
    }

    // ── Fixture helpers - synthetic, hand-authored, never real market data ─────────────────────────

    private static RealMarketBar Bar(int currentBar, decimal open, decimal high, decimal low, decimal close, decimal volume) =>
        new(FixtureSessionId, FixtureStart.AddMinutes(5 * currentBar), "ES", "M5", open, high, low, close, volume, currentBar);

    private static List<RealMarketBar> RegularSeries(int count, int intervalMinutes, DateTime? start = null, int startBar = 0)
    {
        DateTime t = start ?? FixtureStart;
        var bars = new List<RealMarketBar>(count);
        for (int i = 0; i < count; i++)
        {
            decimal price = 7500m + i;
            bars.Add(new RealMarketBar(FixtureSessionId, t.AddMinutes(intervalMinutes * i), "ES", "M5", price, price + 1m, price - 1m, price + 0.5m, 100m + i, startBar + i));
        }
        return bars;
    }
}
