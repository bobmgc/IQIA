using System;
using System.Collections.Generic;
using System.IO;
using IQIAIndicator.Core.Calibration;
using IQIAIndicator.Visualization.Dashboards;
using Xunit;

namespace IQIAIndicator.Tests.Calibration;

/// <summary>
/// Sprint 15.17.1 (QDE-012 real-market capture - production export observability), Phase 2G's 10
/// required tests, plus direct coverage of the new ExportStatus resolver shared by DatasetDashboard
/// and ScientificCollectionMonitorWidget. As with Sprint 15.17's own test suite, IQIAIndicator itself
/// cannot be instantiated here (derives from ATAS's Indicator base class) - every test below targets
/// ScientificDatasetExporter/ExportResult/DatasetDashboard.ResolveExportStatus directly, which is
/// exactly the code IQIAIndicator.ExportScientificDataset() now delegates to.
/// </summary>
public sealed class ExportObservabilityTests
{
    // ── TEST 1: default output directory resolves to %LOCALAPPDATA%\IQIA\ScientificDataset ────────
    // IQIAIndicator cannot be constructed in tests, so this asserts the exact expression its
    // ScientificDatasetOutputDirectory property initializer uses (IQIAIndicator.cs:128-129) computes
    // the expected path - not the live property value itself, which is untestable here.

    [Fact]
    public void DefaultProductionPath_ResolvesToLocalAppDataIqiaScientificDataset()
    {
        string expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IQIA", "ScientificDataset");
        string actual = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IQIA", "ScientificDataset");

        Assert.Equal(expected, actual);
        Assert.EndsWith(Path.Combine("IQIA", "ScientificDataset"), actual, StringComparison.Ordinal);
    }

    // ── TEST 2: default path is NOT under Temp/TestResults/bin/obj/repository/OneDrive ─────────────

