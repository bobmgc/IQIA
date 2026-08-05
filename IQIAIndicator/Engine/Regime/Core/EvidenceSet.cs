using IQIAIndicator.Engine.Regime.Evidence;
using IQIAIndicator.Engine.Regime.Evidence.ADF;
using IQIAIndicator.Engine.Regime.Evidence.KPSS;
using IQIAIndicator.Engine.Regime.Evidence.DFA;

namespace IQIAIndicator.Engine.Regime.Core;

/// <summary>
/// Conteneur immuable de toutes les évidences statistiques produites pour un bar.
/// Chaque propriété est typée : les moteurs aval lisent des observations brutes,
/// jamais des décisions de régime.
/// Les propriétés peuvent être null si le modèle est en warmup ou non implémenté.
/// </summary>
public sealed class EvidenceSet
{
    public required DateTime              Timestamp     { get; init; }
    public required AdfResult?            Adf           { get; init; }
    public required KpssResult?           Kpss          { get; init; }
    public required HurstResult?          Hurst         { get; init; }
    public required HalfLifeResult?       HalfLife      { get; init; }
    public required VarianceRatioResult?  VarianceRatio { get; init; }
    public required CusumResult?          Cusum         { get; init; }
    public required VolatilityResult?     Volatility    { get; init; }
    public          BaiPerronResult?      BaiPerron     { get; init; }  // null jusqu'au Sprint 2.4
    public required DfaResult?            Dfa           { get; init; }
}
