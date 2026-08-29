============================================================
QDE-012 — SPRINT 15.17 — ATAS → SCIENTIFIC DATASET COLLECTOR
REAL MARKET DATA ACQUISITION PIPELINE
============================================================

Date: 2026-08-13
Protocol: QDE-012 v1.1 (locked), unchanged by this sprint.
Scope: INFRASTRUCTURE ONLY. No calibration, no k selection, no Risk Engine, no production Stop
Loss, no scientific-model changes. This sprint wires the existing (but never-firing)
ScientificDatasetCollector into a real OHLCV-capable, reliably-exported dataset. It builds the
pipe; it does not run water through it - that requires a real ATAS Market Replay session the
user runs personally (Phase 12 below).

============================================================
1. VERDICT
============================================================

**CODE READY — ATAS VALIDATION PENDING.**

Per the sprint's own gate rule (Phase 11/14): PASS can only be declared once a real ATAS Market
Replay session has produced a non-empty, integrity-checked dataset, and that has not happened in
this sprint - no ATAS session was run, none was simulated, and none is claimed. Everything in
this report up to the "ATAS Validation Status" section (§17) is code-level: build, unit tests,
and static verification of the wiring. Sections 17-21 describe what a real run must produce for
the sprint to move from this verdict to PASS.

============================================================
2. EXECUTIVE SUMMARY
============================================================

Sprint 15.16 found that `ScientificDatasetCollector` already receives real per-bar data from ATAS
(via `IQIAIndicator.OnCalculate`) but (a) only ever captured Close price, never full OHLCV, and
(b) accumulated records in memory with no reliable export trigger - `ExportScientificDataset()`
had no caller anywhere in the live UI. This sprint closes both gaps without touching any trading
logic:

- `ScientificDatasetRecord` now carries Open/High/Low/Volume alongside the pre-existing
  Close/Metrics/Categories, sourced from the same `Core.MarketContext` object every other engine
  in the pipeline already uses for this bar - no new data acquisition, no re-derivation.
- The collector validates structural OHLCV integrity (High≥Low, High≥max(Open,Close),
  Low≤min(Open,Close), Volume≥0) before accepting a record, and tracks out-of-order timestamps as
  a diagnostic counter rather than rejecting them (rationale: §7).
- `IQIAIndicator` now overrides ATAS's own `OnDispose()` - confirmed by reflection against the
  installed `ATAS.Indicators.dll` to be a real, protected virtual hook on `BaseIndicator`, not an
  invented method - to export automatically when the indicator is removed from the chart. The
  pre-existing `ExportScientificDataset()` manual-trigger path is unchanged and still works.
- Export now produces four files per session: the pre-existing rich CSV/JSON (Metrics/Categories
  preserved, now also carrying OHLCV columns), plus a new minimal OHLCV-only CSV and a
  `dataset_metadata.json` provenance file, both new in this sprint.
- 21 new automated tests (exceeding the 15 required) cover mapping, validation, dedup, ordering,
  export, metadata, and non-mutation. Full suite: 89/89 passing, 0 regressions, 0 build warnings.
- Zero production business-logic files touched: no scientific model, no Decision/Fusion/Signal/
  TradePlan engine, no threshold, no A1 formula, no QDE-012 protocol file.

============================================================
3. ÉTAT INITIAL (from Sprint 15.17 Phase 1 audit)
============================================================

- Architecture confirmed: a single, synchronous, in-process call chain from ATAS through
  `IQIAIndicator.OnCalculate` to every engine to the collector - no `MarketAdapter`/`TCP`/
  `MarketDataStore`/`LiveDataManager`/`CandleBuilder`/`FeatureEngine`/`StrategyEngine` exists in
  this codebase under any name (`StrategyEngine` in particular is a design doc that was never
  implemented; `MethodologyEngine` fills its real role).
- `ScientificDatasetCollector.Add(...)` was already called every bar when
  `EnableScientificDataset` was on, but `ScientificDatasetRecord` only stored `CurrentPrice`
  (confirmed to be `context.Price.Close`) - no Open/High/Low/Volume anywhere.
