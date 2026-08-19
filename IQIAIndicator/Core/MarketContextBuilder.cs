using System;
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

    // Sprint 15.25 (Lot 12.12, Problem B): no longer readonly - see RefreshInstrument's doc comment.
    private string  _symbol;
    private decimal _tickSize;
    private decimal _tickValue;
    private decimal _pointValue;
    private int     _decimals;
    private string  _timeFrame;

    // Seul etat conserve : heure du premier bar pour ElapsedMinutes
    private DateTime _firstBarTime;

    // Plus grand bar déjà vu en temps réel (IsRealtime == true) par ce builder.
    // Sert uniquement à la détection heuristique du Replay (voir Build).
    private int _maxRealtimeBar = -1;

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

    /// <summary>Sprint 15.25 (Lot 12.12, Problem B): exposes the currently-cached TickSize so the caller
    /// (IQIAIndicator.cs) can detect a builder constructed before ATAS's InstrumentInfo.TickSize was
    /// actually populated - see RefreshInstrument's doc comment for the full rationale.</summary>
    public decimal TickSize => _tickSize;

    /// <summary>
    /// Sprint 15.25 (Lot 12.12, Problem B). Re-applies the instrument snapshot ATAS reports THIS bar,
    /// without touching any other builder state (_firstBarTime/_maxRealtimeBar - the Replay-heuristic
    /// bookkeeping Build() below relies on). Exists solely to self-heal a builder constructed at bar 0
    /// before ATAS's own InstrumentInfo.TickSize was actually populated: IQIAIndicator.cs only ever
    /// (re)constructs this builder once, at bar==0 (documented contract: "Doit etre appele en sequence
    /// croissante"); if TickSize was 0 at that single moment (plausible during ATAS's initial
    /// historical-load pass, before per-instrument metadata is fully resolved), MarketContextValidator
    /// .CheckInstrument would otherwise reject EVERY subsequent bar for the rest of the session -
    /// including live bars long after the instrument metadata became available - freezing the entire
    /// downstream pipeline (Risk stage, dataset collection, BarIndex) with no diagnostic trace (Lot
    /// 12.12 report, Problem B). Never invents a value: only re-reads the SAME ATAS-provided fields
    /// CreateBuilder() already reads, whenever the caller detects TickSize is still non-positive.
    /// </summary>
    public void RefreshInstrument(
        string  symbol,
        decimal tickSize,
        decimal tickValue,
        decimal pointValue,
        int     decimals,
        string  timeFrame)
    {
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
        {
            _firstBarTime = c.Time;
            _maxRealtimeBar = -1;
        }

        bool isRealtime = bar == currentBar - 1;

        // Détection heuristique du Replay ATAS : le SDK public (ATAS.Indicators / ATAS.Types /
        // ATAS.DataFeedsCore, vérifié par réflexion) n'expose aucun flag "chart en cours de
        // relecture". En dehors d'un Replay, un bar déjà vu en temps réel n'est jamais recalculé :
        // OnCalculate ne revisite un bar <= _maxRealtimeBar qu'au moment d'un rewind de Replay.
        bool isReplay = bar <= _maxRealtimeBar && !isRealtime;
        if (isRealtime)
            _maxRealtimeBar = Math.Max(_maxRealtimeBar, bar);

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
                IsRealtime        = isRealtime,
                IsHistorical      = bar < currentBar - 1,
                IsReplay          = isReplay
            }
        };
    }
}
