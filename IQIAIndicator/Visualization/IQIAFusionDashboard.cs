using System.Drawing;
using System.Globalization;
using IQIAIndicator.Engine.Decision.Arbitration;
using IQIAIndicator.Engine.Decision.Core;
using IQIAIndicator.Engine.Decision.States;
using IQIAIndicator.Engine.Fusion.Core;
using IQIAIndicator.Engine.Fusion.State;
using IQIAIndicator.Engine.Presentation;
using IQIAIndicator.Engine.Regime.Core;
using OFT.Rendering.Context;
using OFT.Rendering.Tools;

namespace IQIAIndicator.Visualization;

/// <summary>
/// Read-only ATAS panel for IQIA normal and debug views.
/// </summary>
internal sealed class IQIAFusionDashboard
{
    private const int PanelX = 12;
    private const int PanelY = 12;
    private const int PanelWidth = 310;
    private const int PanelHeight = 630;
    private const int DebugPanelWidth = 1050;
    private const int DebugPanelHeight = 860;
    private const int BarWidth = 190;
    private const int RowHeight = 34;
    private const int ProfileRowHeight = 36;
    private const int DebugLineHeight = 16;
    private const int ValueColumnOffset = 165;

    private static readonly RenderFont HeaderFont = new("Arial", 12f);
    private static readonly RenderFont BodyFont = new("Arial", 10f);
    private static readonly RenderFont DebugFont = new("Arial", 9f);

    private static readonly Color PanelBackground = Color.FromArgb(235, 28, 31, 35);
    private static readonly Color BarBackground = Color.FromArgb(255, 65, 70, 76);
    private static readonly Color TextColor = Color.White;
    private static readonly Color SecondaryTextColor = Color.LightGray;
    private static readonly Color Green = Color.FromArgb(255, 57, 169, 85);
    private static readonly Color Orange = Color.FromArgb(255, 235, 151, 45);
    private static readonly Color Red = Color.FromArgb(255, 210, 68, 68);

    private static readonly DashboardRow[] Rows =
    [
        new(FusionDimension.Stationarity, "Stationarity"),
        new(FusionDimension.Persistence, "Persistence"),
        new(FusionDimension.MeanReversion, "Mean Reversion"),
        new(FusionDimension.StructuralStability, "Structural Stability"),
        new(FusionDimension.RandomWalk, "Random Walk")
    ];

    private static readonly CandidateRow[] CandidateRows =
    [
        new(MarketState.StableRange, "StableRange"),
        new(MarketState.Trending, "Trending"),
        new(MarketState.MeanReverting, "MeanReverting"),
        new(MarketState.StructuralBreak, "StructuralBreak"),
        new(MarketState.RandomWalk, "RandomWalk")
    ];

    public void Draw(
        RenderContext renderContext,
        EvidenceSet evidence,
        FusionResult fusionResult,
        FusionSnapshot fusionSnapshot,
        DecisionResult decisionResult,
        int barIndex,
        DateTime timestamp,
        int availableEvidenceCount,
        OpportunityPresentation? opportunityPresentation,
        bool debugMode)
    {
        if (debugMode)
        {
            DrawDebug(
                renderContext,
                evidence,
                fusionResult,
                fusionSnapshot,
                decisionResult,
                barIndex,
                timestamp,
                availableEvidenceCount,
                opportunityPresentation);
            return;
        }

        DrawNormal(renderContext, fusionSnapshot, decisionResult, opportunityPresentation);
    }

