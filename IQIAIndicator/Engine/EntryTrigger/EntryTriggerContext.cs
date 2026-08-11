using IQIAIndicator.Engine.Entry;
using IQIAIndicator.Engine.Methodology.Core;
using IQIAIndicator.Engine.ScientificFusion;

namespace IQIAIndicator.Engine.EntryTrigger;

public sealed record EntryTriggerContext(
    EntryBusinessContext BusinessContext,
    ScientificAssessment ScientificAssessment,
    EntryAssessment EntryAssessment,
    EntryCandidate EntryCandidate,
    MethodologySelection? MethodologySelection = null);
