using System.Globalization;
using System.Text;
using IQIAIndicator.Engine.Decision.Core;
using IQIAIndicator.Engine.Entry;
using IQIAIndicator.Engine.EntryTrigger;
using IQIAIndicator.Engine.Methodology.Core;
using IQIAIndicator.Engine.TradePlan;

namespace IQIAIndicator.Backtest;

/// <summary>
/// Sprint 15.25 (Lot 14.3, brief §19/§24). Deterministic, wall-clock-free canonicalization of a full
/// <see cref="BacktestSignalResult"/> bar, used to build
/// <see cref="BacktestSignalPipelineResult.DeterministicHash"/>.
///
/// Placed alongside (not inside) <see cref="BacktestFingerprint"/> - that Lot 14.1 file is left
/// untouched (same reasoning already applied by Lot 14.2's <see cref="HistoricalSeriesFingerprint"/>:
/// its formatters are <c>private</c>, scoped to scenario/bar/evidence, and Lot 14.1 is not one of this
/// lot's protected files but there is no reason to touch a working, tested file when reuse of its one
/// PUBLIC primitive - <see cref="BacktestFingerprint.Sha256Hex"/> - and its one internal-but-same-
/// assembly helper - <see cref="BacktestFingerprint.AppendBar"/>, for the Regime/Context portion this
/// lot does not redefine - already covers everything needed).
///
/// EXCLUDED FROM THE HASH, deliberately, mirroring <see cref="BacktestFingerprint"/>'s own documented
/// scoping: every <c>DateTime.UtcNow</c>-stamped "Timestamp"/"CreatedAt" field found by inspection
/// across Entry/EntryTrigger/Methodology/TradePlan (see the Lot 14.3 report §Determinism for the full
/// list) - these record WHEN a stage computed its result on THIS machine, not anything about the bar -
/// and every free-text Explanation/Diagnostics/Warnings/InvalidationReason string, whose CONTENT is
/// already fully implied by the numeric/enum fields hashed alongside it (exactly Lot 14.1's own
/// rationale for skipping EvidenceSet.Explanation). Only their COUNT is hashed, as a structural check.
/// </summary>
internal static class BacktestSignalFingerprint
{
    /// <summary>Appends one bar's canonical signal-pipeline fingerprint line to <paramref name="accumulator"/>.
    /// Call once per bar in <see cref="BacktestSignalPipelineResult.Bars"/>, in chronological order.</summary>
    public static void AppendSignalBar(StringBuilder accumulator, BacktestSignalResult result)
    {
        accumulator.Append("SignalBar[").Append(result.BarIndex).Append("] status=").Append(result.Status);
        accumulator.Append(" reason=").Append(result.Reason ?? "null").Append('|');

        if (result.Context is not null && result.Regime is not null)
        {
            BacktestFingerprint.AppendBar(
                accumulator, result.BarIndex, result.Status == BacktestSignalStatus.Warmup, result.Context, result.Regime);
        }

        AppendDecision(accumulator, result.Decision);
        AppendMethodology(accumulator, result.Methodology);
        AppendEntry(accumulator, result.Entry);
        AppendEntryTrigger(accumulator, result.EntryTrigger);
        AppendTradePlan(accumulator, result.TradePlan);
        AppendException(accumulator, result.Exception);

        accumulator.Append('\n');
    }

    private static void AppendDecision(StringBuilder sb, DecisionResult? r)
    {
        sb.Append(" Decision=");
        if (r is null) { sb.Append("NULL|"); return; }
        sb.Append("[Winner=").Append(r.Winner)
          .Append(",WinnerScore=").Append(D(r.WinnerScore))
          .Append(",Ambiguity=").Append(D(r.AmbiguityScore))
          .Append(",State=").Append(r.State)
          .Append(",Confidence=").Append(D(r.Confidence))
          .Append(",Candidates=").Append(r.Candidates.Length)
          .Append(",Triggered=").Append(r.TriggeredRules.Count)
          .Append(",Rejected=").Append(r.RejectedRules.Count)
          .Append(']').Append('|');
    }

    private static void AppendMethodology(StringBuilder sb, MethodologySelection? m)
    {
        sb.Append(" Methodology=");
        if (m is null) { sb.Append("NULL|"); return; }
        sb.Append("[Name=").Append(m.SelectedMethodology.Name)
          .Append(",Version=").Append(m.Version)
          .Append(']').Append('|');
    }

    private static void AppendEntry(StringBuilder sb, EntryCandidate? e)
    {
        sb.Append(" Entry=");
        if (e is null) { sb.Append("NULL|"); return; }
        sb.Append("[Status=").Append(e.OpportunityStatus)
          .Append(",Priority=").Append(D(e.OpportunityPriority))
          .Append(",Reasons=").Append(e.OpportunityReasons.Count)
          .Append(",Warnings=").Append(e.Warnings.Count)
          .Append(",Diagnostics=").Append(e.Diagnostics.Count)
          .Append(']').Append('|');
    }

    private static void AppendEntryTrigger(StringBuilder sb, EntryTriggerCandidate? t)
    {
        sb.Append(" EntryTrigger=");
        if (t is null) { sb.Append("NULL|"); return; }
        EntryTriggerAssessment a = t.Assessment;
        sb.Append("[TriggerStatus=").Append(a.TriggerStatus)
          .Append(",Direction=").Append(a.Direction)
          .Append(",Confidence=").Append(D(a.ScientificConfidence))
          .Append(",Priority=").Append(D(a.OpportunityPriority))
          .Append(",Reason=").Append(a.Reason)
          .Append(",Equilibrium=").Append(N(a.EstimatedEquilibrium))
          .Append(",Distance=").Append(N(a.DistanceToEquilibrium))
          .Append(",CurrentPrice=").Append(D(t.CurrentPrice))
          .Append(",Warnings=").Append(t.Warnings.Count)
          .Append(",Diagnostics=").Append(t.Diagnostics.Count)
          .Append(']').Append('|');
    }

    private static void AppendTradePlan(StringBuilder sb, TradePlan? p)
    {
        sb.Append(" TradePlan=");
        if (p is null) { sb.Append("NULL|"); return; }
        sb.Append("[Valid=").Append(p.IsValid)
          .Append(",Status=").Append(p.Status)
          .Append(",Direction=").Append(p.Direction)
          .Append(",Entry=").Append(N(p.EntryPrice))
          .Append(",SL=").Append(N(p.StopLoss))
          .Append(",TP=").Append(N(p.TakeProfit))
          .Append(",RiskPerUnit=").Append(N(p.RiskPerUnit))
          .Append(",RiskAmount=").Append(N(p.RiskAmount))
          .Append(",PositionSize=").Append(p.PositionSize?.ToString(CultureInfo.InvariantCulture) ?? "null")
          .Append(",RR=").Append(p.RiskRewardRatio is double rr ? D(rr) : "null")
          .Append(",Diagnostics=").Append(p.Diagnostics.Count)
          .Append(']').Append('|');
    }

    private static void AppendException(StringBuilder sb, BacktestStageException? x)
    {
        sb.Append(" Exception=");
        sb.Append(x is null ? "NULL" : $"[Stage={x.Stage},Type={x.ExceptionType}]");
    }

    private static string D(double value) => value.ToString(CultureInfo.InvariantCulture);

    private static string D(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    private static string N(double? value) => value is double v ? D(v) : "null";

    private static string N(decimal? value) => value is decimal v ? v.ToString(CultureInfo.InvariantCulture) : "null";
}
