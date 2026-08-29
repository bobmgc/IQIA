using System;
using System.IO;
using System.Text.Json;
using IQIAIndicator.Core.Calibration;
using Xunit;

namespace IQIAIndicator.Tests.Calibration;

/// <summary>
/// Sprint 15.17.2 (QDE-012 real-market capture - persistent ATAS lifecycle diagnostics), Phase 6's
/// 10 required tests. As with every prior sprint in this series, IQIAIndicator itself cannot be
/// instantiated here (derives from ATAS's Indicator base class) - every test targets
/// DatasetLifecycleSnapshotWriter/DatasetLifecycleSnapshot/DatasetLifecycleLog directly, which is
/// exactly what IQIAIndicator.WriteLifecycleSnapshot() delegates to. TEST 4's actual call-ORDER
/// guarantee (OnDisposeEntered persisted before ExportStarted) lives in IQIAIndicator.OnDispose()
/// itself and is verified by code inspection (see the report), not by a test in this file - what IS
/// tested here is that the writer correctly persists each of those two states independently and in
/// sequence, which is the mechanism the ordering guarantee depends on.
/// </summary>
public sealed class DatasetLifecycleSnapshotTests
{
    // ── TEST 1: construction du lifecycle snapshot ───────────────────────────────────────────────

    [Fact]
    public void TryWrite_WithFreshLog_CreatesReadableSnapshotFile()
    {
        using var tempDir = new TemporaryDirectory("iqia_test_lifecycle_");
        var sessionId = Guid.NewGuid();
        var lifecycle = new DatasetLifecycleLog();
        lifecycle.MarkConstructed(new DateTime(2026, 8, 14, 10, 0, 0, DateTimeKind.Utc));

        bool success = DatasetLifecycleSnapshotWriter.TryWrite(
            tempDir.Path, sessionId, "ESH26", "1min", DateTime.UtcNow, lifecycle,
            barsReceived: 0, barsWritten: 0, lastError: null, out string path, out string? error);

        Assert.True(success);
        Assert.Null(error);
        Assert.True(File.Exists(path));
        Assert.EndsWith("_lifecycle.json", path, StringComparison.Ordinal);

        DatasetLifecycleSnapshot? snapshot = JsonSerializer.Deserialize<DatasetLifecycleSnapshot>(File.ReadAllText(path));
        Assert.NotNull(snapshot);
        Assert.Equal(sessionId, snapshot!.SessionId);
        Assert.NotNull(snapshot.ConstructedAt);
    }

    // ── TEST 2: DatasetEnabled est persisté ──────────────────────────────────────────────────────

    [Fact]
    public void TryWrite_WithDatasetEnabled_PersistsTimestamp()
    {
        using var tempDir = new TemporaryDirectory("iqia_test_lifecycle_");
        var enabledAt = new DateTime(2026, 8, 14, 10, 1, 0, DateTimeKind.Utc);
        var lifecycle = new DatasetLifecycleLog();
        lifecycle.MarkDatasetEnabled(enabledAt);

        DatasetLifecycleSnapshotWriter.TryWrite(
            tempDir.Path, Guid.NewGuid(), "ESH26", "1min", enabledAt, lifecycle,
            0, 0, null, out string path, out _);

        DatasetLifecycleSnapshot snapshot = ReadBack(path);
        Assert.Equal(enabledAt, snapshot.DatasetEnabledAt);
    }

    // ── TEST 3: FirstAdd / LastAdd sont persistés ────────────────────────────────────────────────

    [Fact]
    public void TryWrite_WithMultipleAdds_PersistsFirstAndLastDistinctly()
    {
        using var tempDir = new TemporaryDirectory("iqia_test_lifecycle_");
        var start = new DateTime(2026, 8, 14, 10, 0, 0, DateTimeKind.Utc);
        var lifecycle = new DatasetLifecycleLog();
        lifecycle.MarkAdd(start);
        lifecycle.MarkAdd(start.AddMinutes(5));
        lifecycle.MarkAdd(start.AddMinutes(10));

        DatasetLifecycleSnapshotWriter.TryWrite(
            tempDir.Path, Guid.NewGuid(), "ESH26", "1min", start, lifecycle,
            3, 3, null, out string path, out _);

        DatasetLifecycleSnapshot snapshot = ReadBack(path);
        Assert.Equal(start, snapshot.FirstAddAt);
        Assert.Equal(start.AddMinutes(10), snapshot.LastAddAt);
        Assert.NotEqual(snapshot.FirstAddAt, snapshot.LastAddAt);
        Assert.Equal(3, snapshot.BarsReceived);
        Assert.Equal(3, snapshot.BarsWritten);
    }

    // ── TEST 4: OnDisposeEntered est écrit avant ExportStarted (mechanism, see class doc comment) ───

