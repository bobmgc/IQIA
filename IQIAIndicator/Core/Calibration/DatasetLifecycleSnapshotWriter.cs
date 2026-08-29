using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace IQIAIndicator.Core.Calibration;

/// <summary>
/// Sprint 15.17.2 (QDE-012 real-market capture - persistent ATAS lifecycle diagnostics). Writes
/// DatasetLifecycleSnapshot to disk under the same "ScientificDataset_{Symbol}_{TimeFrame}_{stamp}"
/// prefix ScientificDatasetSessionWriter uses for the four export files (ScientificDatasetSession.
/// BuildFilePrefix, shared, not duplicated) - so `*_lifecycle.json` sits alongside them, findable
/// from Windows Explorer with no other tool.
///
/// TryWrite is called repeatedly during a live session (IQIAIndicator: once when the collector is
/// created, periodically while bars arrive, and around OnDispose/export) - it MUST NEVER throw, since
/// a diagnostic write failure must never break indicator teardown or bar processing (Sprint 15.17.2
/// brief, Phase 5). Every failure is caught and reported back via the out errorMessage parameter
/// instead - the caller decides what, if anything, to do with it (IQIAIndicator surfaces it on the
/// Dataset dashboard rather than losing it silently).
///
/// Writes go to a temporary file first, then an atomic File.Move(overwrite: true) onto the real path
/// - a crash or concurrent read mid-write can only ever see the PREVIOUS complete, valid snapshot or
/// the new one, never a half-written, unparsable file (Phase 6, TEST 8).
/// </summary>
public static class DatasetLifecycleSnapshotWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static string BuildPath(string outputDirectory, string symbol, string timeFrame, DateTime startTime)
    {
        string prefix = ScientificDatasetSessionWriter.BuildFilePrefix(symbol, timeFrame, startTime);
        return Path.Combine(outputDirectory, prefix + "_lifecycle.json");
    }

    public static bool TryWrite(
        string outputDirectory,
        Guid sessionId,
        string symbol,
        string timeFrame,
        DateTime startTime,
        DatasetLifecycleLog lifecycle,
        int barsReceived,
        int barsWritten,
        string? lastError,
        out string path,
        out string? errorMessage)
    {
        path = BuildPath(outputDirectory, symbol, timeFrame, startTime);

        try
        {
            ArgumentNullException.ThrowIfNull(lifecycle);
            Directory.CreateDirectory(outputDirectory);

            var snapshot = new DatasetLifecycleSnapshot(
                sessionId,
                symbol,
                timeFrame,
                lifecycle.ConstructedAt,
                lifecycle.DatasetEnabledAt,
                lifecycle.FirstAddAt,
                lifecycle.LastAddAt,
                lifecycle.OnDisposeEnteredAt,
                lifecycle.ExportStartedAt,
                lifecycle.ExportCompletedAt,
                lifecycle.ExportFailedAt,
                barsReceived,
                barsWritten,
                lastError,
                SnapshotWrittenAt: DateTime.UtcNow);

            string json = JsonSerializer.Serialize(snapshot, JsonOptions);
            string tempPath = path + ".tmp";
            File.WriteAllText(tempPath, json, Utf8NoBom);
            File.Move(tempPath, path, overwrite: true);

            errorMessage = null;
            return true;
        }
        catch (Exception exception)
        {
            errorMessage = $"{exception.GetType().Name}: {exception.Message}";
            return false;
        }
    }
}
