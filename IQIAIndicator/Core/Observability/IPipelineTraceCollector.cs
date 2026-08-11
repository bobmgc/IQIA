using System;
using System.Collections.Generic;

namespace IQIAIndicator.Core.Observability;

public interface IPipelineTraceCollector
{
    bool Enabled { get; }

    PipelineTraceRun StartRun(
        Guid runId,
        DateTime timestamp,
        int currentBar,
        string symbol,
        string timeFrame);

    PipelineTraceScope BeginStage(PipelineTraceRun run, PipelineTraceStage stage);

    void Record(PipelineTraceRun run, PipelineTraceEvent traceEvent);

    string BuildReport(PipelineTraceRun run);
}
