using ATAS.DataFeedsCore;
using IQIAIndicator.Infrastructure.ATAS;
using Xunit;

namespace IQIAIndicator.Tests.Infrastructure.ATAS;

/// <summary>
/// Sprint 15.25 (Lot 12.6). Coverage for ATASInstrumentQuantityStepResolver - NOT wired into
/// IQIAIndicator.cs (see that class's doc comment for why: Security.LotSize defaults to 1 even when
/// never populated, indistinguishable from genuine data). These tests document the resolver's own,
/// internally-consistent logic (prefer a positive Security.LotSize, fall back to the manual parameter
/// otherwise, never invent, never branch on symbol) AND the exact discovery that disqualified it from
/// active use - both are preserved here as evidence for a future lot.
/// </summary>
public sealed class ATASInstrumentQuantityStepResolverTests
{
    private static Security BuildSecurity(string instrument, decimal? lotSize) =>
        lotSize is decimal ls
            ? new() { Instrument = instrument, LotSize = ls }
            : new() { Instrument = instrument };

    [Fact]
    public void Resolve_SecurityLotSizePositive_UsesAtasLotSize()
    {
        Security security = BuildSecurity("MES", lotSize: 1m);

        (int quantityStep, string source) = ATASInstrumentQuantityStepResolver.Resolve(security, manualQuantityStep: 0);

        Assert.Equal(1, quantityStep);
        Assert.Equal(ATASInstrumentQuantityStepResolver.AtasLotSizeSource, source);
    }

    [Fact]
    public void Resolve_DifferentPositiveLotSize_IsNotHardcodedToOne()
    {
        // Proves the resolver reads the REAL, per-instrument value rather than any constant - a
        // different security reporting LotSize=5 must resolve to 5, not 1.
        Security security = BuildSecurity("SOME-OTHER-INSTRUMENT", lotSize: 5m);

        (int quantityStep, string source) = ATASInstrumentQuantityStepResolver.Resolve(security, manualQuantityStep: 0);

        Assert.Equal(5, quantityStep);
        Assert.Equal(ATASInstrumentQuantityStepResolver.AtasLotSizeSource, source);
    }

    [Fact]
    public void Resolve_SecurityLotSizeZero_FallsBackToManualValue_NeverInventsOne()
    {
        Security security = BuildSecurity("MES", lotSize: 0m);

        (int quantityStep, string source) = ATASInstrumentQuantityStepResolver.Resolve(security, manualQuantityStep: 0);

        Assert.Equal(0, quantityStep);
        Assert.Equal(ATASInstrumentQuantityStepResolver.ManualSource, source);
    }

    [Fact]
    public void Resolve_SecurityNull_FallsBackToManualValue()
    {
        (int quantityStep, string source) = ATASInstrumentQuantityStepResolver.Resolve(null, manualQuantityStep: 3);

        Assert.Equal(3, quantityStep);
        Assert.Equal(ATASInstrumentQuantityStepResolver.ManualSource, source);
    }

    [Fact]
    public void Resolve_ManualValueUnconfigured_AtasUnavailable_StaysZero_NeverBecomesOne()
    {
        // Section 12.6 brief: "NE PAS utiliser QuantityStep = 1 ... simplement pour faire passer IsValid
        // à true". Confirms the resolver never substitutes 1 (or any constant) when both sources are
        // genuinely absent - 0 (unconfigured) must stay 0.
        (int quantityStep, string source) = ATASInstrumentQuantityStepResolver.Resolve(null, manualQuantityStep: 0);

        Assert.Equal(0, quantityStep);
        Assert.Equal(ATASInstrumentQuantityStepResolver.ManualSource, source);
    }

    [Theory]
    [InlineData("MES")]
    [InlineData("ES")]
    [InlineData("XYZ999")]
    public void Resolve_NeverBranchesOnSymbol(string symbol)
    {
        Security security = BuildSecurity(symbol, lotSize: 2m);

        (int quantityStep, string source) = ATASInstrumentQuantityStepResolver.Resolve(security, manualQuantityStep: 0);

        Assert.Equal(2, quantityStep);
        Assert.Equal(ATASInstrumentQuantityStepResolver.AtasLotSizeSource, source);
    }

    // ── Integration with ATASInstrumentAdapter.Build (Lot 12.2, protected, unchanged) ───────────────

    [Fact]
    public void MesWithAtasLotSizeAvailable_ProducesValidSpecification()
    {
        // Hypothetical only - documents what the resolver WOULD produce if it were ever wired in
        // (it is not - see class doc comment). Mirrors the real MES capture's field values.
        Security security = new() { Instrument = "MES", TickSize = 0.25m, TickCost = 1.25m, LotSize = 1m };

        (int effectiveQuantityStep, _) = ATASInstrumentQuantityStepResolver.Resolve(security, manualQuantityStep: 0);
        var spec = ATASInstrumentAdapter.Build(security, fallbackMinQuantity: 1, fallbackMaxQuantity: 50, quantityStep: effectiveQuantityStep);

        Assert.Equal(1, spec.QuantityStep);
        Assert.True(spec.IsValid, "With LotSize=1 resolved as QuantityStep and manual Min/MaxQuantity fallbacks configured, the specification must be valid.");
    }

    [Fact]
    public void UnpopulatedSecurity_DefaultsLotSizeToOne_IndistinguishableFromGenuineData()
    {
        // THE discovery that caused this resolver to be rejected for active use in IQIAIndicator.cs -
        // see the class doc comment. A Security no connector has ever populated already reports
        // LotSize = 1 by SDK default (proven here by reflection-free construction, not assumed) -
        // Resolve cannot tell this apart from a real feed genuinely reporting "1" for MES. Wiring this
        // in would have silently produced QuantityStep = 1 / IsValid = true for ANY unpopulated
        // instrument, exactly what the Lot 12.6 brief forbids, without ever writing that literal value.
        Security neverPopulated = new() { Instrument = "MES", TickSize = 0.25m, TickCost = 1.25m };

        Assert.Equal(1m, neverPopulated.LotSize);

        (int quantityStep, string source) = ATASInstrumentQuantityStepResolver.Resolve(neverPopulated, manualQuantityStep: 0);

        Assert.Equal(1, quantityStep);
        Assert.Equal(ATASInstrumentQuantityStepResolver.AtasLotSizeSource, source);
    }

    [Fact]
    public void ActualProductionPath_NeverUsesTheResolver_QuantityStepStaysManualOnly()
    {
        // Confirms what IQIAIndicator.cs actually does since this lot's reversal: QuantityStep is
        // ATASInstrumentAdapter.Build's manual parameter directly, completely bypassing this resolver -
        // an unpopulated Security correctly stays fail-closed via the SAME path Lot 12.2/12.3 already
        // established and tested (ATASRuntimeDiagnosticsTests.Test04).
        Security security = new() { Instrument = "MES", TickSize = 0.25m, TickCost = 1.25m };

        var spec = ATASInstrumentAdapter.Build(security, fallbackMinQuantity: 0, fallbackMaxQuantity: 0, quantityStep: 0);

        Assert.Equal(0, spec.QuantityStep);
        Assert.False(spec.IsValid, "No reliable source for QuantityStep - must stay fail-closed, never invented.");
    }
}
