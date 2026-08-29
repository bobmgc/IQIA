using System;
using System.Collections.Generic;
using IQIAIndicator.Backtest;
using IQIAIndicator.Core;
using IQIAIndicator.Core.MarketData;
using IQIAIndicator.Engine.Risk;
using Xunit;

namespace IQIAIndicator.Tests.BacktestTests;

/// <summary>Sprint 15.25 (Lot 14.1, brief §10). BacktestScenario never completes a missing field with an
/// invented value - absence is always reported, never defaulted.</summary>
public sealed class BacktestScenarioTests
{
    private static readonly DateTime T0 = new(2026, 1, 5, 14, 30, 0, DateTimeKind.Utc);

    private static HistoricalSeries ValidSeries() => HistoricalSeries.Create(
        "ES", "M5", "UTC", "TestProvider",
        new List<HistoricalBar>
        {
            new(T0, 100m, 101m, 99m, 100.5m, 10m),
            new(T0.AddMinutes(5), 100.5m, 102m, 100m, 101.5m, 12m)
        });

    private static BacktestWindow ValidWindow() => new("TRAIN", T0, T0.AddMinutes(10));

    private static InstrumentRiskSpecification ValidInstrument() => InstrumentRiskSpecification.FromInstrumentInfo(
        new InstrumentInfo("ES", TickSize: 0.25m, TickValue: 12.5m, PointValue: 50m, Decimals: 2),
        minQuantity: 1, maxQuantity: 50, quantityStep: 1);

    private static RiskPolicy ValidPolicy() => new(
        MaxRiskPerTradePercent: 0.02m, MaxRiskPerTradeAmount: null,
        MaxDailyLossPercent: null, MaxDailyLossAmount: null,
        MaxDrawdownPercent: null, MaxDrawdownAmount: null,
        MaxOpenRiskPercent: null, MaxOpenRiskAmount: null,
        MinRiskReward: null, MaxPositionSize: null);

    [Fact]
    public void AllFieldsPresent_Creates()
    {
        BacktestScenario scenario = BacktestScenario.Create(ValidSeries(), ValidWindow(), 50_000m, ValidInstrument(), ValidPolicy());

        Assert.Equal(50_000m, scenario.InitialCapital);
        Assert.Equal("TRAIN", scenario.Window.Name);
        Assert.Equal("ES", scenario.Instrument.Symbol);
    }

    [Fact]
    public void MissingSeries_Invalid()
    {
        bool ok = BacktestScenario.TryCreate(null!, ValidWindow(), 50_000m, ValidInstrument(), ValidPolicy(), out _, out var errors);
        Assert.False(ok);
        Assert.Contains(errors, e => e.Contains("HistoricalSeries", StringComparison.Ordinal));
    }

    [Fact]
    public void MissingWindow_Invalid()
    {
        bool ok = BacktestScenario.TryCreate(ValidSeries(), null!, 50_000m, ValidInstrument(), ValidPolicy(), out _, out var errors);
        Assert.False(ok);
        Assert.Contains(errors, e => e.Contains("BacktestWindow", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveInitialCapital_Invalid_MirrorsRiskEngineConvention(decimal capital)
    {
        bool ok = BacktestScenario.TryCreate(ValidSeries(), ValidWindow(), capital, ValidInstrument(), ValidPolicy(), out _, out var errors);
        Assert.False(ok);
        Assert.Contains(errors, e => e.Contains("InitialCapital", StringComparison.Ordinal));
    }

    [Fact]
    public void MissingInstrument_Invalid()
    {
        bool ok = BacktestScenario.TryCreate(ValidSeries(), ValidWindow(), 50_000m, null!, ValidPolicy(), out _, out var errors);
        Assert.False(ok);
        Assert.Contains(errors, e => e.Contains("InstrumentRiskSpecification", StringComparison.Ordinal));
    }

    [Fact]
    public void IncompleteInstrument_Invalid_ReusesInstrumentRiskSpecificationIsValid()
    {
        var incomplete = new InstrumentRiskSpecification("", TickSize: 0m, TickValue: 0m, PointValue: 0m, MinQuantity: 0, MaxQuantity: 0, QuantityStep: 0);
        bool ok = BacktestScenario.TryCreate(ValidSeries(), ValidWindow(), 50_000m, incomplete, ValidPolicy(), out _, out var errors);
        Assert.False(ok);
        Assert.Contains(errors, e => e.Contains("IsValid", StringComparison.Ordinal));
    }

    [Fact]
    public void MissingPolicy_Invalid()
    {
        bool ok = BacktestScenario.TryCreate(ValidSeries(), ValidWindow(), 50_000m, ValidInstrument(), null!, out _, out var errors);
        Assert.False(ok);
        Assert.Contains(errors, e => e.Contains("RiskPolicy", StringComparison.Ordinal));
    }

    [Fact]
    public void PolicyWithAllNullLimits_IsStillPresent_ThereforeValid()
    {
        // RiskPolicy's own contract: every individually-null limit field means "unconstrained", not
        // "absent policy". Only the RiskPolicy object itself must be non-null.
        var unconstrained = new RiskPolicy(null, null, null, null, null, null, null, null, null, null);
        BacktestScenario scenario = BacktestScenario.Create(ValidSeries(), ValidWindow(), 50_000m, ValidInstrument(), unconstrained);
        Assert.Null(scenario.Policy.MaxRiskPerTradePercent);
    }

    [Fact]
    public void Create_Throws_NamingEveryViolation()
    {
        var ex = Assert.Throws<ArgumentException>(() => BacktestScenario.Create(null!, null!, 0m, null!, null!));
        Assert.Contains("HistoricalSeries", ex.Message, StringComparison.Ordinal);
        Assert.Contains("BacktestWindow", ex.Message, StringComparison.Ordinal);
        Assert.Contains("InitialCapital", ex.Message, StringComparison.Ordinal);
        Assert.Contains("InstrumentRiskSpecification", ex.Message, StringComparison.Ordinal);
        Assert.Contains("RiskPolicy", ex.Message, StringComparison.Ordinal);
    }
}
