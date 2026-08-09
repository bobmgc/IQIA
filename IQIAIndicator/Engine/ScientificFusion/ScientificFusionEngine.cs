using System;
using System.Collections.Generic;
using System.Linq;
using IQIAIndicator.Engine.ScientificFusion;
using IQIAIndicator.Engine.ScientificModels.Abstractions;

namespace IQIAIndicator.Engine.ScientificFusion;

public sealed class ScientificFusionEngine
{
    public ScientificAssessment Assess(IReadOnlyList<ScientificModelResult> scientificResults)
    {
        var builder = new ScientificAssessmentBuilder();
        builder.Populate(scientificResults);
        return builder.Build();
    }
}
