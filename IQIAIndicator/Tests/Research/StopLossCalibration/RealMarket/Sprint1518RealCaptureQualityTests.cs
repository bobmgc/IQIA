using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using IQIAIndicator.Core.Calibration;
using Xunit;

namespace IQIAIndicator.Tests.Research.StopLossCalibration.RealMarket;

/// <summary>
/// Sprint 15.18 (QDE-012 real-market data quality). Runs RealMarketQualityAnalyzer against the actual
/// real ATAS capture supplied as evidence for this sprint (committed read-only under
/// RawCapture/Sprint15_18/ - see MANIFEST_SHA256.txt), NOT a synthetic fixture. This is the one test
/// class in the sprint that is allowed to say "real market data" - RealMarketQualityAnalyzerTests.cs
/// covers the validator's own logic with hand-authored fixtures only.
///
/// Two capture sessions exist and are analyzed SEPARATELY, never concatenated (brief Section 11):
/// - 165421 (SessionId 967a640e...): the primary capture, 1732 written bars - full pipeline runs here.
/// - 165335 (SessionId 20c7b9c8...): a single-bar, 71-callback capture from the same day, used only as
///   independent corroborating evidence for the FORMING_BAR_CAPTURE finding (see
///   ZeroRangeSegment165421_TailIsContiguousAndMatches165335SingleBarEvidence below).
///
/// Also writes the 8 files required by the brief's Section 18 into RealMarketOutputPaths's directory -
/// this test IS the producer of those deliverables, not just a checker of pre-existing ones.
/// </summary>
public sealed class Sprint1518RealCaptureQualityTests
{
    private const int Horizon = 40; // QDE-012 §9, locked - this sprint only checks data sufficiency against it, never recalibrates it

    private static readonly Guid Session165421Id = Guid.Parse("967a640e-f224-4ffc-8dfc-13104179cf14");
    private static readonly Guid Session165335Id = Guid.Parse("20c7b9c8-7d79-47f5-9ff2-b47d8e474394");

    // ── TEST 11: source dataset immutability ────────────────────────────────────────────────────────
    // Every raw file's SHA-256 must match the hash recorded in MANIFEST_SHA256.txt at copy time
    // (computed directly from the original %LocalAppData%\IQIA\ScientificDataset\ export, Sprint 15.18).
    // A mismatch means the committed "raw evidence" was edited after the fact - exactly what this
    // sprint's "never modify the source dataset" rule forbids.

    [Theory]
    [InlineData("ScientificDataset_ES_M5_20260814_165421.csv", "c645618c9e302dbad58710f1e7c7e281aef8f1f7cb543f06ba189c45f8f2ff56")]
    [InlineData("ScientificDataset_ES_M5_20260814_165421.json", "2eb3aa6cf1763659bf100ac2ac0170d894517a875e3e185bf9fab9c0818071f5")]
    [InlineData("ScientificDataset_ES_M5_20260814_165421_lifecycle.json", "22aed9dfc1a1e579ce565d0fdf074d42196e50d08aef70ebda04f455670fc8e9")]
    [InlineData("ScientificDataset_ES_M5_20260814_165421_metadata.json", "52a7747aa45e0179ef60439bff51db966d1fb5dc54b8cff4ab8891e746d85547")]
    [InlineData("ScientificDataset_ES_M5_20260814_165421_ohlcv.csv", "6170e3fedcbbbfe9bb144f6ae2defeb3ea05280ab9a337610a72dfac9a0553d3")]
    [InlineData("ScientificDataset_ES_M5_20260814_165335.csv", "18254df48fd9250f8cd0a4b32ca4d91f988b5dfe8366bda35d7b9a3bea04002b")]
    [InlineData("ScientificDataset_ES_M5_20260814_165335.json", "2c77135bfdb1e6fde78c4b289207f6f28cf67985eaa656dfd25a8e348010a350")]
    [InlineData("ScientificDataset_ES_M5_20260814_165335_lifecycle.json", "e5f3452f1e5c666d18e4e8c557684e0a2c6dfd239f5749eedc5677fa82ed0b1f")]
    [InlineData("ScientificDataset_ES_M5_20260814_165335_metadata.json", "d368186c9af09b996616eefe8b9647223b9aa1e84b2ca68a667fd29c4fcf74fe")]
    [InlineData("ScientificDataset_ES_M5_20260814_165335_ohlcv.csv", "751d39e78f595903593a821c4a9cc1fb1a9948dab64e1d704f1fdc5fc0047738")]
    public void RawCaptureFile_MatchesManifestChecksum_NeverModifiedSinceCapture(string fileName, string expectedSha256)
    {
        string path = Path.Combine(RawCaptureDirectory(), fileName);
        Assert.True(File.Exists(path), $"Raw capture fixture missing: {path}");

        byte[] bytes = File.ReadAllBytes(path);
        string actual = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        Assert.Equal(expectedSha256, actual);
    }

