using System.Collections.Generic;
using IQIAIndicator.Core.Observability;

namespace IQIAIndicator.Visualization.State;

/// <summary>
/// Historique glissant des PipelineTraceRun, propriété exclusive du Performance Dashboard.
/// Ne recalcule aucun timing : accumule simplement les mesures déjà produites par
/// PipelineTraceCollector pour permettre moyenne/min/max/rolling/P99.
/// </summary>
internal sealed class PerformanceHistory
{
    private const int Capacity = 300;
    private readonly Queue<PipelineTraceRun> _runs = new();

    public void Record(PipelineTraceRun? run)
    {
        if (run is null)
            return;

        _runs.Enqueue(run);
        while (_runs.Count > Capacity)
            _runs.Dequeue();
    }

    public IReadOnlyCollection<PipelineTraceRun> Runs => _runs;
}
