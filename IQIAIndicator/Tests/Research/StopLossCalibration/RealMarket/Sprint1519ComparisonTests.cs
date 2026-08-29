using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using IQIAIndicator.Core.Calibration;
using Xunit;

namespace IQIAIndicator.Tests.Research.StopLossCalibration.RealMarket;

/// <summary>
/// Sprint 15.19 (QDE-012 forming-bar-capture fix). Produces the three comparison artifacts required by
/// the brief's Section 19 (Sprint1519_FormingBarAnalysis.csv, Sprint1519_CollectorSemantics.csv,
/// Sprint1519_RealCaptureComparison.csv) and proves the fix's mechanism end to end.
///
/// IMPORTANT HONESTY CONSTRAINT (brief Section 15/21/22): this sprint must NOT fabricate a new real
/// ATAS capture and must NOT claim the fix is validated against real data - only a fresh ATAS Market
/// Replay session, run by the user with this sprint's code, can do that. What this file CAN do
/// honestly, and does:
///   1. Re-derive the real Sprint 15.18 evidence (the 633-bar tail, the 165335 session) from the
///      SAME committed, read-only raw capture fixtures Sprint 15.18 used - unchanged, re-verified.
///   2. Demonstrate the fix's actual mechanism using a SYNTHETIC sequence explicitly modeled on the
///      documented real pattern (71 callbacks for one bar, Session 165335) - run through the REAL
///      pre-15.19 rule (LegacyFirstAddWinsReference, a comparison-only reimplementation) and the REAL
///      Sprint 15.19 ScientificDatasetCollector side by side. This is clearly a reproduction, not a
///      new capture, and is labeled as such everywhere it appears.
///   3. State plainly, in the comparison CSV itself, that "AFTER" numbers for the REAL 165421/165335
///      sessions cannot be computed from the existing exports (the pre-15.19 collector never
///      persisted a rejected duplicate's payload - there is no raw per-callback data left to replay).
/// </summary>
public sealed class Sprint1519ComparisonTests
{
    private static readonly Guid Session165421Id = Guid.Parse("967a640e-f224-4ffc-8dfc-13104179cf14");

    [Fact]
    public void GenerateSprint1519ComparisonArtifacts()
    {
        string outputDir = RealMarketOutputPaths.ResolveOutputDirectory();
        string rawCaptureDir = Path.Combine(outputDir, "RawCapture", "Sprint15_18");

        IReadOnlyList<RealMarketBar> bars165421 = RealMarketOhlcvCsvReader.Read(Path.Combine(rawCaptureDir, "ScientificDataset_ES_M5_20260814_165421_ohlcv.csv"));
        IReadOnlyList<FormingBarSegment> zeroRangeSegments = RealMarketQualityAnalyzer.DetectZeroRangeSegments(bars165421);
        Assert.Single(zeroRangeSegments); // re-confirms Sprint 15.18's finding, unchanged source data

        (IReadOnlyList<ScientificDatasetRecord> legacyResult, IReadOnlyList<ScientificDatasetRecord> newResult, List<ScientificDatasetRecord> syntheticCallbacks) =
            RunSyntheticReplayModeledOnSession165335();

        WriteFormingBarAnalysis(Path.Combine(outputDir, "Sprint1519_FormingBarAnalysis.csv"), bars165421, zeroRangeSegments.Single());
        WriteCollectorSemantics(Path.Combine(outputDir, "Sprint1519_CollectorSemantics.csv"));
        WriteRealCaptureComparison(
            Path.Combine(outputDir, "Sprint1519_RealCaptureComparison.csv"),
            bars165421,
            zeroRangeSegments.Single(),
            syntheticCallbacks,
            legacyResult,
            newResult);

        Assert.True(File.Exists(Path.Combine(outputDir, "Sprint1519_FormingBarAnalysis.csv")));
        Assert.True(File.Exists(Path.Combine(outputDir, "Sprint1519_CollectorSemantics.csv")));
        Assert.True(File.Exists(Path.Combine(outputDir, "Sprint1519_RealCaptureComparison.csv")));
    }

    // ── the mechanism proof itself, asserted directly (not just written to CSV) ─────────────────────

