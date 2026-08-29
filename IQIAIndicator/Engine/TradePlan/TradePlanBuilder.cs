using System;
using System.Collections.Generic;
using IQIAIndicator.Engine.EntryTrigger;

namespace IQIAIndicator.Engine.TradePlan;

/// <summary>
/// Sprint 15.8. Builds a deterministic TradePlan from data the pipeline already produced. Never
/// invents an entry price, stop, target, risk budget or position size - a field the system cannot
/// honestly source is left null, with the reason recorded in Diagnostics/InvalidationReason (see
/// Phase 1 audit finding in TradePlanContext.cs: no Risk Engine / SL methodology / account risk
/// budget exists anywhere else in the codebase yet).
/// </summary>
public sealed class TradePlanBuilder
{
    public TradePlan Build(TradePlanContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var diagnostics = new List<string>();
        EntryTriggerAssessment assessment = context.EntryTriggerCandidate.Assessment;
        DirectionCandidate direction = assessment.Direction;

        if (direction != DirectionCandidate.BUY_CANDIDATE && direction != DirectionCandidate.SELL_CANDIDATE)
        {
            string noTradeReason = DescribeNoTradeReason(direction, assessment.Reason);
            diagnostics.Add($"No trade plan constructed: {noTradeReason}");
            return new TradePlan(
                IsValid: false,
                Status: TradePlanStatus.NO_TRADE,
                Direction: direction,
                EntryPrice: null,
                StopLoss: null,
                TakeProfit: null,
                RiskPerUnit: null,
                RiskAmount: null,
                PositionSize: null,
                RiskRewardRatio: null,
                InvalidationReason: noTradeReason,
                Diagnostics: diagnostics.AsReadOnly(),
                Timestamp: DateTime.UtcNow);
        }

        // Phase 3: Entry price - the only real price the pipeline offers today (never a computed/
        // predicted price).
        decimal? entryPrice = context.EntryTriggerCandidate.CurrentPrice > 0m
            ? context.EntryTriggerCandidate.CurrentPrice
            : null;
        if (entryPrice is null)
        {
            diagnostics.Add("EntryPrice unavailable: CurrentPrice is missing or non-positive.");
        }

        // Phase 4: Stop Loss - only ever sourced from TradeRiskParameters (the Risk Engine extension
        // point, unpopulated today - Phase 1 audit found no SL methodology anywhere). Never derived
        // from an arbitrary tick/point offset.
        decimal? stopLoss = context.RiskParameters?.StopLoss;
        if (stopLoss is null)
        {
            diagnostics.Add("StopLoss unavailable: no stop-loss methodology is implemented in the system yet (see QDE-011_Risk_Engine_Theory.md).");
        }

        // Phase 5: Take Profit - the only real target the pipeline offers is the mean-reversion
        // model's own equilibrium estimate (KalmanFilterModel.EstimatedMean, already computed
        // upstream, exposed as EntryTriggerAssessment.EstimatedEquilibrium). Used only when it sits on
        // the profitable side of entry for the resolved direction, so a plan never contradicts the
        // direction the system already committed to.
        decimal? takeProfit = null;
        if (entryPrice is decimal entry && assessment.EstimatedEquilibrium is double equilibrium && double.IsFinite(equilibrium))
        {
            decimal candidateTakeProfit = (decimal)equilibrium;
            bool consistent = direction == DirectionCandidate.BUY_CANDIDATE
                ? candidateTakeProfit > entry
                : candidateTakeProfit < entry;

            if (consistent)
            {
                takeProfit = candidateTakeProfit;
            }
            else
            {
                diagnostics.Add($"TakeProfit unavailable: EstimatedEquilibrium ({candidateTakeProfit}) is not on the profitable side of EntryPrice ({entry}) for {direction}.");
            }
        }
        else
        {
            diagnostics.Add("TakeProfit unavailable: EstimatedEquilibrium not present in the pipeline for this bar.");
        }

        // Phase 6: Risk per unit, in account currency - abs(Entry - StopLoss) x PointValue. Price
        // distance and currency conversion are never mixed with tick counts.
        decimal? riskPerUnit = null;
        if (entryPrice is decimal entryForRisk && stopLoss is decimal sl)
        {
            decimal riskDistance = Math.Abs(entryForRisk - sl);
            riskPerUnit = riskDistance * context.InstrumentInfo.PointValue;
        }

        bool degenerateRisk = riskPerUnit is decimal rpu0 && rpu0 <= 0m;
        if (degenerateRisk)
        {
            diagnostics.Add("PositionSize unavailable: RiskPerUnit is zero (StopLoss coincides with EntryPrice).");
        }

        // Phase 7: Position sizing - only if the Risk Engine extension point supplied a risk-per-trade
        // budget (no account/capital info exists anywhere in the system today - Phase 1 audit).
        decimal? riskPerTrade = context.RiskParameters?.RiskPerTrade;
        int? positionSize = null;
        decimal? riskAmount = null;
        if (!degenerateRisk && riskPerUnit is decimal rpu && riskPerTrade is decimal budget)
        {
            int size = (int)Math.Floor(budget / rpu);
            if (size > 0)
            {
                positionSize = size;
                riskAmount = rpu * size;
            }
            else
            {
                diagnostics.Add($"PositionSize unavailable: risk budget ({budget}) is smaller than RiskPerUnit ({rpu}) - would round down to zero contracts.");
            }
        }
        else if (!degenerateRisk && riskPerTrade is null)
        {
            diagnostics.Add("PositionSize unavailable: no risk-per-trade budget is configured or exposed by the system yet.");
        }

        // Phase 8: Risk/Reward - only when Entry, StopLoss and TakeProfit are all valid and risk is
        // strictly positive. Never divides by zero, never reports NaN/Infinity.
        double? riskRewardRatio = null;
        if (!degenerateRisk && entryPrice is decimal entryForRr && stopLoss is decimal slForRr && takeProfit is decimal tpForRr)
        {
            decimal reward = direction == DirectionCandidate.BUY_CANDIDATE ? tpForRr - entryForRr : entryForRr - tpForRr;
            decimal risk = direction == DirectionCandidate.BUY_CANDIDATE ? entryForRr - slForRr : slForRr - entryForRr;

            if (risk > 0m)
            {
                riskRewardRatio = (double)(reward / risk);
            }
        }

        (TradePlanStatus status, string? invalidationReason) = ClassifyStatus(entryPrice, stopLoss, takeProfit, positionSize, degenerateRisk);

        return new TradePlan(
            IsValid: status == TradePlanStatus.PLAN_READY,
            Status: status,
            Direction: direction,
            EntryPrice: entryPrice,
            StopLoss: stopLoss,
            TakeProfit: takeProfit,
            RiskPerUnit: riskPerUnit,
            RiskAmount: riskAmount,
            PositionSize: positionSize,
            RiskRewardRatio: riskRewardRatio,
            InvalidationReason: invalidationReason,
            Diagnostics: diagnostics.AsReadOnly(),
            Timestamp: DateTime.UtcNow);
    }

