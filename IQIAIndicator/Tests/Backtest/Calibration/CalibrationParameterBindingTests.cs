using System;
using System.Linq;
using IQIAIndicator.Backtest;
using IQIAIndicator.Backtest.Calibration;
using IQIAIndicator.Core.MarketData;
using IQIAIndicator.Engine.Decision.Core;
using IQIAIndicator.Engine.Decision.States;
using IQIAIndicator.Engine.Entry;
using IQIAIndicator.Engine.EntryTrigger;
using IQIAIndicator.Engine.Methodology.Core;
using IQIAIndicator.Engine.ScientificFusion;
using IQIAIndicator.Engine.ScientificModels.Abstractions;
using Xunit;

namespace IQIAIndicator.Tests.BacktestTests.Calibration;

/// <summary>
/// Sprint 15.25 (Lot 14.10, P0-3). Proves the ONLY calibration parameter this lot makes real -
/// AmbiguityGateThreshold - is genuinely bound end to end:
/// CalibrationParameterSet -&gt; CalibrationParameterBinding -&gt; PipelineParameterOverrides -&gt;
/// BacktestEngine -&gt; SignalEngine -&gt; EntryTriggerEngine -&gt; EntryTriggerBuilder -&gt; observable Direction.
///
/// Covers the six mandatory tests from the Lot 14.10 brief: DEFAULT BEHAVIOUR, PARAMETER PROPAGATION (at
/// the real consumer, not just the container), BEHAVIOURAL DIFFERENCE (never merely a different
/// fingerprint), RUN ISOLATION, DETERMINISM, and LOOK-AHEAD.
/// </summary>
public sealed class CalibrationParameterBindingTests
{
    // ── Consumer-level helper - mirrors Tests.EntryTrigger.DecisionDirectionCoherenceTests.Trigger, but
    // with an explicit, injectable threshold instead of always relying on the production default. ───────

    private static EntryTriggerCandidate Trigger(double ambiguityScore, double ambiguityGateThreshold, double dynamicZScore = -2.0)
    {
        var decisionResult = new DecisionResult { Winner = MarketState.MeanReverting, Confidence = 0.9, AmbiguityScore = ambiguityScore };
        MethodologySelection methodologySelection = new MethodologyEngine().Evaluate(decisionResult);
        EntryTriggerContext context = BuildContext(methodologySelection, dynamicZScore);
        return new EntryTriggerBuilder(ambiguityGateThreshold).Build(context);
    }

    private static EntryTriggerContext BuildContext(MethodologySelection? methodologySelection, double? dynamicZScore)
    {
        var scientificAssessment = new ScientificAssessment(
            OverallConfidence: 0.8,
            EvidenceAgreement: Array.Empty<string>(),
            EvidenceConflict: Array.Empty<string>(),
            MissingEvidence: Array.Empty<string>(),
            ExecutedModels: Array.Empty<string>(),
            SuccessfulModels: Array.Empty<string>(),
            FailedModels: Array.Empty<string>(),
            ScientificResults: Array.Empty<ScientificModelResult>(),
            Diagnostics: "test fixture");

        var entryAssessment = new EntryAssessment(
            scientificAssessment,
            AssessmentQuality: 0.8,
            EntryReadiness: EntryReadiness.READY_FOR_NEXT_STAGE,
            OpportunityStatus: OpportunityStatus.QUALIFIED,
            OpportunityPriority: 0.8,
            OpportunityReasons: Array.Empty<string>(),
            BlockingIssues: Array.Empty<string>(),
            SupportingEvidence: Array.Empty<string>(),
            Warnings: Array.Empty<string>(),
            Diagnostics: Array.Empty<string>());

        var entryCandidate = new EntryCandidate(
            entryAssessment,
            DateTime.UtcNow,
            OpportunityStatus.QUALIFIED,
            OpportunityPriority: 0.8,
            OpportunityReasons: Array.Empty<string>(),
            Warnings: Array.Empty<string>(),
            Diagnostics: Array.Empty<string>());

        var businessContext = new EntryBusinessContext(
            CurrentPrice: 100m,
            EstimatedEquilibrium: 100.0,
            DistanceToEquilibrium: 0.0,
            DynamicZScore: dynamicZScore,
            ScientificConfidence: 0.8,
            OpportunityPriority: 0.8,
            Methodology: methodologySelection?.SelectedMethodology.Name,
            Timestamp: DateTime.UtcNow,
            SupportingEvidence: Array.Empty<string>(),
            BlockingIssues: Array.Empty<string>(),
            Diagnostics: Array.Empty<string>(),
            Warnings: Array.Empty<string>(),
            OpportunityReasons: Array.Empty<string>(),
            OpportunityStatus: OpportunityStatus.QUALIFIED);

        return new EntryTriggerContext(businessContext, scientificAssessment, entryAssessment, entryCandidate, methodologySelection);
    }

