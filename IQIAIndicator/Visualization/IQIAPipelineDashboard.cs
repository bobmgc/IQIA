using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using IQIAIndicator.Core;
using IQIAIndicator.Engine.Decision.Core;
using ScientificMarketContext = IQIAIndicator.Engine.ScientificModels.Abstractions.MarketContext;
using IQIAIndicator.Engine.Entry;
using IQIAIndicator.Engine.Fusion.Core;
using IQIAIndicator.Engine.Fusion.State;
using IQIAIndicator.Engine.Methodology.Core;
using IQIAIndicator.Engine.Presentation;
using IQIAIndicator.Engine.Regime.Core;
using IQIAIndicator.Engine.ScientificFusion;
using IQIAIndicator.Engine.ScientificModels.Abstractions;
using IQIAIndicator.Engine.Visualization;
using OFT.Rendering.Context;
using OFT.Rendering.Tools;

namespace IQIAIndicator.Visualization;

/// <summary>
/// Dashboard de diagnostic autonome pour visualiser le pipeline scientifique en temps réel.
/// Il ne calcule rien et ne modifie aucune donnée : il affiche uniquement les objets déjà produits.
/// </summary>
internal sealed class IQIAPipelineDashboard
{
    private const int PanelX = 345;
    private const int PanelY = 12;
    private const int PanelWidth = 980;
    private const int PanelHeight = 980;
    private const int SectionSpacing = 18;

    private static readonly RenderFont HeaderFont = new("Arial", 12f);
    private static readonly RenderFont BodyFont = new("Arial", 9f);
    private static readonly RenderFont SmallFont = new("Arial", 8.5f);

    private static readonly Color PanelBackground = Color.FromArgb(235, 28, 31, 35);
    private static readonly Color BarBackground = Color.FromArgb(255, 65, 70, 76);
    private static readonly Color TextColor = Color.White;
    private static readonly Color SecondaryTextColor = Color.LightGray;
    private static readonly Color Green = Color.FromArgb(255, 57, 169, 85);
    private static readonly Color Orange = Color.FromArgb(255, 235, 151, 45);
    private static readonly Color Red = Color.FromArgb(255, 210, 68, 68);
    private static readonly Color Gray = Color.FromArgb(255, 140, 145, 150);

