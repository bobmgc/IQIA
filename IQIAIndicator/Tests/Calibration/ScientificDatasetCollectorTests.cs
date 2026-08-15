using System;
using System.Collections.Generic;
using System.IO;
using IQIAIndicator.Core.Calibration;
using Xunit;

namespace IQIAIndicator.Tests.Calibration;

/// <summary>
/// Sprint 15.19 (QDE-012 forming-bar-capture fix) note: several tests below were rewritten from their
/// pre-15.19 form. The originals asserted that a SINGLE Add() call (or two calls for the SAME bar)
/// immediately became part of the finalized dataset - i.e. they encoded the exact "first Add() wins"
/// premise Sprint 15.18 proved unsafe against real ATAS data (a real capture's committed bars were, in
/// a contiguous 633-bar tail, single-tick snapshots of still-forming candles - see
/// QDE-012_Sprint_15.18_ATAS_Real_Data_Quality_Report.md §6). Under the new "last update before
/// advance wins" contract (ScientificDatasetCollector.cs class doc comment), a bar only becomes part of
/// Records/RecordsAccepted once a callback for a LATER bar (or Flush() explicitly excluding it) proves
/// it is closed - so a lone Add() call, or repeated calls for the same still-open bar, correctly leave
/// Count/RecordsAccepted at 0 until that proof arrives. This is not a relaxation of the tests; it is
/// the whole point of Sprint 15.19.
/// </summary>
public sealed class ScientificDatasetCollectorTests
{
    [Fact]
    public void Add_UniqueRecord_StaysPendingUntilBarAdvances()
    {
        var sessionId = Guid.NewGuid();
        var collector = new ScientificDatasetCollector(sessionId);
        var record = CreateRecord(sessionId, currentBar: 0);

        collector.Add(record);

        // Sprint 15.19: one callback for bar 0 is not proof bar 0 has closed - it only becomes the
        // pending observation. Nothing is finalized (Count/RecordsAccepted) until a later bar arrives.
        Assert.Equal(0, collector.Count);
        Assert.Equal(0, collector.RecordsAccepted);
        Assert.Equal(0, collector.DuplicateRecordsRejected);
        Assert.Equal(1, collector.TotalAddAttempts);
        Assert.Equal(0, collector.Errors);
        Assert.NotNull(collector.PendingBar);
        Assert.Equal(0, collector.PendingBar!.CurrentBar);

        collector.Add(CreateRecord(sessionId, currentBar: 1));

        Assert.Equal(1, collector.Count);
        Assert.Equal(1, collector.RecordsAccepted);
        Assert.Equal(sessionId, collector.Records[0].SessionId);
        Assert.Equal(0, collector.Records[0].CurrentBar);
    }

    [Fact]
    public void Add_SameRecordTwice_IsFormingBarUpdate_NotDuplicateRejection()
    {
        // Sprint 15.19: pre-15.19 this asserted "second identical callback for the same bar is
        // rejected as a duplicate" - exactly the misclassification Sprint 15.18's brief calls out
        // ("do not misuse duplicate to hide legitimate forming-bar updates"). Two callbacks for a
        // still-open bar are both legitimate observations of that one forming bar.
        var sessionId = Guid.NewGuid();
        var collector = new ScientificDatasetCollector(sessionId);
        var record = CreateRecord(sessionId, currentBar: 5);

        collector.Add(record);
        collector.Add(record);

        Assert.Equal(0, collector.Count);
        Assert.Equal(0, collector.RecordsAccepted);
        Assert.Equal(1, collector.FormingBarUpdates);
        Assert.Equal(0, collector.DuplicateRecordsRejected);
        Assert.Equal(2, collector.TotalAddAttempts);
        Assert.Equal(0, collector.Errors);
    }

    [Fact]
    public void Add_TrueDuplicate_ReDeliveryOfAlreadyFinalizedBar_IsRejected()
    {
        // The scientifically correct meaning of "duplicate" under Sprint 15.19: a callback for a bar
        // that has ALREADY been finalized (proven closed by a later bar), delivered again.
        var sessionId = Guid.NewGuid();
        var collector = new ScientificDatasetCollector(sessionId);
        var bar5 = CreateRecord(sessionId, currentBar: 5);

        collector.Add(bar5);
        collector.Add(CreateRecord(sessionId, currentBar: 6)); // finalizes bar 5
        collector.Add(bar5); // re-delivery of the now-finalized bar 5

        Assert.Equal(1, collector.Count);
        Assert.Equal(1, collector.RecordsAccepted);
        Assert.Equal(1, collector.DuplicateRecordsRejected);
        Assert.Equal(3, collector.TotalAddAttempts);
        Assert.Equal("Duplicate observation rejected: bar already finalized.", collector.RejectedReason);
    }