- `ExportScientificDataset()` existed but had no caller anywhere in the live UI or lifecycle - a
  user could collect data all session and lose it all on indicator removal.
- No `Dispose`/lifecycle-teardown code existed anywhere in `IQIAIndicator.cs`.

============================================================
4. ARCHITECTURE RÉELLE
============================================================

```
ATAS platform (external, third-party)
   ↓
IQIAIndicator.OnCalculate(int bar, decimal value)         [IQIAIndicator.cs:158]
   ↓ GetCandle(bar) → ATAS IndicatorCandle
MarketContextBuilder.Build(bar, CurrentBar)                [Core/MarketContextBuilder.cs:52]
   ↓ → Core.MarketContext context  (full OHLCV, Volume, Instrument, Clock, TimeFrame)
Regime → Fusion → Decision → Methodology → SignalEngine → TradePlanEngine   (unchanged, untouched)
   ↓
[if EnableScientificDataset]
   ScientificDatasetRecord.From(..., open: context.Price.Open, high: context.Price.High,
                                      low: context.Price.Low,  volume: context.Volume.Volume)
   ↓
   ScientificDatasetCollector.Add(record)     [IQIAIndicator.cs:343-370, wrapped in try/catch]
   ↓ (in memory, per bar)
[on OnDispose() OR manual ExportScientificDataset() call]
   ScientificDatasetSessionWriter.Export(...)  → 4 files on disk (§8/§9)
```

No new architectural layer was introduced. The branch point is exactly where Sprint 15.17's
Phase 1 audit identified it: `IQIAIndicator.cs`, inside the existing `if (EnableScientificDataset)`
block, immediately after every trading engine has already produced its result for the bar.

============================================================
5. SCIENTIFICDATASETCOLLECTOR
============================================================

`Core/Calibration/ScientificDatasetCollector.cs`. New in this sprint:

- `InvalidRecordsRejected`, `RecordsOutOfOrder`, `FirstTimestamp`, `LastTimestamp` (all previously
  absent).
- `Add()` now validates OHLCV structural integrity before the pre-existing dedup check (§7).
- `ToOhlcvCsv()` / `ExportOhlcvCsv(path)` - the new minimal dataset export (§9).
- `Errors` is now `DuplicateRecordsRejected + InvalidRecordsRejected` (was `DuplicateRecordsRejected`
  alone) - both existing tests asserting `Errors` still pass unchanged, since neither test ever
  produces an invalid record (`InvalidRecordsRejected` stays 0 in both).
- `ValidateInvariant()` extended to `TotalAddAttempts == RecordsAccepted + DuplicateRecordsRejected
  + InvalidRecordsRejected`.

Unchanged: the `(SessionId, Symbol, TimeFrame, CurrentBar)` dedup identity, `ToCsv()`/`ExportCsv()`/
`ToJson()`/`ExportJson()` (now carrying 4 extra OHLCV columns, still fully backward compatible),
`Describe()`/correlation statistics.

============================================================
6. POINT DE BRANCHEMENT
============================================================

**Design rationale (Phase 2), before any code was written:**

- **Why this point is correct**: `context` (`Core.MarketContext`, fully built and validated) is
  already the last local variable every engine above has already consumed by the time execution
  reaches this line. Placing the collector call here means it can only ever be a passive reader of
  already-finalized state - it is structurally impossible for it to influence any engine's input,
  because every engine already ran before this line executes.
- **What data is available**: `context.Price.{Open,High,Low,Close}`, `context.Volume.{Volume,
  BidVolume,AskVolume,Delta}`, `context.Instrument.Symbol`, `context.TimeFrame`,
  `context.Clock.CurrentTime` - all already read from ATAS's `IndicatorCandle` earlier in the same
  method, not re-derived or re-fetched.
