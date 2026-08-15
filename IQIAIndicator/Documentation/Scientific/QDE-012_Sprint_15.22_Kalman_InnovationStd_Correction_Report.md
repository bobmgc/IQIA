============================================================
QDE-012 — SPRINT 15.22 — KALMAN INNOVATIONSTD CORRECTION &
WINDOW SELECTION
============================================================

Date: 2026-08-15
Protocol: QDE-012 v1.1 (locked), unchanged by this sprint.
Scope: `KalmanFilterModel.cs` only, plus its direct unit tests. No `k`, no A1/A2/D1/D2 threshold, no
Risk/Decision/Signal/Strategy/Dashboard/Regime Engine file touched.

============================================================
1. BEFORE
============================================================

`KalmanFilterModel.EstimateMeasurementNoise`/`EstimateInitialVariance` computed variance over
`context.MarketContext.History` **in full** — every observation since the start of the series, never a
bounded window. Sprint 15.21 proved this makes `InnovationStd` grow with total history length whenever
the underlying price process is non-stationary: 3.79 → 40.65 (×10.8) across the real Sprint 15.19 ES/M5
capture (Spearman(index, InnovationStd)=0.971), and reproduced on a synthetic `RandomWalk` of the same
length (5.02 → 42.84), while `WhiteNoise`/`MeanRevertingOu`/`AR1(0.95)` (stationary by construction)
stayed flat. Root cause confirmed by this sprint's own supporting experiment (Sprint 15.21 §10):
non-stationarity of the underlying process, not sample length per se, and not outlier sensitivity
(MAD/classical ratio ≈0.96–1.02 at N=40).

============================================================
2. DEFECT
============================================================

`A1 StopDistance = k × InnovationStd` inherited an ever-growing, session-length-dependent scale.
`D1`/`D2` inherit it twice (`InnovationStd` directly, and via `DynamicZScore = (price-mean)/InnovationStd`
as `z0`). `A2 = k × CurrentVolatility` is independent — `VolatilityModel.cs` never calls
`KalmanFilterModel` (confirmed by direct code read, unchanged this sprint).

============================================================
3. CORRECTION
============================================================

`KalmanFilterModel.cs` now bounds the observations it uses internally to the most recent
`ObservationWindowSize` (=20) — or all available observations if fewer than 20 exist (documented warmup
behavior, unchanged from pre-15.22 for short histories). `context.MarketContext.History` itself, and
every other consumer of it, are untouched — the window is entirely internal to this one class. A new,
additive, diagnostic-only metric `ObservationsUsed` was added to the result dictionary so the window
boundary is independently observable/testable, not just implied.

Two pre-existing, secondary mathematical imprecisions were found while retracing the algorithm line by
line (documented in `KalmanFilterModelWindowTests.cs`'s header and the Sprint 15.21 report, **not**
changed this sprint — out of scope, no independent justification was made to touch them):
- `innovationAtCurrent` is the POSTERIOR residual `(1-K_T)×innovation_T`, not the textbook pre-update
  innovation used to compute the Kalman gain.
- `InnovationStd = sqrt(P_T(posterior) + R)` uses the posterior covariance, not `P_{T-1}+Q` (the
  predicted covariance a canonical one-step innovation-variance formula would use).

============================================================
4. N CANDIDATES / METRICS / N RECOMMENDED
============================================================

8-candidate empirical sweep (N=10/15/20/25/30/40/60/80) on the real Sprint 15.19 ES/M5 capture (4179
common bars), 3 `RandomWalk` seeds (42/43/44, length 4279), and 3 stationary families (`WhiteNoise`,
`MeanRevertingOu(0.5)`, `AR1(0.95)`, length 4279). Full table in the chat-delivered Sprint 15.22 report
§5. Summary: N=20 and N=25 are the two strongest, closely-matched candidates (N=25 marginally ahead on
regime/HalfLife correlation [0.562 vs 0.462] and `AR1(0.95)` sub-period stability [stdOfMedian 0.011 vs
0.012]; N=20 marginally ahead on market/Range correlation [Pearson(Range)=0.443 vs 0.411,
Pearson(rollStdev20)=0.749 vs 0.728]). All 8 candidates pass the history-independence criterion
(|Spearman(index)| ≤0.136, vs 0.971 pre-fix) and the 3-seed `RandomWalk` check (all ≤0.166, vs
0.045–0.166 range across seeds/N).

**N recommended: 20.** Tie-break rule (not "N that maximizes any single correlation" — Sprint 15.22
report §14 rule): prefer the simpler, already-established value. N=20 already matches an independent,
pre-existing convention in this same codebase's Regime Engine (`VolatilityModel.CurrentVolatilityWindow`
=20; `HalfLifeEvidence`/`VarianceRatioEvidence`/`CusumEvidence`'s `MinimumSampleSize`=20) — not chosen
because it equals QDE-012's `Horizon`=40 (it doesn't).

============================================================
5. LIMITATIONS
============================================================

