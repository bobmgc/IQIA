using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using ATAS.Indicators;
using IQIAIndicator.Core;
using IQIAIndicator.Engine.Regime;

namespace IQIAIndicator;

/// <summary>
/// Point d'entree de l'indicateur IQIA dans ATAS.
/// Sprint 2 : construit le MarketContext, le valide, puis appelle le RegimeEngine.
/// Aucun signal, aucune fleche, aucun calcul de risque.
/// </summary>
[DisplayName("IQIA Signal")]
[Category("IQIA")]
public sealed class IQIAIndicator : Indicator
{
    private MarketContextBuilder _builder = null!;  // initialise au premier bar

    private readonly MarketContextValidator _validator    = new();
    private readonly MarketCache            _cache        = new();
    private readonly ILogger                _logger       = NullLogger.Instance;
    private readonly RegimeEngine           _regimeEngine = new();

    // --- Parametres instrument -----------------------------------------------
    [Display(Name = "Valeur du Tick (€/$)", GroupName = "Instrument", Order = 10)]
    [Range(0.01, 10_000)]
    public decimal TickValue { get; set; } = 12.50m;

    [Display(Name = "Valeur du Point (€/$)", GroupName = "Instrument", Order = 20)]
    [Range(0.01, 100_000)]
    public decimal PointValue { get; set; } = 50m;

    [Display(Name = "Decimales du prix", GroupName = "Instrument", Order = 30)]
    [Range(0, 8)]
    public int PriceDecimals { get; set; } = 2;

    public IQIAIndicator() : base(true)
    {
        DenyToChangePanel = true;
        ((ValueDataSeries)DataSeries[0]).VisualType = VisualMode.Hide;
    }

    protected override void OnCalculate(int bar, decimal value)
    {
        if (bar == 0 || _builder is null)
            _builder = CreateBuilder();

        var context    = _builder.Build(bar, CurrentBar);
        var validation = _validator.Validate(context);

        if (!validation.IsValid)
            return;

        var _ = _regimeEngine.Analyze(context);

        // Sprint 2 termine ici — Decision Engine au Sprint 3
    }

    // --- Cablage du builder avec les sources ATAS ----------------------------

    private MarketContextBuilder CreateBuilder() =>
        new(
            getBar:     GetCandle,
            symbol:     InstrumentInfo?.Instrument ?? string.Empty,
            tickSize:   InstrumentInfo?.TickSize   ?? 0m,
            tickValue:  TickValue,
            pointValue: PointValue,
            decimals:   PriceDecimals,
            timeFrame:  ChartInfo?.TimeFrame       ?? string.Empty
        );
}
