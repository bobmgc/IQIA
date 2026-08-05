using IQIAIndicator.Core;
using IQIAIndicator.Engine.Regime.Core;
using IQIAIndicator.Engine.Regime.Evidence;
using IQIAIndicator.Engine.Regime.Evidence.ADF;
using IQIAIndicator.Engine.Regime.Evidence.KPSS;

namespace IQIAIndicator.Engine.Regime;

/// <summary>
/// Orchestrateur pur : appelle chaque modele et assemble l'EvidenceSet.
/// Aucune logique metier. Aucune decision de regime.
/// </summary>
public sealed class RegimeEngine
{
    private readonly AdfEvidence           _adf   = new();
    private readonly KpssEvidence          _kpss  = new();
    private readonly HurstEvidence         _hurst = new();
    private readonly HalfLifeEvidence      _hl    = new();
    private readonly VarianceRatioEvidence _vr    = new();
    private readonly CusumEvidence         _cusum = new();
    private readonly VolatilityEvidence    _vol   = new();
    private readonly DfaEvidence           _dfa   = new();

    public EvidenceSet Collect(MarketContext context) => new()
    {
        Timestamp     = context.Clock.CurrentTime,
        Adf           = _adf.Compute(context),
        Kpss          = _kpss.Compute(context),
        Hurst         = _hurst.Compute(context),
        HalfLife      = _hl.Compute(context),
        VarianceRatio = _vr.Compute(context),
        Cusum         = _cusum.Compute(context),
        Volatility    = _vol.Compute(context),
        Dfa           = _dfa.Compute(context),
        BaiPerron     = null
    };
}
