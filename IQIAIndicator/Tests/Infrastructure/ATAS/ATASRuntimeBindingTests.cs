using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using ATAS.DataFeedsCore;
using ATAS.DataFeedsCore.Statistics;
using IQIAIndicator.Engine.Risk;
using IQIAIndicator.Infrastructure.ATAS;
using Utils.Common.Collections;
// ATAS.DataFeedsCore also declares its own TradeDirection (order side) - disambiguated via this alias
// rather than fully qualifying every call site.
using RiskDirection = IQIAIndicator.Engine.Risk.TradeDirection;

namespace IQIAIndicator.Tests.Infrastructure.ATAS;

/// <summary>
/// Sprint 15.25 (Lot 12.2, Section 17). Unit coverage of the ATAS runtime binding
/// (ATASAccountStateAdapter/ATASInstrumentAdapter): pure mapping from real ATAS domain types
/// (ATAS.DataFeedsCore.Portfolio/Security, ATAS.DataFeedsCore.Statistics.EquityValue - all constructed
/// here exactly as ATAS itself would, confirmed by the Lot 12.1 reflection audit to have public
/// parameterless constructors and settable properties) into AccountState/InstrumentRiskSpecification
/// (Lot 10, unchanged), then through the unmodified RiskEngine. These fixtures are simulated ATAS
/// objects, not a live ATAS session - they prove the mapping logic is correct, never that Live/Replay
/// ATAS itself behaves this way (Lot 12.2, Section 18 - that requires live validation, see the report).
/// </summary>
public static class ATASRuntimeBindingTests
{
    public static void RunAll()
    {
        Test01_AccountMapping();
        Test02_EquityMapping();
        Test03_EquityUnavailableFailsClosed();
        Test04_MesSpecificationProducesMesRiskPerUnit();
        Test05_EsSpecificationProducesEsRiskPerUnit();
        Test06_DataDrivenNotSymbolDriven();
        Test07_IncompleteInstrumentFailsClosed();
        Test08_ExistingRiskEngineRulesUnchanged();
    }

    // ── Fixtures ─────────────────────────────────────────────────────────────────────────────────

    private static Security BuildSecurity(string instrument, decimal tickSize, decimal tickCost, decimal? lotMin = 1m, decimal? lotMax = 50m) =>
        new()
        {
            Instrument = instrument,
            TickSize = tickSize,
            TickCost = tickCost,
            LotMinSize = lotMin,
            LotMaxSize = lotMax
        };

    private static Portfolio BuildPortfolio(decimal balance) => new() { Balance = balance };

    /// <summary>Minimal in-memory stand-in for ATAS's own IMutableEnumerable&lt;T&gt; (Utils.Common.dll) -
    /// only GetEnumerator is exercised by ATASAccountStateAdapter (Any/Last via LINQ); the four
    /// Added/Changed/Removed/Cleared events are never raised (no test here needs live mutation
    /// notifications), satisfying the interface without behaving like a real observable collection.</summary>
    private sealed class FakeMutableEnumerable<T> : IMutableEnumerable<T>
    {
        private readonly List<T> _values;
        public FakeMutableEnumerable(params T[] values) => _values = new List<T>(values);
        public IEnumerator<T> GetEnumerator() => _values.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        // Explicit (never field-like) accessors: satisfies IMutableEnumerable<T> without ever needing
        // to raise them - no test here needs live mutation notifications - and avoids a
        // never-invoked-field-like-event warning that auto-implemented events would otherwise produce.
        public event Action<T>? Added { add { } remove { } }
        public event Action<T>? Changed { add { } remove { } }
        public event Action<T>? Removed { add { } remove { } }
        public event Action? Cleared { add { } remove { } }
    }

    private sealed class FakeTradingStatistics : ITradingStatistics
    {
        public required IMutableEnumerable<EquityValue> Equity { get; init; }
        public IMutableEnumerable<Order> Orders => new FakeMutableEnumerable<Order>();
        public IMutableEnumerable<MyTrade> MyTrades => new FakeMutableEnumerable<MyTrade>();
        public IMutableEnumerable<HistoryMyTrade> HistoryMyTrades => new FakeMutableEnumerable<HistoryMyTrade>();
        public IMutableEnumerable<IStatisticsParameterGroup> Statistics => new FakeMutableEnumerable<IStatisticsParameterGroup>();
    }

