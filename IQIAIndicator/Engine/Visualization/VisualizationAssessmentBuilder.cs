using System.Collections.Generic;
using System.Linq;
using IQIAIndicator.Engine.Entry;
using IQIAIndicator.Engine.EntryTrigger;

namespace IQIAIndicator.Engine.Visualization;

public sealed class VisualizationAssessmentBuilder
{
    private readonly List<string> _diagnostics = new();
    private readonly List<string> _displayReasons = new();
    private readonly List<string> _warnings = new();
    private DisplayStatus _displayStatus;
    private int _displayPriority;

    public void Populate(EntryTriggerCandidate entryTriggerCandidate)
    {
        _diagnostics.Clear();
        _displayReasons.Clear();
        _warnings.Clear();
        _displayStatus = DisplayStatus.HIDDEN;
        _displayPriority = 0;

        if (entryTriggerCandidate is null)
        {
            _warnings.Add("EntryTriggerCandidate missing.");
            _diagnostics.Add("Visualization assessment could not be populated.");
            return;
        }

        _displayStatus = ToDisplayStatus(entryTriggerCandidate.EntryCandidate.OpportunityStatus);
        _displayPriority = ToDisplayPriority(_displayStatus);

        if (entryTriggerCandidate.EntryCandidate.OpportunityReasons is not null && entryTriggerCandidate.EntryCandidate.OpportunityReasons.Count > 0)
        {
            _displayReasons.AddRange(entryTriggerCandidate.EntryCandidate.OpportunityReasons.Where(reason => !string.IsNullOrWhiteSpace(reason)));
        }

        if (entryTriggerCandidate.Warnings is not null && entryTriggerCandidate.Warnings.Count > 0)
        {
            _warnings.AddRange(entryTriggerCandidate.Warnings.Where(warning => !string.IsNullOrWhiteSpace(warning)));
        }

        if (entryTriggerCandidate.Diagnostics is not null && entryTriggerCandidate.Diagnostics.Count > 0)
        {
            _diagnostics.AddRange(entryTriggerCandidate.Diagnostics.Where(diagnostic => !string.IsNullOrWhiteSpace(diagnostic)));
        }
    }

    public VisualizationAssessment Build()
        => new(
            _displayStatus,
            _displayPriority,
            _displayReasons.AsReadOnly(),
            _warnings.AsReadOnly(),
            _diagnostics.AsReadOnly());

    private static DisplayStatus ToDisplayStatus(OpportunityStatus opportunityStatus)
        => opportunityStatus switch
        {
            OpportunityStatus.WATCHLIST => DisplayStatus.VISIBLE,
            OpportunityStatus.QUALIFIED => DisplayStatus.HIGHLIGHTED,
            OpportunityStatus.HIGH_PRIORITY => DisplayStatus.FEATURED,
            _ => DisplayStatus.HIDDEN
        };

    private static int ToDisplayPriority(DisplayStatus displayStatus)
        => displayStatus switch
        {
            DisplayStatus.VISIBLE => 1,
            DisplayStatus.HIGHLIGHTED => 2,
            DisplayStatus.FEATURED => 3,
            _ => 0
        };
}
