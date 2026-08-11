using System.Drawing;
using System.Linq;
using IQIAIndicator.Core.Observability;
using IQIAIndicator.Engine.Decision.Arbitration;
using IQIAIndicator.Engine.Decision.Core;
using IQIAIndicator.Engine.Decision.States;
using IQIAIndicator.Engine.Fusion.Core;
using IQIAIndicator.Visualization.Rendering;
using IQIAIndicator.Visualization.State;
using OFT.Rendering.Context;

namespace IQIAIndicator.Visualization.Dashboards;

/// <summary>
/// Pipeline de décision : Scientific Assessment → Fusion → Decision Candidates →
/// Arbitration → Winner → Entry → Trigger, avec score/confidence/explication/durée par étape.
/// </summary>
internal sealed class DecisionDashboard
{
    public const int Width = 980;
    public const int Height = 700;

    private static readonly (FusionDimension Dimension, string Label)[] Dimensions =
    [
        (FusionDimension.Stationarity, "Stationarity"),
        (FusionDimension.Persistence, "Persistence"),
        (FusionDimension.MeanReversion, "Mean Reversion"),
        (FusionDimension.StructuralStability, "Structural Stability"),
        (FusionDimension.RandomWalk, "Random Walk")
    ];

    private static readonly (MarketState State, string Label)[] Candidates =
    [
        (MarketState.StableRange, "StableRange"),
        (MarketState.Trending, "Trending"),
        (MarketState.MeanReverting, "MeanReverting"),
        (MarketState.StructuralBreak, "StructuralBreak"),
        (MarketState.RandomWalk, "RandomWalk")
    ];

    public void Draw(RenderContext renderContext, DashboardContext context, int x, int y)
    {
        renderContext.FillRectangle(DashboardTheme.PanelBackground, new Rectangle(x, y, Width, Height));
        DashboardCanvas.Title(renderContext, "IQIA — DECISION PIPELINE", x + 10, y + 8);

        int leftY = y + 42;
        int leftX = x + 10;

        DrawStage(renderContext, "1) SCIENTIFIC ASSESSMENT",
            DashboardCanvas.FormatDouble(context.ScientificAssessment?.OverallConfidence),
            DashboardCanvas.FormatDouble(context.ScientificAssessment?.OverallConfidence),
            context.ScientificAssessment?.Diagnostics ?? "N/A",
            StageMs(context.PipelineTrace, PipelineTraceStage.ScientificFusion),
            leftX, ref leftY);

        DrawStage(renderContext, "2) FUSION (régime)",
            "N/A", "N/A",
            context.FusionSnapshot is null ? "N/A" : $"UpdateCount={context.FusionSnapshot.UpdateCount}  StateChanged={context.FusionSnapshot.StateChanged}  Stability={context.FusionSnapshot.ProfileAnalysis.ProfileStability:F3}",
            null, // non tracé : EvidenceFusionEngine.Fuse n'est pas encapsulé dans un PipelineTraceScope
            leftX, ref leftY);

        DrawFusionBars(renderContext, context.FusionResult, context.FusionSnapshot?.StableResult, leftX, ref leftY);

        leftY += 4;
        DashboardCanvas.SectionHeader(renderContext, "3) DECISION CANDIDATES", leftX, ref leftY);
        DrawDecisionCandidates(renderContext, context.DecisionResult, leftX, ref leftY);

        leftY += 4;
        DrawStage(renderContext, "4) ARBITRATION",
            DashboardCanvas.FormatDouble(context.DecisionResult?.WinnerScore),
            DashboardCanvas.FormatDouble(1.0 - (context.DecisionResult?.AmbiguityScore ?? 0.0)),
            context.DecisionResult?.ArbitrationExplanation ?? "N/A",
            StageMs(context.PipelineTrace, PipelineTraceStage.Decision),
            leftX, ref leftY);

        DrawStage(renderContext, "5) WINNER",
            context.DecisionResult is null ? "N/A" : DashboardCanvas.SplitPascalCase(context.DecisionResult.Winner.ToString()),
            DashboardCanvas.FormatDouble(context.DecisionResult?.Confidence),
            context.DecisionResult?.Explanation ?? "N/A",
            null,
            leftX, ref leftY);

        DrawStage(renderContext, "6) ENTRY",
            context.EntryCandidate?.OpportunityStatus.ToString() ?? "N/A",
            DashboardCanvas.FormatDouble(context.EntryCandidate?.OpportunityPriority),
            context.EntryCandidate?.Assessment.EntryReadiness.ToString() ?? "N/A",
            StageMs(context.PipelineTrace, PipelineTraceStage.Entry),
            leftX, ref leftY);

        DrawStage(renderContext, "7) TRIGGER",
            context.EntryTriggerCandidate?.Assessment.TriggerStatus.ToString() ?? "N/A",
            DashboardCanvas.FormatDouble(context.EntryTriggerCandidate?.Assessment.ScientificConfidence),
            context.EntryTriggerCandidate?.Assessment.Reason.ToString() ?? "N/A",
            StageMs(context.PipelineTrace, PipelineTraceStage.EntryTrigger),
            leftX, ref leftY);
    }

