using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using IQIAIndicator.Engine.Regime.Core;
using IQIAIndicator.Visualization.Rendering;
using IQIAIndicator.Visualization.State;
using IQIAIndicator.Visualization.Widgets;
using OFT.Rendering.Context;

namespace IQIAIndicator.Visualization.Dashboards;

/// <summary>
/// Vue chercheur : une carte indépendante par modèle scientifique + les preuves de régime brutes
/// (ADF/KPSS/Hurst/HalfLife/VarianceRatio/CUSUM/Volatility/BaiPerron/DFA).
///
/// Sprint 13.5 (H2) : chaque ModelCard peut être développée/repliée indépendamment via un clic sur
/// son en-tête (voir IQIAIndicator.ProcessMouseClick → DashboardManager.TryHandleMouseClick →
/// ScientificDashboard.TryHandleClick). L'état de présentation est gardé ici, clé = ModelName
/// (stable, jamais la position/l'ordre de dessin). Aucune donnée scientifique n'est recalculée ou
/// modifiée par cet état — uniquement ce qui est dessiné.
/// </summary>
internal sealed class ScientificDashboard
{
    public const int Width = 980;
    public const int Height = 560;
    private const int CardsPerRow = 3;
    private const int CardGap = 12;

    private readonly HashSet<string> _collapsedModelNames = new();
    private IReadOnlyList<ModelCardHitArea> _hitAreas = Array.Empty<ModelCardHitArea>();

    public void Draw(RenderContext renderContext, DashboardContext context, int x, int y)
    {
        renderContext.FillRectangle(DashboardTheme.PanelBackground, new Rectangle(x, y, Width, Height));
        DashboardCanvas.Title(renderContext, "IQIA — SCIENTIFIC", x + 10, y + 8);

        var results = context.ScientificAssessment?.ScientificResults;
        if (results is { Count: > 0 })
        {
            IReadOnlyList<string> modelNames = results.Select(result => result.ModelName).ToArray();
            IReadOnlyList<ModelCardLayoutSlot> slots = ComputeLayout(modelNames, IsExpanded, x + 10, y + 40, CardsPerRow, ModelCard.Width, CardGap);

            var hitAreas = new List<ModelCardHitArea>(slots.Count);
            for (int index = 0; index < results.Count; index++)
            {
                ModelCardLayoutSlot slot = slots[index];
                ModelCard.Draw(renderContext, results[index], context.PipelineTrace, slot.Expanded, slot.X, slot.Y);
                hitAreas.Add(new ModelCardHitArea(slot.ModelName, slot.X, slot.Y, ModelCard.Width, ModelCard.HeaderHeight));
            }

            _hitAreas = hitAreas;

            int evidenceY = slots.Max(slot => slot.Y + slot.Height) + CardGap + 10;
            DashboardCanvas.SectionHeader(renderContext, "PREUVES DE RÉGIME (EvidenceSet)", x + 10, ref evidenceY);
            DrawEvidence(renderContext, context.Evidence, x + 10, ref evidenceY);
        }
        else
        {
            _hitAreas = Array.Empty<ModelCardHitArea>();
            renderContext.DrawString("Aucun modèle scientifique exécuté pour ce régime.", DashboardTheme.BodyFont, DashboardTheme.SecondaryTextColor, x + 10, y + 44);

            // Même espacement qu'avant le Sprint 13.5 pour ce cas (aucune carte à reflow) :
            // équivalent à 1 ligne de cartes en hauteur pleine, comme le comportement historique.
            int evidenceY = y + 40 + (ModelCard.ExpandedHeight + CardGap) + 10;
            DashboardCanvas.SectionHeader(renderContext, "PREUVES DE RÉGIME (EvidenceSet)", x + 10, ref evidenceY);
            DrawEvidence(renderContext, context.Evidence, x + 10, ref evidenceY);
        }
    }

    /// <summary>Défaut : expanded (Étape 3) — une carte n'est repliée qu'après un clic explicite.</summary>
    internal bool IsExpanded(string modelName) => !_collapsedModelNames.Contains(modelName);

    internal void ToggleExpanded(string modelName)
    {
        if (!_collapsedModelNames.Remove(modelName))
            _collapsedModelNames.Add(modelName);
    }

