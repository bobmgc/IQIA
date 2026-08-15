using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using IQIAIndicator.Core.Calibration;
using IQIAIndicator.Engine.Decision.Core;
using IQIAIndicator.Engine.Decision.States;
using IQIAIndicator.Engine.ScientificFusion;
using IQIAIndicator.Engine.ScientificModels.Abstractions;
using Xunit;
using ScientificMarketContext = IQIAIndicator.Engine.ScientificModels.Abstractions.MarketContext;

namespace IQIAIndicator.Tests.Calibration;

/// <summary>
/// Sprint 15.17 (QDE-012 real-market capture) - the 15 tests required by the sprint brief's Phase 11.
/// These tests prove ONLY that ScientificDatasetCollector/ScientificDatasetRecord/
/// ScientificDatasetSessionWriter behave correctly when fed valid or invalid inputs directly - IQIAIndicator
/// itself cannot be unit-tested (it derives from ATAS's Indicator base class, which requires the live
/// ATAS platform to construct - confirmed by the absence of "new IQIAIndicator(" anywhere in this test
/// suite, pre-existing or new). This is the same distinction Phase 10 of the brief itself requires:
/// "le collector fonctionne lorsqu'il reçoit un événement valide" is what is proven here; "ATAS fournit
/// correctement cet événement" is NOT and cannot be proven by any test in this file - only a real ATAS
/// Market Replay run (Phase 12/13/14 of the brief) can establish that.
/// </summary>
public sealed class ScientificDatasetRealMarketCaptureTests
{
    // ── TEST 1: OHLCV mapping correct ────────────────────────────────────────────────────────────

    [Fact]
    public void From_MapsOpenHighLowCloseVolume_Correctly()
    {
        ScientificDatasetRecord record = BuildFrom(open: 100.25m, high: 101.50m, low: 99.75m, close: 100.90m, volume: 4321m);

        Assert.Equal(100.25m, record.Open);
        Assert.Equal(101.50m, record.High);
        Assert.Equal(99.75m, record.Low);
        Assert.Equal(100.90m, record.Close);
        Assert.Equal(100.90m, record.CurrentPrice); // Close is CurrentPrice under its historical name
        Assert.Equal(4321m, record.Volume);
    }

    // ── TEST 2: Timestamp conservé ───────────────────────────────────────────────────────────────

    [Fact]
    public void From_PreservesTimestamp_Exactly()
    {
        var timestamp = new DateTime(2026, 3, 5, 14, 30, 0, DateTimeKind.Utc);
        ScientificDatasetRecord record = BuildFrom(timestamp: timestamp);

        Assert.Equal(timestamp, record.Timestamp);
    }

    // ── TEST 3: Symbol conservé ──────────────────────────────────────────────────────────────────

    [Fact]
    public void From_PreservesSymbol_Exactly()
    {
        ScientificDatasetRecord record = BuildFrom(symbol: "ESH26");

        Assert.Equal("ESH26", record.Symbol);
    }

    // ── TEST 4: TimeFrame conservé ───────────────────────────────────────────────────────────────

    [Fact]
    public void From_PreservesTimeFrame_Exactly()
    {
        ScientificDatasetRecord record = BuildFrom(timeFrame: "5min");

        Assert.Equal("5min", record.TimeFrame);
    }

    // ── TEST 5: Second callback for the same still-forming bar updates the pending snapshot ─────────
    // Sprint 15.19 (QDE-012 forming-bar-capture fix) supersedes this test's original assertions.
    // Pre-15.19 it asserted that a second callback for the same CurrentBar was rejected as a
    // "duplicate" and that the FIRST snapshot was already "accepted" after just one call - exactly the
    // first-Add()-wins behavior QDE-012_Sprint_15.18_ATAS_Real_Data_Quality_Report.md §6 proved unsafe
    // against real ATAS data (session 165335: 71 callbacks for one still-open bar; the primary
    // capture's 633-bar tail was single-tick snapshots committed this way). Under the new contract
    // (ScientificDatasetCollector.cs class doc comment) a second callback for the same bar is a
    // legitimate FormingBarUpdates event, not a duplicate, and nothing is "accepted" until a callback
    // for a LATER bar proves this one has closed.

