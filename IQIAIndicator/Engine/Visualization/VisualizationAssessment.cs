using System.Collections.Generic;
using System.Collections.Immutable;

namespace IQIAIndicator.Engine.Visualization;

public enum DisplayStatus
{
    HIDDEN,
    VISIBLE,
    HIGHLIGHTED,
    FEATURED
}

public sealed record VisualizationAssessment(
    DisplayStatus DisplayStatus,
    int DisplayPriority,
    IReadOnlyList<string> DisplayReasons,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Diagnostics)
{
    public ImmutableDictionary<string, object> ToDictionary()
        => ImmutableDictionary<string, object>.Empty
            .Add("DisplayStatus", DisplayStatus.ToString())
            .Add("DisplayPriority", DisplayPriority)
            .Add("DisplayReasons", DisplayReasons)
            .Add("Warnings", Warnings)
            .Add("Diagnostics", Diagnostics);
}