    private static void DrawNormal(
        RenderContext renderContext,
        FusionSnapshot fusionSnapshot,
        DecisionResult decisionResult,
        OpportunityPresentation? opportunityPresentation)
    {
        renderContext.FillRectangle(PanelBackground, new Rectangle(PanelX, PanelY, PanelWidth, PanelHeight));
        int y = PanelY + 8;

        DrawNormalTitle(renderContext, "IQIA", PanelX + 10, y);
        y += 38;

        DrawNormalSection(renderContext, "MARKET", PanelX + 10, y);
        y += 29;
        DrawLabelValue(renderContext, "Behaviour", FormatMarketState(decisionResult.Winner), PanelX + 10, y);
        y += 28;
        DrawLabelValue(renderContext, "Winner Score", FormatPercent(decisionResult.WinnerScore), PanelX + 10, y);
        y += 28;
        DrawLabelValue(renderContext, "Decision Quality", FormatDecisionQuality(decisionResult.AmbiguityScore), PanelX + 10, y);
        y += 28;
        DrawLabelValue(renderContext, "Snapshot Status", fusionSnapshot.StateChanged ? "Updating" : "Stable", PanelX + 10, y);
        y += 28;
        DrawLabelValue(renderContext, "Snapshot ID", $"#{fusionSnapshot.UpdateCount}", PanelX + 10, y);
        y += 36;

        DrawNormalSection(renderContext, "MARKET PROFILE", PanelX + 10, y);
        y += 29;

        for (int index = 0; index < Rows.Length; index++)
        {
            DashboardRow row = Rows[index];
            int rowY = y + index * ProfileRowHeight;
            double value = GetValue(fusionSnapshot.StableResult, row.Dimension);
            Color stateColor = GetStateColor(value);
            int filledWidth = (int)Math.Round(BarWidth * value, MidpointRounding.AwayFromZero);

            renderContext.DrawString(row.Label, BodyFont, TextColor, PanelX + 10, rowY);
            int barY = rowY + 16;
            renderContext.DrawString(FormatPercent(value), BodyFont, stateColor, PanelX + 240, barY - 2);

            var barBounds = new Rectangle(PanelX + 10, barY, BarWidth, 10);
            renderContext.FillRectangle(BarBackground, barBounds);
            renderContext.FillRectangle(stateColor, new Rectangle(barBounds.X, barBounds.Y, filledWidth, barBounds.Height));
        }

        y += Rows.Length * ProfileRowHeight + 4;
        DrawLabelValue(renderContext, "Recommended Methodology", FormatMethodology(decisionResult.Winner), PanelX + 10, y);
        y += 28;
        DrawLabelValue(renderContext, "Signal", opportunityPresentation?.SignalLabel ?? "Not Available", PanelX + 10, y);
        y += 28;
        DrawLabelValue(renderContext, "Risque", opportunityPresentation?.RiskLabel ?? "Not Available", PanelX + 10, y);
    }

    private static void DrawDebug(
        RenderContext renderContext,
        EvidenceSet evidence,
        FusionResult rawFusionResult,
        FusionSnapshot fusionSnapshot,
        DecisionResult decisionResult,
        int barIndex,
        DateTime timestamp,
        int availableEvidenceCount,
        OpportunityPresentation? opportunityPresentation)
    {
        renderContext.FillRectangle(PanelBackground, new Rectangle(PanelX, PanelY, DebugPanelWidth, DebugPanelHeight));
        renderContext.DrawString("IQIA - Mode Debug", HeaderFont, TextColor, PanelX + 10, PanelY + 8);
        renderContext.DrawString(
            $"Bar Index: {barIndex}  Time: {timestamp:HH:mm:ss}  Available Evidence: {availableEvidenceCount}",
            BodyFont,
            SecondaryTextColor,
            PanelX + 10,
            PanelY + 29);

        int evidenceY = PanelY + 58;
        DrawSectionTitle(renderContext, "Evidence Models", PanelX + 10, evidenceY);
        DrawEvidenceModels(renderContext, evidence, PanelX + 10, evidenceY + 20);

        int rulesY = PanelY + 225;
        DrawSectionTitle(renderContext, "Fusion Rules", PanelX + 10, rulesY);
        DrawFusionRules(renderContext, rawFusionResult, PanelX + 10, rulesY + 20);

        int resultY = PanelY + 375;
        DrawSectionTitle(renderContext, "Raw Fusion", PanelX + 10, resultY);
        DrawFusionResult(renderContext, rawFusionResult, PanelX + 10, resultY + 22);

        int stableY = PanelY + 545;
        DrawSectionTitle(renderContext, "Stable Fusion", PanelX + 10, stableY);
        DrawFusionResult(renderContext, fusionSnapshot.StableResult, PanelX + 10, stableY + 22);

        int differenceY = PanelY + 715;
        DrawSectionTitle(renderContext, "Difference", PanelX + 10, differenceY);
        DrawFusionDifference(renderContext, rawFusionResult, fusionSnapshot.StableResult, PanelX + 10, differenceY + 22);

        int candidatesY = PanelY + 58;
        DrawSectionTitle(renderContext, "Decision Candidates", PanelX + 560, candidatesY);
        DrawDecisionCandidates(renderContext, decisionResult, PanelX + 560, candidatesY + 22);

        int arbitrationY = PanelY + 200;
        DrawSectionTitle(renderContext, "Decision Arbitration", PanelX + 560, arbitrationY);
        DrawDecisionArbitration(renderContext, decisionResult, PanelX + 560, arbitrationY + 22);

        int decisionY = PanelY + 315;
        DrawSectionTitle(renderContext, "Decision Result", PanelX + 560, decisionY);
        DrawDecisionResult(renderContext, decisionResult, PanelX + 560, decisionY + 22);

        int presentationY = PanelY + 475;
        DrawSectionTitle(renderContext, "Signal Presentation", PanelX + 560, presentationY);
        DrawField(renderContext, "Signal", opportunityPresentation?.SignalLabel ?? "N/A", PanelX + 560, presentationY + 22, TextColor);
        DrawField(renderContext, "Risque", opportunityPresentation?.RiskLabel ?? "N/A", PanelX + 560, presentationY + 36, TextColor);
    }