- The pre-existing R/Q semantic conflation (measurement noise vs. genuine state variation) is not fixed
  by windowing — a smaller, secondary, documented residual concern for a future, separately-justified
  Kalman-model audit, not blocking this sprint's fix.
- Outlier sensitivity was measured in depth at N=40 only (Sprint 15.21) and via a dedicated stress test
  at N=10/20/25/80 this sprint (single/multi synthetic outlier, ratio up to ~7–11x at N=20/25 under the
  classical estimator) — an accepted, inherent property of variance-based estimation on a bounded
  window, not swapped for a robust estimator (no independent justification made to do so, per Sprint
  15.22's explicit rule).
- `AR1(0.95)` sub-period stability degrades markedly above N≈40 (stdOfMedian 0.009–0.040 up to N=40,
  jumping to 0.109–0.127 at N=60/80) — an additional data point against larger windows, consistent with
  the market/regime-correlation decline observed at the same N values.

============================================================
6. TESTS
============================================================

`Tests/ScientificModels/KalmanFilterModelWindowTests.cs` (8 tests, TDD — written before the
implementation): History Length Invariance, No Look-Ahead, Rolling Window Boundary, Warmup, Outlier
Sensitivity (bounded, not eliminated), Unit Consistency (linear scaling with price level), QDE-012
Integration Contract (finite/non-negative/linear-in-k), Kalman State Integrity (no NaN/Infinity/negative
variance, deterministic).

Two pre-existing tests required correction because their premise was the pre-15.22 full-history design,
not a new defect introduced here:
- `OrnsteinUhlenbeckModelTests.AssertLargeShockDrivesThetaAndStrengthToZero` — re-derived empirically
  against the corrected model (30-flat-observation padding could push `NormalizedInnovation` past 3.0
  under the old full-history design; a 20-observation window structurally cannot exceed ≈2.97 for a
  single terminal outlier). Assertion changed from an exact-zero floor to a documented near-zero bound
  (<0.05), doc comment explains why in full.
- `Sprint1515HistoricalReconciliationXunitTests.VerifySprint1514Unaffected` — retired via
  `[Fact(Skip=...)]`, not deleted. Its premise ("the current code reproduces Sprint 15.14's published
  `A1_stable_regions.csv` numbers exactly") is now permanently false by design — the whole point of this
  sprint's fix is that those numbers change on any non-stationary-enough series, exactly what Sprint
  15.14's TRAIN curves are. Sprint 15.14's own `.md` report (unmodified) remains the authoritative
  historical record.

Full-suite result after correction: **172 passed, 0 failed, 1 skipped (documented), 173 total** — no
regression. `A1VsA2EntryAnalysisXunitTests` (hardcoded `entryCount==112662`) passed unchanged, confirming
the fix changes `InnovationStd`'s numeric values only, not which bars are model-valid/signal-eligible.

============================================================
7. VALIDATION REELLE (ATAS)
============================================================

NOT_YET_VALIDATED_IN_ATAS. Everything in this report is offline (real historical capture data replayed
through the corrected code, plus synthetic series) — no new live ATAS Market Replay session has been run
with this sprint's code. See the chat-delivered Sprint 15.22 report §14/§17 for what a validating capture
would need to show.

============================================================
8. IMPACT QDE-012
============================================================

See the chat-delivered Sprint 15.22 report §12/§15 for the full before/after numeric comparison (at
k=3, the Sprint 15.14 "illustration only" reference point) and the per-sprint conclusion status table
(15.11–15.14: still classified `INVALIDATED_BY_REAL_DATA`/`NEEDS_REVERIFICATION` from Sprint 15.21,
**not** automatically rehabilitated by this sprint's fix — re-testing them is explicitly Sprint 15.23
scope, not performed here).

============================================================
9. INCIDENT NOTE — OUTPUT ARTIFACT REGENERATION
============================================================

Running the full test suite (required by this sprint's Phase 0 baseline and Phase 4 post-correction
checks) silently regenerated numerous `Tests/Research/StopLossCalibration/Output/*` files as a
side-effect of pre-existing "producer" tests (documented behavior since Sprint 15.10–15.15). Several of
these files were untracked by git and had no recoverable backup; their pre-Sprint-15.22 exact byte
content is permanently lost (the Sprint 15.14/15.15 `.md` reports, which are the authoritative record of
those sprints' conclusions, are unaffected). The user committed the current state (commit `be76c91`,
"calibration") as a new safety baseline — all `Output/*` files are now git-tracked, so any future test
run's regeneration is fully visible via `git diff` and reversible. This commit is a go-forward safety net
only; it does not recover the specific untracked files already overwritten before it was made.

============================================================
NEXT ACTION
============================================================

Sprint 15.23 (or a dedicated calibration sprint): re-run the existing 15.11–15.14 synthetic-campaign
tooling (already wired to the corrected `KalmanFilterModel` — no further code change needed for this)
and re-interpret A1's k-region under the corrected metric; obtain a real ATAS capture with this sprint's
code to validate `InnovationStd`'s local, history-independent behavior live; only then revisit whether
A1 remains the preferred candidate over A2 (which is entirely unaffected by this sprint's fix).
