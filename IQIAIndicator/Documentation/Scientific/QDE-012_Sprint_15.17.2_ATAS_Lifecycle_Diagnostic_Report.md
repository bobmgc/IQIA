============================================================
QDE-012 — SPRINT 15.17.2 — PERSISTENT ATAS LIFECYCLE
DIAGNOSTICS
============================================================

Date: 2026-08-14
Protocol: QDE-012 v1.1 (locked), unchanged by this sprint.
Scope: INFRASTRUCTURE ONLY. No calibration, no k selection, no Risk Engine, no scientific-model or
trading-logic changes. This sprint makes the ATAS lifecycle diagnostic SURVIVE the destruction of
the indicator that produces it - it does not run ATAS, and it does not claim OnDispose() is called.

============================================================
1. WHY THE DASHBOARD IS NOT ENOUGH AFTER DETACHMENT
============================================================

Sprint 15.17.1 added `DatasetLifecycleLog` and a Dataset dashboard section showing it live -
Constructed / Dataset Enabled / First Add / Last Add / OnDispose Entered - specifically to answer
whether ATAS calls `OnDispose()` when a user removes IQIA Signal from a chart. But that log lives
only in `IQIAIndicator`'s own memory, and the dashboard that renders it is drawn by that same
`IQIAIndicator` instance. The moment the user performs the exact action being diagnosed - removing
the indicator - both the instance and its dashboard disappear together. There is no way to look at
the dashboard *after* the event it was built to observe. Any answer has to come from something that
outlives the instance: a file on disk.

============================================================
2. WHY A PERSISTENT SNAPSHOT IS NECESSARY
============================================================

