using System;
using System.Collections.Generic;
using System.Linq;
using IQIAIndicator.Backtest.Cost;
using IQIAIndicator.Backtest.Execution;
using IQIAIndicator.Engine.EntryTrigger;
using IQIAIndicator.Engine.Risk;

namespace IQIAIndicator.Backtest.Risk;

/// <summary>
/// Sprint 15.25 (Lot 14.8, brief §3/§5/§6/§7/§9/§10). The <c>Signal -&gt; Risk Evaluation -&gt; Position
/// Sizing</c> step of the brief's own architecture diagram, for ONE candidate position. STATELESS BY
/// DESIGN (brief §17 "Run Isolation"/§20 Invariant 6): static class, no fields; every call is a pure
/// function of its inputs.
///
/// Reuses <see cref="Engine.Risk.RiskEngine"/> (Lot 10, unmodified, already ATAS-free and deterministic)
/// for the risk-budget/position-sizing math (brief §5/§6/§7 are already exactly what
/// <see cref="Engine.Risk.RiskEngine.Evaluate"/> computes) - this class only ADDS what does not exist
/// anywhere else: resolving a <see cref="RiskDistanceConfiguration"/> into a StopLoss price (brief §8,
/// since the backtest signal pipeline never populates <c>TradePlan.StopLoss</c> - see this lot's report
/// §2), and the exposure clamp (brief §10, entirely new).
///
/// PRIORITY ORDER (brief §10: "Risk limit -&gt; Quantity limit -&gt; Exposure limit"): RiskEngine.Evaluate's
/// own Phase 9 already combines Risk-limit and Quantity-limit into one clamped PositionSize; this method
/// applies the caller's RequestedQuantity and the Exposure limit as the final two clamps on top, in that
/// order, so the result always respects every constraint simultaneously (brief §10: "Le résultat final doit
/// respecter TOUTES les contraintes").
/// </summary>
public static class PositionRiskEvaluator
{
    public static RiskEvaluationResult Evaluate(
        SimulatedPosition position,
        int requestedQuantity,
        AccountState account,
        InstrumentRiskSpecification instrument,
        RiskPolicy policy,
        BacktestRiskConfiguration riskConfiguration,
        ExecutionCostConfiguration costConfiguration)
    {
        ArgumentNullException.ThrowIfNull(position);
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(instrument);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(riskConfiguration);
        ArgumentNullException.ThrowIfNull(costConfiguration);

        if (requestedQuantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(requestedQuantity), requestedQuantity, "RequestedQuantity must be strictly positive.");

        // Brief §12: LONG/SHORT are handled identically below - only Closed positions (Direction is
        // necessarily BUY_CANDIDATE/SELL_CANDIDATE, per Lot 14.5's own invariant) ever reach this branch.
        if (position.Status != PositionStatus.Closed || position.EntryPrice is not decimal theoreticalEntry)
            return Rejected(requestedQuantity, PositionRiskReason.PositionNotExecutable, riskConfiguration.MaxExposure,
                new[] { "Position never reached Closed - nothing to risk-evaluate." });

        // Brief §4: EnableRiskControls is a real kill switch, independent of every sub-value - historical
        // Lot 14.6/14.7 behaviour (AllowedQuantity == RequestedQuantity) is reproduced exactly.
        if (!riskConfiguration.EnableRiskControls)
        {
            return new RiskEvaluationResult(
                IsAllowed: true, RequestedQuantity: requestedQuantity, AllowedQuantity: requestedQuantity,
                RiskConstrainedQuantity: null, ExposureConstrainedQuantity: null,
                RiskPerUnit: null, TotalEstimatedRisk: null, MaximumAllowedRisk: null,
                Exposure: null, MaxExposure: riskConfiguration.MaxExposure,
                Reason: PositionRiskReason.Allowed,
                Diagnostics: new[] { "Risk controls disabled - historical Lot 14.6/14.7 behaviour preserved." });
        }

        if (!instrument.IsValid)
            return Rejected(requestedQuantity, PositionRiskReason.InvalidInstrument, riskConfiguration.MaxExposure,
                new[] { "InstrumentRiskSpecification is invalid (see InstrumentRiskSpecification.IsValid)." });

        // Brief §8: no invented stop - a non-positive configured risk distance means "not calculable".
        if (riskConfiguration.RiskDistance.PriceUnits <= 0m)
            return Rejected(requestedQuantity, PositionRiskReason.InvalidRiskDistance, riskConfiguration.MaxExposure,
                new[] { "No usable RiskDistance is configured (RiskDistanceConfiguration.PriceUnits <= 0) - risk-based sizing is not calculable." });

        TradeDirection direction = MapDirection(position.Direction);

        // Brief §7: direction-aware StopLoss price, resolved once here from the configured distance -
        // never derived from TradePlan (unavailable) nor invented from an ATR/heuristic.
        decimal stopLoss = direction == TradeDirection.Buy
            ? theoreticalEntry - riskConfiguration.RiskDistance.PriceUnits
            : theoreticalEntry + riskConfiguration.RiskDistance.PriceUnits;

        var riskRequest = new RiskEngineRequest(direction, theoreticalEntry, stopLoss, null, instrument, account, policy);
        RiskAssessment assessment = new RiskEngine().Evaluate(riskRequest);

        if (!assessment.IsAccepted || assessment.PositionSize is not int riskQuantity || riskQuantity <= 0)
            return Rejected(requestedQuantity, MapReason(assessment.RejectionReasons), riskConfiguration.MaxExposure,
                assessment.Diagnostics, maximumAllowedRisk: assessment.RiskBudget, riskPerUnit: assessment.RiskPerUnit);

        // Brief §11: combine RequestedQuantity with the Risk-Engine-derived quantity first.
        int candidateQuantity = Math.Min(requestedQuantity, riskQuantity);

        // Brief §10: exposure limit, computed on the Lot 14.7 EXECUTED entry price (never the theoretical
        // price) - the notional actually committed at the real fill, per the brief's own formula
        // "Exposure = ExecutedPrice x Quantity x ContractMultiplier". Price itself does not depend on
        // quantity (Backtest.Cost.ExecutionPriceModel), so it is safe to resolve once, independent of the
        // final clamp.
        int? exposureQuantity = null;
        decimal? finalExposure = null;
        if (riskConfiguration.MaxExposure is decimal maxExposure)
        {
            OrderSide entrySide = ExecutionPriceModel.EntryFillSide(position.Direction);
            ExecutedPrice entryFill = ExecutionPriceModel.Apply(
                theoreticalEntry, entrySide, candidateQuantity, position.EntryTimestamp,
                costConfiguration.Slippage, costConfiguration.Spread);
            decimal multiplier = instrument.ContractMultiplier ?? 1m;
            decimal perUnitExposure = entryFill.Price * multiplier;

            exposureQuantity = perUnitExposure > 0m ? (int)Math.Floor(maxExposure / perUnitExposure) : 0;
            if (exposureQuantity < 0)
                exposureQuantity = 0;
        }

        int finalQuantity = exposureQuantity is int eq ? Math.Min(candidateQuantity, eq) : candidateQuantity;

        if (finalQuantity <= 0)
        {
            PositionRiskReason reason = exposureQuantity is int e2 && e2 <= 0
                ? PositionRiskReason.ExposureLimitExceeded
                : PositionRiskReason.ZeroRisk;
            return Rejected(requestedQuantity, reason, riskConfiguration.MaxExposure,
                new[] { "FinalQuantity resolved to zero or less after combining requested/risk/exposure constraints." },
                maximumAllowedRisk: assessment.RiskBudget, riskPerUnit: assessment.RiskPerUnit,
                riskConstrainedQuantity: riskQuantity, exposureConstrainedQuantity: exposureQuantity);
        }

        if (riskConfiguration.MaxExposure is not null)
        {
            OrderSide entrySide = ExecutionPriceModel.EntryFillSide(position.Direction);
            ExecutedPrice finalFill = ExecutionPriceModel.Apply(
                theoreticalEntry, entrySide, finalQuantity, position.EntryTimestamp,
                costConfiguration.Slippage, costConfiguration.Spread);
            decimal multiplier = instrument.ContractMultiplier ?? 1m;
            finalExposure = finalFill.Price * multiplier * finalQuantity;
        }

        return new RiskEvaluationResult(
            IsAllowed: true, RequestedQuantity: requestedQuantity, AllowedQuantity: finalQuantity,
            RiskConstrainedQuantity: riskQuantity, ExposureConstrainedQuantity: exposureQuantity,
            RiskPerUnit: assessment.RiskPerUnit, TotalEstimatedRisk: assessment.RiskPerUnit * finalQuantity,
            MaximumAllowedRisk: assessment.RiskBudget, Exposure: finalExposure, MaxExposure: riskConfiguration.MaxExposure,
            Reason: PositionRiskReason.Allowed, Diagnostics: assessment.Diagnostics);
    }