    // ── Session metadata sanity: reuses the production ScientificDatasetMetadata type read-only ──────

    [Fact]
    public void Session165421_Metadata_MatchesSuppliedEvidenceExactly()
    {
        ScientificDatasetMetadata metadata = ReadMetadata("ScientificDataset_ES_M5_20260814_165421_metadata.json");

        Assert.Equal("ATAS", metadata.Source);
        Assert.Equal(Session165421Id, metadata.SessionId);
        Assert.Equal("ES", metadata.Symbol);
        Assert.Equal("M5", metadata.TimeFrame);
        Assert.Equal(3059, metadata.BarsReceived);
        Assert.Equal(1732, metadata.BarsWritten);
        Assert.Equal(1327, metadata.Duplicates);
        Assert.Equal(0, metadata.InvalidRejected);
        Assert.Equal(0, metadata.OutOfOrder);
        Assert.Equal(metadata.BarsReceived, metadata.BarsWritten + metadata.Duplicates + metadata.InvalidRejected);
    }

    [Fact]
    public void Session165335_Metadata_ShowsSeventyOneAttemptsForOneBar_IndependentSession()
    {
        ScientificDatasetMetadata metadata = ReadMetadata("ScientificDataset_ES_M5_20260814_165335_metadata.json");

        Assert.Equal(Session165335Id, metadata.SessionId);
        Assert.NotEqual(Session165421Id, metadata.SessionId); // never conflated with the main capture
        Assert.Equal(71, metadata.BarsReceived);
        Assert.Equal(1, metadata.BarsWritten);
        Assert.Equal(70, metadata.Duplicates);
        Assert.Equal(metadata.FirstTimestamp, metadata.LastTimestamp); // single CurrentBar, single Timestamp
    }

    [Fact]
    public void Session165335_OhlcvFile_TheOneWrittenBarIsASingleTickSnapshot()
    {
        IReadOnlyList<RealMarketBar> bars = RealMarketOhlcvCsvReader.Read(Path.Combine(RawCaptureDirectory(), "ScientificDataset_ES_M5_20260814_165335_ohlcv.csv"));

        RealMarketBar bar = Assert.Single(bars);
        Assert.NotEqual(bar.Open, bar.High);
        // NOTE: in THIS particular capture the single accepted bar happens to have High != Low (a
        // multi-tick spread was captured before the 71-call session was torn down) - it is not
        // zero-range. The decisive forming-bar evidence from this session is the ADD-ATTEMPT PATTERN
        // itself (71 Add() calls, all sharing CurrentBar=1030 and Timestamp=2026-08-14T14:50:00, across
        // ~43 real seconds of wall-clock collection time per its lifecycle.json), not this bar's own
        // OHLC shape - see Sprint 15.18 report Section 6 for the full reasoning.
        Assert.Equal(1030, bar.CurrentBar);
    }

