============================================================
QDE-012 — SPRINT 15.13 — REGIME-CONDITIONAL STOP LOSS
A1 vs A2 vs HYBRID — DECISION REPORT
============================================================

Date: 2026-08-12
Protocol: QDE-012 v1.1 (locked), unchanged by this sprint.
Scope: RESEARCH ONLY. No production file was modified.

============================================================
1. VERDICT
============================================================

**A1 > HYBRID > A2.**

HYBRID (VolatilityRegime-conditional routing between A1 and A2) is directionally
between A1 and A2 on pooled reversion rate, consistently across TRAIN, VALIDATION and
TEST — the ordering never flips. It clearly beats A2 overall. It does not beat A1
overall. It captures a genuine, mechanistically real advantage on VarianceBreak (close
to A2's full post-break recovery), but this is outweighed by two defects: it inherits
A2's Trending stop-hit artifact almost entirely (~97% of Trending entries route to the
A2 leg), and it gives back part of A1's advantage on the two persistent/quasi-unit-root
series (RandomWalk, AR1(0.95)) by routing a meaningful minority of their entries to A2.
The overall Hybrid-vs-A1 effect size is small (well under 1 percentage point of pooled
ReversionRate) and its **sign flips under 2 of 5 leave-one-independent-series-out
variants** — a textbook fragile result, not a robust one, even though the *ordering*
A1 > HYBRID > A2 itself never flipped across TRAIN/VALIDATION/TEST.

No production parameter, Risk Engine, or Stop Loss implementation follows from this
result, win or lose, per the sprint's scope.

============================================================
2. SCOPE
============================================================

Research only. Allowed paths: `Tests/Research/`, `Tests/XunitWrappers/`,
`Documentation/Scientific/`. Verified at the end of this report (§29 gate) that no file
outside these paths was touched.

============================================================
3. QDE-012 VERSION
============================================================

v1.1, locked. Not modified. A1/A2 formulas, datasets, grids, splits, seeds, and
protocol-defined vs. exploratory metric classification are all reused unchanged from
`Tests/Research/StopLossCalibration/` (Sprint 15.10–15.12).

============================================================
4. SPRINT 15.12 FINDINGS (RECAP)
============================================================

- A1 (`k×InnovationStd`) > A2 (`k×CurrentVolatility`), qualified, not unanimous.
- A1 wins on persistent/quasi-unit-root series (RandomWalk, AR1(0.95)) by 20-25pp
  ReversionRate.
- A2's only genuine advantage: VarianceBreak (+5.8pp ReversionRate, A1's stop-out rate
  triples after the break while A2 stays flat).
- A2's apparent Trending "advantage" (100% immediate stop-out vs A1's 20%) was ruled a
  metric artifact, not a real edge: an uninformative near-zero-width stop.
- Discovery: `AR1_phi0.5≡MeanRevertingOu_k0.5`, `AR1_phi0.95≡MeanRevertingOu_k0.05` —
  byte-identical series. 11 dataset labels = 9 independent series.
- Next-sprint recommendation: test whether `VolatilityRegime` (already computed,
  unused for this purpose) can gate the sigma choice per entry instead of globally.
  This sprint tests exactly that, named HYBRID for this sprint only.

============================================================
5. DATASET DEDUPLICATION
============================================================

`Hybrid/IndependentDatasetCatalog.cs` labels every row `IndependentDataset` (bool) +
`AliasOf` (string). The main comparison grid still runs all 11 historical labels for
continuity; every scientific reduction (dataset summary, overall comparison,
sensitivity, TRAIN/VALIDATION/TEST conclusions, the Hybrid-vs-A1 delta) filters to
`IndependentDataset == true` first.

============================================================
6. INDEPENDENT DATASET COUNT
============================================================

**9 independent series, 2 aliases.**

Independent: WhiteNoise, RandomWalk, AR1_phi0.5, AR1_phi0.95, Trending, LowVolatility,
HighVolatility, StructuralBreak, VarianceBreak.
Aliases: MeanRevertingOu_k0.5 (= AR1_phi0.5), MeanRevertingOu_k0.05 (= AR1_phi0.95).

============================================================
7. REGIME AUDIT
============================================================

