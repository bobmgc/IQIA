using ATAS.DataFeedsCore;

namespace IQIAIndicator.Infrastructure.ATAS;

/// <summary>
/// Sprint 15.25 (Lot 12.6). NOT WIRED INTO IQIAIndicator.cs - investigated and REJECTED for active use;
/// kept only as a tested reference for a future lot. QuantityStep remains exclusively the manual
/// parameter (unchanged since Lot 12.2), exactly as before this lot.
///
/// Lot 12.1/12.4 established, by reflection against the installed ATAS assemblies, that
/// <c>Security</c> has no property literally named QuantityStep/VolumeStep/LotStep/ContractSize. A real
/// MES Replay capture (Lot 12.6 brief) showed <c>Security.LotMinSize</c>/<c>LotMaxSize</c> genuinely
/// unavailable for this feed, while <c>Security.LotSize</c> (a separate, non-nullable property) reported
/// 1 - plausibly the correct MES order-quantity increment, and ATAS's own SDK
/// (<c>IDataFeedConnector.ConvertCurrency(..., roundToLotSize)</c>, confirmed by reflection) already uses
/// <c>LotSize</c> internally as a quantity-rounding increment, the same role QuantityStep plays in
/// RiskEngine's own position sizing.
///
/// That evidence looked strong enough to implement - until this class's own test suite
/// (Tests/Infrastructure/ATAS/ATASInstrumentQuantityStepResolverTests.cs) caught, empirically (by
/// reflecting a freshly-constructed <c>new Security()</c>), that <c>LotSize</c> DEFAULTS TO 1 even when
/// never populated by any connector - indistinguishable from a real feed genuinely reporting "1" for
/// MES. Wiring <c>Resolve</c>'s output into <c>ATASInstrumentAdapter.Build</c>'s <c>quantityStep</c>
/// argument would therefore have silently reproduced exactly what the Lot 12.6 brief explicitly forbids
/// ("QuantityStep = 1 ... simplement pour faire passer IsValid à true") for every instrument whose feed
/// does not explicitly populate LotSize - without ever writing that literal value anywhere. This is the
/// STOP RULE's own condition ("une API ATAS n'est pas suffisamment certaine pour être utilisée") - not
/// wired in; see the Lot 12.6 report for the full account. Contrast with
/// <c>Security.LotMinSize</c>/<c>LotMaxSize</c> (nullable, default to <c>null</c> - unambiguous), which
/// is exactly why ATASInstrumentAdapter.Build's existing MinQuantity/MaxQuantity fallback (Lot 12.2,
/// protected, unchanged) does not share this problem and needed no reconsideration.
///
/// Kept, unused, so a future lot has a tested starting point IF a way to reliably distinguish a
/// genuinely-populated LotSize from the SDK's own default is found (e.g. an ATAS "is populated" flag, or
/// cross-referencing multiple ATAS-reported fields). Symbol-agnostic: never branches on Security.Instrument.
/// Pure, deterministic, independently testable without instantiating IQIAIndicator (which requires a live
/// ATAS host and cannot be unit-tested).
/// </summary>
public static class ATASInstrumentQuantityStepResolver
{
    public const string AtasLotSizeSource = "ATAS.LotSize";
    public const string ManualSource = "Manual";

    /// <summary>Resolved QuantityStep and a label naming exactly which source produced it. NOT called by
    /// IQIAIndicator.cs - see the class doc comment for why.</summary>
    public static (int QuantityStep, string Source) Resolve(Security? security, int manualQuantityStep)
    {
        if (security?.LotSize is decimal lotSize && lotSize > 0m)
            return ((int)lotSize, AtasLotSizeSource);

        return (manualQuantityStep, ManualSource);
    }
}
