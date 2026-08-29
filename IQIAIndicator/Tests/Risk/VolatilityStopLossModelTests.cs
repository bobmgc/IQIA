using System;
using System.Collections.Generic;
using IQIAIndicator.Engine.Entry;
using IQIAIndicator.Engine.EntryTrigger;
using IQIAIndicator.Engine.Risk;
using IQIAIndicator.Engine.ScientificFusion;
using IQIAIndicator.Engine.ScientificModels.Abstractions;

namespace IQIAIndicator.Tests.RiskTests;

/// <summary>
/// Sprint 15.25 (Lot 15.3). Unit-level coverage of <see cref="VolatilityStopLossModel"/> - the first real
/// implementation of <see cref="IStopLossStrategy"/> (see the class's own doc comment). Built directly
/// against synthetic <see cref="EntryTriggerCandidate"/> fixtures, the same approach
/// <c>DecisionDirectionCoherenceTests.Trigger</c>/<c>BuildContext</c> already uses for
/// <see cref="EntryTriggerBuilder"/> - here the fixture is constructed BY HAND (bypassing
/// EntryTriggerBuilder entirely) so every test controls Direction/CurrentPrice/CurrentVolatility
/// independently and deterministically. Test lettering (Case A-J) mirrors the Lot 15.3 task brief §17.
///
/// NON-CALIBRATION DISCIPLINE (brief "RÈGLE ABSOLUE"): every numeric fixture below is chosen only to
/// exercise a specific code path (direction, rounding, degeneracy, NaN, tick alignment) - never selected
/// or tuned against PnL/win-rate/Sharpe/expectancy. DefaultVolatilityMultiplier (2.0) is exercised
/// unmodified in most cases; Case H uses an explicit non-default multiplier only to make the tick-rounding
/// direction unambiguous, not as a calibration choice.
/// </summary>
public static class VolatilityStopLossModelTests
{
    public static void RunAll()
    {
        // Case A-D: direction and volatility-magnitude shape.
        CaseA_Buy_NormalVolatility_StopBelowEntry_CorrectDistance();
        CaseB_Sell_NormalVolatility_StopAboveEntry();
        CaseC_LowVolatility_SmallerDistance_StillValid();
        CaseD_HighVolatility_LargerDistance_StillValid();

        // Case E: zero volatility - both entry points.
        CaseE_ZeroVolatility_ConstructorThrows();
        CaseE_ZeroVolatility_TryResolveStopPrice_ReturnsNull();

        // Case F: NaN volatility (raw double, pre-decimal-conversion).
        CaseF_NaNVolatility_TryResolveStopPrice_ReturnsNull_NeverThrows();

        // Case G: EntryPrice <= 0.
        CaseG_ZeroEntryPrice_ReturnsNull();
        CaseG_NegativeEntryPrice_ReturnsNull();

        // Case H: tick rounding - alignment + direction (toward entry).
        CaseH_TickAlignment_And_RoundingDirection_Buy();
        CaseH_TickAlignment_And_RoundingDirection_Sell();

        // Case I: no minimum-stop-distance concept - documents the gap, does not invent a rule.
        CaseI_ExtremelySmallVolatility_ProducesTinyButPositiveDistance_NeverRejectedAsTooClose();

        // Additional invalid-input coverage (brief §9: never fabricate for degenerate/missing inputs).
        NonDirectional_NoAction_ReturnsNull();
        NonDirectional_Watch_ReturnsNull();
        MissingVolatilityModelResult_ReturnsNull();
        UnsuccessfulVolatilityModelResult_ReturnsNull();
        VolatilityMetricWrongType_ReturnsNull();
        VolatilityMetricNegative_ReturnsNull();

        // Determinism (brief §6): same inputs -> bit-identical outputs. No mutable state anywhere in the
        // model (readonly fields set once in the constructor; TryResolveStopPrice is a pure static method).
        Determinism_Resolve_SameInputsTwice_IdenticalOutput();
        Determinism_TryResolveStopPrice_SameInputsTwice_IdenticalOutput();

        // Property tests (brief §18): a manual sweep of representative (direction, entryPrice,
        // currentVolatility, tickSize, multiplier) combinations, proving the invariants hold for every
        // non-null result - this codebase's convention for "property"-style tests (no FsCheck dependency),
        // matching the parameter-grid-with-per-invariant-Assert shape already used under Tests/GoldenDatasets/.
        PropertySweep_AllNonNullResults_SatisfyCoreInvariants();

        NullCandidate_ThrowsArgumentNullException();
    }

