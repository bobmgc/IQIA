using System;

namespace IQIAIndicator.Core.Calibration;

/// <summary>Sprint 15.17.1: the outcome of a single dataset export attempt. Distinct from
/// Visualization.Dashboards.ExportStatus (dashboard-facing, 5 values including pre-attempt states
/// like NotEnabled/ReadyToExport) - this only ever describes an attempt that actually happened.</summary>
public enum ExportOutcome
{
    Success,
    NoData,
    Failed
}

/// <summary>
/// Sprint 15.17.1 (QDE-012 real-market capture - production export observability). Replaces
/// ExportScientificDataset()'s previous ambiguous `string` return (empty string meant either "no
/// data" or was never reachable on failure, since failures were swallowed by the caller). Every
/// field here is populated from a real attempt - nothing is guessed. Session is populated only on
/// Success, kept for backward compatibility with IQIAIndicator.LastScientificDatasetSession/
/// LastScientificDatasetReport (unchanged public API), otherwise every path/count field on this
/// record itself already mirrors the equivalent Session field for callers that don't need the
/// full Session object.
/// </summary>
public sealed record ExportResult(
    ExportOutcome Outcome,
    string OutputDirectory,
    string? CsvPath,
    string? JsonPath,
    string? MetadataPath,
    string? OhlcvCsvPath,
    DateTime ExportedAt,
    int RecordCount,
    string? ErrorMessage,
    string? ErrorType,
    ScientificDatasetSession? Session = null)
{
    public static ExportResult NoData(string outputDirectory) => new(
        ExportOutcome.NoData,
        outputDirectory,
        CsvPath: null,
        JsonPath: null,
        MetadataPath: null,
        OhlcvCsvPath: null,
        ExportedAt: DateTime.UtcNow,
        RecordCount: 0,
        ErrorMessage: "No data collected - nothing to export.",
        ErrorType: null);

    public static ExportResult Failed(string outputDirectory, Exception exception) => new(
        ExportOutcome.Failed,
        outputDirectory,
        CsvPath: null,
        JsonPath: null,
        MetadataPath: null,
        OhlcvCsvPath: null,
        ExportedAt: DateTime.UtcNow,
        RecordCount: 0,
        ErrorMessage: exception.Message,
        ErrorType: exception.GetType().Name);

    public static ExportResult From(ScientificDatasetSession session) => new(
        ExportOutcome.Success,
        session.OutputDirectory,
        session.CSVPath,
        session.JSONPath,
        session.MetadataPath,
        session.OhlcvCsvPath,
        ExportedAt: session.EndTime ?? DateTime.UtcNow,
        RecordCount: session.BarsCollected,
        ErrorMessage: null,
        ErrorType: null,
        Session: session);
}
