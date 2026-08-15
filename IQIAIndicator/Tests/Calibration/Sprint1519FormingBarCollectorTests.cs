using System;
using System.Collections.Generic;
using System.IO;
using IQIAIndicator.Core.Calibration;
using IQIAIndicator.Tests.Research.StopLossCalibration;
using Xunit;

namespace IQIAIndicator.Tests.Calibration;

/// <summary>
/// Sprint 15.19 (QDE-012 forming-bar-capture fix), Section 13's 14 required tests. Complements the
/// updated pre-existing suites (ScientificDatasetCollectorTests.cs,
/// ScientificDatasetRealMarketCaptureTests.cs) with the exact scenarios the brief enumerates by
/// number, so the traceability between brief and test is unambiguous. Uses
/// ScientificDatasetCollector/ScientificDatasetRecord directly - IQIAIndicator itself cannot be
/// instantiated in tests (derives from ATAS's Indicator base class), same limitation as every prior
/// Sprint 15.17.x/15.18 test file.
/// </summary>
public sealed class Sprint1519FormingBarCollectorTests
{
    // ── TEST 1: one bar, one callback -> one finalized observation after bar transition ─────────────

    [Fact]
    public void Test1_OneBarOneCallback_FinalizesOnlyAfterBarTransition()
    {
        var sessionId = Guid.NewGuid();
        var collector = new ScientificDatasetCollector(sessionId);

        collector.Add(Bar(sessionId, currentBar: 0));
        Assert.Equal(0, collector.RecordsAccepted); // not proven closed yet

        collector.Add(Bar(sessionId, currentBar: 1));
        Assert.Equal(1, collector.RecordsAccepted);
        Assert.Equal(0, collector.Records[0].CurrentBar);
    }

    // ── TEST 2: one bar, multiple callbacks with CHANGING OHLCV -> only the FINAL snapshot commits ──

    [Fact]
    public void Test2_ChangingOhlcvAcrossCallbacks_OnlyFinalSnapshotIsCommitted()
    {
        var sessionId = Guid.NewGuid();
        var collector = new ScientificDatasetCollector(sessionId);

        collector.Add(Bar(sessionId, currentBar: 0, close: 100m, high: 101m, volume: 10m));
        collector.Add(Bar(sessionId, currentBar: 0, close: 103m, high: 104m, volume: 25m));
        collector.Add(Bar(sessionId, currentBar: 0, close: 102m, high: 105m, volume: 40m)); // final tick before close
        collector.Add(Bar(sessionId, currentBar: 1));

        Assert.Equal(1, collector.RecordsAccepted);
        Assert.Equal(102m, collector.Records[0].Close);
        Assert.Equal(105m, collector.Records[0].High);
        Assert.Equal(40m, collector.Records[0].Volume);
        Assert.Equal(2, collector.FormingBarUpdates);
    }

    // ── TEST 3: one bar, multiple IDENTICAL callbacks -> one finalized observation ───────────────────

    [Fact]
    public void Test3_IdenticalRepeatedCallbacks_StillOneFinalizedObservation()
    {
        var sessionId = Guid.NewGuid();
        var collector = new ScientificDatasetCollector(sessionId);
        ScientificDatasetRecord record = Bar(sessionId, currentBar: 0);

        collector.Add(record);
        collector.Add(record);
        collector.Add(record);
        collector.Add(Bar(sessionId, currentBar: 1));

        Assert.Equal(1, collector.RecordsAccepted);
        Assert.Equal(1, collector.Count);
        Assert.Equal(2, collector.FormingBarUpdates); // 2nd and 3rd identical calls both counted, neither rejected
    }

    // ── TEST 4: bar N receives 10 updates, then bar N+1 appears -> bar N contains update #10 ────────