    private sealed class FakeTradingStatisticsProvider : ITradingStatisticsProvider
    {
        // Declared non-nullable to match ITradingStatisticsProvider's own annotation exactly; tests that
        // need to simulate a missing stream pass null! at the call site instead (see TEST 3).
        public required ITradingStatistics Realtime { get; init; }
        public required ITradingStatistics Replay { get; init; }
        public Task<ITradingStatistics> LoadHistoryAsync(DateTime from, DateTime to, ICollection<string>? accounts = null, ICollection<string>? securities = null) =>
            throw new NotSupportedException("Not used by ATASAccountStateAdapter - history loading is out of this lot's scope.");
    }

    private static AccountState DefaultAccount(decimal? currentEquity) =>
        ATASAccountStateAdapter.Build(
            portfolio: null, currentEquity,
            initialCapital: 50000m, peakEquity: 50000m, dailyStartingEquity: 50000m, dailyPnL: 0m,
            riskUsedToday: 0m, openRisk: 0m);

    private static RiskPolicy DefaultPolicy() => new(0.02m, null, null, null, null, null, null, null, null, null);

    // ── TEST 1: Account mapping ─────────────────────────────────────────────────────────────────

    private static void Test01_AccountMapping()
    {
        Portfolio portfolio = BuildPortfolio(balance: 48000m);
        AccountState account = ATASAccountStateAdapter.Build(
            portfolio, currentEquity: 51000m,
            initialCapital: 50000m, peakEquity: 52000m, dailyStartingEquity: 50500m, dailyPnL: 500m,
            riskUsedToday: 100m, openRisk: 200m);

        Assert(account.CurrentEquity == 51000m, $"CurrentEquity must be the resolved ATAS equity. Actual={account.CurrentEquity}.");
        Assert(account.CurrentBalance == 48000m, $"CurrentBalance must come from Portfolio.Balance. Actual={account.CurrentBalance}.");
        Assert(account.InitialCapital == 50000m, "InitialCapital must remain the manual configuration value, never overwritten by ATAS (Lot 12.2, Section 3).");
        Assert(account.PeakEquity == 52000m && account.DailyStartingEquity == 50500m && account.DailyPnL == 500m
            && account.RiskUsedToday == 100m && account.OpenRisk == 200m,
            "Fields with no ATAS equivalent must pass through the manual configuration unchanged.");
    }

    // ── TEST 2: Equity mapping ───────────────────────────────────────────────────────────────────

    private static void Test02_EquityMapping()
    {
        var equitySeries = new FakeMutableEnumerable<EquityValue>(
            new EquityValue("MES", DateTime.UtcNow.AddMinutes(-2), 50000m, 50000m),
            new EquityValue("MES", DateTime.UtcNow.AddMinutes(-1), 50500m, 50500m),
            new EquityValue("MES", DateTime.UtcNow, 51000m, 51000m));
        var realtimeStats = new FakeTradingStatistics { Equity = equitySeries };
        var replayStats = new FakeTradingStatistics { Equity = new FakeMutableEnumerable<EquityValue>(new EquityValue("MES", DateTime.UtcNow, 49000m, 49000m)) };
        var provider = new FakeTradingStatisticsProvider { Realtime = realtimeStats, Replay = replayStats };

        decimal? realtimeEquity = ATASAccountStateAdapter.TryGetCurrentEquity(provider, isReplay: false);
        decimal? replayEquity = ATASAccountStateAdapter.TryGetCurrentEquity(provider, isReplay: true);

        Assert(realtimeEquity == 51000m, $"Must return the LATEST point on Realtime's equity curve. Actual={realtimeEquity}.");
        Assert(replayEquity == 49000m, $"isReplay=true must read .Replay, not .Realtime. Actual={replayEquity}.");
    }

    // ── TEST 3: Equity unavailable -> fail closed ────────────────────────────────────────────────

