using System.Collections.Generic;
using System.Linq;
using IQIAIndicator.Visualization.Dashboards;
using IQIAIndicator.Visualization.Widgets;
using Xunit;

namespace IQIAIndicator.Tests.Dashboards;

/// <summary>
/// Vérifie la sémantique du repli/dépli par carte (Sprint 13.5, H2) : état par carte, résolution
/// de clic, et reflow du layout — sans dépendre de RenderContext (non instanciable hors ATAS).
/// ScientificDashboard.Draw (qui exige un RenderContext réel) reste vérifié en runtime dans ATAS,
/// pas ici — ces tests couvrent la même logique de résolution, extraite en méthodes pures
/// (ComputeLayout, TryResolveClick) partagées avec Draw/TryHandleClick.
/// </summary>
public sealed class ScientificDashboardCardInteractionTests
{
    private const string Kalman = "KalmanFilterModel";
    private const string OrnsteinUhlenbeck = "OrnsteinUhlenbeckModel";

    [Fact]
    public void IsExpanded_InitialState_IsTrue()
    {
        var dashboard = new ScientificDashboard();

        Assert.True(dashboard.IsExpanded(Kalman));
    }

    [Fact]
    public void ToggleExpanded_Once_Collapses()
    {
        var dashboard = new ScientificDashboard();

        dashboard.ToggleExpanded(Kalman);

        Assert.False(dashboard.IsExpanded(Kalman));
    }

    [Fact]
    public void ToggleExpanded_Twice_ExpandsAgain()
    {
        var dashboard = new ScientificDashboard();

        dashboard.ToggleExpanded(Kalman);
        dashboard.ToggleExpanded(Kalman);

        Assert.True(dashboard.IsExpanded(Kalman));
    }

    [Fact]
    public void ToggleExpanded_TwoCards_AreIndependent()
    {
        var dashboard = new ScientificDashboard();

        dashboard.ToggleExpanded(Kalman);

        Assert.False(dashboard.IsExpanded(Kalman));
        Assert.True(dashboard.IsExpanded(OrnsteinUhlenbeck));
    }

    [Fact]
    public void TryResolveClick_PointInsideHeader_ResolvesModel()
    {
        IReadOnlyList<ModelCardHitArea> hitAreas = BuildHitAreas(new[] { "A", "B", "C" }, _ => true, originX: 20, originY: 50);
        ModelCardHitArea firstCard = hitAreas[0];

        bool resolved = ScientificDashboard.TryResolveClick(hitAreas, firstCard.X + 5, firstCard.Y + 5, out string? modelName);

        Assert.True(resolved);
        Assert.Equal("A", modelName);
    }

    [Fact]
    public void TryResolveClick_PointInsideContentBelowHeader_IsIgnored()
    {
        IReadOnlyList<ModelCardHitArea> hitAreas = BuildHitAreas(new[] { "A" }, _ => true, originX: 0, originY: 0);
        ModelCardHitArea card = hitAreas[0];

        // À l'intérieur du corps déplié de la carte (< ExpandedHeight) mais sous la bande d'en-tête.
        int pointY = card.Y + ModelCard.HeaderHeight + 10;
        bool resolved = ScientificDashboard.TryResolveClick(hitAreas, card.X + 5, pointY, out _);

        Assert.False(resolved);
    }

    [Fact]
    public void TryResolveClick_PointOutsideAnyCard_IsIgnored()
    {
        IReadOnlyList<ModelCardHitArea> hitAreas = BuildHitAreas(new[] { "A", "B" }, _ => true, originX: 0, originY: 0);

        bool resolved = ScientificDashboard.TryResolveClick(hitAreas, -500, -500, out _);

        Assert.False(resolved);
    }

    [Fact]
    public void ComputeLayout_CollapsingEntireRow_ShrinksRowAndShiftsNextRowUp()
    {
        var names = new[] { "A", "B", "C", "D" }; // A,B,C = ligne 0 (CardsPerRow=3), D = ligne 1
        var collapsed = new HashSet<string> { "A", "B", "C" };

        var slots = ScientificDashboard.ComputeLayout(names, name => !collapsed.Contains(name), originX: 0, originY: 0, cardsPerRow: 3, cardWidth: ModelCard.Width, cardGap: 12);

        var slotD = slots.Single(slot => slot.ModelName == "D");
        Assert.Equal(ModelCard.CollapsedHeight + 12, slotD.Y);
    }

    [Fact]
    public void ComputeLayout_OneExpandedSiblingInRow_KeepsRowHeightExpanded_NoDeadGap()
    {
        var names = new[] { "A", "B", "C", "D" };
        var collapsed = new HashSet<string> { "B" }; // A et C restent dépliées sur la même ligne que B

        var slots = ScientificDashboard.ComputeLayout(names, name => !collapsed.Contains(name), originX: 0, originY: 0, cardsPerRow: 3, cardWidth: ModelCard.Width, cardGap: 12);

        var slotD = slots.Single(slot => slot.ModelName == "D");
        var slotB = slots.Single(slot => slot.ModelName == "B");

        // La ligne garde sa hauteur pleine tant qu'une voisine est encore dépliée : pas d'espace vide.
        Assert.Equal(ModelCard.ExpandedHeight + 12, slotD.Y);
        Assert.Equal(ModelCard.CollapsedHeight, slotB.Height);
    }

    [Fact]
    public void ComputeLayout_DifferentOrigin_ProducesConsistentRelativeLayout()
    {
        var names = new[] { "A", "B" };

        var slotsAtOrigin = ScientificDashboard.ComputeLayout(names, _ => true, originX: 10, originY: 20, cardsPerRow: 3, cardWidth: ModelCard.Width, cardGap: 12);
        var slotsAfterResize = ScientificDashboard.ComputeLayout(names, _ => true, originX: 200, originY: 400, cardsPerRow: 3, cardWidth: ModelCard.Width, cardGap: 12);

        // Le calcul repart toujours du point d'origine fourni (aucune coordonnée absolue mise en
        // cache d'un appel précédent) : la position relative à l'origine reste identique après resize.
        for (int index = 0; index < names.Length; index++)
        {
            Assert.Equal(slotsAtOrigin[index].X - 10, slotsAfterResize[index].X - 200);
            Assert.Equal(slotsAtOrigin[index].Y - 20, slotsAfterResize[index].Y - 400);
        }
    }

    private static IReadOnlyList<ModelCardHitArea> BuildHitAreas(string[] names, System.Func<string, bool> isExpanded, int originX, int originY)
    {
        var slots = ScientificDashboard.ComputeLayout(names, isExpanded, originX, originY, cardsPerRow: 3, cardWidth: ModelCard.Width, cardGap: 12);
        return slots.Select(slot => new ModelCardHitArea(slot.ModelName, slot.X, slot.Y, ModelCard.Width, ModelCard.HeaderHeight)).ToList();
    }
}
