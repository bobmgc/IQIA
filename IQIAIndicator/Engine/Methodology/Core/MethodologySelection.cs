using IQIAIndicator.Engine.Decision.Core;

namespace IQIAIndicator.Engine.Methodology.Core;

public sealed record MethodologySelection(
    DecisionResult DecisionResult,
    QuantitativeMethodology SelectedMethodology,
    DateTime Timestamp,
    string Version,
    string Explanation);