    // Phase 10: status classification. PLAN_BLOCKED is reserved for a computation that broke down
    // despite nominal inputs (missing entry, non-positive risk) - never NaN/Infinity, never a fictional
    // fallback value. SIGNAL_ONLY is reserved for a categorical capability gap (a component the system
    // has no source for yet, e.g. StopLoss/PositionSize today).
    private static (TradePlanStatus Status, string? InvalidationReason) ClassifyStatus(
        decimal? entryPrice, decimal? stopLoss, decimal? takeProfit, int? positionSize, bool degenerateRisk)
    {
        if (entryPrice is null)
        {
            return (TradePlanStatus.PLAN_BLOCKED, "Entry price unavailable (CurrentPrice invalid).");
        }

        if (degenerateRisk)
        {
            return (TradePlanStatus.PLAN_BLOCKED, "Computed risk is zero; refusing to size a trade against a zero-distance stop.");
        }

        var missing = new List<string>();
        if (stopLoss is null) missing.Add("Stop loss unavailable");
        if (takeProfit is null) missing.Add("Take profit unavailable");
        if (positionSize is null) missing.Add("Position sizing unavailable");

        if (missing.Count > 0)
        {
            return (TradePlanStatus.SIGNAL_ONLY, string.Join("; ", missing));
        }

        return (TradePlanStatus.PLAN_READY, null);
    }

    private static string DescribeNoTradeReason(DirectionCandidate direction, EntryTriggerReason reason)
    {
        if (direction == DirectionCandidate.WATCH)
        {
            return "Signal on watchlist only (not yet a directional candidate).";
        }

        return reason switch
        {
            EntryTriggerReason.DECISION_AMBIGUOUS => "Decision became ambiguous.",
            EntryTriggerReason.DYNAMIC_ZSCORE_UNAVAILABLE => "Entry trigger lost (DynamicZScore unavailable).",
            EntryTriggerReason.PRICE_AT_EQUILIBRIUM => "No directional deviation (price at equilibrium).",
            _ => $"No directional candidate (Reason={reason})."
        };
    }
}
