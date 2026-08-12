# QDE-012
# Scientific Specification – Stop Loss Calibration Protocol

Version : 1.1 (POC-validated; Candidate D formula formally locked 2026-08-12 via §14 amendment; campaign not yet run)

Status : Research Protocol — locked before calibration campaign

Module : Research / Tests.Research.StopLossCalibration (isolated harness, no production dependency beyond read-only model consumption)

Sprint : 15.10

Dependencies : QDE-011 (Risk Engine Theory), Sprint 15.9 audit (metric formulas/units/windows)

---

## 1. Purpose

Determine, empirically and without inventing a parameter, whether Candidate A (Volatility Stop) or
Candidate D (Mean-Reversion Invalidation) is a scientifically defensible Stop Loss methodology for
IQIA — or whether the evidence does not support choosing either yet.

This document is the control: every definition below is locked *before* the full grid campaign runs.
Changing a definition mid-campaign to improve a result is explicitly forbidden (Sprint 15.10 §6).

## 2. Architectural isolation

The harness lives entirely under `IQIAIndicator/Tests/Research/StopLossCalibration/`. It:

- **Reads** (never modifies, subclasses, or wraps-and-alters) the real production classes:
  `KalmanFilterModel`, `OrnsteinUhlenbeckModel`, `DynamicZScoreModel`, `VolatilityModel` (via
  `ScientificModelRegistry`'s own wiring/order), and `HalfLifeEvidence` — so calibration measures the
  system that actually runs, not a re-derivation of its formulas.
- Never touches `ScientificFusion`, `Decision`, `EntryTrigger`, `TradePlan`, `Dashboard`, `IQIAIndicator`
  runtime, or ATAS execution.
- Produces no production `StopLoss`, `RiskEngine`, `PositionSize`, `PLAN_READY`, chart line, or arrow.

## 3. Deduplicated metric: EquilibriumDistance

Sprint 15.9's audit found `DistanceToEquilibrium` (VolatilityModel) and `ExpectedReversionDistance`
(DynamicZScoreModel) are the identical formula (`abs(CurrentPrice − EstimatedEquilibrium)`) computed
twice under two names. The harness carries this as a single field, `EquilibriumDistance`
(`BarMetrics.EquilibriumDistance`), and the analysis treats it as one variable, never two.

## 4. Kalman caveat (carried through every conclusion)

`InnovationStd` depends on `KalmanFilterModel`'s fixed `processNoise = 0.1 × measurementNoise` ratio.
This ratio is **not calibrated** in this sprint and is **not modified** by it. Every conclusion in this
protocol that uses `InnovationStd` (Candidate A1, Candidate D) is conditional on that fixed ratio — a
different ratio could shift the results. This is stated once here and must be repeated in the final
report's limitations section, not silently assumed.

## 5. Metric audit summary (full detail: Sprint 15.9 chat report)

| Metric | Formula | Unit | Window | Low-variance / NaN behavior |
|---|---|---|---|---|
| InnovationStd | `sqrt(max(stateCovariance+measurementNoise, 1e-6))`, batch Kalman pass | price | full supplied history | floors at `1e-6`, never NaN |
| DynamicZScore | `(price−EstimatedMean)/InnovationStd`, fallback `sign×6.0` if `InnovationStd≤0` | σ units | same as InnovationStd | never NaN (explicit fallback) |
| CurrentVolatility | `stdev(returns)`, population variance | price | `min(20, history−1)` | requires ≥3 history points |
| VolatilityRegime / Percentile | thresholded ratio / rolling rank | categorical / [0,1] | same as CurrentVolatility | `UNKNOWN` if non-finite |
| HalfLife (real) | OLS `Δx=intercept+λx`, `HalfLife=−ln2/λ`, valid only if `λ<0` | bars | trailing 30 bars (min 20) | `Invalid` (not NaN) if `λ≥0` |
| HalfLife.RSquared | `1−SSR/SST`, clamped [0,1] | dimensionless | same regression | 0.0 if `SST≤1e-12` |
| EstimatedEquilibrium | Kalman `stateMean` | price | full supplied history | requires ≥2 history points |
| EquilibriumDistance | `abs(price−EstimatedEquilibrium)` | price | point-in-time | depends on Kalman success |
| MaxZScoreForConfidence | constant `6.0` | σ units | n/a | not derived from data — Candidate D's own hypothesis under test, not a given |

⚠ A second, heuristic "HalfLife" exists in `OrnsteinUhlenbeckModel` (Kalman-innovation-derived, not a
regression). The harness **only** uses the real regression (`Regime/Evidence/HalfLife`), never this one.

## 6. Event definitions (locked)

**Entry** — bar T is signal-eligible iff, using `series[0..T]` only:
- Kalman + OrnsteinUhlenbeck + DynamicZScore + Volatility all succeed (`BarMetrics.ModelsValid`), and
- `DynamicZScore(T) ≠ 0` (mirrors production's existing, unchanged NO_ACTION-on-zero rule — no dead
  zone is introduced).
- `T ≥ WarmupBars (30)`, so every metric has had at least one full window before being trusted.
- Direction: `DynamicZScore<0 → Buy`, `>0 → Sell` (the same sign rule EntryTriggerBuilder already uses,
  restated here rather than imported, to keep the harness's production dependency limited to the
  scientific model classes).

**Horizon** — fixed `H` bars measured after entry. **H = 40** for the full campaign (generous relative
to κ=0.5's ~1.4-bar theoretical half-life, while remaining short enough that even κ=0.05 "weak"
reversion has a fair chance to show a partial move). Series are generated at **length = 600** (not the
catalog's 120-bar default) specifically to yield enough independent-ish entries per series for
statistics — the *generator, seed, and parameters* are exactly the documented golden-dataset ones;
only `length` (an existing parameter on every `SyntheticSeriesCatalog` method) is increased.

**ReturnedToEquilibrium** — the first bar `h∈[1,H]` (post-entry) where price crosses the *entry-time*
`EstimatedEquilibrium` in the favorable direction (`price≥eq` for Buy, `price≤eq` for Sell). Equilibrium
is **never recomputed** with future data — it is frozen at the value `BarMetricsComputer` produced at
entry. `ReturnedToEquilibrium = EquilibriumBar.HasValue`.

**StopHit(distance)** — the first bar `h` where the running adverse excursion (price-unit magnitude
against the position) reaches or exceeds `distance`. Derived from a precomputed per-entry excursion
path (`StopLossEvaluator`), not by re-walking the price path per candidate — O(1) per (entry, k) after
O(H) work done once per entry.

**Fate** (`EntryFate`): `StoppedOut` if the stop fires strictly before equilibrium is reached (or
equilibrium is never reached); `Reverted` if equilibrium is reached at or before the stop fires (or the
stop never fires); `Undetermined` if neither happens within H — never silently folded into either
bucket.

**MAE / MFE** — price-unit **magnitudes** (always ≥0), already direction-adjusted (a Buy's downside and
a Sell's upside both read as "adverse"; consumers never re-flip a sign). MAE/MFE = the final value of
the running adverse/favorable excursion path at `h=BarsAvailable`.

`BarsAvailable` may be `<H` near the end of a series — never silently padded; every consumer must read
it, not assume it equals H.

## 7. Look-ahead separation (the critical constraint)

Two classes, one direction of data flow:
- `BarMetricsComputer.Compute(series, T)` reads **only** `series[0..T]`.
- `OutcomeSimulator.Simulate(series, T, direction, entryPrice, equilibriumAtEntry, H)` reads **only**
  `series[T+1..T+H]`, and takes `entryPrice`/`equilibriumAtEntry` as already-frozen parameters — it
  never recomputes them.

This is not just documented — `StopLossCalibrationPocTests.AssertLookAheadSafety_...` computes a bar's
metrics against the full series and against a copy physically truncated right after that bar, and
asserts byte-identical results. **This passed in the POC run** (see §POC results, external report).

## 8. Datasets (all reused from `SyntheticSeriesCatalog`, no new generator created)

| # | Family | Generator call |
|---|---|---|
| 1 | White Noise | `WhiteNoise(length: 600)` |
| 2 | Random Walk | `RandomWalk(length: 600)` |
| 3 | AR(1) φ=0.5 | `Ar1(length: 600, phi: 0.5m)` |
| 4 | AR(1) φ=0.95 | `Ar1(length: 600, phi: 0.95m)` |
| 5 | Mean Reversion forte | `MeanRevertingOu(length: 600, kappa: 0.5m)` |
| 6 | Mean Reversion faible | `MeanRevertingOu(length: 600, kappa: 0.05m)` |
| 7 | Trending | `Trending(length: 600)` |
| 8 | Volatilité faible | `LowVolatility(length: 600)` |
| 9 | Volatilité élevée | `HighVolatility(length: 600)` |
| 10 | Régime changeant | `StructuralBreak(length: 600)` **and** `VarianceBreak(length: 600)` (both fit "changing regime" — mean-shift and variance-shift are different phenomena, kept separate) |

## 9. Train / Validation / Test split

Synthetic series generated once have no natural temporal train/val/test boundary that avoids
serial-correlation leakage across the split point (AR/OU processes are autocorrelated; a slice-based
split risks entries near the boundary being influenced by data on the other side via overlapping
history windows). Chosen approach: **independent-seed split**, not a temporal slice.

For every dataset family/parameter combination in §8:
- **TRAIN**: seed 42 (the catalog's documented default)
- **VALIDATION**: seed 43
- **TEST**: seed 44

Same generator, same parameters, independently drawn realizations. Parameter selection (k region, R²
threshold) uses **TRAIN+VALIDATION only**. **TEST is not touched until final evaluation** and is
never used to pick a parameter. This is a deliberate substitution for the "60/20/20 temporal split"
suggested in the sprint spec — documented explicitly per the spec's own instruction ("si les données ne
permettent pas cette séparation, le signaler explicitement").

## 10. Grids

- **k** (Candidate A1, A2, and D's dispersion multiplier): `0.25, 0.5, 0.75, ..., 10.0` (step 0.25, 40 points).
- **R² threshold** (Candidate D2): `0.00, 0.05, 0.10, ..., 0.90` (step 0.05, 19 points).
- **Scale**: `0.01×, 0.1×, 1×, 10×, 100×, 1000×` applied to price level; analysis reported primarily as
  the dimensionless ratio `StopDistance / InnovationStd` (or `/CurrentVolatility`), not raw price — a
  methodology that only changes because of the price's unit is flagged as suspect, not scored as
  scale-sensitive in a meaningful sense.

## 11. Selection criteria

A stable **region**, not a single best point (Sprint 15.10 §10 explicit anti-overfitting instruction).
A candidate parameter zone is only reportable if metrics stay within a small, documented tolerance band
across ≥5 consecutive grid points, ≥3 dataset families, and both TRAIN and VALIDATION splits.

Validation requires **all** of: (A) scale invariance, (B) inter-dataset stability, (C) inter-regime
stability, (D) temporal/seed stability, (E) outlier robustness, (F) no single-parameter over-dependence,
(G) coherence with the Mean Reversion hypothesis, (H) reproducibility, (I) reasonable — not
profit-maximal — performance, (J) survives TEST.

## 12. Real market data

`REAL MARKET VALIDATION = NOT PERFORMED` — no historical real-market OHLC data exists anywhere in this
project (confirmed: only synthetic generators and live-ATAS-feed code exist; no stored tick/bar
archive). Every conclusion this protocol can produce is a **synthetic validation** of mathematical
properties (invariance, robustness, controlled behavior) — never evidence that a methodology is
profitable on ES/NQ/NASDAQ or any real instrument. The final report will state this on every relevant
conclusion, not once and then implicitly forgotten.

## 13. What Phases 8–14 will *not* do

No production StopLoss, RiskEngine, PositionSizing runtime, `PLAN_READY`, SL/TP chart line, or arrow is
created at any point in this sprint, regardless of results.

---

## 14. Protocol amendment — Candidate D formula (formally locked before campaign execution)

**Amendment date: 2026-08-12.** The Conformance Gate run before the first campaign attempt found that
no section of this document actually locked Candidate D's formula — only its name and general
description ("Mean-Reversion Invalidation") existed. §10's reference to "D's dispersion multiplier"
assumed a formula that was never written down. The implementation built in response to that gap
(equilibrium-anchored, derived from Sprint 15.9's own candidate proposal) is formalized here, by
decision, as the locked definition. It is no longer to be described as an improvised correction or a
personal interpretation — from this point on it **is** the protocol.

### Candidate D — Mean-Reversion Invalidation

1. **Hypothesis**: the mean-reversion trade's hypothesis is invalidated when price has moved even
   further from the model's own equilibrium estimate than it already had at entry — not when it has
   moved an arbitrary fixed distance from the entry price itself.
2. **Inputs** (all entry-time-only, no look-ahead): `z0 = DynamicZScore` at entry, `σ = InnovationStd`
   at entry, `k` = dispersion multiplier (grid parameter).
3. **Exact mathematical definition**: the invalidation boundary is a fixed price level, anchored to
   `EstimatedEquilibrium` at entry time, at a distance of `k` standard deviations.
4. **BUY formula**: `SL = EstimatedEquilibrium − k × InnovationStd`
5. **SELL formula**: `SL = EstimatedEquilibrium + k × InnovationStd`
6. **StopDistance formula** (distance from entry price, what `StopLossEvaluator` actually consumes):
   `StopDistance = (k − |z0|) × InnovationStd`
7. **Applicability condition**: `InnovationStd > 0` **and** `DynamicZScore` is available **and**
   `k > |z0|`.
8. **Degenerate condition**: `k ≤ |z0|` → Candidate D is `NOT APPLICABLE` / `DEGENERATE` for that
   (entry, k) pair — the invalidation level would sit at or on the wrong side of entry. Never treated
   as an immediate stop-hit; excluded from that k's applicable population entirely (see §15).
9. **D1 definition**: the formula above, unfiltered.
10. **D2 definition**: the same formula as D1, additionally applicable only if
    `HalfLifeValid == true` **and** `HalfLifeRSquared ≥ RSquaredThreshold`. Same degenerate rule as D1
    otherwise.
11. **R² filter**: threshold swept over the protocol-locked grid, §10 (`0.00 → 0.90`, step `0.05`, 19
    points) — unchanged by this amendment.
12. **Interpretation**: D1/D2 is a genuinely different experiment from A1, not a restatement of it.
    A1's distance from entry is `k×σ` for every entry, constant. D1/D2's distance from entry is
    `(k−|z0|)×σ`, which varies per entry with how extended price already was at signal time, and is
    undefined below `k=|z0|`. A run of D1/D2 that produces results numerically close to A1 for a given
    dataset would itself be a finding (it would mean `|z0|` is typically small there), not an
    equivalence assumed in advance.

## 15. Denominator convention (locked)

For every candidate and every grid cell:
- `TotalEntries` = the number of signal-eligible entries generated for that (dataset, split, scale) —
  independent of any candidate or parameter.
- `ApplicableEntries` = the subset of `TotalEntries` for which the candidate's `StopDistance` was
  computable (always `= TotalEntries` for A1/A2, since they are never degenerate; `≤ TotalEntries` for
  D1/D2 per §14's degenerate rule, and further reduced for D2 by the R² filter).
- `DegenerateEntries = TotalEntries − ApplicableEntries`.

`StopHitRate`, `ReversionRate`, `UndeterminedRate`, `StopHitBeforeEquilibriumRate`, `MAE`, `MFE`, and
`TimeToEquilibrium` are all computed over `ApplicableEntries`, never `TotalEntries`. Every report and
every CSV row must display all three counts (`TotalEntries`, `ApplicableEntries`, `DegenerateEntries`)
side by side, so a shrinking applicable population is always visible, never implied.

## 16. StopHitBeforeEquilibriumRate (locked)

`StopHitBeforeEquilibriumRate` = (number of applicable entries whose stop is touched strictly before
equilibrium is reached, i.e. `EntryFate.StoppedOut`) / `ApplicableEntries`.

## 17. Protocol-defined vs. exploratory metrics

**Protocol-defined** (may be used for stable-region detection and any selection decision):
`StopHitRate`, `ReversionRate`, `UndeterminedRate`, `StopHitBeforeEquilibriumRate`, `MAE`, `MFE`,
`TimeToEquilibrium`, the dimensionless stop ratio (§10).

**Exploratory** (descriptive only — reportable in CSVs/summary/final report, but must never drive a k
selection, a D1-vs-D2 choice, a stable-region determination, or the final verdict):
`FalseInvalidationRate`, `TrueInvalidationRate` — requested by name in the original Sprint 15.10 spec
but never given a locked formula in this document; their exact operationalization
(`FalseInvalidationRate = StoppedOut / GroundTruthReverts`, `TrueInvalidationRate = StoppedOut /
GroundTruthNonReverts`) is this harness's own choice, not a protocol-locked one.

**Diagnostic tool classification**: `StableRegionAnalyzer`'s `CvThreshold = 0.10` is an **exploratory
diagnostic criterion**, not a protocol-defined trading parameter and not scientific proof of stability
on its own. Stable-region findings using it must be reported as "flat under a 10% coefficient-of-
variation diagnostic," never as an unqualified claim of stability.
