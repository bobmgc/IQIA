using System;

namespace IQIAIndicator.Core.Calibration;

/// <summary>
/// Sprint 15.17.2 (QDE-012 real-market capture - persistent ATAS lifecycle diagnostics). On-disk
/// mirror of DatasetLifecycleLog plus the collector counts and identity needed to interpret it
/// without any other file. Deliberately flat and small - one JSON object, not a growing log -
/// because only the LATEST state matters for diagnosing a session after the fact. Every event field
/// is nullable and simply reflects what actually happened: a null OnDisposeEnteredAt after a real
/// session means exactly that ATAS never called OnDispose() (or the file was never updated again
/// after that point) - this type and its writer never assert an interpretation, only record
/// observations. See QDE-012_Sprint_15.17.2 report for how to read the finished file.
/// </summary>
public sealed record DatasetLifecycleSnapshot(
    Guid SessionId,
    string Symbol,
    string TimeFrame,
    DateTime? ConstructedAt,
    DateTime? DatasetEnabledAt,
    DateTime? FirstAddAt,
    DateTime? LastAddAt,
    DateTime? OnDisposeEnteredAt,
    DateTime? ExportStartedAt,
    DateTime? ExportCompletedAt,
    DateTime? ExportFailedAt,
    int BarsReceived,
    int BarsWritten,
    string? LastError,
    DateTime SnapshotWrittenAt);
