============================================================
QDE-012 — SPRINT 15.19 — FIX ATAS FORMING-BAR CAPTURE
SEMANTICS
============================================================

Date: 2026-08-14
Protocol: QDE-012 v1.1 (locked), unchanged by this sprint.
Scope: DATA-COLLECTION INFRASTRUCTURE CORRECTION ONLY. No calibration, no k selection, no Risk
Engine, no scientific-model change, no trading-logic change. This sprint's sole objective:
**make scientific data collection bar-close safe.**

============================================================
1. PROBLEM
============================================================

`ScientificDatasetCollector`'s pre-15.19 dedup policy used identity `(SessionId, Symbol, TimeFrame,
CurrentBar)` with **first `Add()` wins**: the first callback observed for a given bar was committed
immediately; every later callback for the same bar was rejected as a "duplicate." ATAS can invoke
`OnCalculate` many times while a bar is still forming (tick by tick), so "first callback" is often the
bar's *least* complete state - not its final, closed one. Sprint 15.18 proved this is not theoretical:
a real ATAS capture's exported dataset contained a contiguous 633-bar (36.5%) tail of single-tick,
`Volume=1`, zero-range "bars" - snapshots of candles that had not yet closed.

============================================================
2. EVIDENCE FROM SPRINT 15.18 (established facts, not re-interpreted)
============================================================

Taken as given, per this sprint's brief:

- **Session 165421** (primary capture, SessionId `967a640e-...`): 1732 written bars; a contiguous
  633-bar tail (indices 1099-1731, 2026-07-06T01:35:00 → end of capture) where every bar has
  `Open=High=Low=Close` and median `Volume=1` - the signature of a single captured print, not a
  closed 5-minute candle.
- **Session 165335** (SessionId `20c7b9c8-...`): 71 `OnCalculate` calls, **all for the same
  `CurrentBar=1030`**, across ~43 real seconds (`DatasetEnabledAt` 14:53:35.63Z →
  `OnDisposeEnteredAt` 14:54:18.70Z) - only the first of those 71 snapshots survived export.

Both facts are re-verified in this sprint, unchanged, from the same read-only raw capture fixtures
Sprint 15.18 committed (`Tests/Research/StopLossCalibration/RealMarket/RawCapture/Sprint15_18/`,
SHA-256-checked) - see `Sprint1519_FormingBarAnalysis.csv`. **Conclusion re-confirmed, not
re-litigated: first-Add()-wins is not acceptable for scientific OHLCV capture.**

============================================================
3. ATAS SDK INVESTIGATION
============================================================

Before writing any code, the actual installed ATAS SDK (`ATAS.Indicators.dll`, `ATAS.DataFeedsCore.dll`,
`ATAS.Types.dll` under `C:\Program Files (x86)\ATAS Platform\`) was inspected by reflection (the same
technique Sprint 15.17 used to confirm `OnDispose()` is real).

**Searched for and NOT found** (confirmed absent from every public/protected member of every
public type in all three assemblies): `IsNewBar`, `IsClosed`, `CandleState`, `BarState`, `OnBarClose`,
`CloseBar`, or any other explicit "this candle just closed" signal. `BaseIndicator`'s only per-bar
callback is `abstract void OnCalculate(int bar, decimal value)` - there is no second, close-specific
callback anywhere in the hierarchy (`Indicator`/`ExtendedIndicator`/`BaseIndicator`).
`ATAS.Indicators.IndicatorCandle` (the object `MarketContextBuilder.cs` already reads
Open/High/Low/Close/Volume from) has properties for `Time` (bar open time) and `LastTime`, but nothing
resembling "IsFinal".

**Found and usable**: `ATAS.Indicators.BaseIndicator.CurrentBar` - a real, public `int` property,
already read by `IQIAIndicator.cs` (`_builder.Build(bar, CurrentBar)`) and already turned into
`MarketContext.Execution.IsHistorical`/`IsRealtime`/`IsReplay` by the existing, untouched
`Core/MarketContextBuilder.cs` (`bool isRealtime = bar == currentBar - 1; ... IsHistorical = bar <
currentBar - 1`). This is the closest thing to a "closed bar" signal the SDK exposes: as long as
`bar == CurrentBar - 1`, `bar` is the newest bar on the chart and could still receive more ticks; once
a callback for a **later** bar arrives, `bar` is proven closed. Critically, this is not a
"push" notification - ATAS never calls back specifically to announce a close - so the only actionable
form of this signal is **retroactive**: observing that OnCalculate has moved on to a later bar is
itself the proof the previous one closed.

**SDK_CLOSE_SIGNAL: NOT_FOUND** (no explicit signal) — but see Section 4: a usable, already-partially-
wired substitute exists, so this does not block the fix.

============================================================
4. DESIGN QUESTION - OPTION CHOSEN
============================================================

Per the brief's four options:
- **Option A** (explicit closed-bar signal from ATAS): does not exist - ruled out by Section 3.
- **Option B** (detect `bar > previously observed bar`, finalize the previous bar): the only
  mechanism that actually works, because it is the only evidence available at all (Section 3).
- **Option C** (maintain the latest observation for the current bar, finalize only on advance):
  functionally the same mechanism as B, described from the collector's state-machine perspective
  rather than the raw callback perspective.
- **Option D**: none found beyond A/B/C's overlap - `BaseIndicator.CurrentBar` is additional
  corroborating context (Section 3), not a fourth independent mechanism.

**Chosen: B/C combined** - the collector holds a single "pending" (still-forming) bar, replaced by
every further callback for that same bar identity, and commits ("finalizes") it only when a callback
for a later bar (or a Symbol/TimeFrame change, or explicit session-end `Flush()`) proves it is no
longer being updated. This is the minimal, least-invasive change that is actually possible with the
installed SDK - it changes `ScientificDatasetCollector.Add()`'s internal state machine only; it does
not touch `MarketContextBuilder.cs`, does not add a new ATAS lifecycle hook, and does not change what
data is read from `IndicatorCandle`.

============================================================
5. OLD COLLECTOR SEMANTICS
============================================================

```
identity = (SessionId, Symbol, TimeFrame, CurrentBar)
Add(record):
    if identity already seen -> reject as "duplicate", counted, first snapshot kept forever
    else -> commit immediately, remember identity