    private static void Test03_EquityUnavailableFailsClosed()
    {
        Assert(ATASAccountStateAdapter.TryGetCurrentEquity(null, isReplay: false) is null, "A null provider must resolve to null equity.");

        var emptyStats = new FakeTradingStatistics { Equity = new FakeMutableEnumerable<EquityValue>() };
        var provider = new FakeTradingStatisticsProvider { Realtime = emptyStats, Replay = null! };
        Assert(ATASAccountStateAdapter.TryGetCurrentEquity(provider, isReplay: false) is null, "An empty equity curve must resolve to null, never a fabricated default value.");
        Assert(ATASAccountStateAdapter.TryGetCurrentEquity(provider, isReplay: true) is null, "A missing Replay stream must resolve to null.");

        AccountState account = ATASAccountStateAdapter.Build(BuildPortfolio(60000m), currentEquity: null,
            initialCapital: 50000m, peakEquity: 50000m, dailyStartingEquity: 50000m, dailyPnL: 0m, riskUsedToday: 0m, openRisk: 0m);
        Assert(account.CurrentEquity == 0m,
            "Unavailable equity must resolve to the 0m sentinel - and critically, never to Balance (60000, available on the same Portfolio) or InitialCapital (50000).");

        RiskEngineRequest request = new(
            RiskDirection.Buy, EntryPrice: 100m, StopLoss: 95m, TakeProfit: 110m,
            ATASInstrumentAdapter.Build(BuildSecurity("MES", 0.25m, 1.25m), 1, 50, 1),
            account, DefaultPolicy());
        RiskAssessment assessment = new RiskEngine().Evaluate(request);

        Assert(assessment.Status == RiskDecisionStatus.REJECTED, "Unavailable equity must REJECT, never ACCEPT.");
        Assert(assessment.RejectionReasons.Contains(RiskRejectionReason.INVALID_EQUITY), "Must reuse the existing INVALID_EQUITY reason (Lot 10) - no new reason invented.");
        Assert(assessment.PositionSize is null, "No position must be sized against unavailable equity.");
    }

    // ── TEST 4: MES specification ───────────────────────────────────────────────────────────────

    private static void Test04_MesSpecificationProducesMesRiskPerUnit()
    {
        // Real MES tick economics: 0.25pt tick, $1.25/tick -> $5/point.
        InstrumentRiskSpecification spec = ATASInstrumentAdapter.Build(BuildSecurity("MES", 0.25m, 1.25m), 1, 50, 1);
        RiskAssessment assessment = new RiskEngine().Evaluate(new RiskEngineRequest(
            RiskDirection.Buy, 100m, 95m, 110m, spec, DefaultAccount(50000m), DefaultPolicy()));

        Assert(spec.Symbol == "MES" && spec.PointValue == 5m, $"MES PointValue must be TickCost(1.25)/TickSize(0.25)=5. Actual={spec.PointValue}.");
        Assert(assessment.RiskPerUnit == 25m, $"MES: RiskDistance(5) x PointValue(5) = 25. Actual={assessment.RiskPerUnit}.");
    }

    // ── TEST 5: ES specification ─────────────────────────────────────────────────────────────────

    private static void Test05_EsSpecificationProducesEsRiskPerUnit()
    {
        // Real ES tick economics: 0.25pt tick, $12.50/tick -> $50/point.
        InstrumentRiskSpecification spec = ATASInstrumentAdapter.Build(BuildSecurity("ES", 0.25m, 12.5m), 1, 50, 1);
        RiskAssessment assessment = new RiskEngine().Evaluate(new RiskEngineRequest(
            RiskDirection.Buy, 100m, 95m, 110m, spec, DefaultAccount(50000m), DefaultPolicy()));

        Assert(spec.Symbol == "ES" && spec.PointValue == 50m, $"ES PointValue must be TickCost(12.5)/TickSize(0.25)=50. Actual={spec.PointValue}.");
        Assert(assessment.RiskPerUnit == 250m, $"ES: RiskDistance(5) x PointValue(50) = 250. Actual={assessment.RiskPerUnit}.");
        Assert(assessment.RiskPerUnit != 25m, "ES must not silently reuse MES's RiskPerUnit.");
    }

