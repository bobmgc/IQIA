using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using ATAS.DataFeedsCore;
using ATAS.DataFeedsCore.Statistics;
using IQIAIndicator.Engine.Risk;
using IQIAIndicator.Infrastructure.ATAS;
using Utils.Common.Collections;
using RiskDirection = IQIAIndicator.Engine.Risk.TradeDirection;

namespace IQIAIndicator.Tests.Infrastructure.ATAS;

/// <summary>
/// Sprint 15.25 (Lot 12.3, Section 9). Unit coverage of the TEMPORARY diagnostic capture
/// (ATASRuntimeDiagnostics) added to investigate why a real Replay session reported CurrentEquity=$0.00
/// and MES INSTRUMENT_SPEC_INVALID. Reuses the same simulated-ATAS-object approach as
/// ATASRuntimeBindingTests.cs (Lot 12.2) - these fixtures prove the capture logic is correct, never
/// that a live Replay/Live session behaves this way (that requires the runtime validation procedure
/// documented in the Lot 12.3 report).
/// </summary>
public static class ATASRuntimeDiagnosticsTests
{
    public static void RunAll()
    {
        Test01_AccountValuesCapturedCorrectly();
        Test02_UnavailableEquityNeverSilentlyFallsBack();
        Test03_MesInstrumentBuiltFromAtasData();
        Test04_MissingQuantityStepAloneInvalidatesSpecificationEvenWithValidAtasData();
        Test05_InvalidStopLossUnchangedWhenAbsent();
    }

    // ── Fixtures (mirrors ATASRuntimeBindingTests.cs, Lot 12.2) ─────────────────────────────────────

    private static Security BuildSecurity(string instrument, decimal tickSize, decimal tickCost, decimal? lotMin = 1m, decimal? lotMax = 50m) =>
        new() { Instrument = instrument, TickSize = tickSize, TickCost = tickCost, LotMinSize = lotMin, LotMaxSize = lotMax };

    private static Portfolio BuildPortfolio(string accountId, decimal balance, decimal openPnL) =>
        new() { AccountID = accountId, Balance = balance, OpenPnL = openPnL, IsRealAccount = false };

    private static Position BuildPosition(decimal volume, decimal unrealizedPnL) =>
        new() { Volume = volume, UnrealizedPnL = unrealizedPnL };

    private sealed class FakeMutableEnumerable<T> : IMutableEnumerable<T>
    {
        private readonly List<T> _values;
        public FakeMutableEnumerable(params T[] values) => _values = new List<T>(values);
        public IEnumerator<T> GetEnumerator() => _values.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
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
        public required ITradingStatistics Realtime { get; init; }
        public required ITradingStatistics Replay { get; init; }
        public Task<ITradingStatistics> LoadHistoryAsync(DateTime from, DateTime to, ICollection<string>? accounts = null, ICollection<string>? securities = null) =>
            throw new NotSupportedException("Not used by ATASRuntimeDiagnostics.");
    }

    // ── TEST 1: Account ATAS values correctly read ──────────────────────────────────────────────────