    [Fact]
    public void Add_SecondCallbackForSameCurrentBar_UpdatesFormingSnapshot_NotRejectedAsDuplicate()
    {
        var sessionId = Guid.NewGuid();
        var collector = new ScientificDatasetCollector(sessionId);
        ScientificDatasetRecord firstTick = BuildFrom(sessionId: sessionId, currentBar: 12, close: 100.5m);
        ScientificDatasetRecord laterTick = BuildFrom(sessionId: sessionId, currentBar: 12, close: 101.0m);

        collector.Add(firstTick);
        collector.Add(laterTick);

        Assert.Equal(0, collector.RecordsAccepted); // bar 12 still open - nothing finalized yet
        Assert.Equal(1, collector.FormingBarUpdates);
        Assert.Equal(0, collector.DuplicateRecordsRejected);
        Assert.Equal(2, collector.TotalAddAttempts);
        Assert.Equal(101.0m, collector.PendingBar!.Close); // latest tick wins, not the first

        collector.Add(BuildFrom(sessionId: sessionId, currentBar: 13)); // proves bar 12 closed

        Assert.Equal(1, collector.RecordsAccepted);
        Assert.Equal(101.0m, collector.Records[0].Close); // the FINAL observed state was committed, not the first
    }

    // ── TEST 6: Bar invalide rejetée ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(100, 99, 100, 100, 10)]   // High < Low
    [InlineData(100, 105, 99, 110, 10)]   // High < Close
    [InlineData(100, 105, 101, 95, 10)]   // Low > Open
    [InlineData(100, 105, 99, 100, -5)]   // negative Volume
    public void Add_StructurallyInvalidBar_IsRejected(decimal open, decimal high, decimal low, decimal close, decimal volume)
    {
        var sessionId = Guid.NewGuid();
        var collector = new ScientificDatasetCollector(sessionId);
        ScientificDatasetRecord record = BuildFrom(sessionId: sessionId, open: open, high: high, low: low, close: close, volume: volume);

        collector.Add(record);

        Assert.Equal(0, collector.RecordsAccepted);
        Assert.Equal(1, collector.InvalidRecordsRejected);
        Assert.Equal(0, collector.Count);
        Assert.NotEmpty(collector.RejectedReason);
    }

    [Fact]
    public void Add_StructurallyValidBar_IsAcceptedAsPending_NotRejected()
    {
        // Sprint 15.19: "accepted" (not rejected as invalid) no longer means "immediately finalized" -
        // see TEST 5's updated doc comment. A single valid callback becomes the pending bar; it is
        // proven closed (and only then counted in RecordsAccepted) once a later bar arrives.
        var sessionId = Guid.NewGuid();
        var collector = new ScientificDatasetCollector(sessionId);
        // High == max(Open,Close) and Low == min(Open,Close) exactly - boundary case, must be valid.
        ScientificDatasetRecord record = BuildFrom(sessionId: sessionId, currentBar: 0, open: 100m, high: 100m, low: 98m, close: 98m, volume: 0m);

        collector.Add(record);

        Assert.Equal(0, collector.InvalidRecordsRejected);
        Assert.Equal(0, collector.RecordsAccepted);
        Assert.NotNull(collector.PendingBar);
        Assert.Equal(98m, collector.PendingBar!.Close);

        collector.Add(BuildFrom(sessionId: sessionId, currentBar: 1));

        Assert.Equal(1, collector.RecordsAccepted);
    }

    // ── TEST 7: Out-of-order CALLBACKS (accepted... i.e. counted, not applied) ──────────────────────
    // Sprint 15.19 supersedes this test pair's original meaning. Pre-15.19, "out of order" meant "an
    // ACCEPTED record's Timestamp regressed vs. the previously accepted record's Timestamp" - a
    // per-record check that made sense only because every Add() call was immediately accepted or
    // rejected on the spot (first-Add()-wins). Under the new pending/finalize contract there is no
    // longer a single "the record was just accepted" moment to hang that check on - a record's
    // Timestamp is whatever ATAS reports for that bar, taken as-is (Section 10 of the QDE-012
    // Sprint 15.19 brief: never invented, never reordered). What genuinely CAN go out of order now is
    // the CALLBACK STREAM itself: ATAS invoking OnCalculate for a CurrentBar behind the one currently
    // pending (edge case G, brief §6) - e.g. a Market Replay rewind. RecordsOutOfOrder now measures
    // exactly that: a rewind that does not match any already-finalized bar (which would instead be a
    // true duplicate - see Add_TrueDuplicate_ReDeliveryOfAlreadyFinalizedBar_IsRejected in
    // ScientificDatasetCollectorTests.cs). Per the brief's instruction not to blindly preserve
    // obsolete assumptions, this pair is rewritten around that new, real behavior rather than kept
    // passing by coincidence.