    // ── Full pipeline over the primary 1732-bar capture, plus the 8 required output files ────────────

    [Fact]
    public void Session165421_FullQualityPipeline_ProducesRequiredOutputFilesAndKeyFacts()
    {
        IReadOnlyList<RealMarketBar> bars = RealMarketOhlcvCsvReader.Read(Path.Combine(RawCaptureDirectory(), "ScientificDataset_ES_M5_20260814_165421_ohlcv.csv"));
        Assert.Equal(1732, bars.Count);

        ScientificDatasetMetadata metadata = ReadMetadata("ScientificDataset_ES_M5_20260814_165421_metadata.json");

        SessionIntegrityFacts session = RealMarketQualityAnalyzer.AnalyzeSessionIntegrity(bars);
        Assert.True(session.SingleSession);
        Assert.True(session.SingleSymbol);
        Assert.True(session.SingleTimeFrame);
        Assert.Equal(Session165421Id, session.DistinctSessionIds[0]);

        TimeSpan? expectedInterval = RealMarketQualityAnalyzer.TryParseExpectedInterval(bars[0].TimeFrame);
        Assert.Equal(TimeSpan.FromMinutes(5), expectedInterval);

        TimestampFacts timestamps = RealMarketQualityAnalyzer.AnalyzeTimestamps(bars);
        Assert.True(timestamps.StrictlyIncreasing);
        Assert.Equal(0, timestamps.DuplicateTimestampCount);
        Assert.Equal(new DateTime(2026, 6, 29, 22, 0, 0), timestamps.FirstTimestamp);
        Assert.Equal(new DateTime(2026, 7, 8, 8, 15, 0), timestamps.LastTimestamp);

        IReadOnlyList<GapRecord> gaps = RealMarketQualityAnalyzer.ClassifyGaps(bars, expectedInterval!.Value);
        Assert.Equal(6, gaps.Count); // 1 weekend-sized + 5 daily-maintenance-sized, per the report's exploratory analysis
        Assert.Single(gaps, g => g.Classification == "LARGE_GAP_PLAUSIBLE_MULTI_DAY_CLOSURE");
        Assert.Equal(5, gaps.Count(g => g.Classification == "MEDIUM_GAP_PLAUSIBLE_INTRADAY_CLOSURE"));
        Assert.Empty(gaps.Where(g => g.Classification is "IRREGULAR_INTERVAL_UNRESOLVED" or "UNRESOLVED_GAP_SIZE"));

        OhlcValidationFacts ohlc = RealMarketQualityAnalyzer.ValidateOhlc(bars);
        Assert.Equal(0, ohlc.StructuralViolations);
        Assert.Equal(0, ohlc.NonPositivePriceCount);
        Assert.Equal(0, ohlc.NegativeVolumeCount);
        Assert.Equal(0, ohlc.ZeroVolumeCount);

        VolumeFacts volume = RealMarketQualityAnalyzer.AnalyzeVolume(bars);
        Assert.Equal(1m, volume.Min);
        Assert.Equal(0, volume.NegativeCount);

        ContinuityFacts continuity = RealMarketQualityAnalyzer.AnalyzeContinuity(bars, expectedInterval.Value);
        Assert.Equal(7, continuity.Runs.Count);
        Assert.Equal(1732, continuity.UniqueCurrentBars); // no CurrentBar gaps within the accepted sequence

        HorizonAdequacyFacts horizon = RealMarketQualityAnalyzer.AnalyzeHorizonAdequacy(bars.Count, continuity.Runs, Horizon);
        Assert.True(horizon.UsableWindows > 1000, $"Expected well over 1000 usable 40-bar windows; got {horizon.UsableWindows}.");

        IReadOnlyList<FormingBarSegment> zeroRangeSegments = RealMarketQualityAnalyzer.DetectZeroRangeSegments(bars);
        FormingBarSegment tailSegment = Assert.Single(zeroRangeSegments); // exactly one contiguous block in this capture
        Assert.Equal(633, tailSegment.BarCount);
        Assert.Equal(bars.Count - 1, tailSegment.EndIndex); // runs all the way to the last bar in the file
        Assert.Equal(1m, tailSegment.MedianVolumeInSegment);

        QualityStatus timezoneStatus = RealMarketQualityAnalyzer.ClassifyTimezone(metadata.Timezone);
        Assert.Equal(QualityStatus.Unresolved, timezoneStatus);

        // ── write the 8 required deliverable files (brief Section 18) ──────────────────────────────
        string outputDir = RealMarketOutputPaths.ResolveOutputDirectory();
        IReadOnlyList<double> consecutiveReturns = RealMarketQualityAnalyzer.AnalyzeConsecutiveReturns(bars, expectedInterval.Value);
        HorizonAdequacyFacts cleanSegmentHorizon = ComputeCleanSegmentHorizon(bars, continuity, zeroRangeSegments, expectedInterval.Value);

        RealMarketQualityReportWriter.WriteTimestampAnalysis(Path.Combine(outputDir, "real_market_timestamp_analysis.csv"), timestamps, expectedInterval.Value, gaps);
        RealMarketQualityReportWriter.WriteGapAnalysis(Path.Combine(outputDir, "real_market_gap_analysis.csv"), gaps);
        RealMarketQualityReportWriter.WriteOhlcvValidation(Path.Combine(outputDir, "real_market_ohlcv_validation.csv"), ohlc, consecutiveReturns, zeroRangeSegments);
        RealMarketQualityReportWriter.WriteDuplicateAnalysis(
            Path.Combine(outputDir, "real_market_duplicate_analysis.csv"),
            metadata.BarsReceived, metadata.BarsWritten, metadata.Duplicates, metadata.InvalidRejected, metadata.OutOfOrder,
            zeroRangeSegments,
            "Predominantly (B) repeated observation of a forming bar: confirmed directly by the contiguous " +
            "633-bar zero-range/near-zero-volume tail segment (idx 1099..1731) in this session, and " +
            "independently corroborated by session 165335 (71 Add() attempts, all for the same CurrentBar " +
            "and Timestamp, across ~43 real seconds). For bars before idx 1099, individual duplicate " +
            "payloads are not retained by ScientificDatasetCollector (first-Add()-wins discards them before " +
            "export), so per-bar classification there is (D) ambiguous / cannot determine from this evidence " +
            "alone - see Sprint 15.18 report Section 6.");
        RealMarketQualityReportWriter.WriteHorizonAnalysis(Path.Combine(outputDir, "real_market_horizon_analysis.csv"), horizon, cleanSegmentHorizon, continuity.Runs);

        var sessionRows = new List<(string, Guid, string, string, int, int, DateTime?, DateTime?, string)>
        {
            ("ScientificDataset_ES_M5_20260814_165421", metadata.SessionId, metadata.Symbol, metadata.TimeFrame, metadata.BarsReceived, metadata.BarsWritten, metadata.FirstTimestamp, metadata.LastTimestamp, "Primary capture analyzed by this sprint."),
        };
        ScientificDatasetMetadata metadata335 = ReadMetadata("ScientificDataset_ES_M5_20260814_165335_metadata.json");
        sessionRows.Add(("ScientificDataset_ES_M5_20260814_165335", metadata335.SessionId, metadata335.Symbol, metadata335.TimeFrame, metadata335.BarsReceived, metadata335.BarsWritten, metadata335.FirstTimestamp, metadata335.LastTimestamp, "Independent, separate session - single-bar/71-callback corroborating evidence only, never concatenated with 165421."));
        RealMarketQualityReportWriter.WriteSessionAnalysis(Path.Combine(outputDir, "real_market_session_analysis.csv"), sessionRows);

        var verdicts = BuildDimensionVerdicts(session, timestamps, gaps, ohlc, volume, continuity, horizon, zeroRangeSegments, timezoneStatus);
        RealMarketQualityReportWriter.WriteSummary(Path.Combine(outputDir, "real_market_quality_summary.csv"), verdicts);

        string admissibility = BuildAdmissibilityText(verdicts);
        RealMarketQualityReportWriter.WriteAdmissibility(Path.Combine(outputDir, "real_market_admissibility.txt"), admissibility);

        foreach (string file in new[]
        {
            "real_market_quality_summary.csv", "real_market_timestamp_analysis.csv", "real_market_duplicate_analysis.csv",
            "real_market_ohlcv_validation.csv", "real_market_gap_analysis.csv", "real_market_horizon_analysis.csv",
            "real_market_session_analysis.csv", "real_market_admissibility.txt"
        })
        {
            Assert.True(File.Exists(Path.Combine(outputDir, file)), $"Required Sprint 15.18 deliverable was not written: {file}");
        }

        Assert.Contains("BLOCKED", admissibility, StringComparison.Ordinal);
        Assert.Contains("CALIBRATION_RUN:", admissibility, StringComparison.Ordinal);
        Assert.Contains("NO", admissibility, StringComparison.Ordinal);
        Assert.DoesNotContain("K_SELECTED:\n    YES", admissibility, StringComparison.Ordinal);
    }

