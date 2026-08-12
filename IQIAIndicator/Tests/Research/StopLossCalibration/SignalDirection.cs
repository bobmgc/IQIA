namespace IQIAIndicator.Tests.Research.StopLossCalibration;

/// <summary>
/// Sprint 15.10. Deliberately distinct from Engine.EntryTrigger.DirectionCandidate - the harness never
/// imports or reuses production's direction type, to keep the isolation boundary obvious at a glance.
/// Assigned the same way EntryTriggerBuilder.DetermineDirection already does (DynamicZScore &lt; 0 =>
/// Buy, &gt; 0 => Sell, ==0 => no entry) - this is a restatement of that existing, unchanged rule for
/// the harness's own use, not a new one.
/// </summary>
public enum SignalDirection
{
    Buy,
    Sell
}