    /// <summary>
    /// Résout un clic reçu par IQIAIndicator.ProcessMouseClick contre les zones de header
    /// enregistrées lors du DERNIER Draw (Étape 4 : même calcul que le dessin, jamais de rectangle
    /// dupliqué à la main). Retourne true si le clic a été consommé (header d'une carte) — dans ce
    /// cas l'appelant doit marquer l'événement Handled et demander un redraw. Un clic hors header
    /// (contenu de carte ou zone vide) ne fait rien (Étape 14).
    /// </summary>
    public bool TryHandleClick(int pointX, int pointY)
    {
        if (!TryResolveClick(_hitAreas, pointX, pointY, out string? modelName))
            return false;

        ToggleExpanded(modelName!);
        return true;
    }

    /// <summary>Résolution pure du clic contre une liste de zones — séparée de TryHandleClick pour
    /// rester testable sans RenderContext (aucune carte n'a encore été dessinée nécessaire).</summary>
    internal static bool TryResolveClick(IReadOnlyList<ModelCardHitArea> hitAreas, int pointX, int pointY, out string? modelName)
    {
        foreach (ModelCardHitArea area in hitAreas)
        {
            if (area.Contains(pointX, pointY))
            {
                modelName = area.ModelName;
                return true;
            }
        }

        modelName = null;
        return false;
    }

    /// <summary>
    /// Calcule position/hauteur de chaque carte pour une grille dont la hauteur de chaque LIGNE
    /// s'adapte à sa carte la plus haute (Étape 11) : si une ligne contient encore une carte
    /// dépliée, la ligne garde sa hauteur pleine (pas de grille cassée, pas de chevauchement) ; ce
    /// n'est que lorsque TOUTES les cartes d'une ligne sont repliées que la ligne rétrécit et que
    /// les lignes suivantes remontent. Fonction pure (aucune dépendance à RenderContext) : c'est le
    /// même calcul qui positionne le dessin ET les zones de hit-test (Étape 4/12), donc les deux
    /// restent toujours cohérents, y compris après un resize (Étape 12 — recalculé à chaque appel,
    /// jamais de coordonnées absolues mises en cache d'un appel précédent).
    /// </summary>
    internal static IReadOnlyList<ModelCardLayoutSlot> ComputeLayout(
        IReadOnlyList<string> modelNames,
        Func<string, bool> isExpanded,
        int originX,
        int originY,
        int cardsPerRow,
        int cardWidth,
        int cardGap)
    {
        var slots = new List<ModelCardLayoutSlot>(modelNames.Count);
        if (modelNames.Count == 0)
            return slots;

        int currentY = originY;
        int rowCount = (modelNames.Count - 1) / cardsPerRow + 1;

        for (int row = 0; row < rowCount; row++)
        {
            int rowStart = row * cardsPerRow;
            int rowEnd = Math.Min(rowStart + cardsPerRow, modelNames.Count);

            int rowHeight = 0;
            for (int i = rowStart; i < rowEnd; i++)
                rowHeight = Math.Max(rowHeight, ModelCard.HeightFor(isExpanded(modelNames[i])));

            for (int i = rowStart; i < rowEnd; i++)
            {
                int column = i - rowStart;
                bool expanded = isExpanded(modelNames[i]);
                int cardX = originX + column * (cardWidth + cardGap);
                slots.Add(new ModelCardLayoutSlot(modelNames[i], cardX, currentY, ModelCard.HeightFor(expanded), expanded));
            }

            currentY += rowHeight + cardGap;
        }

        return slots;
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

/// <summary>Position/hauteur calculée pour une ModelCard lors d'un Draw donné (Sprint 13.5).</summary>
internal readonly record struct ModelCardLayoutSlot(string ModelName, int X, int Y, int Height, bool Expanded);

/// <summary>Zone cliquable (header uniquement, Étape 5) enregistrée pour une ModelCard lors du
/// dernier Draw. Clé d'identité = ModelName (stable), jamais la position/l'ordre de dessin.</summary>
internal readonly record struct ModelCardHitArea(string ModelName, int X, int Y, int Width, int HeaderHeight)
{
    public bool Contains(int pointX, int pointY) =>
        pointX >= X && pointX < X + Width && pointY >= Y && pointY < Y + HeaderHeight;
}