    // ── TEST 6: Data-driven, not symbol-driven ──────────────────────────────────────────────────
    // The strongest possible proof against a hidden "if symbol == ..." branch: a Security instance
    // NAMED "ES" but carrying MES's tick economics must behave exactly like MES, proving the engine
    // reacts only to the numeric fields, never to the Instrument string.

    private static void Test06_DataDrivenNotSymbolDriven()
    {
        Security mislabeledSecurity = BuildSecurity("ES", tickSize: 0.25m, tickCost: 1.25m); // MES economics, ES name
        InstrumentRiskSpecification spec = ATASInstrumentAdapter.Build(mislabeledSecurity, 1, 50, 1);
        RiskAssessment assessment = new RiskEngine().Evaluate(new RiskEngineRequest(
            RiskDirection.Buy, 100m, 95m, 110m, spec, DefaultAccount(50000m), DefaultPolicy()));

        Assert(spec.Symbol == "ES", "Sanity check: the Symbol string itself is indeed 'ES'.");
        Assert(assessment.RiskPerUnit == 25m,
            $"A Security named 'ES' but carrying MES's TickSize/TickCost must produce MES's RiskPerUnit (25), proving the mapping reacts only to the numeric fields - never to the Instrument string. Actual={assessment.RiskPerUnit}.");
    }

    // ── TEST 7: Fail-closed instrument ──────────────────────────────────────────────────────────

    private static void Test07_IncompleteInstrumentFailsClosed()
    {
        Security incomplete = BuildSecurity("MES", tickSize: 0.25m, tickCost: 0m); // TickCost missing/zero
        InstrumentRiskSpecification spec = ATASInstrumentAdapter.Build(incomplete, 1, 50, 1);
        RiskAssessment assessment = new RiskEngine().Evaluate(new RiskEngineRequest(
            RiskDirection.Buy, 100m, 95m, 110m, spec, DefaultAccount(50000m), DefaultPolicy()));

        Assert(!spec.IsValid, "An incomplete Security (TickCost=0) must produce an invalid InstrumentRiskSpecification.");
        Assert(assessment.Status == RiskDecisionStatus.REJECTED, "Incomplete instrument data must REJECT, never ACCEPT.");
        Assert(assessment.RejectionReasons.Contains(RiskRejectionReason.INSTRUMENT_SPEC_INVALID), "Must reuse the existing INSTRUMENT_SPEC_INVALID reason (Lot 10).");

        Security nullSecurity = null!;
        InstrumentRiskSpecification specFromNull = ATASInstrumentAdapter.Build(nullSecurity, 0, 0, 0);
        Assert(!specFromNull.IsValid, "A completely absent Security (TradingManager/Security unavailable) must also produce an invalid specification, never a fabricated one.");
    }

    // ── TEST 8: Existing Risk Engine rules unchanged ────────────────────────────────────────────
    // Reproduces Lot 10's own TEST 20 (RiskEngineTests.Test20_RoundingIsConservative) through the ATAS
    // binding path, proving RiskEngine.cs's calculation core is untouched by this lot.

    private static void Test08_ExistingRiskEngineRulesUnchanged()
    {
        InstrumentRiskSpecification spec = ATASInstrumentAdapter.Build(BuildSecurity("ES", 0.25m, 12.5m), 1, 50, 1); // RiskPerUnit(2pt)=100
        AccountState account = ATASAccountStateAdapter.Build(BuildPortfolio(14000m), currentEquity: 14000m,
            initialCapital: 14000m, peakEquity: 14000m, dailyStartingEquity: 14000m, dailyPnL: 0m, riskUsedToday: 0m, openRisk: 0m);
        RiskAssessment assessment = new RiskEngine().Evaluate(new RiskEngineRequest(
            RiskDirection.Buy, 100m, 98m, 110m, spec, account, new RiskPolicy(0.02m, null, null, null, null, null, null, null, null, null)));

        Assert(assessment.RiskBudget == 280m, $"Budget math (14000 x 0.02 = 280) must be identical to Lot 10. Actual={assessment.RiskBudget}.");
        Assert(assessment.PositionSize == 2, $"Conservative rounding (280/100=2.8 -> 2, never 3) must be identical to Lot 10 TEST 20. Actual={assessment.PositionSize}.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
