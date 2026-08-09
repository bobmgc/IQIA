using System.Collections.Generic;
using System.Collections.Immutable;
using IQIAIndicator.Engine.ScientificModels.Abstractions;

namespace IQIAIndicator.Engine.ScientificFusion;

public sealed record ScientificAssessment(
    double OverallConfidence,
    IReadOnlyList<string> EvidenceAgreement,
    IReadOnlyList<string> EvidenceConflict,
    IReadOnlyList<string> MissingEvidence,
    IReadOnlyList<string> ExecutedModels,
    IReadOnlyList<string> SuccessfulModels,
    IReadOnlyList<string> FailedModels,
    IReadOnlyList<ScientificModelResult> ScientificResults,
    string Diagnostics)
{
    public ImmutableDictionary<string, object> ToDictionary()
        => ImmutableDictionary<string, object>.Empty
            .Add("OverallConfidence", OverallConfidence)
            .Add("EvidenceAgreement", EvidenceAgreement)
            .Add("EvidenceConflict", EvidenceConflict)
            .Add("MissingEvidence", MissingEvidence)
            .Add("ExecutedModels", ExecutedModels)
            .Add("SuccessfulModels", SuccessfulModels)
            .Add("FailedModels", FailedModels)
            .Add("ScientificResults", ScientificResults)
            .Add("Diagnostics", Diagnostics);
}
