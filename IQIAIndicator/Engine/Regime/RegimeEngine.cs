using IQIAIndicator.Core;
using IQIAIndicator.Engine.Regime.Core;
using IQIAIndicator.Engine.Regime.Evidence;
using IQIAIndicator.Engine.Regime.Evidence.ADF;
using IQIAIndicator.Engine.Regime.Evidence.HalfLife;
using IQIAIndicator.Engine.Regime.Evidence.KPSS;
using IQIAIndicator.Engine.Regime.Evidence.VarianceRatio;
using IQIAIndicator.Engine.Regime.Evidence.CUSUM;
using IQIAIndicator.Engine.Regime.Evidence.BaiPerron;

namespace IQIAIndicator.Engine.Regime;

/// <summary>
/// Orchestrateur pur : appelle chaque modele et assemble l'EvidenceSet.
/// Aucune logique metier. Aucune decision de regime.
/// </summary>
public sealed class RegimeEngine
{
    private const int AdfKpssWindowSize = 60;
    private const int AdfKpssMinimumSampleSize = 30;
    private const int DfaWindowSize = 128;
    private const int DfaMinimumSampleSize = 80;
    private const int HalfLifeWindowSize = 30;
    private const int HalfLifeMinimumSampleSize = 20;
    private const int VarianceRatioWindowSize = 30;
    private const int VarianceRatioMinimumSampleSize = 20;
    private const int CusumWindowSize = 30;
    private const int CusumMinimumSampleSize = 20;
    private const int BaiPerronWindowSize = 128;
    private const int BaiPerronMinimumSampleSize = 64;

    private readonly AdfEvidence           _adf   = new();
    private readonly KpssEvidence          _kpss  = new();
    private readonly HurstEvidence         _hurst = new();
    private readonly HalfLifeEvidence      _hl    = new();
    private readonly VarianceRatioEvidence _vr    = new();
    private readonly CusumEvidence         _cusum = new();
    private readonly BaiPerronEvidence     _baiPerron = new();
    private readonly VolatilityEvidence    _vol   = new();
    private readonly DfaEvidence           _dfa   = new();
    private readonly decimal[] _priceBuffer = new decimal[DfaWindowSize];
    private int _priceBufferHead;
    private int _priceBufferCount;

    public EvidenceSet Collect(MarketContext context)
    {
        if (context.Clock.IsFirstBar)
        {
            _priceBufferHead = _priceBufferCount = 0;
            Array.Clear(_priceBuffer);
        }

        _priceBuffer[_priceBufferHead] = context.Price.Close;
        _priceBufferHead = (_priceBufferHead + 1) % DfaWindowSize;
        _priceBufferCount = Math.Min(_priceBufferCount + 1, DfaWindowSize);

        EvidenceContext adfKpssContext = BuildEvidenceContext(
            context,
            Math.Min(_priceBufferCount, AdfKpssWindowSize),
            AdfKpssMinimumSampleSize,
            AdfKpssWindowSize);
        EvidenceContext dfaContext = BuildEvidenceContext(
            context,
            _priceBufferCount,
            DfaMinimumSampleSize,
            DfaWindowSize);
        EvidenceContext halfLifeContext = BuildEvidenceContext(
            context,
            Math.Min(_priceBufferCount, HalfLifeWindowSize),
            HalfLifeMinimumSampleSize,
            HalfLifeWindowSize);
        EvidenceContext varianceRatioContext = BuildEvidenceContext(
            context,
            Math.Min(_priceBufferCount, VarianceRatioWindowSize),
            VarianceRatioMinimumSampleSize,
            VarianceRatioWindowSize);
        EvidenceContext cusumContext = BuildEvidenceContext(
            context,
            Math.Min(_priceBufferCount, CusumWindowSize),
            CusumMinimumSampleSize,
            CusumWindowSize);
        EvidenceContext baiPerronContext = BuildEvidenceContext(
            context,
            _priceBufferCount,
            BaiPerronMinimumSampleSize,
            BaiPerronWindowSize);

        return new EvidenceSet
        {
            Timestamp     = context.Clock.CurrentTime,
            Adf           = _adf.Compute(adfKpssContext),
            Kpss          = _kpss.Compute(adfKpssContext),
            Hurst         = _hurst.Compute(context),
            HalfLife      = _hl.Compute(halfLifeContext),
            VarianceRatio = _vr.Compute(varianceRatioContext),
            Cusum         = _cusum.Compute(cusumContext),
            Volatility    = _vol.Compute(context),
            Dfa           = _dfa.Compute(dfaContext),
            BaiPerron     = _baiPerron.Compute(baiPerronContext)
        };
    }

    private EvidenceContext BuildEvidenceContext(
        MarketContext marketContext,
        int sampleSize,
        int minimumSampleSize,
        int windowSize)
    {
        var series = new decimal[sampleSize];
        for (int i = 0; i < sampleSize; i++)
            series[i] = _priceBuffer[(_priceBufferHead - sampleSize + i + DfaWindowSize) % DfaWindowSize];

        return new EvidenceContext
        {
            Series = series,
            SampleSize = sampleSize,
            MinimumSampleSize = minimumSampleSize,
            WindowSize = windowSize,
            Timestamp = marketContext.Clock.CurrentTime,
            Symbol = marketContext.Instrument.Symbol,
            TimeFrame = marketContext.TimeFrame
        };
    }
}