    // ── Case A-D ─────────────────────────────────────────────────────────────────────────────────

    private static void CaseA_Buy_NormalVolatility_StopBelowEntry_CorrectDistance()
    {
        // volatility=2.0, multiplier=2.0(default) -> rawDistance=4.0, rawStop=96.0, already tick-aligned.
        EntryTriggerCandidate candidate = Candidate(DirectionCandidate.BUY_CANDIDATE, entryPrice: 100m, currentVolatility: 2.0);
        decimal? stop = VolatilityStopLossModel.TryResolveStopPrice(candidate, tickSize: 0.25m);

        Assert(stop is not null, "BUY with normal volatility must resolve a stop.");
        Assert(stop!.Value < 100m, $"BUY StopLoss must be below EntryPrice. Actual={stop}.");
        Assert(stop.Value == 96m, $"BUY StopLoss must be Entry(100) - Volatility(2.0)*Multiplier(2.0) = 96, already tick-aligned. Actual={stop}.");
    }

    private static void CaseB_Sell_NormalVolatility_StopAboveEntry()
    {
        EntryTriggerCandidate candidate = Candidate(DirectionCandidate.SELL_CANDIDATE, entryPrice: 100m, currentVolatility: 2.0);
        decimal? stop = VolatilityStopLossModel.TryResolveStopPrice(candidate, tickSize: 0.25m);

        Assert(stop is not null, "SELL with normal volatility must resolve a stop.");
        Assert(stop!.Value > 100m, $"SELL StopLoss must be above EntryPrice. Actual={stop}.");
        Assert(stop.Value == 104m, $"SELL StopLoss must be Entry(100) + Volatility(2.0)*Multiplier(2.0) = 104, already tick-aligned. Actual={stop}.");
    }

    private static void CaseC_LowVolatility_SmallerDistance_StillValid()
    {
        // volatility=0.5 -> rawDistance=1.0 (vs Case A's 4.0) - smaller, still strictly positive/valid.
        EntryTriggerCandidate candidate = Candidate(DirectionCandidate.BUY_CANDIDATE, entryPrice: 100m, currentVolatility: 0.5);
        decimal? stop = VolatilityStopLossModel.TryResolveStopPrice(candidate, tickSize: 0.25m);

        Assert(stop is not null, "Low (but positive) volatility must still resolve a stop.");
        decimal distance = 100m - stop!.Value;
        Assert(distance == 1.0m, $"Distance must be Volatility(0.5)*Multiplier(2.0)=1.0. Actual={distance}.");
        Assert(distance < 4.0m, "Low-volatility distance must be strictly smaller than Case A's normal-volatility distance (4.0).");
    }

    private static void CaseD_HighVolatility_LargerDistance_StillValid()
    {
        // volatility=10.0 -> rawDistance=20.0 (vs Case A's 4.0) - larger, still valid.
        EntryTriggerCandidate candidate = Candidate(DirectionCandidate.BUY_CANDIDATE, entryPrice: 100m, currentVolatility: 10.0);
        decimal? stop = VolatilityStopLossModel.TryResolveStopPrice(candidate, tickSize: 0.25m);

        Assert(stop is not null, "High volatility must still resolve a stop.");
        decimal distance = 100m - stop!.Value;
        Assert(distance == 20.0m, $"Distance must be Volatility(10.0)*Multiplier(2.0)=20.0. Actual={distance}.");
        Assert(distance > 4.0m, "High-volatility distance must be strictly larger than Case A's normal-volatility distance (4.0).");
    }

    // ── Case E ───────────────────────────────────────────────────────────────────────────────────

    private static void CaseE_ZeroVolatility_ConstructorThrows()
    {
        bool threw = false;
        try
        {
            _ = new VolatilityStopLossModel(currentVolatility: 0m, tickSize: 0.25m);
        }
        catch (ArgumentOutOfRangeException)
        {
            threw = true;
        }

        Assert(threw, "The documented contract requires the constructor to throw ArgumentOutOfRangeException for CurrentVolatility <= 0.");
    }

    private static void CaseE_ZeroVolatility_TryResolveStopPrice_ReturnsNull()
    {
        // The underlying VolatilityModel metric is 0.0 - TryGetCurrentVolatility must reject it (rawVolatility
        // <= 0.0) BEFORE the throwing constructor is ever reached, so TryResolveStopPrice never throws here.
        EntryTriggerCandidate candidate = Candidate(DirectionCandidate.BUY_CANDIDATE, entryPrice: 100m, currentVolatility: 0.0);
        decimal? stop = VolatilityStopLossModel.TryResolveStopPrice(candidate, tickSize: 0.25m);
        Assert(stop is null, $"Zero CurrentVolatility must resolve to null (never reaching the throwing constructor). Actual={stop}.");
    }

