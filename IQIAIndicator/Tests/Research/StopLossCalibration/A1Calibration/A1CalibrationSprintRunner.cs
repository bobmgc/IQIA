using System.Collections.Generic;

namespace IQIAIndicator.Tests.Research.StopLossCalibration.A1Calibration;

public sealed record A1SprintOutput(
    A1CampaignResult Campaign,
    IReadOnlyList<RegionCandidate> TrainRegions,
    IReadOnlyList<RegionSplitCheck> ValidationChecks,
    IReadOnlyList<RegionTestResult> TestResults,
    IReadOnlyList<LeaveOneOutResult> LeaveOneOut,
    IReadOnlyList<CurveShapeResult> CurveShapes,
    double MaxScaleDeviation);

/// <summary>
/// Sprint 15.14. Orchestrates Phases C-G in the mandated order: run the full grid once (TRAIN,
/// VALIDATION and TEST rows are all computed in the same pass, since they are independent seeded
/// series - the ordering constraint that matters is analytical, not computational), discover regions
/// from TRAIN only, check them against VALIDATION, confirm/reject against TEST last, then
/// sensitivity/family/scale/variance-break/trending/curve-shape analyses and the CSV/summary outputs.
/// No region is ever redefined after VALIDATION or TEST is inspected.
/// </summary>
public static class A1CalibrationSprintRunner
{
    public static A1SprintOutput RunAll()
    {
        A1CampaignResult campaign = A1KCampaignRunner.Run();

        IReadOnlyList<RegionCandidate> trainRegions = A1StableRegionDiscovery.DiscoverFromTrain(campaign.Rows);
        A1StableRegionDiscovery.Write(campaign.OutputDirectory, trainRegions);

        IReadOnlyList<RegionSplitCheck> validationChecks = A1ValidationConfirmation.Check(campaign.Rows, trainRegions, "VALIDATION");
        A1ValidationConfirmation.Write(campaign.OutputDirectory, validationChecks, "A1_k_train_validation.csv");

        IReadOnlyList<RegionTestResult> testResults = A1TestConfirmation.Confirm(campaign.Rows, trainRegions, validationChecks);
        A1TestConfirmation.Write(campaign.OutputDirectory, testResults);

        A1DatasetKConsensus.Write(campaign.OutputDirectory, campaign.Rows);

        IReadOnlyList<LeaveOneOutResult> leaveOneOut = A1LeaveOneOutAnalysis.Run(campaign.Rows, trainRegions);
        A1LeaveOneOutAnalysis.Write(campaign.OutputDirectory, leaveOneOut);

        (string _, double maxScaleDeviation) = A1ScaleAnalysis.Write(campaign.OutputDirectory, campaign.Rows);

        A1VarianceBreakAnalysis.Write(campaign.OutputDirectory);
        A1TrendingAnalysis.Write(campaign.OutputDirectory, campaign.Rows);

        IReadOnlyList<CurveShapeResult> curveShapes = A1CurveShapeAnalysis.Analyze(campaign.Rows);
        A1CurveShapeAnalysis.Write(campaign.OutputDirectory, curveShapes);

        A1CalibrationSummaryWriter.Write(campaign.OutputDirectory, campaign, trainRegions, validationChecks, testResults, curveShapes, maxScaleDeviation);

        return new A1SprintOutput(campaign, trainRegions, validationChecks, testResults, leaveOneOut, curveShapes, maxScaleDeviation);
    }
}
