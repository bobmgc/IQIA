using System;
using System.Collections.Generic;
using IQIAIndicator.Engine.Entry;

namespace IQIAIndicator.Engine.EntryTrigger;

// Business-only context for EntryTrigger. No presentation strings.
public sealed record EntryBusinessContext(
    decimal CurrentPrice,
    double? EstimatedEquilibrium,
    double? DistanceToEquilibrium,
    double? DynamicZScore,
    double ScientificConfidence,
    double OpportunityPriority,
    string? Methodology,
    DateTime Timestamp,
    IReadOnlyList<string> SupportingEvidence,
    IReadOnlyList<string> BlockingIssues,
    IReadOnlyList<string> Diagnostics,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> OpportunityReasons,
    OpportunityStatus OpportunityStatus);
