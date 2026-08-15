============================================================
QDE-012 — SPRINT 15.16 — REAL MARKET DATA ACQUISITION
& OUT-OF-SAMPLE VALIDATION
============================================================

Date: 2026-08-13
Protocol: QDE-012 v1.1 (locked), unchanged by this sprint.
Scope: SCIENTIFIC VALIDATION GATE ONLY. No calibration, no Risk Engine, no production Stop
Loss, no model changes. This sprint asks a single question — does A1's synthetic-data
robustness survive contact with real market data? — and answers it honestly: it could not
be tested, because no real market data exists anywhere in this project.

============================================================
1. VERDICT
============================================================

**INSUFFICIENT REAL DATA.**

No real historical market OHLC data exists anywhere in this repository, in any provider
integration reachable offline, or in any exported capture from the one live-data bridge
that does exist. Per this sprint's own Section 0 rule, the campaign was not run and no
result was fabricated or substituted with synthetic data. This is a scientific outcome,
not a failure of process: Sprints 15.10–15.15 already flagged this gap five times in a
row without it being closed, and this sprint closes the question definitively rather than
repeating the flag a sixth time.

============================================================
2. FACTS
============================================================

Established by direct repository inspection (full detail: `RealMarket/real_market_data_inventory.csv`).

- No data files (csv/json/parquet/hdf5/db/sqlite) exist anywhere in the repository other
  than the synthetic campaign's own `Tests/Research/StopLossCalibration/Output/` artifacts
  (29 CSVs + 4 txt), which contain aggregate per-(candidate,dataset,split,k) statistics —
  no OHLC, no timestamps, no instrument identity.
- No folder named `Data/`, `MarketData/`, or `Historical/` exists anywhere in the tree.
- No source-code reference to NinjaTrader, Databento, Binance, Polygon.io, or Yahoo
  Finance exists anywhere in this repository (source or documentation).
- The only external market-facing integration is **ATAS**, a third-party desktop trading
  platform. It provides live chart rendering (`Infrastructure/ATAS/*`) and a live,
  bar-by-bar `MarketContext` builder (`Core/MarketContextBuilder.cs`) fed by ATAS's
  `IndicatorCandle` type. It requires ATAS installed locally (referenced only via local
  `HintPath` DLLs, no NuGet/network SDK) and a running chart with a live or replay data
  subscription. There is no historical-data export tool.
