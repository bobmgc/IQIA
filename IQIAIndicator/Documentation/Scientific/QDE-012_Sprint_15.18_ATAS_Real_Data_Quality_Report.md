============================================================
QDE-012 — SPRINT 15.18 — ATAS REAL MARKET DATA QUALITY &
OBSERVATION CONTRACT
============================================================

Date: 2026-08-14
Protocol: QDE-012 v1.1 (locked), unchanged by this sprint.
Scope: DATA QUALITY AUDIT ONLY. No A1 calibration, no k selection, no Risk Engine or stop-loss
production logic touched, no trading rule introduced, no repair of the source dataset. This sprint
answers exactly one question: **can the supplied real ATAS capture be trusted as an input dataset for
a future QDE-012 calibration campaign?**

============================================================
1. OBJECTIVE
============================================================

Sprint 15.17/15.17.1/15.17.2 proved the ATAS → `ScientificDatasetCollector` → export pipeline is
wired correctly and that ATAS really does invoke `OnDispose()`. A real ATAS capture has now been
supplied as evidence. This sprint inspects that capture's actual content - not just its metadata -
against a rigorous real-market-data quality contract, and issues an explicit admissibility gate.

This sprint does **not** determine an optimal `k`, does **not** compare A1 vs A2, and does **not**
recommend a production stop-loss. Those remain out of scope until a future sprint, per the brief.

============================================================
2. SOURCE FILES
============================================================

