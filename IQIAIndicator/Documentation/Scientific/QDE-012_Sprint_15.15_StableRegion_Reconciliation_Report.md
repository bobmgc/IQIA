============================================================
QDE-012 — SPRINT 15.15 — STABLE REGION ANALYZER CORRECTION
& HISTORICAL RECONCILIATION
============================================================

Date: 2026-08-13
Protocol: QDE-012 v1.1 (locked), unchanged by this sprint.
Scope: RESEARCH ONLY. Production code: untouched. This is a correction to shared test
infrastructure (`Tests/Research/StopLossCalibration/StableRegionAnalyzer.cs`) plus a
historical reconciliation — not a calibration, not an optimization, no new k selected.

============================================================
1. VERDICT
============================================================

**PASS — BUG FIXED, DESCRIPTIVE HISTORICAL VALUES CORRECTED.**

`StableRegionAnalyzer.MergeOverlapping` had a real bug: merged-window descriptive
statistics (`MeanReversionRate`, `ReversionRateCv`, `MeanFalseInvalidationRate`,
`FalseInvalidationRateCv`) were not recomputed after merging, silently keeping the first
constituent 5-point sub-window's values instead of summarizing the full merged k-range.
Window **bounds** (`StartK`/`EndK`/`StartKIndex`/`EndKIndex`) were never affected. The fix
is isolated to that recomputation. Of the five QDE-012 sprints so far, only Sprint 15.10's
raw `campaign_summary.txt` digest carried the wrong descriptive values — no published
scientific *conclusion*, in any of the five sprint reports, was derived from those
specific numbers (verified by full-text audit, §7–§11). Sprint 15.14, which discovered
this bug itself, is verified — not assumed — to have already produced numbers identical
to what the fix now produces directly.

============================================================
2. BUG DESCRIPTION
============================================================

`StableRegionAnalyzer.FindStableWindows` finds every 5-point-wide window of the k-grid
where `ReversionRate`'s coefficient of variation (CV) is ≤10%, then merges overlapping/
adjacent windows into wider contiguous regions (`MergeOverlapping`). The merge step
correctly extended the merged window's index/K bounds, but computed its
`MeanReversionRate`/`ReversionRateCv`/`MeanFalseInvalidationRate`/`FalseInvalidationRateCv`
by simply carrying forward whichever `StableWindow` object was `current` in the fold —
in practice, the **first** 5-point sub-window in the merge chain — rather than
recomputing those four fields over the full, final merged range.

============================================================
3. ROOT CAUSE
============================================================

```csharp
// pre-fix:
current = current with
{
    EndKIndex = Math.Max(current.EndKIndex, w.EndKIndex),
    EndK = Math.Max(current.EndK, w.EndK),
};
```

The `with` expression only overrides `EndKIndex`/`EndK`; every other field of `current`
(`MeanReversionRate`, `ReversionRateCv`, `MeanFalseInvalidationRate`,
`FalseInvalidationRateCv`, and even `StartKIndex`/`StartK`, though those happened to
already be correct since `current` always starts as the leftmost window) is carried
through unchanged from whatever `current` was before this merge step — i.e. the first
sub-window's own values, computed over only its own 5 points, not the eventual full span.

============================================================
4. MINIMAL REPRODUCTION
============================================================

`Tests/Research/StopLossCalibration/StableRegionAnalyzerMergeTests.cs`, TEST 3, uses
exactly the brief's own conceptual example: three chained overlapping 5-point windows
(`[0..4]`, `[1..5]`, `[2..6]`, i.e. k=1→5, k=2→6, k=3→7) that must merge into one
`[0..6]` (k=1→7) window.

Run against the pre-fix code (captured in
`Output/StableRegionAnalyzer_BugReproduction.txt`), the simpler two-window case (TEST 2)
failed immediately:

```
System.InvalidOperationException : Merged window's mean must be recomputed over all 6
points, not just the first 5. Expected=0.8266666666666668, Actual=0.8200000000000001.
```

`0.82` is exactly the mean of the first 5-point sub-window (`0.80,0.81,0.82,0.83,0.84`);
`0.8267` is the true mean of all 6 points including `0.86`. This is not a hypothetical —
it is the exact class of discrepancy Sprint 15.14 found empirically (WhiteNoise TRAIN:
83.8% reported vs 97.6% true, for a window reported as spanning k=0.25–10.00).

============================================================
5. FIX
============================================================