    [Fact]
    public void SyntheticReplayModeledOnSession165335_OldRuleKeepsFirstTick_NewRuleKeepsFinalTick()
    {
        (IReadOnlyList<ScientificDatasetRecord> legacyResult, IReadOnlyList<ScientificDatasetRecord> newResult, List<ScientificDatasetRecord> syntheticCallbacks) =
            RunSyntheticReplayModeledOnSession165335();

        ScientificDatasetRecord legacyKept = Assert.Single(legacyResult);
        ScientificDatasetRecord newKept = Assert.Single(newResult);

        Assert.Equal(syntheticCallbacks[0].Close, legacyKept.Close);   // old: first tick (Sprint 15.18's actual finding)
        Assert.Equal(syntheticCallbacks[0].Volume, legacyKept.Volume);
        Assert.Equal(syntheticCallbacks[^1].Close, newKept.Close);     // new: final tick before the bar advanced
        Assert.Equal(syntheticCallbacks[^1].Volume, newKept.Volume);
        Assert.NotEqual(legacyKept.Close, newKept.Close);
        Assert.True(newKept.Volume > legacyKept.Volume, "the final tick's accumulated volume must exceed the first tick's - matching Session 165335's real signature (single-print Volume=1 vs. a fully-formed bar).");
    }

    // ── synthetic replay construction ────────────────────────────────────────────────────────────
    // Modeled explicitly on the REAL evidence in Session 165335's metadata.json/lifecycle.json
    // (QDE-012_Sprint_15.18 report §6): 71 OnCalculate callbacks for CurrentBar=1030, ES/M5, across
    // ~43 real seconds, before the indicator was removed. This reproduction does not claim to be that
    // capture - the real capture's actual per-tick OHLCV was never persisted (only the first tick
    // survived pre-15.19's dedup) - it only reproduces the CALLBACK PATTERN that is documented and
    // known: N repeated callbacks for one still-forming bar, price/volume evolving as the bar forms.

    private static (IReadOnlyList<ScientificDatasetRecord> Legacy, IReadOnlyList<ScientificDatasetRecord> New, List<ScientificDatasetRecord> Callbacks) RunSyntheticReplayModeledOnSession165335()
    {
        const int callbackCount = 71; // Session 165335: BarsReceived=71 for one bar
        const int currentBar = 1030;  // Session 165335's actual CurrentBar
        var sessionId = Guid.NewGuid(); // synthetic session - never the real 20c7b9c8-... SessionId
        var timestamp = new DateTime(2026, 8, 14, 14, 50, 0, DateTimeKind.Unspecified); // matches the real session's constant bar-open Timestamp

        var callbacks = new List<ScientificDatasetRecord>(callbackCount);
        for (int i = 1; i <= callbackCount; i++)
        {
            decimal close = 7815.00m + (0.05m * i); // price drifts slightly as the bar forms
            decimal high = Math.Max(7815.00m, close) + 1m;
            decimal low = Math.Min(7815.00m, close) - 1m;
            callbacks.Add(new ScientificDatasetRecord(
                sessionId, timestamp, "ES", "M5", close, 1, currentBar,
                new Dictionary<string, double?>(), new Dictionary<string, string>(),
                Open: 7815.00m, High: high, Low: low, Volume: i)); // volume accumulates tick by tick, exactly like a real forming candle
        }

        IReadOnlyList<ScientificDatasetRecord> legacyResult = LegacyFirstAddWinsReference.Simulate(callbacks);

        var newCollector = new ScientificDatasetCollector(sessionId);
        foreach (ScientificDatasetRecord callback in callbacks)
            newCollector.Add(callback);
        newCollector.Add(callbacks[^1] with { CurrentBar = currentBar + 1, Timestamp = timestamp.AddMinutes(5) }); // bar advances, proving bar 1030 closed

        return (legacyResult, newCollector.Records, callbacks);
    }

    // ── CSV writers ──────────────────────────────────────────────────────────────────────────────