    public void Draw(
        RenderContext renderContext,
        ScientificMarketContext? marketContext,
        EvidenceSet? evidence,
        FusionResult? fusionResult,
        FusionSnapshot? fusionSnapshot,
        DecisionResult? decisionResult,
        MethodologySelection? methodologySelection,
        OpportunityPresentation? opportunityPresentation,
        ChartAnnotationCandidate? chartAnnotationCandidate,
        ScientificAssessment? scientificAssessment,
        EntryCandidate? entryCandidate,
        VisualizationCandidate? visualizationCandidate,
        bool rendererCalled,
        int annotationsRendered,
        DateTime? lastRenderTime,
        int barIndex,
        DateTime timestamp,
        int availableEvidenceCount)
    {
        if (renderContext is null)
        {
            return;
        }

        renderContext.FillRectangle(PanelBackground, new Rectangle(PanelX, PanelY, PanelWidth, PanelHeight));
        renderContext.DrawString("IQIA - Pipeline Debug", HeaderFont, TextColor, PanelX + 10, PanelY + 8);
        renderContext.DrawString(
            $"Bar {barIndex} • {timestamp:HH:mm:ss} • Evidence {availableEvidenceCount}",
            BodyFont,
            SecondaryTextColor,
            PanelX + 10,
            PanelY + 28);

        int y = PanelY + 52;
        int x = PanelX + 10;

        DrawSection(renderContext, "1) MARKET CONTEXT", x, ref y);
        DrawField(renderContext, "Timestamp", FormatDate(marketContext?.Timestamp), x, ref y, TextColor);
        DrawField(renderContext, "CurrentPrice", FormatDecimal(marketContext?.CurrentPrice), x, ref y, TextColor);
        DrawField(renderContext, "CurrentBar", FormatDecimal(marketContext?.CurrentBar), x, ref y, TextColor);
        DrawField(renderContext, "History Count", FormatInt(marketContext?.History?.Count ?? 0), x, ref y, TextColor);
        DrawField(renderContext, "History First", FormatDecimal(marketContext?.History?.FirstOrDefault()), x, ref y, TextColor);
        DrawField(renderContext, "History Last", FormatDecimal(marketContext?.History?.LastOrDefault()), x, ref y, TextColor);
        DrawField(renderContext, "Symbol", FormatOptional(marketContext?.Symbol), x, ref y, TextColor);
        DrawField(renderContext, "TimeFrame", FormatOptional(marketContext?.TimeFrame), x, ref y, TextColor);
        DrawField(renderContext, "Exchange", FormatOptional(marketContext?.Exchange), x, ref y, TextColor);
        DrawField(renderContext, "Session", FormatOptional(marketContext?.Session), x, ref y, TextColor);
        DrawField(renderContext, "MarketState", decisionResult?.Winner.ToString() ?? "N/A", x, ref y, TextColor);
        DrawField(renderContext, "Validation", FormatValidationState(marketContext), x, ref y, TextColor);

        y += SectionSpacing;
        DrawSection(renderContext, "2) DECISION", x, ref y);
        DrawField(renderContext, "Behavior", decisionResult?.Winner.ToString() ?? "N/A", x, ref y, TextColor);
        DrawField(renderContext, "Confidence", FormatDouble(decisionResult?.Confidence), x, ref y, TextColor);
        DrawField(renderContext, "Winner", decisionResult?.Winner.ToString() ?? "N/A", x, ref y, TextColor);
        DrawField(renderContext, "DecisionQuality", FormatDouble(decisionResult?.AmbiguityScore), x, ref y, TextColor);

        y += SectionSpacing;
        DrawSection(renderContext, "3) METHODOLOGY", x, ref y);
        DrawField(renderContext, "Selected Methodology", methodologySelection?.SelectedMethodology.Name ?? "N/A", x, ref y, TextColor);
        DrawField(renderContext, "Reason", Truncate(methodologySelection?.Explanation ?? "N/A", 140), x, ref y, TextColor);

        y += SectionSpacing;
        DrawSection(renderContext, "4) SIGNAL ENGINE", x, ref y);
        DrawStatus(renderContext, "Executed", opportunityPresentation is not null, x, ref y);
        DrawField(renderContext, "Scientific Models", FormatInt(scientificAssessment?.ScientificResults?.Count ?? 0), x, ref y, TextColor);
        DrawField(renderContext, "Successful Models", FormatInt(scientificAssessment?.SuccessfulModels?.Count ?? 0), x, ref y, TextColor);
        DrawField(renderContext, "Failed Models", FormatInt(scientificAssessment?.FailedModels?.Count ?? 0), x, ref y, TextColor);

        y += SectionSpacing;
        DrawSection(renderContext, "5) SCIENTIFIC MODELS", x, ref y);
        if (scientificAssessment?.ScientificResults is { Count: > 0 })
        {
            foreach (ScientificModelResult result in scientificAssessment.ScientificResults)
            {
                DrawStatus(renderContext, result.ModelName, result.Success, x, ref y);
                DrawField(renderContext, "Executed", result.Success ? "✓ Executed" : "✗ Not Executed", x, ref y, result.Success ? Green : Gray);
                DrawField(renderContext, "Success", result.Success ? "true" : "false", x, ref y, result.Success ? Green : Red);
                DrawField(renderContext, "Score", FormatDouble(result.Score), x, ref y, TextColor);
                DrawField(renderContext, "Metrics", FormatMetrics(result.Metrics), x, ref y, TextColor);
                DrawField(renderContext, "Diagnostics", Truncate(result.Explanation, 140), x, ref y, TextColor);
                y += 8;
            }
        }
        else
        {
            DrawField(renderContext, "Models", "No scientific models executed", x, ref y, SecondaryTextColor);
        }

        y += SectionSpacing;
        DrawSection(renderContext, "6) SCIENTIFIC FUSION", x, ref y);
        DrawField(renderContext, "Evidence Count", FormatInt(scientificAssessment?.ScientificResults?.Count ?? 0), x, ref y, TextColor);
        DrawField(renderContext, "Agreement", FormatList(scientificAssessment?.EvidenceAgreement), x, ref y, TextColor);
        DrawField(renderContext, "Conflict", FormatList(scientificAssessment?.EvidenceConflict), x, ref y, TextColor);
        DrawField(renderContext, "Overall Confidence", FormatDouble(scientificAssessment?.OverallConfidence), x, ref y, TextColor);
        DrawField(renderContext, "Diagnostics", Truncate(scientificAssessment?.Diagnostics ?? "N/A", 180), x, ref y, TextColor);

        y += SectionSpacing;
        DrawSection(renderContext, "7) ENTRY", x, ref y);
        DrawField(renderContext, "Status", entryCandidate?.Assessment.OpportunityStatus.ToString() ?? "N/A", x, ref y, TextColor);
        DrawField(renderContext, "Priority", FormatDouble(entryCandidate?.OpportunityPriority), x, ref y, TextColor);
        DrawField(renderContext, "Blocking Issues", FormatList(entryCandidate?.Assessment.BlockingIssues), x, ref y, TextColor);
        DrawField(renderContext, "Supporting Evidence", FormatList(entryCandidate?.Assessment.SupportingEvidence), x, ref y, TextColor);

        y += SectionSpacing;
        DrawSection(renderContext, "8) VISUALIZATION", x, ref y);
        DrawField(renderContext, "VisualizationCandidate", visualizationCandidate?.Assessment.DisplayStatus.ToString() ?? "N/A", x, ref y, TextColor);
        DrawField(renderContext, "Created", FormatDate(visualizationCandidate?.CreatedAt), x, ref y, TextColor);

        y += SectionSpacing;
        DrawSection(renderContext, "9) CHART ANNOTATIONS", x, ref y);
        DrawField(renderContext, "Annotation Count", FormatInt(chartAnnotationCandidate?.Annotations?.Count ?? 0), x, ref y, TextColor);
        DrawField(renderContext, "Arrow Up", FormatInt(CountAnnotations(chartAnnotationCandidate, AnnotationType.Arrow, AnnotationVisibility.Pinned)), x, ref y, TextColor);
        DrawField(renderContext, "Arrow Down", FormatInt(CountAnnotations(chartAnnotationCandidate, AnnotationType.Arrow, AnnotationVisibility.Hidden)), x, ref y, TextColor);
        DrawField(renderContext, "Labels", FormatInt(CountAnnotations(chartAnnotationCandidate, AnnotationType.Label, AnnotationVisibility.Pinned)), x, ref y, TextColor);
        DrawField(renderContext, "Warnings", FormatInt(chartAnnotationCandidate?.Warnings?.Count ?? 0), x, ref y, TextColor);

        y += SectionSpacing;
        DrawSection(renderContext, "10) PRESENTATION", x, ref y);
        DrawField(renderContext, "Opportunity Status", opportunityPresentation?.OpportunityStatus ?? "N/A", x, ref y, TextColor);
        DrawField(renderContext, "Priority", opportunityPresentation?.OpportunityPriority.ToString() ?? "N/A", x, ref y, TextColor);
        DrawField(renderContext, "Scientific Summary", Truncate(opportunityPresentation?.ScientificSummary ?? "N/A", 180), x, ref y, TextColor);

        y += SectionSpacing;
        DrawSection(renderContext, "11) ATAS RENDERER", x, ref y);
        DrawStatus(renderContext, "Renderer Called", rendererCalled, x, ref y);
        DrawField(renderContext, "Annotations Rendered", FormatInt(annotationsRendered), x, ref y, TextColor);
        DrawField(renderContext, "Last Render Time", FormatDate(lastRenderTime), x, ref y, TextColor);
    }

