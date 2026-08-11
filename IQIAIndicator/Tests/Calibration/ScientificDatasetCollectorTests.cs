using System;
using System.Collections.Generic;
using System.IO;
using IQIAIndicator.Core.Calibration;
using Xunit;

namespace IQIAIndicator.Tests.Calibration;

public sealed class ScientificDatasetCollectorTests
{
    [Fact]
    public void Add_UniqueRecord_IsAccepted()
    {
        var sessionId = Guid.NewGuid();
        var collector = new ScientificDatasetCollector(sessionId);
        var record = CreateRecord(sessionId, currentBar: 0);

        collector.Add(record);

        Assert.Equal(1, collector.Count);
        Assert.Equal(1, collector.RecordsAccepted);
        Assert.Equal(0, collector.DuplicateRecordsRejected);
        Assert.Equal(1, collector.TotalAddAttempts);
        Assert.Equal(0, collector.Errors);
        Assert.Equal(sessionId, collector.Records[0].SessionId);
        Assert.Equal(0, collector.Records[0].CurrentBar);
    }

    [Fact]
    public void Add_SameRecordTwice_IsRejectedSecondTime()
    {
        var sessionId = Guid.NewGuid();
        var collector = new ScientificDatasetCollector(sessionId);
        var record = CreateRecord(sessionId, currentBar: 5);

        collector.Add(record);
        collector.Add(record);

        Assert.Equal(1, collector.Count);
        Assert.Equal(1, collector.RecordsAccepted);
        Assert.Equal(1, collector.DuplicateRecordsRejected);
        Assert.Equal(2, collector.TotalAddAttempts);
        Assert.Equal(1, collector.Errors);
        Assert.Equal("Duplicate observation rejected.", collector.RejectedReason);
    }

    [Fact]
    public void Add_DifferentBarRecords_BothAccepted()
    {
        var sessionId = Guid.NewGuid();
        var collector = new ScientificDatasetCollector(sessionId);
        var first = CreateRecord(sessionId, currentBar: 2);
        var second = CreateRecord(sessionId, currentBar: 3);

        collector.Add(first);
        collector.Add(second);

        Assert.Equal(2, collector.Count);
        Assert.Equal(2, collector.RecordsAccepted);
        Assert.Equal(0, collector.DuplicateRecordsRejected);
        Assert.Equal(2, collector.TotalAddAttempts);
        Assert.Equal(0, collector.Errors);
        Assert.Contains(collector.Records, record => record.CurrentBar == 2);
        Assert.Contains(collector.Records, record => record.CurrentBar == 3);
    }

    [Fact]
    public void Add_SameBarDifferentSession_IsAcceptedInSeparateCollectors()
    {
        var firstSession = Guid.NewGuid();
        var secondSession = Guid.NewGuid();
        var firstCollector = new ScientificDatasetCollector(firstSession);
        var secondCollector = new ScientificDatasetCollector(secondSession);
        var firstRecord = CreateRecord(firstSession, currentBar: 10);
        var secondRecord = CreateRecord(secondSession, currentBar: 10);

        firstCollector.Add(firstRecord);
        secondCollector.Add(secondRecord);

        Assert.Equal(1, firstCollector.Count);
        Assert.Equal(1, secondCollector.Count);
        Assert.Equal(firstSession, firstCollector.Records[0].SessionId);
        Assert.Equal(secondSession, secondCollector.Records[0].SessionId);
    }

    [Fact]
    public void ExportCsvAndJson_AfterDuplicateRejected_WritesSingleRecord()
    {
        var sessionId = Guid.NewGuid();
        var collector = new ScientificDatasetCollector(sessionId);
        var record = CreateRecord(sessionId, currentBar: 7);

        collector.Add(record);
        collector.Add(record);

        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string csvPath = Path.Combine(directory, "dataset.csv");
        string jsonPath = Path.Combine(directory, "dataset.json");

        collector.ExportCsv(csvPath);
        collector.ExportJson(jsonPath);

        string csvContent = File.ReadAllText(csvPath);
        string jsonContent = File.ReadAllText(jsonPath);

        Assert.Contains("SessionId", csvContent);
        Assert.Equal(2, csvContent.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Contains("\"SessionId\"", jsonContent);
        Assert.Contains(sessionId.ToString("D"), jsonContent);
        Assert.Contains("\"CurrentBar\": 7", jsonContent);
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
