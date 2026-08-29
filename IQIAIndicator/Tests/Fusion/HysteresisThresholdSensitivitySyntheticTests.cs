using IQIAIndicator.Engine.Fusion.Core;
using IQIAIndicator.Engine.Fusion.State;
using IQIAIndicator.Tests.BacktestTests.Calibration.HysteresisSensitivity;

namespace IQIAIndicator.Tests.Fusion;

/// <summary>
/// Sprint 15.25 (Lot 14.18, brief §25/§27). Synthetic Cases A-E for the hysteresis threshold sensitivity
/// study, plus the mandatory threshold-binding, determinism, run-isolation and look-ahead tests (brief
/// §27). Every case runs through <see cref="PersistenceHysteresisReplica"/> - the one test-only
/// exception is <see cref="ThresholdBinding_ReplicaMatchesRealFusionStateManager_AtProductionValues"/>,
/// which cross-checks the replica against the REAL <see cref="FusionStateManager"/> to prove fidelity
/// (brief §26/§29: no production file is modified anywhere in this Lot).
/// </summary>
public static class HysteresisThresholdSensitivitySyntheticTests
{
    public static void RunAll()
    {
        CaseA_LowVariationRawStaysFrozenAfterFirstBar();
        CaseB_HighVariationRawUpdatesEveryBar();
        CaseC_AlternatingAroundThreshold_BoundaryIsInclusive();
        CaseD_RawSystematicallyExceedsThreshold_AlwaysUpdates();
        CaseE_LongLowVariationSequence_ProducesOneLongFrozenRun();
        ThresholdBinding_ReplicaMatchesRealFusionStateManager_AtProductionValues();
        ThresholdBinding_DifferentThresholdsProduceDifferentStableSequences();
        Determinism_SameThresholdSameInput_ProducesBitIdenticalOutputTwice();
        RunIsolation_InterleavedInstancesDoNotCrossContaminate();
        LookAhead_PrefixOutputIsUnaffectedByFutureContinuation();
        FrozenRunDefinition_UsesExactBitIdenticalComparison_NoEpsilon();
    }

    // ══════════════════════ §25 SYNTHETIC CASES A-E ══════════════════════

    // CASE A: raw varies very little (steps of 0.001, far below every grid threshold except the 0.00
    // boundary case) - the stable value must freeze at the first bar's value for the rest of the run.
    private static void CaseA_LowVariationRawStaysFrozenAfterFirstBar()
    {
        double[] raw = { 0.500, 0.501, 0.502, 0.501, 0.500, 0.501, 0.502 };
        double[] stable = Run(raw, threshold: 0.03);

        Assert(stable.Distinct().Count() == 1,
            $"Case A: raw movements of 0.001/step must never cross the 0.03 threshold - expected exactly 1 distinct stable value, observed {stable.Distinct().Count()} ([{string.Join(", ", stable.Select(v => v.ToString("F6")))}]).");
    }

    // CASE B: raw varies a lot - every step far exceeds the threshold even after EMA damping (alpha=0.20),
    // so the stable value must move on every single bar after the first.
    private static void CaseB_HighVariationRawUpdatesEveryBar()
    {
        double[] raw = { 0.10, 0.90, 0.10, 0.90, 0.10, 0.90 };
        double[] stable = Run(raw, threshold: 0.03);

        for (int i = 1; i < stable.Length; i++)
            Assert(stable[i] != stable[i - 1],
                $"Case B: large alternating raw swings (0.10<->0.90) must update the stable value every bar - bar {i} was frozen at {stable[i]:F6}.");
    }