    private static void DrawStage(
        RenderContext renderContext,
        string title,
        string score,
        string confidence,
        string explanation,
        double? durationMs,
        int x,
        ref int y)
    {
        DashboardCanvas.SectionHeader(renderContext, title, x, ref y);
        DashboardCanvas.SmallField(renderContext, "Score", score, x, ref y, valueOffset: 100);
        DashboardCanvas.SmallField(renderContext, "Confidence", confidence, x, ref y, valueOffset: 100);
        DashboardCanvas.SmallField(renderContext, "Durée", DashboardCanvas.FormatMs(durationMs), x, ref y, valueOffset: 100);
        DashboardCanvas.SmallField(renderContext, "Explication", DashboardCanvas.Truncate(explanation, 110), x, ref y, valueOffset: 100);
        y += 6;
    }

    private static void DrawFusionBars(RenderContext renderContext, FusionResult? raw, FusionResult? stable, int x, ref int y)
    {
        DashboardCanvas.SectionHeader(renderContext, "Dimensions (raw vs. stable)", x, ref y);
        foreach ((FusionDimension dimension, string label) in Dimensions)
        {
            double rawValue = GetValue(raw, dimension);
            double stableValue = GetValue(stable, dimension);
            renderContext.DrawString(label, DashboardTheme.SmallFont, DashboardTheme.TextColor, x, y);
            renderContext.DrawString(
                $"Raw={rawValue:F3}  Stable={stableValue:F3}",
                DashboardTheme.SmallFont,
                DashboardTheme.StateColor(stableValue),
                x + 160,
                y);
            y += 13;
        }

        y += 4;
    }

    private static void DrawDecisionCandidates(RenderContext renderContext, DecisionResult? decisionResult, int x, ref int y)
    {
        foreach ((MarketState state, string label) in Candidates)
        {
            DecisionCandidate? candidate = decisionResult?.Candidates.FirstOrDefault(candidate => candidate.MarketState == state);
            renderContext.DrawString(label, DashboardTheme.SmallFont, DashboardTheme.TextColor, x, y);
            renderContext.DrawString(
                candidate is null
                    ? "Scientific=n/a  Quality=n/a  Final=n/a"
                    : $"Scientific={candidate.ScientificScore:F3}  Quality={candidate.QualityScore:F3}  Final={candidate.FinalScore:F3}",
                DashboardTheme.SmallFont,
                candidate is null ? DashboardTheme.SecondaryTextColor : DashboardTheme.StateColor(candidate.FinalScore),
                x + 130,
                y);
            y += 14;
        }
    }

    private static double GetValue(FusionResult? fusionResult, FusionDimension dimension) =>
        fusionResult is not null && fusionResult.Dimensions.TryGetValue(dimension, out FusionConfidence? confidence)
            ? System.Math.Clamp(confidence.Value, 0.0, 1.0)
            : 0.0;

    private static double? StageMs(PipelineTraceRun? trace, PipelineTraceStage stage)
    {
        if (trace is null)
            return null;

        var evt = trace.Events.FirstOrDefault(item => item.Stage == stage);
        return evt is null ? null : evt.Elapsed.TotalMilliseconds;
    }
}
