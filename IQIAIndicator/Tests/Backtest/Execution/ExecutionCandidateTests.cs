using System;
using IQIAIndicator.Backtest;
using IQIAIndicator.Backtest.Execution;
using IQIAIndicator.Engine.EntryTrigger;
using IQIAIndicator.Engine.TradePlan;
using Xunit;

namespace IQIAIndicator.Tests.BacktestTests.Execution;

/// <summary>
/// Sprint 15.25 (Lot 14.5, brief §5/§39). <see cref="ExecutionCandidate.FromSignal"/> - a pure
/// extraction, never a recomputation of the signal (brief §5).
/// </summary>
public sealed class ExecutionCandidateTests
{
    private static readonly DateTime Anchor = new(2026, 1, 5, 14, 30, 0, DateTimeKind.Utc);

    private static BacktestSignalResult SignalWithPlan(TradePlan? plan) =>
        new(BarIndex: 7, Timestamp: Anchor, Status: BacktestSignalStatus.Ready, Reason: null,
            Context: null, Regime: null, Decision: null, Methodology: null, Signal: null, Entry: null,
            EntryTrigger: null, TradePlan: plan, Exception: null);

    [Fact]
    public void FromSignal_ExtractsBarIndexTimestampDirectionEntryPriceAndTradePlanStatus()
    {
        var plan = new TradePlan(
            IsValid: false, Status: TradePlanStatus.SIGNAL_ONLY, Direction: DirectionCandidate.BUY_CANDIDATE,
            EntryPrice: 100m, StopLoss: null, TakeProfit: null, RiskPerUnit: null, RiskAmount: null,
            PositionSize: null, RiskRewardRatio: null, InvalidationReason: null,
            Diagnostics: Array.Empty<string>(), Timestamp: DateTime.UtcNow);

        ExecutionCandidate candidate = ExecutionCandidate.FromSignal(SignalWithPlan(plan));

        Assert.Equal(7, candidate.SignalBarIndex);
        Assert.Equal(Anchor, candidate.SignalTimestamp);
        Assert.Equal(DirectionCandidate.BUY_CANDIDATE, candidate.Direction);
        Assert.Equal(100m, candidate.EntryPrice);
        Assert.Equal(TradePlanStatus.SIGNAL_ONLY, candidate.TradePlanStatus);
    }

    [Fact]
    public void FromSignal_NoTradePlan_ProducesNoActionDirection_AndNullEntryPrice()
    {
        ExecutionCandidate candidate = ExecutionCandidate.FromSignal(SignalWithPlan(null));

        Assert.Equal(DirectionCandidate.NO_ACTION, candidate.Direction);
        Assert.Null(candidate.EntryPrice);
        Assert.Null(candidate.TradePlanStatus);
    }

    [Fact]
    public void FromSignal_NullSignal_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ExecutionCandidate.FromSignal(null!));
    }

    [Fact]
    public void FromSignal_NeverCopiesTheFullTradePlan_OnlyTheFourRelevantFields()
    {
        // Structural check (brief §5: "Ne pas copier inutilement tout le TradePlan") - ExecutionCandidate
        // has exactly SignalBarIndex/SignalTimestamp/Direction/EntryPrice/TradePlanStatus, never
        // StopLoss/TakeProfit/RiskPerUnit/PositionSize/RiskRewardRatio/Diagnostics.
        var properties = typeof(ExecutionCandidate).GetProperties();
        Assert.Equal(5, properties.Length);
    }
}
