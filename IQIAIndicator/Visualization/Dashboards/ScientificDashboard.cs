using System.Drawing;
using IQIAIndicator.Engine.Regime.Core;
using IQIAIndicator.Visualization.Rendering;
using IQIAIndicator.Visualization.State;
using IQIAIndicator.Visualization.Widgets;
using OFT.Rendering.Context;

namespace IQIAIndicator.Visualization.Dashboards;

/// <summary>
/// Vue chercheur : une carte indépendante par modèle scientifique + les preuves de régime brutes
/// (ADF/KPSS/Hurst/HalfLife/VarianceRatio/CUSUM/Volatility/BaiPerron/DFA).
/// </summary>
internal sealed class ScientificDashboard
{
    public const int Width = 980;
    public const int Height = 560;
    private const int CardsPerRow = 3;
    private const int CardGap = 12;

    public void Draw(RenderContext renderContext, DashboardContext context, bool expandDiagnostics, int x, int y)
    {
        renderContext.FillRectangle(DashboardTheme.PanelBackground, new Rectangle(x, y, Width, Height));
        DashboardCanvas.Title(renderContext, "IQIA — SCIENTIFIC", x + 10, y + 8);

        var results = context.ScientificAssessment?.ScientificResults;
        if (results is { Count: > 0 })
        {
            for (int index = 0; index < results.Count; index++)
            {
                int row = index / CardsPerRow;
                int column = index % CardsPerRow;
                int cardX = x + 10 + column * (ModelCard.Width + CardGap);
                int cardY = y + 40 + row * (ModelCard.Height + CardGap);
                ModelCard.Draw(renderContext, results[index], context.PipelineTrace, expandDiagnostics, cardX, cardY);
            }
        }
        else
        {
            renderContext.DrawString("Aucun modèle scientifique exécuté pour ce régime.", DashboardTheme.BodyFont, DashboardTheme.SecondaryTextColor, x + 10, y + 44);
        }

        int rowsUsed = results is { Count: > 0 } ? (results.Count - 1) / CardsPerRow + 1 : 1;
        int evidenceY = y + 40 + rowsUsed * (ModelCard.Height + CardGap) + 10;
        DashboardCanvas.SectionHeader(renderContext, "PREUVES DE RÉGIME (EvidenceSet)", x + 10, ref evidenceY);
        DrawEvidence(renderContext, context.Evidence, x + 10, ref evidenceY);
    }

    private static void DrawEvidence(RenderContext renderContext, EvidenceSet? evidence, int x, ref int y)
    {
        DashboardCanvas.SmallField(renderContext, "ADF", FormatAdf(evidence), x, ref y, valueOffset: 90);
        DashboardCanvas.SmallField(renderContext, "KPSS", FormatKpss(evidence), x, ref y, valueOffset: 90);
        DashboardCanvas.SmallField(renderContext, "Hurst", FormatHurst(evidence), x, ref y, valueOffset: 90);
        DashboardCanvas.SmallField(renderContext, "Half-Life", FormatHalfLife(evidence), x, ref y, valueOffset: 90);
        DashboardCanvas.SmallField(renderContext, "Variance Ratio", FormatVarianceRatio(evidence), x, ref y, valueOffset: 90);
        DashboardCanvas.SmallField(renderContext, "CUSUM", FormatCusum(evidence), x, ref y, valueOffset: 90);
        DashboardCanvas.SmallField(renderContext, "Volatility", FormatVolatility(evidence), x, ref y, valueOffset: 90);
        DashboardCanvas.SmallField(renderContext, "Bai-Perron", FormatBaiPerron(evidence), x, ref y, valueOffset: 90);
        DashboardCanvas.SmallField(renderContext, "DFA", FormatDfa(evidence), x, ref y, valueOffset: 90);
    }

    private static string FormatAdf(EvidenceSet? evidence) => evidence?.Adf is { } adf
        ? $"Statistic={adf.Statistic:F3}  PValue={adf.PValue:F3}  Confidence={adf.Confidence:F3}  IsStationary={adf.IsStationary}"
        : "Unavailable";

    private static string FormatKpss(EvidenceSet? evidence) => evidence?.Kpss is { } kpss
        ? $"Statistic={kpss.Statistic:F3}  PValue={kpss.PValue:F3}  Confidence={kpss.Confidence:F3}  IsStationary={kpss.IsStationary}"
        : "Unavailable";

    private static string FormatHurst(EvidenceSet? evidence) => evidence?.Hurst is { } hurst
        ? $"VarianceRatio={hurst.VarianceRatio:F3}  HurstProxy={hurst.HurstProxy:F3}  Confidence={hurst.Confidence:F3}"
        : "Unavailable";

    private static string FormatHalfLife(EvidenceSet? evidence) => evidence?.HalfLife is { } halfLife
        ? $"HalfLife={halfLife.HalfLife:F3}  RSquared={halfLife.RSquared:F3}  Confidence={halfLife.Confidence:F3}  SampleSize={halfLife.SampleSize}"
        : "Unavailable";

    private static string FormatVarianceRatio(EvidenceSet? evidence) => evidence?.VarianceRatio is { } varianceRatio
        ? $"VarianceRatio={varianceRatio.VarianceRatio:F3}  ZScore={varianceRatio.ZStatistic:F3}  PValue={varianceRatio.PValue:F3}  Confidence={varianceRatio.Confidence:F3}"
        : "Unavailable";

    private static string FormatCusum(EvidenceSet? evidence) => evidence?.Cusum is { } cusum
        ? $"ChangeDetected={cusum.ChangeDetected}  Positive={cusum.PositiveCusum:F3}  Negative={cusum.NegativeCusum:F3}  Confidence={cusum.Confidence:F3}"
        : "Unavailable";

    private static string FormatVolatility(EvidenceSet? evidence) => evidence?.Volatility is { } volatility
        ? $"AcfAbsReturns={volatility.AcfAbsReturns:F3}  IsClustering={volatility.IsClustering}  Confidence={volatility.Confidence:F3}"
        : "Unavailable";

    private static string FormatBaiPerron(EvidenceSet? evidence) => evidence?.BaiPerron is { } baiPerron
        ? $"BreakCount={baiPerron.BreakCount}  Confidence={baiPerron.Confidence:F3}  BIC={baiPerron.BicScore:F3}  SampleSize={baiPerron.SampleSize}"
        : "Unavailable";

    private static string FormatDfa(EvidenceSet? evidence) => evidence?.Dfa is { } dfa
        ? $"Hurst={dfa.Hurst:F3}  RSquared={dfa.RSquared:F3}  Confidence={dfa.Confidence:F3}  WindowCount={dfa.WindowCount}  LogLogPoints={dfa.WindowSizes?.Count ?? 0}"
        : "Unavailable";
}