    // CASE C: raw drifts by exactly the smoothed-delta that lands on the threshold boundary. The
    // production comparison is `>=` (FusionStateManager.cs:117/118), so a delta exactly equal to the
    // threshold must count as CHANGED, not frozen - and one ULP above it must freeze. The threshold used
    // is the ACTUAL computed smoothed delta (not a hand-picked decimal literal, which could miss the
    // IEEE-754 boundary by rounding), so this test exercises the real bit-exact edge.
    private static void CaseC_AlternatingAroundThreshold_BoundaryIsInclusive()
    {
        const double alpha = 0.20;
        const double previousStable = 0.50;
        const double rawStep = 0.65;
        double smoothedValue = alpha * rawStep + (1.0 - alpha) * previousStable;
        double exactDelta = smoothedValue - previousStable;
        double[] rawSequence = { previousStable, rawStep };

        double[] atExactBoundary = Run(rawSequence, threshold: exactDelta, alpha);
        Assert(atExactBoundary[1] != atExactBoundary[0],
            $"Case C: a smoothed delta EXACTLY equal to the threshold (delta={exactDelta:F17}) must count as an update, matching production's `>=` comparison (FusionStateManager.cs:117/118), not `>`.");

        double justAboveDelta = Math.BitIncrement(exactDelta);
        double[] justAboveBoundary = Run(rawSequence, threshold: justAboveDelta, alpha);
        Assert(justAboveBoundary[1] == justAboveBoundary[0],
            $"Case C: a threshold one ULP above the smoothed delta ({justAboveDelta:F17} vs delta={exactDelta:F17}) must freeze - confirms the `>=` boundary is exact, not accidentally satisfied by a looser comparison.");
    }

    // CASE D: every raw step is chosen so the resulting smoothed delta systematically clears the threshold
    // by a wide margin - stable must track raw closely (never more than one bar behind reaching each
    // plateau's EMA limit), i.e. always updates.
    private static void CaseD_RawSystematicallyExceedsThreshold_AlwaysUpdates()
    {
        double[] raw = { 0.05, 0.95, 0.05, 0.95, 0.05, 0.95, 0.05 };
        double[] stable = Run(raw, threshold: 0.03);

        int updates = 0;
        for (int i = 1; i < stable.Length; i++)
            if (stable[i] != stable[i - 1]) updates++;
        Assert(updates == stable.Length - 1,
            $"Case D: every step from a systematically-exceeding raw sequence must update the stable value - expected {stable.Length - 1} updates, observed {updates}.");
    }

    // CASE E: a long sequence (500 bars) of low-amplitude raw variation around a fixed midpoint - verifies
    // the mechanism produces one correspondingly long frozen run, i.e. the mechanism observed on the real
    // 926-bar run (Lot 14.17 §14) is reproducible on demand with the real recursion, not a data artifact.
    private static void CaseE_LongLowVariationSequence_ProducesOneLongFrozenRun()
    {
        var random = new Random(Seed: 1418);
        double[] raw = new double[500];
        double midpoint = 0.40;
        for (int i = 0; i < raw.Length; i++)
            raw[i] = midpoint + (random.NextDouble() - 0.5) * 0.02; // +/-0.01 around midpoint

        double[] stable = Run(raw, threshold: 0.03);
        int longestRun = LongestFrozenRun(stable);

        Assert(longestRun >= 400,
            $"Case E: 500 bars of raw variation confined to +/-0.01 (well under the 0.03 threshold, even after EMA damping) must produce one dominant frozen run spanning most of the sequence - observed longest run={longestRun}.");
    }

    // ══════════════════════ §27 THRESHOLD BINDING ══════════════════════

    // Proves the replica is a faithful stand-in for the REAL FusionStateManager at production
    // Alpha=0.20/HysteresisThreshold=0.03 - the one place in this Lot where the real production class is
    // instantiated, purely to VALIDATE the replica, never to reconfigure or modify it (brief §26/§29).
    private static void ThresholdBinding_ReplicaMatchesRealFusionStateManager_AtProductionValues()
    {
        double[] rawSequence =
        {
            0.140, 0.145, 0.150, 0.900, 0.120, 0.121, 0.122, 0.123, 0.400, 0.401,
            0.700, 0.010, 0.011, 0.500, 0.501, 0.502, 0.999, 0.001, 0.300, 0.305
        };

        var manager = new FusionStateManager();
        var replica = new PersistenceHysteresisReplica(hysteresisThreshold: 0.03, alpha: 0.20);

        for (int i = 0; i < rawSequence.Length; i++)
        {
            FusionSnapshot snapshot = manager.Update(BuildRawFusion(rawSequence[i]), DateTime.UnixEpoch.AddMinutes(5 * i));
            double realStable = snapshot.StableResult.Dimensions[FusionDimension.Persistence].Value;
            double replicaStable = replica.Update(rawSequence[i]);

            Assert(realStable == replicaStable,
                $"Threshold binding fidelity: bar {i} diverged between real FusionStateManager ({realStable:F17}) and PersistenceHysteresisReplica ({replicaStable:F17}) at production Alpha=0.20/Threshold=0.03 - the replica must be bit-identical to production before it can be trusted for the sensitivity sweep.");
        }
    }