    private static void DrawSection(RenderContext renderContext, string title, int x, ref int y)
    {
        renderContext.DrawString(title, BodyFont, SecondaryTextColor, x, y);
        y += 16;
    }

    private static void DrawField(RenderContext renderContext, string label, string value, int x, ref int y, Color color)
    {
        renderContext.DrawString(label, SmallFont, SecondaryTextColor, x, y);
        renderContext.DrawString(value, SmallFont, color, x + 180, y);
        y += 13;
    }

    private static void DrawStatus(RenderContext renderContext, string label, bool executed, int x, ref int y)
    {
        Color color = executed ? Green : Gray;
        string text = executed ? "✓ Executed" : "✗ Not Executed";
        renderContext.DrawString(label, SmallFont, SecondaryTextColor, x, y);
        renderContext.DrawString(text, SmallFont, color, x + 180, y);
        y += 13;
    }

    private static string FormatValidationState(ScientificMarketContext? marketContext)
    {
        if (marketContext is null)
        {
            return "N/A";
        }

        bool isComplete = marketContext.Timestamp != default
            && marketContext.History is { Count: > 0 }
            && marketContext.CurrentBar >= 0m;

        return isComplete ? "Valid" : "Incomplete";
    }

    private static string FormatDate(DateTime? value) => value is null ? "N/A" : value.Value.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

    private static string FormatDecimal(decimal? value) => value is null ? "N/A" : value.Value.ToString(CultureInfo.InvariantCulture);

    private static string FormatOptional(string? value) => string.IsNullOrWhiteSpace(value) ? "N/A" : value;

    private static string FormatDouble(double? value) => value is null ? "N/A" : value.Value.ToString("0.0000", CultureInfo.InvariantCulture);

    private static string FormatInt(int? value) => value is null ? "N/A" : value.Value.ToString(CultureInfo.InvariantCulture);

    private static string FormatInt(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string FormatList(IReadOnlyList<string>? values)
    {
        if (values is null || values.Count == 0)
        {
            return "N/A";
        }

        return string.Join(" | ", values.Take(3));
    }

    private static string FormatMetrics(IReadOnlyDictionary<string, object>? values)
    {
        if (values is null || values.Count == 0)
        {
            return "N/A";
        }

        return string.Join(" | ", values.Take(3).Select(entry => $"{entry.Key}={entry.Value}"));
    }

    private static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "N/A";
        }

        return value.Length <= maxLength ? value : value[..maxLength] + "...";
    }

    private static int CountAnnotations(ChartAnnotationCandidate? candidate, AnnotationType annotationType, AnnotationVisibility visibility)
    {
        if (candidate?.Annotations is null)
        {
            return 0;
        }

        return candidate.Annotations.Count(annotation =>
            annotation.AnnotationType == annotationType && annotation.Visibility == visibility);
    }
}
