using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace IQIAIndicator.Core.Calibration;

/// <summary>
/// Sprint 15.17 (QDE-012 real-market capture) - provenance/integrity record for one collection
/// session, written alongside the CSV/JSON exports as dataset_metadata.json. Every value here comes
/// directly from the ScientificDatasetCollector instance passed to ScientificDatasetSessionWriter.Export
/// - nothing is invented or estimated. Timezone is reported honestly as "Unspecified" rather than a
/// guessed value: the live pipeline (Core/MarketContextBuilder.cs) passes ATAS's IndicatorCandle.Time
/// through unconverted, with no TimeZoneInfo/DateTimeKind handling anywhere - see the QDE-012
/// Sprint 15.16/15.17 audits. CollectorVersion identifies this record schema, not the ATAS platform.
///
/// Sprint 15.19 (QDE-012 forming-bar-capture fix): FormingBarUpdates and BarsPendingAtDispose added as
/// trailing OPTIONAL parameters (default 0) so System.Text.Json can still deserialize metadata.json
/// files written before this sprint (e.g. the Sprint 15.18 raw-capture fixtures committed under
/// Tests/Research/StopLossCalibration/RealMarket/RawCapture/, which must never be edited) without
/// throwing on the now-missing fields - same pattern Sprint 15.17 used for ScientificDatasetRecord's
/// Open/High/Low/Volume. BarsWritten now means "bars ATAS proved closed" (see
/// ScientificDatasetCollector.RecordsAccepted's doc comment) rather than "first snapshot accepted" -
/// same field name, evolved meaning, not a schema break.
/// </summary>
public sealed record ScientificDatasetMetadata(
    string Source,
    Guid SessionId,
    string Symbol,
    string TimeFrame,
    DateTime CollectionStart,
    DateTime? CollectionEnd,
    int BarsReceived,
    int BarsWritten,
    int Duplicates,
    int InvalidRejected,
    int OutOfOrder,
    DateTime? FirstTimestamp,
    DateTime? LastTimestamp,
    string Timezone,
    string CollectorVersion,
    int FormingBarUpdates = 0,
    int BarsPendingAtDispose = 0);

public sealed record ScientificDatasetSession(
    Guid SessionId,
    DateTime StartTime,
    DateTime? EndTime,
    string Symbol,
    string TimeFrame,
    int BarsCollected,
    string OutputDirectory,
    string? CSVPath,
    string? JSONPath,
    TimeSpan Duration,
    long DatasetSizeBytes,
    string Source = "ATAS",
    string? OhlcvCsvPath = null,
    string? MetadataPath = null)
{
    public double AverageMillisecondsPerBar =>
        BarsCollected == 0 ? 0.0 : Duration.TotalMilliseconds / BarsCollected;

    public string BuildReport()
    {
        var builder = new StringBuilder();
        builder.AppendLine("====================================");
        builder.AppendLine("Scientific Dataset");
        builder.AppendLine("====================================");
        builder.AppendLine($"SessionId : {SessionId}");
        builder.AppendLine($"Symbol : {Symbol}");
        builder.AppendLine($"TimeFrame : {TimeFrame}");
        builder.AppendLine($"Bars : {BarsCollected}");
        builder.AppendLine($"Duration : {Duration.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture)} s");
        builder.AppendLine($"Average/bar : {AverageMillisecondsPerBar.ToString("F3", CultureInfo.InvariantCulture)} ms");
        builder.AppendLine($"OutputDirectory : {OutputDirectory}");
        builder.AppendLine($"CSV : {CSVPath ?? "N/A"}");
        builder.AppendLine($"JSON : {JSONPath ?? "N/A"}");
        builder.AppendLine($"OHLCV CSV : {OhlcvCsvPath ?? "N/A"}");
        builder.AppendLine($"Metadata : {MetadataPath ?? "N/A"}");
        builder.AppendLine($"Source : {Source}");
        builder.AppendLine($"Dataset size : {DatasetSizeBytes} bytes");
        builder.AppendLine("====================================");
        return builder.ToString();
    }
}

public sealed class ScientificDatasetSessionWriter
{
    /// <summary>Identifies this record's on-disk schema (which fields exist), not the ATAS platform
    /// or the data itself. Bump only if ScientificDatasetRecord's or this metadata's shape changes.</summary>
    private const string CollectorVersion = "1.0-real-ohlcv";

