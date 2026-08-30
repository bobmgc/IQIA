using System;
using System.Collections.Generic;
using System.Linq;
using IQIAIndicator.Engine.ScientificFusion;
using IQIAIndicator.Engine.ScientificModels.Abstractions;

namespace IQIAIndicator.Engine.ScientificFusion;

public sealed class ScientificFusionEngine
{
    public ScientificAssessment Assess(IReadOnlyList<ScientificModelResult> scientificResults)
        => Assess(scientificResults, null);

    /// <summary>Audit 2026-08-30 (P0-2). <paramref name="expectedModelNames"/> = the models the
    /// registry resolved for this bar's methodology; null keeps the legacy MeanReversion expected
    /// set (back-compat for direct callers/tests).</summary>
    public ScientificAssessment Assess(
        IReadOnlyList<ScientificModelResult> scientificResults,
        IReadOnlyList<string>? expectedModelNames)
    {
        var builder = new ScientificAssessmentBuilder();
        builder.Populate(scientificResults, expectedModelNames);
        return builder.Build();
    }
}