    // Proves the threshold PARAMETER actually controls behaviour (not silently ignored/hardcoded) - a
    // raw movement whose smoothed delta sits strictly between two threshold candidates must produce
    // DIFFERENT stable outputs for the two replicas.
    private static void ThresholdBinding_DifferentThresholdsProduceDifferentStableSequences()
    {
        // smoothed delta after step 1->2 (alpha=0.20): |0.20*0.65+0.80*0.50 - 0.50| = 0.03 exactly (as in
        // Case C) - use a threshold just above (0.031, frozen) and just below (0.029, updates) 0.03.
        double[] raw = { 0.50, 0.65 };
        double belowStable = Run(raw, threshold: 0.029)[1];
        double aboveStable = Run(raw, threshold: 0.031)[1];

        Assert(belowStable != aboveStable,
            $"Threshold binding: a smoothed delta of exactly 0.03 must freeze under threshold=0.031 ({aboveStable:F6}) but update under threshold=0.029 ({belowStable:F6}) - observed them equal, meaning the threshold parameter is not actually being applied.");
    }

    // ══════════════════════ §21 DETERMINISM ══════════════════════

    private static void Determinism_SameThresholdSameInput_ProducesBitIdenticalOutputTwice()
    {
        double[] raw = MakePseudoRealisticSequence(seed: 7, length: 300);
        double[] run1 = Run(raw, threshold: 0.03);
        double[] run2 = Run(raw, threshold: 0.03);

        Assert(run1.Length == run2.Length && run1.SequenceEqual(run2),
            "Determinism: identical dataset + identical threshold must produce a bit-identical stable sequence on repeated runs.");
    }

    // ══════════════════════ §22 RUN ISOLATION ══════════════════════

    // A=0.01, B=0.03, C=0.075, B=0.03 (brief §22 exact pattern) - each threshold gets its own fresh
    // PersistenceHysteresisReplica instance (no static/shared state anywhere in the class), so
    // Result(B1) must equal Result(B2) regardless of what ran between them.
    private static void RunIsolation_InterleavedInstancesDoNotCrossContaminate()
    {
        double[] raw = MakePseudoRealisticSequence(seed: 22, length: 200);

        double[] resultA = Run(raw, threshold: 0.01);
        double[] resultB1 = Run(raw, threshold: 0.03);
        double[] resultC = Run(raw, threshold: 0.075);
        double[] resultB2 = Run(raw, threshold: 0.03);

        Assert(resultB1.SequenceEqual(resultB2),
            "Run isolation: Result(B1) (threshold=0.03, run first) must equal Result(B2) (threshold=0.03, run after A and C) - no configuration may contaminate another.");
        Assert(!resultA.SequenceEqual(resultB1), "Run isolation sanity: threshold=0.01 and threshold=0.03 must not coincidentally produce identical sequences on this input (sanity check that the test is not vacuous).");
        Assert(!resultC.SequenceEqual(resultB1), "Run isolation sanity: threshold=0.075 and threshold=0.03 must not coincidentally produce identical sequences on this input (sanity check that the test is not vacuous).");
    }

    // ══════════════════════ §24 LOOK-AHEAD ══════════════════════

