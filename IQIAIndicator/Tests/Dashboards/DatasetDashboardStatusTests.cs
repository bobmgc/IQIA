using System;
using IQIAIndicator.Core.Calibration;
using IQIAIndicator.Visualization.Dashboards;
using Xunit;

namespace IQIAIndicator.Tests.Dashboards;

/// <summary>
/// Vérifie la sémantique de DatasetDashboard.ResolveDatasetStatus (Sprint 13.3) — pas uniquement
/// le texte affiché, mais l'état DatasetState réellement résolu à partir des données exposées par
/// ScientificDatasetCollector.Status et DashboardContext.EnableScientificDataset.
/// </summary>
public sealed class DatasetDashboardStatusTests
{
    [Fact]
    public void ResolveDatasetStatus_CollectorAbsent_IsIdle()
    {
        DatasetState state = DatasetDashboard.ResolveDatasetStatus(collector: null, enableScientificDataset: false);

        Assert.Equal(DatasetState.Idle, state);
    }

    [Fact]
    public void ResolveDatasetStatus_CollectorPresentButNeverCollected_DisabledStaysIdle()
    {
        var collector = new ScientificDatasetCollector(Guid.NewGuid());

        DatasetState state = DatasetDashboard.ResolveDatasetStatus(collector, enableScientificDataset: false);

        Assert.Equal(DatasetState.Idle, state);
    }

    [Fact]
    public void ResolveDatasetStatus_ActiveCollectorCollectingStatus_IsCollecting()
    {
        var collector = new ScientificDatasetCollector(Guid.NewGuid());
        collector.Add(CreateRecord(collector.SessionId, currentBar: 0));

        Assert.Equal("Collecting", collector.Status);

        DatasetState state = DatasetDashboard.ResolveDatasetStatus(collector, enableScientificDataset: true);

        Assert.Equal(DatasetState.Collecting, state);
    }

    [Fact]
    public void ResolveDatasetStatus_ActiveCollectorDebugStatus_IsDebug()
    {
        var collector = new ScientificDatasetCollector(Guid.NewGuid()) { IsDebugMode = true };
        collector.Add(CreateRecord(collector.SessionId, currentBar: 0));

        Assert.Equal("Debug", collector.Status);

        DatasetState state = DatasetDashboard.ResolveDatasetStatus(collector, enableScientificDataset: true);

        Assert.Equal(DatasetState.Debug, state);
    }

    [Fact]
    public void ResolveDatasetStatus_CollectorWithDataThenDisabled_IsStopped()
    {
        var collector = new ScientificDatasetCollector(Guid.NewGuid());
        collector.Add(CreateRecord(collector.SessionId, currentBar: 0));

        DatasetState state = DatasetDashboard.ResolveDatasetStatus(collector, enableScientificDataset: false);

        Assert.Equal(DatasetState.Stopped, state);
    }

    [Fact]
    public void ResolveDatasetStatus_CollectorWithDataReenabled_IsCollectingAgain()
    {
        var collector = new ScientificDatasetCollector(Guid.NewGuid());
        collector.Add(CreateRecord(collector.SessionId, currentBar: 0));

        // Simule l'arrêt puis la réactivation : le collecteur ne redémarre jamais de zéro
        // (TotalAddAttempts ne redescend pas), seul le drapeau EnableScientificDataset change.
        DatasetState stopped = DatasetDashboard.ResolveDatasetStatus(collector, enableScientificDataset: false);
        DatasetState reactivated = DatasetDashboard.ResolveDatasetStatus(collector, enableScientificDataset: true);

        Assert.Equal(DatasetState.Stopped, stopped);
        Assert.Equal(DatasetState.Collecting, reactivated);
    }

    private static ScientificDatasetRecord CreateRecord(Guid sessionId, int currentBar)
    {
        return new ScientificDatasetRecord(
            sessionId,
            DateTime.UtcNow,
            "TEST",
            "1m",
            100m,
            1,
            currentBar,
            new System.Collections.Generic.Dictionary<string, double?>(),
            new System.Collections.Generic.Dictionary<string, string>());
    }
}
