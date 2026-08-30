using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using IQIAIndicator.Engine.Decision.States;
using IQIAIndicator.Engine.ScientificModels.Abstractions;

namespace IQIAIndicator.Engine.ScientificModels.Trend;

/// <summary>
/// Audit 2026-08-30 (P0-2). First real implementation of the trend-following methodology's primary
/// model, replacing the Sprint 15.4 stub. Multi-horizon Time Series Momentum (Moskowitz, Ooi and
/// Pedersen, 2012 - see Documentation/Research/R-005): the signal is NOT "price above a moving
/// average", it is "do the RETURNS show a statistically persistent direction over several horizons".
///
/// SELF-CONTAINED (mirrors <c>DynamicZScoreModel</c>): reads only <c>MarketContext.History</c> - no
/// dependency on the Kalman/OU/Volatility/SPRT stack (all four are hard-gated to
/// <see cref="MarketState.MeanReverting"/> and cannot run in a trending regime), no plumbing of the
/// Fusion dimensions (the returns autocorrelation this model needs is computed here from the same
/// history).
///
/// METHOD (per horizon L, in bars):
///   cumRet_L  = ln(P_t / P_{t-L})                       (cumulative log return over the horizon)
///   sigmaBar  = sample stddev of the last L one-bar log returns
///   tstat_L   = cumRet_L / (sigmaBar * sqrt(L))          (volatility-scaled momentum t-statistic)
///   vote_L    = sign(tstat_L),  strength_L = tanh(|tstat_L| / <see cref="TStatScale"/>)
/// MomentumScore = mean over available horizons of (vote_L * strength_L)                 in [-1, 1]
/// HorizonAgreement = fraction of available horizons voting with the dominant sign
/// rho1 = lag-1 autocorrelation of one-bar log returns over the last <see cref="AutocorrelationWindow"/>
///        bars (positive -> persistent/trending, negative -> mean-reverting)
/// PersistenceFactor = clamp(0.5 + rho1, 0, 1)
/// Confidence (Score) = clamp(|MomentumScore| * HorizonAgreement * PersistenceFactor, 0, 1)
///
/// NON CALIBRATED: <see cref="DefaultLookbacks"/> (or the override), <see cref="TStatScale"/> and <see cref="AutocorrelationWindow"/>
/// are conventional starting values (1h/3h/6h/12h on an M5 chart; a 2.0 t-stat scale; a 100-bar ACF
/// window) - never grid-searched or optimised against this project's PnL. A future calibration lot
/// owns them.
/// </summary>
public sealed class TimeSeriesMomentumModel : IScientificModel
{
    public string Name => "TimeSeriesMomentumModel";

    public string Category => "Trend";

    /// <summary>NON CALIBRATED production default. Horizon lengths in bars - 1h/3h/6h/12h on an M5
    /// chart. Overridable per backtest call via PipelineParameterOverrides.MomentumLookbacks.</summary>
    public static readonly int[] DefaultLookbacks = { 12, 36, 72, 144 };

    private readonly int[] _lookbacks;
    private readonly int _shortestLookback;

    public TimeSeriesMomentumModel() : this(null)
    {
    }

    /// <param name="lookbacks">Multi-horizon lookback set in bars, or null/empty for
    /// <see cref="DefaultLookbacks"/>. Copied defensively; sorted ascending so the shortest is the
    /// warm-up floor.</param>
    public TimeSeriesMomentumModel(int[]? lookbacks)
    {
        int[] source = lookbacks is { Length: > 0 } ? lookbacks : DefaultLookbacks;
        _lookbacks = source.Where(value => value > 0).Distinct().OrderBy(value => value).ToArray();
        if (_lookbacks.Length == 0)
        {
            _lookbacks = DefaultLookbacks;
        }

        _shortestLookback = _lookbacks[0];
    }

    /// <summary>NON CALIBRATED. |t-stat| that maps (via tanh) to ~0.76 strength.</summary>
    private const double TStatScale = 2.0;

    /// <summary>NON CALIBRATED. Bars used for the lag-1 returns autocorrelation.</summary>
    private const int AutocorrelationWindow = 100;

    /// <summary>NON CALIBRATED. Bars used for the price-scale volatility estimate exposed for the
    /// stop-loss model (a trend-following trade has no VolatilityModel run to source it from).</summary>
    private const int VolatilityWindow = 60;

