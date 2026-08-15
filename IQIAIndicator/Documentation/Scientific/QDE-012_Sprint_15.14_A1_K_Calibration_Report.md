============================================================
QDE-012 — SPRINT 15.14 — A1 K-CALIBRATION & ROBUST STOP REGION
============================================================

Date: 2026-08-12
Protocol: QDE-012 v1.1 (locked), unchanged by this sprint.
Scope: RESEARCH ONLY. No production file was modified.

============================================================
1. VERDICT
============================================================

**ROBUST K REGION FOUND — QUALIFIED.**

A single, wide, mutually-overlapping k-region — approximately **k ∈ [2.5, 10.0]** as a
conservative "already at ceiling" reading, or **k ∈ [0.5, 10.0]** under the sprint's
formal CV≤10% diagnostic (see §9/§17 for why these differ) — survived TRAIN discovery,
VALIDATION confirmation, TEST confirmation (evaluated last, unmodified), and
leave-one-independent-series-out, for both the POOLED curve and every individual
independent dataset except Trending. It is QUALIFIED, not unqualified, because: (a)
Trending is fundamentally different — ReversionRate ≡ 0% at every k, by construction,
not a region in any useful sense — and its presence lowers the POOLED region's level
substantially even though it doesn't destabilize it; (b) VarianceBreak's post-break
period needs a visibly larger k than its pre-break period to reach the same ceiling;
(c) the diagnostic window bounds are wider than the region's true "at-ceiling" plateau
because they include a still-rising low-k tail (§17).

No k was selected for production. No Risk Engine, production Stop Loss, or trading
parameter was created or implied.

============================================================
2. OBJECTIVE
============================================================

Sprint 15.13 concluded A1 > HYBRID > A2; HYBRID was not pursued further. A1 is now the
sole candidate. The question is exclusively whether a **region** of k is robust for A1
(`StopDistance = k × InnovationStd`) across TRAIN → VALIDATION → TEST and
leave-one-independent-series-out — not what the single best k is. A point optimum
(e.g. "k=2.75 excellent, k=2.50/3.00 poor") would not qualify; a run of ≥5 consecutive,
qualitatively-consistent k values would.

============================================================
3. PROTOCOL
============================================================

QDE-012 v1.1 (locked), Sprint 15.12 and 15.13 reports read before any code was written.
Existing components audited and reused unmodified: `CandidateStopDistance`,
`CandidateAggregator`, `CampaignDatasetCatalog`, `CampaignGrids`, `StableRegionAnalyzer`,
`CalibrationEntryBuilder`, `StopLossEvaluator`, `OutcomeSimulator`, `BarMetricsComputer`,
plus Sprint 15.13's `Hybrid.IndependentDatasetCatalog` / `Hybrid.VarianceBreakInfo`
(reused rather than re-declaring the same 9-independent/2-alias list and breakpoint a
third time).

============================================================
4. CANDIDATE A1
============================================================

`StopDistance = k × InnovationStd`, called via
`CandidateStopDistance.Compute(CandidateKind.A1, entry, k)` — the exact, unmodified
Sprint 15.10 formula, delegated to directly, never reimplemented.
`A1CalibrationConformanceTests.AssertA1DelegatesToCandidateStopDistance` spot-checks this
equality on 20 entries × 4 k values.

============================================================
5. DATASET BATTERY
============================================================

11 historical labels retained for the raw grid (continuity); every scientific reduction
filters to the **9 independent series**: WhiteNoise, RandomWalk, AR1_phi0.5, AR1_phi0.95,
Trending, LowVolatility, HighVolatility, StructuralBreak, VarianceBreak.
`MeanRevertingOu_k0.5`/`MeanRevertingOu_k0.05` are labeled aliases of `AR1_phi0.5`/
`AR1_phi0.95` and never double-counted in any reduction.

============================================================
6. K-GRID
============================================================

0.25 → 10.00, step 0.25, **exactly 40 values**. Unmodified from QDE-012 v1.1 throughout
— verified by `A1CalibrationConformanceTests` (reuses `ConformanceTests.Test1_GridConformance`)
both before and structurally guaranteed after (the grid is a read-only static list,
never mutated at runtime).

