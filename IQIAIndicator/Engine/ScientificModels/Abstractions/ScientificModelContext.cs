using IQIAIndicator.Engine.Decision.Core;
using IQIAIndicator.Engine.Methodology.Core;

namespace IQIAIndicator.Engine.ScientificModels.Abstractions;

public sealed record ScientificModelContext(
    MarketContext MarketContext,
    DecisionResult DecisionResult,
    MethodologySelection MethodologySelection);