    private static void DrawEvidenceModels(RenderContext renderContext, EvidenceSet evidence, int x, int y)
    {
        DrawDebugLine(renderContext, x, y, "ADF", FormatAdf(evidence));
        DrawDebugLine(renderContext, x, y + DebugLineHeight, "KPSS", FormatKpss(evidence));
        DrawDebugLine(renderContext, x, y + 2 * DebugLineHeight, "DFA", FormatDfa(evidence));
        DrawDebugLine(renderContext, x, y + 3 * DebugLineHeight, "Half-Life", FormatHalfLife(evidence));
        DrawDebugLine(renderContext, x, y + 4 * DebugLineHeight, "Variance Ratio", FormatVarianceRatio(evidence));
        DrawDebugLine(renderContext, x, y + 5 * DebugLineHeight, "CUSUM", FormatCusum(evidence));
        DrawDebugLine(renderContext, x, y + 6 * DebugLineHeight, "Bai-Perron", FormatBaiPerron(evidence));
    }

    private static void DrawFusionRules(RenderContext renderContext, FusionResult fusionResult, int x, int y)
    {
        for (int index = 0; index < Rows.Length; index++)
        {
            DashboardRow row = Rows[index];
            FusionConfidence confidence = GetConfidence(fusionResult, row.Dimension);
            int rowY = y + index * 25;

            renderContext.DrawString($"{row.Label} Rule", DebugFont, TextColor, x, rowY);
            renderContext.DrawString(
                $"Value={confidence.Value:F3}  Confidence={confidence.Confidence:F3}  Explanation={Truncate(confidence.Explanation, 92)}",
                DebugFont,
                GetStateColor(confidence.Value),
                x + 190,
                rowY);
        }
    }

    private static void DrawFusionResult(RenderContext renderContext, FusionResult fusionResult, int x, int y)
    {
        const int debugBarWidth = 250;
        const int debugRowHeight = 29;

        for (int index = 0; index < Rows.Length; index++)
        {
            DashboardRow row = Rows[index];
            int rowY = y + index * debugRowHeight;
            double value = GetValue(fusionResult, row.Dimension);
            Color stateColor = GetStateColor(value);
            int filledWidth = (int)Math.Round(debugBarWidth * value, MidpointRounding.AwayFromZero);

            renderContext.DrawString(row.Label, BodyFont, TextColor, x, rowY);
            renderContext.DrawString(value.ToString("F2", CultureInfo.InvariantCulture), BodyFont, stateColor, x + 180, rowY);
            var barBounds = new Rectangle(x + 235, rowY + 2, debugBarWidth, 11);
            renderContext.FillRectangle(BarBackground, barBounds);
            renderContext.FillRectangle(stateColor, new Rectangle(barBounds.X, barBounds.Y, filledWidth, barBounds.Height));
        }
    }

    private static void DrawFusionDifference(
        RenderContext renderContext,
        FusionResult rawFusionResult,
        FusionResult stableFusionResult,
        int x,
        int y)
    {
        const int debugRowHeight = 22;

        for (int index = 0; index < Rows.Length; index++)
        {
            DashboardRow row = Rows[index];
            int rowY = y + index * debugRowHeight;
            double rawValue = GetValue(rawFusionResult, row.Dimension);
            double stableValue = GetValue(stableFusionResult, row.Dimension);
            double difference = rawValue - stableValue;

            renderContext.DrawString(row.Label, DebugFont, TextColor, x, rowY);
            renderContext.DrawString(
                $"Raw={rawValue:F3}  Stable={stableValue:F3}  Difference={difference:F3}",
                DebugFont,
                GetStateColor(Math.Abs(difference)),
                x + 170,
                rowY);
        }
    }

    private static void DrawDecisionCandidates(RenderContext renderContext, DecisionResult decisionResult, int x, int y)
    {
        for (int index = 0; index < CandidateRows.Length; index++)
        {
            CandidateRow row = CandidateRows[index];
            DecisionCandidate? candidate = FindCandidate(decisionResult, row.State);
            int rowY = y + index * 20;

            renderContext.DrawString(row.Label, DebugFont, TextColor, x, rowY);
            renderContext.DrawString(
                candidate is null
                    ? "Scientific=n/a  Quality=n/a  Final=n/a"
                    : $"Scientific={candidate.ScientificScore:F3}  Quality={candidate.QualityScore:F3}  Final={candidate.FinalScore:F3}",
                DebugFont,
                candidate is null ? SecondaryTextColor : GetStateColor(candidate.FinalScore),
                x + 170,
                rowY);
        }
    }