```

Consequence (Sprint 15.18's finding): the FIRST snapshot of a still-forming bar is what gets
committed, silently, with no structural signal that anything is wrong (every OHLC invariant still
holds trivially for a single-tick "bar").

============================================================
6. NEW COLLECTOR SEMANTICS
============================================================

```
Add(record):
    if structurally invalid -> reject (InvalidRecordsRejected), pending bar untouched
    if no bar pending yet -> this record becomes the pending bar
    if record matches the pending bar's identity exactly -> FormingBarUpdates++, REPLACE pending (latest wins)
    if record advances past the pending bar (same Symbol/TimeFrame, higher CurrentBar),
       or Symbol/TimeFrame changed -> commit the OLD pending bar (RecordsAccepted++), this record becomes the new pending bar
    if record rewinds behind pending AND matches an already-finalized bar -> DuplicateRecordsRejected++ (immutable, never reapplied)
    otherwise (ambiguous rewind) -> RecordsOutOfOrder++ (counted, never applied)

Flush() [called once, at session end]:
    if a bar is still pending -> BarsPendingAtDispose = 1; it is NEVER committed (EXCLUDE_CURRENT_FORMING_BAR)
```

`Records`/`ExportCsv()`/`ExportJson()`/`ExportOhlcvCsv()` only ever see **finalized** (closed-bar)
records - a still-pending bar is structurally absent from every export path, with no separate "don't
export this" check needed anywhere else.

============================================================
7. EXACT STATE MACHINE
============================================================

```
                         ┌──────────────────────────────┐
                         │        (session start)        │
                         └───────────────┬────────────────┘
                                         │ Add(record)
                                         ▼
                              ┌─────────────────────┐
                    ┌────────▶│   no bar pending      │
                    │         └──────────┬───────────┘
                    │                    │ becomes pending
                    │                    ▼
                    │         ┌─────────────────────┐   Add(same identity)
                    │         │   BAR PENDING (open)  │◀──────────────────────┐
                    │         └──────────┬───────────┘   FormingBarUpdates++  │
                    │                    │  Add(advances / stream switch)      │ replace pending
                    │                    ▼                                     │ (latest wins)
                    │         ┌─────────────────────┐                         │
                    └─────────┤   COMMIT (finalize)   │─────────────────────────┘
                    new bar   │   RecordsAccepted++    │
                    pending   └──────────┬───────────┘
                                         │
                     Add(rewind, matches finalized) ──▶ DuplicateRecordsRejected++ (pending untouched)
                     Add(rewind, ambiguous)          ──▶ RecordsOutOfOrder++ (pending untouched)
                     Flush() while a bar is pending  ──▶ BarsPendingAtDispose=1, that bar is DROPPED