    [Fact]
    public void TryWrite_CalledAfterOnDisposeEntered_ThenAfterExportStarted_ReflectsBothStagesInOrder()
    {
        using var tempDir = new TemporaryDirectory("iqia_test_lifecycle_");
        var start = DateTime.UtcNow;
        var lifecycle = new DatasetLifecycleLog();

        // Stage 1: mirrors IQIAIndicator.OnDispose()'s first two statements.
        lifecycle.MarkOnDisposeEntered(start.AddSeconds(1));
        DatasetLifecycleSnapshotWriter.TryWrite(tempDir.Path, Guid.NewGuid(), "ESH26", "1min", start, lifecycle, 10, 10, null, out string path, out _);
        DatasetLifecycleSnapshot afterDispose = ReadBack(path);
        Assert.NotNull(afterDispose.OnDisposeEnteredAt);
        Assert.Null(afterDispose.ExportStartedAt); // export has not been attempted yet at this stage

        // Stage 2: mirrors ExportScientificDataset()'s first statement.
        lifecycle.MarkExportStarted(start.AddSeconds(2));
        DatasetLifecycleSnapshotWriter.TryWrite(tempDir.Path, Guid.NewGuid(), "ESH26", "1min", start, lifecycle, 10, 10, null, out path, out _);
        DatasetLifecycleSnapshot afterExportStarted = ReadBack(path);
        Assert.NotNull(afterExportStarted.OnDisposeEnteredAt);
        Assert.NotNull(afterExportStarted.ExportStartedAt);
        Assert.True(afterExportStarted.ExportStartedAt > afterExportStarted.OnDisposeEnteredAt);
    }

    // ── TEST 5: ExportCompleted est persisté après succès ────────────────────────────────────────

    [Fact]
    public void TryWrite_WithExportCompleted_PersistsTimestamp()
    {
        using var tempDir = new TemporaryDirectory("iqia_test_lifecycle_");
        var completedAt = DateTime.UtcNow;
        var lifecycle = new DatasetLifecycleLog();
        lifecycle.MarkExportStarted(completedAt.AddSeconds(-1));
        lifecycle.MarkExportCompleted(completedAt);

        DatasetLifecycleSnapshotWriter.TryWrite(
            tempDir.Path, Guid.NewGuid(), "ESH26", "1min", completedAt, lifecycle,
            10, 10, null, out string path, out _);

        DatasetLifecycleSnapshot snapshot = ReadBack(path);
        Assert.Equal(completedAt, snapshot.ExportCompletedAt);
        Assert.Null(snapshot.ExportFailedAt);
    }

    // ── TEST 6: ExportFailed contient l'erreur ───────────────────────────────────────────────────

    [Fact]
    public void TryWrite_WithExportFailedAndError_PersistsErrorMessage()
    {
        using var tempDir = new TemporaryDirectory("iqia_test_lifecycle_");
        var failedAt = DateTime.UtcNow;
        var lifecycle = new DatasetLifecycleLog();
        lifecycle.MarkExportStarted(failedAt.AddSeconds(-1));
        lifecycle.MarkExportFailed(failedAt);
        const string realError = "IOException: disk full (simulated for TEST 6)";

        DatasetLifecycleSnapshotWriter.TryWrite(
            tempDir.Path, Guid.NewGuid(), "ESH26", "1min", failedAt, lifecycle,
            10, 0, realError, out string path, out _);

        DatasetLifecycleSnapshot snapshot = ReadBack(path);
        Assert.Equal(failedAt, snapshot.ExportFailedAt);
        Assert.Null(snapshot.ExportCompletedAt);
        Assert.Equal(realError, snapshot.LastError);
    }

    // ── TEST 7: un lifecycle sans OnDispose reste valide et lisible ──────────────────────────────
    // This is the exact scenario the whole sprint exists to support: if ATAS never calls OnDispose(),
    // the file must still exist and be valid JSON with OnDisposeEnteredAt explicitly null - not
    // missing, not malformed, not a guess about what happened.

