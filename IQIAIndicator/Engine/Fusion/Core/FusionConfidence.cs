namespace IQIAIndicator.Engine.Fusion.Core;

/// <summary>
/// Confiance associée à une dimension scientifique de fusion.
/// </summary>
public sealed record FusionConfidence
{
    public double Value { get; init; } = 0.0;

    public double Confidence { get; init; } = 0.0;

    public string Explanation { get; init; } = string.Empty;

    /// <summary>
    /// True when this dimension reflects a genuine measurement; false when it is a placeholder
    /// (e.g. the "Missing Evidence" sentinel) standing in for evidence that could not be computed.
    /// Consumers must check this before treating Value as informative - a Value of 0.0 on an
    /// unavailable dimension means "we don't know", not "we measured zero" (see Sprint 14 / audit
    /// findings DEC-01, FUS-02). Defaults to true so every existing construction site that reports a
    /// real measurement is unaffected.
    /// </summary>
    public bool IsAvailable { get; init; } = true;
}