    /// <summary>Only ever called for a Closed position (brief §12), whose Direction is guaranteed to be
    /// BUY_CANDIDATE/SELL_CANDIDATE by Lot 14.5's own invariant.</summary>
    private static TradeDirection MapDirection(DirectionCandidate direction) => direction switch
    {
        DirectionCandidate.BUY_CANDIDATE => TradeDirection.Buy,
        DirectionCandidate.SELL_CANDIDATE => TradeDirection.Sell,
        _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, "Only a Closed position's BUY_CANDIDATE/SELL_CANDIDATE direction is ever risk-evaluated.")
    };

    /// <summary>Maps the Engine.Risk vocabulary onto this lot's smaller, backtest-facing reason set (see
    /// PositionRiskReason's own doc comment for the full table). RiskEngine.Evaluate can report several
    /// reasons at once; the first match in this priority order becomes the single primary Reason.</summary>
    private static PositionRiskReason MapReason(IReadOnlyList<RiskRejectionReason> reasons)
    {
        if (reasons.Contains(RiskRejectionReason.INSTRUMENT_SPEC_INVALID))
            return PositionRiskReason.InvalidInstrument;
        if (reasons.Contains(RiskRejectionReason.INVALID_STOP_LOSS))
            return PositionRiskReason.InvalidRiskDistance;
        if (reasons.Contains(RiskRejectionReason.QUANTITY_LIMIT))
            return PositionRiskReason.MaxQuantityExceeded;
        if (reasons.Contains(RiskRejectionReason.POSITION_SIZE_INVALID))
            return PositionRiskReason.ZeroRisk;

        // INVALID_CAPITAL, INVALID_EQUITY, RISK_BUDGET_EXCEEDED, MAX_DRAWDOWN_REACHED, DAILY_LOSS_LIMIT,
        // OPEN_RISK_LIMIT, and the never-realistically-reached INVALID_ENTRY/INVALID_TAKE_PROFIT/
        // INVALID_RISK_REWARD/UNKNOWN_ERROR (this lot never supplies a TakeProfit, and EntryPrice always
        // comes from an already-validated SimulatedPosition) all bucket as "the account/policy could not
        // afford this trade's risk".
        return PositionRiskReason.RiskLimitExceeded;
    }

    private static RiskEvaluationResult Rejected(
        int requestedQuantity, PositionRiskReason reason, decimal? maxExposure, IReadOnlyList<string> diagnostics,
        decimal? maximumAllowedRisk = null, decimal? riskPerUnit = null,
        int? riskConstrainedQuantity = null, int? exposureConstrainedQuantity = null) =>
        new(
            IsAllowed: false, RequestedQuantity: requestedQuantity, AllowedQuantity: 0,
            RiskConstrainedQuantity: riskConstrainedQuantity, ExposureConstrainedQuantity: exposureConstrainedQuantity,
            RiskPerUnit: riskPerUnit, TotalEstimatedRisk: null, MaximumAllowedRisk: maximumAllowedRisk,
            Exposure: null, MaxExposure: maxExposure,
            Reason: reason, Diagnostics: diagnostics);
}