    private static void DrawDecisionArbitration(RenderContext renderContext, DecisionResult decisionResult, int x, int y)
    {
        DecisionCandidate? winner = decisionResult.Candidates.Length > 0 ? decisionResult.Candidates[0] : null;
        DecisionCandidate? runnerUp = decisionResult.Candidates.Length > 1 ? decisionResult.Candidates[1] : null;
        double runnerUpScore = runnerUp?.FinalScore ?? 0.0;
        double difference = winner is null ? 0.0 : winner.FinalScore - runnerUpScore;

        DrawDebugLine(renderContext, x, y, "Winner", winner?.MarketState.ToString() ?? "Unknown");
        DrawDebugLine(renderContext, x, y + DebugLineHeight, "Runner Up", runnerUp?.MarketState.ToString() ?? "None");
        DrawDebugLine(renderContext, x, y + 2 * DebugLineHeight, "Winner Score", FormatScore(decisionResult.WinnerScore));
        DrawDebugLine(renderContext, x, y + 3 * DebugLineHeight, "Runner Up Score", FormatScore(runnerUpScore));
        DrawDebugLine(renderContext, x, y + 4 * DebugLineHeight, "Difference", FormatScore(difference));
        DrawDebugLine(renderContext, x, y + 5 * DebugLineHeight, "Ambiguity", FormatScore(decisionResult.AmbiguityScore));
    }

    private static void DrawDecisionResult(RenderContext renderContext, DecisionResult decisionResult, int x, int y)
    {
        DrawDebugLine(renderContext, x, y, "Winner", decisionResult.Winner.ToString());
        DrawDebugLine(renderContext, x, y + DebugLineHeight, "Confidence", FormatScore(decisionResult.WinnerScore));
        DrawDebugLine(renderContext, x, y + 2 * DebugLineHeight, "Explanation", Truncate(decisionResult.Explanation, 130));
    }

    private static void DrawSectionTitle(RenderContext renderContext, string title, int x, int y) =>
        renderContext.DrawString($"================ {title} ================", BodyFont, TextColor, x, y);

    private static void DrawDebugLine(RenderContext renderContext, int x, int y, string model, string values)
    {
        renderContext.DrawString(model, DebugFont, TextColor, x, y);
        renderContext.DrawString(values, DebugFont, SecondaryTextColor, x + 120, y);
    }

    private static void DrawField(RenderContext renderContext, string label, string value, int x, int y, Color color)
    {
        renderContext.DrawString(label, DebugFont, SecondaryTextColor, x, y);
        renderContext.DrawString(value, DebugFont, color, x + 130, y);
    }

    private static void DrawNormalTitle(RenderContext renderContext, string title, int x, int y)
    {
        renderContext.DrawString("-----------------------------", BodyFont, SecondaryTextColor, x, y);
        renderContext.DrawString(title, HeaderFont, TextColor, x, y + 12);
        renderContext.DrawString("-----------------------------", BodyFont, SecondaryTextColor, x, y + 24);
    }

    private static void DrawNormalSection(RenderContext renderContext, string title, int x, int y)
    {
        renderContext.DrawString("-----------------------------", BodyFont, SecondaryTextColor, x, y);
        renderContext.DrawString(title, HeaderFont, TextColor, x, y + 12);
    }

    private static void DrawWaitingSection(RenderContext renderContext, string title, int x, int y)
    {
        renderContext.DrawString("=========================", BodyFont, SecondaryTextColor, x, y);
        renderContext.DrawString(title, BodyFont, TextColor, x, y + 14);
        renderContext.DrawString("Waiting...", BodyFont, SecondaryTextColor, x + 115, y + 14);
    }

    private static void DrawLabelValue(RenderContext renderContext, string label, string value, int x, int y)
    {
        renderContext.DrawString(label, BodyFont, SecondaryTextColor, x, y);
        renderContext.DrawString(value, BodyFont, TextColor, x + ValueColumnOffset, y);
    }

    private static string FormatAdf(EvidenceSet evidence) => evidence.Adf is { } adf
        ? $"Statistic={adf.Statistic:F3}  PValue={adf.PValue:F3}  Confidence={adf.Confidence:F3}  IsStationary={adf.IsStationary}"
        : "Unavailable";

