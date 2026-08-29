# QDE-012 — Sprint 15.12: A1 vs A2 Scientific Decision & Behavioral Analysis

Date: 2026-08-12.

## 1. Verdict

**A1 > A2, qualified — not unanimous, direction robust to sensitivity checks.** No production parameter selected.

## 2. Scope

Analysis-only sprint. Answers one question: does A1 (`k×InnovationStd`) or A2 (`k×CurrentVolatility`) provide the more scientifically defensible Stop Loss behavior, given the data already collected plus new entry-level decomposition. No Risk Engine, no production code, no k hardcoded.

## 3. Protocol reference

`QDE-012_StopLoss_Calibration_Protocol.md` v1.1 (unchanged — no amendment needed). Sprint 15.11 report taken as prior scientific input.

## 4. Baseline

57 fast tests passing before this sprint (unchanged after — see §15). `git status` confirmed clean production perimeter before starting.

## 5. Harness audit

`CandidateStopDistance.cs` confirmed: A1 (`k×InnovationStd`, line 37) and A2 (`k×CurrentVolatility`, line 40) are the **only** two lines that differ between the candidates. Every downstream step — `StopLossEvaluator`, `OutcomeSimulator`'s direction-adjusted excursion path, `EntryFate` classification, `CandidateAggregator`'s MAE/MFE/rate computation — is identical shared code. **Conclusion: any A1-vs-A2 behavioral difference found below is caused by `InnovationStd` vs `CurrentVolatility` diverging as inputs, not by asymmetric harness treatment.** BUY/SELL symmetry: proven by construction in `OutcomeSimulator` (`move = direction==Buy ? price-entryPrice : entryPrice-price`), already covered by `ConformanceTests.TEST9` for the shared code path both candidates use.

## 6. A1 formula
`StopDistance = k × InnovationStd`, measured from `EntryPrice`.

## 7. A2 formula
`StopDistance = k × CurrentVolatility`, measured from `EntryPrice`. Same formula shape as A1, different sigma source.

## 8. A1/A2 ratio analysis (Phase 2 — the mechanism, traced quantitatively)

`RatioCurVolOverInnovStd = CurrentVolatility / InnovationStd` per entry (k cancels), TRAIN, scale=1, from the new entry-level dump (`A1_vs_A2_ratio_analysis.csv`, 112,662 rows):

| Dataset | N | Median ratio | P10–P90 | Mean |
|---|---|---|---|---|
| WhiteNoise/LowVol/HighVol | 569 | 1.20 | 0.92–1.47 | 1.20 |
| StructuralBreak | 569 | 0.85 | 0.38–1.43 | 0.86 |
| AR1(0.5)=MeanRevertingOu(0.5)* | 569 | 0.83 | 0.66–0.99 | 0.82 |
| VarianceBreak | 569 | 1.49 | 1.02–2.32 | 1.64 |
| AR1(0.95)=MeanRevertingOu(0.05)* | 569 | 0.31 | 0.25–0.37 | 0.31 |
| RandomWalk | 569 | 0.21 | 0.16–0.27 | 0.22 |
| Trending | 569 | 0.0049 | 0.0038–0.0086 | 0.0055 |

**\*Important methodological finding, not previously caught (Sprints 15.9–15.11): `AR1_phi0.5` and `MeanRevertingOu_k0.5` are mathematically identical series.** `Ar1`'s recurrence is `level=φ·level_prev+noise`; `MeanRevertingOu`'s is `deviation=deviation_prev·(1−κ)+noise`. With `φ=0.5` and `κ=0.5`, `1−κ=φ=0.5` exactly — same recurrence, same seed, same noise draw. Verified byte-identical via `diff` on the entry-level dump (0 differing lines). Same identity holds for `AR1_phi0.95`/`MeanRevertingOu_k0.05` (`1−0.05=0.95`). **The campaign's "11 datasets" contain only 9 independent series.** This does not invalidate any Sprint 15.10/15.11 number (they're all correct), but it means those two pairs should never be treated as 4 independent confirmations — see §12/§17.