    [Fact]
    public void Add_DifferentBarRecords_EachFinalizesWhenSucceededByTheNextBar()
    {
        var sessionId = Guid.NewGuid();
        var collector = new ScientificDatasetCollector(sessionId);
        var first = CreateRecord(sessionId, currentBar: 2);
        var second = CreateRecord(sessionId, currentBar: 3);
        var third = CreateRecord(sessionId, currentBar: 4);

        collector.Add(first);
        collector.Add(second); // finalizes bar 2
        collector.Add(third);  // finalizes bar 3

        Assert.Equal(2, collector.Count);
        Assert.Equal(2, collector.RecordsAccepted);
        Assert.Equal(0, collector.DuplicateRecordsRejected);
        Assert.Equal(3, collector.TotalAddAttempts);
        Assert.Equal(0, collector.Errors);
        Assert.Contains(collector.Records, record => record.CurrentBar == 2);
        Assert.Contains(collector.Records, record => record.CurrentBar == 3);
        Assert.Equal(4, collector.PendingBar!.CurrentBar); // bar 4 still open, correctly not finalized
    }

    [Fact]
    public void Add_SameBarDifferentSession_IsIndependentPerCollector()
    {
        var firstSession = Guid.NewGuid();
        var secondSession = Guid.NewGuid();
        var firstCollector = new ScientificDatasetCollector(firstSession);
        var secondCollector = new ScientificDatasetCollector(secondSession);
        var firstRecord = CreateRecord(firstSession, currentBar: 10);
        var secondRecord = CreateRecord(secondSession, currentBar: 10);

        firstCollector.Add(firstRecord);
        firstCollector.Add(CreateRecord(firstSession, currentBar: 11)); // finalizes bar 10
        secondCollector.Add(secondRecord);
        secondCollector.Add(CreateRecord(secondSession, currentBar: 11)); // finalizes bar 10

        Assert.Equal(1, firstCollector.Count);
        Assert.Equal(1, secondCollector.Count);
        Assert.Equal(firstSession, firstCollector.Records[0].SessionId);
        Assert.Equal(secondSession, secondCollector.Records[0].SessionId);
    }

    [Fact]
    public void ExportCsvAndJson_AfterFormingBarUpdate_WritesSingleFinalizedRecord()
    {
        // Sprint 15.19: pre-15.19 this exported after two identical Add() calls with no advancing
        // bar - under the new contract that bar is still pending, so a third call (advancing to the
        // next bar) is required before it is finalized and appears in the export.
        var sessionId = Guid.NewGuid();
        var collector = new ScientificDatasetCollector(sessionId);
        var record = CreateRecord(sessionId, currentBar: 7);

        collector.Add(record);
        collector.Add(record); // forming-bar update, not a duplicate
        collector.Add(CreateRecord(sessionId, currentBar: 8)); // finalizes bar 7

        using var tempDir = new TemporaryDirectory();
        Directory.CreateDirectory(tempDir.Path);
        string csvPath = Path.Combine(tempDir.Path, "dataset.csv");
        string jsonPath = Path.Combine(tempDir.Path, "dataset.json");

        collector.ExportCsv(csvPath);
        collector.ExportJson(jsonPath);

        string csvContent = File.ReadAllText(csvPath);
        string jsonContent = File.ReadAllText(jsonPath);

        Assert.Contains("SessionId", csvContent);
        Assert.Equal(2, csvContent.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries).Length); // header + exactly 1 finalized row
        Assert.Contains("\"SessionId\"", jsonContent);
        Assert.Contains(sessionId.ToString("D"), jsonContent);
        Assert.Contains("\"CurrentBar\": 7", jsonContent);
        Assert.DoesNotContain("\"CurrentBar\": 8", jsonContent); // bar 8 still pending, correctly excluded
    }

    private static ScientificDatasetRecord CreateRecord(Guid sessionId, int currentBar)
    {
        return new ScientificDatasetRecord(
            sessionId,
            DateTime.UtcNow,
            "TEST",
            "1m",
            100m,
            1,
            currentBar,
            new Dictionary<string, double?>(),
            new Dictionary<string, string>());
    }
}