- **What data is copied**: Open/High/Low/Close/Volume/Timestamp/Symbol/TimeFrame/CurrentBar, plus
  the pre-existing scientific Metrics/Categories extraction (unchanged). `BidVolume`/`AskVolume`/
  `Delta`/`TickSize`/`PointValue`/`TickValue` are available but were **not** added to the record
  schema this sprint - out of scope of the "OHLCV minimum" contract in the brief's Phase 4/9; they
  remain readable from `context`/`InstrumentInfo` in the live pipeline if a future sprint needs
  them.
- **What is not available**: a genuine ATAS-native "bar closed" flag (confirmed absent by
  reflection and by the pre-existing `MarketContextBuilder.cs` comments - see Sprint 15.17 Phase 1
  audit §3/§5) and a genuine ATAS-native timezone (bar timestamp is `IndicatorCandle.Time` passed
  through unconverted - recorded honestly as "Unspecified" in the new metadata file, §10, rather
  than guessed).
- **How side effects are avoided**: the entire block is wrapped in `try/catch` (new this sprint -
  it was not there before) so that any unexpected exception in the collection path is swallowed
  rather than propagating into `OnCalculate` and disrupting the trading pipeline or crashing the
  indicator. Expected rejections (invalid OHLCV, duplicates) are handled inside
  `ScientificDatasetCollector.Add()` itself via counters, not exceptions - the catch block is a
  last-resort safety net, not the primary rejection mechanism.

============================================================
7. FLUX ATAS → COLLECTOR
============================================================

| Step | File | Method | Data | Frequency | Timestamp available |
|---|---|---|---|---|---|
| 1 | ATAS platform | (external) | `IndicatorCandle` | per `OnCalculate` invocation (frequency not independently verifiable from this repo - see Phase 1 audit §3) | yes, `IndicatorCandle.Time` |
| 2 | `IQIAIndicator.cs:158` | `OnCalculate(bar, value)` | raw ATAS candle via `GetCandle(bar)` | same as above | yes |
| 3 | `Core/MarketContextBuilder.cs:52` | `Build(bar, currentBar)` | `Core.MarketContext` (OHLCV+Volume+Instrument+Clock+TimeFrame) | once per `OnCalculate` call | yes, unconverted passthrough |
| 4 | `IQIAIndicator.cs:~345` | `ScientificDatasetRecord.From(...)` | flattened record incl. Open/High/Low/Close/Volume | once per `OnCalculate` call, only if `EnableScientificDataset` | yes |
| 5 | `Core/Calibration/ScientificDatasetCollector.cs` | `Add(record)` | validated + deduped in-memory record | same as step 4 | yes |
| 6 | `Core/Calibration/ScientificDatasetSession.cs` | `Export(...)` | 4 files on disk | on `OnDispose()` or manual `ExportScientificDataset()` call | n/a |

No thread hand-off occurs anywhere in this chain (confirmed no `lock`/`Task`/`ConcurrentX` usage
anywhere in the path - Phase 1 audit §10, unchanged by this sprint).

============================================================
8. FORMAT DE DONNÉES
============================================================

`ScientificDatasetRecord` (backward-compatible - see §11 for the migration approach):

```
SessionId : Guid
Timestamp : DateTime      (ATAS IndicatorCandle.Time, unconverted)
Symbol : string
TimeFrame : string
CurrentPrice : decimal    (= Close; historical field name kept for compatibility)
HistoryLength : int
CurrentBar : int
Metrics : IReadOnlyDictionary<string, double?>     (unchanged, scientific model outputs)
Categories : IReadOnlyDictionary<string, string>   (unchanged, scientific model outputs)
Open : decimal            (new, Sprint 15.17)
High : decimal            (new, Sprint 15.17)
Low : decimal             (new, Sprint 15.17)
Volume : decimal          (new, Sprint 15.17)
Close : decimal           (new, computed property = CurrentPrice - for OHLCV-shaped consumers)
```

