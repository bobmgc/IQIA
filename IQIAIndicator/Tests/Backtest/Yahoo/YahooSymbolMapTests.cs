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
    [InlineData("NQ")]
    [InlineData("SPY")]
    [InlineData("")]
    [InlineData("mes")] // ordinal-cased: lowercase is not the same key
    public void UnverifiedSymbol_Throws_NeverGuessesATicker(string symbol)
    {
        Assert.ThrowsAny<Exception>(() => YahooSymbolMap.Resolve(symbol));
    }

    [Fact]
    public void SupportedSymbols_ContainsExactlyEsAndMes_InThisLot()
    {
        var supported = YahooSymbolMap.SupportedSymbols;
        Assert.Equal(2, supported.Count);
        Assert.Contains("ES", supported);
        Assert.Contains("MES", supported);
    }
}
