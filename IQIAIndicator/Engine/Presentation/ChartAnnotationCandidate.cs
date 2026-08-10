using System;
using System.Collections.Generic;

namespace IQIAIndicator.Engine.Presentation;

public sealed record ChartAnnotationCandidate(
    IReadOnlyList<ChartAnnotation> Annotations,
    DateTime CreatedAt,
    IReadOnlyList<string> Diagnostics,
    IReadOnlyList<string> Warnings);
