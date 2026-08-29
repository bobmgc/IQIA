using System.Collections.Generic;
using System.Collections.Immutable;
using IQIAIndicator.Engine.ScientificFusion;

namespace IQIAIndicator.Engine.Entry;
public enum EntryReadiness
{
    INSUFFICIENT_EVIDENCE,
    PARTIAL,
    READY_FOR_NEXT_STAGE
}

public enum OpportunityStatus
{
    NOT_QUALIFIED,
    WATCHLIST,
    QUALIFIED,
    HIGH_PRIORITY
}

public sealed record EntryAssessment(
    ScientificAssessment ScientificAssessment,
    double AssessmentQuality,
    EntryReadiness EntryReadiness,
    OpportunityStatus OpportunityStatus,
    double OpportunityPriority,
    IReadOnlyList<string> OpportunityReasons,
    IReadOnlyList<string> BlockingIssues,
    IReadOnlyList<string> SupportingEvidence,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Diagnostics)
{
    public ImmutableDictionary<string, object> ToDictionary()
        => ImmutableDictionary<string, object>.Empty
            .Add("ScientificAssessment", ScientificAssessment)
            .Add("AssessmentQuality", AssessmentQuality)
            .Add("EntryReadiness", EntryReadiness.ToString())
            .Add("OpportunityStatus", OpportunityStatus.ToString())
            .Add("OpportunityPriority", OpportunityPriority)
            .Add("OpportunityReasons", OpportunityReasons)
            .Add("BlockingIssues", BlockingIssues)
            .Add("SupportingEvidence", SupportingEvidence)
            .Add("Warnings", Warnings)
            .Add("Diagnostics", Diagnostics);
}
