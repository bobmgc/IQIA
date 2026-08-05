namespace IQIAIndicator.Core;

/// <summary>
/// État d'exécution de l'indicateur pour le bar courant.
/// Permet aux moteurs de distinguer historique, temps réel et replay.
/// </summary>
public sealed class ExecutionContext
{
    public required int  CurrentBar        { get; init; }
    public required int  LastCalculatedBar { get; init; }
    public required bool IsRealtime        { get; init; }
    public required bool IsHistorical      { get; init; }
    public required bool IsReplay          { get; init; }
}
