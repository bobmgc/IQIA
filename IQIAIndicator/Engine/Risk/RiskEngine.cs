using System;
using System.Collections.Generic;

namespace IQIAIndicator.Engine.Risk;

/// <summary>
/// Sprint 15.25 (Lot 10). Transforms a candidate trade into a complete, structured risk decision:
/// Capital/Account State -> Risk Budget -> Trade Risk -> Position Sizing -> SL/TP validation ->
/// Risk/Reward -> Risk Constraints -> Risk Decision (see Section 2 of the Lot 10 specification). Pure,
/// deterministic, no ATAS/clock/network dependency and no global mutable state - every input is supplied
/// explicitly via RiskEngineRequest. Never invents a value it cannot honestly derive from the inputs
/// (mirrors TradePlanBuilder's own discipline).
/// </summary>
public sealed class RiskEngine
{
    public RiskAssessment Evaluate(RiskEngineRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var reasons = new List<RiskRejectionReason>();
        var diagnostics = new List<string>();

        // Phase 1: Capital / Equity validation (Section 3, Section 8).
        bool validCapital = request.Account.InitialCapital > 0m;
        if (!validCapital)
        {
            reasons.Add(RiskRejectionReason.INVALID_CAPITAL);
            diagnostics.Add($"InitialCapital must be positive. Actual={request.Account.InitialCapital}.");
        }

        bool validEquity = request.Account.CurrentEquity > 0m;
        if (!validEquity)
        {
            reasons.Add(RiskRejectionReason.INVALID_EQUITY);
            diagnostics.Add($"CurrentEquity must be positive. Actual={request.Account.CurrentEquity}.");
        }

        // Phase 2: Instrument specification validation (Section 5, Section 8).
        bool validInstrument = request.Instrument.IsValid;
        if (!validInstrument)
        {
            reasons.Add(RiskRejectionReason.INSTRUMENT_SPEC_INVALID);
            diagnostics.Add("InstrumentRiskSpecification is invalid (TickSize/TickValue/PointValue/quantity bounds - see InstrumentRiskSpecification.IsValid).");
        }

        // Phase 3: Entry price validation.
        bool validEntry = request.EntryPrice > 0m;
        if (!validEntry)
        {
            reasons.Add(RiskRejectionReason.INVALID_ENTRY);
            diagnostics.Add($"EntryPrice must be positive. Actual={request.EntryPrice}.");
        }

        // Phase 4: Stop Loss validation - direction-aware. A SL on the wrong side of Entry (or missing
        // entirely) is never silently accepted (Section 6, Section 10).
        decimal? riskDistance = null;
        bool validStopLoss = false;
        if (validEntry && request.StopLoss is decimal sl)
        {
            riskDistance = request.Direction == TradeDirection.Buy
                ? request.EntryPrice - sl
                : sl - request.EntryPrice;
            validStopLoss = riskDistance > 0m;
        }

        if (!validStopLoss)
        {
            reasons.Add(RiskRejectionReason.INVALID_STOP_LOSS);
            diagnostics.Add(request.StopLoss is null
                ? "StopLoss was not provided."
                : $"StopLoss is on the wrong side of Entry for {request.Direction}. Entry={request.EntryPrice}, StopLoss={request.StopLoss}.");
            riskDistance = null;
        }

        // Phase 5: Risk per unit, in account currency = RiskDistance x PointValue (Section 6).
        decimal? riskPerUnit = null;
        if (validInstrument && riskDistance is decimal rd)
        {
            riskPerUnit = rd * request.Instrument.PointValue;
        }

        // Phase 6: Take Profit / Reward distance validation (Section 9). A candidate with no TakeProfit at
        // all is not itself a rejection here - only Phase 10's MinRiskReward check can require one.
        decimal? rewardDistance = null;
        bool takeProfitProvided = request.TakeProfit is not null;
        bool validTakeProfit = true;
        if (validEntry && request.TakeProfit is decimal tp)
        {
            rewardDistance = request.Direction == TradeDirection.Buy
                ? tp - request.EntryPrice
                : request.EntryPrice - tp;
            validTakeProfit = rewardDistance > 0m;
            if (!validTakeProfit)
            {
                reasons.Add(RiskRejectionReason.INVALID_TAKE_PROFIT);
                diagnostics.Add($"TakeProfit is not on the profitable side of Entry for {request.Direction}. Entry={request.EntryPrice}, TakeProfit={request.TakeProfit}.");
                rewardDistance = null;
            }
        }

        // Phase 7: Drawdown / Daily loss / Open risk constraints (Section 8). Evaluated independently of
        // one another so that several simultaneous violations are all reported, not just the first one hit.
        bool drawdownBreached = false;
        if (request.Policy.MaxDrawdownPercent is decimal maxDdPct && request.Account.CurrentDrawdownPercent is decimal ddPct && ddPct >= maxDdPct)
        {
            drawdownBreached = true;
        }
        if (request.Policy.MaxDrawdownAmount is decimal maxDdAmt && request.Account.CurrentDrawdown >= maxDdAmt)
        {
            drawdownBreached = true;
        }
        if (drawdownBreached)
        {
            reasons.Add(RiskRejectionReason.MAX_DRAWDOWN_REACHED);
            diagnostics.Add($"CurrentDrawdown ({request.Account.CurrentDrawdown}) breaches the configured maximum drawdown.");
        }

        decimal dailyLossSoFar = Math.Max(0m, -request.Account.DailyPnL);
        decimal? dailyLossRemaining = null;
        bool dailyLossBreached = false;
        decimal? maxDailyLossFromPercent = request.Policy.MaxDailyLossPercent is decimal maxDlPct && request.Account.DailyStartingEquity > 0m
            ? request.Account.DailyStartingEquity * maxDlPct
            : null;
        decimal? maxDailyLoss = Min(maxDailyLossFromPercent, request.Policy.MaxDailyLossAmount);
        if (maxDailyLoss is decimal mdl)
        {
            dailyLossRemaining = mdl - dailyLossSoFar;
            dailyLossBreached = dailyLossRemaining <= 0m;
        }
        if (dailyLossBreached)
        {
            reasons.Add(RiskRejectionReason.DAILY_LOSS_LIMIT);
            diagnostics.Add($"DailyPnL ({request.Account.DailyPnL}) has exhausted the configured daily loss limit.");
        }

        decimal? openRiskRemaining = null;
        bool openRiskBreached = false;
        decimal? maxOpenRiskFromPercent = request.Policy.MaxOpenRiskPercent is decimal maxOrPct && validEquity
            ? request.Account.CurrentEquity * maxOrPct
            : null;
        decimal? maxOpenRisk = Min(maxOpenRiskFromPercent, request.Policy.MaxOpenRiskAmount);
        if (maxOpenRisk is decimal mor)
        {
            openRiskRemaining = mor - request.Account.OpenRisk;
            openRiskBreached = openRiskRemaining <= 0m;
        }
        if (openRiskBreached)
        {
            reasons.Add(RiskRejectionReason.OPEN_RISK_LIMIT);
            diagnostics.Add($"OpenRisk ({request.Account.OpenRisk}) has exhausted the configured open-risk limit.");
        }

        // Phase 8: Risk budget for this trade (Section 7-8) = min(per-trade caps, remaining daily-loss
        // room, remaining open-risk room), zeroed outright once max drawdown is reached.
        decimal? riskBudget = null;
        if (validEquity)
        {
            decimal? perTradeFromPercent = request.Policy.MaxRiskPerTradePercent is decimal rp ? request.Account.CurrentEquity * rp : null;
            riskBudget = Min(perTradeFromPercent, request.Policy.MaxRiskPerTradeAmount);
            riskBudget = Min(riskBudget, dailyLossRemaining);
            riskBudget = Min(riskBudget, openRiskRemaining);

            if (drawdownBreached)
            {
                riskBudget = 0m;
            }
        }

        bool riskBudgetExhausted = riskBudget is null || riskBudget <= 0m;
        if (riskBudgetExhausted && validEquity && !drawdownBreached && !dailyLossBreached && !openRiskBreached)
        {
            // Only reported when no other capital constraint already explains the zero/undefined budget.
            reasons.Add(RiskRejectionReason.RISK_BUDGET_EXCEEDED);
            diagnostics.Add(riskBudget is null
                ? "RiskBudget is undefined: no MaxRiskPerTradePercent/MaxRiskPerTradeAmount is configured in the RiskPolicy."
                : $"RiskBudget resolved to {riskBudget}, which cannot size a trade.");
        }

        // Phase 9: Position sizing (Section 7) - floor to whole contracts, round down to the nearest
        // QuantityStep, then clamp into [MinQuantity, MaxQuantity] / MaxPositionSize. Never rounds up.
        int? positionSize = null;
        decimal? riskAmount = null;
        if (validInstrument && !riskBudgetExhausted && riskPerUnit is decimal rpu && rpu > 0m && riskBudget is decimal budget)
        {
            int raw = (int)Math.Floor(budget / rpu);
            int stepped = request.Instrument.QuantityStep > 0
                ? raw - (raw % request.Instrument.QuantityStep)
                : raw;

            if (stepped <= 0)
            {
                reasons.Add(RiskRejectionReason.POSITION_SIZE_INVALID);
                diagnostics.Add($"RiskBudget ({budget}) is smaller than one QuantityStep ({request.Instrument.QuantityStep}) worth of RiskPerUnit ({rpu}) - would round down to zero contracts.");
            }
            else if (stepped < request.Instrument.MinQuantity)
            {
                reasons.Add(RiskRejectionReason.QUANTITY_LIMIT);
                diagnostics.Add($"Affordable size ({stepped}) is below MinQuantity ({request.Instrument.MinQuantity}); taking MinQuantity would exceed the risk budget.");
            }
            else
            {
                int clamped = Math.Min(stepped, request.Instrument.MaxQuantity);
                if (request.Policy.MaxPositionSize is int maxPolicySize)
                {
                    clamped = Math.Min(clamped, maxPolicySize);
                }
                if (clamped < stepped)
                {
                    diagnostics.Add($"Position size clamped from {stepped} to {clamped} by MaxQuantity/MaxPositionSize.");
                }

                positionSize = clamped;
                riskAmount = rpu * clamped;
            }
        }

        // Phase 10: Risk/Reward (Section 9-10). RR uses price distances directly (consistent with
        // TradePlanBuilder's existing RR calculation) - never divides by a zero/negative risk distance.
        double? riskRewardRatio = null;
        if (riskDistance is decimal riskD && riskD > 0m && rewardDistance is decimal rewardD)
        {
            riskRewardRatio = (double)(rewardD / riskD);
        }

        decimal? rewardAmount = null;
        if (validInstrument && rewardDistance is decimal rewardDForAmount && positionSize is int sizeForReward)
        {
            rewardAmount = rewardDForAmount * request.Instrument.PointValue * sizeForReward;
        }

        if (request.Policy.MinRiskReward is double minRr)
        {
            if (riskRewardRatio is null)
            {
                if (validStopLoss && validTakeProfit)
                {
                    // A minimum RR is mandated but there is nothing to verify it against (no TakeProfit).
                    reasons.Add(RiskRejectionReason.INVALID_TAKE_PROFIT);
                    diagnostics.Add("MinRiskReward is configured but no valid TakeProfit was provided to verify it against.");
                }
            }
            else if (riskRewardRatio < minRr)
            {
                reasons.Add(RiskRejectionReason.INVALID_RISK_REWARD);
                diagnostics.Add($"RiskRewardRatio ({riskRewardRatio:F2}) is below the configured MinRiskReward ({minRr:F2}).");
            }
        }

        // Phase 11: Safety net - a trade must never be ACCEPTED without a resolved PositionSize. If every
        // upstream input was nominally valid yet sizing still failed to resolve for an unaccounted reason,
        // report it explicitly rather than silently accepting a plan with no size.
        if (positionSize is null
            && validCapital && validEquity && validInstrument && validEntry && validStopLoss && !riskBudgetExhausted
            && !reasons.Contains(RiskRejectionReason.POSITION_SIZE_INVALID)
            && !reasons.Contains(RiskRejectionReason.QUANTITY_LIMIT))
        {
            reasons.Add(RiskRejectionReason.POSITION_SIZE_INVALID);
            diagnostics.Add("PositionSize could not be resolved despite nominally valid inputs.");
        }

        RiskDecisionStatus status = reasons.Count == 0 && positionSize is not null
            ? RiskDecisionStatus.ACCEPTED
            : RiskDecisionStatus.REJECTED;

        return new RiskAssessment(
            Status: status,
            RejectionReasons: reasons.AsReadOnly(),
            Direction: request.Direction,
            EntryPrice: request.EntryPrice,
            StopLoss: request.StopLoss,
            TakeProfit: request.TakeProfit,
            RiskDistance: riskDistance,
            RewardDistance: rewardDistance,
            RiskPerUnit: riskPerUnit,
            RiskBudget: riskBudget,
            PositionSize: positionSize,
            RiskAmount: riskAmount,
            RewardAmount: rewardAmount,
            RiskRewardRatio: riskRewardRatio,
            Diagnostics: diagnostics.AsReadOnly());
    }

    private static decimal? Min(decimal? a, decimal? b)
    {
        if (a is null) return b;
        if (b is null) return a;
        return Math.Min(a.Value, b.Value);
    }
}