    private static void Test01_AccountValuesCapturedCorrectly()
    {
        Portfolio portfolio = BuildPortfolio("ACC-1", balance: 25000m, openPnL: -150m);
        Position position = BuildPosition(volume: 2m, unrealizedPnL: -150m);
        var provider = new FakeTradingStatisticsProvider
        {
            Realtime = new FakeTradingStatistics { Equity = new FakeMutableEnumerable<EquityValue>(new EquityValue("MES", DateTime.UtcNow, 24850m, 24850m)) },
            Replay = new FakeTradingStatistics { Equity = new FakeMutableEnumerable<EquityValue>() }
        };

        ATASAccountDiagnostic diagnostic = ATASRuntimeDiagnostics.CaptureAccount(portfolio, position, provider, finalEquityUsed: 24850m);

        Assert(diagnostic.AccountID == "ACC-1", $"Actual={diagnostic.AccountID}.");
        Assert(diagnostic.Balance == 25000m, $"Actual={diagnostic.Balance}.");
        Assert(diagnostic.OpenPnL == -150m, $"Actual={diagnostic.OpenPnL}.");
        Assert(diagnostic.PositionVolume == 2m, $"Actual={diagnostic.PositionVolume}.");
        Assert(diagnostic.PositionUnrealizedPnL == -150m, $"Actual={diagnostic.PositionUnrealizedPnL}.");
        Assert(diagnostic.RealtimeEquity == 24850m, $"Actual={diagnostic.RealtimeEquity}.");
        Assert(diagnostic.ReplayEquity is null, "An empty Replay curve must be captured as null, not a fabricated value.");
        Assert(diagnostic.FinalEquityUsed == 24850m, $"Actual={diagnostic.FinalEquityUsed}.");
    }

    // ── TEST 2: Unavailable Equity never silently falls back ────────────────────────────────────────

    private static void Test02_UnavailableEquityNeverSilentlyFallsBack()
    {
        // Portfolio has a real Balance (25000), but no statistics provider at all - the exact shape of
        // the reported Replay symptom (Capital $25,000.00 configured, Equity $0.00).
        Portfolio portfolio = BuildPortfolio("ACC-1", balance: 25000m, openPnL: 0m);

        ATASAccountDiagnostic diagnostic = ATASRuntimeDiagnostics.CaptureAccount(portfolio, position: null, statisticsProvider: null, finalEquityUsed: 0m);

        Assert(diagnostic.Balance == 25000m, "Balance must still be captured (it IS available) even though Equity is not.");
        Assert(diagnostic.RealtimeEquity is null, "No provider -> RealtimeEquity must be null, never Balance (25000) or 0 presented as a real reading.");
        Assert(diagnostic.ReplayEquity is null, "No provider -> ReplayEquity must be null.");
        Assert(diagnostic.FinalEquityUsed == 0m, "The 0m sentinel actually sent to AccountState must be visible here too, for side-by-side comparison with the raw (null) ATAS reading.");

        AccountState account = ATASAccountStateAdapter.Build(portfolio, currentEquity: null,
            initialCapital: 25000m, peakEquity: 25000m, dailyStartingEquity: 25000m, dailyPnL: 0m, riskUsedToday: 0m, openRisk: 0m);
        RiskAssessment assessment = new RiskEngine().Evaluate(new RiskEngineRequest(
            RiskDirection.Buy, 7554.50m, 7553m, 7556m,
            ATASInstrumentAdapter.Build(BuildSecurity("MES", 0.25m, 1.25m), 1, 50, 1),
            account, new RiskPolicy(0.02m, null, null, null, null, null, null, null, null, null)));

        Assert(assessment.Status == RiskDecisionStatus.REJECTED, "Reproduces the observed Replay symptom: REJECTED.");
        Assert(assessment.RejectionReasons.Contains(RiskRejectionReason.INVALID_EQUITY), "Must reproduce INVALID_EQUITY exactly as observed on the real Replay dashboard.");
    }

    // ── TEST 3: MES instrument built from ATAS data ─────────────────────────────────────────────────

    private static void Test03_MesInstrumentBuiltFromAtasData()
    {
        Security security = BuildSecurity("MES", tickSize: 0.25m, tickCost: 1.25m, lotMin: 1m, lotMax: 50m);
        InstrumentRiskSpecification spec = ATASInstrumentAdapter.Build(security, fallbackMinQuantity: 0, fallbackMaxQuantity: 0, quantityStep: 1);

        ATASInstrumentDiagnostic diagnostic = ATASRuntimeDiagnostics.CaptureInstrument(security, spec);

        Assert(diagnostic.Instrument == "MES" && diagnostic.TickSize == 0.25m && diagnostic.TickCost == 1.25m,
            "Raw ATAS values must be captured unchanged.");
        Assert(diagnostic.FinalSpecification.PointValue == 5m, $"Actual={diagnostic.FinalSpecification.PointValue}.");
        Assert(diagnostic.FinalSpecification.IsValid, "With QuantityStep explicitly configured (1) and ATAS providing everything else, the specification must be valid.");
        Assert(diagnostic.InvalidFieldReasons.Count == 0, "A valid specification must report zero invalid-field reasons.");
    }

