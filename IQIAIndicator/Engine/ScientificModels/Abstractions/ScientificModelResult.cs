using System.Collections.Generic;

namespace IQIAIndicator.Engine.ScientificModels.Abstractions;

public sealed record ScientificModelResult(
    string ModelName,
    bool Success,
    double Score,
    string Explanation,
    IReadOnlyDictionary<string, object>? Metrics = null);
