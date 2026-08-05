namespace IQIAIndicator.Engine.Regime.Evidence;

/// <summary>
/// Résultat brut du test CUSUM (détection de rupture structurelle).
/// Aucune décision de régime — observation statistique uniquement.
/// </summary>
public sealed class CusumResult
{
    public required decimal SPlus      { get; init; }  // statistique CUSUM haussière
    public required decimal SMinus     { get; init; }  // statistique CUSUM baissière
    public required bool    HasBreak   { get; init; }  // max(S+,S-) dépasse le seuil
    public required decimal Confidence { get; init; }
    public required bool    IsValid    { get; init; }
    public required string  Explanation { get; init; }

    public static CusumResult Invalid(string reason) => new()
    {
        SPlus = 0m, SMinus = 0m, HasBreak = false,
        Confidence = 0m, IsValid = false, Explanation = reason
    };
}
