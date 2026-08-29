using System.Collections.Generic;
using System.Collections.Immutable;
using IQIAIndicator.Engine.ScientificModels.Abstractions;

namespace IQIAIndicator.Engine.ScientificFusion;

/// <summary>
/// Distinguishes WHY a scientific assessment has no successful model evidence, so "no model is
/// registered for the current regime" (a structural pipeline gap - see Sprint 14 / audit finding
/// ARC-003) is never displayed or reasoned about as if it were "the registered models ran and found
/// insufficient data" (a per-model, potentially transient, condition). The latter is already fully
/// distinguishable via ExecutedModels/SuccessfulModels/FailedModels/MissingEvidence; this adds the
/// one distinction that was previously impossible to make: whether any model was ever registered to
/// run at all for the current methodology.
/// </summary>
public enum ScientificCoverageStatus
{
    /// <summary>At least one scientific model was registered for the current methodology and was executed (its own Success/Explanation report the per-model outcome).</summary>
    ModelsExecuted,

    /// <summary>No scientific model is registered for the current methodology; the model registry returned an empty set, so nothing was executed.</summary>
    NoModelCoverage
}

public sealed record ScientificAssessment(
    double OverallConfidence,
    IReadOnlyList<string> EvidenceAgreement,
    IReadOnlyList<string> EvidenceConflict,
    IReadOnlyList<string> MissingEvidence,
    IReadOnlyList<string> ExecutedModels,
    IReadOnlyList<string> SuccessfulModels,
    IReadOnlyList<string> FailedModels,
    IReadOnlyList<ScientificModelResult> ScientificResults,
    string Diagnostics,
    ScientificCoverageStatus CoverageStatus = ScientificCoverageStatus.NoModelCoverage)
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
            .Add("Diagnostics", Diagnostics)
            .Add("CoverageStatus", CoverageStatus.ToString());
}