    /// <summary>Honest placeholder: no timezone conversion is applied anywhere in the live pipeline
    /// today (Core/MarketContextBuilder.cs passes ATAS's IndicatorCandle.Time through unconverted).
    /// Recorded explicitly rather than guessed - see QDE-012_Sprint_15.16/15.17 reports.</summary>
    private const string UnspecifiedTimezone = "Unspecified (ATAS IndicatorCandle.Time passthrough, no conversion applied)";

    private ScientificDatasetSession? _lastSession;

    public ScientificDatasetSession? LastSession => _lastSession;

    public ScientificDatasetSession Export(
        ScientificDatasetCollector collector,
        string outputDirectory,
        string symbol,
        string timeFrame,
        Guid sessionId,
        DateTime startTime,
        DateTime endTime)
    {
        ArgumentNullException.ThrowIfNull(collector);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        Directory.CreateDirectory(outputDirectory);
        string prefix = BuildFilePrefix(symbol, timeFrame, startTime);
        string csvPath = Path.Combine(outputDirectory, prefix + ".csv");
        string jsonPath = Path.Combine(outputDirectory, prefix + ".json");
        string ohlcvCsvPath = Path.Combine(outputDirectory, prefix + "_ohlcv.csv");
        string metadataPath = Path.Combine(outputDirectory, prefix + "_metadata.json");

        collector.ExportCsv(csvPath);
        collector.ExportJson(jsonPath);
        collector.ExportOhlcvCsv(ohlcvCsvPath);

        var metadata = new ScientificDatasetMetadata(
            Source: "ATAS",
            SessionId: sessionId,
            Symbol: symbol,
            TimeFrame: timeFrame,
            CollectionStart: startTime,
            CollectionEnd: endTime,
            BarsReceived: collector.TotalAddAttempts,
            BarsWritten: collector.RecordsAccepted,
            Duplicates: collector.DuplicateRecordsRejected,
            InvalidRejected: collector.InvalidRecordsRejected,
            OutOfOrder: collector.RecordsOutOfOrder,
            FirstTimestamp: collector.FirstTimestamp,
            LastTimestamp: collector.LastTimestamp,
            Timezone: UnspecifiedTimezone,
            CollectorVersion: CollectorVersion,
            FormingBarUpdates: collector.FormingBarUpdates,
            BarsPendingAtDispose: collector.BarsPendingAtDispose);
        File.WriteAllText(
            metadataPath,
            JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        long size = new FileInfo(csvPath).Length + new FileInfo(jsonPath).Length + new FileInfo(ohlcvCsvPath).Length + new FileInfo(metadataPath).Length;
        _lastSession = new ScientificDatasetSession(
            sessionId,
            startTime,
            endTime,
            symbol,
            timeFrame,
            collector.Count,
            outputDirectory,
            csvPath,
            jsonPath,
            endTime - startTime,
            size,
            Source: "ATAS",
            OhlcvCsvPath: ohlcvCsvPath,
            MetadataPath: metadataPath);
        return _lastSession;
    }

    /// <summary>Sprint 15.17.2: shared with DatasetLifecycleSnapshotWriter so the persistent lifecycle
    /// snapshot file sits under the exact same "ScientificDataset_{Symbol}_{TimeFrame}_{stamp}" prefix
    /// as the four export files from the same session - one naming rule, not two that could drift
    /// apart. startTime (not endTime) is used deliberately: it is known from session start, before any
    /// export has happened, which is exactly when the lifecycle file first needs to exist.</summary>
    internal static string BuildFilePrefix(string symbol, string timeFrame, DateTime startTime)
    {
        string safeSymbol = Sanitize(symbol, "UnknownSymbol");
        string safeTimeFrame = Sanitize(timeFrame, "UnknownTimeFrame");
        string stamp = startTime.ToLocalTime().ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        return $"ScientificDataset_{safeSymbol}_{safeTimeFrame}_{stamp}";
    }

    private static string Sanitize(string value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        foreach (char character in value)
            builder.Append(Array.IndexOf(invalid, character) >= 0 ? '_' : character);
        return builder.ToString().Replace(' ', '_');
    }
}
