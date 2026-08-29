using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using IQIAIndicator.Core.Observability;
using IQIAIndicator.Engine.Decision.Arbitration;
using IQIAIndicator.Engine.Decision.Core;
using IQIAIndicator.Engine.Entry;
using IQIAIndicator.Engine.EntryTrigger;
using IQIAIndicator.Engine.Risk;
using IQIAIndicator.Engine.ScientificFusion;
using IQIAIndicator.Engine.ScientificModels.Abstractions;
using IQIAIndicator.Infrastructure.ATAS;

namespace IQIAIndicator.Core.Calibration;

using ScientificMarketContext = global::IQIAIndicator.Engine.ScientificModels.Abstractions.MarketContext;

// Sprint 15.17 (QDE-012 real-market capture): Open/High/Low/Volume added as trailing OPTIONAL
// positional parameters (default 0m) specifically so the two pre-existing call sites that construct
// this record positionally (Tests/Calibration/ScientificDatasetCollectorTests.cs,
// Tests/Dashboards/DatasetDashboardStatusTests.cs) keep compiling unchanged. CurrentPrice is kept as
// the historical field name (it already carries the bar's Close - see From() below) rather than
// renamed, for the same reason. Do not reorder existing parameters.
public sealed record ScientificDatasetRecord(
    Guid SessionId,
    DateTime Timestamp,
    string Symbol,
    string TimeFrame,
    decimal CurrentPrice,
    int HistoryLength,
    int CurrentBar,
    IReadOnlyDictionary<string, double?> Metrics,
    IReadOnlyDictionary<string, string> Categories,
    decimal Open = 0m,
    decimal High = 0m,
    decimal Low = 0m,
    decimal Volume = 0m)
{
    /// <summary>Close is CurrentPrice under its historical name - see the ScientificDatasetRecord doc
    /// comment above. Exposed under its OHLCV name too so consumers (e.g. the OHLCV CSV export) never
    /// have to know about the historical alias.</summary>
    public decimal Close => CurrentPrice;

    public static ScientificDatasetRecord From(
        Guid sessionId,
        int currentBar,
        ScientificMarketContext marketContext,
        ScientificAssessment scientificAssessment,
        DecisionResult decisionResult,
        decimal open,
        decimal high,
        decimal low,
        decimal volume,
        PipelineTraceRun? trace = null,
        EntryCandidate? entryCandidate = null,
        EntryTriggerCandidate? entryTriggerCandidate = null,
        global::IQIAIndicator.Engine.TradePlan.TradePlan? tradePlan = null,
        RiskAssessment? riskAssessment = null,
        // Sprint 15.25 (Lot 12.5 - ATAS raw risk telemetry): see the dedicated comment block below for
        // the full rationale. All optional/trailing, same additive discipline as every parameter above.
        ATASAccountDiagnostic? atasAccountDiagnostic = null,
        ATASInstrumentDiagnostic? atasInstrumentDiagnostic = null,
        bool? atasEquityIsReplay = null,
        int? atasEquitySeriesCount = null,
        DateTime? atasEquityLastTimestamp = null,
        AccountState? riskAccountState = null,
        InstrumentRiskSpecification? riskInstrumentSpec = null,
        RiskEngineRequest? riskEngineRequest = null,
        // Sprint 15.25 (Lot 12.6 - ATAS binding correction): see the dedicated comment block below for
        // the full rationale. All optional/trailing, same additive discipline as every parameter above.
        bool? atasEquityHeuristicIsReplay = null,
        bool? atasPortfolioIsReplay = null,
        string? minQuantitySource = null,
        string? maxQuantitySource = null,
        // Sprint 15.25 (Lot 12.12 - Problem A/C and Problem B): see the dedicated comment block below
        // for the full rationale. All optional/trailing, same additive discipline as every parameter
        // above.
        // Sprint 15.25 (Lot 12.12): "global::" qualification required here - the root namespace
        // IQIAIndicator also contains a class literally named IQIAIndicator (the indicator entry point),
        // which shadows the namespace for an unqualified "IQIAIndicator.Core...." reference from a call
        // site inside that class (see IQIAIndicator.cs's own "global::IQIAIndicator.Engine..." usages
        // for the same, pre-existing reason - Lot 12.11 report).
        global::IQIAIndicator.Core.AtasDataContext? atasContext = null,
        string? riskStageError = null)
    {
        if (sessionId == Guid.Empty)
            throw new ArgumentException("SessionId is required.", nameof(sessionId));

        ArgumentNullException.ThrowIfNull(marketContext);
        ArgumentNullException.ThrowIfNull(scientificAssessment);
        ArgumentNullException.ThrowIfNull(decisionResult);

        var metrics = new Dictionary<string, double?>(StringComparer.Ordinal);
        var categories = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (ScientificModelResult result in scientificAssessment.ScientificResults)
        {
            AddMetric(metrics, categories, result.ModelName, "Score", result.Score);
            if (result.Metrics is null)
                continue;

            foreach (KeyValuePair<string, object> metric in result.Metrics)
            {
                if (TryConvertDouble(metric.Value, out double numericValue))
                    AddMetric(metrics, categories, result.ModelName, metric.Key, numericValue);
                else if (metric.Value is string text)
                    categories[$"{result.ModelName}.{metric.Key}"] = text;
            }
        }

        AddMetric(metrics, categories, "Fusion", "OverallConfidence", scientificAssessment.OverallConfidence);
        AddMetric(metrics, categories, "Decision", "ScientificScore", FirstCandidate(decisionResult)?.ScientificScore);
        AddMetric(metrics, categories, "Decision", "QualityScore", FirstCandidate(decisionResult)?.QualityScore);
        AddMetric(metrics, categories, "Decision", "FinalScore", decisionResult.WinnerScore);

        categories["Fusion.SuccessfulModels"] = string.Join(";", scientificAssessment.SuccessfulModels);
        categories["Fusion.FailedModels"] = string.Join(";", scientificAssessment.FailedModels);
        categories["Fusion.EvidenceAgreement"] = string.Join(";", scientificAssessment.EvidenceAgreement);
        categories["Fusion.EvidenceConflict"] = string.Join(";", scientificAssessment.EvidenceConflict);
        categories["Fusion.MissingEvidence"] = string.Join(";", scientificAssessment.MissingEvidence);
        categories["Decision.TriggeredRules"] = string.Join(";", decisionResult.TriggeredRules);
        categories["Decision.RejectedRules"] = string.Join(";", decisionResult.RejectedRules);
        categories["Decision.Winner"] = decisionResult.Winner.ToString();
        categories["Decision.RuleExplanation"] = decisionResult.RuleExplanation;
        categories["Decision.ArbitrationExplanation"] = decisionResult.ArbitrationExplanation;
        categories["Fusion.Diagnostics"] = scientificAssessment.Diagnostics;

        // Sprint 15.25 (Lot 9 - QDE-012 instrumentation gap closed): the Lots 3/5/7/8 audits found that
        // this dataset never captured EntryTrigger/TradePlan output at all, forcing every offline
        // Direction/TradePlan analysis to be re-derived rather than measured directly. Purely passive -
        // reads already-computed values from objects the pipeline already produced this bar; never
        // recomputes, never influences, never mutates them (see
        // ScientificDatasetRealMarketCaptureTests.From_WithNewInstrumentationParams_DoesNotMutateThem).
        // A field genuinely absent at this stage (object null, or a business-null price) is recorded as
        // the literal string "NOT AVAILABLE" - never a fabricated value.
        const string notAvailable = "NOT AVAILABLE";
        categories["Entry.OpportunityStatus"] = entryCandidate is null ? notAvailable : entryCandidate.OpportunityStatus.ToString();
        categories["EntryTrigger.TriggerStatus"] = entryTriggerCandidate is null ? notAvailable : entryTriggerCandidate.Assessment.TriggerStatus.ToString();
        categories["EntryTrigger.Direction"] = entryTriggerCandidate is null ? notAvailable : entryTriggerCandidate.Assessment.Direction.ToString();
        categories["EntryTrigger.Reason"] = entryTriggerCandidate is null ? notAvailable : entryTriggerCandidate.Assessment.Reason.ToString();
        categories["TradePlan.Status"] = tradePlan is null ? notAvailable : tradePlan.Status.ToString();
        categories["TradePlan.EntryPrice"] = tradePlan?.EntryPrice?.ToString(CultureInfo.InvariantCulture) ?? notAvailable;
        categories["TradePlan.StopLoss"] = tradePlan?.StopLoss?.ToString(CultureInfo.InvariantCulture) ?? notAvailable;
        categories["TradePlan.TakeProfit"] = tradePlan?.TakeProfit?.ToString(CultureInfo.InvariantCulture) ?? notAvailable;

        // Sprint 15.25 (Lot 11 - Risk Engine integration): same passive-capture discipline as the Lot 9
        // block above - riskAssessment is the same already-computed object IQIAIndicator.cs's Risk stage
        // produced this bar (see RiskEngineRequestFactory.FromTradePlan), never recomputed here. Null
        // when RiskEngineRequestFactory could not build a request (no assessable TradePlan candidate) -
        // recorded as "NOT AVAILABLE", never fabricated.
        categories["Risk.Status"] = riskAssessment is null ? notAvailable : riskAssessment.Status.ToString();
        categories["Risk.RejectionReasons"] = riskAssessment is null
            ? notAvailable
            : string.Join(";", riskAssessment.RejectionReasons);
        categories["Risk.PositionSize"] = riskAssessment?.PositionSize?.ToString(CultureInfo.InvariantCulture) ?? notAvailable;
        categories["Risk.RiskAmount"] = riskAssessment?.RiskAmount?.ToString(CultureInfo.InvariantCulture) ?? notAvailable;
        categories["Risk.RiskBudget"] = riskAssessment?.RiskBudget?.ToString(CultureInfo.InvariantCulture) ?? notAvailable;
        categories["Risk.RiskRewardRatio"] = riskAssessment?.RiskRewardRatio?.ToString(CultureInfo.InvariantCulture) ?? notAvailable;

        // Sprint 15.25 (Lot 12.5 - ATAS raw risk telemetry): closes the observability gap the Lot 12.4
        // audit found - ATAS.* diagnostic values were already computed every bar by IQIAIndicator.cs's
        // Risk stage (ATASRuntimeDiagnostics.CaptureAccount/CaptureInstrument, Lot 12.3) but never
        // reached this dataset, because the "if (trace is not null)" block below only ever read
        // PipelineTraceEvent.Elapsed, never PipelineTraceEvent.Details (where the ATAS.* values live).
        // Same passive-capture discipline as the Lot 9/11 blocks above: every value here is read from an
        // already-computed object, never recomputed, never validated, never used to influence
        // RiskEngine/TradePlan/EntryTrigger in any way.
        //
        // Three explicitly separate levels, never merged (Lot 12.5 brief, Section 4/8):
        //   ATAS.Raw.*      - the literal ATAS SDK values (ATASRuntimeDiagnostics, Lot 12.3, unchanged).
        //   ATAS.Adapter.*  - AccountState/InstrumentRiskSpecification AFTER ATASAccountStateAdapter/
        //                     ATASInstrumentAdapter mapped them (Lot 12.2, unchanged) - computed
        //                     unconditionally every bar (Lot 12 dashboard rationale), independent of
        //                     whether a TradePlan candidate exists this bar.
        //   ATAS.RiskInput.*- the exact RiskEngineRequest RiskEngine.Evaluate received this bar (Lot 11,
        //                     unchanged) - absent (Present=False/"NOT AVAILABLE") whenever
        //                     RiskEngineRequestFactory returned null, same gate as Risk.Status above.
        //     RiskEngineRequestFactory never reconstructs/recomputes Account or Instrument (see its own
        //     doc comment) - ATAS.RiskInput.Instrument.*/Account.* will equal ATAS.Adapter.* whenever
        //     ATAS.RiskInput.Present is True; capturing both separately is what lets that be OBSERVED
        //     rather than assumed.
        // A value missing at any boundary is recorded as "NOT AVAILABLE" (the same sentinel already used
        // throughout this file) - never inferred, never fabricated, never replaced by a manual/fallback
        // value. ATAS.QuantityStepAvailable/ATAS.QuantityStep are permanently False/"NOT AVAILABLE": no
        // QuantityStep-equivalent property exists anywhere in the inspected ATAS assemblies
        // (ATAS.DataFeedsCore.Security - Lot 12.1, re-confirmed independently by Lot 12.4's reflection
        // audit) - a fact about the ATAS API surface, not a per-bar reading, hence a constant rather than
        // a lookup on atasInstrumentDiagnostic.
        string? equitySourceMode = atasEquityIsReplay switch
        {
            true => "Replay",
            false => "Realtime",
            null => null
        };
        decimal? currentModeEquity = atasEquityIsReplay switch
        {
            true => atasAccountDiagnostic?.ReplayEquity,
            false => atasAccountDiagnostic?.RealtimeEquity,
            null => null
        };

        // ATAS.Raw.* - Account (Lot 12.5 brief, Section 2 "ACCOUNT").
        categories["ATAS.Raw.Account.AccountID"] = atasAccountDiagnostic?.AccountID ?? notAvailable;
        categories["ATAS.Raw.Account.IsRealAccount"] = atasAccountDiagnostic?.IsRealAccount?.ToString(CultureInfo.InvariantCulture) ?? notAvailable;
        categories["ATAS.Raw.Account.Currency"] = atasAccountDiagnostic?.Currency ?? notAvailable;
        categories["ATAS.Raw.Account.Balance"] = atasAccountDiagnostic?.Balance?.ToString(CultureInfo.InvariantCulture) ?? notAvailable;
        categories["ATAS.Raw.Account.BalanceAvailable"] = atasAccountDiagnostic?.BalanceAvailable?.ToString(CultureInfo.InvariantCulture) ?? notAvailable;
        categories["ATAS.Raw.Account.BalancePower"] = atasAccountDiagnostic?.BalancePower?.ToString(CultureInfo.InvariantCulture) ?? notAvailable;
        categories["ATAS.Raw.Account.OpenPnL"] = atasAccountDiagnostic?.OpenPnL?.ToString(CultureInfo.InvariantCulture) ?? notAvailable;
        categories["ATAS.Raw.Account.ClosedPnL"] = atasAccountDiagnostic?.ClosedPnL?.ToString(CultureInfo.InvariantCulture) ?? notAvailable;
        categories["ATAS.Raw.Account.TotalPnL"] = atasAccountDiagnostic?.TotalPnL?.ToString(CultureInfo.InvariantCulture) ?? notAvailable;

        // ATAS.Raw.* - Equity (Lot 12.5 brief, Section 2 "EQUITY"/Section 6). Never deduced from
        // Balance, never computed - both values below are exactly ATASAccountDiagnostic.RealtimeEquity/
        // .ReplayEquity (Lot 12.3, unchanged), and the two literally-named fields the brief requires
        // (ATAS.EquityAvailable/ATAS.Equity) report strictly the value for the mode actually queried
        // this bar (atasEquityIsReplay, mirroring ATASAccountStateAdapter.TryGetCurrentEquity's own
        // Replay/Realtime selection - a second, independent read, never a second decision).
        categories["ATAS.Raw.Equity.SourceMode"] = equitySourceMode ?? notAvailable;
        categories["ATAS.Raw.Equity.RealtimeValue"] = atasAccountDiagnostic?.RealtimeEquity?.ToString(CultureInfo.InvariantCulture) ?? notAvailable;
        categories["ATAS.Raw.Equity.ReplayValue"] = atasAccountDiagnostic?.ReplayEquity?.ToString(CultureInfo.InvariantCulture) ?? notAvailable;
        categories["ATAS.Raw.Equity.SeriesCount"] = atasEquitySeriesCount?.ToString(CultureInfo.InvariantCulture) ?? notAvailable;
        categories["ATAS.Raw.Equity.LastTimestamp"] = atasEquityLastTimestamp?.ToString("O", CultureInfo.InvariantCulture) ?? notAvailable;
        categories["ATAS.EquityAvailable"] = (currentModeEquity is not null).ToString(CultureInfo.InvariantCulture);
        categories["ATAS.Equity"] = currentModeEquity?.ToString(CultureInfo.InvariantCulture) ?? notAvailable;

        // Sprint 15.25 (Lot 12.6 - ATAS binding correction, brief Phase F; priority order revised
        // Lot 12.11). ATAS.Raw.Equity.SourceMode above now reflects the CORRECTED signal
        // (atasEquityIsReplay - see ATASEquityReplayDetector.cs) actually used to pick Replay vs Realtime
        // this bar; the two fields below let that be compared directly against its two inputs.
        // HeuristicIsReplay is the original in-house bar-index heuristic (Core.MarketContextBuilder.cs,
        // unchanged) - still consulted, but only when PortfolioIsReplay is unavailable (Lot 12.11).
        // PortfolioIsReplay (Portfolio.IsReplay(), ATAS.DataFeedsCore.Extensions - confirmed by reflection
        // to check Portfolio.AccountID == "Replay" exactly) is, since Lot 12.11, the highest-priority
        // input to that same decision when available (a real, live, non-Replay account was confirmed
        // populating it correctly by the Lot 12.10 capture) - no longer purely observational.
        categories["ATAS.Adapter.Equity.HeuristicIsReplay"] = atasEquityHeuristicIsReplay?.ToString(CultureInfo.InvariantCulture) ?? notAvailable;
        categories["ATAS.Raw.Account.PortfolioIsReplay"] = atasPortfolioIsReplay?.ToString(CultureInfo.InvariantCulture) ?? notAvailable;

        // ATAS.Raw.* - Instrument (Lot 12.5 brief, Section 3).
        categories["ATAS.Raw.Instrument.Symbol"] = atasInstrumentDiagnostic?.Instrument ?? notAvailable;
        categories["ATAS.Raw.Instrument.TickSize"] = atasInstrumentDiagnostic?.TickSize?.ToString(CultureInfo.InvariantCulture) ?? notAvailable;
        categories["ATAS.Raw.Instrument.TickCost"] = atasInstrumentDiagnostic?.TickCost?.ToString(CultureInfo.InvariantCulture) ?? notAvailable;
        categories["ATAS.Raw.Instrument.LotSize"] = atasInstrumentDiagnostic?.LotSize?.ToString(CultureInfo.InvariantCulture) ?? notAvailable;
        categories["ATAS.Raw.Instrument.LotMinSize"] = atasInstrumentDiagnostic?.LotMinSize?.ToString(CultureInfo.InvariantCulture) ?? notAvailable;
        categories["ATAS.Raw.Instrument.LotMaxSize"] = atasInstrumentDiagnostic?.LotMaxSize?.ToString(CultureInfo.InvariantCulture) ?? notAvailable;
        categories["ATAS.Raw.Instrument.Digits"] = atasInstrumentDiagnostic?.Digits?.ToString(CultureInfo.InvariantCulture) ?? notAvailable;
        categories["ATAS.Raw.Instrument.BaseCurrency"] = atasInstrumentDiagnostic?.BaseCurrency ?? notAvailable;
        categories["ATAS.Raw.Instrument.QuoteCurrency"] = atasInstrumentDiagnostic?.QuoteCurrency ?? notAvailable;
        categories["ATAS.QuantityStepAvailable"] = false.ToString(CultureInfo.InvariantCulture);
        categories["ATAS.QuantityStep"] = notAvailable;

        // ATAS.Adapter.* (Lot 12.5 brief, Section 4).
        categories["ATAS.Adapter.Account.InitialCapital"] = riskAccountState?.InitialCapital.ToString(CultureInfo.InvariantCulture) ?? notAvailable;
        categories["ATAS.Adapter.Account.CurrentEquity"] = riskAccountState?.CurrentEquity.ToString(CultureInfo.InvariantCulture) ?? notAvailable;
        categories["ATAS.Adapter.Account.CurrentBalance"] = riskAccountState?.CurrentBalance?.ToString(CultureInfo.InvariantCulture) ?? notAvailable;
        categories["ATAS.Adapter.Instrument.Symbol"] = riskInstrumentSpec is not null && !string.IsNullOrWhiteSpace(riskInstrumentSpec.Symbol)
            ? riskInstrumentSpec.Symbol
            : notAvailable;
        categories["ATAS.Adapter.Instrument.TickSize"] = riskInstrumentSpec?.TickSize.ToString(CultureInfo.InvariantCulture) ?? notAvailable;
        categories["ATAS.Adapter.Instrument.TickValue"] = riskInstrumentSpec?.TickValue.ToString(CultureInfo.InvariantCulture) ?? notAvailable;
        categories["ATAS.Adapter.Instrument.PointValue"] = riskInstrumentSpec?.PointValue.ToString(CultureInfo.InvariantCulture) ?? notAvailable;
        categories["ATAS.Adapter.Instrument.QuantityStep"] = riskInstrumentSpec?.QuantityStep.ToString(CultureInfo.InvariantCulture) ?? notAvailable;
        categories["ATAS.Adapter.Instrument.MinQuantity"] = riskInstrumentSpec?.MinQuantity.ToString(CultureInfo.InvariantCulture) ?? notAvailable;
        categories["ATAS.Adapter.Instrument.MaxQuantity"] = riskInstrumentSpec?.MaxQuantity.ToString(CultureInfo.InvariantCulture) ?? notAvailable;
        // Sprint 15.25 (Lot 12.5): reuses InstrumentRiskSpecification.IsValid (Lot 10, read-only,
        // unmodified) - the exact same boolean RiskEngine.Evaluate itself already consults; never a new
        // validation rule.
        categories["ATAS.Adapter.Instrument.IsValid"] = riskInstrumentSpec?.IsValid.ToString(CultureInfo.InvariantCulture) ?? notAvailable;
        // Sprint 15.25 (Lot 12.6, brief Phase F) - names exactly which source produced the resolved
        // QuantityStep/MinQuantity/MaxQuantity above ("ATAS.LotSize"/"ATAS.LotMinSize"/"ATAS.LotMaxSize"
        // vs "Manual"). MinQuantity/MaxQuantity's own resolution is unchanged
        // (ATASInstrumentAdapter.Build, Lot 12.2, protected) - these two labels only observe, read-only,
        // which branch that unchanged logic took. No QuantityStepSource here: Lot 12.6 investigated
        // Security.LotSize as a candidate source but found it unsafe (its SDK default, 1, is
        // indistinguishable from real data - see ATASInstrumentQuantityStepResolver.cs) - QuantityStep
        // stays exclusively the manual parameter, unchanged, already fully represented by
        // ATAS.Adapter.Instrument.QuantityStep above.
        categories["ATAS.Adapter.Instrument.MinQuantitySource"] = minQuantitySource ?? notAvailable;
        categories["ATAS.Adapter.Instrument.MaxQuantitySource"] = maxQuantitySource ?? notAvailable;

        // ATAS.RiskInput.* (Lot 12.5 brief, Section 5/6) - the exact RiskEngineRequest object
        // RiskEngine.Evaluate received this bar (Lot 11, unchanged).
        categories["ATAS.RiskInput.Present"] = (riskEngineRequest is not null).ToString(CultureInfo.InvariantCulture);
        categories["ATAS.RiskInput.Direction"] = riskEngineRequest?.Direction.ToString() ?? notAvailable;
        categories["ATAS.RiskInput.EntryPrice"] = riskEngineRequest?.EntryPrice.ToString(CultureInfo.InvariantCulture) ?? notAvailable;
        categories["ATAS.RiskInput.StopLoss"] = riskEngineRequest?.StopLoss?.ToString(CultureInfo.InvariantCulture) ?? notAvailable;
        categories["ATAS.RiskInput.TakeProfit"] = riskEngineRequest?.TakeProfit?.ToString(CultureInfo.InvariantCulture) ?? notAvailable;
        categories["ATAS.RiskInput.Instrument.QuantityStep"] = riskEngineRequest?.Instrument.QuantityStep.ToString(CultureInfo.InvariantCulture) ?? notAvailable;
        categories["ATAS.RiskInput.Instrument.MinQuantity"] = riskEngineRequest?.Instrument.MinQuantity.ToString(CultureInfo.InvariantCulture) ?? notAvailable;
        categories["ATAS.RiskInput.Instrument.MaxQuantity"] = riskEngineRequest?.Instrument.MaxQuantity.ToString(CultureInfo.InvariantCulture) ?? notAvailable;
        categories["ATAS.RiskInput.Instrument.IsValid"] = riskEngineRequest?.Instrument.IsValid.ToString(CultureInfo.InvariantCulture) ?? notAvailable;
        categories["ATAS.RiskInput.Account.CurrentEquity"] = riskEngineRequest?.Account.CurrentEquity.ToString(CultureInfo.InvariantCulture) ?? notAvailable;
        categories["ATAS.RiskInput.Account.InitialCapital"] = riskEngineRequest?.Account.InitialCapital.ToString(CultureInfo.InvariantCulture) ?? notAvailable;

        // StopLoss observability (Lot 12.5 brief, Section 7) - explicit boolean sibling to the existing
        // TradePlan.StopLoss category above (Lot 9, unchanged), so its presence/absence is queryable
        // without string-comparing against "NOT AVAILABLE". Purely reads tradePlan.StopLoss - no new SL
        // strategy, no new value.
        categories["TradePlan.StopLossAvailable"] = (tradePlan?.StopLoss is not null).ToString(CultureInfo.InvariantCulture);

        // Sprint 15.25 (Lot 12.12, Problem A/C): the resolved Live/Replay context (AtasDataContext,
        // wraps the same atasEquityIsReplay decision already captured above as ATAS.Raw.Equity
        // .SourceMode - never a second decision), so an exported dataset can be filtered/grouped by
        // context without re-deriving it from the raw heuristic fields.
        categories["ATAS.Context.Resolved"] = atasContext?.ToString() ?? notAvailable;

        // Sprint 15.25 (Lot 12.12, Problem B): non-"Aucune" only when the Risk stage's ATAS-owned reads
        // threw on this bar (IQIAIndicator.cs's Risk stage catch clause) - lets an exported dataset
        // distinguish "no assessable TradePlan candidate this bar" (Risk.Status/ATAS.RiskInput.Present
        // both already False/"NOT AVAILABLE" above, riskStageError null) from "the Risk stage itself
        // never ran this bar" (same shape, riskStageError non-null) - the exact ambiguity Problem B
        // describes as a live capture with the Risk Engine panel entirely NOT AVAILABLE and no trace of
        // why.
        categories["ATAS.RiskStage.Exception"] = riskStageError ?? "Aucune";

        if (trace is not null)
        {
            foreach (PipelineTraceEvent traceEvent in trace.Events)
                AddMetric(metrics, categories, $"Trace.{traceEvent.Stage}", "ElapsedMs", traceEvent.Elapsed.TotalMilliseconds);
        }

        return new ScientificDatasetRecord(
            sessionId,
            marketContext.Timestamp,
            marketContext.Symbol ?? string.Empty,
            marketContext.TimeFrame ?? string.Empty,
            marketContext.CurrentPrice,
            marketContext.History.Count,
            currentBar,
            metrics,
            categories,
            open,
            high,
            low,
            volume);
    }

    private static DecisionCandidate? FirstCandidate(DecisionResult result) =>
        result.Candidates.Length == 0 ? null : result.Candidates[0];

    private static void AddMetric(
        IDictionary<string, double?> metrics,
        IDictionary<string, string> categories,
        string owner,
        string name,
        double? value)
    {
        string key = $"{owner}.{name}";
        metrics[key] = value;
        categories[key] = value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static bool TryConvertDouble(object value, out double result)
    {
        switch (value)
        {
            case double doubleValue when double.IsFinite(doubleValue):
                result = doubleValue;
                return true;
            case float floatValue when float.IsFinite(floatValue):
                result = floatValue;
                return true;
            case decimal decimalValue:
                result = (double)decimalValue;
                return double.IsFinite(result);
            case int intValue:
                result = intValue;
                return true;
            case long longValue:
                result = longValue;
                return true;
            default:
                result = 0.0;
                return false;
        }
    }
}