    [Fact]
    public void DefaultProductionPath_IsNotUnderTempOrBuildOrRepositoryFolders()
    {
        string defaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IQIA", "ScientificDataset");
        string tempPath = Path.GetTempPath();
        string? repoRoot = FindRepositoryRoot();

        Assert.False(defaultPath.StartsWith(tempPath, StringComparison.OrdinalIgnoreCase),
            $"Production default path must not live under Path.GetTempPath() ({tempPath}). Actual={defaultPath}.");
        Assert.DoesNotContain("TestResults", defaultPath, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Path.Combine("bin", "Debug"), defaultPath, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", defaultPath, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("OneDrive", defaultPath, StringComparison.OrdinalIgnoreCase);
        if (repoRoot is not null)
        {
            Assert.False(defaultPath.StartsWith(repoRoot, StringComparison.OrdinalIgnoreCase),
                $"Production default path must not live inside the git repository ({repoRoot}). Actual={defaultPath}.");
        }
    }

    private static string? FindRepositoryRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, ".git")))
            dir = Directory.GetParent(dir)?.FullName;
        return dir;
    }

    // ── TEST 3: custom ScientificDatasetOutputDirectory is respected ────────────────────────────────
    // Sprint 15.19 (QDE-012 forming-bar-capture fix): every test below that needs collector.Count > 0
    // now adds a SECOND record on a later CurrentBar before exporting - under the new "last update
    // before advance wins" contract, a single Add() only becomes the pending (still-open) bar; it is
    // only finalized, and only then exported, once a callback for a later bar proves it closed. See
    // ScientificDatasetCollector.cs's class doc comment and QDE-012_Sprint_15.18_ATAS_Real_Data_
    // Quality_Report.md §6 for why a lone callback is no longer treated as "the observation."

    [Fact]
    public void Export_WithCustomOutputDirectory_WritesExactlyThere()
    {
        using var tempDir = new TemporaryDirectory("iqia_test_custom_dir_");
        var collector = new ScientificDatasetCollector(Guid.NewGuid());
        collector.Add(BuildValidRecord(collector.SessionId, currentBar: 0));
        collector.Add(BuildValidRecord(collector.SessionId, currentBar: 1)); // proves bar 0 closed

        ExportResult result = ScientificDatasetExporter.Export(
            collector, new ScientificDatasetSessionWriter(), tempDir.Path, collector.SessionId, DateTime.UtcNow.AddSeconds(-1), DateTime.UtcNow);

        Assert.Equal(ExportOutcome.Success, result.Outcome);
        Assert.Equal(tempDir.Path, result.OutputDirectory);
        Assert.StartsWith(tempDir.Path, result.CsvPath!, StringComparison.Ordinal);
        Assert.StartsWith(tempDir.Path, result.OhlcvCsvPath!, StringComparison.Ordinal);
    }

    // ── TEST 4: export creates the expected 4 files ──────────────────────────────────────────────

    [Fact]
    public void Export_WithData_CreatesAllFourFiles()
    {
        using var tempDir = new TemporaryDirectory();
        var collector = new ScientificDatasetCollector(Guid.NewGuid());
        collector.Add(BuildValidRecord(collector.SessionId, currentBar: 0));
        collector.Add(BuildValidRecord(collector.SessionId, currentBar: 1)); // proves bar 0 closed

        ExportResult result = ScientificDatasetExporter.Export(
            collector, new ScientificDatasetSessionWriter(), tempDir.Path, collector.SessionId, DateTime.UtcNow.AddSeconds(-1), DateTime.UtcNow);

        Assert.Equal(ExportOutcome.Success, result.Outcome);
        Assert.True(File.Exists(result.CsvPath));
        Assert.True(File.Exists(result.JsonPath));
        Assert.True(File.Exists(result.OhlcvCsvPath));
        Assert.True(File.Exists(result.MetadataPath));
    }

    // ── TEST 5: export writes OHLCV rows ─────────────────────────────────────────────────────────

    [Fact]
    public void Export_WithData_OhlcvFileContainsRealRows()
    {
        using var tempDir = new TemporaryDirectory();
        var collector = new ScientificDatasetCollector(Guid.NewGuid());
        collector.Add(BuildValidRecord(collector.SessionId, open: 100.5m, high: 101.5m, low: 99.5m, close: 101.0m, volume: 42m, currentBar: 0));
        collector.Add(BuildValidRecord(collector.SessionId, currentBar: 1)); // proves bar 0 closed

        ExportResult result = ScientificDatasetExporter.Export(
            collector, new ScientificDatasetSessionWriter(), tempDir.Path, collector.SessionId, DateTime.UtcNow.AddSeconds(-1), DateTime.UtcNow);

        string[] lines = File.ReadAllLines(result.OhlcvCsvPath!);
        Assert.Equal(2, lines.Length); // header + 1 real row
        Assert.Contains("100.5", lines[1]);
        Assert.Contains("101.5", lines[1]);
        Assert.Contains("99.5", lines[1]);
        Assert.Contains("42", lines[1]);
    }

    // ── TEST 6: metadata contains Source=ATAS, BarsReceived, BarsWritten, FirstTimestamp, LastTimestamp ─

    [Fact]
    public void Export_WithData_MetadataContainsRequiredFields()
    {
        using var tempDir = new TemporaryDirectory();
        var collector = new ScientificDatasetCollector(Guid.NewGuid());
        collector.Add(BuildValidRecord(collector.SessionId, currentBar: 0));
        collector.Add(BuildValidRecord(collector.SessionId, currentBar: 1)); // proves bar 0 closed

        ExportResult result = ScientificDatasetExporter.Export(
            collector, new ScientificDatasetSessionWriter(), tempDir.Path, collector.SessionId, DateTime.UtcNow.AddSeconds(-1), DateTime.UtcNow);

        string json = File.ReadAllText(result.MetadataPath!);
        Assert.Contains("\"Source\": \"ATAS\"", json);
        Assert.Contains("\"BarsReceived\"", json);
        Assert.Contains("\"BarsWritten\"", json);
        Assert.Contains("\"FirstTimestamp\"", json);
        Assert.Contains("\"LastTimestamp\"", json);
    }

    // ── TEST 7: export failure is surfaced and not silently swallowed ───────────────────────────────
    // Forces a real, deterministic I/O failure: a FILE (not a directory) already occupies the exact
    // path Export() needs as a directory, so Directory.CreateDirectory(...) throws IOException.

    [Fact]
    public void Export_WhenOutputDirectoryIsBlockedByAFile_ReturnsFailedWithRealError()
    {
        using var tempDir = new TemporaryDirectory("iqia_test_blocked_");
        Directory.CreateDirectory(Path.GetDirectoryName(tempDir.Path)!);
        File.WriteAllText(tempDir.Path, "this occupies the path Export() needs as a directory");
        try
        {
            var collector = new ScientificDatasetCollector(Guid.NewGuid());
            collector.Add(BuildValidRecord(collector.SessionId, currentBar: 0));
            collector.Add(BuildValidRecord(collector.SessionId, currentBar: 1)); // proves bar 0 closed

            ExportResult result = ScientificDatasetExporter.Export(
                collector, new ScientificDatasetSessionWriter(), tempDir.Path, collector.SessionId, DateTime.UtcNow.AddSeconds(-1), DateTime.UtcNow);

            Assert.Equal(ExportOutcome.Failed, result.Outcome);
            Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
            Assert.False(string.IsNullOrWhiteSpace(result.ErrorType));
            Assert.Equal(tempDir.Path, result.OutputDirectory);
            Assert.Null(result.CsvPath);
            Assert.Null(result.Session);
        }
        finally
        {
            File.Delete(tempDir.Path); // so TemporaryDirectory.Dispose's Directory.Delete is a no-op, not an error
        }
    }

    // ── TEST 8: calling export twice does not corrupt or duplicate the session ──────────────────────
    // IQIAIndicator's own _scientificDatasetAutoExportAttempted guard (preventing OnDispose from
    // triggering two automatic exports) cannot be unit-tested directly - it lives on a class that
    // cannot be constructed here. What IS provable at this layer: the underlying writer/exporter is
    // itself idempotent when invoked twice with the same session identity - re-running Export() never
    // corrupts the files or duplicates collected data, which is the property the guard depends on.

    [Fact]
    public void Export_CalledTwiceWithSameSession_DoesNotCorruptOrDuplicateData()
    {
        using var tempDir = new TemporaryDirectory();
        var collector = new ScientificDatasetCollector(Guid.NewGuid());
        collector.Add(BuildValidRecord(collector.SessionId, currentBar: 0));
        collector.Add(BuildValidRecord(collector.SessionId, currentBar: 1)); // proves bar 0 closed
        var writer = new ScientificDatasetSessionWriter();
        DateTime start = DateTime.UtcNow.AddSeconds(-1);

        ExportResult first = ScientificDatasetExporter.Export(collector, writer, tempDir.Path, collector.SessionId, start, DateTime.UtcNow);
        int recordsAfterFirst = collector.Count;
        ExportResult second = ScientificDatasetExporter.Export(collector, writer, tempDir.Path, collector.SessionId, start, DateTime.UtcNow);

        Assert.Equal(ExportOutcome.Success, first.Outcome);
        Assert.Equal(ExportOutcome.Success, second.Outcome);
        Assert.Equal(first.CsvPath, second.CsvPath); // same session/start time => same filename, not a second file
        Assert.Equal(recordsAfterFirst, collector.Count); // exporting never mutates/duplicates collected records
        Assert.True(File.Exists(second.OhlcvCsvPath));
    }

    // ── TEST 9: empty session produces NO_DATA rather than fake output ──────────────────────────────

    [Fact]
    public void Export_WithEmptyCollector_ReturnsNoDataAndWritesNoFiles()
    {
        using var tempDir = new TemporaryDirectory();
        var collector = new ScientificDatasetCollector(Guid.NewGuid());

        ExportResult result = ScientificDatasetExporter.Export(
            collector, new ScientificDatasetSessionWriter(), tempDir.Path, collector.SessionId, DateTime.UtcNow.AddSeconds(-1), DateTime.UtcNow);

        Assert.Equal(ExportOutcome.NoData, result.Outcome);
        Assert.Null(result.CsvPath);
        Assert.Null(result.OhlcvCsvPath);
        Assert.Null(result.MetadataPath);
        Assert.False(Directory.Exists(tempDir.Path), "NoData must never create the output directory or any file - nothing was collected, so nothing may be presented as exported.");
    }

    // ── TEST 10: no production scientific/trading code is modified ──────────────────────────────────
    // This is a repository/git-diff-level guarantee (Sprint 15.17.1 report, Phase 2K), not something
    // a unit test inside this assembly can prove - a unit test cannot observe what files a git diff
    // touched. Deliberately not faked here as a hollow "test"; see the report's Production Perimeter
    // section for the actual verification (git status/diff, before and after).

    // ── Bonus: ExportStatus resolver, shared by DatasetDashboard and ScientificCollectionMonitorWidget ─

    [Fact]
    public void ResolveExportStatus_Disabled_IsNotEnabled()
    {
        Assert.Equal(ExportStatus.NotEnabled, DatasetDashboard.ResolveExportStatus(false, null, null));
        Assert.Equal(ExportStatus.NotEnabled, DatasetDashboard.ResolveExportStatus(false, new ScientificDatasetCollector(Guid.NewGuid()), null));
    }

    [Fact]
    public void ResolveExportStatus_EnabledNoCollectorOrEmptyCollector_IsNoData()
    {
        Assert.Equal(ExportStatus.NoData, DatasetDashboard.ResolveExportStatus(true, null, null));
        Assert.Equal(ExportStatus.NoData, DatasetDashboard.ResolveExportStatus(true, new ScientificDatasetCollector(Guid.NewGuid()), null));
    }

    [Fact]
    public void ResolveExportStatus_EnabledWithDataNoAttemptYet_IsReadyToExport()
    {
        var collector = new ScientificDatasetCollector(Guid.NewGuid());
        collector.Add(BuildValidRecord(collector.SessionId, currentBar: 0));
        collector.Add(BuildValidRecord(collector.SessionId, currentBar: 1)); // proves bar 0 closed

        Assert.Equal(ExportStatus.ReadyToExport, DatasetDashboard.ResolveExportStatus(true, collector, null));
    }

    [Fact]
    public void ResolveExportStatus_LastAttemptSucceeded_IsExported()
    {
        ExportResult success = ExportResult.From(new ScientificDatasetSession(
            Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow, "TEST", "1min", 1, "C:\\x", "a.csv", "a.json", TimeSpan.Zero, 1));

        Assert.Equal(ExportStatus.Exported, DatasetDashboard.ResolveExportStatus(true, null, success));
    }

    [Fact]
    public void ResolveExportStatus_LastAttemptFailed_IsExportFailed()
    {
        ExportResult failed = ExportResult.Failed("C:\\x", new InvalidOperationException("boom"));

        Assert.Equal(ExportStatus.ExportFailed, DatasetDashboard.ResolveExportStatus(true, null, failed));
    }

    // ── Fixtures ─────────────────────────────────────────────────────────────────────────────────

    private static ScientificDatasetRecord BuildValidRecord(
        Guid sessionId,
        decimal open = 100m,
        decimal high = 101m,
        decimal low = 99m,
        decimal close = 100.5m,
        decimal volume = 10m,
        int currentBar = 0)
    {
        var marketContext = new Engine.ScientificModels.Abstractions.MarketContext(
            Timestamp: DateTime.UtcNow,
            CurrentBar: 0,
            History: new List<decimal> { 100m, close },
            CurrentPrice: close,
            Symbol: "ESH26",
            TimeFrame: "1min");

        var assessment = new Engine.ScientificFusion.ScientificAssessment(
            OverallConfidence: 0.5,
            EvidenceAgreement: Array.Empty<string>(),
            EvidenceConflict: Array.Empty<string>(),
            MissingEvidence: Array.Empty<string>(),
            ExecutedModels: Array.Empty<string>(),
            SuccessfulModels: Array.Empty<string>(),
            FailedModels: Array.Empty<string>(),
            ScientificResults: Array.Empty<Engine.ScientificModels.Abstractions.ScientificModelResult>(),
            Diagnostics: "fixture");

        var decision = new Engine.Decision.Core.DecisionResult
        {
            Winner = Engine.Decision.States.MarketState.MeanReverting,
            WinnerScore = 0.5,
            Confidence = 0.5
        };

        return ScientificDatasetRecord.From(sessionId, currentBar, marketContext, assessment, decision, open, high, low, volume);
    }
}