============================================================
7. METHODOLOGY
============================================================

Full grid — 11 datasets × 3 splits × 6 scales × 40 k = 7,920 (dataset,split,scale,k)
combinations — computed in one pass via `CandidateAggregator.Aggregate("A1", ..., k,
null, entries)`, the existing, unmodified Sprint 15.10 aggregator (produces every
required metric: TotalEntries/ApplicableEntries/DegenerateEntries, StopHitRate,
ReversionRate, UndeterminedRate, StopHitBeforeEquilibriumRate, FalseInvalidationRate,
TrueInvalidationRate — both exploratory, never used for gating — MAE/MFE
mean/median/P75/P90, MedianTimeToEquilibrium, MeanStopDistance, MeanStopRatio).
112,662 entries analyzed, 57s elapsed.

**Region discovery/confirmation pipeline** (all new this sprint, built on the above):
1. TRAIN discovers candidate regions per scope (each of the 9 independent datasets, plus
   a POOLED curve = entry-count-weighted aggregation across all 9) via
   `StableRegionAnalyzer.FindStableWindows` — ≥5 consecutive k, ReversionRate CV≤10%,
   reused unmodified. **10% CV is an exploratory diagnostic threshold, not a locked
   scientific criterion** (QDE-012 §17; restated here per this sprint's brief §13).
2. VALIDATION and TEST only ever re-evaluate the EXACT k-range TRAIN found, never
   redefine it. Two separately-reported (never blended into one score, per §25) checks:
   `CvHolds` (same 10% threshold, reused) and `MeanHolds` (mean ReversionRate over that
   k-range drifts ≤5 percentage points from TRAIN — a second simple, documented,
   exploratory tolerance). TEST additionally classifies CONFIRMED / PARTIALLY_CONFIRMED
   / REJECTED per §17, chained on whether VALIDATION already held.
3. Leave-one-independent-series-out recomputes the POOLED region's stability with each
   of the 9 datasets excluded in turn.
4. Curve-shape classification (7 categories, rule-based, no composite score) per scope.

**A bug found and worked around, not hidden (see §19 Limitations):**
`StableRegionAnalyzer.MergeOverlapping` (Sprint 15.10, unmodified by this sprint) merges
adjacent 5-point stable windows by extending `EndKIndex`/`EndK` only — it never
recomputes `MeanReversionRate`/`ReversionRateCv`/`MeanFalseInvalidationRate` for the
merged window, so those three fields on any *merged* (wider than 5 points) window
silently reflect only the *first* 5-point sub-window in the chain, not the full reported
k-range. This sprint's own region-discovery and curve-shape code recompute these three
values directly from the correctly-bounded merged slice instead of trusting those stale
fields (fixed in `A1StableRegionDiscovery.ToRegionCandidate` and
`A1CurveShapeAnalysis.Classify`) — `StableRegionAnalyzer.cs` itself was left unmodified
(out of this sprint's scope; it is shared, previously-"validated" Sprint 15.10
infrastructure). Window *bounds* were never affected, only the descriptive mean/CV
attached to merged windows.

============================================================
8. TRAIN FINDINGS
============================================================

Per-dataset ReversionRate(k) curves (TRAIN, scale=1) all rise steeply from k=0.25 and
saturate by roughly k≈2.5–3.0, then stay essentially flat to k=10 — e.g.:

| Dataset | k=0.25 | k=1.0 | k=2.0 | k=2.5 | k=3.0 | k=10.0 |
|---|---|---|---|---|---|---|
| RandomWalk | 53.4% | 78.7% | 85.9% | 86.1% | 86.1% | 86.1% |
| AR1_phi0.95 | 48.0% | 75.7% | 89.1% | 91.2% | 91.6% | 91.6% |
| AR1_phi0.5 | 62.0% | 82.2% | 97.0% | 99.1% | 99.1% | 99.3% |
| StructuralBreak | 75.6% | 94.9% | 98.4% | 99.1% | 99.5% | 99.5% |
| WhiteNoise/HighVol/LowVol | 72.8–79.9%¹ | 89.9–91.0%¹ | 98.6% | 99.8% | 99.8% | 99.8% |

¹ split-dependent at low k, converges by k≈2.

This is the classic Sprint 15.11 "asymptotic reversion rate reached between k≈1.5 and
k≈3.0" shape, reproduced independently here with the full 9-dataset battery.

============================================================
9. STABLE REGIONS
============================================================

`StableRegionAnalyzer` (≥5 consecutive k, CV≤10%) found **one merged region per scope**,
covering nearly the entire grid in every case (TRAIN, scale=1):

| Scope | k-range | Width | TRAIN mean RevRate | TRAIN CV | DegenerateNearZero |
|---|---|---|---|---|---|
| WhiteNoise / HighVolatility / LowVolatility | [0.25, 10.0] | 40 pts | 97.6% | 6.0% | No |
| StructuralBreak | [0.25, 10.0] | 40 pts | 97.9% | 4.7% | No |
| VarianceBreak | [0.25, 10.0] | 40 pts | 96.4% | 6.8% | No |
| AR1_phi0.5 | [0.5, 10.0] | 39 pts | 96.8% | 6.7% | No |
| RandomWalk | [0.5, 10.0] | 39 pts | 85.0% | 4.6% | No |
| AR1_phi0.95 | [0.75, 10.0] | 38 pts | 89.8% | 5.2% | No |
| Trending | [0.25, 10.0] | 40 pts | 0.0% | 0.0% | **Yes** |
| POOLED (8 real + Trending) | [0.5, 10.0] | 39 pts | 84.6% | 5.0% | No |

**Important nuance** (why §1's verdict quotes two k-ranges): because
`StableRegionAnalyzer` merges every 5-point window with CV≤10% into one contiguous
block, and the *rising* part of the curve (k≈0.25–1.5) often has low *local* CV even
while the *level* is still climbing, the reported bounds start earlier than where the
curve has actually reached its ceiling (§8). The formally-reported region
(`[0.25/0.5/0.75, 10.0]`) is correct under the stated CV diagnostic; the practically
"already at ceiling" sub-range is closer to **k≈2.5–3.0 through 10.0** for every
non-Trending, non-post-break-VarianceBreak dataset (§8's table). Both framings are
reported here rather than picking one silently.

No k was declared final at this stage — discovery only.

============================================================
10. VALIDATION
============================================================

Every TRAIN-discovered region, re-evaluated on VALIDATION over the *same* k-range
(no re-discovery): **CV holds (≤10%) for all 10 scopes; mean holds (≤5pp drift from
TRAIN) for all 10 scopes** (once the stale-merged-window bug in §7 was corrected — see
§19 for the pre-fix numbers, which spuriously showed 8/10 regions failing the mean
check due to that bug, not a real TRAIN/VALIDATION discrepancy).

============================================================
11. TEST
============================================================

Evaluated **last**, after §9/§10, on the unmodified regions. No new region was created
from TEST; no threshold was adjusted after seeing it.

| Scope | TRAIN | VALIDATION | TEST | Verdict |
|---|---|---|---|---|
| WhiteNoise/HighVolatility/LowVolatility | 97.6% | 97.6% | 97.9% | **CONFIRMED** |
| StructuralBreak | 97.9% | 97.9% | 97.8% | **CONFIRMED** |
| VarianceBreak | 96.4% | 96.6% | 97.1% | **CONFIRMED** |
| AR1_phi0.5 | 96.8% | 96.0% | 96.9% | **CONFIRMED** |
| RandomWalk | 85.0% | 80.5% | 80.2% | **CONFIRMED** |
| AR1_phi0.95 | 89.8% | 87.5% | 88.2% | **CONFIRMED** |
| Trending | 0.0% | 0.0% | 0.0% | **CONFIRMED** (trivially — always 0) |
| POOLED | 84.6% | 83.7% | 84.0% | **CONFIRMED** |

**10 of 10 scopes CONFIRMED.** TEST never diverges from TRAIN/VALIDATION by more than
~4.8pp (RandomWalk, the largest gap) for any real (non-Trending) scope, and the CV over
the region's k-range stays under 6% at TEST for every scope.

============================================================
12. LEAVE-ONE-OUT
============================================================

POOLED region `[0.5, 10.0]`, TRAIN, recomputed excluding each of the 9 independent
datasets in turn:

| Excluded | Mean ReversionRate | CV | Still stable (CV≤10%)? |
|---|---|---|---|
| None (baseline) | 84.6% | 5.00% | Yes |
| WhiteNoise | 82.9% | 5.09% | Yes |
| RandomWalk | 84.5% | 5.07% | Yes |
| AR1_phi0.5 | 83.0% | 4.75% | Yes |
| AR1_phi0.95 | 84.0% | 4.65% | Yes |
| **Trending** | **95.1%** | 5.00% | Yes |
| LowVolatility | 82.9% | 5.09% | Yes |
| HighVolatility | 82.9% | 5.09% | Yes |
| StructuralBreak | 82.8% | 5.32% | Yes |
| VarianceBreak | 83.0% | 4.96% | Yes |

**Stable under every single exclusion — never once exceeds the 10% CV threshold.** The
*level* moves meaningfully depending on whether Trending is included (84.6% with it,
95.1% without it) — exactly the expected effect of a 0%-ReversionRate dataset pulling
the weighted average down — but the *shape* (flat under the diagnostic) survives
regardless. This is a genuinely robust result, unlike Sprint 15.13's HYBRID-vs-A1
sensitivity, which flipped sign under 2 of 5 exclusions.

============================================================
13. DATASET-FAMILY ANALYSIS
============================================================

- **Persistent/near-unit-root** (RandomWalk, AR1_phi0.95): lower ceiling than the rest
  (85.0%/89.8% vs 96–98% elsewhere) but still a genuine, confirmed, wide stable region —
  A1's known relative weakness here (Sprint 15.12) is a *level* effect, not a
  *stability* problem.
- **Mean-reverting** (AR1_phi0.5): high ceiling (96.8%), confirmed, essentially
  identical shape to the stationary group.
- **Volatility** (LowVolatility, HighVolatility): identical numbers to WhiteNoise (both
  are `WhiteNoise` with a different sigma) — confirmed, high ceiling.
- **Structural** (StructuralBreak, VarianceBreak): both confirmed, high ceiling (97.9%/
  96.4%); VarianceBreak's before/after split is analyzed separately (§14).
- **Pathological/special** (Trending, WhiteNoise): Trending is NoSignal/degenerate by
  construction (§15); WhiteNoise behaves like the best-case stationary series (97.6%).

No dataset was hidden behind the POOLED average — every family's own numbers are
reported in §9/§11 individually.

============================================================
14. VARIANCEBREAK
============================================================

Breakpoint reused from `Hybrid.VarianceBreakInfo.BreakIndex` (= SeriesLength/2 = 300,
re-derived from `SyntheticSeriesCatalog.VarianceBreak`'s own private local, not
reinvented). TRAIN, k spot-checks:

| k | Before RevRate | After RevRate | Gap |
|---|---|---|---|
| 0.25 | 72.6% | 71.9% | 0.7pp |
| 1.0 | 92.6% | 80.3% | **12.3pp** |
| 2.0 | 98.1% | 88.6% | **9.5pp** |
| 3.0 | 100.0% | 97.0% | 3.0pp |
| 5.0 | 100.0% | 99.3% | 0.7pp |
| 10.0 | 100.0% | 99.7% | 0.3pp |

The post-break period needs a visibly larger k (roughly k≈3+ vs k≈2 pre-break) to reach
the same ceiling — a real, if modest, k-sensitivity to the variance regime change,
consistent with Sprint 15.12/15.13's finding that whole-history `InnovationStd` lags a
variance increase. It does not destroy the region (both periods still land in the
CONFIRMED, ≤10%-CV zone by k≈3), but it is the dataset-specific reason for this sprint's
"QUALIFIED" verdict rather than an unqualified one.

============================================================
15. TRENDING
============================================================

Examined on its own terms, not corrected for. ReversionRate ≡ 0% at every k (A1
structurally never reaches equilibrium within the 40-bar horizon on a deterministically
drifting series — Sprint 15.12/15.13's own finding, reproduced identically here).
StopHitRate falls from 98.9% (k=0.25) to 0% (k=10) while UndeterminedRate rises from
1.1% to 100% — A1 does **not** force a premature stop here (unlike A2's near-zero,
100%-immediate-stop-out behavior on the same dataset, Sprint 15.12/15.13); it stays
open/unresolved as k widens, which is the sane, non-catastrophic behavior. Trending is
flagged `DegenerateNearZero=True` and excluded from being read as a "stable finding" in
its own right, and its 0% pulls the POOLED region's level down substantially (§12) — it
does not, however, destabilize the POOLED region's shape (still CV≤10% with Trending
included). Per brief §21, this is reported plainly rather than hidden or corrected for.

============================================================
16. SCALE INVARIANCE
============================================================

Full grid (9 independent datasets × 3 splits × 40 k × 6 scales = 6,480 rows,
`A1_scale_analysis.csv`): **maximum |MeanStopRatio − K| observed = 1.78 × 10⁻¹⁵** —
floating-point noise, i.e. exact invariance, reproducing Sprint 15.11's exact-invariance
finding for A1. ReversionRate/StopHitRate at fixed (dataset, split, k) are identical
across all 6 scales in every row inspected. No STOP condition was triggered; no
exception found.

============================================================
17. CURVE-SHAPE ANALYSIS
============================================================

Rule-based classification (§7 methodology, no composite score), TRAIN, scale=1:

| Scope | Shape | Mean RevRate | CV | Widest window |
|---|---|---|---|---|
| WhiteNoise/HighVolatility/LowVolatility | PlateauRobust | 97.6% | 6.0% | 40/40 pts |
| StructuralBreak | PlateauRobust | 97.9% | 4.7% | 40/40 pts |
| VarianceBreak | PlateauRobust | 96.4% | 6.8% | 40/40 pts |
| AR1_phi0.5 | PlateauRobust | 96.0% | 8.8% | 39/40 pts |
| RandomWalk | PlateauRobust | 84.2% | 7.4% | 39/40 pts |
| AR1_phi0.95 | PlateauRobust | 88.0% | 10.5% | 38/40 pts |
| Trending | **NoSignal** | 0.0% | 0.0% | (degenerate — flat at zero, not a finding) |
| POOLED | PlateauRobust | 83.9% | 6.9% | 39/40 pts |

**9 of 10 scopes agree on PlateauRobust** — strong cross-dataset consensus, not a single
dataset's artifact (brief §24's "K CONSENSUS" concern). The one disagreement (Trending)
is explained entirely by its structural inability to revert within the horizon, not by
k-sensitivity. Read alongside §9's caveat: "PlateauRobust" here means CV≤10% over a wide
contiguous k-range, which for every dataset includes both a short rising segment
(k≈0.25–1.5–3.0) and a long true plateau (k≈2.5/3.0–10.0) — the geometry is
**rise-then-plateau**, not flat-from-the-start.

============================================================
18. ROBUSTNESS ASSESSMENT
============================================================

Against the brief's own checklist (§14/§28):
- ≥5 consecutive k values: yes, every region is 38–40 points wide.
- Not dependent on a single optimal point: yes — confirmed by construction (CV
  diagnostic) and by the curve tables in §8/§14 showing gradual, not spiky, behavior.
- Low ReversionRate variation within the region: yes, CV 4.6–10.5% across scopes.
- Acceptable StopHitRate variation: not separately gated (per §11 of the brief,
  FalseInvalidationRate/StopHitRate are descriptive), but reported throughout
  (§9/§13/§15) — no catastrophic StopHitRate behavior observed except Trending's
  expected, structural, non-region-breaking pattern (§15).
- No catastrophic FalseInvalidationRate deterioration: `TrainMeanFalseInvalidationRate`
  stays in the 1.3–3.4% range across all real scopes (§9) — reported descriptively, not
  used for gating, per brief §11/§25.
- Stable TRAIN→VALIDATION: yes, all 10/10 (§10).
- Survives leave-one-out: yes, all variants stay ≤10% CV (§12).
- Survives TEST: yes, 10/10 CONFIRMED (§11).

============================================================
19. LIMITATIONS
============================================================

- **StableRegionAnalyzer merge bug** (§7): merged windows' `MeanReversionRate`/
  `ReversionRateCv`/`MeanFalseInvalidationRate` fields were stale (reflecting only the
  first constituent 5-point sub-window), discovered when this sprint's independently-
  computed `OverallMeanReversionRate` (curve-shape analysis) disagreed with the region-
  discovery mean for the same window (83.8% vs 97.6% for WhiteNoise, pre-fix). Window
  *bounds* were unaffected. This sprint's own code was corrected to recompute these
  values directly from the window's slice; `StableRegionAnalyzer.cs` itself was left
  unmodified (shared Sprint 15.10 infrastructure, out of this sprint's scope). **This
  likely also affected the `MeanReversionRate`/`FalseInvalidationRate` figures reported
  for any merged (wider than 5-point) window in Sprint 15.10/15.11's own
  `campaign_summary.txt` outputs** — their k-region *bounds* are not believed to be
  affected (bounds are computed independently of the buggy fields), but any quoted
  "mean ReversionRate within window" figure for a merged window in those prior reports
  should be treated as potentially unreliable. This is not corrected retroactively here
  (out of scope) — flagged for awareness.
- Real market validation: NOT PERFORMED (as in every prior QDE-012 sprint).
- Kalman `processNoise=0.1×measurementNoise` caveat (Sprint 15.10) still applies to
  every `InnovationStd`-based number here — i.e. all of them, since A1 is the only
  candidate this sprint.
- The 10% CV threshold and the 5pp/10pp mean-drift tolerances are exploratory
  diagnostics, documented as such throughout, not QDE-012-locked criteria (§13/§25).
- The "practical ceiling" k≈2.5–3.0 reading (§8/§9/§17) is a descriptive observation
  from the curve tables, not a second formal criterion — presented alongside the formal
  CV-based bounds, not as a replacement for them.
- MAE/MFE percentile pooling for the POOLED scope uses an entry-count-weighted mean of
  already-computed per-dataset percentiles (documented in `A1PooledCurve.cs`), not a
  re-derived percentile over the full pooled entry set — the same convention Sprint
  15.13 used for the same reason.

============================================================
20. SCIENTIFIC DECISION
============================================================

**ROBUST K REGION FOUND — QUALIFIED.** A1's ReversionRate(k) curve is a wide,
cross-dataset-consistent, rise-then-plateau shape whose plateau portion — most
conservatively k≈2.5–3.0 through k=10.0, or k≈0.5–10.0 under the sprint's formal CV≤10%
diagnostic — survives TRAIN, VALIDATION, TEST (evaluated last, unmodified), and
leave-one-independent-series-out on 8 of 9 independent datasets. The ninth (Trending) is
structurally non-reverting regardless of k and is reported as such, not hidden or
worked around. VarianceBreak's post-break period needs a somewhat larger k within the
same region to reach its ceiling. No single k was selected; a *representative research
k*, if one is wanted for illustration only, would be **k ≈ 3.0** — the point past which
every non-Trending, non-post-break-VarianceBreak dataset has reached ≥98% of its
observed ceiling — but this is explicitly a **research-representative point only**, not
a recommendation.

============================================================
21. PRODUCTION IMPLICATIONS
============================================================

None. No k was selected for production. No Risk Engine, production Stop Loss, or
trading parameter was created, implied, or should be inferred from this result.

============================================================
22. NEXT SPRINT
============================================================

1. Real market data acquisition (repeated recommendation, still not performed across
   four consecutive sprints now).
2. If a production calibration sprint is ever authorized, it should independently
   decide how to treat Trending (excluded population vs. explicit no-stop-signal
   handling) and whether the post-break VarianceBreak sensitivity (§14) warrants a wider
   k or a separate regime-aware treatment — neither decision is made here.
3. Optional (not required): a corrected `StableRegionAnalyzer.MergeOverlapping` — fixing
   the stale merged-window fields found in §7/§19 — would benefit any future sprint that
   reads that class's descriptive statistics directly rather than recomputing them, as
   this sprint had to.