    [Fact]
    public void Add_OutOfOrderBarIndex_IsCountedButPendingBarIsLeftUntouched()
    {
        var sessionId = Guid.NewGuid();
        var collector = new ScientificDatasetCollector(sessionId);

        collector.Add(BuildFrom(sessionId: sessionId, currentBar: 0));
        collector.Add(BuildFrom(sessionId: sessionId, currentBar: 5)); // finalizes bar 0, pending becomes bar 5
        // Rewind: behind pending(5), ahead of last-finalized(0). Marker OHLC (200/205/195/202) is
        // structurally VALID but clearly distinct from the pending bar's default (100/101/99/100.5) -
        // proves this call's payload was never applied, not merely that its CurrentBar wasn't.
        collector.Add(BuildFrom(sessionId: sessionId, currentBar: 3, open: 200m, high: 205m, low: 195m, close: 202m));

        Assert.Equal(1, collector.RecordsAccepted); // only bar 0 - the rewind was not applied
        Assert.Equal(0, collector.DuplicateRecordsRejected);
        Assert.Equal(0, collector.InvalidRecordsRejected);
        Assert.Equal(1, collector.RecordsOutOfOrder);
        Assert.Equal(5, collector.PendingBar!.CurrentBar); // untouched by the rewind
        Assert.NotEqual(202m, collector.PendingBar.Close);
    }

    [Fact]
    public void Add_StrictlyAdvancingBarIndices_NeverIncrementsOutOfOrder()
    {
        var sessionId = Guid.NewGuid();
        var collector = new ScientificDatasetCollector(sessionId);
        var t0 = new DateTime(2026, 3, 5, 10, 0, 0, DateTimeKind.Utc);

        for (int i = 0; i <= 5; i++)
            collector.Add(BuildFrom(sessionId: sessionId, currentBar: i, timestamp: t0.AddMinutes(i)));

        Assert.Equal(0, collector.RecordsOutOfOrder);
        Assert.Equal(5, collector.RecordsAccepted); // bars 0..4 finalized; bar 5 still pending
        Assert.Equal(t0, collector.FirstTimestamp);
        Assert.Equal(t0.AddMinutes(4), collector.LastTimestamp);
    }

    // ── TEST 8: Collection OFF ne persiste rien ──────────────────────────────────────────────────
    // (IQIAIndicator itself cannot be instantiated in tests - see class doc comment. What IS testable:
    // an empty collector - the state EnableScientificDataset=false leaves it in, since Add() is never
    // called at all in that branch of IQIAIndicator.OnCalculate - exports no data rows.)

    [Fact]
    public void EmptyCollector_ExportsNoDataRows()
    {
        var collector = new ScientificDatasetCollector(Guid.NewGuid());

        string csv = collector.ToOhlcvCsv();
        string[] lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Single(lines); // header only, zero data rows
        Assert.Equal(0, collector.Count);
        Assert.Equal(0, collector.TotalAddAttempts);
    }

    // ── TEST 9: Collection ON accepte une observation ────────────────────────────────────────────
    // Sprint 15.19: "accepted" (not rejected as invalid, becomes the pending bar) rather than
    // "immediately finalized" - see TEST 5/6's updated doc comments. Status only ever depended on
    // TotalAddAttempts > 0, unaffected by this sprint.

