============================================================
QDE-012 — SPRINT 15.17.1 — PRODUCTION EXPORT OBSERVABILITY
& ATAS LIFECYCLE VALIDATION
============================================================

Date: 2026-08-14
Protocol: QDE-012 v1.1 (locked), unchanged by this sprint.
Scope: INFRASTRUCTURE ONLY. No calibration, no k selection, no Risk Engine, no production Stop
Loss, no scientific-model changes, no trading logic. This sprint makes the export cycle
observable - it does not change where files go (that was already correct) or run ATAS.

============================================================
1. OBJECTIVE
============================================================

Sprint 15.17's Phase 1 audit (this sprint's own predecessor) found that the files the user found
under `%TEMP%\iqia_sprint1517_*` were 100% test artifacts, not real ATAS output - the production
default path (`%LocalAppData%\IQIA\ScientificDataset`) was already correct, and no export (real or
test) had ever reached it. The one real, separate problem the audit surfaced: export failures were
silently swallowed (`OnDispose()`'s empty `catch (Exception) {}`), and the dashboard could not
distinguish "export never attempted" from "export attempted and failed" from "nothing was
collected." This sprint closes that gap: the export cycle is now fully observable, with an explicit
status, a captured error (type + message), and minimal lifecycle timestamps that will let a real
ATAS session answer, for the first time, whether ATAS actually invokes `OnDispose()` on indicator
removal.

============================================================
2. PHASE 1 AUDIT FINDINGS (carried over, not re-litigated)
============================================================

- Production default path is `%LocalAppData%\IQIA\ScientificDataset` (`IQIAIndicator.cs:128-129`),
  confirmed correct - no bug.
- The `iqia_sprint1517_*` folders under `%TEMP%` were proven, not inferred, to be test artifacts:
  the only two source locations for that exact string are two tests in
  `ScientificDatasetRealMarketCaptureTests.cs` (now fixed, §7 below); the recovered files' literal
  `Symbol="ESH26"`/`TimeFrame="1min"` values only ever appear in those same test fixtures.
  `%LocalAppData%\IQIA\` did not exist anywhere on the machine, confirming no real ATAS export had
  ever landed anywhere.
- The one real, independent bug: `OnDispose()`'s `catch (Exception) {}` swallowed export failures
  completely - no log, no field, no dashboard change. `ExportScientificDataset()` also returned an
  ambiguous empty string for both "no data" and (if it hadn't been caught) failure.
- Whether ATAS genuinely invokes `Dispose()`/`OnDispose()` on indicator removal (vs. only on
  application shutdown or some other lifecycle event) was **not** proven by Sprint 15.17's
  reflection check - that check only proved the hook exists and is overridable. This remains open;
  §11 below is how this sprint makes it answerable.

============================================================
3. EXPORT STATE MODEL
============================================================

Two small, deliberately separate enums - not one combined state machine:

- **`DatasetState`** (pre-existing, unchanged: `Idle/Collecting/Debug/Stopped`) - answers "is
  collection happening."
- **`ExportStatus`** (new, `Visualization/Dashboards/DatasetDashboard.cs`:
  `NotEnabled/NoData/ReadyToExport/Exported/ExportFailed`) - answers "did the last export attempt
  succeed."

These are orthogonal questions (you can be `Collecting` with `ExportStatus=ReadyToExport`, or
`Stopped` with `ExportStatus=Exported` from an earlier manual export). Cramming both into one enum
would either lose information or require meaningless combination-states. `ResolveExportStatus(bool
enableScientificDataset, ScientificDatasetCollector? collector, ExportResult? lastExportResult)` is
the single resolver both `DatasetDashboard` and `ScientificCollectionMonitorWidget` call - status
computation is never duplicated, only its presentation text/color differs per call site
(`DescribeExportStatus` vs. `DescribeExportStatusCompact`).

============================================================
4. EXPORTRESULT
============================================================

`Core/Calibration/ExportResult.cs` (new). Replaces `ExportScientificDataset()`'s previous ambiguous
`string` return:

```
ExportOutcome Outcome        (Success / NoData / Failed)
string OutputDirectory
string? CsvPath / JsonPath / MetadataPath / OhlcvCsvPath
DateTime ExportedAt
int RecordCount
string? ErrorMessage
string? ErrorType
ScientificDatasetSession? Session   (populated only on Success, backward compat)
```

Three factories: `NoData(outputDirectory)`, `Failed(outputDirectory, exception)`,
`From(session)` (success). Every field is populated from a real attempt - nothing guessed.

============================================================
5. SCIENTIFICDATASETEXPORTER (testability boundary)
============================================================

`Core/Calibration/ScientificDatasetExporter.cs` (new). `IQIAIndicator` cannot be instantiated in
any test (derives from ATAS's `Indicator` base class), so the catch-and-record-outcome logic was
extracted into this static, ATAS-independent class:

```csharp
public static ExportResult Export(collector, writer, outputDirectory, sessionId, startTime, endTime)
```

`IQIAIndicator.ExportScientificDataset()` is now a thin wrapper around this that also updates
`DatasetLifecycleLog` and the pre-existing `_lastScientificDatasetSession` field (backward compat
for `LastScientificDatasetSession`/`LastScientificDatasetReport`, both unchanged public properties).
This is what makes Phase 2G's TEST 3-9 possible without any ATAS dependency.

============================================================
6. IQIAINDICATOR LIFECYCLE CHANGES
============================================================

`OnCalculate`'s collection block: unchanged mapping, now also calls
`_datasetLifecycleLog.MarkAdd(DateTime.UtcNow)` on every attempt (inside the existing try, before
`Add(...)`), and `_datasetLifecycleLog.MarkDatasetEnabled(...)` once when the collector is first
created.

`ExportScientificDataset()`: rewritten per §5 - never throws, always returns a real `ExportResult`,
always updates `_lastExportResult` and lifecycle timestamps.

`OnDispose()`: rewritten -
```csharp
protected override void OnDispose()
{
    _datasetLifecycleLog.MarkOnDisposeEntered(DateTime.UtcNow);   // unconditional, first statement
    try
    {
        if (EnableScientificDataset && !_scientificDatasetAutoExportAttempted)
        {
            _scientificDatasetAutoExportAttempted = true;
            ExportScientificDataset();
        }
    }
    catch (Exception)
    {
        // last-resort safety net only - ExportScientificDataset() itself no longer throws
    }
    finally
    {
        base.OnDispose();
    }
}
```
`base.OnDispose()` still always runs, per the brief's explicit requirement.

============================================================
7. ERROR HANDLING - NO LONGER SILENT
============================================================

Before: `OnDispose()`'s `catch (Exception) {}` discarded everything - message, type, even the fact
that an attempt happened.

After: `ScientificDatasetExporter.Export`'s `catch (Exception exception)` converts the exception
into `ExportResult.Failed(outputDirectory, exception)`, preserving `exception.Message` and
`exception.GetType().Name`. `OnDispose()`'s remaining `catch` is a documented last-resort safety net
for failures *outside* that boundary (e.g. if `ScientificDatasetOutputDirectory` itself throws
constructing its value) - it still cannot let a dataset problem block `base.OnDispose()`, but it is
no longer the primary (and only) place failures were being lost.

============================================================
8. DOUBLE-EXPORT PROTECTION
============================================================

`_scientificDatasetAutoExportAttempted` (new `bool` field) guards **only** `OnDispose()`'s automatic
trigger - if ATAS calls `Dispose()`/`OnDispose()` more than once, the second call is a no-op for
export purposes. Manual re-export via `ExportScientificDataset()` remains unrestricted (deliberate -
useful for incremental checkpoints, and the brief did not ask to block it). Separately,
`ScientificDatasetExporter.Export` is idempotent by construction even without the guard: the export
filename is derived from `startTime` (fixed at session start, not `endTime`), so re-exporting the
same session overwrites the same four files rather than creating duplicates - verified by TEST 8
(§10).

============================================================
9. DASHBOARD & WIDGET CHANGES
============================================================

`DatasetDashboard.cs`:
- EXPORT section: added **Export Status** (color-coded), **Record Count**, **Last Export Time**
  (from `ExportResult`, not the old `ScientificDatasetSession`), **Last Export Error** (shows the
  real message only when `ExportStatus.ExportFailed`, otherwise "Aucune"). CSV/JSON/OHLCV
  CSV/Metadata paths now come from `ExportResult` (richer, always current) instead of the older
  `ScientificDatasetSession` (kept for `Export Duration`/`Export Size` only, unaffected).
- **Output Directory** field displays `context.DatasetOutputDirectory` verbatim - this is
  `IQIAIndicator.ScientificDatasetOutputDirectory`'s actual live value passed straight through the
  render pipeline, never reconstructed in the dashboard.
- New **LIFECYCLE (diagnostic ATAS)** section: Constructed / Dataset Enabled / First Add / Last Add
  / OnDispose Entered - directly answers the open question from §2.
- Renamed the pre-existing collector-level `"Dernière erreur"` field to `"Dernier rejet (barre)"` so
  it can no longer be confused with the new, distinct export-level `"Last Export Error"`.
- `Height` bumped 880 → 1020 (10 new field/section rows, ~146px, margin preserved proportionally).

`ScientificCollectionMonitorWidget.cs`: rewritten to call the same `DatasetDashboard.
ResolveExportStatus(...)` resolver (never duplicates the computation) and show
`Pending/Exported/Failed/No Data` per the brief's own wording, replacing the old ad hoc
`lastSession?.CSVPath is null ? "Pending" : "Exported"` check that could never distinguish a real
failure from "never tried."

`DashboardManager.cs`/`DashboardContext.cs`: updated call sites/fields only (`LastExportResult`,
`DatasetLifecycleLog` added to the context; widget call now passes `LastExportResult` +
`EnableScientificDataset` instead of the raw `DatasetSession`).

============================================================
10. TESTS
============================================================

`Tests/Calibration/ExportObservabilityTests.cs` (new), covering Phase 2G's 10 required points
(TEST 10 is a documented git-diff-level check, not a unit test - see its own comment in the file)
plus 5 bonus tests directly exercising `ResolveExportStatus`:

| # | What it covers | Test method |
|---|---|---|
| 1 | Default path resolves to `%LOCALAPPDATA%\IQIA\ScientificDataset` | `DefaultProductionPath_ResolvesToLocalAppDataIqiaScientificDataset` |
| 2 | Default path NOT under Temp/TestResults/bin/obj/repo/OneDrive | `DefaultProductionPath_IsNotUnderTempOrBuildOrRepositoryFolders` |
| 3 | Custom output directory respected | `Export_WithCustomOutputDirectory_WritesExactlyThere` |
| 4 | Export creates the expected 4 files | `Export_WithData_CreatesAllFourFiles` |
| 5 | Export writes OHLCV rows | `Export_WithData_OhlcvFileContainsRealRows` |
| 6 | Metadata contains Source/BarsReceived/BarsWritten/FirstTimestamp/LastTimestamp | `Export_WithData_MetadataContainsRequiredFields` |
| 7 | Export failure surfaced, not swallowed | `Export_WhenOutputDirectoryIsBlockedByAFile_ReturnsFailedWithRealError` |
| 8 | Double export doesn't corrupt/duplicate | `Export_CalledTwiceWithSameSession_DoesNotCorruptOrDuplicateData` |
| 9 | Empty session → NO_DATA, no fake output | `Export_WithEmptyCollector_ReturnsNoDataAndWritesNoFiles` |
| 10 | No production scientific/trading code modified | Not a unit test - see §12 (git diff) |
| bonus | `ResolveExportStatus` correctness (5 tests) | `ResolveExportStatus_*` |

TEST 7's failure is real, not simulated logic: a file is placed at the exact path `Export()` needs
as a directory, so `Directory.CreateDirectory` genuinely throws `IOException` - the test observes
the real exception being caught and converted, not a mocked failure path.

**Test hygiene (Phase 2I)**: `Tests/Calibration/TemporaryDirectory.cs` (new) is a small
`IDisposable` helper that deletes its temp folder in `Dispose()`. The two Sprint 15.17 tests that
created `%TEMP%\iqia_sprint1517_*` folders (the exact folders that caused this sprint's original
confusion) and the one pre-existing test creating an unprefixed temp folder now all use it. The 6
leftover folders from Sprint 15.17's development were removed from disk as part of this sprint - no
test in this repository writes into `%LocalAppData%\IQIA\ScientificDataset` (confirmed by TEST 2,
which asserts the production default is never under `Path.GetTempPath()`).

============================================================
11. ATAS LIFECYCLE INSTRUMENTATION
============================================================

`Core/Calibration/DatasetLifecycleLog.cs` (new) - fixed-shape, not a growing log file:

```
ConstructedAt, DatasetEnabledAt, FirstAddAt, LastAddAt,
OnDisposeEnteredAt, ExportStartedAt, ExportCompletedAt, ExportFailedAt
```

`ConstructedAt`/`DatasetEnabledAt`/`FirstAddAt`/`OnDisposeEnteredAt` are set once (first
occurrence); `LastAddAt`/`ExportStartedAt`/`ExportCompletedAt`/`ExportFailedAt` are overwritten on
each occurrence. Wired into `IQIAIndicator`'s constructor, `OnCalculate`, `ExportScientificDataset`,
and the very first line of `OnDispose()`. Surfaced in the Dataset dashboard's new LIFECYCLE section
(§9).

**This answers exactly one question, and only a real ATAS session can answer it**: after removing
IQIA Signal from a chart, does `OnDispose Entered` show a real timestamp (later than `Last Add`)?
If yes: ATAS calls `OnDispose()` on removal, confirmed. If it stays blank: it does not (at least not
in this environment/version), and Sprint 15.17's export trigger needs a different mechanism -
without inventing one, per the brief's own repeated instruction.

============================================================
12. BUILD / REGRESSION RESULTS
============================================================

```
dotnet build IQIAIndicator/IQIAIndicator.csproj → 0 Warning(s), 0 Error(s)
```

Full suite, before this sprint (Sprint 15.17's closing count): **89/89 passed, 0 failed.**
Full suite, after this sprint: **103 total (89 + 14 new `ExportObservabilityTests`).**

First full-suite run: 102/103 passed, 1 failed -
`AdfLagSelectionScaleStabilityXunitTests.RunAll` (`ADF-PERF-01: lag selection at 100 bars must not
regress to pathological cost... observed 123.05 ms/call`). This is a wall-clock performance
assertion in `Tests/GoldenDatasets/AdfLagSelectionScaleStabilityTests.cs` - part of the pre-existing
ADF statistical-model test suite (Sprint 15.2 area), untouched by this sprint and unrelated to
`Core/Calibration`/`Visualization` (confirmed by the git diff in §13). Re-ran in isolation
immediately after: **passed in 5s, well under threshold** - confirmed a load-sensitive flake from
running the 26-minute full suite concurrently with other work in this session, not a real
regression. A second full-suite run was executed for a clean confirmation (see below).

Second full-suite run (clean, no concurrent load): **103/103 passed, 0 failed.** Confirms the
first run's single failure was the load-sensitive flake diagnosed above, not a real regression.

One `CS8600` nullability warning was introduced and fixed during development (not left in the final
build - confirmed 0 warnings on the final `dotnet build`).

============================================================
13. PRODUCTION PERIMETER (git diff audit)
============================================================

**Production scientific/trading logic modified: NO.** None of `KalmanFilterModel`,
`DynamicZScoreModel`, `VolatilityModel`, `OrnsteinUhlenbeckModel`, any `HalfLife*`/`ADF*`/`KPSS*`/
`DFA*`/`SPRT*` file, `RegimeEngine`, `EvidenceFusionEngine`, `DecisionEngine`, `MethodologyEngine`,
`SignalEngine`, `TradePlanEngine`, any `EntryTrigger*` file, any Risk Engine file,
`CandidateStopDistance`/A1 formula, `SyntheticSeriesCatalog`, or any QDE-012 protocol/calibration
file appears anywhere in `git diff --stat` for this sprint.

**Production dataset/export files modified — YES, exact list:**
- `IQIAIndicator/Core/Calibration/ScientificDatasetCollector.cs` (M, Sprint 15.17's own file - only
  touched incidentally where `Errors` computation already summed both counters, unchanged this
  sprint beyond what Sprint 15.17 already introduced)
- `IQIAIndicator/Core/Calibration/ScientificDatasetRecord.cs` (M, no changes this sprint beyond
  Sprint 15.17's - listed for completeness of the file inventory, not re-touched)
- `IQIAIndicator/Core/Calibration/ScientificDatasetSession.cs` (M, no changes this sprint)
- `IQIAIndicator/IQIAIndicator.cs` (M - export/lifecycle wiring only, per §6)
- `IQIAIndicator/Visualization/Dashboards/DashboardManager.cs` (M - widget call site only)
- `IQIAIndicator/Visualization/Dashboards/DatasetDashboard.cs` (M - new fields/section, per §9)
- `IQIAIndicator/Visualization/State/DashboardContext.cs` (M - 2 new properties)
- `IQIAIndicator/Visualization/Widgets/ScientificCollectionMonitorWidget.cs` (M - status source only)
- `IQIAIndicator/Core/Calibration/ExportResult.cs` (new)
- `IQIAIndicator/Core/Calibration/ScientificDatasetExporter.cs` (new)
- `IQIAIndicator/Core/Calibration/DatasetLifecycleLog.cs` (new)

**Tests modified/created — YES:**
- `IQIAIndicator/Tests/Calibration/ScientificDatasetCollectorTests.cs` (M - temp-dir cleanup only)
- `IQIAIndicator/Tests/Calibration/ExportObservabilityTests.cs` (new)
- `IQIAIndicator/Tests/Calibration/TemporaryDirectory.cs` (new)

**Documentation — YES:**
- `IQIAIndicator/Documentation/Scientific/QDE-012_Sprint_15.17.1_Export_Observability_Report.md`
  (new, this file)

**Unrelated, pre-existing, not part of this sprint** (confirmed by content/timing, not this
sprint's own changes): `Tests/Research/StopLossCalibration/Output/campaign_summary.txt`,
`A1_vs_A2_comparison.csv`, `A1_vs_A2_ratio_analysis.csv`, `StableRegionAnalyzer.cs` (Sprint
15.14/15.15 work) - these are deterministically regenerated every time the full test suite runs
(same seeds/grids, per Sprint 15.15's own report) and were re-touched as a side effect of running
`dotnet test` in this session, not edited by this sprint.

============================================================
14. WHAT IS PROVEN
============================================================

- The production default export path was already correct before this sprint (`%LocalAppData%\IQIA\
  ScientificDataset`) - confirmed again by TEST 1/2, now regression-guarded.
- Export failures are now captured with real exception type/message rather than discarded - proven
  by TEST 7 against a genuine (not simulated) I/O failure.
- "No data collected" and "export failed" are now distinguishable from each other and from "never
  attempted" - proven by TEST 9 and the `ResolveExportStatus` tests.
- Re-triggering export does not corrupt or duplicate files/data - proven by TEST 8.
- The dashboard/widget display the live `ScientificDatasetOutputDirectory` value and a real,
  non-fabricated export status - verified by direct code reading (§9), not by rendering (no
  `RenderContext` can be constructed outside ATAS to unit-test the drawing itself - see §15).
- Zero production trading/scientific files were touched - confirmed by `git diff --stat`.

============================================================
15. WHAT REMAINS UNPROVEN
============================================================

- **ATAS OnDispose lifecycle: NOT YET VALIDATED.** No ATAS session has been run. `DatasetLifecycleLog.
  OnDisposeEnteredAt` exists and is wired correctly by code inspection, but whether ATAS actually
  sets it on indicator removal is unknown until a real session is run (§16).
- Dashboard/widget **rendering** itself (pixel layout, whether the new LIFECYCLE section fits inside
  the bumped `Height`, whether text truncates correctly at real chart widths) cannot be unit-tested -
  `OFT.Rendering.Context.RenderContext` cannot be constructed outside a running ATAS chart. The
  `Height` bump is a calculated estimate (§9), not a rendered/measured confirmation.
  This is the same category of limitation Sprint 15.17 already flagged for `IQIAIndicator` itself.
- Whether `OnCalculate` can fire more than once for a still-forming bar remains unconfirmed from
  source (Sprint 15.17 finding, unchanged) - `BarsReceived` vs. `BarsWritten` in a real session
  would settle this, same as before.
- The `_scientificDatasetAutoExportAttempted` guard's real-world necessity (does ATAS actually call
  `Dispose()` more than once in practice?) is unverified - the guard is a safe no-op if it never
  fires twice, so this is a precaution, not a confirmed requirement.

============================================================
16. EXACT ATAS VALIDATION PROCEDURE
============================================================

1. Open ATAS.
2. Load **IQIA Signal** onto a chart.
3. Select any instrument, any timeframe.
4. Enable **"Activer le dataset scientifique"** (`EnableScientificDataset`).
5. Set **"Dashboard actif"** to **Dataset**.
6. Open Market Replay, start it, let 100+ bars pass.
7. Watch the Dataset dashboard's new **LIFECYCLE** section while this runs: `Constructed` and
   `Dataset Enabled` should already show timestamps; `First Add`/`Last Add` should be advancing.
   `OnDispose Entered` should still read `N/A`.
8. Stop Replay.
9. **Remove IQIA Signal from the chart.**
10. If you can still see the dashboard for an instant before removal completes, check whether
    `OnDispose Entered` gets a timestamp. Otherwise, proceed to the files directly (removal itself
    may be too fast to observe on-screen).
11. Go to `%LOCALAPPDATA%\IQIA\ScientificDataset\`.
12. Check for the four `ScientificDataset_{Symbol}_{TimeFrame}_{timestamp}*` files.
    - **If they exist and `BarsWritten > 100`, `FirstTimestamp < LastTimestamp`, `Source: "ATAS"`
      in the `_metadata.json`**: `OnDispose()` fired correctly - report back these exact values.
    - **If no files exist**: `OnDispose()` did not fire as expected (or fired but hit the
      last-resort catch) - report back exactly what you observed (dashboard state before removal,
      whether any files partially exist, any ATAS-side error dialog). Do not re-run a synthetic
      workaround - this is precisely the signal Sprint 15.17.1 was built to surface honestly.

============================================================
17. VERDICT
============================================================

**READY_FOR_ATAS_LIFECYCLE_VALIDATION** - contingent on the full regression suite finishing green
(§12). Build is clean (0 errors, 0 warnings). All new observability code is unit-tested and passing.
No ATAS session has been run; per this sprint's own rule, that is not claimed. Waiting on you to
execute §16 and report back the real counters/timestamps observed.
