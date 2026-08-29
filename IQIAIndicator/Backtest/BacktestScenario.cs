using System;
using System.Collections.Generic;
using IQIAIndicator.Core.MarketData;
using IQIAIndicator.Engine.Risk;

namespace IQIAIndicator.Backtest;

/// <summary>
/// Sprint 15.25 (Lot 14.1, brief §10). Immutable transport object naming everything one backtest run
/// needs. THIS LOT DOES NOT COMPUTE ANYTHING WITH THESE VALUES - no RiskEngine call, no simulated
/// account, no position, no execution (brief §10). They are carried, validated for presence, and nothing
/// else; <see cref="BacktestEngine"/> (this lot) only reads <see cref="Series"/> and <see cref="Window"/>.
///
/// No field here has a default. A scenario missing <see cref="InitialCapital"/> (i.e. not a positive
/// value - mirroring RiskEngine.Evaluate's own "InitialCapital &gt; 0" convention, Phase 1),
/// <see cref="Instrument"/>, or <see cref="Policy"/> is INVALID, never silently completed with an
/// invented number (brief §10: "Si InitialCapital est absent : scenario invalide", same for Instrument/
/// Policy). RiskPolicy's own fields stay individually nullable by design (Engine/Risk/RiskPolicy.cs:
/// "null = not constrained by this rule") - that is a property of RiskPolicy the Risk Engine already
/// relies on, not something this type re-validates; only the RiskPolicy OBJECT itself must be present.
/// </summary>
public sealed class BacktestScenario
{
    private BacktestScenario(
        HistoricalSeries series,
        BacktestWindow window,
        decimal initialCapital,
        InstrumentRiskSpecification instrument,
        RiskPolicy policy)
    {
        Series = series;
        Window = window;
        InitialCapital = initialCapital;
        Instrument = instrument;
        Policy = policy;
    }

    public HistoricalSeries Series { get; }

    public BacktestWindow Window { get; }

    public decimal InitialCapital { get; }

    public InstrumentRiskSpecification Instrument { get; }

    public RiskPolicy Policy { get; }

    /// <summary>Builds a validated scenario, or throws <see cref="ArgumentException"/> naming every
    /// violation. See <see cref="TryCreate"/> for the non-throwing form.</summary>
    public static BacktestScenario Create(
        HistoricalSeries series,
        BacktestWindow window,
        decimal initialCapital,
        InstrumentRiskSpecification instrument,
        RiskPolicy policy)
    {
        if (!TryCreate(series, window, initialCapital, instrument, policy, out BacktestScenario? scenario, out IReadOnlyList<string> errors))
        {
            throw new ArgumentException(
                $"BacktestScenario is invalid and is never silently completed with a default. Violations: {string.Join(" | ", errors)}");
        }

        return scenario;
    }

    /// <summary>Validates and builds without throwing. <paramref name="scenario"/> is non-null only when
    /// this returns true; <paramref name="errors"/> is empty only in that same case.</summary>
    public static bool TryCreate(
        HistoricalSeries series,
        BacktestWindow window,
        decimal initialCapital,
        InstrumentRiskSpecification instrument,
        RiskPolicy policy,
        out BacktestScenario scenario,
        out IReadOnlyList<string> errors)
    {
        var violations = new List<string>();

        if (series is null)
            violations.Add("HistoricalSeries is required.");

        if (window is null)
            violations.Add("BacktestWindow is required.");

        // Mirrors RiskEngine.Evaluate's own Phase 1 rule (Engine/Risk/RiskEngine.cs) so a scenario that
        // would already be rejected by the Risk Engine downstream is caught here, at configuration time,
        // with a clear reason instead of a generic capital-related rejection several stages later.
        if (initialCapital <= 0m)
            violations.Add($"InitialCapital must be positive (0 means \"not configured\", the same sentinel RiskEngine.Evaluate already uses). Actual={initialCapital}.");

        if (instrument is null)
            violations.Add("InstrumentRiskSpecification is required.");
        else if (!instrument.IsValid)
            violations.Add("InstrumentRiskSpecification is present but fails its own IsValid check (see InstrumentRiskSpecification.IsValid).");

        if (policy is null)
            violations.Add("RiskPolicy is required (its individual limit fields may each be null - that only means \"unconstrained\", per RiskPolicy's own contract).");

        if (violations.Count > 0)
        {
            scenario = null!;
            errors = violations;
            return false;
        }

        // Null-forgiving: reaching here means violations.Count == 0, which the checks above only allow
        // once series/window/instrument/policy are all confirmed non-null - the compiler's flow analysis
        // cannot correlate "violations is empty" with "these specific parameters are non-null", but the
        // logic above guarantees it.
        scenario = new BacktestScenario(series!, window!, initialCapital, instrument!, policy!);
        errors = Array.Empty<string>();
        return true;
    }
}