    [Fact]
    public void Add_ValidObservation_IsAccepted()
    {
        var collector = new ScientificDatasetCollector(Guid.NewGuid());
        collector.Add(BuildFrom(sessionId: collector.SessionId, currentBar: 0));

        Assert.Equal(0, collector.InvalidRecordsRejected);
        Assert.NotNull(collector.PendingBar);
        Assert.Equal("Collecting", collector.Status);

        collector.Add(BuildFrom(sessionId: collector.SessionId, currentBar: 1));

        Assert.Equal(1, collector.Count);
        Assert.Equal(1, collector.RecordsAccepted);
    }

    // ── TEST 10: Export produit un fichier ───────────────────────────────────────────────────────

    [Fact]
    public void SessionWriter_Export_WritesAllFourFiles()
    {
        var sessionId = Guid.NewGuid();
        var collector = new ScientificDatasetCollector(sessionId);
        collector.Add(BuildFrom(sessionId: sessionId, symbol: "ESH26", timeFrame: "1min"));

        using var tempDir = new TemporaryDirectory();
        var writer = new ScientificDatasetSessionWriter();
        DateTime start = DateTime.UtcNow.AddMinutes(-1);
        DateTime end = DateTime.UtcNow;

        ScientificDatasetSession session = writer.Export(collector, tempDir.Path, "ESH26", "1min", sessionId, start, end);

        Assert.True(File.Exists(session.CSVPath));
        Assert.True(File.Exists(session.JSONPath));
        Assert.True(File.Exists(session.OhlcvCsvPath));
        Assert.True(File.Exists(session.MetadataPath));
        Assert.Equal("ATAS", session.Source);
    }

    // ── TEST 11: CSV contient OHLCV ──────────────────────────────────────────────────────────────
    // Sprint 15.19: a second, advancing Add() is needed to finalize bar 0 before it appears in export.

    [Fact]
    public void ToOhlcvCsv_ContainsOhlcvHeaderAndValues()
    {
        var sessionId = Guid.NewGuid();
        var collector = new ScientificDatasetCollector(sessionId);
        collector.Add(BuildFrom(sessionId: sessionId, currentBar: 0, open: 4500.25m, high: 4510.75m, low: 4495.00m, close: 4505.50m, volume: 12345m));
        collector.Add(BuildFrom(sessionId: sessionId, currentBar: 1)); // proves bar 0 closed

        string csv = collector.ToOhlcvCsv();
        string[] lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("SessionId,Timestamp,Symbol,TimeFrame,Open,High,Low,Close,Volume,CurrentBar", lines[0].TrimEnd('\r'));
        Assert.Contains("4500.25", lines[1]);
        Assert.Contains("4510.75", lines[1]);
        Assert.Contains("4495.00", lines[1]);
        Assert.Contains("4505.50", lines[1]);
        Assert.Contains("12345", lines[1]);
    }

    // ── TEST 12: Metadata correcte ───────────────────────────────────────────────────────────────
    // Sprint 15.19: rebuilt around the new semantics so every metadata counter is exercised for real,
    // rather than relying on the pre-15.19 "second callback for the same bar = duplicate" premise
    // (QDE-012_Sprint_15.18 report §6 proved that specific premise unsafe against real ATAS data).
    // Scenario: bar 0 receives a forming-bar update, then is finalized by bar 1's arrival; an invalid
    // callback for bar 2 is rejected; bar 0 is then re-delivered (a true duplicate, since it is by now
    // already finalized).