`MergeOverlapping` now: (1) determines the final `[StartKIndex, EndKIndex]` range for
each merged group using index arithmetic only (no `StableWindow` fields other than the
indices themselves); (2) for each final range, slices `curveOrderedByK` to exactly that
range and recomputes `MeanReversionRate`/`ReversionRateCv`/`MeanFalseInvalidationRate`/
`FalseInvalidationRateCv` from scratch, using the same `CoefficientOfVariation` helper
already used for single (unmerged) windows. `WindowSize=5` and `CvThreshold=0.10` are
unchanged; the initial per-5-point window-detection loop is unchanged; the definition of
"stable" is unchanged; no new metric was added. See `StableRegionAnalyzer.cs`'s own
updated doc comments for the full before/after description.

============================================================
6. TESTS
============================================================

`StableRegionAnalyzerMergeTests.cs`, 10 tests, all passing post-fix:

| # | Test | Result |
|---|---|---|
| 1 | Single window, no merge — statistics unchanged | PASS |
| 2 | Two overlapping windows — bounds and stats correct | PASS |
| 3 | Three chained overlapping windows (brief's own example) — merge to full range | PASS |
| 4 | Non-overlapping windows stay separate | PASS |
| 5 | Merge does not mutate source data | PASS |
| 6 | ReversionRate CV matches manual calculation | PASS |
| 7 | FalseInvalidationRate CV matches manual calculation | PASS |
| 8 | Mean≈0, non-zero values → CV=+Infinity (historical `CoefficientOfVariation` behavior, unchanged) | PASS |
| 9 | All values = 0 → CV=0 exactly | PASS |
| 10 | No merge needed — window unaffected by the fix | PASS |

Plus a new reconciliation test (`Sprint1515HistoricalReconciliationXunitTests`,
§11 below) verifying Sprint 15.14's published numbers against the now-fixed shared
analyzer directly.

**Full suite**: Before 65/65 passed, 0 failed (state immediately prior to touching
`StableRegionAnalyzer.cs` — this is Sprint 15.14's own closing "After" count). After 67/67
passed, 0 failed (the 2 new test classes: `StableRegionAnalyzerMergeXunitTests`,
`Sprint1515HistoricalReconciliationXunitTests`). **Zero regressions.**

============================================================
7. SPRINT 15.10 IMPACT
============================================================

No standalone Sprint 15.10 `.md` report exists — its findings live in
`QDE-012_StopLoss_Calibration_Protocol.md` and are cited by later sprints. Sprint 15.10's
own generated artifact that uses `StableRegionAnalyzer` is
`Tests/Research/StopLossCalibration/Output/campaign_summary.txt`
(`CampaignSummaryGenerator.WriteStableRegions`, unmodified this sprint), regenerated
deterministically every time `CampaignExecutionXunitTests` runs.

**Merge occurred**: yes, in nearly every reported window (spans of 38–40 of the 40-point
grid for A1/A2/D1 across all 11 dataset labels). **Classification: B — DESCRIPTIVE VALUES
AFFECTED.** Before/after (TRAIN, scale=1; full table in `StableRegionAnalyzer_BeforeAfter.csv`,
66 rows):

| Candidate | Dataset | Before MeanRevRate | After MeanRevRate | Before CV | After CV |
|---|---|---|---|---|---|
| A1 | WhiteNoise | 83.8% | 97.6% | 8.7% | 6.0% |
| A1 | RandomWalk | 77.3% | 85.0% | 9.2% | 4.6% |
| A1 | AR1_phi0.5 | 81.8% | 96.8% | 9.9% | 6.7% |
| A1 | AR1_phi0.95 | 79.3% | 89.8% | 7.8% | 5.2% |
| A1 | StructuralBreak | 88.4% | 97.9% | 8.9% | 4.7% |
| A1 | VarianceBreak | 81.1% | 96.4% | 7.5% | 6.8% |
| A2 | RandomWalk | 50.8% | 75.7% | 9.3% | **15.8%** |
| A2 | AR1_phi0.95 | 54.2% | 80.0% | 9.0% | **15.5%** |
| D1 | WhiteNoise | 85.1% | 97.7% | 9.6% | 5.9% |

Window **bounds were identical before and after in all 66 rows** — confirmed
programmatically (`BoundsChanged=False` everywhere in the CSV). Every merged window's
mean was understated pre-fix (biased toward the low-k, still-rising portion of the
curve), consistent with the shape found independently in Sprint 15.14 (§8 of that report).

**Notable secondary finding**: for A2 specifically, on RandomWalk and AR1_phi0.95 (its
two weakest datasets per Sprint 15.11/15.12), the *correctly recomputed* CV over the
full merged range now **exceeds** the 10% threshold (15.8%/15.5%) that each individual
5-point sub-window satisfied on its own. This is not a contradiction — local flatness of
overlapping 5-point pieces does not guarantee the full merged span is uniformly flat —
but it means those two specific A2 regions, as now correctly summarized, would not
individually re-qualify as "stable" under the same CV≤10% standard used to build them.
This is reported as a discovered nuance, not corrected further: the brief's fix scope is
the calculation, not the merge/stability logic itself (§5 of the brief).

**No downstream scientific conclusion is affected**: no report (15.11–15.14) cites
`campaign_summary.txt`'s stable-window numbers.

============================================================
8. SPRINT 15.11 IMPACT
============================================================

**Classification: A — UNAFFECTED.** Full-text audit of
`QDE-012_Sprint_15.11_A2_A1_D1_Decision_Report.md` (grep for "StableRegion", "stable
window", "coefficient of variation", "MergeOverlapping", "CV" — zero matches other than
generic prose) confirms its "Zone A/B/C/D" trade-off analysis and asymptote tables (§6/§7
of that report) use an **independently coded, different methodology**: "within 2pp of
the k=10 ceiling value," explicitly documented in that report as "a diagnostic
convention chosen for this analysis, not a protocol-locked criterion" (§12 of that
report) — never `StableRegionAnalyzer`. No code path connects Sprint 15.11's published
numbers to the buggy merge logic.

============================================================
9. SPRINT 15.12 IMPACT
============================================================

**Classification: A — UNAFFECTED.** `QDE-012_Sprint_15.12_A1_vs_A2_Decision_Report.md`
performs entry-level, per-dataset direct comparison (§9/§11/§12 of that report) and a
leave-one-out sensitivity analysis over raw `ReversionRate` deltas — no stable-window or
coefficient-of-variation gating anywhere. Zero references to `StableRegionAnalyzer` in
the report text or in the code paths (`A1VsA2EntryAnalysis.cs`) that produced its numbers.

============================================================
10. SPRINT 15.13 IMPACT
============================================================

**Classification: A — UNAFFECTED.** The Hybrid sprint (`Hybrid/` folder) built an
entirely separate aggregation pipeline (`HybridCandidateAggregator`) reusing
`StopLossEvaluator`/`CandidateStopDistance`/`CalibrationEntryBuilder` directly — it never
calls `StableRegionAnalyzer.FindStableWindows` anywhere. Confirmed by both the original
construction of that sprint's code and a fresh grep of the codebase.

============================================================
11. SPRINT 15.14 IMPACT
============================================================

Sprint 15.14 is the one sprint that **did** call `StableRegionAnalyzer.FindStableWindows`
directly (`A1StableRegionDiscovery.DiscoverFromTrain`) — and its own report already
documents finding this exact bug mid-sprint and working around it: rather than trusting
`StableWindow.MeanReversionRate`/`.ReversionRateCv`/`.MeanFalseInvalidationRate` for a
merged window, `A1StableRegionDiscovery.ToRegionCandidate` and
`A1CurveShapeAnalysis.Classify` recomputed those values directly from the merged
window's own `[StartKIndex, EndKIndex]` slice — the same fix now applied to the shared
class itself.

**Per the brief's instruction not to blindly recompute 15.14's verdict**: this sprint
added `Sprint1515HistoricalReconciliation.VerifySprint1514Unaffected` (new test, does not
touch any Sprint 15.14 file), which rebuilds the exact same TRAIN, scale=1 curves for the
9 independent datasets + POOLED, calls the **now-fixed shared** `StableRegionAnalyzer.
FindStableWindows` **directly** (not via Sprint 15.14's workaround), and compares the
result field-by-field against Sprint 15.14's already-published `A1_stable_regions.csv`.

**Result: exact match, every scope, both `MeanReversionRate` and `ReversionRateCv`, to
within floating-point tolerance (1e-6).** Confirmed by the passing test, not asserted by
inspection alone.

**Sprint 15.14's verdict — ROBUST K REGION FOUND — QUALIFIED — is CONFIRMED UNCHANGED.**
Its workaround was mathematically equivalent to this sprint's fix.

**Classification**: not a clean fit for A/B/C/D as stated in the brief (it used the buggy
component but had already neutralized the bug in its own analysis) — recorded here as
**"SELF-CORRECTED, VERIFIED EQUIVALENT"**, closest in spirit to A (no scientific
conclusion was ever affected) since Sprint 15.14's *published numbers* were correct from
the start.

============================================================
12. BEFORE/AFTER COMPARISON
============================================================

| Sprint | Used Analyzer | Merge Occurred | Before Stats | After Stats | Verdict Affected |
|---|---|---|---|---|---|
| 15.10 | Yes (`campaign_summary.txt`) | Yes (A1/A2/D1, ~all 11 datasets) | Understated means, e.g. A1 WhiteNoise 83.8%/CV8.7% | Corrected, e.g. A1 WhiteNoise 97.6%/CV6.0% (full table: `StableRegionAnalyzer_BeforeAfter.csv`) | No — no report cites these numbers |
| 15.11 | No | N/A | N/A | N/A | No |
| 15.12 | No | N/A | N/A | N/A | No |
| 15.13 | No | N/A | N/A | N/A | No |
| 15.14 | Yes (worked around) | Yes | Workaround already correct (self-fixed mid-sprint) | Verified identical to fixed shared analyzer (§11) | No — confirmed unchanged |

No value in this table was fabricated: 15.10's before/after figures come from the two
`campaign_summary.txt` snapshots (pre-fix saved to
`Output/_sprint15.15_snapshots/campaign_summary_BEFORE_FIX.txt` before any code was
touched; post-fix regenerated by the unmodified `CampaignExecutionXunitTests` after the
fix); 15.14's equivalence is a passing automated test, not an assumption.

============================================================
13. SCIENTIFIC CONCLUSIONS AFFECTED
============================================================

**None.** No published scientific verdict in Sprint 15.10 (none exists as a standalone
report), 15.11 (A1/A2/D1 `PARTIALLY VALIDATED`), 15.12 (`A1 > A2, qualified`), 15.13
(`A1 > HYBRID > A2`), or 15.14 (`ROBUST K REGION FOUND — QUALIFIED`) depended on the
buggy merged-window statistics.

============================================================
14. SCIENTIFIC CONCLUSIONS UNAFFECTED
============================================================

All five sprints' scientific conclusions stand exactly as published:
- 15.11: A1/A2/D1 `PARTIALLY VALIDATED`, Zone C ≈ k=2.0–3.5.
- 15.12: A1 > A2, qualified.
- 15.13: A1 > HYBRID > A2, HYBRID not pursued.
- 15.14: A1 `ROBUST K REGION FOUND — QUALIFIED`, k≈[0.5,10.0] formal / k≈[2.5,10.0]
  strict reading — now doubly confirmed (Sprint 15.14's own workaround, and this
  sprint's independent reconciliation against the fixed shared analyzer, agree exactly).

============================================================
15. HISTORICAL DATA AVAILABILITY
============================================================

All raw campaign outputs needed for this reconciliation were available and were used —
none were fabricated. `A1_results.csv`/`A2_results.csv`/`D1_results.csv`/
`campaign_summary.txt` (Sprint 15.10) and `A1_stable_regions.csv`/`A1_k_grid_results.csv`
(Sprint 15.14) are deterministically regenerated by unmodified, pre-existing test code
every time the full suite runs (same seeds, same grids, same QDE-012 v1.1 formulas) —
"raw output unavailable" was never needed for any sprint in this reconciliation.

============================================================
16. LIMITATIONS
============================================================

- Sprint 15.10 has no standalone narrative report to check for direct quotations of
  `campaign_summary.txt` numbers — the absence of a citeable conclusion was established
  by checking every later report (15.11–15.14) that could plausibly have inherited a
  15.10 stable-window figure, not by inspecting a document that does not exist.
- The newly-discovered A2 post-fix CV>10% cases (§7) are reported descriptively; this
  sprint does not re-run or re-gate Sprint 15.10/15.11's A2 conclusions, since A2 was
  already rejected relative to A1 in Sprint 15.12/15.13 on independent grounds, and
  re-opening that comparison is explicitly out of this sprint's scope (§19 of the brief).
- The 10% CV threshold remains an **exploratory diagnostic threshold**, not a locked
  scientific criterion (unchanged, per brief §15).
- This sprint does not audit every possible future consumer of `StableRegionAnalyzer` —
  only the five sprints named in the brief plus a full-codebase grep for other call
  sites (none found beyond `CampaignSummaryGenerator.cs` and Sprint 15.14's own files).

============================================================
17. PRODUCTION PERIMETER
============================================================

**Zero production files modified.** `git status --short` / `git diff --stat` before and
after this sprint (§18 gate below) confirm every change is under `Tests/Research/`,
`Tests/XunitWrappers/`, or `Documentation/Scientific/`. The only shared component
modified is `Tests/Research/StopLossCalibration/StableRegionAnalyzer.cs`, exactly as
authorized, and only its `MergeOverlapping` method.

============================================================
18. FINAL CONCLUSION
============================================================

The bug was real, reproducible with a 3-line hand-checkable example, and is now fixed
without changing `WindowSize`, `CvThreshold`, the stability definition, or the initial
window-detection logic. It affected descriptive statistics in one sprint's raw digest
(15.10) and was already independently neutralized in the one other sprint that used the
shared component (15.14, now verified by test rather than assumed). No scientific
verdict published across Sprints 15.10–15.14 requires revision.

============================================================
19. NEXT SPRINT
============================================================

No scientific follow-up is required by this correction. Standing recommendations from
prior sprints remain: real market data acquisition (repeated across four consecutive
sprints, still not performed). If any future sprint reads `StableRegionAnalyzer`'s
descriptive fields directly, it can now do so without the workaround Sprint 15.14 needed.
