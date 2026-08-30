using System;
using IQIAIndicator.Core.MarketData.Yahoo;
using Xunit;

namespace IQIAIndicator.Tests.BacktestTests.Yahoo;

/// <summary>Sprint 15.25 (Lot 14.2, brief §14). Only ES/MES are resolvable, both to their empirically
/// verified (2026-08-22) continuous-futures Yahoo tickers - no symbol is ever guessed from a pattern.</summary>
public sealed class YahooSymbolMapTests
{
    [Fact]
    public void Es_ResolvesToEsEqualsF()
    {
        Assert.Equal("ES=F", YahooSymbolMap.Resolve("ES"));
    }

    [Fact]
    public void Mes_ResolvesToMesEqualsF()
    {
        Assert.Equal("MES=F", YahooSymbolMap.Resolve("MES"));
    }

    [Theory]
    [InlineData("SPY")]
    [InlineData("6E")]
    [InlineData("")]
    [InlineData("mes")] // ordinal-cased: lowercase is not the same key
    public void UnverifiedSymbol_Throws_NeverGuessesATicker(string symbol)
    {
        Assert.ThrowsAny<Exception>(() => YahooSymbolMap.Resolve(symbol));
    }

    [Fact]
    public void SupportedSymbols_AreAllIndividuallyVerifiedFutures()
    {
        var supported = YahooSymbolMap.SupportedSymbols;
        // Audit 2026-08-30 (P0-2): NQ/YM/RTY/GC/CL added (each live-verified as instrumentType=FUTURE).
        Assert.Equal(7, supported.Count);
        foreach (string symbol in new[] { "ES", "MES", "NQ", "YM", "RTY", "GC", "CL" })
        {
            Assert.Contains(symbol, supported);
        }
    }
}
