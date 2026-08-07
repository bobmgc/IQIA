namespace IQIAIndicator.Engine.Methodology.Core;

public sealed record QuantitativeMethodology(
    string Name,
    string Description,
    string PrimaryModel,
    IReadOnlyList<string> SupportingModels,
    string ValidationModel,
    IReadOnlyList<string> CompatibleBehaviours,
    string Version,
    IReadOnlyList<string> Extensions);