**Mechanism, monotonic and clean**: the ratio shrinks steadily as a series' persistence/trend strength grows — from ~1.2 (pure noise) through ~0.83 (moderate AR(1)) down to ~0.31 (near-unit-root) to ~0.21 (true random walk) to ~0.005 (deterministic trend). `InnovationStd` (whole-history Kalman variance) inflates on any series whose level wanders further over time; `CurrentVolatility` (20-bar trailing return stdev) does not, since it only ever sees recent bar-to-bar changes. This single ratio explains essentially everything found in §9–11 below.

## 9. Dataset-by-dataset comparison (Phase 3, TRAIN, k=2.0, no early aggregation — `A1_vs_A2_dataset_summary.csv`)

| Dataset | A1 RevRate | A2 RevRate | Δ (A2−A1) | A1 StopHitRate | A2 StopHitRate |
|---|---|---|---|---|---|
| WhiteNoise/LowVol/HighVol | 98.6% | 99.0% | **+0.35pp** | 17.8% | 10.7% |
| StructuralBreak | 98.4% | 98.8% | **+0.35pp** | 14.6% | 13.2% |
| VarianceBreak | 93.2% | 98.9% | **+5.80pp** | 45.2% | 15.8% |
| AR1(0.5)/OU(0.5) | 97.0% | 93.3% | −3.69pp | 11.1% | 26.4% |
| Trending | 0.0% | 0.0% | 0.00pp | 20.4% | **100.0%** |
| AR1(0.95)/OU(0.05) | 89.1% | 63.8% | **−25.31pp** | 13.0% | 67.3% |
| RandomWalk | 85.9% | 62.2% | **−23.73pp** | 8.6% | 70.5% |

MAE/MFE percentiles are **identical between A1 and A2 per dataset** (both draw from the same underlying price paths — expected, confirms no harness asymmetry). No dataset failure was averaged away.

## 10. Trending analysis (Phase 4 — mandatory, entry-by-entry)

Sample (TRAIN, bar 30): `InnovationStd≈11.7`, `CurrentVolatility≈0.153` (ratio 0.013). `A1_StopDistance≈23.4`, `A2_StopDistance≈0.31`. **A2 stops out on bar 1, every single entry, 569/569 (100%).** A1 stops out 116/569 (20.4%, later, bar 16–18 typically) and is `Undetermined` for the remaining 453/569 (79.6%) — the stop is simply too wide to resolve within 40 bars.

**Answer to the sprint's own multiple-choice question (D vs E vs A/B):** this is **(D) a metric artifact, not (A) a genuine A2 advantage.** `CurrentVolatility` measures only the 20-bar trailing *return* dispersion; on a smooth deterministic-drift-plus-tiny-noise series, bar-to-bar changes are tiny even though the price has moved far over 30+ bars — so `CurrentVolatility` drastically understates the actual scale of movement. A2's stop firing on bar 1 is not "faster invalidation detection," it's an uninformative, near-zero-width stop that would fire on almost any tick. This is not a point in A2's favor.

## 11. Persistent-series analysis (Phase 5)

RandomWalk and AR1(0.95)=MeanRevertingOu(0.05) (2 independent series after deduplication, §8): ratio medians 0.21 and 0.31 respectively — `InnovationStd` 3–5× larger than `CurrentVolatility`. Traced to source: **the Kalman filter's whole-history variance estimate grows with a random-walk-like series' unbounded wandering, while the 20-bar trailing return stdev stays roughly bounded regardless of how far the level has drifted.** This is a structural property of the two estimators (answer **E**), not a dataset artifact — confirmed by the monotonic ratio trend across the full persistence spectrum in §8. A2's stop ends up 3–5× tighter than A1's at the same k, causing dramatically more stop-outs (67–70% vs 9–13%) and materially lower reversion capture (62–64% vs 86–89%).

## 12. VarianceBreak analysis (Phase 6 — separate treatment, before/after breakpoint)

Breakpoint recovered from the generator itself (`SyntheticSeriesCatalog.VarianceBreak`: noise amplitude 1.0→3.0 at `breakIndex = length/2 = 300`), not invented. Entry-level split (TRAIN, scale=1, k=2.0):

| Period | A1 Reverted | A1 StoppedOut | A2 Reverted | A2 StoppedOut |
|---|---|---|---|---|
| Before break (bar<300, N=270) | 98.1% | 1.9% | 98.9% | 1.1% |
| After break (bar≥300, N=299) | 88.6% | **11.0%** | **99.0%** | **0.7%** |