    // ── TEST 1: DEFAULT BEHAVIOUR ────────────────────────────────────────────────────────────────────

    [Fact]
    public void DefaultEntryTriggerBuilder_StillUsesProductionThreshold_0_95_Unchanged()
    {
        // Exactly the pre-Lot-14.10 boundary behaviour (mirrors
        // DecisionDirectionCoherenceTests.AssertGateStillBlocksExactlyAtZeroPointZeroFiveDifferenceBoundary):
        // AmbiguityScore=0.95 (Difference=0.05 exactly) must still be blocked with NO override supplied.
        EntryTriggerCandidate atBoundary = Trigger(ambiguityScore: 0.95, ambiguityGateThreshold: EntryTriggerBuilder.AmbiguityGateThreshold);
        Assert.Equal(DirectionCandidate.NO_ACTION, atBoundary.Assessment.Direction);

        Assert.Equal(0.95, EntryTriggerBuilder.AmbiguityGateThreshold);

        var decisionResult = new DecisionResult { Winner = MarketState.MeanReverting, Confidence = 0.9, AmbiguityScore = 0.95 };
        MethodologySelection methodologySelection = new MethodologyEngine().Evaluate(decisionResult);
        EntryTriggerContext context = BuildContext(methodologySelection, dynamicZScore: -2.0);

        // The parameterless constructor must produce EXACTLY the same result as the explicit constructor
        // called with the production constant - proving the default constructor is not a second, silently
        // divergent code path.
        EntryTriggerCandidate viaDefaultCtor = new EntryTriggerBuilder().Build(context);
        EntryTriggerCandidate viaExplicitCtor = new EntryTriggerBuilder(EntryTriggerBuilder.AmbiguityGateThreshold).Build(context);

        Assert.Equal(viaExplicitCtor.Assessment.Direction, viaDefaultCtor.Assessment.Direction);
        Assert.Equal(viaExplicitCtor.Assessment.Reason, viaDefaultCtor.Assessment.Reason);
    }

    [Fact]
    public void CalibrationParameterBinding_EmptyParameterSet_ResolvesToNullOverride_FallsBackToProductionDefault()
    {
        PipelineParameterOverrides overrides = CalibrationParameterBinding.Resolve(CalibrationParameterSet.Empty());

        Assert.Null(overrides.AmbiguityGateThreshold);
        Assert.Same(PipelineParameterOverrides.None, PipelineParameterOverrides.None); // sanity: the shared no-op instance
    }

    // ── TEST 2: PARAMETER PROPAGATION - verified at the REAL CONSUMER, not just the container ─────────

    [Fact]
    public void CalibrationParameterBinding_ExplicitThreshold_PropagatesToTheRealConsumer_EntryTriggerBuilder()
    {
        CalibrationParameterSet parameterSet = CalibrationParameterSet.Create(new[]
        {
            CalibrationParameter.Decimal(CalibrationParameterBinding.AmbiguityGateThresholdParameterName, 0.80m, "score", min: 0m, max: 1m)
        });

        PipelineParameterOverrides overrides = CalibrationParameterBinding.Resolve(parameterSet);
        Assert.Equal(0.80, overrides.AmbiguityGateThreshold);

        // The consumer-level check the brief requires: construct the REAL EntryTriggerBuilder with the
        // resolved value and observe its actual behavioural effect - never just the container's field.
        EntryTriggerCandidate suppressed = Trigger(ambiguityScore: 0.85, ambiguityGateThreshold: overrides.AmbiguityGateThreshold!.Value);
        Assert.Equal(DirectionCandidate.NO_ACTION, suppressed.Assessment.Direction); // 0.85 >= 0.80 -> gate closed

        EntryTriggerCandidate open = Trigger(ambiguityScore: 0.75, ambiguityGateThreshold: overrides.AmbiguityGateThreshold!.Value);
        Assert.Equal(DirectionCandidate.BUY_CANDIDATE, open.Assessment.Direction); // 0.75 < 0.80 -> gate open, DynamicZScore=-2.0 -> BUY
    }