Two capture sessions were supplied, both produced on this machine under
`%LocalAppData%\IQIA\ScientificDataset\` by the Sprint 15.17/15.17.1/15.17.2 pipeline. Both are
committed **read-only, byte-exact** under
`IQIAIndicator/Tests/Research/StopLossCalibration/RealMarket/RawCapture/Sprint15_18/` (SHA-256
manifest: `MANIFEST_SHA256.txt` in that folder), and were never modified by this sprint - every number
in this report is computed from those exact bytes.

| File prefix | SessionId | Role in this sprint |
|---|---|---|
| `ScientificDataset_ES_M5_20260814_165421` | `967a640e-f224-4ffc-8dfc-13104179cf14` | **Primary capture.** Full quality pipeline runs on this one. 1732 written bars. |
| `ScientificDataset_ES_M5_20260814_165335` | `20c7b9c8-7d79-47f5-9ff2-b47d8e474394` | Independent, much shorter capture from the same day. Analyzed **separately**, never concatenated - used only as corroborating evidence for Section 6. |

Each session contributes its `.csv`, `.json`, `_ohlcv.csv`, `_metadata.json`, `_lifecycle.json` - 10
files total, all under the manifest above.

============================================================
3. CAPTURE METADATA (primary session, 165421)
============================================================

Taken verbatim from `ScientificDataset_ES_M5_20260814_165421_metadata.json` (production type
`ScientificDatasetMetadata`, read-only, re-asserted by
`Sprint1518RealCaptureQualityTests.Session165421_Metadata_MatchesSuppliedEvidenceExactly`):

```
Source            : ATAS
SessionId         : 967a640e-f224-4ffc-8dfc-13104179cf14
Symbol            : ES
TimeFrame         : M5
BarsReceived      : 3059
BarsWritten       : 1732
Duplicates        : 1327
InvalidRejected   : 0
OutOfOrder        : 0
FirstTimestamp    : 2026-06-29T22:00:00
LastTimestamp     : 2026-07-08T08:15:00
Timezone          : Unspecified (ATAS IndicatorCandle.Time passthrough, no conversion applied)
CollectorVersion  : 1.0-real-ohlcv
```

Arithmetic check: `3059 = 1732 + 1327 + 0` — holds exactly (asserted by test, not just eyeballed).

============================================================
4. LIFECYCLE VERIFICATION
============================================================

From `ScientificDataset_ES_M5_20260814_165421_lifecycle.json`:

```
OnDisposeEnteredAt  : 2026-08-14T14:58:23.4235642Z
ExportStartedAt     : 2026-08-14T14:58:23.4257598Z
ExportCompletedAt   : 2026-08-14T14:58:23.8256323Z
ExportFailedAt       : null
LastError            : null
```

`OnDisposeEnteredAt < ExportStartedAt < ExportCompletedAt`, `ExportFailedAt`/`LastError` both null -
the export lifecycle genuinely completed, exactly once, with no error. This confirms the Sprint
15.17.1/15.17.2 wiring works on a real ATAS session, not just in unit tests. **This alone is not
sufficient for admissibility** (Section 15) - it only proves the pipe delivered whatever ATAS put into
it, not that what ATAS put into it is scientifically usable as-is.

============================================================
5. OBSERVATION CONTRACT (reconstructed from source)
============================================================

Reconstructed by reading `ScientificDatasetCollector.cs`, `ScientificDatasetRecord.cs`,
`MarketContextBuilder.cs`, and `IQIAIndicator.cs` directly (not inferred from behavior):

- **Identity key**: `(SessionId, Symbol, TimeFrame, CurrentBar)` — a `record struct
  ObservationIdentity`, `ScientificDatasetCollector.cs:326`.
- **CurrentBar semantics**: the `bar` parameter of ATAS's `OnCalculate(int bar, decimal value)`,
  passed straight through as `ScientificDatasetRecord.From(..., currentBar: bar, ...)`
  (`IQIAIndicator.cs:447-449`). ATAS holds `bar` constant across every callback while that bar is
  still forming and increments it only when a new bar starts.
- **Timestamp semantics**: `context.Clock.CurrentTime`, itself `c.Open`/`c.High`/`c.Low`/`c.Close`'s
  sibling field on the same `IndicatorCandle c = GetCandle(bar)` object
  (`MarketContextBuilder.cs:75-82`) - i.e. the bar's **open time**, passed through unconverted.
- **SessionId semantics**: one `Guid.NewGuid()` per `EnableScientificDataset` activation
  (`IQIAIndicator.cs:253`), constant for the lifetime of that collector.
- **Symbol/TimeFrame semantics**: read once per bar from `context.Instrument.Symbol`/`context.TimeFrame`
  - both ultimately sourced from ATAS's own instrument/chart configuration, not chosen by this code.
- **Duplicate definition**: a second (or later) `Add()` call whose `(SessionId, Symbol, TimeFrame,
  CurrentBar)` tuple was already accepted. Rejected **before** being appended to `_records` - **first
  `Add()` wins**, unconditionally (`ScientificDatasetCollector.cs:99-116`). The rejected record's
  payload (its Open/High/Low/Close/Volume) is discarded and never retained anywhere.
- **Out-of-order definition**: an accepted record whose `Timestamp` is earlier than the
  previously-accepted record's `Timestamp`. Counted (`RecordsOutOfOrder`), **not rejected** - by
  design, per the class's own doc comment (ATAS Market Replay rewinds can legitimately revisit bars).
- **Invalid OHLCV definition**: structural only - `High < max(Open,Close)`, `Low > min(Open,Close)`,
  `High < Low`, or `Volume < 0` - checked **before** the dedup check, so an invalid record never
  occupies the dedup slot (`TryValidateOhlcv`, `ScientificDatasetCollector.cs:133-176`).
- **Closed vs. forming candle**: **there is no ATAS-native "bar closed" signal anywhere in this
  codebase** (confirmed absent by the Sprint 15.17 reflection audit, restated in that sprint's report
  §6). `GetCandle(bar)` returns ATAS's own `IndicatorCandle` object for the bar currently being
  processed - while that bar is still forming, its `High`/`Low`/`Close`/`Volume` fields are live and
  mutate tick-by-tick. Nothing in `MarketContextBuilder`/`IQIAIndicator` waits for or detects bar
  close before reading them.
- **Can `OnCalculate` fire more than once for the same still-forming bar?** Sprint 15.17 left this
  explicitly unconfirmed from source ("only a real ATAS run can settle this empirically" - Sprint
  15.17 report §13/§25). **This sprint's real data answers it: yes**, decisively - see Section 6.
- **Does the collector intentionally keep the first observation?** Yes, explicitly and by design
  (first-`Add()`-wins is the documented dedup policy, not an accident).
- **Is this scientifically acceptable for QDE-012?** **Not without qualification** - see Section 6.
  First-`Add()`-wins is a sound policy for genuine re-delivery of an already-closed bar (e.g. a replay
  rewind), but this sprint's real data shows it can also retain a bar's *very first, single-tick*
  snapshot when `OnCalculate` fires repeatedly while that bar is still forming - the opposite of what
  a "closed 5-minute candle" dataset needs.

============================================================
6. DUPLICATE ANALYSIS
============================================================

```
BarsReceived (Add attempts)      : 3059
BarsWritten (accepted)           : 1732
DuplicateRecordsRejected         : 1327
duplicate_rate = 1327/3059       : 43.38%
avg Add() attempts per accepted  : 1.77
```

**Why they occur**: reconstructed from source (Section 5) - a duplicate is any `OnCalculate` call
whose `bar` index matches one already accepted for this session. Nothing in the call chain
(`IQIAIndicator.OnCalculate` → `MarketContextBuilder.Build` → `ScientificDatasetRecord.From` →
`Collector.Add`) is asynchronous or batched (Sprint 15.17 report §7 confirmed no threading anywhere
in this path), so every one of the 3059 attempts is a genuine, distinct `OnCalculate` invocation from
ATAS itself - not a defect in this sprint's reading of the file.

**Can the same CurrentBar arrive with changing OHLCV before the candle closes? — CONFIRMED YES**,
by two independent pieces of real evidence:

**Evidence A - session 165335 (isolated single-bar capture).** This entire session's metadata:
`BarsReceived=71, BarsWritten=1, Duplicates=70` - **all 71 Add() attempts share the same
`CurrentBar=1030` and the same `Timestamp=2026-08-14T14:50:00`**. Its `lifecycle.json` shows
`DatasetEnabledAt=14:53:35.63Z` and `OnDisposeEnteredAt=14:54:18.70Z` - **~43 seconds of real
wall-clock time**, during which ATAS called `OnCalculate` 71 times for one still-open bar before the
indicator was removed. The bar was demonstrably not yet closed for most of that window (`Timestamp`
14:50:00 is ~3.5-4 minutes before wall-clock 14:53:35, i.e. mid-way through a 5-minute bar). Only the
*first* of those 71 snapshots survived to export; the other 70's Open/High/Low/Close/Volume are
unrecoverable (ScientificDatasetCollector never retains a rejected duplicate's payload).

**Evidence B - session 165421 (the primary capture) itself.** `RealMarketQualityAnalyzer
.DetectZeroRangeSegments` finds **exactly one contiguous run of 633 bars** (indices 1099-1731 of
1732, i.e. the entire tail of the file) where `Open == High == Low == Close`. Their `Volume`: min 1,
max 51, **median 1**. By contrast the other 1099 bars (indices 0-1098) have `Volume` min 120, median
852, max 184030, and non-zero range. A "5-minute candle" with `Volume=1` and zero range is exactly
what a *single captured print* looks like - the collector kept the very first tick of these 633 bars'
formation, not their eventual closed state. The transition point,
`idx 1099 / Timestamp 2026-07-06T01:35:00`, falls **inside** continuous run #4 (idx 1056-1331) - not
at a session-boundary gap - so this is a genuine mid-session change in capture behavior (most
plausibly: replay transitioning from fast historical catch-up, where each bar arrives once already
closed, to tick-paced forward playback, where each new bar's first tick is what the dedup keeps), not
an artifact of a market closure.

**Classification (brief's A/B/C/D taxonomy):** **Predominantly (B) repeated observation of a forming
bar** - confirmed directly for the 633-bar tail and independently corroborated by the isolated
165335 session. For the 1099 bars before the tail, individual rejected-duplicate payloads are not
retained by the collector, so their specific mechanism (A vs. B) cannot be distinguished bar-by-bar
from this evidence alone - correctly reported as **(D) ambiguous** at that granularity, even though
the aggregate mechanism (B) is now established with high confidence.

**FORMING_BAR_CAPTURE RISK: CONFIRMED, not merely theoretical.** Per the brief's explicit instruction,
this is flagged and reported, not silently fixed in this sprint - see Section 21 (Next Sprint) for the
minimal recommended correction.

Full detail: `real_market_duplicate_analysis.csv`.

============================================================
7. TIMESTAMP ANALYSIS
============================================================

```
FirstTimestamp             : 2026-06-29T22:00:00
LastTimestamp               : 2026-07-08T08:15:00
Strictly increasing         : True   (1731/1731 consecutive pairs)
Duplicate timestamps        : 0
Min / Modal / Median interval : 300s (all three identical - 1725 of 1731 intervals are exactly 5 min)
Mean interval                : 420.6s (pulled up by the 6 gaps below)
Max interval                 : 191100s (~53.1h)
Gap count (interval > 300s)  : 6
```

All 6 gaps are **exact whole multiples** of the 300-second bar spacing (no partial/irregular
interval anywhere in the file) and fall into two clean size buckets:

| Gap size | Count | From → To (example) |
|---|---|---|
| 3900s (65 min = 13×300s) | 5 | e.g. 2026-06-30T20:55 → 2026-06-30T22:00 |
| 191100s (~53.1h = 637×300s) | 1 | 2026-07-03T16:55 → 2026-07-05T22:00 |

Per the brief's instruction not to invent a trading calendar (none exists in this repository - same
finding as Sprint 15.16's audit), these are classified **structurally only**:
`MEDIUM_GAP_PLAUSIBLE_INTRADAY_CLOSURE` (×5) and `LARGE_GAP_PLAUSIBLE_MULTI_DAY_CLOSURE` (×1) - size
and whole-multiple heuristics, not a verified exchange schedule. **External commentary (not encoded
in any repo file or test assertion):** this exact pattern - five ~65-minute recurring gaps plus one
~53-hour gap - is what a domain reader would recognize as CME Globex ES's daily maintenance break and
weekend closure. That recognition is not something this repository can verify on its own, so the
formal dimension verdict stays `PASS_WITH_CAVEATS`, not a calendar-confirmed `PASS`.

No irregular or non-multiple gap exists anywhere in the file (`IRREGULAR_INTERVAL_UNRESOLVED` count:
0). Full detail: `real_market_timestamp_analysis.csv`, `real_market_gap_analysis.csv`.

============================================================
8. OHLC VALIDATION
============================================================

Every one of the 1732 written bars was checked against `High≥max(Open,Close)`,
`Low≤min(Open,Close)`, `High≥Low`:

```
Structural violations        : 0
NaN / Infinite values        : 0 (impossible by construction - decimal has no such representation;
                                  RealMarketOhlcvCsvReader would throw parsing such a token, not
                                  silently accept one)
