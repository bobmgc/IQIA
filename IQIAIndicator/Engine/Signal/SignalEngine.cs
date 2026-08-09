using IQIAIndicator.Engine.Decision.Core;
using IQIAIndicator.Engine.Methodology.Core;
using IQIAIndicator.Engine.ScientificModels.Abstractions;
using IQIAIndicator.Engine.ScientificModels.Registry;

namespace IQIAIndicator.Engine.Signal;

public sealed class SignalEngine
{
    private readonly ScientificModelRegistry _registry;

    public SignalEngine()
    {
        _registry = new ScientificModelRegistry();
    }

    public SignalCandidate Evaluate(MethodologySelection methodologySelection)
    {
        ArgumentNullException.ThrowIfNull(methodologySelection);

        IReadOnlyList<IScientificModel> activeModels = _registry.Resolve(methodologySelection);
        return EvaluateInternal(methodologySelection, activeModels);
    }

    public SignalCandidate Evaluate(MethodologySelection methodologySelection, IReadOnlyList<IScientificModel> models)
    {
        ArgumentNullException.ThrowIfNull(methodologySelection);
        ArgumentNullException.ThrowIfNull(models);

        return EvaluateInternal(methodologySelection, models);
    }

    private SignalCandidate EvaluateInternal(
        MethodologySelection methodologySelection,
        IReadOnlyList<IScientificModel> models)
    {
        ArgumentNullException.ThrowIfNull(methodologySelection);
        ArgumentNullException.ThrowIfNull(models);

        var context = new ScientificModelContext(
            new MarketContext(
                DateTime.UtcNow,
                0m,
                Array.Empty<decimal>()),
            methodologySelection.DecisionResult,
            methodologySelection,
            Array.Empty<ScientificModelResult>());

        var signalContext = new SignalContext(
            methodologySelection.DecisionResult,
            methodologySelection,
            context);

        var builder = new SignalResultBuilder();
        var scientificResults = new List<ScientificModelResult>();
        var currentContext = context;

        foreach (IScientificModel model in models)
        {
            signalContext = signalContext with { ScientificModelContext = currentContext };
            ScientificModelResult result = model.Evaluate(signalContext.ScientificModelContext);
            scientificResults.Add(result);
            currentContext = currentContext with { ScientificResults = scientificResults.AsReadOnly() };
            builder.AddScientificResult(result);
        }

        return builder.Build(
            DateTime.UtcNow,
            methodologySelection.SelectedMethodology,
            $"Signal pipeline executed for {methodologySelection.DecisionResult.Winner} using {models.Count} scientific model(s)");
    }
}