    // Update(t) must depend only on raw[0..t], never on raw[t+1..]. Verified by replaying an identical
    // prefix with two DIFFERENT continuations and asserting the prefix's outputs are bit-identical
    // regardless of what comes after it - the recursion is causal by construction (only
    // _previousStableValue, a scalar carried forward, plus the current bar's raw value are read), but
    // this test demonstrates it directly rather than only by code inspection.
    private static void LookAhead_PrefixOutputIsUnaffectedByFutureContinuation()
    {
        double[] prefix = { 0.20, 0.203, 0.401, 0.404, 0.15 };
        double[] continuationX = { 0.99, 0.01, 0.50 };
        double[] continuationY = { 0.30, 0.31, 0.32 };

        double[] fullX = Run(prefix.Concat(continuationX).ToArray(), threshold: 0.03);
        double[] fullY = Run(prefix.Concat(continuationY).ToArray(), threshold: 0.03);

        for (int i = 0; i < prefix.Length; i++)
            Assert(fullX[i] == fullY[i],
                $"Look-ahead: bar {i} (within the shared prefix) diverged between the two continuations ({fullX[i]:F17} vs {fullY[i]:F17}) - the recursion must be causal, never influenced by future bars.");
    }

    // ══════════════════════ §6 FROZEN RUN DEFINITION ══════════════════════

    // Confirms that "frozen" is decided by EXACT bit-identical comparison (no epsilon), matching brief §6
    // ("ne pas introduire un epsilon arbitraire... utiliser la représentation réellement produite par le
    // pipeline") - the carry-forward in Update()/BuildStableResult is a literal reassignment
    // (`newStableValue = previousStableValue`), so two frozen bars share the exact same double bit
    // pattern, never merely "close".
    private static void FrozenRunDefinition_UsesExactBitIdenticalComparison_NoEpsilon()
    {
        double[] raw = { 0.500, 0.5001, 0.5002 }; // both movements far under threshold
        double[] stable = Run(raw, threshold: 0.03);

        Assert(BitConverter.DoubleToInt64Bits(stable[0]) == BitConverter.DoubleToInt64Bits(stable[1]) &&
               BitConverter.DoubleToInt64Bits(stable[1]) == BitConverter.DoubleToInt64Bits(stable[2]),
            "Frozen run definition: consecutive frozen stable values must be bit-identical (same IEEE-754 bit pattern), not merely within an epsilon.");
    }

    // ══════════════════════ helpers ══════════════════════

    private static double[] Run(double[] raw, double threshold, double alpha = 0.20)
    {
        var replica = new PersistenceHysteresisReplica(threshold, alpha);
        var stable = new double[raw.Length];
        for (int i = 0; i < raw.Length; i++)
            stable[i] = replica.Update(raw[i]);
        return stable;
    }

    private static int LongestFrozenRun(double[] stable)
    {
        int longest = 1, current = 1;
        for (int i = 1; i < stable.Length; i++)
        {
            current = stable[i] == stable[i - 1] ? current + 1 : 1;
            if (current > longest) longest = current;
        }
        return longest;
    }

    private static double[] MakePseudoRealisticSequence(int seed, int length)
    {
        var random = new Random(seed);
        var raw = new double[length];
        double value = 0.10;
        for (int i = 0; i < length; i++)
        {
            value = Math.Clamp(value + (random.NextDouble() - 0.5) * 0.05, 0.0, 1.0);
            raw[i] = value;
        }
        return raw;
    }

    private static FusionResult BuildRawFusion(double persistenceValue)
    {
        var builder = new FusionResultBuilder();
        builder.Dimensions[FusionDimension.Stationarity] = new FusionConfidence { Value = 0.40, Confidence = 0.80 };
        builder.Dimensions[FusionDimension.Persistence] = new FusionConfidence { Value = persistenceValue, Confidence = 0.80 };
        builder.Dimensions[FusionDimension.MeanReversion] = new FusionConfidence { Value = 0.55, Confidence = 0.80 };
        builder.Dimensions[FusionDimension.RandomWalk] = new FusionConfidence { Value = 0.20, Confidence = 0.80 };
        return builder.Build();
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
