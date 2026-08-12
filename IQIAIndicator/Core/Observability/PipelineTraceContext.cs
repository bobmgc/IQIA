using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;

namespace IQIAIndicator.Core.Observability;

public enum PipelineTraceStage
{
    MarketContext,
    Decision,
    Methodology,
    ScientificModels,
    ScientificFusion,
    Entry,
    EntryTrigger,
    TradePlan,
    Visualization,
    ChartAnnotation,
    Presentation,
    Renderer
}

public sealed record PipelineTraceContext(
    Guid RunId,
    DateTime RunTimestamp,
    int CurrentBar,
    string Symbol,
    string TimeFrame);

public sealed record PipelineTraceEvent(
    PipelineTraceContext Context,
    PipelineTraceStage Stage,
    DateTime StartedAt,
    DateTime CompletedAt,
    TimeSpan Elapsed,
    bool Success,
    string? Error,
    int DiagnosticCount,
    IReadOnlyDictionary<string, string> Details);

public sealed class PipelineTraceRun
{
    private readonly List<PipelineTraceEvent> _events = new();

    internal PipelineTraceRun(PipelineTraceContext context)
    {
        Context = context;
    }

    public PipelineTraceContext Context { get; }

    public IReadOnlyList<PipelineTraceEvent> Events => new ReadOnlyCollection<PipelineTraceEvent>(_events);

    public TimeSpan TotalElapsed => _events.Count == 0
        ? TimeSpan.Zero
        : _events.Max(item => item.CompletedAt) - _events.Min(item => item.StartedAt);

    internal void Add(PipelineTraceEvent traceEvent)
    {
        _events.Add(traceEvent);
    }
}

public readonly struct PipelineTraceScope
{
    private readonly IPipelineTraceCollector? _collector;
    private readonly PipelineTraceRun? _run;
    private readonly PipelineTraceContext? _context;
    private readonly PipelineTraceStage _stage;
    private readonly DateTime _startedAt;

    internal PipelineTraceScope(
        IPipelineTraceCollector collector,
        PipelineTraceRun run,
        PipelineTraceStage stage)
    {
        _collector = collector;
        _run = run;
        _context = run.Context;
        _stage = stage;
        _startedAt = DateTime.UtcNow;
    }

    public void Complete(
        IReadOnlyDictionary<string, string>? details = null,
        int diagnosticCount = 0)
    {
        if (_collector is null || _run is null || _context is null)
            return;

        DateTime completedAt = DateTime.UtcNow;
        _collector.Record(
            _run,
            new PipelineTraceEvent(
                _context,
                _stage,
                _startedAt,
                completedAt,
                completedAt - _startedAt,
                true,
                null,
                diagnosticCount,
                details ?? EmptyDetails.Instance));
    }

    public void Fail(Exception exception, int diagnosticCount = 0)
    {
        if (_collector is null || _run is null || _context is null)
            return;

        DateTime completedAt = DateTime.UtcNow;
        _collector.Record(
            _run,
            new PipelineTraceEvent(
                _context,
                _stage,
                _startedAt,
                completedAt,
                completedAt - _startedAt,
                false,
                exception.Message,
                diagnosticCount,
                EmptyDetails.Instance));
    }

    private sealed class EmptyDetails : IReadOnlyDictionary<string, string>
    {
        public static readonly EmptyDetails Instance = new();
        public IEnumerable<string> Keys => Array.Empty<string>();
        public IEnumerable<string> Values => Array.Empty<string>();
        public int Count => 0;
        public string this[string key] => throw new KeyNotFoundException();
        public bool ContainsKey(string key) => false;
        public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => Enumerable.Empty<KeyValuePair<string, string>>().GetEnumerator();
        public bool TryGetValue(string key, out string value) { value = string.Empty; return false; }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

public static class PipelineTraceDetails
{
    public static IReadOnlyDictionary<string, string> Create(params (string Key, object? Value)[] values)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((string key, object? value) in values)
            result[key] = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        return result;
    }
}

public static class PipelineTraceReportFormatter
{
    public static string Format(PipelineTraceRun run)
    {
        var builder = new StringBuilder();
        builder.AppendLine("====================================");
        builder.AppendLine($"BAR {run.Context.CurrentBar}");
        builder.AppendLine("====================================");
        builder.AppendLine($"RunId : {run.Context.RunId}");
        builder.AppendLine($"Timestamp : {run.Context.RunTimestamp:O}");
        builder.AppendLine($"Symbol : {run.Context.Symbol}");
        builder.AppendLine($"TimeFrame : {run.Context.TimeFrame}");
        builder.AppendLine();

        foreach (PipelineTraceEvent traceEvent in run.Events)
        {
            builder.AppendLine(traceEvent.Stage.ToString());
            builder.AppendLine($"{traceEvent.Elapsed.TotalMilliseconds:F3} ms");
            builder.AppendLine(traceEvent.Success ? "OK" : "FAILED");
            builder.AppendLine($"Diagnostics : {traceEvent.DiagnosticCount}");
            if (!string.IsNullOrWhiteSpace(traceEvent.Error))
                builder.AppendLine($"Error : {traceEvent.Error}");
            foreach (KeyValuePair<string, string> detail in traceEvent.Details)
                builder.AppendLine($"{detail.Key} : {detail.Value}");
            builder.AppendLine();
        }

        builder.AppendLine("====================================");
        builder.AppendLine($"Total : {run.TotalElapsed.TotalMilliseconds:F3} ms");
        builder.AppendLine("====================================");
        return builder.ToString();
    }
}
