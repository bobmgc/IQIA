using IQIAIndicator.Engine.Methodology.Core;
using IQIAIndicator.Engine.ScientificModels.Abstractions;

namespace IQIAIndicator.Engine.Signal;

public sealed class SignalResultBuilder
{
    private readonly List<ScientificModelResult> _scientificResults = new();

    public void AddScientificResult(ScientificModelResult result)
    {
        _scientificResults.Add(result);
    }

    public SignalCandidate Build(
        DateTime timestamp,
        QuantitativeMethodology methodology,
        string explanation)
    {
        return new SignalCandidate(
            timestamp,
            methodology,
            _scientificResults.AsReadOnly(),
            explanation);
    }
}
