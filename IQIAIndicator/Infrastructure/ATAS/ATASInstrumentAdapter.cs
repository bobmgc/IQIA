using ATAS.DataFeedsCore;
using IQIAIndicator.Engine.Risk;

namespace IQIAIndicator.Infrastructure.ATAS;

/// <summary>
/// Sprint 15.25 (Lot 12.2). Read-only mapping from ATAS's own <see cref="Security"/> (confirmed by the
/// Lot 12.1 reflection audit: Instrument/TickSize/TickCost/LotSize/LotMinSize/LotMaxSize/Digits, on
/// <c>Indicator.TradingManager.Security</c>) to <see cref="InstrumentRiskSpecification"/> (Lot 10,
/// unchanged). Contains no strategy/risk logic - only field renaming and one documented derived value.
///
/// Never branches on Symbol - every field comes from the Security instance actually supplied for the
/// chart's current instrument, whatever it is (ES, MES, or any future instrument). PointValue is not a
/// native ATAS field (Lot 12.1, Section 4): derived as TickCost / TickSize, the same relationship
/// already implicit in IQIA's pre-Lot-12.2 manual ES defaults (12.50 / 0.25 = 50).
///
/// QuantityStep has no ATAS equivalent (Lot 12.1, Section 11) - never fabricated; the caller's existing
/// manual value (RiskInstrumentQuantityStep, Lot 11) is always used unchanged. MinQuantity/MaxQuantity
/// prefer Security.LotMinSize/LotMaxSize when ATAS reports them (&gt; 0), falling back to the caller's
/// existing manual RiskInstrumentMinQuantity/MaxQuantity (Lot 11) otherwise - not a fabricated value, the
/// same explicit user configuration that already existed before this lot.
///
/// Always returns a value (never null): when Security itself, or a mandatory field, is unavailable, the
/// missing pieces surface as their neutral "unconfigured" sentinel (empty Symbol, 0 TickSize/TickValue/
/// PointValue) - InstrumentRiskSpecification.IsValid (Lot 10, unchanged) already turns that into
/// INSTRUMENT_SPEC_INVALID via RiskEngine.Evaluate, exactly the fail-closed outcome Lot 12.2 Section 12
/// requires. This mirrors ATASAccountStateAdapter.Build's identical convention for CurrentEquity.
/// </summary>
public static class ATASInstrumentAdapter
{
    public static InstrumentRiskSpecification Build(
        Security? security,
        int fallbackMinQuantity,
        int fallbackMaxQuantity,
        int quantityStep)
    {
        string symbol = security?.Instrument ?? string.Empty;
        decimal tickSize = security?.TickSize ?? 0m;
        decimal tickValue = security?.TickCost ?? 0m;
        decimal pointValue = tickSize > 0m && tickValue > 0m ? tickValue / tickSize : 0m;

        int minQuantity = security?.LotMinSize is decimal lotMin && lotMin > 0m ? (int)lotMin : fallbackMinQuantity;
        int maxQuantity = security?.LotMaxSize is decimal lotMax && lotMax > 0m ? (int)lotMax : fallbackMaxQuantity;

        return new InstrumentRiskSpecification(symbol, tickSize, tickValue, pointValue, minQuantity, maxQuantity, quantityStep);
    }
}
