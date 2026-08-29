using IQIAIndicator.Engine.Decision.Core;
using IQIAIndicator.Engine.Methodology.Core;
using IQIAIndicator.Engine.ScientificModels.Abstractions;

namespace IQIAIndicator.Engine.Signal;

public sealed record SignalContext(
    DecisionResult DecisionResult,
    MethodologySelection MethodologySelection,
    ScientificModelContext ScientificModelContext);