```

============================================================
8. EDGE-CASE POLICY
============================================================

| Case | Behavior |
|---|---|
| A. first observed bar | becomes pending; no commit yet |
| B. repeated callbacks, same bar | `FormingBarUpdates++`, pending replaced (latest wins) |
| C. OHLC changes during forming | new snapshot replaces old in full - never merged/aggregated |
| D. Volume changes during forming | same - the whole record (incl. Volume) is replaced atomically |
| E. bar N → bar N+1 transition | bar N committed (`RecordsAccepted++`), bar N+1 becomes pending |
| F. skipped bar indexes | handled implicitly - advancing from bar 5 to bar 8 commits 5, bars 6/7 simply never exist; no crash, no special-cased counter (matches the pre-15.19 design's existing tolerance for this) |
| G. out-of-order callbacks | rewind matching an already-finalized bar → `DuplicateRecordsRejected`; any other rewind → `RecordsOutOfOrder`. Neither is ever applied to the pending bar. |
| H. session reset (`Clear()`) | pending bar, last-finalized identity, `Flush()` state, and every counter are all reset; a fresh bar 0 after `Clear()` behaves exactly like true session start (verified: `Sprint1519FormingBarCollectorTests.Test9`) |
| I. Symbol/TimeFrame change | treated as an immediate stream switch: old pending bar is committed, new record starts a new pending bar under its own identity - two instruments/timeframes are never blended in one pending slot |
| J. `OnDispose()` while a bar is forming | **EXCLUDE_CURRENT_FORMING_BAR** (chosen policy, explicit): `Flush()` marks it `BarsPendingAtDispose=1` and never commits it. Chosen because there is no SDK evidence available at dispose time that the pending bar has actually closed (Section 3) - claiming it closed would repeat exactly the defect this sprint fixes, just moved to session end. |

============================================================
9. API CHANGES
============================================================

**Allowed-file-list constraint honored**: `Visualization/Dashboards/DatasetDashboard.cs`,
`Visualization/Widgets/ScientificCollectionMonitorWidget.cs`, and
`Visualization/State/SystemHealthAggregator.cs` are NOT on this sprint's allowed-file list, and all
three read `ScientificDatasetCollector`'s public API directly. Every property they use
(`RecordsAccepted`, `DuplicateRecordsRejected`, `InvalidRecordsRejected`, `RecordsOutOfOrder`, `Errors`,
`Status`, `TotalAddAttempts`, `Count`, `BarsCollected`, `FirstTimestamp`, `LastTimestamp`,
`RejectedReason`, `SessionId`, `Describe()`) keeps its exact name and type - **zero signature changes**
- so these three files compile and behave unchanged; only the underlying *meaning* of
`RecordsAccepted`/`DuplicateRecordsRejected`/`RecordsOutOfOrder` evolved (Section 6), same evolution
philosophy Sprint 15.17 already used for `ScientificDatasetRecord`'s optional trailing parameters.

**New, additive-only members** on `ScientificDatasetCollector`:
`FormingBarUpdates`, `BarsPendingAtDispose`, `PendingBar`, `PendingBarFirstSeenAt`,
`PendingBarLastSeenAt`, `Flush()`, and the Sprint-15.19-named aliases `BarsFinalized`,
`BarsDuplicated`, `BarsOutOfOrder` (each `=>` the pre-existing property of the same value).

**`ScientificDatasetMetadata`** (`Core/Calibration/ScientificDatasetSession.cs`): two new trailing
OPTIONAL parameters, `FormingBarUpdates = 0` and `BarsPendingAtDispose = 0` - defaulted specifically so
`System.Text.Json` still deserializes the Sprint 15.18 raw-capture fixtures' `metadata.json` files
(committed, read-only, never edited) without throwing on the now-missing fields. Verified: all 63
Sprint 15.18 tests still pass unchanged (Section 10).

**`IQIAIndicator.cs`**: `OnDispose()` now calls `_scientificDatasetCollector?.Flush()` as its first
action, before `WriteLifecycleSnapshot()`/`ExportScientificDataset()`. No other method signature
changed.

============================================================
10. TESTS
============================================================

**New this sprint** (all passing):

| File | Tests | Purpose |
|---|---|---|
| `Tests/Calibration/Sprint1519FormingBarCollectorTests.cs` | 15 | The brief's Section 13 items 1-14, one method per item (Test 6 split into two: with/without a bar pending at Flush) |
| `Tests/Research/StopLossCalibration/RealMarket/Sprint1519ComparisonTests.cs` | 2 | Generates the 3 required comparison CSVs; independently asserts the fix's mechanism (old rule keeps tick #1, new rule keeps tick #71) on a synthetic reproduction of Session 165335's documented pattern |
| `Tests/Research/StopLossCalibration/RealMarket/LegacyFirstAddWinsReference.cs` | (helper, no tests of its own) | Comparison-only reimplementation of the pre-15.19 dedup rule |

**Traceability - brief Section 13 → test method:**

| # | Requirement | Test |
|---|---|---|
| 1 | one bar, one callback → finalized after transition | `Test1_OneBarOneCallback_FinalizesOnlyAfterBarTransition` |
| 2 | changing OHLCV, only final snapshot committed | `Test2_ChangingOhlcvAcrossCallbacks_OnlyFinalSnapshotIsCommitted` |
| 3 | identical repeated callbacks → one record | `Test3_IdenticalRepeatedCallbacks_StillOneFinalizedObservation` |
| 4 | 10 updates, bar N contains update #10 | `Test4_TenUpdatesForOneBar_FinalizedRecordIsTheTenthUpdate` |
| 5 | multiple bars, multiple updates each | `Test5_MultipleConsecutiveBarsEachWithFormingUpdates_ExactlyOneRecordEach` |
| 6 | current bar at dispose - documented policy | `Test6_FlushAtDisposeWithBarStillForming_ExcludesItFromFinalizedDataset`, `Test6b_FlushWithNothingPending_ReportsZeroPendingAtDispose` |
| 7 | invalid update between valid ones | `Test7_InvalidUpdateBetweenValidOnes_NeverCorruptsThePendingBar` |
| 8 | out-of-order bar index | `Test8_OutOfOrderBarIndex_DiagnosedAsRecordsOutOfOrder_NeverAppliedToPending` |
| 9 | session reset | `Test9_ClearAfterPendingBarExists_LeavesNoResidualPendingState` |
| 10 | Symbol/TimeFrame change | `Test10_SymbolChangeMidSession_...`, `Test10b_TimeFrameChangeMidSession_...` |
| 11 | scale/invariance assumptions untouched | `Test11_QDE012ProtocolConstants_StillLocked` |
| 12 | export format remains valid | `Test12_OhlcvCsvHeader_UnchangedBySprint1519` |
| 13 | no source OHLCV mutation | `Test13_AddNeverMutatesTheSourceRecord_...` |
| 14 | no production trading logic touched | git-diff-level guarantee, not unit-testable - see Section 11 (same category as Sprint 15.17.1's own TEST 10) |

============================================================
11. REGRESSION RESULTS
============================================================

**Before this sprint's fix** (i.e. the exact pre-15.19 collector behavior): 7 tests in
`ExportObservabilityTests.cs` and 6 tests across `ScientificDatasetCollectorTests.cs`/
`ScientificDatasetRealMarketCaptureTests.cs` encoded the old first-Add()-wins assumption directly
(e.g. "one `Add()` call is immediately `RecordsAccepted`", "a second callback for the same bar is a
`DuplicateRecordsRejected`"). Per the brief's explicit instruction, none of these were "blindly"
updated - each failure was traced to its specific obsolete assumption and rewritten with a doc comment
explaining why (see the files themselves; summarized in `Sprint1519_CollectorSemantics.csv`'s
`SecondCallbackSameBar`/`CommitPolicy` rows). One test (`Add_OutOfOrderTimestamp_IsAcceptedButCounted`
and its sibling) was renamed entirely: its pre-15.19 concept ("an accepted record's Timestamp
regressed") no longer has a natural home under the new commit-on-advance model, and was replaced with
the concept `RecordsOutOfOrder` now actually measures - an ambiguous CurrentBar rewind (brief edge case
G) - rather than kept passing by coincidence.

**After**: full-repository `dotnet test` (no filter) - **172/172 passing, 0 failed, 0 skipped.**
Broken down:
- `Tests/Calibration/*` + `Tests/Dashboards/*`: 67/67 passing.
- `Tests/Research/StopLossCalibration/RealMarket/*` (all Sprint 15.18 + 15.19 tests): 80/80 passing
  (63 Sprint 15.18 + 17 Sprint 15.19).
- New Sprint 15.19 collector-semantics suite (`Sprint1519FormingBarCollectorTests.cs`): 15/15 passing.
- Every other pre-existing suite (A1/A2/Hybrid calibration campaigns, scientific model conformance,
  fusion/decision/signal engines, etc.): unaffected, all passing.

**Before** (for comparison, same full suite at Sprint 15.18's close): 154/154 passing. **After**:
172/172 - the delta is this sprint's new tests (15 in `Sprint1519FormingBarCollectorTests.cs` + 2 in
`Sprint1519ComparisonTests.cs`), with zero regressions anywhere else in the suite.

**Skipped**: 0.

============================================================
12. PRODUCTION PERIMETER
============================================================

**Modified** (all on this sprint's explicit allowed list):
- `Core/Calibration/ScientificDatasetCollector.cs`
- `Core/Calibration/ScientificDatasetSession.cs`
- `IQIAIndicator.cs` (only: one `Flush()` call added at the top of `OnDispose()`)
- `Tests/Calibration/ScientificDatasetCollectorTests.cs`
- `Tests/Calibration/ScientificDatasetRealMarketCaptureTests.cs`
- `Tests/Calibration/ExportObservabilityTests.cs`

**Created** (all on this sprint's explicit allowed list):
- `Tests/Calibration/Sprint1519FormingBarCollectorTests.cs`
- `Tests/Research/StopLossCalibration/RealMarket/Sprint1519ComparisonTests.cs`
- `Tests/Research/StopLossCalibration/RealMarket/LegacyFirstAddWinsReference.cs`
- `Tests/Research/StopLossCalibration/RealMarket/Sprint1519_FormingBarAnalysis.csv`
- `Tests/Research/StopLossCalibration/RealMarket/Sprint1519_CollectorSemantics.csv`
- `Tests/Research/StopLossCalibration/RealMarket/Sprint1519_RealCaptureComparison.csv`
- `Documentation/Scientific/QDE-012_Sprint_15.19_Forming_Bar_Collector_Fix_Report.md` (this file)

**`Core/Calibration/ScientificDatasetRecord.cs`** - on the allowed list, but **not modified**: no
change to it was needed (the pending/finalization logic operates entirely on whole
`ScientificDatasetRecord` instances; nothing about the record's own shape needed to change).

**Not touched**: `Engine/`, `Decision/`, `Risk/`, `Signal/`, `Strategy/`, any stop-loss production
logic, any QDE-012 protocol/report file, `Core/MarketContextBuilder.cs`,
`Core/Calibration/ScientificDatasetExporter.cs`, `Core/Calibration/ExportResult.cs`,
`Core/Calibration/DatasetLifecycleLog.cs`/`DatasetLifecycleSnapshot*.cs`, and all three
`Visualization/*` files discussed in Section 9 - confirmed by `git status`/`git diff --stat` showing
no change to any of those paths from this sprint's work. **No production modification was found
necessary at any point in this sprint.**

============================================================
13. REAL ATAS VALIDATION STATUS
============================================================

**REAL_ATAS_RECAPTURE: NOT_RUN.** Per the brief's explicit instruction (Sections 15/21/22), this
sprint does not perform, simulate, or fabricate a real ATAS Market Replay session. Everything in
Sections 1-12 is proven at the unit-test level and via a clearly-labeled synthetic reproduction of the
documented Session 165335 pattern (`Sprint1519ComparisonTests.cs` - see its class doc comment for the
exact honesty boundary observed). `Sprint1519_RealCaptureComparison.csv` states explicitly, for every
metric it cannot compute from the existing Sprint 15.18 export, `NOT_COMPUTABLE_REQUIRES_NEW_CAPTURE` -
the pre-15.19 collector never persisted a rejected duplicate's raw OHLCV, so there is no way to replay
the real 165421/165335 sessions through the new rule after the fact. **The fix is code-complete and
unit-proven; it is not yet validated against a real ATAS session run with this sprint's code.**

============================================================
14. LIMITATIONS
============================================================

- **No real ATAS validation yet** (Section 13) - by design, per the brief.
- **The real 165421/165335 captures cannot be "replayed" through the new rule** - only a fresh capture
  can show the actual after-fix numbers for real market data.
- **`BaseIndicator.CurrentBar` vs `bar`-parameter comparison is corroborating context, not literal
  proof of closure** - the actual finalization trigger is retroactive (a later callback arrived), which
  is the best evidence available, not a guarantee ATAS itself considers the bar "closed" in some
  internal sense this SDK surface doesn't expose (Section 3's stop condition was considered and did
  not apply: retroactive advancement is sufficient, reliable evidence, not a guess).
- **Timezone remains unresolved** - unchanged from Sprint 15.18/15.19, out of this sprint's scope
  (`MarketContextBuilder.cs` was not touched).
- **`Visualization/*` dashboards do not yet surface the new diagnostics** (`FormingBarUpdates`,
  `BarsPendingAtDispose`) - those three files are outside this sprint's allowed-file list; the new
  counters are fully available on the collector's public API for a future sprint to wire in.
- **Skipped-bar-index handling (edge case F) has no dedicated counter** - matches the pre-15.19
  design's own tolerance for non-contiguous `CurrentBar` sequences; not a new gap introduced here.

============================================================
15. FINAL VERDICT
============================================================

The forming-bar-capture defect Sprint 15.18 found in real ATAS data is fixed at the code level: the
collector now buffers the currently-forming bar and commits only the final observation once a later
bar proves it closed, backed by 17 new tests (all passing) plus a synthetic reproduction of the exact
documented real-world pattern (Session 165335) showing the old rule keeps the first tick and the new
rule keeps the last. The fix is not yet confirmed against a fresh real ATAS capture - that is Sprint
15.19's necessary next step, not something this sprint can or should claim on its own.

============================================================
16. NEXT ACTION
============================================================

Run a new real ATAS Market Replay session (same instrument/timeframe as Session 165421 ideally, for a
direct comparison) with this sprint's code, then re-run the exact Sprint 15.18 quality contract
(`RealMarketQualityAnalyzer`, unchanged) against the fresh capture. Success criteria: the zero-range/
`Volume=1` single-tick tail signature (Section 2) should not reappear; `DUPLICATE_HANDLING` and
`SCIENTIFIC_ADMISSIBILITY` should be re-evaluated from `BLOCKED` toward `PASS`/`PASS_WITH_CAVEATS`
using that same contract, not a redefined one. Only after that confirmation should a future sprint
revisit QDE-012 calibration on real data.

============================================================
FINAL GATE
============================================================

```
SDK_CLOSE_SIGNAL:
    NOT_FOUND

FORMING_BAR_DETECTION:
    PASS

FINAL_SNAPSHOT_CAPTURE:
    PASS

ONE_RECORD_PER_CLOSED_BAR:
    PASS

ONDISPOSE_POLICY:
    EXCLUDE_CURRENT_FORMING_BAR

OHLCV_INTEGRITY:
    PASS

TIMESTAMP_INTEGRITY:
    PASS

HORIZON_40_ADEQUACY:
    UNRESOLVED

REAL_ATAS_RECAPTURE:
    NOT_RUN

QDE_012_CALIBRATION:
    NOT_RUN

K_SELECTED:
    NO

PRODUCTION_TRADING_CHANGES:
    0
```

**SPRINT 15.19 VERDICT:**
The forming-bar-capture defect is fixed and unit-proven (17 new tests, zero regressions across the
full suite), but remains unvalidated against a real ATAS session until the user runs one with this
sprint's code.
