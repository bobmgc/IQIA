using System;

namespace IQIAIndicator.Core.Calibration;

/// <summary>
/// Sprint 15.17.1 (QDE-012 real-market capture). Minimal, fixed-shape instrumentation - not a log
/// file, not an unbounded history - that exists to answer exactly one open question from Sprint
/// 15.17: does ATAS's own lifecycle actually invoke IQIAIndicator.OnDispose() when the indicator is
/// removed from a chart? Reflection against the installed ATAS.Indicators.dll only proved the hook
/// exists and is overridable (Sprint 15.17 audit); it cannot prove ATAS calls it on removal
/// specifically. Comparing OnDisposeEnteredAt against LastAddAt after a real removal settles this.
///
/// "First" timestamps (ConstructedAt, DatasetEnabledAt, FirstAddAt, OnDisposeEnteredAt) are set once
/// and never overwritten. "Most recent" timestamps (LastAddAt, ExportStartedAt/CompletedAt/FailedAt)
/// are overwritten on every occurrence - there is deliberately no growing list of every export
/// attempt, since only the most recent one matters for diagnosing the current session.
/// </summary>
public sealed class DatasetLifecycleLog
{
    public DateTime? ConstructedAt { get; private set; }
    public DateTime? DatasetEnabledAt { get; private set; }
    public DateTime? FirstAddAt { get; private set; }
    public DateTime? LastAddAt { get; private set; }
    public DateTime? OnDisposeEnteredAt { get; private set; }
    public DateTime? ExportStartedAt { get; private set; }
    public DateTime? ExportCompletedAt { get; private set; }
    public DateTime? ExportFailedAt { get; private set; }

    public void MarkConstructed(DateTime at) => ConstructedAt ??= at;

    public void MarkDatasetEnabled(DateTime at) => DatasetEnabledAt ??= at;

    public void MarkAdd(DateTime at)
    {
        FirstAddAt ??= at;
        LastAddAt = at;
    }

    public void MarkOnDisposeEntered(DateTime at) => OnDisposeEnteredAt ??= at;

    public void MarkExportStarted(DateTime at) => ExportStartedAt = at;

    public void MarkExportCompleted(DateTime at) => ExportCompletedAt = at;

    public void MarkExportFailed(DateTime at) => ExportFailedAt = at;
}