    // ── Case F ───────────────────────────────────────────────────────────────────────────────────

    private static void CaseF_NaNVolatility_TryResolveStopPrice_ReturnsNull_NeverThrows()
    {
        EntryTriggerCandidate candidate = Candidate(DirectionCandidate.BUY_CANDIDATE, entryPrice: 100m, currentVolatility: double.NaN);
        decimal? stop = VolatilityStopLossModel.TryResolveStopPrice(candidate, tickSize: 0.25m);
        Assert(stop is null, $"NaN CurrentVolatility must resolve to null. Actual={stop}.");

        EntryTriggerCandidate positiveInf = Candidate(DirectionCandidate.SELL_CANDIDATE, entryPrice: 100m, currentVolatility: double.PositiveInfinity);
        Assert(VolatilityStopLossModel.TryResolveStopPrice(positiveInf, tickSize: 0.25m) is null, "PositiveInfinity CurrentVolatility must resolve to null.");

        EntryTriggerCandidate negativeInf = Candidate(DirectionCandidate.SELL_CANDIDATE, entryPrice: 100m, currentVolatility: double.NegativeInfinity);
        Assert(VolatilityStopLossModel.TryResolveStopPrice(negativeInf, tickSize: 0.25m) is null, "NegativeInfinity CurrentVolatility must resolve to null.");
    }

    // ── Case G ───────────────────────────────────────────────────────────────────────────────────

    private static void CaseG_ZeroEntryPrice_ReturnsNull()
    {
        EntryTriggerCandidate candidate = Candidate(DirectionCandidate.BUY_CANDIDATE, entryPrice: 0m, currentVolatility: 2.0);
        Assert(VolatilityStopLossModel.TryResolveStopPrice(candidate, tickSize: 0.25m) is null, "EntryPrice==0 must resolve to null.");
    }

    private static void CaseG_NegativeEntryPrice_ReturnsNull()
    {
        EntryTriggerCandidate candidate = Candidate(DirectionCandidate.SELL_CANDIDATE, entryPrice: -50m, currentVolatility: 2.0);
        Assert(VolatilityStopLossModel.TryResolveStopPrice(candidate, tickSize: 0.25m) is null, "Negative EntryPrice must resolve to null.");
    }

    // ── Case H ───────────────────────────────────────────────────────────────────────────────────

    private static void CaseH_TickAlignment_And_RoundingDirection_Buy()
    {
        // currentVolatility=1.0, multiplier=1.3 (explicit, non-default - only to make the raw distance
        // land off a 0.25 tick boundary unambiguously) -> rawDistance=1.3, rawStop=100-1.3=98.7.
        // 98.7/0.25=394.8 -> Ceiling=395 -> 395*0.25=98.75 (toward entry: 98.75 > 98.7, distance SHRINKS
        // from 1.3 to 1.25). Rounding away from entry (Floor) would instead give 98.5 (distance 1.5) -
        // this test distinguishes the two unambiguously.
        EntryTriggerCandidate candidate = Candidate(DirectionCandidate.BUY_CANDIDATE, entryPrice: 100m, currentVolatility: 1.0);
        decimal? stop = VolatilityStopLossModel.TryResolveStopPrice(candidate, tickSize: 0.25m, multiplier: 1.3);

        Assert(stop is not null, "Case H BUY must resolve a stop.");
        Assert(stop!.Value == 98.75m, $"BUY must round the raw stop (98.7) UP toward Entry via Ceiling to the tick boundary 98.75, not away from it (98.5). Actual={stop}.");
        Assert(stop.Value % 0.25m == 0m, $"StopLoss must be exactly tick-aligned. Actual={stop} % 0.25 = {stop.Value % 0.25m}.");
        decimal distance = 100m - stop.Value;
        Assert(distance == 1.25m && distance < 1.3m, $"Rounding toward Entry must SHRINK the distance below the raw pre-rounding value (1.3). Actual distance={distance}.");
    }

