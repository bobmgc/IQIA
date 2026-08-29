using IQIAIndicator.Engine.Decision.Core;

namespace IQIAIndicator.Engine.Methodology.Core;

public sealed class MethodologyEngine
{
    private readonly MethodologyRegistry _registry = new();

    public MethodologySelection Evaluate(DecisionResult decisionResult)
    {
        ArgumentNullException.ThrowIfNull(decisionResult);

        QuantitativeMethodology methodology = _registry.Resolve(decisionResult.Winner);

        return new MethodologySelection(
            decisionResult,
            methodology,
            DateTime.UtcNow,
            methodology.Version,
            $"Methodology selected for {decisionResult.Winner} via MethodologyRegistry");
    }
}