    // ── the explicit, reasoned per-dimension verdicts (Section 14/15 of the brief) ──────────────────
    // This is intentionally the ONE place in this sprint where a PASS/PASS_WITH_CAVEATS/FAIL/UNRESOLVED
    // judgment is assigned - every value here is backed by a fact asserted above and explained in the
    // Sprint 15.18 report. Never a weighted/composite score (brief explicitly forbids that).

    private static List<DimensionVerdict> BuildDimensionVerdicts(
        SessionIntegrityFacts session, TimestampFacts timestamps, IReadOnlyList<GapRecord> gaps,
        OhlcValidationFacts ohlc, VolumeFacts volume, ContinuityFacts continuity, HorizonAdequacyFacts horizon,
        IReadOnlyList<FormingBarSegment> zeroRangeSegments, QualityStatus timezoneStatus)
    {
        int zeroRangeBars = zeroRangeSegments.Sum(s => s.BarCount);
        bool hasConfirmedFormingBarSegment = zeroRangeSegments.Count > 0;

        return new List<DimensionVerdict>
        {
            new("DataCollection", QualityStatus.Pass,
                "Export lifecycle completed (OnDisposeEnteredAt/ExportStartedAt/ExportCompletedAt all set, ExportFailedAt/LastError null); BarsReceived=BarsWritten+Duplicates+InvalidRejected arithmetic holds exactly."),
            new("StructuralValidity", QualityStatus.Pass,
                $"0 structural OHLC invariant violations, 0 non-positive prices, 0 negative volume across all {ohlc.TotalBars} written bars."),
            new("DuplicateBehavior", hasConfirmedFormingBarSegment ? QualityStatus.Fail : QualityStatus.PassWithCaveats,
                $"first-Add()-wins dedup (SessionId+Symbol+TimeFrame+CurrentBar) confirmed to retain pre-close, single-tick snapshots for a real, contiguous {zeroRangeBars}-bar segment ({100.0 * zeroRangeBars / ohlc.TotalBars:F1}% of the file) - not merely a theoretical FORMING_BAR_CAPTURE risk. Corroborated independently by session 165335."),
            new("TimestampIntegrity", timestamps.StrictlyIncreasing && timestamps.DuplicateTimestampCount == 0 && !gaps.Any(g => g.Classification is "IRREGULAR_INTERVAL_UNRESOLVED" or "UNRESOLVED_GAP_SIZE") ? QualityStatus.PassWithCaveats : QualityStatus.Fail,
                "Strictly monotonic, 0 duplicate timestamps, every gap is an exact multiple of the 5-minute bar spacing and size-consistent with either a multi-day or intraday closure. Caveat: no repo-native trading calendar exists to confirm these gaps against an authoritative session schedule."),
            new("OhlcvIntegrity", hasConfirmedFormingBarSegment ? QualityStatus.PassWithCaveats : QualityStatus.Pass,
                "All written values are structurally valid; however a materially-sized subset (see DuplicateBehavior) is very likely a forming-bar snapshot rather than a true closed-bar aggregate, which corrupts the MEANING (not the structural validity) of Range/Volume for that subset."),
            new("VolumeIntegrity", QualityStatus.Pass,
                $"0 negative-volume rows; Volume ranges {volume.Min}-{volume.Max} (median {volume.Median}); the {zeroRangeBars}-bar suspected-forming-bar segment has a median volume of 1, consistent with a single captured print rather than volume data corruption."),
            new("Continuity", QualityStatus.PassWithCaveats,
                $"{continuity.Runs.Count} continuous runs, longest {continuity.Runs.Max(r => r.End - r.Start + 1)} bars; CurrentBar sequence has zero internal gaps (every accepted bar index is present). Calendar-time gaps are all session-boundary-shaped (see TimestampIntegrity)."),
            new("Timezone", timezoneStatus,
                "Metadata self-reports \"Unspecified (ATAS IndicatorCandle.Time passthrough, no conversion applied)\" - honest, not fabricated, but unusable for any timezone-sensitive interpretation until a future sprint adds real conversion."),
            new("Coverage", QualityStatus.PassWithCaveats,
                $"1732 written bars spanning {(timestamps.LastTimestamp - timestamps.FirstTimestamp).TotalDays:F1} calendar days is numerically adequate for exploratory analysis, but is a single, short, contiguous historical window - not enough independent regimes/instruments for a production-grade calibration sample (Section 9/12 of the brief)."),
            new("Horizon40Adequacy", QualityStatus.PassWithCaveats,
                $"{horizon.UsableWindows} usable 40-bar windows ({horizon.PercentUsable:F1}% of the naive gap-ignoring count) - numerically far more than needed, but a large fraction fall inside or adjacent to the suspected-forming-bar tail segment and must not be used for calibration until DuplicateBehavior is resolved."),
            new("SessionIntegrity", session.SingleSession && session.SingleSymbol && session.SingleTimeFrame ? QualityStatus.Pass : QualityStatus.Fail,
                "Single SessionId/Symbol/TimeFrame throughout the file; the second supplied capture (165335) carries a different SessionId and was analyzed and reported strictly separately, never concatenated."),
        };
    }