`VolatilityRegime` (`Engine/ScientificModels/Context/VolatilityModel.cs`,
`ClassifyVolatilityRegime`): `relativeVolatility<0.9 && percentile<0.33`→LOW;
`relativeVolatility>1.1 && percentile>0.66`→HIGH; else MEDIUM; non-finite→UNKNOWN, with
a guard forcing LOW when `currentVolatility≤1e-9`. `CurrentVolatility` is a 20-bar
trailing stdev of raw price *differences* — a different, independent computation from
`InnovationStd` (whole-history Kalman innovation stdev); `VolatilityModel` only reads
`InnovationStd` as an upstream validity gate, never as an input to the regime math.

**Empirical distribution observed** (9 independent datasets, all 3 splits, scale=1,
112,662/... entries — see `VolatilityRegimeLookAheadAuditTests`, 11-label superset):
MEDIUM 7508, HIGH 7378, LOW 3891, **UNKNOWN 0**. On the independent-9-dataset,
scale=1 subset specifically: LOW 3076, MEDIUM 5933, HIGH 6354, UNKNOWN 0.

**UNKNOWN was never observed, on any dataset, split, or scale.** Reading
`ClassifyVolatilityRegime`'s non-finite guards against how `relativeVolatility` and
`volatilityPercentile` are actually computed (both have safe numeric fallbacks before
reaching the classifier — `relativeVolatility` defaults to 1.0, `percentile` defaults to
0.0, neither can produce NaN/Infinity through the code path used here), the UNKNOWN
branch may be structurally unreachable in this codebase as currently written, not merely
rare. Direct consequence: **`HybridStrict` and `HybridConservative` are numerically
identical in every single output row of this entire campaign** — the two variants exist
only to differ on UNKNOWN handling, and UNKNOWN never occurs, so the brief's §7 question
("does the result depend on the UNKNOWN policy") is answered: **no, not on this data —
the choice is moot.**

============================================================
8. LOOK-AHEAD AUDIT
============================================================

