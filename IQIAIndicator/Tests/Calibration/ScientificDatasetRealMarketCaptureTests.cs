using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using IQIAIndicator.Core;
using IQIAIndicator.Core.Calibration;
using IQIAIndicator.Engine.Decision.Core;
using IQIAIndicator.Engine.Decision.States;
using IQIAIndicator.Engine.Entry;
using IQIAIndicator.Engine.EntryTrigger;
using IQIAIndicator.Engine.Risk;
using IQIAIndicator.Engine.ScientificFusion;
using IQIAIndicator.Engine.ScientificModels.Abstractions;
using IQIAIndicator.Engine.TradePlan;
using IQIAIndicator.Infrastructure.ATAS;
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

    // ── TEST 16 (Sprint 15.25, Lot 9): the new EntryCandidate/EntryTriggerCandidate/TradePlan
    // instrumentation parameters must not mutate their inputs, mirroring TEST 13's proof for the
    // pre-existing marketContext/assessment/decision parameters ─────────────────────────────────────

    [Fact]
    public void From_WithNewInstrumentationParams_DoesNotMutateThem()
    {
        EntryCandidate entryCandidate = BuildEntryCandidate();
        EntryTriggerCandidate entryTriggerCandidate = BuildEntryTriggerCandidate();
        TradePlan tradePlan = BuildTradePlan();
        EntryCandidate entryCandidateSnapshot = entryCandidate with { };
        EntryTriggerCandidate entryTriggerCandidateSnapshot = entryTriggerCandidate with { };
        TradePlan tradePlanSnapshot = tradePlan with { };

        ScientificDatasetRecord.From(
            Guid.NewGuid(), 0, BuildScientificMarketContext(), BuildAssessment(), BuildDecision(),
            100m, 101m, 99m, 10m,
            entryCandidate: entryCandidate, entryTriggerCandidate: entryTriggerCandidate, tradePlan: tradePlan);

        Assert.Equal(entryCandidateSnapshot, entryCandidate);
        Assert.Equal(entryTriggerCandidateSnapshot, entryTriggerCandidate);
        Assert.Equal(tradePlanSnapshot, tradePlan);
    }

    [Fact]
    public void From_WithInstrumentationParams_PopulatesTheNewCategories()
    {
        EntryCandidate entryCandidate = BuildEntryCandidate();
        EntryTriggerCandidate entryTriggerCandidate = BuildEntryTriggerCandidate();
        TradePlan tradePlan = BuildTradePlan();

        ScientificDatasetRecord record = ScientificDatasetRecord.From(
            Guid.NewGuid(), 0, BuildScientificMarketContext(), BuildAssessment(), BuildDecision(),
            100m, 101m, 99m, 10m,
            entryCandidate: entryCandidate, entryTriggerCandidate: entryTriggerCandidate, tradePlan: tradePlan);

        Assert.Equal("QUALIFIED", record.Categories["Entry.OpportunityStatus"]);
        Assert.Equal("READY", record.Categories["EntryTrigger.TriggerStatus"]);
        Assert.Equal("BUY_CANDIDATE", record.Categories["EntryTrigger.Direction"]);
        Assert.Equal("READY", record.Categories["EntryTrigger.Reason"]);
        Assert.Equal("SIGNAL_ONLY", record.Categories["TradePlan.Status"]);
        Assert.Equal("100", record.Categories["TradePlan.EntryPrice"]);
        Assert.Equal("NOT AVAILABLE", record.Categories["TradePlan.StopLoss"]);
    }

    [Fact]
    public void From_WithoutInstrumentationParams_ReportsNotAvailable()
    {
        ScientificDatasetRecord record = ScientificDatasetRecord.From(
            Guid.NewGuid(), 0, BuildScientificMarketContext(), BuildAssessment(), BuildDecision(),
            100m, 101m, 99m, 10m);

        Assert.Equal("NOT AVAILABLE", record.Categories["Entry.OpportunityStatus"]);
        Assert.Equal("NOT AVAILABLE", record.Categories["EntryTrigger.TriggerStatus"]);
        Assert.Equal("NOT AVAILABLE", record.Categories["EntryTrigger.Direction"]);
        Assert.Equal("NOT AVAILABLE", record.Categories["EntryTrigger.Reason"]);
        Assert.Equal("NOT AVAILABLE", record.Categories["TradePlan.Status"]);
        Assert.Equal("NOT AVAILABLE", record.Categories["TradePlan.EntryPrice"]);
        Assert.Equal("NOT AVAILABLE", record.Categories["TradePlan.StopLoss"]);
        Assert.Equal("NOT AVAILABLE", record.Categories["TradePlan.TakeProfit"]);
        Assert.Equal("NOT AVAILABLE", record.Categories["Risk.Status"]);
        Assert.Equal("NOT AVAILABLE", record.Categories["Risk.RejectionReasons"]);
        Assert.Equal("NOT AVAILABLE", record.Categories["Risk.PositionSize"]);
        Assert.Equal("NOT AVAILABLE", record.Categories["Risk.RiskAmount"]);
        Assert.Equal("NOT AVAILABLE", record.Categories["Risk.RiskBudget"]);
        Assert.Equal("NOT AVAILABLE", record.Categories["Risk.RiskRewardRatio"]);
    }

    // ── TEST 17 (Sprint 15.25, Lot 11): RiskAssessment instrumentation ───────────────────────────────
    // Mirrors TEST 16's proof for EntryCandidate/EntryTriggerCandidate/TradePlan: passive capture only,
    // no mutation, "NOT AVAILABLE" when absent - see ScientificDatasetRecord.From's Lot 11 comment block.

    [Fact]
    public void From_WithRiskAssessmentParam_DoesNotMutateIt()
    {
        RiskAssessment riskAssessment = BuildRiskAssessment();
        RiskAssessment riskAssessmentSnapshot = riskAssessment with { };

        ScientificDatasetRecord.From(
            Guid.NewGuid(), 0, BuildScientificMarketContext(), BuildAssessment(), BuildDecision(),
            100m, 101m, 99m, 10m,
            riskAssessment: riskAssessment);

        Assert.Equal(riskAssessmentSnapshot, riskAssessment);
    }

    [Fact]
    public void From_WithRiskAssessmentParam_PopulatesTheRiskCategories()
    {
        RiskAssessment riskAssessment = BuildRiskAssessment();

        ScientificDatasetRecord record = ScientificDatasetRecord.From(
            Guid.NewGuid(), 0, BuildScientificMarketContext(), BuildAssessment(), BuildDecision(),
            100m, 101m, 99m, 10m,
            riskAssessment: riskAssessment);

        Assert.Equal("ACCEPTED", record.Categories["Risk.Status"]);
        Assert.Equal(string.Empty, record.Categories["Risk.RejectionReasons"]);
        Assert.Equal("4", record.Categories["Risk.PositionSize"]);
        Assert.Equal("1000", record.Categories["Risk.RiskAmount"]);
        Assert.Equal("1000", record.Categories["Risk.RiskBudget"]);
        Assert.Equal("2", record.Categories["Risk.RiskRewardRatio"]);
    }

    // ── TEST 18 (Sprint 15.25, Lot 12.5): ATAS raw risk telemetry ───────────────────────────────────
    // Proves the observability contract the Lot 12.5 brief requires: a value ATAS/the adapters/the Risk
    // Engine request actually carried is exported unchanged; a value genuinely absent stays "NOT
    // AVAILABLE"; nothing here mutates its inputs or influences RiskEngine's own result; the three levels
    // (RAW_ATAS/ADAPTER/RISK_INPUT) are captured distinctly rather than merged.

    [Fact]
    public void From_WithAtasTelemetryParams_DoesNotMutateThem()
    {
        ATASAccountDiagnostic atasAccount = BuildAtasAccountDiagnostic();
        InstrumentRiskSpecification instrumentSpec = BuildInstrumentRiskSpecification();
        ATASInstrumentDiagnostic atasInstrument = BuildAtasInstrumentDiagnostic(instrumentSpec);
        AccountState accountState = BuildAccountState();
        RiskEngineRequest riskEngineRequest = BuildRiskEngineRequest(instrumentSpec, accountState);
        ATASAccountDiagnostic atasAccountSnapshot = atasAccount with { };
        ATASInstrumentDiagnostic atasInstrumentSnapshot = atasInstrument with { };
        AccountState accountStateSnapshot = accountState with { };
        InstrumentRiskSpecification instrumentSpecSnapshot = instrumentSpec with { };
        RiskEngineRequest riskEngineRequestSnapshot = riskEngineRequest with { };

        ScientificDatasetRecord.From(
            Guid.NewGuid(), 0, BuildScientificMarketContext(), BuildAssessment(), BuildDecision(),
            100m, 101m, 99m, 10m,
            atasAccountDiagnostic: atasAccount,
            atasInstrumentDiagnostic: atasInstrument,
            atasEquityIsReplay: false,
            atasEquitySeriesCount: 12,
            atasEquityLastTimestamp: new DateTime(2026, 3, 5, 12, 0, 0, DateTimeKind.Utc),
            riskAccountState: accountState,
            riskInstrumentSpec: instrumentSpec,
            riskEngineRequest: riskEngineRequest);

        Assert.Equal(atasAccountSnapshot, atasAccount);
        Assert.Equal(atasInstrumentSnapshot, atasInstrument);
        Assert.Equal(accountStateSnapshot, accountState);
        Assert.Equal(instrumentSpecSnapshot, instrumentSpec);
        Assert.Equal(riskEngineRequestSnapshot, riskEngineRequest);
    }

    [Fact]
    public void From_WithAtasTelemetryParams_PopulatesRawAdapterAndRiskInputCategories()
    {
        InstrumentRiskSpecification instrumentSpec = BuildInstrumentRiskSpecification(quantityStep: 1);
        AccountState accountState = BuildAccountState();
        ATASAccountDiagnostic atasAccount = BuildAtasAccountDiagnostic();
        ATASInstrumentDiagnostic atasInstrument = BuildAtasInstrumentDiagnostic(instrumentSpec);
        RiskEngineRequest riskEngineRequest = BuildRiskEngineRequest(instrumentSpec, accountState);

        ScientificDatasetRecord record = ScientificDatasetRecord.From(
            Guid.NewGuid(), 0, BuildScientificMarketContext(), BuildAssessment(), BuildDecision(),
            100m, 101m, 99m, 10m,
            atasAccountDiagnostic: atasAccount,
            atasInstrumentDiagnostic: atasInstrument,
            atasEquityIsReplay: false,
            atasEquitySeriesCount: 12,
            atasEquityLastTimestamp: new DateTime(2026, 3, 5, 12, 0, 0, DateTimeKind.Utc),
            riskAccountState: accountState,
            riskInstrumentSpec: instrumentSpec,
            riskEngineRequest: riskEngineRequest);

        // RAW_ATAS
        Assert.Equal("ACC-1", record.Categories["ATAS.Raw.Account.AccountID"]);
        Assert.Equal("25000", record.Categories["ATAS.Raw.Account.Balance"]);
        Assert.Equal("Realtime", record.Categories["ATAS.Raw.Equity.SourceMode"]);
        Assert.Equal("24850", record.Categories["ATAS.Raw.Equity.RealtimeValue"]);
        Assert.Equal("NOT AVAILABLE", record.Categories["ATAS.Raw.Equity.ReplayValue"]);
        Assert.Equal("12", record.Categories["ATAS.Raw.Equity.SeriesCount"]);
        Assert.Equal("True", record.Categories["ATAS.EquityAvailable"]);
        Assert.Equal("24850", record.Categories["ATAS.Equity"]);
        Assert.Equal("MES", record.Categories["ATAS.Raw.Instrument.Symbol"]);
        Assert.Equal("0.25", record.Categories["ATAS.Raw.Instrument.TickSize"]);
        // No ATAS API exposes a QuantityStep-equivalent (Lot 12.1/12.4) - permanently unavailable, a fact
        // about the API surface, never a per-bar reading.
        Assert.Equal("False", record.Categories["ATAS.QuantityStepAvailable"]);
        Assert.Equal("NOT AVAILABLE", record.Categories["ATAS.QuantityStep"]);

        // ADAPTER
        Assert.Equal("25000", record.Categories["ATAS.Adapter.Account.InitialCapital"]);
        Assert.Equal("24850", record.Categories["ATAS.Adapter.Account.CurrentEquity"]);
        Assert.Equal("MES", record.Categories["ATAS.Adapter.Instrument.Symbol"]);
        Assert.Equal("1", record.Categories["ATAS.Adapter.Instrument.QuantityStep"]);
        Assert.Equal("True", record.Categories["ATAS.Adapter.Instrument.IsValid"]);

        // RISK_INPUT
        Assert.Equal("True", record.Categories["ATAS.RiskInput.Present"]);
        Assert.Equal("Buy", record.Categories["ATAS.RiskInput.Direction"]);
        Assert.Equal("1", record.Categories["ATAS.RiskInput.Instrument.QuantityStep"]);
        Assert.Equal("True", record.Categories["ATAS.RiskInput.Instrument.IsValid"]);
        Assert.Equal("24850", record.Categories["ATAS.RiskInput.Account.CurrentEquity"]);

        // StopLoss observability sibling
        Assert.Equal("False", record.Categories["TradePlan.StopLossAvailable"]);
    }

    [Fact]
    public void From_WithoutAtasTelemetryParams_ReportsNotAvailable()
    {
        ScientificDatasetRecord record = ScientificDatasetRecord.From(
            Guid.NewGuid(), 0, BuildScientificMarketContext(), BuildAssessment(), BuildDecision(),
            100m, 101m, 99m, 10m);

        // Section 9.2/9.3 of the Lot 12.5 brief: an unavailable ATAS value must stay unavailable, and no
        // manual/fallback value may replace it - every new category below must read "NOT AVAILABLE"
        // (or its boolean-flag equivalent), never a fabricated number.
        string[] notAvailableKeys =
        {
            "ATAS.Raw.Account.AccountID", "ATAS.Raw.Account.IsRealAccount", "ATAS.Raw.Account.Currency",
            "ATAS.Raw.Account.Balance", "ATAS.Raw.Account.BalanceAvailable", "ATAS.Raw.Account.BalancePower",
            "ATAS.Raw.Account.OpenPnL", "ATAS.Raw.Account.ClosedPnL", "ATAS.Raw.Account.TotalPnL",
            "ATAS.Raw.Equity.SourceMode", "ATAS.Raw.Equity.RealtimeValue", "ATAS.Raw.Equity.ReplayValue",
            "ATAS.Raw.Equity.SeriesCount", "ATAS.Raw.Equity.LastTimestamp", "ATAS.Equity",
            "ATAS.Raw.Instrument.Symbol", "ATAS.Raw.Instrument.TickSize", "ATAS.Raw.Instrument.TickCost",
            "ATAS.Raw.Instrument.LotSize", "ATAS.Raw.Instrument.LotMinSize", "ATAS.Raw.Instrument.LotMaxSize",
            "ATAS.Raw.Instrument.Digits", "ATAS.Raw.Instrument.BaseCurrency", "ATAS.Raw.Instrument.QuoteCurrency",
            "ATAS.QuantityStep",
            "ATAS.Adapter.Account.InitialCapital", "ATAS.Adapter.Account.CurrentEquity", "ATAS.Adapter.Account.CurrentBalance",
            "ATAS.Adapter.Instrument.Symbol", "ATAS.Adapter.Instrument.TickSize", "ATAS.Adapter.Instrument.TickValue",
            "ATAS.Adapter.Instrument.PointValue", "ATAS.Adapter.Instrument.QuantityStep",
            "ATAS.Adapter.Instrument.MinQuantity", "ATAS.Adapter.Instrument.MaxQuantity", "ATAS.Adapter.Instrument.IsValid",
            "ATAS.RiskInput.Direction", "ATAS.RiskInput.EntryPrice", "ATAS.RiskInput.StopLoss", "ATAS.RiskInput.TakeProfit",
            "ATAS.RiskInput.Instrument.QuantityStep", "ATAS.RiskInput.Instrument.MinQuantity",
            "ATAS.RiskInput.Instrument.MaxQuantity", "ATAS.RiskInput.Instrument.IsValid",
            "ATAS.RiskInput.Account.CurrentEquity", "ATAS.RiskInput.Account.InitialCapital",
        };
        foreach (string key in notAvailableKeys)
            Assert.Equal("NOT AVAILABLE", record.Categories[key]);

        // Explicit availability/presence flags must read False, never True, when nothing was supplied -
        // this is the literal "ATAS.QuantityStepAvailable = false" / "ATAS.EquityAvailable = false"
        // requirement from the brief's Section 2/3.
        Assert.Equal("False", record.Categories["ATAS.EquityAvailable"]);
        Assert.Equal("False", record.Categories["ATAS.QuantityStepAvailable"]);
        Assert.Equal("False", record.Categories["ATAS.RiskInput.Present"]);
        Assert.Equal("False", record.Categories["TradePlan.StopLossAvailable"]);
    }

    [Fact]
    public void From_QuantityStep_IsPreservedEndToEnd_FromAdapterThroughRiskInput()
    {
        InstrumentRiskSpecification instrumentSpec = BuildInstrumentRiskSpecification(quantityStep: 7);
        RiskEngineRequest riskEngineRequest = BuildRiskEngineRequest(instrumentSpec, BuildAccountState());

        ScientificDatasetRecord record = ScientificDatasetRecord.From(
            Guid.NewGuid(), 0, BuildScientificMarketContext(), BuildAssessment(), BuildDecision(),
            100m, 101m, 99m, 10m,
            riskInstrumentSpec: instrumentSpec,
            riskEngineRequest: riskEngineRequest);

        Assert.Equal("7", record.Categories["ATAS.Adapter.Instrument.QuantityStep"]);
        Assert.Equal("7", record.Categories["ATAS.RiskInput.Instrument.QuantityStep"]);
    }

    [Fact]
    public void From_Equity_IsPreservedEndToEnd_FromAdapterThroughRiskInput()
    {
        AccountState accountState = BuildAccountState() with { CurrentEquity = 18342.75m };
        RiskEngineRequest riskEngineRequest = BuildRiskEngineRequest(BuildInstrumentRiskSpecification(), accountState);

        ScientificDatasetRecord record = ScientificDatasetRecord.From(
            Guid.NewGuid(), 0, BuildScientificMarketContext(), BuildAssessment(), BuildDecision(),
            100m, 101m, 99m, 10m,
            riskAccountState: accountState,
            riskEngineRequest: riskEngineRequest);

        Assert.Equal("18342.75", record.Categories["ATAS.Adapter.Account.CurrentEquity"]);
        Assert.Equal("18342.75", record.Categories["ATAS.RiskInput.Account.CurrentEquity"]);
    }

    [Fact]
    public void From_WithAtasTelemetryParams_ExistingCategoriesRemainUnchanged()
    {
        // Section 9.6 of the Lot 12.5 brief: adding the new telemetry parameters must not alter any
        // pre-existing category's value. Mirrors TEST 16/17's fixtures exactly, just with the new
        // telemetry parameters additionally supplied.
        EntryCandidate entryCandidate = BuildEntryCandidate();
        EntryTriggerCandidate entryTriggerCandidate = BuildEntryTriggerCandidate();
        TradePlan tradePlan = BuildTradePlan();
        RiskAssessment riskAssessment = BuildRiskAssessment();

        ScientificDatasetRecord withoutTelemetry = ScientificDatasetRecord.From(
            Guid.NewGuid(), 0, BuildScientificMarketContext(), BuildAssessment(), BuildDecision(),
            100m, 101m, 99m, 10m,
            entryCandidate: entryCandidate, entryTriggerCandidate: entryTriggerCandidate,
            tradePlan: tradePlan, riskAssessment: riskAssessment);

        ScientificDatasetRecord withTelemetry = ScientificDatasetRecord.From(
            Guid.NewGuid(), 0, BuildScientificMarketContext(), BuildAssessment(), BuildDecision(),
            100m, 101m, 99m, 10m,
            entryCandidate: entryCandidate, entryTriggerCandidate: entryTriggerCandidate,
            tradePlan: tradePlan, riskAssessment: riskAssessment,
            atasAccountDiagnostic: BuildAtasAccountDiagnostic(),
            atasInstrumentDiagnostic: BuildAtasInstrumentDiagnostic(BuildInstrumentRiskSpecification()),
            atasEquityIsReplay: true,
            atasEquitySeriesCount: 3,
            atasEquityLastTimestamp: DateTime.UtcNow,
            riskAccountState: BuildAccountState(),
            riskInstrumentSpec: BuildInstrumentRiskSpecification(),
            riskEngineRequest: BuildRiskEngineRequest());

        // Only PRE-EXISTING categories are asserted unchanged here - the new "ATAS.*"/
        // "TradePlan.StopLossAvailable" categories are EXPECTED to differ between the two calls (that is
        // exactly what supplying vs. omitting the new telemetry parameters means) and are already
        // covered by From_WithAtasTelemetryParams_PopulatesRawAdapterAndRiskInputCategories/
        // From_WithoutAtasTelemetryParams_ReportsNotAvailable above.
        foreach (string key in withoutTelemetry.Categories.Keys)
        {
            if (key.StartsWith("ATAS.", StringComparison.Ordinal) || key == "TradePlan.StopLossAvailable")
                continue;

            Assert.Equal(withoutTelemetry.Categories[key], withTelemetry.Categories[key]);
        }
    }

    [Fact]
    public void Instrumentation_IsPassive_RiskEngineResultUnchangedBeforeAndAfterCapture()
    {
        // Section 9.7/9's explicit before/after requirement: RiskAssessment = X before capture,
        // RiskAssessment = X after - the instrumentation must never influence RiskEngine's own decision.
        RiskEngineRequest request = BuildRiskEngineRequest();
        var engine = new RiskEngine();

        RiskAssessment before = engine.Evaluate(request);

        ScientificDatasetRecord.From(
            Guid.NewGuid(), 0, BuildScientificMarketContext(), BuildAssessment(), BuildDecision(),
            100m, 101m, 99m, 10m,
            riskAssessment: before,
            atasAccountDiagnostic: BuildAtasAccountDiagnostic(),
            atasInstrumentDiagnostic: BuildAtasInstrumentDiagnostic(BuildInstrumentRiskSpecification()),
            riskAccountState: BuildAccountState(),
            riskInstrumentSpec: BuildInstrumentRiskSpecification(),
            riskEngineRequest: request);

        RiskAssessment after = engine.Evaluate(request);

        // RiskAssessment (protected, Lot 10 - not modified here) carries two IReadOnlyList<T> members
        // (RejectionReasons/Diagnostics) that RiskEngine.Evaluate rebuilds as new List instances on every
        // call - the record's own generated Equals therefore compares those two members by reference,
        // so two independently-Evaluate()'d results are never record-Equal even with identical content.
        // That is a pre-existing, incidental property of RiskAssessment's shape, unrelated to this lot -
        // asserted here field-by-field (with SequenceEqual for the two list members) instead, which is
        // what "the decision is unchanged" actually means.
        Assert.Equal(before.Status, after.Status);
        Assert.Equal(before.Direction, after.Direction);
        Assert.Equal(before.EntryPrice, after.EntryPrice);
        Assert.Equal(before.StopLoss, after.StopLoss);
        Assert.Equal(before.TakeProfit, after.TakeProfit);
        Assert.Equal(before.RiskDistance, after.RiskDistance);
        Assert.Equal(before.RewardDistance, after.RewardDistance);
        Assert.Equal(before.RiskPerUnit, after.RiskPerUnit);
        Assert.Equal(before.RiskBudget, after.RiskBudget);
        Assert.Equal(before.PositionSize, after.PositionSize);
        Assert.Equal(before.RiskAmount, after.RiskAmount);
        Assert.Equal(before.RewardAmount, after.RewardAmount);
        Assert.Equal(before.RiskRewardRatio, after.RiskRewardRatio);
        Assert.True(before.RejectionReasons.SequenceEqual(after.RejectionReasons));
        Assert.True(before.Diagnostics.SequenceEqual(after.Diagnostics));
    }

    [Fact]
    public void From_TelemetryCaptureHasNoSymbolSpecialCasing()
    {
        // Section 9.8: no Symbol == "MES"/"ES" mapping anywhere in the telemetry path - captured
        // verbatim for an arbitrary, never-seen-before symbol exactly the same way as for "MES"/"ES".
        foreach (string symbol in new[] { "MES", "ES", "XYZ999" })
        {
            InstrumentRiskSpecification spec = BuildInstrumentRiskSpecification() with { Symbol = symbol };
            ATASInstrumentDiagnostic atasInstrument = BuildAtasInstrumentDiagnostic(spec) with { Instrument = symbol };

            ScientificDatasetRecord record = ScientificDatasetRecord.From(
                Guid.NewGuid(), 0, BuildScientificMarketContext(), BuildAssessment(), BuildDecision(),
                100m, 101m, 99m, 10m,
                atasInstrumentDiagnostic: atasInstrument,
                riskInstrumentSpec: spec);

            Assert.Equal(symbol, record.Categories["ATAS.Raw.Instrument.Symbol"]);
            Assert.Equal(symbol, record.Categories["ATAS.Adapter.Instrument.Symbol"]);
        }
    }

    // ── TEST 19 (Sprint 15.25, Lot 12.6): ATAS binding correction telemetry ─────────────────────────
    // Proves the 4 new parameters (atasEquityHeuristicIsReplay/atasPortfolioIsReplay/minQuantitySource/
    // maxQuantitySource) are captured passively, correctly, and "NOT AVAILABLE" when absent - same
    // discipline as every Lot 9/11/12.5 instrumentation parameter. No quantityStepSource parameter -
    // Lot 12.6 investigated and rejected using Security.LotSize for QuantityStep (see
    // ATASInstrumentQuantityStepResolver.cs); QuantityStep stays exclusively manual, already fully
    // represented by the existing ATAS.Adapter.Instrument.QuantityStep category (Lot 12.5, unchanged).

    [Fact]
    public void From_WithLot126TelemetryParams_PopulatesNewCategories()
    {
        ScientificDatasetRecord record = ScientificDatasetRecord.From(
            Guid.NewGuid(), 0, BuildScientificMarketContext(), BuildAssessment(), BuildDecision(),
            100m, 101m, 99m, 10m,
            atasEquityIsReplay: true, // the CORRECTED signal actually used
            atasEquityHeuristicIsReplay: false, // the OLD heuristic - deliberately disagreeing, as in the real capture
            atasPortfolioIsReplay: true,
            minQuantitySource: "Manual",
            maxQuantitySource: "Manual");

        Assert.Equal("Replay", record.Categories["ATAS.Raw.Equity.SourceMode"]); // reflects the corrected signal
        Assert.Equal("False", record.Categories["ATAS.Adapter.Equity.HeuristicIsReplay"]); // the old heuristic, for comparison
        Assert.Equal("True", record.Categories["ATAS.Raw.Account.PortfolioIsReplay"]);
        Assert.Equal("Manual", record.Categories["ATAS.Adapter.Instrument.MinQuantitySource"]);
        Assert.Equal("Manual", record.Categories["ATAS.Adapter.Instrument.MaxQuantitySource"]);
    }

    [Fact]
    public void From_WithoutLot126TelemetryParams_ReportsNotAvailable()
    {
        ScientificDatasetRecord record = ScientificDatasetRecord.From(
            Guid.NewGuid(), 0, BuildScientificMarketContext(), BuildAssessment(), BuildDecision(),
            100m, 101m, 99m, 10m);

        Assert.Equal("NOT AVAILABLE", record.Categories["ATAS.Adapter.Equity.HeuristicIsReplay"]);
        Assert.Equal("NOT AVAILABLE", record.Categories["ATAS.Raw.Account.PortfolioIsReplay"]);
        Assert.Equal("NOT AVAILABLE", record.Categories["ATAS.Adapter.Instrument.MinQuantitySource"]);
        Assert.Equal("NOT AVAILABLE", record.Categories["ATAS.Adapter.Instrument.MaxQuantitySource"]);
    }

    [Fact]
    public void From_WithLot126TelemetryParams_ExistingLot125CategoriesUnaffected()
    {
        // Adding the 4 new Lot 12.6 parameters must not change any Lot 12.5 category's value for a bar
        // where they are irrelevant (e.g. Instrument.Symbol, unrelated to Equity source selection).
        InstrumentRiskSpecification spec = BuildInstrumentRiskSpecification();

        ScientificDatasetRecord without = ScientificDatasetRecord.From(
            Guid.NewGuid(), 0, BuildScientificMarketContext(), BuildAssessment(), BuildDecision(),
            100m, 101m, 99m, 10m, riskInstrumentSpec: spec);

        ScientificDatasetRecord with = ScientificDatasetRecord.From(
            Guid.NewGuid(), 0, BuildScientificMarketContext(), BuildAssessment(), BuildDecision(),
            100m, 101m, 99m, 10m, riskInstrumentSpec: spec,
            atasEquityHeuristicIsReplay: true, atasPortfolioIsReplay: false,
            minQuantitySource: "Manual", maxQuantitySource: "Manual");

        Assert.Equal(without.Categories["ATAS.Adapter.Instrument.Symbol"], with.Categories["ATAS.Adapter.Instrument.Symbol"]);
        Assert.Equal(without.Categories["ATAS.Adapter.Instrument.QuantityStep"], with.Categories["ATAS.Adapter.Instrument.QuantityStep"]);
    }

    // ── Fixtures (Lot 12.5) ──────────────────────────────────────────────────────────────────────

    private static ATASAccountDiagnostic BuildAtasAccountDiagnostic() =>
        new(
            AccountID: "ACC-1",
            IsRealAccount: false,
            Currency: "USD",
            Balance: 25000m,
            BalanceAvailable: 24000m,
            BalancePower: 48000m,
            OpenPnL: -150m,
            ClosedPnL: 300m,
            TotalPnL: 150m,
            PositionVolume: 2m,
            PositionUnrealizedPnL: -150m,
            PositionRealizedPnL: 300m,
            RealtimeEquity: 24850m,
            ReplayEquity: null,
            FinalEquityUsed: 24850m);

    private static ATASInstrumentDiagnostic BuildAtasInstrumentDiagnostic(InstrumentRiskSpecification finalSpec) =>
        new(
            Instrument: "MES",
            TickSize: 0.25m,
            TickCost: 1.25m,
            LotSize: 1m,
            LotMinSize: 1m,
            LotMaxSize: 50m,
            Digits: 2,
            BaseCurrency: "USD",
            QuoteCurrency: "USD",
            FinalSpecification: finalSpec,
            InvalidFieldReasons: Array.Empty<string>());

    private static AccountState BuildAccountState() =>
        new(
            InitialCapital: 25000m,
            CurrentEquity: 24850m,
            CurrentBalance: 25000m,
            PeakEquity: 25000m,
            DailyStartingEquity: 25000m,
            DailyPnL: -150m,
            RiskUsedToday: 0m,
            OpenRisk: 0m);

    private static InstrumentRiskSpecification BuildInstrumentRiskSpecification(int quantityStep = 1) =>
        new(
            Symbol: "MES",
            TickSize: 0.25m,
            TickValue: 1.25m,
            PointValue: 5m,
            MinQuantity: 1,
            MaxQuantity: 50,
            QuantityStep: quantityStep);

    private static RiskEngineRequest BuildRiskEngineRequest(InstrumentRiskSpecification? spec = null, AccountState? account = null) =>
        new(
            Direction: TradeDirection.Buy,
            EntryPrice: 7554.50m,
            StopLoss: 7553m,
            TakeProfit: 7556m,
            Instrument: spec ?? BuildInstrumentRiskSpecification(),
            Account: account ?? BuildAccountState(),
            Policy: new RiskPolicy(0.02m, null, null, null, null, null, null, null, null, null));

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

    // Sprint 15.25 (Lot 9): fixtures for the new TEST 16 instrumentation-passivity tests only - not
    // used by TEST 1-15 above.

    private static EntryCandidate BuildEntryCandidate()
    {
        var entryAssessment = new EntryAssessment(
            BuildAssessment(),
            AssessmentQuality: 0.8,
            EntryReadiness: EntryReadiness.READY_FOR_NEXT_STAGE,
            OpportunityStatus: OpportunityStatus.QUALIFIED,
            OpportunityPriority: 0.8,
            OpportunityReasons: Array.Empty<string>(),
            BlockingIssues: Array.Empty<string>(),
            SupportingEvidence: Array.Empty<string>(),
            Warnings: Array.Empty<string>(),
            Diagnostics: Array.Empty<string>());

        return new EntryCandidate(
            entryAssessment,
            DateTime.UtcNow,
            OpportunityStatus.QUALIFIED,
            OpportunityPriority: 0.8,
            OpportunityReasons: Array.Empty<string>(),
            Warnings: Array.Empty<string>(),
            Diagnostics: Array.Empty<string>());
    }

    private static EntryTriggerCandidate BuildEntryTriggerCandidate()
    {
        var assessment = new EntryTriggerAssessment(
            TriggerStatus: EntryTriggerStatus.READY,
            Direction: DirectionCandidate.BUY_CANDIDATE,
            ScientificConfidence: 0.8,
            OpportunityPriority: 0.8,
            Reason: EntryTriggerReason.READY,
            EstimatedEquilibrium: 110.0,
            DistanceToEquilibrium: null,
            Timestamp: DateTime.UtcNow);

        return new EntryTriggerCandidate(
            assessment,
            BuildEntryCandidate(),
            CurrentPrice: 100m,
            Warnings: Array.Empty<string>(),
            Diagnostics: Array.Empty<string>(),
            DateTime.UtcNow);
    }

    private static TradePlan BuildTradePlan() =>
        new TradePlanEngine().Process(new TradePlanContext(
            BuildEntryTriggerCandidate(),
            new InstrumentInfo("TEST", TickSize: 0.25m, TickValue: 12.5m, PointValue: 50m, Decimals: 2)));

    // Sprint 15.25 (Lot 11): a plain RiskAssessment record literal, not routed through RiskEngine itself -
    // RiskEngine's own correctness is already fully covered by Lot 10's RiskEngineTests.cs and Lot 11's
    // RiskEngineIntegrationTests.cs. This fixture exists only to prove ScientificDatasetRecord.From
    // captures a RiskAssessment's fields correctly, independent of how that assessment was produced.
    private static RiskAssessment BuildRiskAssessment() =>
        new(
            Status: RiskDecisionStatus.ACCEPTED,
            RejectionReasons: Array.Empty<RiskRejectionReason>(),
            Direction: TradeDirection.Buy,
            EntryPrice: 100m,
            StopLoss: 95m,
            TakeProfit: 110m,
            RiskDistance: 5m,
            RewardDistance: 10m,
            RiskPerUnit: 250m,
            RiskBudget: 1000m,
            PositionSize: 4,
            RiskAmount: 1000m,
            RewardAmount: 2000m,
            RiskRewardRatio: 2.0,
            Diagnostics: Array.Empty<string>());
}
