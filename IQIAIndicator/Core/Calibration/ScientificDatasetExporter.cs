using System;

namespace IQIAIndicator.Core.Calibration;

/// <summary>
/// Sprint 15.17.1 (QDE-012 real-market capture - production export observability). Extracted from
/// IQIAIndicator.ExportScientificDataset() so the "never throw, always record a real outcome"
/// behavior is unit-testable without the live ATAS platform - IQIAIndicator derives from ATAS's
/// Indicator base class and cannot be instantiated in any test (confirmed by grep across the whole
/// test suite, Sprint 15.17/15.17.1 reports). This is the ONLY place a dataset export is actually
/// attempted; IQIAIndicator.ExportScientificDataset() is a thin wrapper that also updates
/// DatasetLifecycleLog and the indicator's own cached fields.
/// </summary>
public static class ScientificDatasetExporter
{
    public static ExportResult Export(
        ScientificDatasetCollector? collector,
        ScientificDatasetSessionWriter writer,
        string outputDirectory,
        Guid sessionId,
        DateTime startTime,
        DateTime endTime)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        if (collector is null || collector.Count == 0)
            return ExportResult.NoData(outputDirectory);

        try
        {
            ScientificDatasetSession session = writer.Export(
                collector,
                outputDirectory,
                collector.Records[0].Symbol,
                collector.Records[0].TimeFrame,
                sessionId,
                startTime,
                endTime);
            return ExportResult.From(session);
        }
        catch (Exception exception)
        {
            return ExportResult.Failed(outputDirectory, exception);
        }
    }
}
