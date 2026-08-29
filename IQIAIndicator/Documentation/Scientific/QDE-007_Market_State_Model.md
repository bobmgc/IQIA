# QDE-007
# Scientific Specification – Market State Model

Version : 1.0

Status : Core Scientific Specification

Module : Decision Engine

Author : BobLabs

Classification : Core Theory

Dependencies

QDE-001
QDE-002
QDE-003
QDE-004
QDE-005
QDE-006

---

# 1. Purpose

This document defines the scientific model used by IQIA
to transform independent behavioural dimensions
into an interpretable Market State.

The objective is not to predict price.

The objective is to identify
the behavioural regime
currently governing the market.

---

# 2. Fundamental Principle

A Market State is NOT measured directly.

It is inferred.

The Decision Engine observes
multiple behavioural dimensions
and estimates which behavioural hypothesis
best explains the market.

Therefore

Market State

is

an interpretation

not

a measurement.

---

# 3. Behavioural Dimensions

IQIA observes five independent dimensions.

Stationarity

Persistence

Mean Reversion

Structural Stability

Random Walk

Each dimension represents
one scientific property only.

No dimension represents
a Market State.

---

# 4. Behavioural Space

Every market can be represented
as a point inside a five-dimensional space.

Example

Stationarity

0.82

Persistence

0.18

Mean Reversion

0.77

Structural Stability

0.81

Random Walk

0.12

This vector is the market signature.

The Decision Engine classifies
this signature.

---

# 5. Scientific Philosophy

IQIA does NOT classify candles.

IQIA does NOT classify trends.

IQIA classifies

market behaviour.

The behavioural signature
remains relatively stable
while individual candles fluctuate.

---

# 6. Behavioural Hypotheses

Current hypotheses are

Stable Range

Trending

Mean Reverting

Structural Break

Random Walk

These are hypotheses,
not truths.

Each hypothesis receives
its own compatibility score.

---

# 7. Market State Selection

The Market State is selected
through scientific arbitration.

All hypotheses are evaluated.

Each receives

Scientific Score

Quality Score

Final Score

The winner becomes
the current Market State.

The remaining hypotheses
are preserved.

Nothing is discarded.

---

# 8. Ambiguity

Markets are rarely binary.

Example

Trending

0.81

Mean Reverting

0.79

Both hypotheses remain plausible.

IQIA therefore computes

Ambiguity Score.

High ambiguity

↓

Low confidence.

Low ambiguity

↓

High confidence.

This mechanism prevents
false certainty.

---

# 9. Temporal Behaviour

A Market State
should not change
every tick.

Behaviour changes
more slowly
than prices.

Therefore

Raw Decision

↓

Temporal Stabilization

↓

Displayed Market State

This principle is fundamental.

---

# 10. Market Events

Market States

describe

long-lasting behaviour.

Market Events

describe

temporary phenomena.

Examples

Structural Break

Liquidity Sweep

Volatility Expansion

Volatility Compression

Gap

News Shock

Events modify states.

Events are NOT states.

---

# 11. Structural Break

Structural Break
is redefined.

Old philosophy

Structural Break

=

Market State

New philosophy

Structural Break

=

Market Event

The market enters
a transition.

The transition ends.

A new Market State emerges.

---

# 12. Expected Lifetime

Approximate duration

Market State

Several minutes

Several hours

Entire session

Market Event

One bar

Few bars

Transition only

This temporal distinction
is essential.

---

# 13. Scientific Confidence

Confidence does NOT measure

probability of profit.

Confidence measures

coherence
between behavioural dimensions.

Example

Trending

0.82

Mean Reverting

0.81

Confidence

Low

because

both hypotheses
are almost equivalent.

---

# 14. Decision Philosophy

The Decision Engine never predicts.

It estimates

the most coherent explanation
of observed behaviour.

This distinction protects IQIA
against overfitting.

---

# 15. Future Evolution

Future Market States
may include

Volatility Regime

Auction Market

Distribution

Accumulation

Only if they represent
independent behavioural regimes.

No Market State
should duplicate
an existing dimension.

---

# 16. Architectural Position

Evidence Models

↓

Behavioural Dimensions

↓

Fusion

↓

Market State Inference

↓

Strategy Selection

↓

Signal Generation

↓

Risk Management

This hierarchy must never be violated.

---

# 17. Scientific Principles

IQIA follows
seven principles.

Behaviour before prediction.

Interpretation before decision.

Decision before strategy.

Strategy before signal.

Signal before risk.

Risk before execution.

Execution remains external
to IQIA.

---

# 18. Validation Criteria

A Market State model
is considered scientifically valid
if

it remains stable
during coherent market behaviour,

changes only
when behavioural evidence changes,

explains transitions,

preserves ambiguity,

remains interpretable.

---

# 19. Scientific Verdict

Market States
are not indicators.

They are behavioural hypotheses.

Behavioural dimensions
are not strategies.

They are scientific observations.

IQIA therefore separates

Observation

↓

Interpretation

↓

Decision

↓

Strategy

↓

Signal

↓

Risk

This separation constitutes
the core scientific philosophy
of IQIA.

---

# 20. Final Conclusion

The Market State Model transforms
independent scientific observations
into an interpretable behavioural hypothesis.

It never predicts price.

It never predicts direction.

It estimates

which behavioural regime
currently best explains
the market.

This interpretation
becomes the foundation
of every downstream component
inside IQIA.