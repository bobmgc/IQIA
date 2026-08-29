using IQIAIndicator.Engine.Methodology.Core;
using IQIAIndicator.Engine.ScientificModels.Abstractions;

namespace IQIAIndicator.Engine.Signal;

public sealed record SignalCandidate(
    DateTime Timestamp,
    QuantitativeMethodology Methodology,
    IReadOnlyList<ScientificModelResult> ScientificResults,
    string Explanation);
