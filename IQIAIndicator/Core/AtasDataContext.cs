namespace IQIAIndicator.Core;

/// <summary>
/// Sprint 15.25 (Lot 12.12, Problem A/C). Explicit, resolved LIVE/REPLAY context for this bar - distinct
/// from <see cref="ExecutionContext.IsReplay"/> (Core.MarketContextBuilder's in-house bar-index
/// heuristic, documented there and since Lot 12.6/12.10/12.11 as unreliable on its own: a real Replay
/// capture proved it misclassifies genuinely-replayed bars as "Realtime", and a real Live capture proved
/// the opposite - Lot 12.10). Wraps the SAME decision ATASEquityReplayDetector.IsReplayContext already
/// makes for Equity source selection (Lot 12.6/12.11) - never a second, independent decision - so every
/// dashboard consumer that needs to know "are we in Replay" reads the one already-corrected answer
/// instead of re-deriving it from the raw heuristic (see AtasDataContextResolver).
///
/// Unknown only before this bar's Risk stage has ever produced a resolution this session (no ATAS-derived
/// decision exists yet to report, e.g. before the first successfully-validated bar, or - Lot 12.12,
/// Problem B - immediately after a Risk-stage ATAS binding exception) - never a silent default to Live
/// or Replay.
/// </summary>
public enum AtasDataContext
{
    Unknown,
    Live,
    Replay
}