    private static void CaseH_TickAlignment_And_RoundingDirection_Sell()
    {
        // Mirror of the BUY case: rawStop=100+1.3=101.3. 101.3/0.25=405.2 -> Floor=405 -> 101.25
        // (toward entry: 101.25 < 101.3, distance shrinks from 1.3 to 1.25). Away-from-entry (Ceiling)
        // would instead give 101.5 (distance 1.5).
        EntryTriggerCandidate candidate = Candidate(DirectionCandidate.SELL_CANDIDATE, entryPrice: 100m, currentVolatility: 1.0);
        decimal? stop = VolatilityStopLossModel.TryResolveStopPrice(candidate, tickSize: 0.25m, multiplier: 1.3);

        Assert(stop is not null, "Case H SELL must resolve a stop.");
        Assert(stop!.Value == 101.25m, $"SELL must round the raw stop (101.3) DOWN toward Entry via Floor to the tick boundary 101.25, not away from it (101.5). Actual={stop}.");
        Assert(stop.Value % 0.25m == 0m, $"StopLoss must be exactly tick-aligned. Actual={stop} % 0.25 = {stop.Value % 0.25m}.");
        decimal distance = stop.Value - 100m;
        Assert(distance == 1.25m && distance < 1.3m, $"Rounding toward Entry must SHRINK the distance below the raw pre-rounding value (1.3). Actual distance={distance}.");
    }

    // ── Case I ───────────────────────────────────────────────────────────────────────────────────

    private static void CaseI_ExtremelySmallVolatility_ProducesTinyButPositiveDistance_NeverRejectedAsTooClose()
    {
        // Documents a real, confirmed gap (RiskPolicy.cs / InstrumentRiskSpecification.cs have no minimum-
        // stop-distance field anywhere): a stop only ONE TICK away from Entry is accepted like any other,
        // never rejected for being "too close". volatility=0.13, multiplier=2.0(default) -> rawDistance=0.26,
        // rawStop=99.74 -> 99.74/0.25=398.96 -> Ceiling=399 -> 99.75: the smallest possible nonzero
        // distance for this tick size (exactly one tick).
        EntryTriggerCandidate candidate = Candidate(DirectionCandidate.BUY_CANDIDATE, entryPrice: 100m, currentVolatility: 0.13);
        decimal? stop = VolatilityStopLossModel.TryResolveStopPrice(candidate, tickSize: 0.25m);

        Assert(stop is not null, "An extremely small (but positive) volatility must NOT be rejected for producing a too-close stop - no such rule exists in this architecture.");
        decimal distance = 100m - stop!.Value;
        Assert(distance == 0.25m, $"Distance must be exactly one tick (0.25) - the smallest representable positive distance at this tick size. Actual={distance}.");
        Assert(distance > 0m, "Distance must still be strictly positive - only exactly-zero (or wrong-side) distances are ever rejected (Resolve's own degeneracy check), never 'too small'.");
    }

    // ── Additional invalid-input / never-fabricate coverage ─────────────────────────────────────────

    private static void NonDirectional_NoAction_ReturnsNull()
    {
        EntryTriggerCandidate candidate = Candidate(DirectionCandidate.NO_ACTION, entryPrice: 100m, currentVolatility: 2.0);
        Assert(VolatilityStopLossModel.TryResolveStopPrice(candidate, tickSize: 0.25m) is null, "NO_ACTION must never produce a stop.");
    }

    private static void NonDirectional_Watch_ReturnsNull()
    {
        EntryTriggerCandidate candidate = Candidate(DirectionCandidate.WATCH, entryPrice: 100m, currentVolatility: 2.0);
        Assert(VolatilityStopLossModel.TryResolveStopPrice(candidate, tickSize: 0.25m) is null, "WATCH must never produce a stop.");
    }

    private static void MissingVolatilityModelResult_ReturnsNull()
    {
        EntryTriggerCandidate candidate = CandidateWithScientificResults(DirectionCandidate.BUY_CANDIDATE, entryPrice: 100m, results: Array.Empty<ScientificModelResult>());
        Assert(VolatilityStopLossModel.TryResolveStopPrice(candidate, tickSize: 0.25m) is null, "No VolatilityModel result present must resolve to null, never fabricate a value.");
    }

    private static void UnsuccessfulVolatilityModelResult_ReturnsNull()
    {
        var metrics = new Dictionary<string, object> { [ScientificMetricKeys.CurrentVolatility] = 2.0 };
        var results = new[] { new ScientificModelResult("VolatilityModel", Success: false, Score: 0.0, Explanation: "failed", Metrics: metrics) };
        EntryTriggerCandidate candidate = CandidateWithScientificResults(DirectionCandidate.BUY_CANDIDATE, entryPrice: 100m, results: results);
        Assert(VolatilityStopLossModel.TryResolveStopPrice(candidate, tickSize: 0.25m) is null, "Success=false VolatilityModel result must resolve to null.");
    }

