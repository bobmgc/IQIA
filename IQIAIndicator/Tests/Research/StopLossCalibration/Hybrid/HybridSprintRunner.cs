namespace IQIAIndicator.Tests.Research.StopLossCalibration.Hybrid;

/// <summary>
/// Sprint 15.13. Phase B orchestrator (brief §28 execution order) - runs the full campaign and every
/// derived analysis CSV. Runs only after the Phase A conformance/look-ahead audit has passed and the
/// user has explicitly authorized the full campaign; not invoked automatically by Phase A.
/// </summary>
public static class HybridSprintRunner
{
    public static void RunAll()
    {
        HybridCampaignResult campaign = HybridCampaignRunner.Run();

        HybridEntryAnalysis.RunAndWrite();
        HybridDatasetSummaryAnalysis.Write(campaign);
        HybridScaleAnalysis.Write(campaign);
        HybridTrainValidationTestAnalysis.Write(campaign);
        HybridSensitivityAnalysis.Write(campaign);
        HybridVarianceBreakAnalysis.Write(campaign.OutputDirectory);
        HybridTrendingAnalysis.Write(campaign.OutputDirectory);
    }
}