    private static string FormatKpss(EvidenceSet evidence) => evidence.Kpss is { } kpss
        ? $"Statistic={kpss.Statistic:F3}  PValue={kpss.PValue:F3}  Confidence={kpss.Confidence:F3}  IsStationary={kpss.IsStationary}"
        : "Unavailable";

    private static string FormatDfa(EvidenceSet evidence) => evidence.Dfa is { } dfa
        ? $"Hurst={dfa.Hurst:F3}  RSquared={dfa.RSquared:F3}  Confidence={dfa.Confidence:F3}  WindowCount={dfa.WindowCount}"
        : "Unavailable";

    private static string FormatHalfLife(EvidenceSet evidence) => evidence.HalfLife is { } halfLife
        ? $"HalfLife={halfLife.HalfLife:F3}  RSquared={halfLife.RSquared:F3}  Confidence={halfLife.Confidence:F3}  SampleSize={halfLife.SampleSize}"
        : "Unavailable";

    private static string FormatVarianceRatio(EvidenceSet evidence) => evidence.VarianceRatio is { } varianceRatio
        ? $"VarianceRatio={varianceRatio.VarianceRatio:F3}  ZScore={varianceRatio.ZStatistic:F3}  PValue={varianceRatio.PValue:F3}  Confidence={varianceRatio.Confidence:F3}"
        : "Unavailable";

    private static string FormatCusum(EvidenceSet evidence) => evidence.Cusum is { } cusum
        ? $"Positive={cusum.PositiveCusum:F3}  Negative={cusum.NegativeCusum:F3}  Threshold={cusum.Threshold:F3}  Confidence={cusum.Confidence:F3}"
        : "Unavailable";

    private static string FormatBaiPerron(EvidenceSet evidence) => evidence.BaiPerron is { } baiPerron
        ? $"BreakCount={baiPerron.BreakCount}  Confidence={baiPerron.Confidence:F3}  BIC={baiPerron.BicScore:F3}  SampleSize={baiPerron.SampleSize}"
        : "Unavailable";

    private static double GetValue(FusionResult fusionResult, FusionDimension dimension) =>
        Math.Clamp(GetConfidence(fusionResult, dimension).Value, 0.0, 1.0);

    private static FusionConfidence GetConfidence(FusionResult fusionResult, FusionDimension dimension) =>
        fusionResult.Dimensions.TryGetValue(dimension, out FusionConfidence? confidence)
            ? confidence
            : new FusionConfidence
            {
                Value = 0.0,
                Confidence = 0.0,
                Explanation = "Unavailable"
            };

    private static Color GetStateColor(double value) =>
        value >= 0.75 ? Green : value >= 0.40 ? Orange : Red;

    private static string Truncate(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..(maximumLength - 3)] + "...";

    private static string FormatDecisionQuality(double ambiguity)
    {
        string label = ambiguity switch
        {
            <= 0.25 => "Excellent",
            <= 0.50 => "Good",
            <= 0.75 => "Moderate",
            _ => "Weak"
        };

        return $"{label} ({FormatPercent(ambiguity)})";
    }

    private static string FormatMethodology(MarketState winner) => winner switch
    {
        MarketState.StableRange => "Range Methodology",
        MarketState.MeanReverting => "Mean Reversion Methodology",
        MarketState.Trending => "Trend Following Methodology",
        MarketState.StructuralBreak => "Structural Break Methodology",
        MarketState.RandomWalk => "Random Walk Methodology",
        _ => "Not Available"
    };

    private static string FormatPercent(double value) =>
        Math.Clamp(value, 0.0, 1.0).ToString("P0", CultureInfo.InvariantCulture);

    private static string FormatScore(double value) => value.ToString("F3", CultureInfo.InvariantCulture);

    private static string FormatMarketState(MarketState state) =>
        state == MarketState.Unknown ? "Unknown" : SplitPascalCase(state.ToString());

    private static string SplitPascalCase(string value)
    {
        var chars = new List<char>(value.Length + 4);
        for (int index = 0; index < value.Length; index++)
        {
            if (index > 0 && char.IsUpper(value[index]) && !char.IsWhiteSpace(value[index - 1]))
                chars.Add(' ');

            chars.Add(value[index]);
        }

        return new string(chars.ToArray());
    }

    private static DecisionCandidate? FindCandidate(DecisionResult decisionResult, MarketState state)
    {
        foreach (DecisionCandidate candidate in decisionResult.Candidates)
        {
            if (candidate.MarketState == state)
                return candidate;
        }

        return null;
    }

    private readonly record struct DashboardRow(FusionDimension Dimension, string Label);

    private readonly record struct CandidateRow(MarketState State, string Label);
}