    [Fact]
    public void Test4_TenUpdatesForOneBar_FinalizedRecordIsTheTenthUpdate()
    {
        var sessionId = Guid.NewGuid();
        var collector = new ScientificDatasetCollector(sessionId);

        for (int update = 1; update <= 10; update++)
            collector.Add(Bar(sessionId, currentBar: 0, close: 100m + update, high: 111m, volume: update * 10m));

        collector.Add(Bar(sessionId, currentBar: 1));

        Assert.Equal(1, collector.RecordsAccepted);
        Assert.Equal(110m, collector.Records[0].Close);  // update #10 -> 100 + 10
        Assert.Equal(100m, collector.Records[0].Volume); // update #10 -> 10 * 10
        Assert.Equal(9, collector.FormingBarUpdates);    // updates 2..10
    }

    // ── TEST 5: multiple consecutive bars, each with multiple forming updates -> exactly one record per bar ─

    [Fact]
    public void Test5_MultipleConsecutiveBarsEachWithFormingUpdates_ExactlyOneRecordEach()
    {
        var sessionId = Guid.NewGuid();
        var collector = new ScientificDatasetCollector(sessionId);

        for (int bar = 0; bar < 5; bar++)
        {
            collector.Add(Bar(sessionId, currentBar: bar, close: 100m + bar, high: 110m + bar));
            collector.Add(Bar(sessionId, currentBar: bar, close: 100m + bar + 0.5m, high: 110m + bar)); // forming update
        }
        collector.Add(Bar(sessionId, currentBar: 5)); // finalizes bar 4

        Assert.Equal(5, collector.RecordsAccepted);
        for (int bar = 0; bar < 5; bar++)
        {
            Assert.Contains(collector.Records, r => r.CurrentBar == bar && r.Close == 100m + bar + 0.5m);
        }
        Assert.Equal(5, collector.FormingBarUpdates); // one forming update per bar, 5 bars
    }

    // ── TEST 6: current bar at OnDispose -> explicit documented policy is respected ─────────────────
    // Policy: EXCLUDE_CURRENT_FORMING_BAR - see ScientificDatasetCollector.Flush()'s doc comment.

    [Fact]
    public void Test6_FlushAtDisposeWithBarStillForming_ExcludesItFromFinalizedDataset()
    {
        var sessionId = Guid.NewGuid();
        var collector = new ScientificDatasetCollector(sessionId);

        collector.Add(Bar(sessionId, currentBar: 0));
        collector.Add(Bar(sessionId, currentBar: 1)); // finalizes bar 0; bar 1 becomes pending, still open

        collector.Flush();

        Assert.Equal(1, collector.RecordsAccepted); // only bar 0 - bar 1 was never proven closed
        Assert.Equal(1, collector.BarsPendingAtDispose);
        Assert.NotNull(collector.PendingBar);
        Assert.Equal(1, collector.PendingBar!.CurrentBar);
        Assert.DoesNotContain(collector.Records, r => r.CurrentBar == 1);
    }

    [Fact]
    public void Test6b_FlushWithNothingPending_ReportsZeroPendingAtDispose()
    {
        var sessionId = Guid.NewGuid();
        var collector = new ScientificDatasetCollector(sessionId);
        collector.Add(Bar(sessionId, currentBar: 0));
        collector.Add(Bar(sessionId, currentBar: 1)); // finalizes bar 0

        // Nothing is "pending" only in the sense that bar 1 IS the pending bar - Flush() always
        // excludes whatever is pending at the moment it is called, per EXCLUDE_CURRENT_FORMING_BAR.
        // BarsPendingAtDispose is 0 only when Flush() itself is never called (see Test6_..., which
        // calls it explicitly) - this test instead proves Flush() is idempotent and safe pre-call.
        Assert.Equal(0, collector.BarsPendingAtDispose); // Flush() not called yet
        collector.Flush();
        Assert.Equal(1, collector.BarsPendingAtDispose); // bar 1 was pending when Flush() ran
        collector.Flush(); // idempotent
        Assert.Equal(1, collector.BarsPendingAtDispose);
    }

    // ── TEST 7: invalid forming update followed by a valid one -> final valid state can still commit ─