    private static void VolatilityMetricWrongType_ReturnsNull()
    {
        var metrics = new Dictionary<string, object> { [ScientificMetricKeys.CurrentVolatility] = "not-a-double" };
        var results = new[] { new ScientificModelResult("VolatilityModel", Success: true, Score: 1.0, Explanation: "ok", Metrics: metrics) };
        EntryTriggerCandidate candidate = CandidateWithScientificResults(DirectionCandidate.BUY_CANDIDATE, entryPrice: 100m, results: results);
        Assert(VolatilityStopLossModel.TryResolveStopPrice(candidate, tickSize: 0.25m) is null, "A non-double CurrentVolatility metric value must resolve to null, never an invalid cast.");
    }

    private static void VolatilityMetricNegative_ReturnsNull()
    {
        EntryTriggerCandidate candidate = Candidate(DirectionCandidate.BUY_CANDIDATE, entryPrice: 100m, currentVolatility: -3.0);
        Assert(VolatilityStopLossModel.TryResolveStopPrice(candidate, tickSize: 0.25m) is null, "Negative CurrentVolatility must resolve to null.");
    }

    private static void NullCandidate_ThrowsArgumentNullException()
    {
        bool threw = false;
        try
        {
            VolatilityStopLossModel.TryResolveStopPrice(null!, tickSize: 0.25m);
        }
        catch (ArgumentNullException)
        {
            threw = true;
        }
        Assert(threw, "TryResolveStopPrice must throw ArgumentNullException for a null candidate (fail loudly on a programmer error, not silently return null).");
    }

    // ── Determinism ──────────────────────────────────────────────────────────────────────────────

    private static void Determinism_Resolve_SameInputsTwice_IdenticalOutput()
    {
        var model = new VolatilityStopLossModel(currentVolatility: 3.7m, tickSize: 0.25m, multiplier: 2.5);
        decimal? first = model.Resolve(TradeDirection.Buy, 4523.75m);
        decimal? second = model.Resolve(TradeDirection.Buy, 4523.75m);
        Assert(first == second, $"Same (direction, entryPrice) on the same model instance must return bit-identical results. First={first}, Second={second}.");

        // A brand-new instance with the exact same constructor inputs must also agree - no mutable/shared
        // state anywhere (every field is readonly, set once in the constructor).
        var modelAgain = new VolatilityStopLossModel(currentVolatility: 3.7m, tickSize: 0.25m, multiplier: 2.5);
        decimal? third = modelAgain.Resolve(TradeDirection.Buy, 4523.75m);
        Assert(first == third, $"A fresh instance with identical constructor inputs must produce the identical result. First={first}, Third={third}.");
    }

    private static void Determinism_TryResolveStopPrice_SameInputsTwice_IdenticalOutput()
    {
        EntryTriggerCandidate candidate = Candidate(DirectionCandidate.SELL_CANDIDATE, entryPrice: 4523.75m, currentVolatility: 6.3);
        decimal? first = VolatilityStopLossModel.TryResolveStopPrice(candidate, tickSize: 0.25m);
        decimal? second = VolatilityStopLossModel.TryResolveStopPrice(candidate, tickSize: 0.25m);
        Assert(first == second, $"Repeated calls with the identical candidate/tickSize must be bit-identical. First={first}, Second={second}.");
    }

    // ── Property sweep (brief §18) ───────────────────────────────────────────────────────────────

