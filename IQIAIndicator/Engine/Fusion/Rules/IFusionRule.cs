using IQIAIndicator.Engine.Fusion.Core;

namespace IQIAIndicator.Engine.Fusion.Rules;

/// <summary>
/// Contrat d'une future règle de fusion scientifique.
/// </summary>
public interface IFusionRule
{
    string Name { get; }

    void Evaluate(FusionContext context, FusionResultBuilder builder);
}