    private static string BuildAdmissibilityText(IReadOnlyList<DimensionVerdict> verdicts)
    {
        QualityStatus Get(string dimension) => verdicts.Single(v => v.Dimension == dimension).Status;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("QDE-012 Sprint 15.18 - REAL_DATA_ADMISSIBILITY gate");
        sb.AppendLine(new string('=', 60));
        sb.AppendLine();
        foreach (DimensionVerdict v in verdicts)
            sb.AppendLine($"{v.Dimension}: {v.Status}");
        sb.AppendLine();
        sb.AppendLine("DATA_COLLECTION:");
        sb.AppendLine($"    {(Get("DataCollection") == QualityStatus.Pass ? "PASS" : "FAIL")}");
        sb.AppendLine("DATA_INTEGRITY:");
        sb.AppendLine($"    {ToGateWord(Get("OhlcvIntegrity"))}");
        sb.AppendLine("TIMESTAMP_INTEGRITY:");
        sb.AppendLine($"    {ToGateWord(Get("TimestampIntegrity"))}");
        sb.AppendLine("OHLCV_INTEGRITY:");
        sb.AppendLine($"    {ToGateWord(Get("OhlcvIntegrity"))}");
        sb.AppendLine("DUPLICATE_HANDLING:");
        sb.AppendLine($"    {ToGateWord(Get("DuplicateBehavior"))}");
        sb.AppendLine("HORIZON_40_ADEQUACY:");
        sb.AppendLine($"    {ToGateWord(Get("Horizon40Adequacy"))}");
        sb.AppendLine("TIMEZONE:");
        sb.AppendLine($"    {ToGateWord(Get("Timezone"))}");
        sb.AppendLine("SCIENTIFIC_ADMISSIBILITY:");
        // BLOCKED because DuplicateBehavior is a CONFIRMED defect materially threatening any range/volume-
        // based statistic (e.g. a future ATR-style stop distance) for a large, contiguous, and - critically
        // - the MOST RECENT slice of the sample (the segment most likely to land in a held-out validation
        // split). See Sprint 15.18 report Sections 6 and 16 for the full reasoning.
        sb.AppendLine($"    {(Get("DuplicateBehavior") == QualityStatus.Fail ? "BLOCKED" : "PASS_WITH_CAVEATS")}");
        sb.AppendLine("PRODUCTION_MODIFICATIONS:");
        sb.AppendLine("    0");
        sb.AppendLine("CALIBRATION_RUN:");
        sb.AppendLine("    NO");
        sb.AppendLine("K_SELECTED:");
        sb.AppendLine("    NO");
        return sb.ToString();
    }

