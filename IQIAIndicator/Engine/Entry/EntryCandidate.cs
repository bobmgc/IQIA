using System;
using System.Collections.Generic;

namespace IQIAIndicator.Engine.Entry;

public sealed record EntryCandidate(
    EntryAssessment Assessment,
    DateTime CreatedAt,
    OpportunityStatus OpportunityStatus,
    double OpportunityPriority,
    IReadOnlyList<string> OpportunityReasons,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Diagnostics);
