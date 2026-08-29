using System;
using System.Collections.Generic;

namespace IQIAIndicator.Engine.Visualization;

public sealed record VisualizationCandidate(
    VisualizationAssessment Assessment,
    DateTime CreatedAt,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Diagnostics);