**A1's stop-out rate jumps ~6× after the volatility regime change (1.9%→11.0%); A2's stays flat or improves slightly (1.1%→0.7%).** Mechanism: post-break, `InnovationStd` (whole-history) is still anchored by the pre-break, smaller-variance data mixed into its sample, so it under-estimates the new regime's dispersion — A1's stop becomes *too tight* for the new, larger noise, causing more stop-outs. `CurrentVolatility`'s 20-bar window adapts to the tripled noise within ~20 bars. **This is a genuine, mechanistically-sound A2 advantage** (answer **B**, real advantage) — the one clear case in this campaign where A2 is measurably more robust than A1.

## 13. k-grid analysis (Phase 7 — descriptive only, no production recommendation)

Reuses the existing 40-point grid CSVs from Sprint 15.10/15.11 (no re-run needed). A1's stable/non-degenerate zone (from the 15.11 report) is k≈2.0–3.5 depending on dataset; A2 has no degenerate population at any k (never null unless `CurrentVolatility` itself is unavailable, which never occurred in this campaign). The dataset-level gap documented in §9–12 (RandomWalk/AR1(0.95) favoring A1, VarianceBreak favoring A2) persists across the entire k-grid, not just k=2.0 — confirmed by the aggregated CSVs' full 40-point curves for both candidates (same shape, different plateau heights, as already reported in Sprint 15.10 §5).

## 14. TRAIN/VALIDATION analysis (Phase 8) & 15. TEST hold-out

`A1_vs_A2_train_validation_test.csv`: for every dataset, TRAIN/VALIDATION/TEST RevRate values for both candidates stay within a few points of each other, confirming reproducibility. The largest `|TRAIN−VALIDATION|` spread observed is **6.3pp** (A2, RandomWalk and AR1(0.95)) — flagged here as a **descriptive diagnostic note** (not a rejection threshold, no arbitrary statistical cutoff invented), since it's still small relative to the ~24–25pp A1-vs-A2 gap itself on those same datasets. TEST was not touched until this point; no value in §9–13 was chosen using TEST.

## 16. Scale invariance (Phase 9)

`A1_vs_A2_scale_analysis.csv`: **max deviation from k, across both candidates, all 11 dataset labels, all 6 scales = 0.0 (exact), reconfirming Sprint 15.10 (A1) and Sprint 15.11 (A2, post-fix)**. `ReversionRate`/`StopHitRate`/`UndeterminedRate` at fixed k were already confirmed scale-stable for both in prior sprints; this sprint's exact-ratio confirmation adds no new divergence.

## 17. Sensitivity analysis (Phase 12 — diagnostic only, not a selection method)

`A1_vs_A2_sensitivity.csv`. Leave-one-(independent-series)-out on the mean `Δ RevRate (A2−A1)` across the **9 independent series** (§8 dedup applied — the duplicate pairs are counted once each):

| Excluded | Series used | Mean Δ (A2−A1) |
|---|---|---|
| None | 9 | −5.06pp |
| RandomWalk | 8 | −2.73pp |
| VarianceBreak | 8 | −6.42pp |
| Trending | 8 | −5.69pp |
| RandomWalk + VarianceBreak | 7 | −3.94pp |

**The sign never flips — A2 is worse on average in every variant tested.** The magnitude does move (−2.7 to −6.4pp), so the conclusion is directionally robust but not numerically fragile-free; no single dataset is solely responsible for the direction. (Per the sprint's own instruction, this average is diagnostic only — it is not how §9's per-dataset findings were reached, and it deliberately hides the VarianceBreak/Trending structure that §9–12 report in full.)

## 18. Decision matrix (Phase 11)

Family grouping corrected for the §8 duplicate-series finding (each independent series counted once):