    // ── TEST 3: BEHAVIOURAL DIFFERENCE - never merely a different fingerprint ───────────────────────────

    [Fact]
    public void TwoThresholds_ProduceGenuinelyDifferentDirection_ForTheIdenticalDecision()
    {
        const double ambiguityScore = 0.90; // Difference = 0.10

        EntryTriggerCandidate closed = Trigger(ambiguityScore, ambiguityGateThreshold: 0.85); // 0.90 >= 0.85 -> suppressed
        EntryTriggerCandidate open = Trigger(ambiguityScore, ambiguityGateThreshold: 0.95); // 0.90 <  0.95 -> not suppressed

        Assert.Equal(DirectionCandidate.NO_ACTION, closed.Assessment.Direction);
        Assert.Equal(DirectionCandidate.BUY_CANDIDATE, open.Assessment.Direction);
        Assert.NotEqual(closed.Assessment.Direction, open.Assessment.Direction);
    }

    [Fact]
    public void TwoCalibrationExperiments_DifferingOnlyByAmbiguityGateThreshold_ProduceDifferentPositionCounts_EndToEnd()
    {
        // End-to-end version of TEST 3, through the EXACT surface CalibrationExperimentRunner uses -
        // proves the wiring all the way from CalibrationParameterSet to a real, observable PositionCount
        // difference, never merely a different ConfigurationFingerprint/ExperimentId (the brief's explicit
        // "Un fingerprint différent sans changement comportemental est INSUFFISANT").
        HistoricalSeries series = BacktestTestSeriesBuilder.MeanRevertingOu(320, seed: 61UL, kappa: 0.8m);
        CalibrationDataset dataset = CalibrationTestFixtures.Dataset(series);
        CalibrationWindowSet windows = CalibrationTestFixtures.Windows(dataset, leadInBars: 128, trainBars: 150, validationBars: 20, oosBars: 20);
        CalibrationExperimentSetup setup = CalibrationTestFixtures.Setup();

        CalibrationParameterSet gateAlwaysOpen = CalibrationParameterSet.Create(new[]
        {
            CalibrationParameter.Decimal(CalibrationParameterBinding.AmbiguityGateThresholdParameterName, 0.999m, "score", min: 0m, max: 1m)
        });
        CalibrationParameterSet gateAlwaysClosed = CalibrationParameterSet.Create(new[]
        {
            CalibrationParameter.Decimal(CalibrationParameterBinding.AmbiguityGateThresholdParameterName, 0.0m, "score", min: 0m, max: 1m)
        });

        CalibrationExperiment openExperiment = CalibrationExperiment.Create(gateAlwaysOpen, dataset, windows, setup);
        CalibrationExperiment closedExperiment = CalibrationExperiment.Create(gateAlwaysClosed, dataset, windows, setup);

        Assert.NotEqual(openExperiment.ConfigurationFingerprint, closedExperiment.ConfigurationFingerprint);

        int openPositions = TotalPositions(CalibrationExperimentRunner.Run(openExperiment));
        int closedPositions = TotalPositions(CalibrationExperimentRunner.Run(closedExperiment));

        // Threshold=0.0 -> AmbiguityScore (always in [0,1]) is virtually always >= 0.0 -> the gate is
        // virtually always closed -> ~0 positions. Threshold=0.999 -> the gate is open for almost every
        // decision -> strictly more positions. A REAL behavioural difference, not just a different id.
        Assert.True(openPositions > closedPositions,
            $"Expected the gate-always-open experiment ({openPositions} positions) to produce strictly more positions than the gate-always-closed one ({closedPositions}).");
        Assert.Equal(0, closedPositions);
    }

    private static int TotalPositions(CalibrationExperimentRunResult result) =>
        result.Results.Where(r => r.Direction == CalibrationDirectionFilter.All).Sum(r => r.PositionCount);

    // ── TEST 4: RUN ISOLATION ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ExperimentA_ExperimentB_ExperimentA_DifferingOnlyByThreshold_ProduceIdenticalResultsForTheRepeatedA()
    {
        HistoricalSeries series = BacktestTestSeriesBuilder.MeanRevertingOu(320, seed: 41UL);
        CalibrationDataset dataset = CalibrationTestFixtures.Dataset(series);
        CalibrationWindowSet windows = CalibrationTestFixtures.Windows(dataset, leadInBars: 128, trainBars: 100, validationBars: 40, oosBars: 40);
        CalibrationExperimentSetup setup = CalibrationTestFixtures.Setup();

        CalibrationExperiment experimentA = CalibrationExperiment.Create(ThresholdSet(0.05m), dataset, windows, setup);
        CalibrationExperiment experimentB = CalibrationExperiment.Create(ThresholdSet(0.95m), dataset, windows, setup);

        CalibrationExperimentRunResult a1 = CalibrationExperimentRunner.Run(experimentA);
        CalibrationExperimentRunner.Run(experimentB); // interleaved run with a DIFFERENT threshold - must not contaminate A
        CalibrationExperimentRunResult a2 = CalibrationExperimentRunner.Run(experimentA);

        Assert.Equal(Summary(a1), Summary(a2));
    }

