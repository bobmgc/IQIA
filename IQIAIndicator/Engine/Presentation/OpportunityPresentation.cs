using System;
using System.Collections.Immutable;

namespace IQIAIndicator.Engine.Presentation;

public sealed record OpportunityPresentation(
    string Title,
    string Subtitle,
    string OpportunityStatus,
    int OpportunityPriority,
    string SignalLabel,
    string RiskLabel,
    string ScientificSummary,
    ImmutableArray<string> SupportingEvidence,
    ImmutableArray<string> BlockingIssues,
    ImmutableArray<string> Warnings,
    ImmutableArray<string> Diagnostics,
    ImmutableDictionary<string, object> Metrics,
    DateTime CreatedAt)
{
    public ImmutableDictionary<string, object> ToDictionary()
        => ImmutableDictionary<string, object>.Empty
            .Add("Title", Title)
            .Add("Subtitle", Subtitle)
            .Add("OpportunityStatus", OpportunityStatus)
            .Add("OpportunityPriority", OpportunityPriority)
            .Add("SignalLabel", SignalLabel)
            .Add("RiskLabel", RiskLabel)
            .Add("ScientificSummary", ScientificSummary)
            .Add("SupportingEvidence", SupportingEvidence)
            .Add("BlockingIssues", BlockingIssues)
            .Add("Warnings", Warnings)
            .Add("Diagnostics", Diagnostics)
            .Add("Metrics", Metrics)
            .Add("CreatedAt", CreatedAt);
}
