using System.Collections.Generic;
using ATAS.DataFeedsCore;
using ATAS.DataFeedsCore.Statistics;
using IQIAIndicator.Engine.Risk;

namespace IQIAIndicator.Infrastructure.ATAS;

/// <summary>
/// Sprint 15.25 (Lot 12.3) - TEMPORARY DIAGNOSTIC, not consumed by RiskEngine or any decision path.
/// Captures the exact raw ATAS values behind ATASAccountStateAdapter/ATASInstrumentAdapter (Lot 12.2),
/// side by side with the final resolved value each one produced, so a real Replay/Live session can be
/// inspected to answer precisely why CurrentEquity/InstrumentRiskSpecification arrive at RiskEngine as
/// invalid. Every field is nullable and left null when genuinely unavailable - never a fabricated
/// value; formatting null as "UNAVAILABLE" (as opposed to "0") is done by the caller (IQIAIndicator.cs),
/// this record only carries the raw, un-formatted data. Candidate for removal once the Lot 12.3 root
/// cause is confirmed and, if ever needed, addressed by a later lot - see the Lot 12.3 report.
/// </summary>
public sealed record ATASAccountDiagnostic(
    string? AccountID,
    bool? IsRealAccount,
    string? Currency,
    decimal? Balance,
    decimal? BalanceAvailable,
    decimal? BalancePower,
    decimal? OpenPnL,
    decimal? ClosedPnL,
    decimal? TotalPnL,
    decimal? PositionVolume,
    decimal? PositionUnrealizedPnL,
    decimal? PositionRealizedPnL,
    decimal? RealtimeEquity,
    decimal? ReplayEquity,
    decimal FinalEquityUsed);

/// <summary>Sprint 15.25 (Lot 12.3) - TEMPORARY DIAGNOSTIC, see ATASAccountDiagnostic's doc comment
/// (same rules: raw ATAS values only, never fabricated, never consumed by RiskEngine).</summary>
public sealed record ATASInstrumentDiagnostic(
    string? Instrument,
    decimal? TickSize,
    decimal? TickCost,
    decimal? LotSize,
    decimal? LotMinSize,
    decimal? LotMaxSize,
    int? Digits,
    string? BaseCurrency,
    string? QuoteCurrency,
    InstrumentRiskSpecification FinalSpecification,
    IReadOnlyList<string> InvalidFieldReasons);

/// <summary>
/// Sprint 15.25 (Lot 12.3). Pure, read-only capture functions - no ATAS side effects, no mutation, no
/// influence on RiskEngine/TradePlan/EntryTrigger. CaptureAccount/CaptureInstrument only read already-
/// accessible ATAS objects (the same Portfolio/Position/Security/ITradingStatisticsProvider
/// ATASAccountStateAdapter/ATASInstrumentAdapter already read, Lot 12.2); ExplainInvalidFields reads
/// only InstrumentRiskSpecification's own public fields (Lot 10, unmodified) to report exactly which
/// ones fail InstrumentRiskSpecification.IsValid, without touching that file.
/// </summary>
public static class ATASRuntimeDiagnostics
{
    public static ATASAccountDiagnostic CaptureAccount(
        Portfolio? portfolio,
        Position? position,
        ITradingStatisticsProvider? statisticsProvider,
        decimal finalEquityUsed)
    {
        decimal? realtimeEquity = ATASAccountStateAdapter.TryGetCurrentEquity(statisticsProvider, isReplay: false);
        decimal? replayEquity = ATASAccountStateAdapter.TryGetCurrentEquity(statisticsProvider, isReplay: true);

        return new ATASAccountDiagnostic(
            portfolio?.AccountID,
            portfolio?.IsRealAccount,
            portfolio?.Currency?.ToString(),
            portfolio?.Balance,
            portfolio?.BalanceAvailable,
            portfolio?.BalancePower,
            portfolio?.OpenPnL,
            portfolio?.ClosedPnL,
            portfolio?.TotalPnL,
            position?.Volume,
            position?.UnrealizedPnL,
            position?.RealizedPnL,
            realtimeEquity,
            replayEquity,
            finalEquityUsed);
    }

    public static ATASInstrumentDiagnostic CaptureInstrument(Security? security, InstrumentRiskSpecification finalSpecification) =>
        new(
            security?.Instrument,
            security?.TickSize,
            security?.TickCost,
            security?.LotSize,
            security?.LotMinSize,
            security?.LotMaxSize,
            security?.Digits,
            security?.BaseCurrency,
            security?.QuoteCurrency,
            finalSpecification,
            ExplainInvalidFields(finalSpecification));

    /// <summary>Names every field of <paramref name="spec"/> that fails InstrumentRiskSpecification.IsValid
    /// (Lot 10, read-only) and why - empty when the specification is already valid.</summary>
    public static IReadOnlyList<string> ExplainInvalidFields(InstrumentRiskSpecification spec)
    {
        var reasons = new List<string>();

        if (string.IsNullOrWhiteSpace(spec.Symbol))
            reasons.Add("Symbol is empty");
        if (spec.TickSize <= 0m)
            reasons.Add($"TickSize <= 0 (actual={spec.TickSize})");
        if (spec.TickValue <= 0m)
            reasons.Add($"TickValue <= 0 (actual={spec.TickValue})");
        if (spec.PointValue <= 0m)
            reasons.Add($"PointValue <= 0 (actual={spec.PointValue})");
        if (spec.MinQuantity <= 0)
            reasons.Add($"MinQuantity <= 0 (actual={spec.MinQuantity})");
        if (spec.MaxQuantity < spec.MinQuantity)
            reasons.Add($"MaxQuantity < MinQuantity (MaxQuantity={spec.MaxQuantity}, MinQuantity={spec.MinQuantity})");
        if (spec.QuantityStep <= 0)
            reasons.Add($"QuantityStep <= 0 (actual={spec.QuantityStep})");
        if (spec.ContractMultiplier is decimal contractMultiplier && contractMultiplier <= 0m)
            reasons.Add($"ContractMultiplier <= 0 (actual={contractMultiplier})");

        return reasons;
    }
}
