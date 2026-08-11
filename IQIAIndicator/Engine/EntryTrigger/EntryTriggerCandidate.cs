using System;
using System.Collections.Generic;
using IQIAIndicator.Engine.Entry;

namespace IQIAIndicator.Engine.EntryTrigger;

public sealed record EntryTriggerCandidate(
    EntryTriggerAssessment Assessment,
    EntryCandidate EntryCandidate,
    decimal CurrentPrice,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Diagnostics,
    DateTime CreatedAt);
