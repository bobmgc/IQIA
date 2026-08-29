namespace IQIAIndicator.Tests.Research.StopLossCalibration;

/// <summary>
/// Sprint 15.10. Whether a candidate stop distance (already computed from entry-time-only data) would
/// have fired before the entry reverted to its entry-time equilibrium.
/// Reverted: price reached equilibrium at or before the stop distance was breached (or the stop was
/// never breached within the measured horizon).
/// StoppedOut: the stop distance was breached strictly before equilibrium was reached (or equilibrium
/// was never reached within the horizon).
/// Undetermined: neither happened within the measured horizon - the horizon was simply too short to
/// resolve this entry. Never silently folded into either bucket above.
/// </summary>
public enum EntryFate
{
    Reverted,
    StoppedOut,
    Undetermined
}

/// <summary>
/// Sprint 15.10. Derives StopHit/EntryFate from an already-computed OutcomeMeasurement - never re-walks
/// the price path per candidate stop distance, so sweeping many k values over many entries stays O(1)
/// lookups per (entry, k) after O(horizon) work done once per entry in OutcomeSimulator.
/// stopDistance must already be a resolved price-unit distance (e.g. k * InnovationStd at entry time) -
/// this class performs no unit conversion and adds no new parameter.
/// </summary>
public static class StopLossEvaluator
{
    public static (bool StopHit, int? StopHitBar, EntryFate Fate) Evaluate(OutcomeMeasurement outcome, double stopDistance)
    {
        int? stopHitBar = null;
        for (int i = 0; i < outcome.AdverseExcursionPath.Count; i++)
        {
            if (outcome.AdverseExcursionPath[i] >= stopDistance)
            {
                stopHitBar = i + 1;
                break;
            }
        }

        bool stopHit = stopHitBar.HasValue;

        EntryFate fate;
        if (stopHit && (outcome.EquilibriumBar is null || stopHitBar < outcome.EquilibriumBar))
        {
            fate = EntryFate.StoppedOut;
        }
        else if (outcome.EquilibriumBar is not null && (!stopHit || outcome.EquilibriumBar <= stopHitBar))
        {
            fate = EntryFate.Reverted;
        }
        else
        {
            fate = EntryFate.Undetermined;
        }

        return (stopHit, stopHitBar, fate);
    }
}