    [Fact]
    public void Metadata_ReportsRealCountersAndSource_NotFabricated()
    {
        var sessionId = Guid.NewGuid();
        var collector = new ScientificDatasetCollector(sessionId);
        ScientificDatasetRecord bar0 = BuildFrom(sessionId: sessionId, currentBar: 0);
        ScientificDatasetRecord bar1 = BuildFrom(sessionId: sessionId, currentBar: 1);
        ScientificDatasetRecord invalidBar2 = BuildFrom(sessionId: sessionId, currentBar: 2, open: 100m, high: 90m, low: 80m, close: 100m);

        collector.Add(bar0);
        collector.Add(bar0);        // forming-bar update (bar 0 still open)
        collector.Add(bar1);        // finalizes bar 0
        collector.Add(invalidBar2); // structurally invalid - rejected, bar 1 stays pending
        collector.Add(bar0);        // re-delivery of the now-finalized bar 0 - a true duplicate

        using var tempDir = new TemporaryDirectory();
        var writer = new ScientificDatasetSessionWriter();
        DateTime start = DateTime.UtcNow.AddMinutes(-1);
        DateTime end = DateTime.UtcNow;
        ScientificDatasetSession session = writer.Export(collector, tempDir.Path, "ESH26", "1min", sessionId, start, end);

        string json = File.ReadAllText(session.MetadataPath!);
        ScientificDatasetMetadata? metadata = JsonSerializer.Deserialize<ScientificDatasetMetadata>(json);

        Assert.NotNull(metadata);
        Assert.Equal("ATAS", metadata!.Source);
        Assert.Equal(sessionId, metadata.SessionId);
        Assert.Equal(5, metadata.BarsReceived);
        Assert.Equal(1, metadata.BarsWritten);    // bar 0, finalized
        Assert.Equal(1, metadata.Duplicates);      // the re-delivery of bar 0
        Assert.Equal(1, metadata.InvalidRejected); // bar 2's invalid callback
        Assert.Equal(1, metadata.FormingBarUpdates); // bar 0's second (still-forming) callback
        Assert.Equal(0, metadata.BarsPendingAtDispose); // Flush() was never called in this test - honestly 0, not assumed
        Assert.False(string.IsNullOrWhiteSpace(metadata.Timezone));
        Assert.False(string.IsNullOrWhiteSpace(metadata.CollectorVersion));
    }

    // ── TEST 13: Source series/input non mutée ───────────────────────────────────────────────────

    [Fact]
    public void From_DoesNotMutateInputs()
    {
        ScientificMarketContext marketContext = BuildScientificMarketContext();
        ScientificAssessment assessment = BuildAssessment();
        DecisionResult decision = BuildDecision();
        ScientificMarketContext marketContextSnapshot = marketContext with { };
        ScientificAssessment assessmentSnapshot = assessment with { };
        DecisionResult decisionSnapshot = decision with { };

        ScientificDatasetRecord.From(Guid.NewGuid(), 0, marketContext, assessment, decision, 100m, 101m, 99m, 10m);

        Assert.Equal(marketContextSnapshot, marketContext);
        Assert.Equal(assessmentSnapshot, assessment);
        Assert.Equal(decisionSnapshot, decision);
    }

    [Fact]
    public void Add_DoesNotMutateSourceRecord()
    {
        var collector = new ScientificDatasetCollector(Guid.NewGuid());
        ScientificDatasetRecord record = BuildFrom(sessionId: collector.SessionId);
        ScientificDatasetRecord snapshot = record with { };

        collector.Add(record);

        Assert.Equal(snapshot, record);
    }

    // ── TEST 14: Scientific metrics existantes toujours présentes ───────────────────────────────────

    [Fact]
    public void From_StillPopulatesScientificMetricsAndCategories_UnchangedByOhlcvAddition()
    {
        ScientificAssessment assessment = BuildAssessment();
        ScientificDatasetRecord record = ScientificDatasetRecord.From(
            Guid.NewGuid(), 0, BuildScientificMarketContext(), assessment, BuildDecision(),
            open: 100m, high: 101m, low: 99m, volume: 10m);

        Assert.Equal(0.42, record.Metrics["KalmanFilterModel.Score"]);
        Assert.Equal(0.42, record.Metrics["KalmanFilterModel.InnovationStd"]);
        Assert.Equal("MeanReverting", record.Categories["Decision.Winner"]);
        Assert.True(record.Metrics.ContainsKey("Fusion.OverallConfidence"));
    }

