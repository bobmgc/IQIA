using System;
using System.Collections.Generic;

namespace IQIAIndicator.Core.Observability;

public sealed class PipelineTraceCollector : IPipelineTraceCollector
{
    public bool Enabled => true;

    public PipelineTraceRun StartRun(
        Guid runId,
        DateTime timestamp,
        int currentBar,
        string symbol,
        string timeFrame) =>
        new(new PipelineTraceContext(runId, timestamp, currentBar, symbol, timeFrame));

    public PipelineTraceScope BeginStage(PipelineTraceRun run, PipelineTraceStage stage)
    {
        ArgumentNullException.ThrowIfNull(run);
        return new PipelineTraceScope(this, run, stage);
    }

    public void Record(PipelineTraceRun run, PipelineTraceEvent traceEvent)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(traceEvent);
        run.Add(traceEvent);
    }

    public string BuildReport(PipelineTraceRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return PipelineTraceReportFormatter.Format(run);
    }
}

public sealed class NullPipelineTraceCollector : IPipelineTraceCollector
{
    public static readonly IPipelineTraceCollector Instance = new NullPipelineTraceCollector();

    private NullPipelineTraceCollector()
    {
    }

    public bool Enabled => false;

    public PipelineTraceRun StartRun(
        Guid runId,
        DateTime timestamp,
        int currentBar,
        string symbol,
        string timeFrame) =>
        throw new InvalidOperationException("Pipeline tracing is disabled.");

    public PipelineTraceScope BeginStage(PipelineTraceRun run, PipelineTraceStage stage) => default;

    public void Record(PipelineTraceRun run, PipelineTraceEvent traceEvent)
    {
    }

    public string BuildReport(PipelineTraceRun run) => string.Empty;
}