Non-positive prices          : 0
Negative volume               : 0
Identical O=H=L=C (zero range): 633  (= the Section 6 tail segment)
```

**Structural vs. statistical, kept separate as instructed:**

| | Min | Median | Mean | Max |
|---|---|---|---|---|
| Range (High-Low) | 0.00 | 2.25 | 2.70 | **26.50** |
| Consecutive-bar return | -0.309% | 0.000% | ~0.0004% | **+0.204%** |

The 26.50-point range bar and the ±0.2-0.3% return bars are **statistically extreme, not structurally
invalid** - every one of them still satisfies all three invariants above. They are left in the raw
dataset untouched, exactly as instructed. The 633 zero-range bars are also **not** a structural
invariant violation (`O=H=L=C` trivially satisfies all three checks) - their concern is representativeness
(Section 6), not validity, which is why they are reported under Duplicate/OHLCV-integrity, not as a
"violation" here.

Full detail: `real_market_ohlcv_validation.csv`.

============================================================
9. VOLUME VALIDATION
============================================================

```
Min      : 1
Max      : 184,030
Median   : 459.5
Mean     : 2,478.6
Zero-volume rows     : 0
Negative-volume rows : 0
```

Volume is heavily right-skewed (mean far above median), which is normal for a futures instrument with
occasional large prints. Zero volume never occurs, so it was never a question of whether to reject it
(the collector's own contract only rejects `Volume<0`, never `Volume==0`). The one volume fact that
matters most for this sprint: the 633-bar zero-range segment's volume is **median 1, max 51** - two
orders of magnitude below the rest of the file (median 852 outside that segment) - which is exactly
the volume signature of a single captured print, reinforcing Section 6's conclusion independently of
the price-range evidence.

Full detail: `real_market_ohlcv_validation.csv`.

============================================================
10. GAP ANALYSIS
============================================================

See Section 7 for the full breakdown. Summary: 6 gaps total, all whole multiples of the 300-second bar
spacing, none classified as unresolved/irregular. `real_market_gap_analysis.csv` lists each gap's
exact From/To timestamps, size in seconds/minutes, implied missing interior bars if the series were
perfectly regular, and its size-bucket classification.

============================================================
11. SESSION INTEGRITY
============================================================

```
Distinct SessionId in 165421 file : 1  (967a640e-f224-4ffc-8dfc-13104179cf14)
Distinct Symbol                    : 1  (ES)
Distinct TimeFrame                 : 1  (M5)
```

No cross-session contamination anywhere in the primary file. The second supplied capture (165335,
SessionId `20c7b9c8-...`) was analyzed **strictly separately** throughout this sprint (Sections 3, 6,
and `real_market_session_analysis.csv`) and never concatenated with 165421 at any point - confirmed by
the report's own test, `Session165335_Metadata_ShowsSeventyOneAttemptsForOneBar_IndependentSession`,
which explicitly asserts the two SessionIds differ.

============================================================
12. TIMEZONE ANALYSIS
============================================================

Metadata self-reports: `"Unspecified (ATAS IndicatorCandle.Time passthrough, no conversion applied)"`.
Confirmed against source (Section 5): `MarketContextBuilder.cs` passes `IndicatorCandle.Time` straight
through with no `TimeZoneInfo`/`DateTimeKind` handling anywhere in the live pipeline - unchanged since
Sprint 15.16/15.17's audits. No timezone configuration of any kind exists anywhere else in this
repository (grep confirms zero references to `TimeZoneInfo`, `DateTimeOffset` conversion, or a named
IANA/Windows zone identifier in production code).

**verdict = TIMEZONE_UNRESOLVED.** Not fabricated, not guessed. The gap pattern in Section 7 is
*consistent* with CME Globex hours under a plausible UTC-like interpretation, but "consistent with" is
not "confirmed as" - this sprint does not convert or relabel a single timestamp.

**Minimal recommended fix** (not performed in this sprint - out of scope, and would touch
`MarketContextBuilder.cs`, which is not on this sprint's allowed-file list): capture ATAS's actual
configured chart/exchange timezone at collection time and record it in the metadata verbatim, or, if
ATAS exposes no such property, have the user record it manually alongside each capture until an
API-level answer is found.

============================================================
13. 40-BAR HORIZON ADEQUACY
============================================================

Using the 7 continuity runs found in Section 14/`real_market_horizon_analysis.csv` (run-boundary
-respecting, i.e. a window may never cross a timestamp gap):

```
Theoretical windows (naive, n - 40, ignoring gaps) : 1692
Usable windows (respecting run boundaries)          : 1452
Blocked by gaps                                     : 240
Percent usable                                      : 85.8%
Longest continuous run                               : 276 bars
```

**1452 usable 40-bar windows is numerically far more than QDE-012's minimum requirement** - this is
not in question. But this sprint explicitly does not stop at "1452 > 40": a large share of those
windows fall inside or immediately adjacent to the 633-bar suspected-forming-bar tail (Section 6). To
quantify that separately, the horizon calculation was re-run using **only the 1099 bars before the
tail begins** (idx 0-1098, i.e. the portion of the file with no confirmed forming-bar contamination):

```
Theoretical windows, clean segment only  : 1059
Usable windows, clean segment only        : 899
Percent usable                             : 84.9%
```

**899 usable windows from data with no confirmed forming-bar contamination is still comfortably
sufficient for QDE-012's horizon requirement.** The honest limitation is scope, not count: this is one
short (8.4-day), single-instrument, single-timeframe historical window, not a sample spanning multiple
independent regimes or instruments (Section 14).

============================================================
14. REAL-MARKET COVERAGE (descriptive only - no regime classification forced)
============================================================

Per-run close-to-close drift and volatility (population stdev of consecutive-bar returns), computed
strictly within each continuity run so no cross-gap "return" contaminates the numbers:

| Run | Bars | Span | Drift | Return stdev |
|---|---|---|---|---|
| 0 | 276 | 06-29 22:00 → 06-30 20:55 | +0.65% | 0.039% |
| 1 | 276 | 06-30 22:00 → 07-01 20:55 | -0.16% | 0.039% |
| 2 | 276 | 07-01 22:00 → 07-02 20:55 | -0.15% | 0.059% |
| 3 | 228 | 07-02 22:00 → 07-03 16:55 | +0.40% | 0.025% |
| 4 | 276 | 07-05 22:00 → 07-06 20:55 | +0.54% | 0.032% |
| 5 | 276 | 07-06 22:00 → 07-07 20:55 | -0.58% | 0.032% |
| 6 | 124 | 07-07 22:00 → 07-08 08:15 | +0.01% | 0.032% |

Overall price level ranged 7479.5-7601.0 (~121.5 points, ~1.6% of level) across the whole capture.
Volatility is fairly uniform across runs (0.025%-0.059% stdev), with Run 2 mildly elevated - not a
dramatic regime break by any measure available here. **No formal trending/mean-reverting/random-walk
classification is asserted** - QDE-012's own scientific models (Kalman/OU/ADF/KPSS/DFA) are the
repository's actual tool for that question, and running them is out of this sprint's scope (it would
mean running exactly the kind of analysis Section 20 forbids). Finer regime labeling: **UNKNOWN** -
what this sprint can say is that the capture shows real, non-trivial (if modest) heterogeneity in
drift and volatility across its 7 runs, not a flat/degenerate series.

============================================================
15. QUALITY GATE (independent dimensions - no composite score)
============================================================

Per the brief's explicit instruction, no weighted/composite score was computed anywhere in this
sprint. Each dimension below is an independent, separately-reasoned verdict
(`real_market_quality_summary.csv` is the authoritative machine-readable copy; reasoning restated
here):

| Dimension | Status | Why |
|---|---|---|
| DataCollection | **PASS** | Export lifecycle completed cleanly; BarsReceived arithmetic holds exactly. |
| StructuralValidity | **PASS** | 0 OHLC invariant violations, 0 non-positive price, 0 negative volume across all 1732 bars. |
| DuplicateBehavior | **FAIL** | First-Add()-wins confirmed (not theorized) to retain pre-close single-tick snapshots for a contiguous 633-bar (36.5%) segment. Corroborated independently by session 165335. |
| TimestampIntegrity | **PASS_WITH_CAVEATS** | Structurally excellent (strict monotonicity, 0 dup timestamps, every gap a clean whole multiple); caveat: no repo-native calendar to confirm gap meaning. |
| OhlcvIntegrity | **PASS_WITH_CAVEATS** | Structurally valid throughout; a large subset's *meaning* (closed-bar vs. forming-bar) is compromised - see DuplicateBehavior. |
| VolumeIntegrity | **PASS** | 0 negative, distribution shape (skew, the tail segment's median-1 volume) is itself corroborating evidence, not a defect. |
| Continuity | **PASS_WITH_CAVEATS** | CurrentBar sequence has zero internal index gaps; calendar-time gaps are all session-boundary-shaped. |
| Timezone | **UNRESOLVED** | Honest "Unspecified" self-report; no repo-native conversion exists to check it against. |
| Coverage | **PASS_WITH_CAVEATS** | 1732 bars / 8.4 days is adequate for exploratory work, not yet a production-grade multi-regime sample. |
| Horizon40Adequacy | **PASS_WITH_CAVEATS** | 1452 (or 899 restricted to the clean segment) usable 40-bar windows - numerically ample, but many windows touch the compromised tail. |
| SessionIntegrity | **PASS** | Single SessionId/Symbol/TimeFrame throughout; the second capture never concatenated. |

============================================================
16. SCIENTIFIC ADMISSIBILITY
============================================================

**REAL_DATA_ADMISSIBILITY = BLOCKED.**

Per the brief's own rule: *"Do not declare PASS simply because the collector/export works"* and
*"BLOCKED: at least one unresolved issue could materially invalidate QDE-012 results."* That is
exactly the situation here. The single driving reason:

`DuplicateBehavior = FAIL` is not a caveat - it is a **confirmed, evidenced defect**: for 633 of 1732
bars (36.5%), the exported "OHLCV" is very likely a single-tick snapshot of a bar that had not yet
closed, not that bar's true closed-candle range/volume. This is silent corruption - every affected
bar still passes every structural OHLC check (Section 8), so nothing about the file's shape would warn
a future calibration sprint that ran QDE-012 on it directly. The corruption also lands in the **most
recent, single contiguous slice of the sample** (the entire tail from 2026-07-06T01:35 onward) - the
portion any reasonable train/validation/test split (QDE-012's own existing synthetic-campaign
convention, per `A1_A2_Hybrid_train_validation_test.csv` and siblings) would be most likely to reserve
as held-out data. A future calibration that used this file naively could silently produce a corrupted,
underestimated range/ATR-style statistic for exactly its out-of-sample evaluation slice - a result
that would look clean (no errors, no crashes, plausible-looking numbers) while being wrong.

The **first 1099 bars (idx 0-1098, through 2026-07-06T01:30) show no evidence of this defect** and
independently clear every other dimension in Section 15 (with the standing Timezone/Coverage/Calendar
caveats). That sub-window remains available for **exploratory, clearly-labeled** work; it is not
itself blocked.

============================================================
17. LIMITATIONS
============================================================

- **FORMING_BAR_CAPTURE is confirmed, not fixed.** Per the brief's explicit instruction, this sprint
  reports and quantifies it but does not modify `ScientificDatasetCollector.cs`'s dedup policy.
- **Timezone remains unresolved** - not converted, not guessed, not fixed this sprint.
- **No repo-native trading calendar exists** - gap classification in Section 7/10 rests on a
  generic size heuristic plus (explicitly separated) external domain commentary, not a verified
  exchange schedule.
- **Single capture, single instrument, single timeframe, 8.4 days.** Even the clean 1099-bar segment
  is one short historical window, not a multi-regime, multi-instrument sample.
- **Rejected-duplicate payloads are unrecoverable.** `ScientificDatasetCollector` never retains a
  rejected record's Open/High/Low/Close/Volume, so per-bar duplicate mechanism (A vs. B) for the 1099
  "clean" bars cannot be established with certainty from this evidence alone - only the aggregate
  mechanism (predominantly B) is established, via the two independent lines of evidence in Section 6.
- **Regime coverage (Section 14) is descriptive only** - no formal trending/mean-reverting
  classification was run; doing so would require running the repository's own scientific models,
  which is out of this sprint's scope.

============================================================
18. PRODUCTION PERIMETER
============================================================

**Zero production files touched.** Every file this sprint created or modified lives under
`Tests/Research/StopLossCalibration/RealMarket/` or `Documentation/Scientific/`:

**New this sprint:**
- `Tests/Research/StopLossCalibration/RealMarket/RealMarketBar.cs`
- `Tests/Research/StopLossCalibration/RealMarket/RealMarketQualityAnalyzer.cs`
- `Tests/Research/StopLossCalibration/RealMarket/RealMarketQualityReportWriter.cs`
- `Tests/Research/StopLossCalibration/RealMarket/RealMarketQualityAnalyzerTests.cs`
- `Tests/Research/StopLossCalibration/RealMarket/Sprint1518RealCaptureQualityTests.cs`
- `Tests/Research/StopLossCalibration/RealMarket/RawCapture/Sprint15_18/` (10 raw evidence files +
  `MANIFEST_SHA256.txt`)
- `Tests/Research/StopLossCalibration/RealMarket/real_market_quality_summary.csv`
- `Tests/Research/StopLossCalibration/RealMarket/real_market_timestamp_analysis.csv`
- `Tests/Research/StopLossCalibration/RealMarket/real_market_duplicate_analysis.csv`
- `Tests/Research/StopLossCalibration/RealMarket/real_market_ohlcv_validation.csv`
- `Tests/Research/StopLossCalibration/RealMarket/real_market_gap_analysis.csv`
- `Tests/Research/StopLossCalibration/RealMarket/real_market_horizon_analysis.csv`
- `Tests/Research/StopLossCalibration/RealMarket/real_market_session_analysis.csv`
- `Tests/Research/StopLossCalibration/RealMarket/real_market_admissibility.txt`
- `Documentation/Scientific/QDE-012_Sprint_15.18_ATAS_Real_Data_Quality_Report.md` (this file)

**Not touched by this sprint:** `Core/Calibration/*.cs`, `IQIAIndicator.cs`, `Engine/`, `Decision/`,
`Risk/`, `Signal/`, `Strategy/`, any QDE-012 protocol file, any A1/A2/D1/D2 formula file, any
`Visualization/` dashboard file - confirmed by `git status`/`git diff --stat` showing no change to any
of those paths from this sprint's work (the pre-existing modifications to
`Core/Calibration/*`/`IQIAIndicator.cs`/`Visualization/*` visible in `git status` predate this sprint,
from Sprints 15.13/15.15/15.17.x, and were not touched further here).

No production modification was found necessary at any point in this sprint - the FORMING_BAR_CAPTURE
finding is reported per Section 4's instruction, not silently patched.

============================================================
19. TESTS
============================================================

**63 tests** in the `RealMarket`/Sprint-15.18 area (`dotnet test --filter FullyQualifiedName~RealMarket`),
all passing. Full-repository `dotnet test` (no filter) confirms **154/154 passing, 0 failed, 0
skipped** - zero regressions anywhere else in the suite from this sprint's additions.

| Brief's required coverage | Test(s) |
|---|---|
| 1. OHLC invariant validation | `ValidateOhlc_DetectsAllThreeStructuralViolationKinds`, `ValidateOhlc_FlagsNonPositivePriceAndNegativeVolumeSeparatelyFromStructural` |
| 2. Timestamp ordering | `AnalyzeTimestamps_StrictlyIncreasingSeries_ReportsStrictlyIncreasingTrue`, `AnalyzeTimestamps_OutOfOrderRow_ReportsStrictlyIncreasingFalse` |
| 3. Duplicate classification | `DetectZeroRangeSegments_ContiguousSingleTickBars_FormsOneSegment`, `DetectZeroRangeSegments_NoZeroRangeBars_ReturnsEmpty` |
| 4. 5-minute interval detection | `TryParseExpectedInterval_RecognizedTimeFrames_ReturnsExpectedSpan` (+unrecognized case) |
| 5. Gap detection | `ClassifyGaps_RegularSeries_FindsNoGaps`, `_WeekendSizedGap_`, `_SixtyFiveMinuteSizedGap_`, `_NonMultipleInterval_` |
| 6. Volume validation | `AnalyzeVolume_ComputesMinMaxMedianMean_AndCountsZeroWithoutRejecting` |
| 7. Session consistency | `AnalyzeSessionIntegrity_SingleSessionSymbolTimeFrame_AllTrue`, `_MixedSymbol_DetectsContamination` |
| 8. Symbol/TimeFrame consistency | (same two tests above) |
| 9. 40-bar horizon availability | `AnalyzeHorizonAdequacy_SingleRunLongerThanHorizon_...`, `_GapSplitsRunsBelowHorizon_...` |
| 10. Timezone-unresolved behavior | `ClassifyTimezone_UnspecifiedSelfReport_IsUnresolved_NeverGuessed`, `_ExplicitIanaIdentifier_IsNotUnresolved` |
| 11. Source dataset immutability | `RawCaptureFile_MatchesManifestChecksum_NeverModifiedSinceCapture` (×10, one per raw file, SHA-256) |
| 12. No fabricated-data fallback | `RealMarketOhlcvCsvReader_MissingFile_ThrowsRatherThanFabricatingRows`, `_MalformedRow_ThrowsRatherThanSkippingOrDefaulting` |

Plus the real-capture integration tests (`Sprint1518RealCaptureQualityTests`) that run the full
pipeline against the actual 165421/165335 files and assert the exact facts reported above (BarsWritten
count, 0 structural violations, the 633-bar/idx-1099 segment, gap count/classification, etc.) - not
just "the code runs," but "the code produces the specific numbers this report claims."

Synthetic fixtures in `RealMarketQualityAnalyzerTests.cs` are small, hand-authored, and explicitly
documented in that file's header as test-only - never reported as a real-market campaign.

============================================================
20. FINAL VERDICT
============================================================

The real ATAS capture is **not yet admissible for QDE-012 calibration as supplied**, despite a fully
working collection/export pipeline and a structurally clean file. The blocking issue is a confirmed
capture-methodology defect (FORMING_BAR_CAPTURE), not a data-corruption or pipeline-reliability issue -
it is fixable, and this sprint identifies exactly what needs to change and where.

============================================================
21. RECOMMENDATION FOR SPRINT 15.19
============================================================

The next scientifically justified action is **infrastructure, not calibration**: give
`ScientificDatasetCollector` (or a caller of it) a way to distinguish a bar's forming state from its
closed state, so the exported dataset reflects final closed-bar OHLCV rather than whichever tick
happened to arrive first. Two directions worth evaluating (neither implemented here, per this sprint's
scope):
(a) switch the dedup policy for a given `CurrentBar` from first-`Add()`-wins to
**last-`Add()`-wins-before-`CurrentBar`-advances** (a materially different, non-trivial change to a
class this sprint was told not to modify); or
(b) capture a second, explicit "bar closed" observation only when `bar` advances past the previously
seen value, using the *previous* bar's now-final `IndicatorCandle` state.
Only after that fix is validated against a **new** real ATAS capture (repeating this sprint's full
quality contract on it) should a future sprint revisit A1 calibration on real data - and even then,
QDE-012's own recommendation to gather a longer, multi-regime capture (Section 14/17) still stands.

============================================================
FINAL GATE
============================================================

```
DATA_COLLECTION:
    PASS

DATA_INTEGRITY:
    PASS_WITH_CAVEATS

TIMESTAMP_INTEGRITY:
    PASS_WITH_CAVEATS

OHLCV_INTEGRITY:
    PASS_WITH_CAVEATS

DUPLICATE_HANDLING:
    FAIL

HORIZON_40_ADEQUACY:
    PASS_WITH_CAVEATS

TIMEZONE:
    UNRESOLVED

SCIENTIFIC_ADMISSIBILITY:
    BLOCKED

PRODUCTION_MODIFICATIONS:
    0

CALIBRATION_RUN:
    NO

K_SELECTED:
    NO
```

**SPRINT 15.18 VERDICT:**
The real ATAS capture is structurally clean and its pipeline is proven end-to-end, but 36.5% of its
bars (a confirmed, evidenced, contiguous tail segment) carry forming-bar single-tick snapshots instead
of closed-candle OHLCV, which blocks calibration admissibility until the collector's dedup/close
semantics are fixed.

**NEXT SPRINT:**
Fix `ScientificDatasetCollector`'s bar-closed/dedup semantics (Section 21 above), then re-run this
exact quality contract on a fresh real ATAS capture before any QDE-012 calibration is attempted.