    // ── TEST 15: Collection ON vs OFF must not affect the trading pipeline ──────────────────────────
    // IQIAIndicator cannot be instantiated in tests (requires the live ATAS platform - see class doc
    // comment), so full Regime/Fusion/Decision/Signal/TradePlan pipeline-identity cannot be asserted
    // end-to-end here. What IS mechanically provable, and is exactly what makes that claim true by
    // construction at the IQIAIndicator.cs branch point: the collection call is read-only with respect
    // to every pipeline object it touches (proven by TEST 13 above) and it is the LAST statement in
    // OnCalculate, after every engine has already produced and stored its result - so nothing
    // downstream of it can observe whether it ran. This test proves the read-only half of that
    // argument the same way TEST 13 does, from a second, independent angle: running Add() within
    // an otherwise-identical collection state must not change what a THIRD, later read of the same
    // inputs would observe.
    [Fact]
    public void CollectionActivity_NeverChangesWhatLaterCodeObservesFromTheSameInputs()
    {
        ScientificMarketContext marketContext = BuildScientificMarketContext();
        ScientificAssessment assessment = BuildAssessment();
        DecisionResult decision = BuildDecision();

        // "Collection OFF": nothing happens.
        ScientificMarketContext offSnapshot = marketContext with { };

        // "Collection ON": the same objects flow through From()/Add().
        var collector = new ScientificDatasetCollector(Guid.NewGuid());
        collector.Add(ScientificDatasetRecord.From(collector.SessionId, 0, marketContext, assessment, decision, 100m, 101m, 99m, 10m));

        Assert.Equal(offSnapshot, marketContext);
        Assert.Equal(MarketState.MeanReverting, decision.Winner); // unchanged regardless of collection
        Assert.Equal(0.42, assessment.ScientificResults[0].Score); // unchanged regardless of collection
    }

    // ── Fixtures ─────────────────────────────────────────────────────────────────────────────────

    private static ScientificDatasetRecord BuildFrom(
        Guid? sessionId = null,
        int currentBar = 0,
        string symbol = "TESTSYM",
        string timeFrame = "1min",
        DateTime? timestamp = null,
        decimal open = 100m,
        decimal high = 101m,
        decimal low = 99m,
        decimal close = 100.5m,
        decimal volume = 10m)
    {
        // CurrentPrice on the scientific MarketContext IS Close (see ScientificDatasetRecord.Close /
        // IQIAIndicator.cs:553's context.Price.Close) - must be threaded through here, not defaulted,
        // or every High/Low/Open/Close consistency check below is validated against the wrong value.
        ScientificMarketContext marketContext = BuildScientificMarketContext(symbol, timeFrame, timestamp, close);
        return ScientificDatasetRecord.From(
            sessionId ?? Guid.NewGuid(),
            currentBar,
            marketContext,
            BuildAssessment(),
            BuildDecision(),
            open,
            high,
            low,
            volume);
    }

    private static ScientificMarketContext BuildScientificMarketContext(string symbol = "TESTSYM", string timeFrame = "1min", DateTime? timestamp = null, decimal close = 100.5m) =>
        new(
            Timestamp: timestamp ?? new DateTime(2026, 3, 5, 12, 0, 0, DateTimeKind.Utc),
            CurrentBar: 0,
            History: new List<decimal> { 100m, close },
            CurrentPrice: close,
            Symbol: symbol,
            TimeFrame: timeFrame);

    private static ScientificAssessment BuildAssessment() => new(
        OverallConfidence: 0.75,
        EvidenceAgreement: new[] { "Kalman" },
        EvidenceConflict: Array.Empty<string>(),
        MissingEvidence: Array.Empty<string>(),
        ExecutedModels: new[] { "KalmanFilterModel" },
        SuccessfulModels: new[] { "KalmanFilterModel" },
        FailedModels: Array.Empty<string>(),
        ScientificResults: new[]
        {
            new ScientificModelResult(
                "KalmanFilterModel",
                Success: true,
                Score: 0.42,
                Explanation: "test fixture",
                Metrics: new Dictionary<string, object> { ["InnovationStd"] = 0.42 })
        },
        Diagnostics: "test fixture");

    private static DecisionResult BuildDecision() => new()
    {
        Winner = MarketState.MeanReverting,
        WinnerScore = 0.8,
        Candidates = ImmutableArray<Engine.Decision.Arbitration.DecisionCandidate>.Empty,
        AmbiguityScore = 0.1,
        State = MarketState.MeanReverting,
        Confidence = 0.8,
        Explanation = "test fixture",
        RuleExplanation = "test fixture",
        ArbitrationExplanation = "test fixture",
        TriggeredRules = new[] { "MeanRevertingRule" },
        RejectedRules = Array.Empty<string>()
    };
}
