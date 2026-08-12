# QDE-012 — Sprint 15.11: A2 Instrumentation Fix + A1/D1 Decision Analysis

Date: 2026-08-12. Protocol reference: `QDE-012_StopLoss_Calibration_Protocol.md` v1.1 (unchanged this sprint — no formal necessity to amend it arose).

## 1. Verdict

**PASS.** Both objectives completed: A2's scale-ratio instrumentation is fixed and verified exact; A1/D1 zone analysis is complete with data-derived boundaries. No parameter is selected for production — none was authorized, none is proposed.

## 2. Baseline

Before this sprint: 57 fast tests passing (all non-campaign suites), `CampaignExecutionXunitTests` and its 174,240-row output independently verified in Sprint 15.10. `git status` showed 15 pre-existing modified production files (unrelated prior sprints) and the QDE-012 harness/docs as untracked additions.

## 3. Modification A2

**Bug** (found in Sprint 15.10's own Limitations, confirmed by fresh audit): `CandidateAggregator.Aggregate` computed `MeanStopRatio` against `entry.Metrics.InnovationStd` unconditionally, regardless of `CandidateKind` — so A2's ratio (whose `StopDistance` is `k×CurrentVolatility`) was being divided by the *wrong* sigma.

**Fix** (`Tests/Research/StopLossCalibration/CandidateAggregator.cs`): the denominator now depends on `CandidateKind` — `CurrentVolatility` for A2, `InnovationStd` for A1/D1/D2. When the relevant sigma is null, zero, or non-finite, the entry is excluded from the ratio (never a fabricated 0/NaN mixed into a real mean) — the harness's existing not-available convention (NaN sentinel, formatted as blank in CSV).

Research-harness-only change. No scientific model, production DTO, or trading logic touched.

## 4. Tests A2

6 targeted tests added (`RatioConformanceTests.cs`), all pass:

| Test | Result |
|---|---|
| A: A2, CurrentVolatility=2, k=3 → StopDistance=6, ratio=3 | PASS |
| B: A1, InnovationStd=2, k=3 → ratio=3 | PASS |
| C: D1, InnovationStd=2 → ratio uses InnovationStd, not CurrentVolatility | PASS |
| D: A2, CurrentVolatility=0 → ratio is NaN, never a fabricated value | PASS |
| E: A2, CurrentVolatility unavailable → ratio is NaN, `ApplicableEntries=0` | PASS |
| F: same entry, different CandidateKind → StopDistance and ratio provably track the kind (A1 used InnovationStd=4, A2 used CurrentVolatility=2, from the *same* fixture) | PASS |

## 5. Résultats scale-invariance A2

Targeted A2-only campaign re-run (all 11 datasets × 3 splits × 6 scales × 40 k, `A2_results_corrected.csv`, 7,920 rows, 1m22s). For every (dataset, k) combination in TRAIN (440 groups) and every (dataset, k) combination in VALIDATION+TEST (880 groups): **min ratio = max ratio = k, exactly, in all 1,320 groups checked. Zero deviation. Zero NA/unavailable cases** (`CurrentVolatility` was positive and finite for every entry at every scale in this campaign).

**A2 scale invariance: EXACT** (not merely approximate) — cleaner than A1's own Sprint 15.10 result, which showed one small artifact (`LowVolatility` at scale≤0.1, `D1` specifically, from `InnovationStd` approaching Kalman's `1e-6` floor). A2's ratio is algebraically `k×CurrentVolatility / CurrentVolatility = k` with no floor-affected term involved, so this cleanliness is expected, not surprising, once traced through.

This is an **EXPLORATORY DIAGNOSTIC measurement**, run over the specific 11×3×6×40 grid tested — not a proof for all possible future series.

## 6. A1 analysis

Reversion rate rises from a low starting point at k=0.25 (48–73% depending on dataset) and reaches a plateau ("asymptotic" reversion rate, within 2pp of its k=10 value) between **k≈1.5 and k≈3.0** depending on dataset (TRAIN, scale=1):

| Dataset | Asymptotic RevRate | k where within 2pp of asymptote |
|---|---|---|
| RandomWalk | 86.1% | 1.50 |
| StructuralBreak | 99.5% | 1.50 |
| WhiteNoise / LowVol / HighVol | 99.8% | 2.00 |
| AR1(0.5) / MeanRevertingOu(0.5) | 99.3% | 2.25 |
| AR1(0.95) / MeanRevertingOu(0.05) | 91.6% | 2.25 |
| VarianceBreak | 99.8% | 3.00 |
| Trending | 0.0% | never (no zone works) |

`StopDistance` crosses the realized MAE-P75 around k=1.25–3.25 and MAE-P90 around k=1.5–4.75 (dataset-dependent) — i.e. once k is large enough to sit above P75, the stop is wider than 3 out of 4 realized adverse excursions.

## 7. D1 analysis

Same asymptotic reversion rates as A1 (expected — both converge to the same "unstopped" ceiling as k grows), but reached **later**, and gated by a real applicability problem at low k:

| Dataset | k: degenerate<5% | k: degenerate<1% | k: RevRate within 2pp of asymptote |
|---|---|---|---|
| RandomWalk | 0.50 | 0.75 | 1.75 |
| AR1(0.95) / MeanRevertingOu(0.05) | 0.75 | 1.00 | 2.50 |
| AR1(0.5) / MeanRevertingOu(0.5) | 1.25 | 1.50 | 2.50 |
| StructuralBreak | 1.25 | 2.00 | 2.25 |
| WhiteNoise / LowVol / HighVol | 1.50 | 2.00 | 2.25 |
| VarianceBreak | 2.00 | 3.00 | 3.50 |

At k=0.25, degenerate rate is 54% pooled across TRAIN (Sprint 15.10 finding, reconfirmed). D1 only becomes broadly usable (non-degenerate **and** stable) around **k≳2.5–3.5**, i.e. roughly 0.5–1.0 k-units later than A1 needs for the same stability, dataset by dataset. **Does the extra complexity buy anything A1 doesn't have?** Based on this data: no clear benefit. Once both are in their respective stable zones, their reversion/stop-hit behavior is close (they're both anchored to the same `InnovationStd`, and D1's entry-adjustment mostly just consumes part of the k budget before the numbers converge to A1-like behavior). D1's only structural difference from A1 — anchoring to equilibrium instead of entry — does not show up as a measurably different reversion or stop-hit outcome in this campaign; it shows up as a *narrower usable k-range* and a population that must be tracked (`ApplicableEntries` < `TotalEntries`).

## 8. A1 vs D1 comparison — dataset-family analysis

No family average was computed that could hide a per-dataset failure — every number above is per-dataset.

| Family | Datasets | A1 vs D1 |
|---|---|---|
| Stationary/noise | WhiteNoise, LowVol, HighVol | Equivalent once past their respective stable-k thresholds (A1: k≈2.0, D1: k≈2.25). Both reach ~99.8–100% reversion. |
| Mean-reverting | MeanRevertingOu(0.5), MeanRevertingOu(0.05), AR1(0.5) | Equivalent asymptote; D1 needs k≈0.75–1.0 units more to clear its degenerate zone than A1 needs to reach stability. |
| Persistent/quasi-unit-root | RandomWalk, AR1(0.95) | Both plateau lowest here (81–92%) — **neither works especially well**, reflecting genuine ambiguity in what "equilibrium" means on a near-unit-root series. D1 stabilizes slightly later than A1 (k≈1.75 vs 1.5 for RandomWalk). |
| Trending | Trending | **Neither candidate works.** 0% reversion at every k for both. Correctly and consistently flagged, not averaged away. |
| Structural break | StructuralBreak | Both strong (99.1–99.5%), D1 needs more k (1.25–2.0 for degeneracy) but converges to the same place as A1. |
| Volatility regime change | VarianceBreak | **A1's slowest-converging dataset (k≈3.0) is also D1's slowest (k≈3.5)** — consistent finding across both candidates, suggesting the difficulty is intrinsic to the dataset (a volatility regime shift genuinely confuses a volatility-anchored stop, for either candidate), not an artifact of one formula.

## 9. TRAIN / VALIDATION

Every boundary in §6/§7 was computed on TRAIN and independently cross-checked on VALIDATION; they agree within 0.25–0.5 k-grid steps everywhere (e.g. A1 WhiteNoise stability at k=2.00 TRAIN vs k=2.00 VALIDATION; D1 VarianceBreak degenerate<1% at k=3.00 TRAIN vs k=3.25 VALIDATION). No value in §6/§7 was selected using TEST. `FalseInvalidationRate`/`TrueInvalidationRate` were not used anywhere in this selection (QDE-012 §17).

## 10. TEST hold-out