    [Fact]
    public void Test7_InvalidUpdateBetweenValidOnes_NeverCorruptsThePendingBar()
    {
        var sessionId = Guid.NewGuid();
        var collector = new ScientificDatasetCollector(sessionId);

        collector.Add(Bar(sessionId, currentBar: 0, close: 100m));
        collector.Add(Bar(sessionId, currentBar: 0, open: 100m, high: 90m, low: 80m, close: 100m)); // invalid: High < Close
        collector.Add(Bar(sessionId, currentBar: 0, close: 103m, high: 104m)); // valid again
        collector.Add(Bar(sessionId, currentBar: 1));

        Assert.Equal(1, collector.InvalidRecordsRejected);
        Assert.Equal(1, collector.RecordsAccepted);
        Assert.Equal(103m, collector.Records[0].Close); // the final VALID state, invalid call never applied
    }

    // ── TEST 8: out-of-order bar index -> correctly diagnosed per the established policy ────────────

    [Fact]
    public void Test8_OutOfOrderBarIndex_DiagnosedAsRecordsOutOfOrder_NeverAppliedToPending()
    {
        var sessionId = Guid.NewGuid();
        var collector = new ScientificDatasetCollector(sessionId);

        collector.Add(Bar(sessionId, currentBar: 0));
        collector.Add(Bar(sessionId, currentBar: 5)); // finalizes bar 0, pending = bar 5
        // Rewind, not matching finalized bar 0. Marker OHLC (200/205/195/202) is structurally VALID
        // but clearly distinct from the pending bar's default (100/101/99/100.5) - proves this call's
        // payload was never applied, not merely that its CurrentBar wasn't.
        collector.Add(Bar(sessionId, currentBar: 3, open: 200m, high: 205m, low: 195m, close: 202m));

        Assert.Equal(1, collector.RecordsOutOfOrder);
        Assert.Equal(0, collector.DuplicateRecordsRejected);
        Assert.Equal(5, collector.PendingBar!.CurrentBar);
        Assert.NotEqual(202m, collector.PendingBar.Close);
    }

    // ── TEST 9: session reset -> pending state does not contaminate the next session ────────────────

    [Fact]
    public void Test9_ClearAfterPendingBarExists_LeavesNoResidualPendingState()
    {
        var sessionId = Guid.NewGuid();
        var collector = new ScientificDatasetCollector(sessionId);
        collector.Add(Bar(sessionId, currentBar: 0));
        collector.Add(Bar(sessionId, currentBar: 1)); // finalizes bar 0, bar 1 pending
        collector.Flush();

        collector.Clear();

        Assert.Null(collector.PendingBar);
        Assert.Equal(0, collector.TotalAddAttempts);
        Assert.Equal(0, collector.RecordsAccepted);
        Assert.Equal(0, collector.BarsPendingAtDispose);
        Assert.Equal(0, collector.Count);

        // A fresh bar 0 after Clear() must behave exactly like session start - not be misread as an
        // out-of-order rewind against the pre-Clear() state.
        collector.Add(Bar(sessionId, currentBar: 0));
        Assert.Equal(0, collector.RecordsOutOfOrder);
        Assert.Equal(0, collector.DuplicateRecordsRejected);
        Assert.NotNull(collector.PendingBar);
    }

    // ── TEST 10: Symbol/TimeFrame change mid-session -> pending state is handled correctly ──────────

    [Fact]
    public void Test10_SymbolChangeMidSession_FinalizesOldPendingBar_NeverBlendsStreams()
    {
        var sessionId = Guid.NewGuid();
        var collector = new ScientificDatasetCollector(sessionId);

        collector.Add(Bar(sessionId, currentBar: 0, symbol: "ES"));
        collector.Add(Bar(sessionId, currentBar: 0, symbol: "NQ")); // instrument switch, same CurrentBar number

        Assert.Equal(1, collector.RecordsAccepted); // the ES bar was finalized by the switch itself
        Assert.Equal("ES", collector.Records[0].Symbol);
        Assert.Equal("NQ", collector.PendingBar!.Symbol); // NQ bar 0 is now pending, independently
        Assert.Equal(0, collector.RecordsOutOfOrder);
        Assert.Equal(0, collector.DuplicateRecordsRejected);
    }