    public ScientificModelResult Evaluate(ScientificModelContext context)
    {
        bool compatible =
            context.DecisionResult.Winner == MarketState.Trending &&
            context.MethodologySelection.SelectedMethodology.Name == "TrendFollowingMethodology";

        if (!compatible)
        {
            return Failure("The selected methodology and decision winner are not compatible with the trend-following stack.");
        }

        IReadOnlyList<decimal> history = context.MarketContext.History;
        if (history is null || history.Count < _shortestLookback + 2)
        {
            return Failure($"TimeSeriesMomentumModel requires at least {_shortestLookback + 2} historical prices; got {history?.Count ?? 0}.");
        }

        double[] prices = new double[history.Count];
        for (int i = 0; i < history.Count; i++)
        {
            prices[i] = (double)history[i];
            if (!double.IsFinite(prices[i]) || prices[i] <= 0.0)
            {
                return Failure("TimeSeriesMomentumModel received a non-finite or non-positive price in the history.");
            }
        }

        double[] logReturns = new double[prices.Length - 1];
        for (int i = 1; i < prices.Length; i++)
        {
            logReturns[i - 1] = Math.Log(prices[i] / prices[i - 1]);
        }

        var contributions = new List<double>(_lookbacks.Length);
        var horizonTStats = new List<string>(_lookbacks.Length);
        int votesUp = 0, votesDown = 0;

        foreach (int lookback in _lookbacks)
        {
            if (logReturns.Length < lookback)
            {
                continue;
            }

            double cumulativeReturn = Math.Log(prices[^1] / prices[^(1 + lookback)]);
            double sigmaBar = SampleStandardDeviation(logReturns, logReturns.Length - lookback, lookback);
            if (!(sigmaBar > 0.0) || !double.IsFinite(cumulativeReturn))
            {
                continue;
            }

            double tStat = cumulativeReturn / (sigmaBar * Math.Sqrt(lookback));
            if (!double.IsFinite(tStat))
            {
                continue;
            }

            int vote = Math.Sign(tStat);
            if (vote > 0) votesUp++;
            else if (vote < 0) votesDown++;

            double strength = Math.Tanh(Math.Abs(tStat) / TStatScale);
            contributions.Add(vote * strength);
            horizonTStats.Add($"L{lookback}={tStat.ToString("F3", CultureInfo.InvariantCulture)}");
        }

        if (contributions.Count == 0)
        {
            return Failure("TimeSeriesMomentumModel could not evaluate a single horizon (degenerate volatility on every lookback).");
        }

        double momentumScore = contributions.Average();
        int dominantVotes = Math.Max(votesUp, votesDown);
        double horizonAgreement = dominantVotes / (double)contributions.Count;

        double rho1 = Lag1Autocorrelation(logReturns, Math.Min(AutocorrelationWindow, logReturns.Length));
        double persistenceFactor = Math.Clamp(0.5 + rho1, 0.0, 1.0);

        // Price-scale volatility (sigma of recent log returns, re-expressed as a price distance around
        // the last price) - exposed so VolatilityStopLossModel can size a stop for a trending trade,
        // which has no VolatilityModel run to read CurrentVolatility from.
        int volWindow = Math.Min(VolatilityWindow, logReturns.Length);
        double logReturnSigma = SampleStandardDeviation(logReturns, logReturns.Length - volWindow, volWindow);
        double currentVolatility = prices[^1] * logReturnSigma;

        double confidence = Math.Clamp(Math.Abs(momentumScore) * horizonAgreement * persistenceFactor, 0.0, 1.0);
        if (!double.IsFinite(confidence))
        {
            confidence = 0.0;
        }

        string direction = momentumScore > 0.0 ? "LONG" : momentumScore < 0.0 ? "SHORT" : "FLAT";
        string diagnostics =
            $"Horizons: {string.Join(", ", horizonTStats)}. MomentumScore={momentumScore.ToString("F3", CultureInfo.InvariantCulture)}, " +
            $"HorizonAgreement={horizonAgreement.ToString("F3", CultureInfo.InvariantCulture)}, " +
            $"ReturnsAutocorrelation={rho1.ToString("F3", CultureInfo.InvariantCulture)}.";

        var metrics = new Dictionary<string, object>
        {
            [ScientificMetricKeys.MomentumScore] = momentumScore,
            [ScientificMetricKeys.MomentumConfidence] = confidence,
            [ScientificMetricKeys.HorizonAgreement] = horizonAgreement,
            [ScientificMetricKeys.ReturnsAutocorrelation] = rho1,
            [ScientificMetricKeys.CurrentVolatility] = currentVolatility,
            ["HorizonsEvaluated"] = contributions.Count,
            ["MomentumDirection"] = direction,
            [ScientificMetricKeys.Diagnostics] = diagnostics
        };

        return new ScientificModelResult(
            Name,
            true,
            confidence,
            "Time Series Momentum evaluation completed across " + contributions.Count + " horizon(s).",
            metrics);
    }

    private ScientificModelResult Failure(string explanation) => new(Name, false, 0.0, explanation);

    /// <summary>Sample standard deviation of <paramref name="count"/> values starting at
    /// <paramref name="offset"/> (Bessel-corrected; 0 when fewer than two values).</summary>
    private static double SampleStandardDeviation(double[] values, int offset, int count)
    {
        if (count < 2)
        {
            return 0.0;
        }

        double mean = 0.0;
        for (int i = 0; i < count; i++)
        {
            mean += values[offset + i];
        }
        mean /= count;

        double sumSquares = 0.0;
        for (int i = 0; i < count; i++)
        {
            double delta = values[offset + i] - mean;
            sumSquares += delta * delta;
        }

        return Math.Sqrt(sumSquares / (count - 1));
    }

    /// <summary>Lag-1 autocorrelation of the last <paramref name="window"/> values (0 when the window
    /// has fewer than three values or is degenerate).</summary>
    private static double Lag1Autocorrelation(double[] values, int window)
    {
        if (window < 3)
        {
            return 0.0;
        }

        int start = values.Length - window;
        double mean = 0.0;
        for (int i = 0; i < window; i++)
        {
            mean += values[start + i];
        }
        mean /= window;

        double numerator = 0.0;
        double denominator = 0.0;
        for (int i = 0; i < window; i++)
        {
            double delta = values[start + i] - mean;
            denominator += delta * delta;
            if (i < window - 1)
            {
                numerator += delta * (values[start + i + 1] - mean);
            }
        }

        if (!(denominator > 0.0))
        {
            return 0.0;
        }

        double rho = numerator / denominator;
        return double.IsFinite(rho) ? Math.Clamp(rho, -1.0, 1.0) : 0.0;
    }
}
