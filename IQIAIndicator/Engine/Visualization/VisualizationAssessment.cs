using System.Collections.Generic;
using System.Collections.Immutable;
using IQIAIndicator.Engine.EntryTrigger;

namespace IQIAIndicator.Engine.Visualization;

public enum DisplayStatus
{
    HIDDEN,
    VISIBLE,
    HIGHLIGHTED,
    FEATURED
}

/// <summary>
/// Sprint 15.6 (BC-01): <see cref="Direction"/> is carried through verbatim from
/// <see cref="EntryTriggerAssessment.Direction"/> - the single source of truth established by Sprint
/// 15.5's decision-coherence gate. Never recomputed here, never re-derived from DynamicZScore or any
/// other raw metric. Null only when no <see cref="EntryTrigger.EntryTriggerCandidate"/> was available
/// to populate from.
/// </summary>
public sealed record VisualizationAssessment(
    DisplayStatus DisplayStatus,
    int DisplayPriority,
    IReadOnlyList<string> DisplayReasons,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Diagnostics,
    DirectionCandidate? Direction = null)
{
    public ImmutableDictionary<string, object> ToDictionary()
        => ImmutableDictionary<string, object>.Empty
            .Add("DisplayStatus", DisplayStatus.ToString())
            .Add("DisplayPriority", DisplayPriority)
            .Add("DisplayReasons", DisplayReasons)
            .Add("Warnings", Warnings)
            .Add("Diagnostics", Diagnostics)
            .Add("Direction", Direction?.ToString() ?? "None");
}