A file written only inside `OnDispose()` itself would prove nothing new - if `OnDispose()` never
fires, no such file would exist either way, and the question ("did it fire?") would remain
unanswerable from the missing evidence alone (missing file could mean "never fired" or "fired but
crashed before writing"). The fix is to write the file **starting from session start**, updated
incrementally as events happen, so that:
- If the file exists at all after a session, `EnableScientificDataset` was turned on at some point -
  proven, not inferred.
- If the file's last recorded event is `LastAdd` with `OnDisposeEnteredAt` still `null`, that is
  itself direct, positive evidence that `OnDispose()` was never reached (or reached but crashed
  before the very first thing it does, which is now writing this exact fact to disk).
- If `OnDisposeEnteredAt` IS populated, ATAS provably called the hook, regardless of what happened
  to the indicator or dashboard afterward.

============================================================
3. LIFECYCLE FILE FORMAT
============================================================

New: `Core/Calibration/DatasetLifecycleSnapshot.cs` (record) +
`Core/Calibration/DatasetLifecycleSnapshotWriter.cs` (writer).

Path: `{ScientificDatasetOutputDirectory}\ScientificDataset_{Symbol}_{TimeFrame}_{stamp}_lifecycle.json`
- same `ScientificDataset_{Symbol}_{TimeFrame}_{stamp}` prefix the four export files from the same
  session already use (`ScientificDatasetSessionWriter.BuildFilePrefix`, extracted and shared this
  sprint rather than duplicated), so all five files for a session sort together in Windows Explorer.
  `stamp` is derived from `startTime` (session start), not `endTime` - known from the very first
  bar, unlike the export timestamp which only exists after a successful export.

Content:
```json
{
  "SessionId": "...", "Symbol": "...", "TimeFrame": "...",
  "ConstructedAt": "...", "DatasetEnabledAt": "...",
  "FirstAddAt": "...", "LastAddAt": "...",
  "OnDisposeEnteredAt": null,
  "ExportStartedAt": null, "ExportCompletedAt": null, "ExportFailedAt": null,
  "BarsReceived": 0, "BarsWritten": 0,
  "LastError": null,
  "SnapshotWrittenAt": "..."
}
```

Every event field is nullable and reflects only what has actually happened by the time of the last
write - the writer and record never assert an interpretation ("OnDispose was not called"), only
record observations (`OnDisposeEnteredAt: null`). Interpretation is left to whoever reads the file
after a real session, per the brief's explicit instruction.

**Atomicity (Phase 6, TEST 8)**: each write serializes to a `.tmp` file, then
`File.Move(tempPath, path, overwrite: true)` replaces the real file in one filesystem operation. A
crash or concurrent read mid-write can only ever observe the previous complete, valid snapshot or
the new one - never a half-written, unparsable file.

**Never throws**: `DatasetLifecycleSnapshotWriter.TryWrite` catches every exception internally and
reports success/failure + a real error message via `out` parameters (Phase 5) - it cannot break
`OnCalculate`'s hot path or block `OnDispose()`/`base.OnDispose()`.

============================================================
4. EVENTS RECORDED & WHEN THEY ARE WRITTEN
============================================================

Reuses `DatasetLifecycleLog` unchanged (Sprint 15.17.1, already tested) as the single in-memory
source of truth - this sprint adds persistence on top of it, not a second logging mechanism.

| Event | Set in `DatasetLifecycleLog` | Snapshot written |
|---|---|---|
| `ConstructedAt` | `IQIAIndicator()` constructor | Not written yet (no collector exists) |
| `DatasetEnabledAt` | `OnCalculate`, collector first created | **Immediately** - file now exists |
| `FirstAddAt`/`LastAddAt` | `OnCalculate`, every collection attempt | Every 50 attempts (`LifecycleSnapshotWriteEveryNBars`) - throttled to avoid per-bar disk I/O on the ATAS calculation path |
| `OnDisposeEnteredAt` | `OnDispose()`, first statement | **Immediately after**, before any export is attempted - the ordering the brief's Phase 4 requires |
| `ExportStartedAt` | `ExportScientificDataset()`, first statement | Immediately after |
| `ExportCompletedAt` / `ExportFailedAt` | `ExportScientificDataset()`, after the actual export attempt | Immediately after (final state for the export attempt) |

`BarsReceived`/`BarsWritten` (from `ScientificDatasetCollector.TotalAddAttempts`/`RecordsAccepted`)
and `LastError` (from `_lastExportResult?.ErrorMessage`) are captured fresh at every snapshot write,
not stored inside `DatasetLifecycleLog` itself - keeping that class unchanged and its own Sprint
15.17.1 tests still valid untouched.

============================================================
5. TESTS
============================================================

`Tests/Calibration/DatasetLifecycleSnapshotTests.cs` (new), Phase 6's 10 required tests plus one
bonus (never-throws-on-blocked-path, mirroring Sprint 15.17.1's TEST 7 pattern):

| # | What it covers | Test method |
|---|---|---|
| 1 | Snapshot construction/persistence | `TryWrite_WithFreshLog_CreatesReadableSnapshotFile` |
| 2 | DatasetEnabled persisted | `TryWrite_WithDatasetEnabled_PersistsTimestamp` |
| 3 | FirstAdd/LastAdd persisted distinctly | `TryWrite_WithMultipleAdds_PersistsFirstAndLastDistinctly` |
| 4 | OnDisposeEntered before ExportStarted (mechanism) | `TryWrite_CalledAfterOnDisposeEntered_ThenAfterExportStarted_ReflectsBothStagesInOrder` |
| 5 | ExportCompleted persisted after success | `TryWrite_WithExportCompleted_PersistsTimestamp` |
| 6 | ExportFailed contains the real error | `TryWrite_WithExportFailedAndError_PersistsErrorMessage` |
| 7 | No-OnDispose lifecycle stays valid, explicit null | `TryWrite_WithoutOnDisposeEver_RemainsValidWithExplicitNull` |
| 8 | Two successive writes never corrupt the JSON | `TryWrite_CalledTwiceInSuccession_LeavesValidJsonEachTime` |
| 9 | No scientific/trading code modified | Not a unit test - see §7 (git diff), same as Sprint 15.17.1's own TEST 10 |
| 10 | Tests use an isolated temp dir and clean it up | `TemporaryDirectory_Dispose_RemovesTheDirectoryItCreated` |
| bonus | Write failure never throws | `TryWrite_WhenOutputDirectoryIsBlockedByAFile_ReturnsFalseWithErrorAndNeverThrows` |

TEST 4's actual ordering GUARANTEE (that `IQIAIndicator.OnDispose()` calls `WriteLifecycleSnapshot()`
before `ExportScientificDataset()`) lives in `IQIAIndicator.cs` itself and cannot be unit-tested
(the class can't be instantiated outside ATAS) - verified by direct code inspection instead (§4
above shows the exact call sequence). What TEST 4 proves is the mechanism that ordering guarantee
depends on: the writer correctly persists each intermediate state independently.

All tests use `Tests/Calibration/TemporaryDirectory.cs` (Sprint 15.17.1) for isolation and cleanup -
no test in this repository writes into `%LOCALAPPDATA%\IQIA\ScientificDataset`.

============================================================
6. BUILD / REGRESSION RESULTS
============================================================

```
dotnet build IQIAIndicator/IQIAIndicator.csproj → 0 Warning(s), 0 Error(s)
```

New test file in isolation: **10/10 passed, 0 failed, 0 warnings.**

Full suite, before this sprint (Sprint 15.17.1's closing count): **103/103 passed, 0 failed.**
Full suite, after this sprint: **113/113 passed, 0 failed, 0 skipped** (103 + 10 new
`DatasetLifecycleSnapshotTests`), 16m20s. Zero regressions.

============================================================
7. PRODUCTION PERIMETER (git diff audit)
============================================================

**Scientific/trading logic modified: NO.** No file under `Engine/` appears anywhere in
`git status`/`git diff` for this sprint (checked explicitly, not assumed) - confirmed no
`KalmanFilterModel`, `DynamicZScoreModel`, `VolatilityModel`, `OrnsteinUhlenbeckModel`, `HalfLife*`,
`ADF*`, `KPSS*`, `DFA*`, `SPRT*`, `RegimeEngine`, `EvidenceFusionEngine`, `DecisionEngine`,
`MethodologyEngine`, `SignalEngine`, `TradePlanEngine`, `EntryTrigger*`, Risk Engine, A1/A2/D
candidate formula, or QDE-012 protocol/calibration file was touched.

**Dataset/export diagnostic modified: YES, exact list:**
- `IQIAIndicator/Core/Calibration/ScientificDatasetSession.cs` (M - extracted `BuildFilePrefix` as
  a shared internal static method; no behavior change to the four existing export files)
- `IQIAIndicator/IQIAIndicator.cs` (M - `WriteLifecycleSnapshot()` added + wired at 4 call sites,
  per §4; no other change)
- `IQIAIndicator/Visualization/State/DashboardContext.cs` (M - 2 new properties)
- `IQIAIndicator/Visualization/Dashboards/DatasetDashboard.cs` (M - 2 new fields, `Height` bumped)
- `IQIAIndicator/Core/Calibration/DatasetLifecycleSnapshot.cs` (new)
- `IQIAIndicator/Core/Calibration/DatasetLifecycleSnapshotWriter.cs` (new)
- `IQIAIndicator/Core/Calibration/ScientificDatasetCollector.cs`, `ScientificDatasetRecord.cs`,
  `Visualization/Dashboards/DashboardManager.cs`, `Visualization/Widgets/
  ScientificCollectionMonitorWidget.cs` (M) - listed by `git status` from Sprint 15.17.1, not
  re-touched this sprint.

**Tests modified/created — YES:**
- `IQIAIndicator/Tests/Calibration/DatasetLifecycleSnapshotTests.cs` (new)
- `IQIAIndicator/Tests/Calibration/ScientificDatasetCollectorTests.cs` (M, Sprint 15.17.1, not
  re-touched)

**Documentation — YES:**
- `IQIAIndicator/Documentation/Scientific/QDE-012_Sprint_15.17.2_ATAS_Lifecycle_Diagnostic_Report.md`
  (new, this file)

============================================================
8. WHAT REMAINS TO BE VERIFIED IN ATAS
============================================================

Nothing about this sprint changes what still needs a real session to confirm - it only changes
WHERE the answer can be read from afterward:

1. **ATAS OnDispose lifecycle: still NOT proven.** This sprint never claims otherwise anywhere in
   its code or comments. The persistent `*_lifecycle.json` file is now the durable evidence a real
   session will leave behind either way.
2. Whether `OnCalculate` can fire more than once per still-forming bar (unconfirmed since Sprint
   15.17) - `BarsReceived` vs. `BarsWritten` in the lifecycle/metadata files would still settle this.
3. Dashboard rendering itself (the new "Lifecycle File"/"Lifecycle Write Error" fields, the `Height`
   bump) cannot be unit-tested - `RenderContext` cannot be constructed outside a running ATAS chart.

============================================================
9. UPDATED FINAL ATAS VALIDATION PROCEDURE
============================================================

1. Open ATAS.
2. Load IQIA Signal.
3. Enable "Activer le dataset scientifique".
4. Set Dashboard actif = Dataset (optional - useful while attached, not required afterward).
5. Launch Market Replay.
6. Let at least 100 bars pass.
7. Stop Replay.
8. Remove IQIA Signal from the chart.
9. Open, in Windows Explorer (no terminal required):
   `%LOCALAPPDATA%\IQIA\ScientificDataset\`
10. Look for `*_lifecycle.json`.
11. Open it in any text editor and read the last populated event:
    - `OnDisposeEntered` + `ExportCompleted` both set → lifecycle fired and export succeeded.
    - `OnDisposeEntered` + `ExportFailed` set → lifecycle fired, export failed (`LastError` has why).
    - `OnDisposeEntered` set, no `ExportStarted`/`Completed`/`Failed` → `OnDispose` fired but
      `EnableScientificDataset` was somehow off by then, or something failed before the export call.
    - `OnDisposeEntered` still `null` → `OnDispose()` was not reached - this is itself the answer,
      not a test failure.
12. Also check `*_ohlcv.csv`, `*_metadata.json`, `*.csv`, `*.json` - same directory, same prefix.

============================================================
10. VERDICT
============================================================

**READY_FOR_REAL_ATAS_LIFECYCLE_TEST** - build is clean (0 errors, 0 warnings), all new persistence
code is unit-tested and passing, production perimeter confirmed untouched by `git diff`. No ATAS
session has been run by this sprint; per its own rule, `OnDispose` being called by ATAS is not
claimed anywhere in this report. Waiting on a real Market Replay run and the resulting
`*_lifecycle.json` to answer that question for the first time with durable, on-disk evidence.