    private static void WriteFormingBarAnalysis(string path, IReadOnlyList<RealMarketBar> bars165421, FormingBarSegment tailSegment)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Metric,Value,Source");
        sb.AppendLine($"Session165421_TotalBarsWritten,{bars165421.Count},RawCapture/Sprint15_18 ohlcv.csv (unchanged since Sprint 15.18)");
        sb.AppendLine($"Session165421_ZeroRangeSuspectSegment_BarCount,{tailSegment.BarCount},RealMarketQualityAnalyzer.DetectZeroRangeSegments (unchanged code, re-run)");
        sb.AppendLine($"Session165421_ZeroRangeSuspectSegment_StartIndex,{tailSegment.StartIndex},same");
        sb.AppendLine($"Session165421_ZeroRangeSuspectSegment_EndIndex,{tailSegment.EndIndex},same");
        sb.AppendLine($"Session165421_ZeroRangeSuspectSegment_StartTimestamp,{tailSegment.StartTimestamp:O},same");
        sb.AppendLine($"Session165421_ZeroRangeSuspectSegment_MedianVolume,{tailSegment.MedianVolumeInSegment.ToString(CultureInfo.InvariantCulture)},same");
        sb.AppendLine($"Session165421_ZeroRangeSuspectSegment_PercentOfFile,{(100.0 * tailSegment.BarCount / bars165421.Count).ToString("F1", CultureInfo.InvariantCulture)},computed");
        sb.AppendLine("Session165335_BarsReceived,71,RawCapture/Sprint15_18 165335_metadata.json (unchanged)");
        sb.AppendLine("Session165335_BarsWritten_UnderPre15_19Semantics,1,same");
        sb.AppendLine("Session165335_Duplicates_UnderPre15_19Semantics,70,same (all 70 were legitimate forming-bar updates, not true duplicates - see Sprint 15.19 report Section 2)");
        sb.AppendLine("Session165335_CollectionWallClockSpan_Seconds,43.07,165335_lifecycle.json: OnDisposeEnteredAt(14:54:18.6976575Z) - DatasetEnabledAt(14:53:35.632299Z)");
        sb.AppendLine("RootCause,\"first-Add()-wins dedup committed the FIRST snapshot per CurrentBar, not the final one\",ScientificDatasetCollector.cs pre-Sprint-15.19 source (git history)");
        sb.AppendLine("ATAS_SDK_Explicit_CloseSignal_Found,NO,\"reflection audit of ATAS.Indicators.dll/ATAS.DataFeedsCore.dll/ATAS.Types.dll - see Sprint 15.19 report Section 3\"");
        sb.AppendLine("ATAS_SDK_UsableSignal,\"BaseIndicator.CurrentBar (already read by IQIAIndicator.cs) compared against the OnCalculate 'bar' parameter\",\"same reflection audit; already computed as MarketContext.Execution.IsHistorical/IsRealtime in MarketContextBuilder.cs, unused by the collector pre-15.19\"");
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static void WriteCollectorSemantics(string path)
    {
        var rows = new (string Dimension, string Old, string New)[]
        {
            ("DedupIdentity", "SessionId+Symbol+TimeFrame+CurrentBar", "unchanged - same logical bar identity"),
            ("CommitPolicy", "first Add() wins - committed immediately on first valid callback", "last update before bar-advance wins - committed only once a later bar proves this one closed"),
            ("SecondCallbackSameBar", "rejected, counted as DuplicateRecordsRejected", "accepted as FormingBarUpdates; replaces the pending snapshot (latest wins)"),
            ("TrueDuplicateDefinition", "any 2nd+ callback for a CurrentBar already seen (finalized or not)", "re-delivery of an ALREADY-FINALIZED (closed, immutable) bar only"),
            ("FinalizationTrigger", "the Add() call itself", "a later callback's CurrentBar advancing past the pending bar, a Symbol/TimeFrame change, or explicit Flush()"),
            ("OnDisposeBehavior", "whatever was collected so far is exported as-is, including a possibly still-forming last bar", "EXCLUDE_CURRENT_FORMING_BAR - Flush() marks the still-pending bar excluded; never exported as if closed"),
            ("OutOfOrderMeaning", "an accepted record's Timestamp regressed vs. the previously accepted record", "a callback's CurrentBar rewinds behind the pending bar without matching an already-finalized one (e.g. an ambiguous Replay rewind)"),
            ("NewDiagnostics", "n/a", "FormingBarUpdates, BarsPendingAtDispose, PendingBar, PendingBarFirstSeenAt/LastSeenAt"),
            ("BackwardCompatibility", "n/a", "RecordsAccepted/DuplicateRecordsRejected/RecordsOutOfOrder property names and Errors/BarsCollected formulas kept unchanged for DatasetDashboard/ScientificCollectionMonitorWidget/SystemHealthAggregator (outside this sprint's allowed-file list); BarsFinalized/BarsDuplicated/BarsOutOfOrder added as same-value aliases under their Sprint 15.19 names"),
            ("MetadataSchema", "ScientificDatasetMetadata: 15 fields", "+FormingBarUpdates, +BarsPendingAtDispose, both optional/defaulted to 0 so Sprint 15.18's committed raw metadata.json fixtures still deserialize unchanged"),
        };

        var sb = new StringBuilder();
        sb.AppendLine("Dimension,OldSemantics_PreSprint1519,NewSemantics_Sprint1519");
        foreach (var row in rows)
            sb.AppendLine(string.Join(",", row.Dimension, Escape(row.Old), Escape(row.New)));
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static void WriteRealCaptureComparison(
        string path,
        IReadOnlyList<RealMarketBar> bars165421,
        FormingBarSegment tailSegment,
        List<ScientificDatasetRecord> syntheticCallbacks,
        IReadOnlyList<ScientificDatasetRecord> legacyResult,
        IReadOnlyList<ScientificDatasetRecord> newResult)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Metric,Before_MeasuredFromRealSession165421,After_Sprint1519,Basis");
        sb.AppendLine($"TotalCallbacks(BarsReceived),3059,NOT_COMPUTABLE_REQUIRES_NEW_CAPTURE,\"165421_metadata.json (real, unchanged). AFTER cannot be computed: needs a fresh ATAS session run with the Sprint 15.19 collector.\"");
        sb.AppendLine($"FinalizedBars(BarsWritten),1732,NOT_COMPUTABLE_REQUIRES_NEW_CAPTURE,\"same - the pre-15.19 export never persisted rejected duplicates' raw OHLCV, so there is no per-callback data left to replay through the new rule for this real session.\"");
        sb.AppendLine($"ZeroRangeSingleTickTailBars,{tailSegment.BarCount},NOT_COMPUTABLE_REQUIRES_NEW_CAPTURE,\"same limitation - expected to shrink toward 0 on a fresh capture (see Synthetic Replay rows below for the proven mechanism), but the actual number can only come from re-running ATAS.\"");
        sb.AppendLine($"ZeroRangeTailMedianVolume,{tailSegment.MedianVolumeInSegment.ToString(CultureInfo.InvariantCulture)},NOT_COMPUTABLE_REQUIRES_NEW_CAPTURE,same limitation");
        sb.AppendLine();
        sb.AppendLine("-- Synthetic replay (Session 165335 pattern reproduction - NOT a new real capture; see class doc comment) --");
        sb.AppendLine("Metric,Before_LegacyFirstAddWinsSimulation,After_Sprint1519RealCollector,Basis");
        sb.AppendLine($"TotalCallbacks,{syntheticCallbacks.Count},{syntheticCallbacks.Count},\"synthetic sequence modeled on Session 165335's documented pattern (71 callbacks, 1 bar, ~43s span)\"");
        sb.AppendLine($"FinalizedRecordsForThisBar,{legacyResult.Count},{newResult.Count},\"both correctly commit exactly 1 record for the 1 bar - the difference is WHICH snapshot\"");
        sb.AppendLine($"CommittedClose,{legacyResult[0].Close.ToString(CultureInfo.InvariantCulture)},{newResult[0].Close.ToString(CultureInfo.InvariantCulture)},\"legacy = 1st callback's Close; new = 71st (final) callback's Close\"");
        sb.AppendLine($"CommittedVolume,{legacyResult[0].Volume.ToString(CultureInfo.InvariantCulture)},{newResult[0].Volume.ToString(CultureInfo.InvariantCulture)},\"legacy = 1 (single tick, matches the real session's signature); new = {syntheticCallbacks.Count} (full accumulated volume)\"");
        sb.AppendLine($"MatchesRealSession165335Signature,YES,N/A,\"legacy result reproduces the documented real Volume=1/first-tick signature exactly\"");
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static string Escape(string value) =>
        value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0 ? value : $"\"{value.Replace("\"", "\"\"")}\"";
}