    // ── TEST 4: The likely real-world root cause - QuantityStep alone invalidates an otherwise fully
    // ATAS-sourced MES specification ───────────────────────────────────────────────────────────────

    private static void Test04_MissingQuantityStepAloneInvalidatesSpecificationEvenWithValidAtasData()
    {
        // Every ATAS-sourced field is valid (Symbol/TickSize/TickCost/LotMinSize/LotMaxSize all present,
        // matching a real MES Security) - only quantityStep is left at its Lot 11 UI-parameter default
        // of 0 (never configured by the user, never sourced from ATAS - Lot 12.1 found no equivalent).
        Security security = BuildSecurity("MES", tickSize: 0.25m, tickCost: 1.25m, lotMin: 1m, lotMax: 50m);
        InstrumentRiskSpecification spec = ATASInstrumentAdapter.Build(security, fallbackMinQuantity: 0, fallbackMaxQuantity: 0, quantityStep: 0);

        ATASInstrumentDiagnostic diagnostic = ATASRuntimeDiagnostics.CaptureInstrument(security, spec);

        Assert(!string.IsNullOrWhiteSpace(diagnostic.Instrument) && diagnostic.TickSize > 0m && diagnostic.TickCost > 0m,
            "Sanity check: the ATAS-sourced fields are indeed all individually valid.");
        Assert(!diagnostic.FinalSpecification.IsValid, "An unconfigured QuantityStep (0) alone must invalidate the specification, exactly reproducing INSTRUMENT_SPEC_INVALID with a correctly-detected MES instrument.");
        Assert(diagnostic.InvalidFieldReasons.Count == 1 && diagnostic.InvalidFieldReasons[0].StartsWith("QuantityStep"),
            $"The diagnostic must name QuantityStep specifically as the sole invalid field. Actual reasons=[{string.Join("; ", diagnostic.InvalidFieldReasons)}].");
    }

    // ── TEST 5: INVALID_STOP_LOSS unchanged when SL is absent ───────────────────────────────────────

    private static void Test05_InvalidStopLossUnchangedWhenAbsent()
    {
        InstrumentRiskSpecification spec = ATASInstrumentAdapter.Build(BuildSecurity("MES", 0.25m, 1.25m), 1, 50, 1);
        AccountState account = ATASAccountStateAdapter.Build(BuildPortfolio("ACC-1", 25000m, 0m), currentEquity: 25000m,
            initialCapital: 25000m, peakEquity: 25000m, dailyStartingEquity: 25000m, dailyPnL: 0m, riskUsedToday: 0m, openRisk: 0m);

        RiskAssessment assessment = new RiskEngine().Evaluate(new RiskEngineRequest(
            RiskDirection.Buy, 7554.50m, StopLoss: null, TakeProfit: 7553.256m,
            spec, account, new RiskPolicy(0.02m, null, null, null, null, null, null, null, null, null)));

        Assert(assessment.Status == RiskDecisionStatus.REJECTED, "A TradePlan with no StopLoss must be REJECTED - unchanged Lot 10 behaviour.");
        Assert(assessment.RejectionReasons.Contains(RiskRejectionReason.INVALID_STOP_LOSS), "Must reuse INVALID_STOP_LOSS exactly - no new reason, no fabricated SL, no RiskEngine change.");
        Assert(assessment.StopLoss is null, "StopLoss must remain null in the assessment - never a fabricated distance/ATR/percentage stop.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