| Family (independent series) | Robustness | Reactivity | False-stop risk | Lets a trade breathe | Stability | Scale invariance | Complexity |
|---|---|---|---|---|---|---|---|
| Stationary (WhiteNoise/LowVol/HighVol) | A1≈A2 | A1≈A2 | A1≈A2 | A1≈A2 | Both high | Both exact | Same |
| Mean-reverting, moderate (AR1(0.5)) | A1 slightly better | A2 more reactive | A2 higher (26% vs 11%) | A1 better | A1 more stable | Both exact | Same |
| Persistent/quasi-unit-root (RandomWalk, AR1(0.95)) | **A1 clearly better** | A2 far more reactive (too much so) | **A2 much higher** (67-70% vs 9-13%) | **A1 far better** | A1 more stable | Both exact | Same |
| Trending | Neither works | A2 "reacts" but meaninglessly (§10) | A2 100% (uninformative) | A1 stays Undetermined (arguably more honest) | Neither | Both exact | Same |
| Structural break | A1≈A2 | A1≈A2 | A1≈A2 | A1≈A2 | Both high | Both exact | Same |
| Volatility regime change (VarianceBreak) | **A2 clearly better** | **A2 correctly more reactive** | **A2 much lower** (0.7% vs 11.0% post-break) | A2 better here | A2 more stable post-break | Both exact | Same |

**Per-family conclusion:** A1 superior (persistent/quasi-unit-root, moderate mean-reversion), A2 superior (volatility regime change), equivalent (stationary, structural break), inconclusive/both-fail (Trending).

## 19. Scientific decision

**A1 > A2, on balance — not unanimous.**

Justification: on 2 of the 6 families (stationary, structural break) the candidates are statistically indistinguishable. On 1 family (volatility regime change) A2 has a genuine, mechanistically-sound advantage (§12). On 2 families (persistent/quasi-unit-root, moderate mean-reversion) A1 has a much larger, also mechanistically-sound advantage (§11) — a 20-25 point reversion-rate gap versus A2's best showing of a 5.8 point gap. Trending is a wash (neither works; A2's apparent "reaction" is a metric artifact, §10, not a real advantage). The sensitivity check (§17) confirms this direction is not an artifact of any single dataset — it never flips sign across five different leave-one-out variants.

**A2 is not rejected outright** — its VarianceBreak result is real and would matter if regime-change robustness were the priority. But averaged fairly across regime types actually present in this campaign, A1's advantage is larger and touches more distinct market conditions (2 families, larger magnitude) than A2's (1 family, smaller magnitude).

## 20. Limitations

- **Real market validation: NOT PERFORMED.** Everything above is synthetic-only (repeated per QDE-012 §12).
- **AR1_phi0.5≡MeanRevertingOu_k0.5 and AR1_phi0.95≡MeanRevertingOu_k0.05 are the same series** (§8) — a genuine methodological gap in Sprints 15.9–15.11 that went uncaught until this sprint. It does not change any earlier reported number (they were all computed correctly), but any claim of "confirmed across N independent datasets" in prior reports should be read as N−2.
- Kalman `processNoise=0.1×measurementNoise` caveat (QDE-012 §4) still applies to every `InnovationStd`-based number (A1's entire formula).
- Zone/threshold language ("within 2pp," "6.3pp spread") is descriptive convention, not a locked statistical criterion.
- No formal hypothesis test was used (Phase 10 instruction) — observations are dependent (same underlying processes across k/scale), so no standard significance test would be valid without much more care; this report stays descriptive rather than fabricate a p-value.
- D1/D2 were not touched this sprint, per instruction.

## 21. Production implications

None. No k selected, no `PLAN_READY`, no chart annotation, no Risk Engine.

## 22. Next sprint recommendation

Given A1's edge is not universal, and D1 (Sprint 15.11) showed no measurable benefit over A1 either: the most defensible next step is **not yet a Risk Engine implementation sprint**. Two options, not a forced choice: (1) a **regime-conditional stop sprint** — since A1 wins on persistent series and A2 wins on volatility regime changes, investigate whether `VolatilityRegime` (already computed, unused for this purpose) could gate which sigma estimator is used, rather than picking one globally; (2) **real market data acquisition**, still the single highest-value gap for either candidate to leave `PARTIALLY VALIDATED`.

---

**Files produced this sprint** (`Tests/Research/StopLossCalibration/Output/`, none of Sprint 15.10/15.11's CSVs deleted): `A1_vs_A2_comparison.csv` (112,662 entry-level rows, k=2.0), `A1_vs_A2_ratio_analysis.csv` (112,662 rows), `A1_vs_A2_dataset_summary.csv`, `A1_vs_A2_scale_analysis.csv`, `A1_vs_A2_train_validation_test.csv`, `A1_vs_A2_sensitivity.csv`, this report.
