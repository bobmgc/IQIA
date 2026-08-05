namespace IQIAIndicator.Engine.Regime.Evidence;

/// <summary>
/// Emplacement réservé pour le test de Bai-Perron (détection de ruptures multiples).
/// Non implémenté — Sprint 2.4 (DFA).
/// </summary>
public sealed class BaiPerronResult
{
    public required bool   IsValid     { get; init; }
    public required string Explanation { get; init; }

    // Toujours invalide jusqu'au Sprint 2.4
    public static readonly BaiPerronResult NotImplemented = new()
    {
        IsValid = false, Explanation = "Bai-Perron non implémenté (Sprint 2.4)."
    };
}
