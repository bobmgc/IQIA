using ATAS.Indicators;

namespace IQIAIndicator.Core;

/// <summary>
/// Seul point d'acces direct aux API ATAS.
/// Lit les donnees brutes d'un bar et construit un MarketContext immutable.
/// Aucune logique metier : pas de cumul, pas de moyenne, pas d'indicateur calcule.
/// </summary>
public sealed class MarketContextBuilder
{
    // Seul couplage avec l'API ATAS
    private readonly Func<int, IndicatorCandle> _getBar;

    private readonly string  _symbol;
    private readonly decimal _tickSize;
    private readonly decimal _tickValue;
    private readonly decimal _pointValue;
    private readonly int     _decimals;
    private readonly string  _timeFrame;

    // Seul etat conserve : heure du premier bar pour ElapsedMinutes
    private DateTime _firstBarTime;

    public MarketContextBuilder(
        Func<int, IndicatorCandle> getBar,
        string  symbol,
        decimal tickSize,
        decimal tickValue,
        decimal pointValue,
        int     decimals,
        string  timeFrame)
    {
        _getBar     = getBar;
        _symbol     = symbol;
        _tickSize   = tickSize;
        _tickValue  = tickValue;
        _pointValue = pointValue;
        _decimals   = decimals;
        _timeFrame  = timeFrame;
    }

    /// <summary>
    /// Construit un MarketContext pour le bar specifie.
    /// Doit etre appele en sequence croissante (0, 1, 2...).
    /// </summary>
    public MarketContext Build(int bar, int currentBar)
    {
        var c = _getBar(bar);

        if (bar == 0)
            _firstBarTime = c.Time;

        return new MarketContext
        {
            BarIndex   = bar,
            TimeFrame  = _timeFrame,
            Price      = new PriceInfo(
                c.Open, c.High, c.Low, c.Close,
                (c.High + c.Low) / 2m,
                (c.High + c.Low + c.Close) / 3m),
            Volume     = new VolumeInfo(c.Volume, c.Bid, c.Ask, c.Delta),
            Instrument = new InstrumentInfo(_symbol, _tickSize, _tickValue, _pointValue, _decimals),
            Clock      = new MarketClock
            {
                CurrentTime    = c.Time,
                CurrentDate    = DateOnly.FromDateTime(c.Time),
                DayOfWeek      = c.Time.DayOfWeek,
                Session        = new SessionInfo(string.Empty, DateTime.MinValue, DateTime.MaxValue),
                ElapsedMinutes = bar == 0 ? 0 : (int)(c.Time - _firstBarTime).TotalMinutes,
                IsFirstBar     = bar == 0,
                IsLastBar      = bar == currentBar - 1
            },
            Execution  = new ExecutionContext
            {
                CurrentBar        = currentBar,
                LastCalculatedBar = bar,
                IsRealtime        = bar == currentBar - 1,
                IsHistorical      = bar < currentBar - 1,
                IsReplay          = false
            }
        };
    }
}