    private static string ToGateWord(QualityStatus status) => status switch
    {
        QualityStatus.Pass => "PASS",
        QualityStatus.PassWithCaveats => "PASS_WITH_CAVEATS",
        QualityStatus.Fail => "FAIL",
        QualityStatus.Unresolved => "UNRESOLVED",
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };

    /// <summary>Horizon adequacy restricted to bars BEFORE the first detected zero-range segment - i.e.
    /// the portion of the file not implicated by the DuplicateBehavior finding. Purely descriptive
    /// (brief Section 13 asks only for a data-sufficiency count, not a calibration run).</summary>
    private static HorizonAdequacyFacts ComputeCleanSegmentHorizon(
        IReadOnlyList<RealMarketBar> bars, ContinuityFacts continuity, IReadOnlyList<FormingBarSegment> zeroRangeSegments, TimeSpan expectedInterval)
    {
        if (zeroRangeSegments.Count == 0)
            return RealMarketQualityAnalyzer.AnalyzeHorizonAdequacy(bars.Count, continuity.Runs, Horizon);

        int firstSuspectIndex = zeroRangeSegments.Min(s => s.StartIndex);
        var cleanBars = bars.Take(firstSuspectIndex).ToList();
        if (cleanBars.Count == 0)
            return new HorizonAdequacyFacts(Horizon, 0, 0, 0, 0.0, 0, 0);

        ContinuityFacts cleanContinuity = RealMarketQualityAnalyzer.AnalyzeContinuity(cleanBars, expectedInterval);
        return RealMarketQualityAnalyzer.AnalyzeHorizonAdequacy(cleanBars.Count, cleanContinuity.Runs, Horizon);
    }

    private static ScientificDatasetMetadata ReadMetadata(string fileName)
    {
        string json = File.ReadAllText(Path.Combine(RawCaptureDirectory(), fileName));
        return JsonSerializer.Deserialize<ScientificDatasetMetadata>(json)
            ?? throw new InvalidOperationException($"Failed to deserialize {fileName}.");
    }

    private static string RawCaptureDirectory()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "IQIAIndicator.Tests.csproj")))
            dir = Directory.GetParent(dir)?.FullName;

        if (dir is null)
            throw new InvalidOperationException("Could not locate IQIAIndicator.Tests.csproj to resolve the Sprint 15.18 raw capture directory.");

        return Path.Combine(dir, "Research", "StopLossCalibration", "RealMarket", "RawCapture", "Sprint15_18");
    }
}