**PASSED.** `VolatilityRegimeLookAheadAuditTests.AssertVolatilityFieldsIdenticalUnderTruncation`
computed `VolatilityRegime`, `CurrentVolatility`, and `VolatilityPercentile` from both the
full series and a series truncated immediately after the bar under test, across
WhiteNoise/VarianceBreak/Trending/RandomWalk/HighVolatility at 11 bar indices including
30/60/100/150/250/**299/300/301** (straddling the VarianceBreak breakpoint) /350/450/598.
Every field was byte-identical in every case. No look-ahead detected; not worked around.

============================================================
9. A1 DEFINITION
============================================================

`StopDistance = k × InnovationStd` — reused unchanged from
`CandidateStopDistance.Compute(CandidateKind.A1, entry, k)`, called by delegation, never
reimplemented.

============================================================
10. A2 DEFINITION
============================================================

`StopDistance = k × CurrentVolatility` — reused unchanged from
`CandidateStopDistance.Compute(CandidateKind.A2, entry, k)`, called by delegation, never
reimplemented.

============================================================
11. HYBRID DEFINITION
============================================================

Locked before the campaign ran, logged to console and `hybrid_definition_lock.txt`:

```
HYBRID_STRICT:              HYBRID_CONSERVATIVE:
HIGH -> A2                   HIGH -> A2
LOW -> A1                    LOW -> A1
MEDIUM -> A1                 MEDIUM -> A1
UNKNOWN -> NOT_APPLICABLE    UNKNOWN -> A1
```

No other rule was implemented. Not modified after observing any TRAIN, VALIDATION, or
TEST result.

============================================================
12. HYBRID VARIANTS
============================================================

Both variants were run (both were needed to test the UNKNOWN-dependence question). As
established in §7, they are **numerically identical throughout** because UNKNOWN never
occurs on this dataset battery. All findings below apply equally to both; where a
number is quoted it is the same for `HybridStrict`/`HybridConservative`.

============================================================
13. OVERALL COMPARISON
============================================================

Pooled over the 9 independent datasets (entry-count-weighted mean), scale=1, k=2.0
(`A1_vs_A2_EntryAnalysis`'s existing comparison anchor, reused, not a new choice):

| Split | Candidate | ReversionRate | StopHitRate | UndeterminedRate |
|---|---|---|---|---|
| TRAIN | A1 | 84.38% | 18.45% | 10.35% |
| TRAIN | A2 | 79.32% | 36.15% | 0.27% |
| TRAIN | HYBRID | 84.09% | 25.72% | 1.02% |
| VALIDATION | A1 | 83.75% | 16.91% | 10.95% |
| VALIDATION | A2 | 80.86% | 31.81% | 0.33% |
| VALIDATION | HYBRID | 83.38% | 25.25% | 1.37% |
| TEST | A1 | 83.52% | 17.87% | 11.40% |
| TEST | A2 | 80.75% | 33.04% | 0.33% |
| TEST | HYBRID | 82.99% | 26.67% | 1.76% |

HYBRID sits strictly between A1 and A2 on ReversionRate at every split (A1 − HYBRID =
0.29pp / 0.37pp / 0.53pp — small, and growing slightly toward TEST, never reversing).
HYBRID's StopHitRate is much closer to A2's than to A1's (+7.3 to +8.8pp above A1) —
this is disproportionately driven by Trending (§17).

============================================================
14. REGIME-BY-REGIME COMPARISON
============================================================

Independent datasets, scale=1, all 3 splits pooled, k=2.0. **Corrected numbers** (an
initial file-read glitch produced transient wrong totals during this analysis; the
values below were reproduced identically across three independent re-reads and are
verified against a plain `uniq -c` row count):

| Regime | N | Mean Ratio (CurVol/InnovStd) | A1 RevRate | A2 RevRate |
|---|---|---|---|---|
| LOW | 3,076 | 0.69 | 95.48% | 87.00% |
| MEDIUM | 5,933 | 0.88 | 93.92% | 90.09% |
| HIGH | 6,354 | 0.91 | 68.90% | 67.94% |
| HIGH (excl. Trending) | 4,699 | — | 93.17% | 91.87% |

**Q1 — does HIGH correspond to A2 being better?** Only partially, and only on one
dataset. Per-dataset HIGH-regime breakdown (N, A1 RevRate, A2 RevRate):

| Dataset | N (HIGH) | A1 | A2 | Δ(A2−A1) |
|---|---|---|---|---|
| VarianceBreak | 1,048 | 92.75% | 99.43% | **+6.68pp** |
| Trending | 1,655 | 0.00% | 0.00% | 0 (but StopHitRate A1=20% vs A2=100%, §17) |
| HighVolatility/LowVolatility/WhiteNoise | 521 each | 99.23% | 99.81% | +0.58pp (negligible) |
| StructuralBreak | 539 | 98.52% | 98.89% | +0.37pp (negligible) |
| AR1_phi0.95 | 532 | 84.96% | 70.49% | **−14.47pp** |
| RandomWalk | 525 | 77.14% | 64.95% | **−12.19pp** |
| AR1_phi0.5 | 492 | 94.92% | 94.72% | −0.20pp (negligible) |

HIGH genuinely favors A2 on exactly one dataset (VarianceBreak). On the two persistent
series, the HIGH-regime subset is precisely where **A1's advantage is largest** — the
opposite of the hypothesis. HIGH is entirely uninformative on Trending (both 0%
ReversionRate) while hiding a catastrophic StopHitRate difference.

**Q2 — do LOW/MEDIUM favor A1?** Yes, consistently (LOW: +8.5pp, MEDIUM: +3.8pp).
Correctly routed by HYBRID.

**Q3 — does the ratio separate cleanly by regime?** Weakly and non-monotonically in the
way that matters: mean ratio rises LOW(0.69)→MEDIUM(0.88)→HIGH(0.91), which looks
monotonic in isolation, but Trending — the dataset with the *lowest* ratio in the whole
campaign (≈0.005, Sprint 15.12) — is classified HIGH ~97% of the time (§17). The
regime signal (`CurrentVolatility/ReferenceVolatility`, a *relative-to-recent-history*
ratio) and the mechanism ratio that actually explains A1-vs-A2 performance
(`CurrentVolatility/InnovationStd`, Sprint 15.12 §8) are different quantities that
usually move together but decouple exactly on Trending — the one case where it matters
most.

**Q4 — does the gain come only from VarianceBreak?** Yes. It is the only dataset with a
double-digit-adjacent, unambiguous, mechanistically explained A2/HYBRID advantage.

**Q5 — does the result hold on other series?** No — it reverses on RandomWalk and
AR1(0.95), and is a wash (ReversionRate) but a real defect (StopHitRate) on Trending.

============================================================
15. VARIANCEBREAK
============================================================

Breakpoint re-derived as `SeriesLength/2 = 300` (`VarianceBreakInfo`, matching
`SyntheticSeriesCatalog.VarianceBreak`'s own private local — not exposed, not invented,
same approach as Sprint 15.12). TRAIN split, scale=1, k=2.0:

| Period | Regime mix (L/M/H) | A1 RevRate | A2 RevRate | HYBRID RevRate |
|---|---|---|---|---|
| Before (N=270) | 71/108/91 (34% HIGH) | 98.15% | 98.89% | 98.52% |
| After (N=299) | 3/19/277 (93% HIGH) | **88.63%** | 98.99% | **98.66%** |

A1 degrades sharply after the break (−9.5pp), exactly reproducing Sprint 15.12's
finding (whole-history `InnovationStd` stays anchored to the pre-break lower-variance
regime, becoming too tight). A2 stays flat. **HYBRID tracks A2 almost exactly
post-break (98.66% vs 98.99%, 0.33pp behind) while staying close to A1 pre-break** —
this is the sprint's one clean, unambiguous positive result. The HIGH-regime share
jumps from 34% to 93% essentially at the break itself (this partition is a coarse
before/after split, not a bar-by-bar lag measurement, but the concentration is strong
enough — combined with `VolatilityModel`'s 20-bar window — to support "reacts within
about one window," not "reacts too late to matter"). VALIDATION and TEST splits show
the same qualitative pattern (A1 drop of 8–10pp after break; HYBRID staying within 1-2pp
of A2 post-break).

============================================================
16. RANDOMWALK
============================================================

TRAIN, scale=1, k=2.0: A1 ReversionRate 85.94%, A2 62.21%, HYBRID 82.07%
(HybridA2Share = 25.0%). HYBRID gives back 3.86pp of A1's advantage relative to A1
alone, but retains the great majority of it versus A2 (recovers ~87% of A1's margin
over A2). In the HIGH-regime subset specifically (the entries HYBRID routes to A2),
A1 still beats A2 by 12.19pp (§14) — HYBRID's routing decision is wrong for these
specific entries, not merely suboptimal on average.

============================================================
17. AR1(0.95)
============================================================

TRAIN, scale=1, k=2.0: A1 ReversionRate 89.10%, A2 63.80%, HYBRID 84.53%
(HybridA2Share = 26.2%). Same pattern as RandomWalk: HYBRID gives back 4.57pp versus
A1 alone while retaining most of the advantage over A2. In the HIGH-regime subset, A1
beats A2 by 14.47pp — again the routing decision is wrong specifically where it fires.

============================================================
18. TRENDING
============================================================

**HYBRID does not avoid A2's Trending defect — it reproduces it almost completely.**

- `HybridA2Share` ≈ 97.4–97.9% across TRAIN/VALIDATION/TEST, stable across every scale
  (0.01 through 1000, exactly constant to 6 decimal places — confirmed scale-invariant,
  §19).
- Regime mix on Trending: ~97–98% HIGH, ~2–3% MEDIUM, 0% LOW, at every scale.
- A1: StopHitRate 20.4%, ReversionRate 0%, MeanStopDistance ≈3,843 (in price units at
  scale=1), UndeterminedRate ≈79.6% (stays open, unresolved, per Sprint 15.12).
- A2: StopHitRate 100%, ReversionRate 0%, MeanStopDistance ≈19.4 — ~200× tighter than
  A1's, an uninformative near-zero stop (Sprint 15.12's "metric artifact" finding).
- **HYBRID: StopHitRate 100%, MeanStopDistance ≈19.8–22.4** — statistically
  indistinguishable from A2's own numbers.

This is reported as a defect inherited from A2 via the regime-routing mechanism, not as
HYBRID "correctly detecting a regime change." Trending's actual mechanism ratio
(CurrentVolatility/InnovationStd ≈ 0.005, Sprint 15.12) says A2 is a terrible choice
here; `VolatilityRegime` says HIGH (recommending A2) anyway, because `relativeVolatility`
measures recent-vs-own-history volatility, which is structurally elevated on a
deterministically drifting series regardless of the absolute InnovationStd comparison
that actually matters for stop calibration.

============================================================
19. SCALE INVARIANCE
============================================================

**Fully holds, no exceptions found.** Across all 648 (dataset × split × scale ×
candidate) combinations in `A1_A2_Hybrid_scale_analysis.csv`: `MeanStopRatio == K`
**exactly** (max absolute deviation measured: 0.0000000000) for A1, A2,
HybridStrict, and HybridConservative alike — the exact-scale-invariance result from
Sprint 15.11 extends cleanly to HYBRID. `HybridA1Share`/`HybridA2Share`/
`HybridUnknownShare` are **exactly constant across all 6 scales for every one of the 9
independent datasets** (measured range = 0.000000 in every case) — `VolatilityRegime`
classification is provably unaffected by a pure price-unit rescale, as expected from its
formula (`relativeVolatility` and `percentile` are both scale-invariant ratios/ranks by
construction). No STOP condition was triggered; nothing here needed further
documentation beyond this confirmation.

============================================================
20. TRAIN
============================================================

ReversionRate: A1 84.38% > HYBRID 84.09% > A2 79.32%. StopHitRate: A1 18.45% <
HYBRID 25.72% < A2 36.15%. Ordering matches the pooled overall picture (§13).
Exploration only — no rule was chosen or tuned from this split; the HYBRID rule was
already locked in §11 before this analysis ran.

============================================================
21. VALIDATION
============================================================

ReversionRate: A1 83.75% > HYBRID 83.38% > A2 80.86%. Same ordering as TRAIN, margin
essentially unchanged (A1−HYBRID: 0.37pp vs TRAIN's 0.29pp). Confirms TRAIN's picture;
no rule was adjusted based on this split either.

============================================================
22. TEST
============================================================

Computed and inspected **last**, after §20/§21. ReversionRate: A1 83.52% > HYBRID
82.99% > A2 80.75%. Same ordering as TRAIN and VALIDATION, margin (A1−HYBRID = 0.53pp)
slightly wider than TRAIN/VALIDATION but the same sign. **No rule was modified after
observing TEST** — the HYBRID definition in §11 is the same one locked before any split
was analyzed. TEST confirms, rather than changes, the conclusion.

============================================================
23. SENSITIVITY
============================================================

Leave-one-independent-series-out, TRAIN, scale=1, k=2.0, unweighted mean of the 9
per-dataset (HybridStrict − A1) ReversionRate deltas:

| Excluded | Included N | Mean Δ (Hybrid − A1) | Sign flipped vs. full set? |
|---|---|---|---|
| None (full set) | 9 | **−0.29pp** | — |
| RandomWalk | 8 | **+0.15pp** | **Yes** |
| AR1_phi0.95 | 8 | **+0.24pp** | **Yes** |
| VarianceBreak | 8 | −1.01pp | No |
| Trending | 8 | −0.33pp | No |
| StructuralBreak | 8 | −0.35pp | No |

The sign flips in 2 of 5 variants (excluding either persistent-series dataset). All
magnitudes are under 1.1 percentage points either way — this is a **small, fragile**
effect by the brief's own criterion (§18/§19: sign-flipping under leave-one-out means a
fragile conclusion). Note this is a different, narrower claim than §20–22: the
**ordering** A1 > HYBRID > A2 held across all three splits without flipping; it is the
**magnitude and sign of the small residual Hybrid-vs-A1 gap**, decomposed dataset by
dataset, that is fragile. Both statements are true simultaneously and are not in
tension: a consistently-small negative average can still flip sign when any single
contributing dataset is removed.

============================================================
24. COMPLEXITY COMPARISON
============================================================

- **A1** / **A2**: one sigma source, no extra state, no branching.
- **HYBRID**: sigma choice **plus** `VolatilityRegime` (a 4-branch classifier over two
  derived quantities — `relativeVolatility`, itself `CurrentVolatility/ReferenceVolatility`
  where `ReferenceVolatility` is a rolling average of 20-bar windows, and
  `VolatilityPercentile`, an empirical rank against prior 20-bar windows — with two
  hand-set thresholds, 0.9/1.1 and 0.33/0.66) **plus** a routing rule **plus** (for the
  Conservative variant) an UNKNOWN fallback branch that, per §7, never actually fires on
  this data.

For a net result that is, on the primary A1-comparison, a small and fragile
underperformance with one real localized win (VarianceBreak) and two real localized
costs (Trending's inherited artifact, partial erosion on RandomWalk/AR1(0.95)), the
added complexity is not obviously justified by the aggregate numbers. It is justified
specifically and only for the VarianceBreak use case in isolation.

============================================================
25. FAILURE MODES
============================================================

1. **Trending**: HYBRID routes ~97% of entries to A2, reproducing A2's uninformative
   near-zero stop and 100% immediate StopHitRate almost exactly (§18).
2. **Persistent series (RandomWalk, AR1(0.95))**: ~25–26% of entries route to A2 despite
   A1 winning by 12–14pp specifically within that HIGH-regime subset (§16/§17) — the
   routing decision is actively wrong, not merely noisy, on exactly these entries.
3. **Regime/mechanism mismatch**: `VolatilityRegime`'s `relativeVolatility` and the
   `CurrentVolatility/InnovationStd` ratio that actually explains A1-vs-A2 performance
   (Sprint 15.12) are different quantities that decouple on Trending specifically — the
   worst possible case for that decoupling to occur (§14 Q3).
4. **UNKNOWN never occurs**: HybridStrict/HybridConservative's only designed difference
   is untested by this campaign (§7/§12).

============================================================
26. SCIENTIFIC DECISION
============================================================

**A1 > HYBRID > A2.** HYBRID is a real, measurable improvement over A2 (avoids the
worst of A2's persistent-series weakness and stationary-series parity while gaining
most of A2's VarianceBreak robustness) but does not improve on A1 overall — it trades a
small, fragile net loss (driven by Trending's inherited defect and partial erosion on
two persistent series) against one genuine, mechanistically sound, but dataset-specific
win (VarianceBreak). The direction of this three-way ordering was stable across
TRAIN/VALIDATION/TEST; the *size* of the HYBRID-vs-A1 gap is not robust to which
dataset is excluded.

============================================================
27. LIMITATIONS
============================================================

- Real market validation: NOT PERFORMED (as in every prior QDE-012 sprint) — every
  conclusion here is synthetic-data-only.
- Kalman `processNoise=0.1×measurementNoise` caveat (Sprint 15.10) still applies to
  every `InnovationStd`-based number, i.e. every A1 and every LOW/MEDIUM-routed HYBRID
  entry.
- The AR1≡MeanRevertingOu aliasing (§5/§6) applies here exactly as in Sprint 15.12.
- `ComparisonK=2.0` is reused from Sprint 15.12's own precedent, not re-derived for this
  sprint; §13–§23's entry-level/reduction analyses are all reported at this single k. The
  full k-grid (0.25→10.0) is available unreduced in `A1_A2_Hybrid_comparison.csv` for
  anyone who wants to check whether the picture changes materially at other k — it was
  not separately swept dataset-by-dataset in this report.
- One transient file-read anomaly occurred while analyzing `A1_A2_Hybrid_regime_analysis.csv`
  during this report's preparation (a first read produced internally-inconsistent regime
  counts, most likely a local file-sync artifact on this OneDrive-synced working
  directory catching the file mid-hydration); it was caught by a row-count cross-check
  against the file's own header, and every number quoted in §14 was reproduced
  identically across three independent re-reads before being used. Noted here for
  transparency, not because it is believed to affect any number in this report.
- UNKNOWN's non-reachability (§7) is an observation about this codebase's current
  `VolatilityModel` formula on this dataset battery, not a proof that UNKNOWN can never
  occur under any input.

============================================================
28. PRODUCTION IMPLICATIONS
============================================================

None. No k was selected. No Risk Engine, production Stop Loss, or trading parameter was
created or implied by this result. Per the sprint's scope (§27 of the brief), this
applies regardless of which candidate "won."

============================================================
29. NEXT SPRINT RECOMMENDATION
============================================================

Two options, not a forced choice, consistent with Sprint 15.12's own framing:

1. **Do not pursue a VolatilityRegime-gated hybrid further as currently defined.** Its
   one real advantage (VarianceBreak) does not outweigh its costs (Trending, persistent
   series), and the net effect is small and fragile. A regime signal built specifically
   to detect variance breaks (rather than the general-purpose LOW/MEDIUM/HIGH classifier
   reused here) might isolate VarianceBreak's benefit without Trending's cost — but per
   this sprint's explicit anti-overfitting rule (brief §21), that would be a **new**
   hypothesis for a **future** sprint, not a same-sprint adjustment.
2. **Real market data acquisition** (repeated recommendation from Sprint 15.11/15.12,
   still not performed) — every conclusion in three consecutive sprints now rests on
   synthetic series only.

============================================================
PRODUCTION GATE (§22/§29 of the brief)
============================================================

- Files created: 21 (18 under `Tests/Research/StopLossCalibration/Hybrid/`, 3 under
  `Tests/XunitWrappers/`), plus this report and the CSV/txt outputs under
  `Tests/Research/StopLossCalibration/Output/`.
- Files modified: none.
- Production files modified: **zero**, confirmed via `git status --short` /
  `git diff --stat` before and after this sprint's work — every pre-existing `M` entry
  in git status predates this session.
- Tests: Before 60/60 passed, 0 failed, 0 skipped (3m 37s). After 63/63 passed, 0
  failed, 0 skipped (5m 39s) — the 3 new tests are `HybridConformanceXunitTests`,
  `HybridLookAheadAuditXunitTests`, `HybridSprintXunitTests`. Zero regressions.
- No k selected. No Risk Engine created. No production SL created or displayed.
