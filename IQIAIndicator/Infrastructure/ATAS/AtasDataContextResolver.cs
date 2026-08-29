using IQIAIndicator.Core;

namespace IQIAIndicator.Infrastructure.ATAS;

/// <summary>
/// Sprint 15.25 (Lot 12.12, Problem A/C). Converts the boolean ATASEquityReplayDetector.IsReplayContext
/// already resolves every bar (Lot 12.6, evidence hierarchy extended Lot 12.11) into the explicit,
/// dashboard-facing <see cref="AtasDataContext"/> - a pure, one-line mapping, never a second decision
/// (see AtasDataContext's own doc comment for why: DashboardManager/DebugDashboard/DatasetDashboard
/// previously read the raw, independently-unreliable context.Execution.IsReplay heuristic directly,
/// completely disconnected from the corrected signal already used to select Equity's source - this
/// resolver is the single seam that lets every consumer share the one corrected answer instead).
///
/// <paramref name="hasBeenResolved"/> models the one state the boolean itself cannot represent: this
/// bar's Risk stage never produced a resolution at all this session (before the first successfully
/// validated bar, or - Lot 12.12, Problem B - after a Risk-stage ATAS binding exception left the
/// previous resolution stale) - reported as Unknown, never guessed as Live or Replay.
/// </summary>
public static class AtasDataContextResolver
{
    public static AtasDataContext Resolve(bool hasBeenResolved, bool isReplay)
    {
        if (!hasBeenResolved)
            return AtasDataContext.Unknown;

        return isReplay ? AtasDataContext.Replay : AtasDataContext.Live;
    }
}
