# QDE-002
# Scientific Audit – Stationarity Dimension

Version : 1.0
Status : Scientific Audit
Module : Fusion Engine
Dimension : Stationarity

Author : BobLabs

---

# 1. Purpose

This document defines the scientific foundations, implementation review,
limitations and recommendations for the Stationarity dimension used inside IQIA.

The objective is not to redesign the statistical models themselves.

The objective is to verify that the current implementation correctly represents
the concept of market stationarity for real-time regime detection.

---

# 2. Scientific Definition

Stationarity is one of the most fundamental concepts in quantitative finance.

A stationary process is a stochastic process whose statistical properties remain
stable over time.

This generally means:

• constant mean

• constant variance

• constant covariance structure

Although financial prices are rarely stationary,
their returns or spread processes often are.

Mean Reversion strategies generally require stationary behaviour.

Trend Following generally requires the opposite.

Therefore Stationarity is one of the primary dimensions of IQIA.

---

# 3. Scientific Objective inside IQIA

Within IQIA,
Stationarity does NOT attempt to answer:

"Is this mathematical time series perfectly stationary?"

Instead it answers:

"How compatible is the current market behaviour with a stationary regime?"

This distinction is extremely important.

IQIA estimates compatibility.

It does not attempt to prove stationarity.

---

# 4. Current Scientific Models

Current implementation uses:

ADF

KPSS

These two tests are intentionally complementary.

---

ADF

Null hypothesis:

Series contains a unit root.

Meaning:

Non-stationary.

Low p-value:

Evidence FOR stationarity.

---

KPSS

Null hypothesis:

Series IS stationary.

High p-value:

Evidence FOR stationarity.

Low p-value:

Evidence AGAINST stationarity.

---

The combination of these tests is scientifically robust because
their null hypotheses are opposite.

One model rejects stationarity.

The other rejects non-stationarity.

Using both greatly reduces false positives.

---

# 5. Current Fusion Philosophy

Current Fusion Rule combines:

ADF

KPSS

↓

Scientific Score

↓

Quality Score

↓

Final Score

This architecture is scientifically sound.

The implementation correctly separates:

Scientific evidence

Statistical confidence

This is considered one of the strongest components of IQIA.

---

# 6. Replay Behaviour

Replay observations indicate that:

Stationarity reacts quickly to market transitions.

This behaviour is expected.

However,
when recalculated every tick,
the displayed value may fluctuate rapidly.

Example:

0.78

↓

0.73

↓

0.81

↓

0.76

↓

0.79

These fluctuations do NOT necessarily indicate regime changes.

They mostly represent statistical noise.

---

# 7. Scientific Interpretation

Important distinction.

The raw statistical models are expected to fluctuate.

The market regime is NOT expected to fluctuate at the same frequency.

Therefore:

Raw Stationarity

≠

Displayed Stationarity

IQIA correctly introduced the Fusion State Manager
to solve this temporal inconsistency.

---

# 8. Strengths

Current implementation provides:

✔ Complementary statistical tests

✔ Independent scientific score

✔ Independent quality score

✔ Robust interpretation

✔ Excellent compatibility with Mean Reversion detection

✔ Excellent compatibility with Decision Engine

No major scientific flaw has been identified.

---

# 9. Weaknesses

No conceptual weakness has been identified.

Remaining limitations are mainly operational.

Examples:

Very short windows

Extremely volatile news events

Insufficient sample size

Temporary statistical oscillations

These limitations are expected for all stationarity tests.

---

# 10. Relationship with other dimensions

Stationarity should never be interpreted alone.

High Stationarity

+

High Mean Reversion

↓

Strong Mean Reversion regime.

High Stationarity

+

High Persistence

↓

Possible conflict requiring arbitration.

Low Stationarity

+

High Persistence

↓

Trending market.

Low Stationarity

+

Low Persistence

↓

Possible Structural Transition.

Therefore Stationarity is a foundational dimension,
not a complete market classification.

---

# 11. Scientific Validation

Current implementation is considered scientifically valid.

No redesign is recommended.

Only temporal stabilization through Fusion State Manager is required.

The underlying statistical models should remain unchanged.

---

# 12. Recommendations

Keep:

ADF

KPSS

Scientific Score

Quality Score

Current Fusion Rule.

Future improvements may include:

Adaptive window size

Confidence weighting

Dynamic calibration

but no redesign is currently justified.

---

# 13. Final Verdict

Scientific Robustness

★★★★★

Implementation Quality

★★★★★

Real-Time Suitability

★★★★☆

Need for Redesign

☆☆☆☆☆

Overall Assessment

9.8 / 10

The Stationarity dimension is considered scientifically mature.

No structural redesign is recommended.

Future work should focus on calibration rather than redesign.