Applied once, after §6–9 were finalized, at two representative Zone-C points (chosen for being inside every dataset's stable+non-degenerate region, not as a recommendation): **A1 @ k=2.5**, **D1 @ k=4.0**.

| | A1 k=2.5 | | | D1 k=4.0 | | |
|---|---|---|---|---|---|---|
| Dataset | TRAIN RevRate | VAL RevRate | TEST RevRate | TRAIN RevRate | VAL RevRate | TEST RevRate |
| WhiteNoise | 99.30% | 99.65% | 99.30% | 99.82% | 100.0% | 99.82% |
| RandomWalk | 86.12% | 81.02% | 80.49% | 86.12% | 81.20% | 80.49% |
| AR1(0.95) | 91.21% | 88.05% | 88.22% | 91.56% | 88.58% | 89.10% |
| VarianceBreak | 96.31% | 97.54% | 97.89% | 99.12% | 98.94% | 99.29% |
| Trending | 0% | 0% | 0% | 0% | 0% | 0% |

Every dataset's TEST value sits within ~1–3pp of its VALIDATION value — reproducible, no evidence of overfitting to TRAIN/VALIDATION. `Undetermined` rates and `StopHitRate` (not tabulated above for space, see raw CSVs) show the same pattern.

## 11. Trade-offs

Not a single "best k" — four zones, boundaries taken directly from §6/§7 (values are the *widest* range across all 11 non-Trending datasets, i.e. conservative):

- **Zone A — too tight (k < ~1.0):** ReversionRate still climbing steeply (48–79% at k=0.25, far from any plateau); `StopDistance` sits below the MAE-P75 of realized outcomes, meaning a *typical* mean-reversion trade's normal noise already breaches it. D1 additionally has 8–54% of entries degenerate here.
- **Zone B — transition (k ≈ 1.0–2.0):** ReversionRate rising fast toward its plateau; `StopDistance` crosses MAE-P75 for most datasets. D1's degenerate population falls under 5% for most datasets by the top of this zone.
- **Zone C — robust (k ≈ 2.0–3.5):** ReversionRate within 2pp of its ceiling for 10/11 datasets (A1) or all 11 (D1, once past its own degenerate threshold); `StopDistance` sits between MAE-P75 and MAE-P90 — comfortably wider than typical noise, not yet absurd. Confirmed stable across TRAIN/VALIDATION/TEST (§10).
- **Zone D — too wide (k > ~5–6):** `StopDistance` exceeds MAE-P90 for every dataset; `StopHitRate`→0 but `Undetermined` climbs instead of `Reverted` (RandomWalk: 13.9% Undetermined at k=5 vs 0% at k=2) — the stop stops protecting anything, all it does is fail to resolve within the horizon.

Trending sits outside all four zones for both candidates — no k value makes it "work," and no average was allowed to hide that.

## 12. Limitations

- Real-market validation: **NOT PERFORMED** (repeated per QDE-012 §12 — synthetic only).
- Kalman `processNoise=0.1×measurementNoise` caveat (QDE-012 §4) applies to every number using `InnovationStd` — A1, D1, and A2's own `StopDistance` computation upstream of the corrected ratio.
- Zone boundaries (§11) are descriptive of *this* 11-dataset/600-bar/40-horizon campaign — not proven to generalize to other horizons or real market microstructure.
- "Within 2pp of asymptote" and the MAE-P75/P90 crossover are diagnostic conventions chosen for this analysis, not protocol-locked criteria — a different analyst could reasonably draw the zone boundaries slightly differently from the same raw numbers (which remain in the CSVs).
- D2 was explicitly **not** recalibrated this sprint, per your instruction — its Sprint 15.10 `NO DECISION` stands unchanged.

## 13. Production implications

None authorized, none implied. No `PLAN_READY`, no `RiskPerTrade`, no production Stop Loss, no chart annotation. This remains a research finding: *if* a future Risk Engine sprint needs a k, Zone C (§11) is where the data supports looking first — not a value to hardcode from this report.

## 14. Production files modified

**NO.** `git status`/`git diff --stat` confirm the only changes this sprint are `Tests/Research/StopLossCalibration/CandidateAggregator.cs` (fix), `CampaignRunner.cs` (new `RunA2Only` method), two new harness files (`RatioConformanceTests.cs`, `A2ScaleCampaignTests.cs`), and two new xUnit wrappers. `QDE-012_StopLoss_Calibration_Protocol.md` was read but not modified — no formal necessity for an amendment arose this sprint.

## 15. Final scientific classification

- **A1: PARTIALLY VALIDATED.** Broadly stable Zone C (k≈2–3.5 depending on dataset), scale-invariant (exact), reproducible TRAIN→VALIDATION→TEST, no degenerate population ever. Not fully VALIDATED: no real-market evidence, Kalman-ratio caveat unresolved, and Trending remains an unexplained failure mode (expected, but unresolved).
- **A2: PARTIALLY VALIDATED** (upgraded from Sprint 15.10's `NO DECISION` — the blocking instrumentation gap is now fixed and measured EXACT). Scale invariance is now cleanly confirmed. Still not VALIDATED: it remains structurally weaker on persistent/quasi-unit-root series (RandomWalk/AR1(0.95), 62–70% reversion vs A1's 81–89%, per the Sprint 15.10 report) and stops out 100% of Trending entries immediately rather than staying Undetermined like A1 — a real, reproducible behavioral difference that still needs a judgment call (aggressive vs. forgiving) this sprint does not make.
- **D1: PARTIALLY VALIDATED.** Same asymptotic behavior as A1, same Kalman caveat, but reaches its stable zone later (roughly k+0.5 to k+1.0 versus A1) due to its low-k degenerate population — extra complexity with no measured benefit over A1 in this campaign.
- **D2: NO DECISION** (unchanged, not recalibrated this sprint, per instruction).

## 16. Recommended next sprint

Not yet a Risk Engine implementation sprint. Two candidate directions, not a forced choice:
1. **A1-vs-A2 decision sprint**: resolve the Trending/persistent-series behavioral difference (§15) with an explicit, documented judgment call (forgiving-but-slower-to-react vs. aggressive-but-tighter) before either can move past `PARTIALLY VALIDATED`.
2. **Real-market data acquisition**: every conclusion in this and the QDE-012 report is synthetic-only; the single highest-value next step for either A1 or A2 to become `VALIDATED` is testing against real OHLC data, which does not exist anywhere in this project yet.