    private static void PropertySweep_AllNonNullResults_SatisfyCoreInvariants()
    {
        DirectionCandidate[] directions = { DirectionCandidate.BUY_CANDIDATE, DirectionCandidate.SELL_CANDIDATE };
        decimal[] entryPrices = { 1m, 50m, 100m, 4523.75m, 19999.75m };
        double[] volatilities = { 0.01, 0.13, 0.5, 1.0, 2.0, 7.25, 50.0, 1234.5 };
        decimal[] tickSizes = { 0.01m, 0.25m, 1m, 5m };
        double[] multipliers = { 0.5, 1.0, VolatilityStopLossModel.DefaultVolatilityMultiplier, 3.0, 10.0 };

        int evaluated = 0, nonNull = 0;

        foreach (DirectionCandidate direction in directions)
        foreach (decimal entryPrice in entryPrices)
        foreach (double volatility in volatilities)
        foreach (decimal tickSize in tickSizes)
        foreach (double multiplier in multipliers)
        {
            evaluated++;
            EntryTriggerCandidate candidate = Candidate(direction, entryPrice, volatility);
            decimal? stop = VolatilityStopLossModel.TryResolveStopPrice(candidate, tickSize, multiplier);

            if (stop is null)
                continue; // A degenerate (zero-or-less post-rounding distance) combination is a legitimate null - never forced.

            nonNull++;
            decimal stopValue = stop.Value;

            if (direction == DirectionCandidate.BUY_CANDIDATE)
                Assert(stopValue < entryPrice, $"[BUY dir={direction} entry={entryPrice} vol={volatility} tick={tickSize} mult={multiplier}] StopPrice must be < EntryPrice. Actual={stopValue}.");
            else
                Assert(stopValue > entryPrice, $"[SELL dir={direction} entry={entryPrice} vol={volatility} tick={tickSize} mult={multiplier}] StopPrice must be > EntryPrice. Actual={stopValue}.");

            decimal distance = direction == DirectionCandidate.BUY_CANDIDATE ? entryPrice - stopValue : stopValue - entryPrice;
            Assert(distance > 0m, $"[entry={entryPrice} vol={volatility} tick={tickSize} mult={multiplier}] StopDistance must always be > 0. Actual={distance}.");

            Assert(stopValue % tickSize == 0m, $"[entry={entryPrice} vol={volatility} tick={tickSize} mult={multiplier}] StopPrice must be exactly tick-aligned. Actual={stopValue} % {tickSize} = {stopValue % tickSize}.");

            // Same input -> same output.
            decimal? repeat = VolatilityStopLossModel.TryResolveStopPrice(candidate, tickSize, multiplier);
            Assert(stop == repeat, $"[entry={entryPrice} vol={volatility} tick={tickSize} mult={multiplier}] Repeated call with identical inputs must be bit-identical. First={stop}, Second={repeat}.");
        }

        Assert(evaluated == directions.Length * entryPrices.Length * volatilities.Length * tickSizes.Length * multipliers.Length,
            $"Sweep must evaluate every grid combination exactly once. Actual={evaluated}.");
        Assert(nonNull > 0, "The sweep must produce at least one non-null result, or the invariants above would be vacuously true.");
    }

    // ── Fixtures ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>Builds an EntryTriggerCandidate by hand (bypassing EntryTriggerBuilder entirely) with a
    /// single successful "VolatilityModel" ScientificModelResult carrying the given CurrentVolatility raw
    /// double metric - exactly the shape VolatilityStopLossModel.TryGetCurrentVolatility reads.</summary>
    private static EntryTriggerCandidate Candidate(DirectionCandidate direction, decimal entryPrice, double currentVolatility)
    {
        var metrics = new Dictionary<string, object> { [ScientificMetricKeys.CurrentVolatility] = currentVolatility };
        var results = new[] { new ScientificModelResult("VolatilityModel", Success: true, Score: 1.0, Explanation: "test fixture", Metrics: metrics) };
        return CandidateWithScientificResults(direction, entryPrice, results);
    }

    private static EntryTriggerCandidate CandidateWithScientificResults(
        DirectionCandidate direction, decimal entryPrice, IReadOnlyList<ScientificModelResult> results)
    {
        var scientificAssessment = new ScientificAssessment(
            OverallConfidence: 0.8,
            EvidenceAgreement: Array.Empty<string>(),
            EvidenceConflict: Array.Empty<string>(),
            MissingEvidence: Array.Empty<string>(),
            ExecutedModels: Array.Empty<string>(),
            SuccessfulModels: Array.Empty<string>(),
            FailedModels: Array.Empty<string>(),
            ScientificResults: results,
            Diagnostics: "VolatilityStopLossModelTests fixture");

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

        var triggerAssessment = new EntryTriggerAssessment(
            TriggerStatus: EntryTriggerStatus.READY,
            Direction: direction,
            ScientificConfidence: 0.8,
            OpportunityPriority: 0.8,
            Reason: EntryTriggerReason.READY,
            EstimatedEquilibrium: null,
            DistanceToEquilibrium: null,
            Timestamp: DateTime.UtcNow);

        return new EntryTriggerCandidate(
            triggerAssessment, entryCandidate, entryPrice, Array.Empty<string>(), Array.Empty<string>(), DateTime.UtcNow);
    }

    // ── Helper ───────────────────────────────────────────────────────────────────────────────────

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
