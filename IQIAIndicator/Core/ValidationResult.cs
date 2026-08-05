namespace IQIAIndicator.Core;

/// <summary>Résultat de la validation d'un MarketContext.</summary>
public sealed class ValidationResult
{
    public required bool                  IsValid   { get; init; }
    public required IReadOnlyList<string> Errors    { get; init; }
    public required IReadOnlyList<string> Warnings  { get; init; }

    /// <summary>Résultat valide sans erreur ni avertissement.</summary>
    public static ValidationResult Ok() => new()
    {
        IsValid  = true,
        Errors   = Array.Empty<string>(),
        Warnings = Array.Empty<string>()
    };
}