Fields NOT captured (available in `context` but out of the brief's OHLCV-minimum scope):
BidVolume, AskVolume, Delta, TickSize, PointValue, TickValue.

============================================================
9. PERSISTENCE
============================================================

`ScientificDatasetSessionWriter.Export(...)` now writes **four** files per session (was two):

```
ScientificDataset_{Symbol}_{TimeFrame}_{yyyyMMdd_HHmmss}.csv           (unchanged, now +OHLCV columns)
ScientificDataset_{Symbol}_{TimeFrame}_{yyyyMMdd_HHmmss}.json          (unchanged, now +OHLCV fields)
ScientificDataset_{Symbol}_{TimeFrame}_{yyyyMMdd_HHmmss}_ohlcv.csv     (NEW - minimal, QDE-012-facing)
ScientificDataset_{Symbol}_{TimeFrame}_{yyyyMMdd_HHmmss}_metadata.json (NEW - provenance/integrity)
```

`_ohlcv.csv` columns exactly: `SessionId,Timestamp,Symbol,TimeFrame,Open,High,Low,Close,Volume,
CurrentBar`. This is the file a future sprint would feed into a real-data loader for QDE-012 (§14).

Existing consumers of `ScientificDatasetSession`/`ExportScientificDataset()` (the pre-existing
public API, `DatasetDashboard`, `ScientificCollectionMonitorWidget`) are unaffected - `.CSVPath`/
`.JSONPath` are unchanged; `.OhlcvCsvPath`/`.MetadataPath` are new, optional-by-default fields.

Export trigger: **`OnDispose()`** override on `IQIAIndicator`, confirmed via reflection against the
installed `ATAS.Indicators.dll` to be a real `protected virtual void OnDispose()` on ATAS's own
`BaseIndicator` (not invented - see §13 for the exact reflection evidence). The pre-existing manual
`ExportScientificDataset()` public method is unchanged and remains available for an in-session
export without waiting for indicator removal.

============================================================
10. PROVENANCE
============================================================

`dataset_metadata.json` (new type `ScientificDatasetMetadata`):

```json
{
  "Source": "ATAS",
  "SessionId": "...",
  "Symbol": "...",
  "TimeFrame": "...",
  "CollectionStart": "...",
  "CollectionEnd": "...",
  "BarsReceived": 0,
  "BarsWritten": 0,
  "Duplicates": 0,
  "InvalidRejected": 0,
  "OutOfOrder": 0,
  "FirstTimestamp": "...",
  "LastTimestamp": "...",
  "Timezone": "Unspecified (ATAS IndicatorCandle.Time passthrough, no conversion applied)",
  "CollectorVersion": "1.0-real-ohlcv"
}
```

`Source` is hardcoded to `"ATAS"` at the one call site (`ScientificDatasetSessionWriter.Export`) -
there is no code path that could ever write `"SYNTHETIC"` here, since this writer is never called
from any synthetic-data code (`Tests/Research/StopLossCalibration/*` does not reference
`ScientificDatasetSessionWriter` at all - confirmed by grep). `Timezone` is reported honestly as
Unspecified rather than guessed - see §12.

============================================================
11. BACKWARD COMPATIBILITY / MIGRATION
============================================================

`ScientificDatasetRecord` is a positional record; two pre-existing test files
(`Tests/Calibration/ScientificDatasetCollectorTests.cs`, `Tests/Dashboards/
DatasetDashboardStatusTests.cs`) construct it positionally with the original 9 arguments. The new
Open/High/Low/Volume fields were appended at the end with `= 0m` defaults specifically so these
call sites keep compiling and passing unchanged - confirmed: both files' tests (11 total) pass
unmodified after this sprint's changes. `ScientificDatasetRecord.From(...)`'s signature did change
(4 new required parameters inserted before the optional `trace` parameter) - safe, because it has
exactly one call site in the entire repository (`IQIAIndicator.cs`), which this sprint updates.

============================================================
12. TIMEZONE
============================================================

Unchanged limitation from Sprint 15.16/15.17 Phase 1 audits: `Core/MarketContextBuilder.cs` passes
ATAS's `IndicatorCandle.Time` through with no `TimeZoneInfo`/`DateTimeKind` conversion anywhere in
the live pipeline. This sprint does not add timezone handling (out of scope - the brief's
prohibition list includes not modifying the scientific/production model files, and
`MarketContextBuilder` was left untouched). The new metadata file records this honestly
(`"Unspecified (ATAS IndicatorCandle.Time passthrough, no conversion applied)"`) rather than
guessing a value - this is a known gap for whoever eventually feeds this dataset into QDE-012 (§14).

============================================================
13. DEDUPLICATION
============================================================

Identity unchanged: `(SessionId, Symbol, TimeFrame, CurrentBar)`, first-`Add()`-wins. New this
sprint: records are now also validated for OHLCV structural integrity **before** the dedup check,
so an invalid record never occupies the dedup slot - a later, valid record for the same bar can
still be accepted. Out-of-order timestamps are tracked (`RecordsOutOfOrder`) but **not** rejected:
`MarketContextBuilder.cs`'s own pre-existing comments document that ATAS Market Replay rewinds can
revisit already-seen bars, and the `CurrentBar`-based dedup already guards against literal
re-insertion of the same bar - rejecting purely on timestamp order would risk silently dropping
legitimate data (e.g. a genuine replay rewind) for no compensating integrity benefit. This
decision is deliberate and documented in `ScientificDatasetCollector.cs`'s own doc comment, not
silent.

Whether ATAS's `OnCalculate` can fire more than once for the same still-forming bar remains
unconfirmed from source (Phase 1 audit §3/§5) - this is exactly the kind of behavior only a real
ATAS run can settle, which is why §17 below asks for `BarsReceived` vs `BarsWritten` to be checked
in a real session.

============================================================
14. TESTS
============================================================

`Tests/Calibration/ScientificDatasetRealMarketCaptureTests.cs`, 21 tests (the 15 required by the
brief's Phase 11, several split into more granular cases), all passing:

| Brief's TEST # | What it covers | Test method(s) |
|---|---|---|
| 1 | OHLCV mapping correct | `From_MapsOpenHighLowCloseVolume_Correctly` |
| 2 | Timestamp conservé | `From_PreservesTimestamp_Exactly` |
| 3 | Symbol conservé | `From_PreservesSymbol_Exactly` |
| 4 | TimeFrame conservé | `From_PreservesTimeFrame_Exactly` |
| 5 | Duplicate CurrentBar rejeté | `Add_DuplicateBarWithOhlcvData_IsRejectedSecondTime` |
| 6 | Bar invalide rejetée | `Add_StructurallyInvalidBar_IsRejected` (×4 cases) + `Add_StructurallyValidBar_IsAccepted` (boundary case) |
| 7 | Ordre chronologique | `Add_OutOfOrderTimestamp_IsAcceptedButCounted`, `Add_InOrderTimestamps_NeverIncrementsOutOfOrder` |
| 8 | Collection OFF ne persiste rien | `EmptyCollector_ExportsNoDataRows` (see caveat below) |
| 9 | Collection ON accepte une observation | `Add_ValidObservation_IsAccepted` |
| 10 | Export produit un fichier | `SessionWriter_Export_WritesAllFourFiles` |
| 11 | CSV contient OHLCV | `ToOhlcvCsv_ContainsOhlcvHeaderAndValues` |
| 12 | Metadata correcte | `Metadata_ReportsRealCountersAndSource_NotFabricated` |
| 13 | Source series/input non mutée | `From_DoesNotMutateInputs`, `Add_DoesNotMutateSourceRecord` |
| 14 | Scientific metrics toujours présentes | `From_StillPopulatesScientificMetricsAndCategories_UnchangedByOhlcvAddition` |
| 15 | Collection ON/OFF n'affecte pas le pipeline | `CollectionActivity_NeverChangesWhatLaterCodeObservesFromTheSameInputs` (see caveat below) |

**Caveats, stated explicitly rather than glossed over:**
- `IQIAIndicator` cannot be instantiated in any test (it derives from ATAS's `Indicator`, requiring
  the live platform) - confirmed by grep, no test anywhere in this repository (pre-existing or
  new) constructs `new IQIAIndicator(`. TEST 8 and TEST 15 are therefore proven at the
  `ScientificDatasetCollector`/`ScientificDatasetRecord` level (an empty collector persists nothing;
  `Add()`/`From()` never mutate their inputs and are the last statement in the method), not at the
  full `IQIAIndicator.OnCalculate` level. Full end-to-end "Regime/Fusion/Decision/Signal/TradePlan
  identical with collection ON vs OFF" can only be established by a real ATAS run producing
  identical dashboard/decision output in both configurations - this is exactly the same category
  of limitation the brief's own Phase 10 already requires being explicit about for the ATAS event
  itself ("ce test prouve seulement que le collector fonctionne... pas qu'ATAS fournit correctement
  cet événement").

**Full suite**: before this sprint, 68/68 passing (Sprint 15.16's closing count). After: **89/89
passing, 0 failed, 0 skipped.** Zero regressions.

============================================================
15. BUILD
============================================================

```
dotnet build IQIAIndicator/IQIAIndicator.csproj   → 0 Warning(s), 0 Error(s)
dotnet test  IQIAIndicator/Tests/IQIAIndicator.Tests.csproj → 89 passed, 0 failed, 0 skipped
```

One xUnit analyzer warning (`xUnit2000`, expected/actual argument order) was found and fixed during
development, not left in the final build - confirmed 0 warnings on the final run.

============================================================
16. GIT PERIMETER
============================================================

`git status --short` / `git diff --stat`, classified:

**Sprint 15.17 (this sprint):**
- `IQIAIndicator/Core/Calibration/ScientificDatasetCollector.cs` (M)
- `IQIAIndicator/Core/Calibration/ScientificDatasetRecord.cs` (M)
- `IQIAIndicator/Core/Calibration/ScientificDatasetSession.cs` (M)
- `IQIAIndicator/IQIAIndicator.cs` (M) - branch-point wiring + `OnDispose()` override only
- `IQIAIndicator/Visualization/Dashboards/DatasetDashboard.cs` (M) - added diagnostic fields only
- `IQIAIndicator/Tests/Calibration/ScientificDatasetRealMarketCaptureTests.cs` (new)
- `IQIAIndicator/Documentation/Scientific/QDE-012_Sprint_15.17_ATAS_Real_Data_Collection_Report.md` (new, this file)

**Pre-existing (prior sprints in this conversation, not touched by 15.17):**
- `IQIAIndicator/Tests/Research/StopLossCalibration/Output/campaign_summary.txt` (M, Sprint 15.15)
- `IQIAIndicator/Tests/Research/StopLossCalibration/StableRegionAnalyzer.cs` (M, Sprint 15.15)
- All `??` untracked files under `Tests/Research/StopLossCalibration/*`,
  `Documentation/Scientific/QDE-012_Sprint_15.14/15.15/15.16*.md`, `Tests/XunitWrappers/
  A1Calibration*`/`StableRegionAnalyzerMerge*`/`Sprint1515*`/`RealMarketGate*` - Sprints 15.14-15.16.

**Zero production business-logic files touched.** None of the explicitly protected files
(`KalmanFilterModel`, `DynamicZScoreModel`, `VolatilityModel`, `HalfLife*`, `ADF*`, `KPSS*`, `DFA*`,
`OrnsteinUhlenbeckModel`, `SPRT*`, `Fusion*`, `DecisionEngine`, `MethodologyEngine`, `SignalEngine`,
`TradePlanEngine`, `EntryTrigger*`, any Risk Engine file, `CandidateStopDistance`/A1 formula, any
QDE-012 protocol/report file, `SyntheticSeriesCatalog`) appear anywhere in this diff.

============================================================
17. ATAS VALIDATION STATUS
============================================================

**Not yet performed.** No ATAS session, live or replay, has been run in this sprint - confirmed by
this session containing no interaction with the ATAS platform, only `dotnet build`/`dotnet test`
against the code. Per the brief's Phase 11/14, this alone caps the verdict at CODE READY — ATAS
VALIDATION PENDING regardless of how clean the build/test results are.

============================================================
18. DATASET PRODUIT OU NON
============================================================

**Non produit.** No dataset file exists yet anywhere under `%LocalAppData%\IQIA\ScientificDataset\`
(or any other location) as a result of this sprint - only the code path that would produce one now
exists and is unit-tested in isolation.

============================================================
19-21. NOMBRE DE BARS / PREMIER / DERNIER TIMESTAMP
============================================================

N/A - no real session has been run (§17/§18).

============================================================
22. INSTRUMENT / TIMEFRAME
============================================================

N/A - not yet selected in a real ATAS session; the procedure below (§23) asks the user to choose
these when running the validation.

============================================================
23. PROCÉDURE DE VALIDATION ATAS EXACTE
============================================================

1. Open ATAS.
2. Load the **IQIA Signal** indicator (`[DisplayName("IQIA Signal")]`, category `IQIA`) onto a
   chart.
3. Select any instrument available in your ATAS setup (the codebase has zero instrument
   commitment - any real, traded instrument works).
4. Select any timeframe on that chart (the harness is timeframe-agnostic).
5. In the indicator's property panel, under **Diagnostic**:
   - Set **"Activer le dataset scientifique"** (`EnableScientificDataset`) to **true**.
   - Optionally check/adjust **"Dossier du dataset scientifique"**
     (`ScientificDatasetOutputDirectory`) - default is
     `%LocalAppData%\IQIA\ScientificDataset` (i.e.
     `C:\Users\<you>\AppData\Local\IQIA\ScientificDataset`).
   - Under **Affichage**, set **"Dashboard actif"** (`ActiveDashboard`) to **Dataset** to watch
     collection live (this is the panel extended in §6/Phase 10 of this sprint - shows Bars
     Received/Accepted/Rejected-Duplicates/Rejected-Invalid/Out Of Order/First+Last
     Timestamp/export paths).
6. Open **Market Replay** in ATAS on the same chart/instrument.
7. Start Replay.
8. Let at least several dozen to a few hundred bars pass (the Dataset dashboard's "Bars Received"/
   "Accepted" counters should be climbing).
9. Stop Replay.
10. **Remove the IQIA Signal indicator from the chart** (this triggers the new `OnDispose()`
    export) - **or**, if you'd rather not remove the indicator, trigger a manual export by any
    means that calls the pre-existing public `ExportScientificDataset()` method (this sprint did
    not add a chart button for it - none existed before, and adding UI was out of this sprint's
    minimal-change scope; removing the indicator is the reliable path validated by this sprint).
11. Retrieve the files from the output directory shown in step 5 (or the Dataset dashboard's
    "Output Folder" field while the indicator was still attached).
12. Verify the counters and files as below.

============================================================
24. OÙ TROUVER LE FICHIER / QUEL NOM / QUOI VÉRIFIER
============================================================

**Location**: the directory from `ScientificDatasetOutputDirectory` (default
`%LocalAppData%\IQIA\ScientificDataset`).

**File names** (all sharing one timestamp-stamped prefix,
`ScientificDataset_{Symbol}_{TimeFrame}_{yyyyMMdd_HHmmss}`):
- `..._ohlcv.csv` - the real-market OHLCV dataset. Open it in any text editor/Excel. Header must
  read exactly `SessionId,Timestamp,Symbol,TimeFrame,Open,High,Low,Close,Volume,CurrentBar`; every
  data row must have plausible, non-zero Open/High/Low/Close for the instrument you selected.
- `..._metadata.json` - open it and check: `"Source": "ATAS"`; `BarsWritten` > 0;
  `FirstTimestamp` < `LastTimestamp`; `BarsReceived` ≥ `BarsWritten` (received includes rejected/
  duplicate attempts); `CollectorVersion` present.
- `.csv` / `.json` (no suffix) - the pre-existing rich scientific export, now also carrying
  Open/High/Low/Volume columns/fields alongside Metrics/Categories - not required for the PASS
  gate but useful to sanity-check that scientific metrics still populate correctly on real data.

**Dashboard to watch live**: the **Dataset** dashboard (`ActiveDashboard = Dataset`, §23 step 5) -
shows all counters in real time while Replay runs, plus the export paths once a session has been
written.

**Values to verify against the brief's own PASS criteria (§14 of the brief)**:
- `BarsWritten` (dashboard "Accepted", metadata `BarsWritten`) **> 0**.
- `FirstTimestamp` **<** `LastTimestamp` (metadata, or dashboard "First Timestamp"/"Last
  Timestamp").
- OHLCV values in `..._ohlcv.csv` are valid (non-zero, `High ≥ Low`, consistent with the real
  instrument's actual price level during the replayed period).
- Timestamps in `..._ohlcv.csv`, read top-to-bottom, are monotonically non-decreasing except for
  any row(s) counted in `OutOfOrder` in the metadata (if that count is 0, they must be strictly
  monotonic).

============================================================
25. LIMITATIONS
============================================================

- Whether ATAS's `OnCalculate` fires once per bar close or multiple times per still-forming bar is
  not confirmed from source (Phase 1 audit) - only a real session's `BarsReceived` vs `BarsWritten`
  gap can settle this empirically. If `BarsReceived` is far larger than `BarsWritten` with
  `DuplicateRecordsRejected` absorbing the difference, that confirms multiple-calls-per-bar and
  validates the dedup design; if they're nearly equal, it suggests one call per closed bar.
- Timezone remains "Unspecified" - not fixed this sprint (§12), a known gap for real-data
  calibration planning.
- BidVolume/AskVolume/Delta/TickSize/PointValue/TickValue are available in `context` but were not
  added to the record schema - out of the brief's OHLCV-minimum scope.
- No chart UI button exists for manual export - `OnDispose()` (indicator removal) is the validated
  reliable trigger; the pre-existing `ExportScientificDataset()` method remains available for any
  caller that wants to trigger export without removing the indicator, but nothing in the live UI
  currently calls it directly.
- Full end-to-end pipeline-identity (collection ON vs OFF, Regime/Fusion/Decision/Signal/TradePlan
  bit-for-bit identical) is not unit-testable - see §14's caveat. It is guaranteed by construction
  (the collector call is the last statement in `OnCalculate`, after every engine has already run,
  and is proven read-only w.r.t. its inputs by TEST 13/15) but not independently observed end-to-end.
- This sprint's OHLCV validation treats "Open/High/Low all exactly 0" as "not supplied" to remain
  backward-compatible with the two pre-15.17 test fixtures - documented in code
  (`ScientificDatasetCollector.TryValidateOhlcv`) and in §11 above; a real ATAS instrument will
  never legitimately produce all-zero Open/High/Low, so this cannot mask a real invalid observation.

============================================================
26. EXACT NEXT STEP
============================================================

**The user runs the procedure in §23 personally.** Nothing further should be built or changed
until that real ATAS Market Replay session has been executed and its output checked against §24.
Once confirmed (`BarsWritten > 0`, `FirstTimestamp < LastTimestamp`, valid OHLCV, monotonic
timestamps), report back with the actual counters/file paths observed - the verdict updates from
CODE READY — ATAS VALIDATION PENDING to **PASS** only on that confirmation, per §14/§27 of the
brief. After PASS, the next sprint (not this one) inspects the real dataset's quality (gaps,
duplicates, distribution, sessions, timezone) - QDE-012 calibration itself remains out of scope
until that inspection sprint is also complete, per §15 of the brief.
