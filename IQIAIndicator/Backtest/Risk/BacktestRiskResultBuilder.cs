using System;
using System.Collections.Generic;
using System.Linq;
using IQIAIndicator.Backtest.Cost;
using IQIAIndicator.Backtest.Execution;
using IQIAIndicator.Backtest.Pnl;
using IQIAIndicator.Engine.Risk;

namespace IQIAIndicator.Backtest.Risk;

/// <summary>
/// Sprint 15.25 (Lot 14.8, brief §13/§16). Sequentially walks every Closed position, in the SAME
/// chronological order Lot 14.6/14.7's own equity curves already use (ExitTimestamp, then PositionId as a
/// deterministic tie-break), threading a running <see cref="AccountState"/> forward - the "Next Risk
/// Decision" loop of brief §16's own ordering (Signal -&gt; Requested Position -&gt; Risk Evaluation -&gt; Final
/// Quantity -&gt; Execution Price -&gt; Execution Costs -&gt; Position PnL -&gt; Net PnL -&gt; Equity Update -&gt; Next
/// Risk Decision).
///
/// ASSUMED SIMPLIFICATION (documented, brief §3 explicitly excludes portfolio VaR/correlation/multi-
/// account): positions are treated as a strictly sequential, ONE-AT-A-TIME series in this same
/// (ExitTimestamp, PositionId) order - never a true overlapping-portfolio model with concurrent open risk.
/// This is why <see cref="PositionRiskEvaluator"/> is always given <c>AccountState.OpenRisk = 0</c> (brief
/// §3's <see cref="Engine.Risk.PortfolioState"/> stub is not wired in - by the time position N is
/// evaluated in THIS loop's order, position N-1 has already fully settled). "Daily" fields
/// (DailyStartingEquity/DailyPnL) degrade to whole-run fields (never reset) - brief §22 excludes "daily
/// loss limit complexe", so no calendar-day boundary detection is implemented; a caller who sets
/// RiskPolicy.MaxDailyLossPercent/Amount is effectively configuring a run-level loss limit under this lot.
///
/// ZERO-RISK/ZERO-COST EQUIVALENCE (brief §18, proven algebraically in this lot's report and verified by
/// tests): with risk controls disabled, AllowedQuantity == RequestedQuantity == pnlConfiguration.Quantity
/// for every position, and Equity = InitialCapital + CumulativePnL at every step, so
/// (Equity - peak(Equity)) == (CumulativePnL - peak(CumulativePnL)) - IDENTICAL to
/// <see cref="Pnl.BacktestPnLResultBuilder"/>/<see cref="Cost.BacktestCostResultBuilder"/>'s own
/// MaximumDrawdown/FinalGrossPnL/FinalNetPnL for the same scenario.
///
/// NOTE: <c>pnlConfiguration.StartingCapital</c> is IGNORED here - <paramref name="initialCapital"/> (the
/// scenario's own <see cref="Backtest.BacktestScenario.InitialCapital"/>) is the single source of truth
/// for the capital baseline used by both risk evaluation and equity tracking, avoiding two independently
/// configurable "capital" numbers.
/// </summary>
public static class BacktestRiskResultBuilder
{
    public static BacktestRiskResult Build(
        IReadOnlyList<SimulatedPosition> positions,
        decimal initialCapital,
        InstrumentRiskSpecification instrument,
        RiskPolicy policy,
        PnLConfiguration pnlConfiguration,
        ExecutionCostConfiguration costConfiguration,
        BacktestRiskConfiguration riskConfiguration)
    {
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(instrument);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(pnlConfiguration);
        ArgumentNullException.ThrowIfNull(costConfiguration);
        ArgumentNullException.ThrowIfNull(riskConfiguration);

        if (initialCapital <= 0m)
            throw new ArgumentOutOfRangeException(nameof(initialCapital), initialCapital, "InitialCapital must be strictly positive.");

        List<SimulatedPosition> closedSorted = positions
            .Where(p => p.Status == PositionStatus.Closed)
            .OrderBy(p => p.ExitTimestamp!.Value)
            .ThenBy(p => p.PositionId)
            .ToList();

        var outcomeByPositionId = new Dictionary<int, PositionRiskOutcome>(closedSorted.Count);

        // Brief §18/§20 Invariant 6: cumulative NetPnL and its running peak are tracked SEPARATELY from
        // InitialCapital (both starting at 0m) - the exact same accumulation Lot 14.6's
        // BacktestPnLResultBuilder/Lot 14.7's BacktestCostResultBuilder already use. Equity is only ever
        // derived as ONE addition (InitialCapital + cumulative), never accumulated incrementally against a
        // running equity variable - the two are mathematically equal but NOT guaranteed bit-identical
        // under finite decimal precision, and this lot's zero-regression guarantee requires the latter.
        decimal cumulativeNetPnL = 0m;
        decimal peakCumulativeNetPnL = 0m;
        int allowedCount = 0, rejectedCount = 0;

        foreach (SimulatedPosition position in closedSorted)
        {
            int requestedQuantity = pnlConfiguration.Quantity;
            decimal equityBefore = initialCapital + cumulativeNetPnL;
            decimal peakEquity = initialCapital + peakCumulativeNetPnL;

            var account = new AccountState(
                InitialCapital: initialCapital,
                CurrentEquity: equityBefore,
                CurrentBalance: null,
                PeakEquity: peakEquity,
                DailyStartingEquity: initialCapital,
                DailyPnL: cumulativeNetPnL,
                RiskUsedToday: 0m,
                OpenRisk: 0m);

            RiskEvaluationResult riskEval = PositionRiskEvaluator.Evaluate(
                position, requestedQuantity, account, instrument, policy, riskConfiguration, costConfiguration);

            PositionCostResult? costResult = null;
            decimal? netPnL = null;

            if (riskEval.IsAllowed && riskEval.AllowedQuantity > 0)
            {
                allowedCount++;

                // Brief §15: costs/PnL computed on the RISK-DETERMINED quantity, never the raw requested
                // one - a fresh per-position PnLConfiguration, same Instrument, quantity overridden.
                PnLConfiguration perPositionConfig = PnLConfiguration.Create(pnlConfiguration.Instrument, quantity: riskEval.AllowedQuantity);
                PositionPnLResult pnlResult = PositionPnLCalculator.Calculate(position, perPositionConfig);
                costResult = PositionCostCalculator.Calculate(position, pnlResult, perPositionConfig, costConfiguration);
                netPnL = costResult.NetPnL;

                cumulativeNetPnL += netPnL!.Value;
                if (cumulativeNetPnL > peakCumulativeNetPnL)
                    peakCumulativeNetPnL = cumulativeNetPnL;
            }
            else
            {
                rejectedCount++;
            }

            decimal equityAfter = initialCapital + cumulativeNetPnL;
            decimal drawdown = cumulativeNetPnL - peakCumulativeNetPnL;

            outcomeByPositionId[position.PositionId] = new PositionRiskOutcome(
                position.PositionId, position.Status, position.Direction,
                position.EntryTimestamp, position.ExitTimestamp,
                riskEval, costResult, netPnL, equityBefore, equityAfter, drawdown);
        }

        var outcomes = new List<PositionRiskOutcome>(positions.Count);
        foreach (SimulatedPosition position in positions)
        {
            outcomes.Add(
                position.Status == PositionStatus.Closed && outcomeByPositionId.TryGetValue(position.PositionId, out PositionRiskOutcome? outcome)
                    ? outcome
                    : NotExecutableOutcome(position, pnlConfiguration.Quantity));
        }

        decimal finalEquity = initialCapital + cumulativeNetPnL;
        decimal peakFinalEquity = initialCapital + peakCumulativeNetPnL;
        decimal maximumDrawdown = outcomeByPositionId.Count > 0 ? outcomeByPositionId.Values.Min(o => o.Drawdown!.Value) : 0m;

        return new BacktestRiskResult(
            outcomes.AsReadOnly(), closedSorted.Count, allowedCount, rejectedCount,
            initialCapital, finalEquity, cumulativeNetPnL, peakFinalEquity, maximumDrawdown,
            RiskResultFingerprint.ComputeHash(outcomes));
    }

    private static PositionRiskOutcome NotExecutableOutcome(SimulatedPosition position, int requestedQuantity)
    {
        var riskEval = new RiskEvaluationResult(
            IsAllowed: false, RequestedQuantity: requestedQuantity, AllowedQuantity: 0,
            RiskConstrainedQuantity: null, ExposureConstrainedQuantity: null,
            RiskPerUnit: null, TotalEstimatedRisk: null, MaximumAllowedRisk: null,
            Exposure: null, MaxExposure: null,
            Reason: PositionRiskReason.PositionNotExecutable,
            Diagnostics: new[] { "Position never reached Closed - excluded from risk/equity evaluation." });

        return new PositionRiskOutcome(
            position.PositionId, position.Status, position.Direction,
            position.EntryTimestamp, position.ExitTimestamp,
            riskEval, null, null, null, null, null);
    }
}