- A live-capture bridge does exist and is unused: `Core/Calibration/ScientificDatasetCollector.cs`
  (toggled by `EnableScientificDataset` on the live indicator) can export already-computed
  scientific metrics — not raw OHLC — to `%LocalAppData%\IQIA\ScientificDataset\` while
  ATAS runs live. No exported session file exists anywhere in this repository or on
  record for this audit.
- `.gitignore` is the stock `dotnet new gitignore` template — no pattern excludes a local
  data cache, meaning no such cache was ever set up for this project.
- `QDE-012_StopLoss_Calibration_Protocol.md` §12 states verbatim, at protocol-lock time
  (before Sprint 15.10): *"no historical real-market OHLC data exists anywhere in this
  project."* Every sprint report from 15.11 through 15.15 repeats "Real market
  validation: NOT PERFORMED" as a standing, unactioned recommendation.
- No instrument ticker (ES, NQ, EURUSD, BTCUSD, etc.) is hardcoded anywhere in the
  repository; `InstrumentInfo.Symbol` is populated only live from ATAS at runtime.
- The five QDE-012 harness components (`CalibrationEntryBuilder`, `BarMetricsComputer`,
  `OutcomeSimulator`, `StopLossEvaluator`, `StableRegionAnalyzer`) all operate on a
  generic `IReadOnlyList<decimal>` (or already-derived intermediate results) with no hard
  dependency on `SyntheticSeriesCatalog`/`CampaignDatasetCatalog`. This is now
  structurally regression-guarded by `RealMarketGateTests.HarnessAcceptsArbitraryNonCatalogSeries_EndToEnd`,
  added this sprint.
- `KalmanFilterModel`, `DynamicZScoreModel`, `VolatilityModel`, `OrnsteinUhlenbeckModel`,
  and `HalfLifeEvidence` are all stateless, batch computations over whatever slice of the
  series they are given — no cross-bar caching in `ScientificModelRegistry.Resolve`
  (fresh instances per call). This is the structural reason `BarMetricsComputer`'s
  look-ahead boundary (only ever reading `series[0..barIndex]`) holds, and is already
  regression-tested by the pre-existing
  `StopLossCalibrationPocTests.AssertLookAheadSafety_TruncatingFutureBarsProducesIdenticalMetrics`.
- The research harness has no timezone-normalization layer: `BarMetricsComputer` stamps
  every bar's `MarketContext.Timestamp` with `DateTime.UtcNow` — a placeholder that works
  only because synthetic series carry no real timestamps. `MarketContextBuilder` (the
  production/live path) passes ATAS's `IndicatorCandle.Time` through unconverted, with no
  `TimeZoneInfo` handling. Neither path has real-timestamp handling ready to reuse today.

============================================================
3. ANALYSIS
============================================================

The absence of real data is not ambiguous or partial — it is total, and corroborated from
four independent angles that would each individually be sufficient: an exhaustive
filesystem search, a full-repository grep for every named provider, the codebase's own
explicit, dated self-assessment written before the synthetic campaign even began, and the
unanimous, repeated confirmation of that self-assessment across five subsequent sprint
reports that never contradicted it. There is no scenario consistent with the evidence in
which real data is "hiding" somewhere in this repository that a deeper search would find.

Separately, the audit found the harness itself is **not** the blocker. `CalibrationEntryBuilder`,
`BarMetricsComputer`, `OutcomeSimulator`, and `StopLossEvaluator` all consume a bare
`decimal[]` price series with no awareness of where it came from; a real-data loader
producing that same shape could be plugged in without touching any of these four classes.
The gap is purely upstream — data acquisition — not the calibration machinery downstream
of it. This is a meaningfully different (and better) position than "the harness isn't
ready," and is why this sprint could still produce a useful, actionable outcome (the data
format spec in §7 below) rather than a purely negative one.

The one caveat surfaced by this analysis and not previously documented: the harness's
`DateTime.UtcNow` timestamp placeholder and the production path's un-converted
`IndicatorCandle.Time` passthrough mean **timezone/session handling does not exist yet**
anywhere in this codebase. This is not a defect introduced by this sprint — it was never
needed while every series was synthetic and timeless — but it is new information a future
real-data sprint must plan for; it was not previously called out in prior QDE-012 reports.

============================================================
4. INTERPRETATION
============================================================

Five consecutive sprints (15.10–15.15) built and refined an increasingly rigorous
synthetic calibration methodology for A1 without ever closing the real-data gap flagged
at protocol-lock time. This sprint's contribution is to stop treating that gap as a
recurring footnote and instead resolve it explicitly: confirm it is real (not an
oversight or a missed search), confirm the harness does not itself need rework to consume
real data once available, and specify exactly what would unblock the next attempt (§7).
Nothing about A1's synthetic-data performance (Sprint 15.14's `ROBUST K REGION FOUND —
QUALIFIED`) is strengthened, weakened, or otherwise touched by this sprint — that verdict
remains exactly what it was, scoped exactly as it always was: a synthetic-data result.

============================================================
5. ANSWERS TO THE MANDATED QUESTIONS (protocol §26)
============================================================

1. **Does A1's synthetic robust region exist on real market data?** Not tested — no real
   data exists to test it against. Not "no," not "yes" — undetermined.
2. **Is ReversionRate(k)'s shape comparable to synthetic?** Not tested.
3. **Does the k∈[0.5,10] region remain stable?** Not tested.
4. **Does the k∈[2.5,10] region remain stable?** Not tested.
5. **Does behavior change by instrument?** Not tested; also moot today — the repository
   commits to zero instruments (§2).
6. **Does behavior change by timeframe?** Not tested; also moot today — no timeframe
   concept exists in the harness at all (series are bar-indexed, timeframe-agnostic).
7. **Do TRAIN results survive VALIDATION?** Not tested (no real TRAIN/VALIDATION exists).
8. **Does TEST confirm without modifying the region?** Not tested.
9. **Are there regimes where A1 fails systematically?** Not tested on real data; on
   synthetic data, Sprint 15.14 already found `Trending` to be structurally non-reverting
   (excluded as `NoSignal`) — whether a real-market analogue of persistent trending
   behaves the same way is exactly the kind of question this sprint cannot yet answer.
10. **Do the synthetic results look optimistic?** Cannot be assessed without a real-data
    comparison point. Flagged as an open question for the sprint that eventually runs one.
11. **Is there enough evidence to consider a future production calibration?** No — not
    because A1 performed poorly (it has not been tested on anything but synthetic data),
    but because the out-of-sample validation this protocol requires before that
    consideration is even opened has not been performed. This answer is independent of
    any trading decision, per protocol §26's own instruction.

============================================================
6. LIMITATIONS
============================================================

- This audit is a repository-only, offline, read-only search. It cannot rule out real
  data existing outside this repository (e.g., on the user's machine, in a cloud
  location, or behind provider credentials not present here) — only that none exists
  *inside* this repository or in any location this sprint had access to.
- The ATAS live-capture bridge (`ScientificDatasetCollector`) was evaluated by reading
  its code, not by running it — doing so would require the ATAS desktop platform
  installed and running live/replay against a real chart, outside this sprint's scope.
- The harness's "no hard coupling to synthetic generators" finding (§2) was verified
  structurally (a hand-authored, non-catalog fixture series runs end-to-end through the
  full pipeline — see `RealMarketGateTests.cs`) but not against actual real market data,
  since none exists; a real loader may still surface issues this structural test cannot
  anticipate (e.g., real gaps, halts, or extreme outliers absent from any hand-authored
  fixture).
- The timezone/session gap noted in §3 is reported descriptively; this sprint does not
  design or implement a fix, per the mandate not to build ahead of the data that would
  make such a fix testable.

============================================================
7. REQUIRED DATA FORMAT (to unblock a future sprint)
============================================================

To run the campaign this sprint could not run, a future sprint needs, at minimum:

- **Series shape**: one file per (instrument, timeframe) pair, containing a
  chronologically ordered, gap-documented sequence of bars. The harness's actual
  consumption point (`CalibrationEntryBuilder.BuildEntries`) only requires a `decimal[]`
  close-price series, but the raw file should retain full OHLC + volume + timestamp so
  quality auditing (§12 of the protocol brief) and timezone handling are possible before
  reduction to that shape.
- **Minimum length**: at least `WarmupBars (30) + Horizon (40) + enough bars for a
  meaningful chronological TRAIN/VALIDATION/TEST split` — in practice several hundred
  bars per split, i.e. materially more than a few hundred bars total. Sprint 15.10's
  synthetic campaign used 600 bars per series per split; real series should match or
  exceed that per split, not in aggregate.
- **Timestamp + timezone**: every bar must carry an explicit, documented timestamp with a
  named timezone/session convention (e.g., exchange-local vs. UTC, and whether overnight/
  session-break bars are included or excluded). This does not exist in the harness today
  (§3) and must be decided explicitly, not defaulted.
- **Provenance**: an identifiable provider and a reproducible acquisition method (an
  exported file with a documented source, or a scriptable/credentialed API pull) — not a
  single manual, undocumented capture that could not be regenerated or audited.
- **Quality baseline**: the dataset should be auditable against every dimension in
  `real_market_data_quality.csv`'s schema (gaps, duplicates, nulls, non-monotonic
  timestamps, outliers, contract-roll events, sessions, overnight periods) before any
  campaign runs against it.
- **Diversity (best-effort, not mandatory)**: protocol §4 asks for cross-asset diversity
  (index/future, FX, commodity, crypto) if available; a single real instrument is
  sufficient to proceed, with diversity limitations reported explicitly, per §4's own
  fallback rule.

============================================================
8. STRUCTURAL TESTS ADDED THIS SPRINT
============================================================

`Tests/Research/StopLossCalibration/RealMarket/RealMarketGateTests.cs`
(`RealMarketGateXunitTests` wrapper), 6 tests, all passing:

| # | Test | Verifies |
|---|---|---|
| 1 | `Test1_HorizonStillLockedAt40` | `CampaignGrids.Horizon == 40` (protocol §9) |
| 2 | `Test2_KGridStillLocked_40PointsQuarterStep` | 40-point k-grid, 0.25 step, [0.25,10.0] bounds (protocol §10) |
| 3 | `Test3_A1Formula_ScaleRatioConsistency` | A1 `StopDistance = k*InnovationStd` exactly, and `StopDistance/InnovationStd == k` (protocol §18) across multiple k/InnovationStd combinations |
| 4 | `Test4_A1_BuySellSymmetry` | BUY: `SL = Entry - k*InnovationStd`; SELL: `SL = Entry + k*InnovationStd`; equal magnitude both sides |
| 5 | `Test5_HarnessAcceptsArbitraryNonCatalogSeries_EndToEnd` | The full pipeline (`CalibrationEntryBuilder` → `BarMetricsComputer`/`OutcomeSimulator` → `CandidateStopDistance` → `StopLossEvaluator`) runs on a hand-authored, non-`SyntheticSeriesCatalog` series — proving no hidden coupling to the synthetic generators |
| 6 | `Test6_RealMarketGateArtifactsAreHonest` | Required audit artifacts exist; the summary text records the actual `INSUFFICIENT REAL DATA` verdict; none of the 9 campaign-result filenames that would only exist after a real campaign ran are present — guards against a future silent fabrication |

**Full suite result**: 68/68 passed, 0 failed (67 pre-existing + this sprint's new
`RealMarketGateXunitTests`). **Zero regressions.**

Pre-existing, still-relevant coverage this sprint deliberately did not duplicate:
`StopLossCalibrationPocTests.AssertLookAheadSafety_TruncatingFutureBarsProducesIdenticalMetrics`
(look-ahead/truncation equivalence) and `ConformanceTests` (grid/dataset/split/scale/
horizon conformance, source immutability, D1/D2 formula and BUY/SELL symmetry) already
cover those invariants for the synthetic harness; this sprint's tests are scoped to what
is new: A1-specific formula/symmetry checks, data-source agnosticism, and this sprint's
own gate-honesty guard.

============================================================
9. OUTPUTS PRODUCED
============================================================

Per protocol §20, only the outputs that could be honestly produced without fabricating a
result were created:

- `RealMarket/real_market_data_inventory.csv` — exhaustive search log (§2 above)
- `RealMarket/real_market_data_quality.csv` — all dimensions `NOT_APPLICABLE`, `OverallQuality=BLOCKED`
- `RealMarket/A1_real_calibration_summary.txt` — BLOCKED summary, verdict, gate-outcome table
- `RealMarket/RealMarketGateTests.cs` + `RealMarket/RealMarketOutputPaths.cs` — structural tests
- `XunitWrappers/RealMarketGateXunitTests.cs` — xunit wrapper
- This report

**Deliberately not produced**: `A1_real_k_grid_results.csv`, `A1_real_train_validation.csv`,
`A1_real_test_confirmation.csv`, `A1_real_stable_regions.csv`, `A1_real_dataset_analysis.csv`,
`A1_real_scale_analysis.csv`, `A1_real_regime_analysis.csv`, `A1_synthetic_vs_real_comparison.csv`,
`A1_real_sensitivity.csv`. Each of these is a campaign-result artifact; producing them —
even empty or placeholder — with no real campaign behind them would misrepresent the
sprint's actual outcome. `RealMarketGateTests.Test6` regression-guards their absence.

============================================================
10. PRODUCTION PERIMETER
============================================================

**Zero production files modified.** All changes in this sprint are confined to:
- `Tests/Research/StopLossCalibration/RealMarket/` (new folder)
- `Tests/XunitWrappers/RealMarketGateXunitTests.cs` (new file)
- `Documentation/Scientific/QDE-012_Sprint_15.16_Real_Market_Validation_Report.md` (this file)

No file under `Core/`, `Engine/`, `Infrastructure/`, `Visualization/`, or the ATAS
indicator entry point was read for modification purposes (only for audit/reading, per
protocol §22). Confirmed by `git status --short` / `git diff --stat` before and after
this sprint (see git log for this session).

============================================================
11. NEXT STEPS
============================================================

This sprint does not select a next sprint number or scope beyond what the protocol
already implies: real data acquisition (per §7's format spec) is the single blocking
prerequisite for any future real-market validation attempt. No k has been selected, no
production file has been touched, and no trading readiness claim is made — per protocol
§28, none of that is in scope regardless of how the synthetic results have looked so far.

============================================================
12. FINAL VERDICT
============================================================

**INSUFFICIENT REAL DATA**