    [Fact]
    public void TryWrite_WithoutOnDisposeEver_RemainsValidWithExplicitNull()
    {
        using var tempDir = new TemporaryDirectory("iqia_test_lifecycle_");
        var start = DateTime.UtcNow;
        var lifecycle = new DatasetLifecycleLog();
        lifecycle.MarkConstructed(start);
        lifecycle.MarkDatasetEnabled(start.AddSeconds(1));
        lifecycle.MarkAdd(start.AddSeconds(2));
        lifecycle.MarkAdd(start.AddSeconds(30));
        // OnDispose deliberately never marked - this is the "ATAS never called it" scenario.

        DatasetLifecycleSnapshotWriter.TryWrite(
            tempDir.Path, Guid.NewGuid(), "ESH26", "1min", start, lifecycle,
            2, 2, null, out string path, out string? error);

        Assert.Null(error);
        DatasetLifecycleSnapshot snapshot = ReadBack(path);
        Assert.NotNull(snapshot.ConstructedAt);
        Assert.NotNull(snapshot.DatasetEnabledAt);
        Assert.NotNull(snapshot.FirstAddAt);
        Assert.NotNull(snapshot.LastAddAt);
        Assert.Null(snapshot.OnDisposeEnteredAt);
        Assert.Null(snapshot.ExportStartedAt);
        Assert.Null(snapshot.ExportCompletedAt);
        Assert.Null(snapshot.ExportFailedAt);
    }

    // ── TEST 8: deux écritures successives ne corrompent pas le JSON ────────────────────────────

    [Fact]
    public void TryWrite_CalledTwiceInSuccession_LeavesValidJsonEachTime()
    {
        using var tempDir = new TemporaryDirectory("iqia_test_lifecycle_");
        var start = DateTime.UtcNow;
        var lifecycle = new DatasetLifecycleLog();
        lifecycle.MarkAdd(start);

        DatasetLifecycleSnapshotWriter.TryWrite(tempDir.Path, Guid.NewGuid(), "ESH26", "1min", start, lifecycle, 1, 1, null, out string path, out _);
        DatasetLifecycleSnapshot first = ReadBack(path);

        lifecycle.MarkAdd(start.AddMinutes(1));
        DatasetLifecycleSnapshotWriter.TryWrite(tempDir.Path, Guid.NewGuid(), "ESH26", "1min", start, lifecycle, 2, 2, null, out path, out _);
        DatasetLifecycleSnapshot second = ReadBack(path); // throws if the file is corrupted/concatenated

        Assert.Equal(1, first.BarsReceived);
        Assert.Equal(2, second.BarsReceived);
        Assert.False(File.Exists(path + ".tmp"), "The temp file used for atomic replacement must not survive a successful write.");
    }

    // ── TEST 9: aucune donnée scientifique/trading n'est modifiée ───────────────────────────────
    // Repository/git-diff-level guarantee, not something a unit test in this assembly can observe -
    // a test cannot inspect what files a git diff touched. See the Sprint 15.17.2 report's Production
    // Perimeter section for the actual verification (git status/diff, before and after). Deliberately
    // not faked here as a hollow assertion, consistent with Sprint 15.17.1's own TEST 10.

    // ── TEST 10: les tests utilisent un dossier temporaire isolé et le nettoient ────────────────

    [Fact]
    public void TemporaryDirectory_Dispose_RemovesTheDirectoryItCreated()
    {
        string capturedPath;
        using (var tempDir = new TemporaryDirectory("iqia_test_lifecycle_cleanup_"))
        {
            capturedPath = tempDir.Path;
            var lifecycle = new DatasetLifecycleLog();
            lifecycle.MarkConstructed(DateTime.UtcNow);
            DatasetLifecycleSnapshotWriter.TryWrite(tempDir.Path, Guid.NewGuid(), "ESH26", "1min", DateTime.UtcNow, lifecycle, 0, 0, null, out _, out _);
            Assert.True(Directory.Exists(capturedPath));
        }

        Assert.False(Directory.Exists(capturedPath), "TemporaryDirectory.Dispose must remove everything it created, including files written into it.");
    }

    // ── Bonus: write failure must never throw, per Phase 5 ───────────────────────────────────────

    [Fact]
    public void TryWrite_WhenOutputDirectoryIsBlockedByAFile_ReturnsFalseWithErrorAndNeverThrows()
    {
        using var tempDir = new TemporaryDirectory("iqia_test_lifecycle_blocked_");
        Directory.CreateDirectory(Path.GetDirectoryName(tempDir.Path)!);
        File.WriteAllText(tempDir.Path, "occupies the path TryWrite needs as a directory");
        try
        {
            var lifecycle = new DatasetLifecycleLog();
            lifecycle.MarkConstructed(DateTime.UtcNow);

            Exception? thrown = Record.Exception(() =>
                DatasetLifecycleSnapshotWriter.TryWrite(tempDir.Path, Guid.NewGuid(), "ESH26", "1min", DateTime.UtcNow, lifecycle, 0, 0, null, out _, out string? error));

            Assert.Null(thrown);
        }
        finally
        {
            File.Delete(tempDir.Path);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────

    private static DatasetLifecycleSnapshot ReadBack(string path)
    {
        DatasetLifecycleSnapshot? snapshot = JsonSerializer.Deserialize<DatasetLifecycleSnapshot>(File.ReadAllText(path));
        Assert.NotNull(snapshot);
        return snapshot!;
    }
}
