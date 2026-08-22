using System.Collections.Generic;
using IQIAIndicator.Backtest;
using IQIAIndicator.Core;
using IQIAIndicator.Engine.Regime.Core;

namespace IQIAIndicator.Tests.BacktestTests;

/// <summary>Sprint 15.25 (Lot 14.1). Test-only IBacktestBarObserver that simply records every callback,
/// in order, for later assertion. Never influences the engine (see IBacktestBarObserver's own contract).</summary>
internal sealed class CapturingBarObserver : IBacktestBarObserver
{
    public sealed record Processed(int Index, bool IsWarmup, MarketContext Context, EvidenceSet Evidence);
    public sealed record Rejected(int Index, MarketContext Context, IReadOnlyList<string> Errors);

    public List<Processed> ProcessedBars { get; } = new();
    public List<Rejected> RejectedBars { get; } = new();

    public void OnBarProcessed(int index, bool isWarmup, MarketContext context, EvidenceSet evidence) =>
        ProcessedBars.Add(new Processed(index, isWarmup, context, evidence));

    public void OnBarRejected(int index, MarketContext context, IReadOnlyList<string> validationErrors) =>
        RejectedBars.Add(new Rejected(index, context, validationErrors));
}