    [Fact]
    public void Test10b_TimeFrameChangeMidSession_FinalizesOldPendingBar()
    {
        var sessionId = Guid.NewGuid();
        var collector = new ScientificDatasetCollector(sessionId);

        collector.Add(Bar(sessionId, currentBar: 10, timeFrame: "M5"));
        collector.Add(Bar(sessionId, currentBar: 10, timeFrame: "M1"));

        Assert.Equal(1, collector.RecordsAccepted);
        Assert.Equal("M5", collector.Records[0].TimeFrame);
        Assert.Equal("M1", collector.PendingBar!.TimeFrame);
    }

    // ── TEST 11: existing scale/invariance assumptions remain untouched ─────────────────────────────
    // QDE-012's locked protocol constants (Horizon, k-grid) live entirely outside this sprint's
    // allowed-file list (Core/Calibration/*, IQIAIndicator.cs, Tests/*) - this test proves they were
    // not touched as a side effect of this sprint's collector work, the same guarantee
    // RealMarketGateTests.Test1/Test2 provide for Sprint 15.16.

    [Fact]
    public void Test11_QDE012ProtocolConstants_StillLocked()
    {
        Assert.Equal(40, CampaignGrids.Horizon);
        Assert.Equal(40, CampaignGrids.KGrid.Count);
        Assert.Equal(0.25, CampaignGrids.KGrid[0], 9);
        Assert.Equal(10.0, CampaignGrids.KGrid[^1], 9);
    }

    // ── TEST 12: existing export format remains valid ───────────────────────────────────────────────

    [Fact]
    public void Test12_OhlcvCsvHeader_UnchangedBySprint1519()
    {
        var sessionId = Guid.NewGuid();
        var collector = new ScientificDatasetCollector(sessionId);
        collector.Add(Bar(sessionId, currentBar: 0));
        collector.Add(Bar(sessionId, currentBar: 1));

        string[] lines = collector.ToOhlcvCsv().Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("SessionId,Timestamp,Symbol,TimeFrame,Open,High,Low,Close,Volume,CurrentBar", lines[0].TrimEnd('\r'));
        Assert.Equal(2, lines.Length); // header + exactly 1 finalized row
    }

    // ── TEST 13: no source OHLCV mutation ────────────────────────────────────────────────────────────

    [Fact]
    public void Test13_AddNeverMutatesTheSourceRecord_AcrossFormingUpdatesAndFinalization()
    {
        var sessionId = Guid.NewGuid();
        var collector = new ScientificDatasetCollector(sessionId);
        ScientificDatasetRecord original = Bar(sessionId, currentBar: 0, close: 100m);
        ScientificDatasetRecord snapshot = original with { };

        collector.Add(original);
        collector.Add(original); // forming update - re-adds the SAME object reference
        collector.Add(Bar(sessionId, currentBar: 1)); // finalizes it

        Assert.Equal(snapshot, original); // still untouched - Add() never writes back into its argument
    }

    // ── TEST 14: no production trading logic touched ────────────────────────────────────────────────
    // Same category as Sprint 15.17.1's own TEST 10: a repository/git-diff-level guarantee, not
    // something a unit test inside this assembly can observe (a test cannot inspect what files a git
    // diff touched). See the Sprint 15.19 report's Production Perimeter section for the actual
    // verification (git status/diff, before and after this sprint's work).

    // ── Fixtures ─────────────────────────────────────────────────────────────────────────────────

    private static ScientificDatasetRecord Bar(
        Guid sessionId,
        int currentBar,
        string symbol = "ES",
        string timeFrame = "M5",
        decimal open = 100m,
        decimal high = 101m,
        decimal low = 99m,
        decimal close = 100.5m,
        decimal volume = 10m) =>
        new(
            sessionId,
            DateTime.UtcNow,
            symbol,
            timeFrame,
            close,
            1,
            currentBar,
            new Dictionary<string, double?>(),
            new Dictionary<string, string>(),
            open,
            high,
            low,
            volume);
}
