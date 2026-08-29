using IQIAIndicator.Tests.Research.StopLossCalibration;
using Xunit;

namespace IQIAIndicator.Tests.XunitWrappers;

public sealed class Sprint1515HistoricalReconciliationXunitTests
{
    /// <summary>
    /// Skipped as of Sprint 15.22 (QDE-012 InnovationStd correction, KalmanFilterModel.cs). This test's
    /// premise was "the current code reproduces Sprint 15.14's published A1_stable_regions.csv numbers
    /// exactly" - true only while KalmanFilterModel used the pre-15.22 full-history design that Sprint
    /// 15.21 proved defective (InnovationStd grows with total history length on non-stationary series;
    /// PROVEN both on the real Sprint 15.19 ES/M5 capture and on a synthetic RandomWalk of the same
    /// length). Sprint 15.22 bounded KalmanFilterModel's observation window specifically to fix that
    /// defect, which necessarily and intentionally changes InnovationStd (and everything A1 derives
    /// from it) on any series long/non-stationary enough to have exercised the old bug - exactly what
    /// this test's synthetic TRAIN curves do. A failure here is the predicted, documented consequence
    /// of the fix, not a regression - see QDE-012_Sprint_15.21 report §12 ("15.14 ...
    /// INVALIDATED_BY_REAL_DATA") and QDE-012_Sprint_15.22 report. Sprint 15.14's published numbers
    /// remain on disk unmodified as the historical record of the PRE-fix model's behavior.
    /// </summary>
    [Fact(Skip = "Retired Sprint 15.22: premise (reproduces pre-15.22 Kalman numbers exactly) is now permanently false by design - see doc comment and QDE-012_Sprint_15.21/15.22 reports.")]
    public void VerifySprint1514Unaffected() => Sprint1515HistoricalReconciliation.VerifySprint1514Unaffected();
}
