namespace IQIAIndicator.Core;

/// <summary>
/// Centralise toutes les informations temporelles du bar courant.
/// Source de vérité unique pour les moteurs qui dépendent du temps.
/// </summary>
public sealed class MarketClock
{
    public required DateTime    CurrentTime    { get; init; }
    public required DateOnly    CurrentDate    { get; init; }
    public required DayOfWeek   DayOfWeek      { get; init; }
    public required SessionInfo Session        { get; init; }
    public required int         ElapsedMinutes { get; init; }
    public required bool        IsFirstBar     { get; init; }
    public required bool        IsLastBar      { get; init; }
}
