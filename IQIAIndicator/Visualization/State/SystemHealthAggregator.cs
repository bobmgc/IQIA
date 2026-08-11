using IQIAIndicator.Engine.Decision.States;
using IQIAIndicator.Visualization.Rendering;

namespace IQIAIndicator.Visualization.State;

/// <summary>
/// Synthèse PASS/WARN/FAIL par étage, calculée uniquement à partir de signaux déjà
/// produits par le pipeline (succès/échec de modèles, compteurs du collecteur de dataset,
/// indicateur "renderer appelé", présence d'une session d'export).
/// Pure présentation : n'introduit aucune nouvelle notion métier ni aucun calcul scientifique.
/// </summary>
internal sealed record SystemHealthReport(
    HealthState Scientific,
    HealthState Fusion,
    HealthState Decision,
    HealthState Dataset,
    HealthState Renderer,
    HealthState Export);

internal static class SystemHealthAggregator
{
    public static SystemHealthReport Evaluate(DashboardContext context)
    {
        return new SystemHealthReport(
            Scientific: EvaluateScientific(context),
            Fusion: EvaluateFusion(context),
            Decision: EvaluateDecision(context),
            Dataset: EvaluateDataset(context),
            Renderer: EvaluateRenderer(context),
            Export: EvaluateExport(context));
    }

    private static HealthState EvaluateScientific(DashboardContext context)
    {
        var assessment = context.ScientificAssessment;
        if (assessment is null)
            return HealthState.Unknown;

        if (assessment.SuccessfulModels.Count == 0)
            return HealthState.Fail;

        return assessment.FailedModels.Count > 0 ? HealthState.Warn : HealthState.Pass;
    }

    private static HealthState EvaluateFusion(DashboardContext context)
    {
        if (context.FusionSnapshot is null || context.FusionResult is null)
            return HealthState.Unknown;

        return context.FusionResult.Dimensions.Count > 0 ? HealthState.Pass : HealthState.Fail;
    }

    private static HealthState EvaluateDecision(DashboardContext context)
    {
        if (context.DecisionResult is null)
            return HealthState.Unknown;

        return context.DecisionResult.Winner == MarketState.Unknown ? HealthState.Warn : HealthState.Pass;
    }

    private static HealthState EvaluateDataset(DashboardContext context)
    {
        if (!context.EnableScientificDataset)
            return HealthState.Unknown;

        var collector = context.DatasetCollector;
        if (collector is null || collector.TotalAddAttempts == 0)
            return HealthState.Waiting;

        if (collector.RecordsAccepted == 0)
            return HealthState.Fail;

        return collector.DuplicateRecordsRejected > 0 ? HealthState.Warn : HealthState.Pass;
    }

    private static HealthState EvaluateRenderer(DashboardContext context) =>
        context.Execution is null ? HealthState.Unknown : HealthState.Pass;

    private static HealthState EvaluateExport(DashboardContext context)
    {
        if (!context.EnableScientificDataset)
            return HealthState.Unknown;

        return context.DatasetSession is null ? HealthState.Waiting : HealthState.Pass;
    }
}