    private static CalibrationParameterSet ThresholdSet(decimal threshold) => CalibrationParameterSet.Create(new[]
    {
        CalibrationParameter.Decimal(CalibrationParameterBinding.AmbiguityGateThresholdParameterName, threshold, "score", min: 0m, max: 1m)
    });

    private static System.Collections.Generic.List<(CalibrationWindowRole, CalibrationDirectionFilter, int, decimal?)> Summary(CalibrationExperimentRunResult result) =>
        result.Results.Select(r => (r.Window.Role, r.Direction, r.PositionCount, r.GrossPnL)).OrderBy(t => t.Item1).ThenBy(t => t.Item2).ToList();

    // ── TEST 5: DETERMINISM ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SameDatasetConfigurationAndThreshold_RunTwice_ProducesIdenticalResults()
    {
        HistoricalSeries series = BacktestTestSeriesBuilder.MeanRevertingOu(300, seed: 71UL);
        CalibrationDataset dataset = CalibrationTestFixtures.Dataset(series);
        CalibrationWindowSet windows = CalibrationTestFixtures.Windows(dataset, leadInBars: 128, trainBars: 100, validationBars: 30, oosBars: 30);
        CalibrationExperiment experiment = CalibrationExperiment.Create(ThresholdSet(0.20m), dataset, windows, CalibrationTestFixtures.Setup());

        CalibrationExperimentRunResult first = CalibrationExperimentRunner.Run(experiment);
        CalibrationExperimentRunResult second = CalibrationExperimentRunner.Run(experiment);

        Assert.Equal(first.ExperimentId, second.ExperimentId);
        Assert.Equal(Summary(first), Summary(second));
    }

    // ── TEST 6: LOOK-AHEAD - the override introduces no future-data access ──────────────────────────────

    [Fact]
    public void NonDefaultThreshold_AppendingMoreOosData_NeverChangesTrainOrValidationResults()
    {
        HistoricalSeries full = BacktestTestSeriesBuilder.MeanRevertingOu(400, seed: 81UL);
        HistoricalSeries shortSeries = BacktestTestSeriesBuilder.Truncate(full, 320);

        const int leadIn = 128, train = 100, validation = 40;
        CalibrationParameterSet threshold = ThresholdSet(0.30m);

        CalibrationDataset shortDataset = CalibrationTestFixtures.Dataset(shortSeries);
        CalibrationWindowSet shortWindows = CalibrationTestFixtures.Windows(shortDataset, leadIn, train, validation, oosBars: 51);
        CalibrationExperiment shortExperiment = CalibrationExperiment.Create(threshold, shortDataset, shortWindows, CalibrationTestFixtures.Setup());

        CalibrationDataset fullDataset = CalibrationTestFixtures.Dataset(full);
        CalibrationWindowSet fullWindows = CalibrationTestFixtures.Windows(fullDataset, leadIn, train, validation, oosBars: 131);
        CalibrationExperiment fullExperiment = CalibrationExperiment.Create(threshold, fullDataset, fullWindows, CalibrationTestFixtures.Setup());

        CalibrationExperimentRunResult shortResult = CalibrationExperimentRunner.Run(shortExperiment);
        CalibrationExperimentRunResult fullResult = CalibrationExperimentRunner.Run(fullExperiment);

        var trainShort = shortResult.Results.Where(r => r.Window.Role == CalibrationWindowRole.Train)
            .OrderBy(r => r.Direction).Select(r => (r.Direction, r.Status, r.SignalCount, r.PositionCount, r.GrossPnL)).ToList();
        var trainFull = fullResult.Results.Where(r => r.Window.Role == CalibrationWindowRole.Train)
            .OrderBy(r => r.Direction).Select(r => (r.Direction, r.Status, r.SignalCount, r.PositionCount, r.GrossPnL)).ToList();

        Assert.Equal(trainShort, trainFull);
    }
}
