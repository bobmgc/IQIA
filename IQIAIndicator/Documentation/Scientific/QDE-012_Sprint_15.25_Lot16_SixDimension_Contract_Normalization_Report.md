# QDE-012 — Sprint 15.25 — Lot 16 — Fusion Dimension Contract Repair & Scientific Normalization

**Date** : 2026-08-27
**Branche** : `feature/structural-stability-v2`
**Lots précédents pertinents** : 14.17, 15.0, 15.7, 15.8, 15.9, Audit indépendant des 6 dimensions
**Type** : Réparation scientifique du contrat numérique — **PAS de calibration, PAS de reweighting, PAS de changement de règle de décision**
**Statut** : **RESULT A — STRENGTH CONTRACT FIXED** (production modifiée, minimalement, à `StructuralBreakEvidenceRule.cs` uniquement)

---

## 1. Objective

Garantir que les valeurs continues envoyées à la Fusion ont un **contrat numérique scientifiquement
exploitable** *avant* toute calibration future. Priorité scientifique du lot : la dimension
`StructuralBreak`, dont le seul champ continu (`Strength`) sature à `1.0` dès qu'une détection CUSUM est
active (Lot 15.8/15.9), et est donc de fait binaire — pondérer dessus reviendrait à pondérer sur le
booléen `Detected` en prétendant utiliser un score continu.

Le but **n'est pas** de calibrer des poids, d'optimiser des performances, de choisir des paramètres de
trading, de modifier les règles de décision, le Risk Engine, d'activer des ordres, ni de faire du
reweighting.

---

## 2. Scope

**Modifié (strict minimum) :**

- `Engine/Fusion/Rules/StructuralBreakEvidenceRule.cs` — une méthode privée ajoutée
  (`NormalizeStrength`), une ligne changée dans `BuildContract` (`double strength = …`), le libellé
  `Explanation` et trois commentaires de doc. **Aucune signature publique modifiée.**

**Ajouté :**

- `Tests/Fusion/Lot16StructuralBreakStrengthContractTests.cs` (propriétés pures, sans réseau)
- `Tests/Research/Lot16ContractNormalization/Lot16StrengthTransformComparisonTests.cs` (Phase 3/4 — comparaison des candidats, observationnel)
- `Tests/Research/Lot16ContractNormalization/Lot16ContractRegressionTests.cs` (intégration Yahoo — bornes, monotonie, non-saturation, déterminisme, run isolation, régression des 5 autres dimensions, identité dataset)
- ce rapport

**Mis à jour (assertions rendues obsolètes par la réparation, StructuralBreak uniquement) :**

- `Tests/Fusion/StructuralBreakEvidenceRuleTests.cs` — les assertions `Strength == cusum.Confidence` deviennent `Strength == r/(1+r)`.
- `Tests/Research/StructuralBreakInformationAudit/StructuralBreakStrengthDistributionAuditTests.cs` §B.2 — l'assertion `Assert.Equal(0, strengthNotEqualConfidenceCount)` (qui épinglait le pass-through saturant) est **inversée** : Strength doit désormais être découplé du clamp, à variance non nulle, non saturé.

**NON touché** (fichiers protégés, aucune nécessité scientifiquement démontrée) :
`CusumStatistics.Compute` (le `Math.Clamp` fautif y reste — code partagé, autres consommateurs),
`CusumResult`, `RegimeEngine`, `FusionStateManager`, `FusionProfileAnalyzer`, `DecisionEngine`,
`DecisionArbitrator`, toutes les `IDecisionRule`, `SignalEngine`, `EntryTriggerBuilder`,
`EntryTriggerEngine`, `TradePlanBuilder`, `RiskEngine`, `RiskPolicy`, `ExecutionSimulator`, le modèle de
coût backtest, le position sizing.

**Interdictions respectées :** NO CALIBRATION, NO REWEIGHTING, NO OPTIMIZATION, NO PARAMETER SEARCH, NO
GRID SEARCH, NO PRODUCTION PARAMETER CHANGE, NO RISK ENGINE CHANGE, NO STOP LOSS CHANGE, NO DECISION RULE
CHANGE, NO ENTRY CHANGE, NO ATAS, NO LIVE TRADING, NO ORDER, NO DLL DEPLOYMENT, NO COMMIT.

---

## 3. Current Six-Dimension Architecture

`Engine/Fusion/Core/FusionDimension.cs` déclare, dans cet ordre : `Stationarity`, `Persistence`,
`MeanReversion`, `StructuralStability`, `RandomWalk`, `StructuralBreak`.

Cartographie vérifiée directement dans le code (aucune hypothèse) :

```
RAW SCIENTIFIC VALUE  (Engine/Regime/Evidence/**  →  EvidenceSet)
        ↓
NORMALIZATION         (à l'intérieur de chaque IFusionRule.Evaluate  →  scientificScore)
        ↓
VALUE                 (FusionConfidence.Value  =  scientificScore, PAS le Blend qui est calculé puis jeté)
        ↓
CONFIDENCE            (FusionConfidence.Confidence  =  qualityScore ; pour StructuralBreak : = Value)
        ↓
FusionStateManager    (EMA alpha=0.20, PUIS hystérésis seuil=0.03, appliqué à 5 des 6 dims ;
                       StructuralStability est injectée séparément via StructuralStabilityRule)
        ↓
STABILIZED DIMENSION  (FusionSnapshot.StableResult.Dimensions[dim])
        ↓
DECISION RULES        (5 IDecisionRule ; consomment 5 des 6 dims — StructuralBreak n'a AUCUN consommateur)
```

- **Producteur de production** : `EvidenceFusionEngine` construit dans `IQIAIndicator.cs` avec 5
  `IFusionRule` : `StationarityRule`, `PersistenceRule`, `MeanReversionRule`, `RandomWalkRule`,
  `StructuralBreakEvidenceRule`.
- `StructuralStabilityRule` **n'est pas** une `IFusionRule` : `FusionStateManager.Update` l'invoque via
  `EvaluateAnalysis(analysis)` puis remplace la dimension `StructuralStability` du résultat stabilisé.
- `FusionProfileAnalyzer` exclut délibérément `StructuralBreak` de sa fenêtre de profil.
- `FusionDimension.StructuralBreak` n'est lu par **aucune** `IDecisionRule` (grep vérifié hors tests :
  seuls `FusionStateManager` (liste) et `StructuralBreakEvidenceRule` (production) le référencent) et
  n'est pas affiché par `DecisionDashboard`.

---

## 4. Complete Numeric Contract Inventory (Phase 1)

| Dimension | Raw Signal | Value Range (mesuré) | Confidence | Normalization | Saturation (mesurée) | Temporal Behaviour (mesuré) |
|---|---|---|---|---|---|---|
| **Stationarity** | ADF p-value + KPSS p-value (+ IsStationary, tailles d'échantillon ; conf. par test = `n/Window`) | RAW [0.119, 0.913], mean 0.389, StdDev **0.203** | `0.70·conf(ADF,KPSS) + 0.30·conf(taille)` ; conf. ADF/KPSS = `n/Window` ⇒ ≈ constante après warm-up | logistique autour de p=0.05 (pente 50), puis `clamp(0.5 + 0.25·(adfDir+kpssDir))` | 0 % à 0, 0 % à 1 ; 29.9 % dans [0.45,0.55] | `PARTIALLY_DAMPED` (80.4 % des changements bruts figés) ; **Confidence STABLE = une seule valeur (0.7577)** |
| **Persistence** | DFA Hurst + Variance Ratio | RAW [0, 0.901], mean **0.078**, médiane **0.015**, StdDev 0.140 | `0.5·(dfaQuality + vrQuality)` ; partage `pValue` VR avec Value | `PersistenceSupport(x)=x²·σ(4x)` (quadratique), `clamp(0.5·(dfa+vr))` | comprimée vers 0 | `HEAVILY_DAMPED` — **94.9 %** brut-change/stable-figé, plateaux figés jusqu'à **924 barres** |
| **MeanReversion** | HalfLife (+ R², taille) | RAW [0, 0.938], mean 0.581, StdDev **0.250** ; **4.37 % à exactement 0** = sentinelle Missing Evidence | `(clamp(conf=R²) + clamp(R²) + (1-e^(-n/50)))/3` | `exp(-HalfLife/10)` | vers 1 (HL→0) et vers 0 (HL extrêmes, outliers > 12000) | `PARTIALLY_DAMPED` (68.2 %) ; corr(Value,Confidence) = **+0.82** |
| **StructuralStability** | `FusionProfileAnalysis` (profil temporel des autres dims stabilisées + soi-même ; fenêtre 6) — **aucune évidence statistique** | STABLE [0.527, 0.810], mean 0.645, StdDev **0.043** (pas de contrepartie brute) | `clamp(0.5·ProfileStability + 0.5·(1-ProfileVelocity))` | `clamp` sur `BehaviourConsistency = 0.65·PS + 0.35·(1-PV)` ; `PS = 1 - MAD/0.5` (**diviseur 0.5 non documenté**) | vers 1 (défaut warm-up + profil lent) | `FROZEN` **by design** ; **`CONFIDENCE_DUPLICATES_VALUE`** (corr 0.999) ; `DERIVED_FROM_OTHER_DIMENSION` |
| **RandomWalk** | Variance Ratio seul | RAW [0, 1.0], mean 0.190, médiane **0.064**, StdDev 0.255 | `0.5·(clamp(conf VR) + (1-e^(-n/50)))` | produit `exp(-|ln VR|/0.25)·exp(-|Z|/2)·clamp(pValue)` — 3 facteurs < 1 | `SATURATED_LOW` (masse près de 0) | `PARTIALLY_DAMPED` (66.9 %) ; corr(Value,Confidence) = **−0.90** (couplage inverse via `VR.PValue`) |
| **StructuralBreak — AVANT Lot 16** | CUSUM `Confidence = Clamp(peakMagnitude/threshold, 0, 1)` (+ `ChangeDetected`, `BreakLocation` ; Bai-Perron `BreakCount` diagnostique) | **[1, 1] quand Detected** (constante 1.0) ; DistinctValues = **1** parmi 10 666 barres détectées | **= Value** (duplication exacte de `contract.Strength` sur `.Value` et `.Confidence`) | **`Clamp(r, 0, 1)`**, `r = peak/threshold` | **`SATURATED` à 1.0** — 93.3 % de toutes les barres | `SATURATED` (l'EMA n'a rien à lisser) ; Value == Confidence exactement |
| **StructuralBreak — APRÈS Lot 16** | mêmes champs CUSUM ; `r` reconstruit dans `StructuralBreakEvidenceRule` depuis `PositiveCusum`/`NegativeCusum`/`Threshold` | **[0, 1)** — `r/(1+r)` ; parmi détectées : min 0.500, médiane **0.937**, max 0.9998, **10 636 valeurs distinctes** | = Value (duplication **inchangée** — hors périmètre Lot 16) | **`r/(1+r)`** (bornée, monotone, sans paramètre ; `s = 0.5` à `r = 1` = frontière de détection Page-CUSUM) | **0 % de saturation parmi les détectées** ; variance **0.0149** (était 0) | RAW varie à chaque barre (comme le ratio) ; stabilisation = même EMA+hystérésis `FusionStateManager` que toute autre dim (inchangé) |

### Réponses aux sous-questions Phase 1

- **A — Saturation.** `StructuralBreak.Strength` (avant) : atteint `1` sur 93.3 % des barres,
  DistinctValues = 1, variance nulle parmi les détectées → **saturation totale**. `RandomWalk` :
  concentre sa masse près de `0` (médiane 0.064) → saturation basse. `Persistence` : médiane 0.015,
  bande basse étroite. `Stationarity` : pas de pile-up aux bornes (0 % à 0 et à 1).
- **B — Compression.** `Persistence` : le terme quadratique `x²·σ(4x)` écrase les faibles directions
  (raw variance 0.140 → stable variance 0.0085). `StructuralBreak` (avant) : compression **totale**
  (raw ratio variance 20 357 → `Strength` variance 0). `RandomWalk` : le produit de 3 facteurs < 1
  compresse fortement vers 0.
- **C — Discontinuité.** Seuil dur avéré : `Math.Clamp(ratio, 0, 1)` dans `CusumStatistics.Compute` —
  produit un plateau artificiel à `1.0` pour tout `r ≥ 1`. Aucun autre `if x > threshold → 1` trouvé
  dans les 6 normalisations (les autres utilisent des transformations continues bornées).
- **D — Cohérence Value/Confidence.** Duplication **exacte** : `StructuralBreak` (`Value` et
  `Confidence` = même `contract.Strength`). Quasi-colinéaire : `StructuralStability`
  (corr 0.999 — mêmes `ProfileStability`/`ProfileVelocity`). Redondante positivement :
  `MeanReversion` (corr +0.82). Contradictoire : `RandomWalk` (corr −0.90). Constante : `Stationarity`
  (`Confidence` STABLE = une seule valeur). **Lot 16 ne corrige pas la duplication Value/Confidence de
  `StructuralBreak`** — hors périmètre (Phase 2 porte sur le contrat `Strength` lui-même). Elle reste
  documentée pour un futur lot.

---

## 5. StructuralBreak Strength Root Cause

Confirmé par le Lot 15.9 §6/§8 et re-mesuré ici (Phase 3, tirage frais) :

```
1. PeakMagnitude = Max(PositiveCusum, |NegativeCusum|)         (variance ~ 7.9e7)
2. Threshold = σ·√(2n·ln n)                                    (variance ~ 1.5e5)
3. RawRatio  r = PeakMagnitude / Threshold  (AVANT clamp)      N=10666  [1.0001 .. 5982.76]  Var=20357  Mean=47.0  Median=14.79
        │
        ▼  Math.Clamp(r, 0.0, 1.0)   ← CusumStatistics.Compute, ligne unique, code protégé
4. Cusum.Confidence                                            N=10666  [1.0 .. 1.0]  Var=0  Distinct=1
5. Contract.Strength (= Clamp(cusum.Confidence,0,1), verbatim) N=10666  [1.0 .. 1.0]  Var=0  Distinct=1
```

**L'effondrement de variance a lieu strictement entre l'étape 3 et l'étape 4**, dans le
`Math.Clamp` de `CusumStatistics.Compute`. `StructuralBreakEvidenceRule` était un passe-plat fidèle
(`Strength = Math.Clamp(cusum.Confidence, 0, 1)`), sans responsabilité dans la perte.

`CusumStatistics.Compute` est du **code protégé partagé** (il sert aussi `ScientificDashboard` et
`BacktestFingerprint`, qui hachent `cusum.Confidence` directement). Le Lot 16 **ne le touche pas**. La
réparation se fait **entièrement en aval**, dans `StructuralBreakEvidenceRule`, en **reconstruisant** le
ratio brut `r` à partir des champs déjà exposés par `CusumResult` (`PositiveCusum`, `NegativeCusum`,
`Threshold`) — ces champs sont déjà garantis causaux par `RegimeEngine.Collect` (fenêtres glissantes
strictement passées, Lot 15.8 §look-ahead).

Pourquoi `peak > threshold` dès `Detected` : le test CUSUM de Page déclare `ChangeDetected` exactement
quand `positiveCusum > threshold` (ou `|negativeCusum| > threshold`). Une fois le seuil franchi, la
somme cumulée continue de croître sans borne tant que le régime décalé persiste dans la fenêtre → `r`
peut atteindre ~6000× le seuil. Le `Clamp` efface toute cette information.

---

## 6. Raw CUSUM Distribution (Phase 3, tirage frais)

Dataset : voir §21. `r = peakMagnitude / threshold`, reconstruit dans le test **sans** modification de
production.

| Sous-ensemble | N | Min | Max | Mean | Median | Variance | P10 | P50 | P90 | P99 |
|---|---|---|---|---|---|---|---|---|---|---|
| Toutes barres valides | 11 428 | 0.218 | 5982.76 | 43.91 | 12.89 | 19 133 | 1.346 | 12.89 | 92.61 | 540.49 |
| `Detected == true` | 10 666 | 1.0001 | 5982.76 | 47.00 | **14.79** | 20 357 | 2.162 | 14.79 | 97.66 | 556.95 |

Distribution **très asymétrique à droite** : médiane 14.79, moyenne 47.0, P99 557, max 5983. La moitié
des barres détectées ont `r ≥ 14.79` (≈ 15× le seuil de détection). C'est la variance native que le
`Clamp` détruisait intégralement.

---

## 7. Current Normalization Analysis

`Strength_before = Math.Clamp(r, 0, 1)` :

- **Monotone** : oui (clamp est monotone non décroissant).
- **Bornée** : oui, [0,1].
- **Non saturante** : **NON** — plateau dur à 1.0 pour tout `r ≥ 1`. Sur données réelles : 100 % des
  barres détectées à `Strength = 1.0`, 1 valeur distincte, variance 0.
- **Sans paramètre** : oui (le clamp n'introduit pas de constante d'échelle, mais il détruit toute
  information au-delà de `r = 1`).
- **Déterministe** : oui.
- **Verdict** : le contrat est *formellement* correct mais *scientifiquement inutile* dès que la
  détection est active — c'est-à-dire 93.3 % du temps.

---

## 8. Candidate Transformations (Phase 3)

Toutes les candidates opèrent sur `r ≥ 0`, sont **paramétriques-libres** (aucune constante d'échelle
choisie pour cette dimension), **`f(0) = 0`**, **monotones croissantes**, **bornées `[0, 1)`**,
**déterministes**, **pures par barre** (aucun look-ahead).

| Code | Transformation | Justification scientifique |
|---|---|---|
| **C0** | `Clamp(r, 0, 1)` | contrat actuel (baseline) |
| **C1** | `r / (1 + r)` | application rationnelle canonique de `[0,∞)→[0,1)` (Michaelis–Menten / logistique de `ln r`). Seule constante : le `1` du dénominateur, qui place `s = 0.5` à `r = 1` — **exactement la frontière de décision Page-CUSUM** (`peak == threshold`). L'ancre est le seuil de détection préexistant, pas une valeur réglée. |
| **C2** | `tanh(r)` | fonction de saturation douce standard |
| **C3** | `1 − e^(−r)` | forme CDF exponentielle |
| **C4** | `(2/π)·arctan(r)` | application arctangente normalisée (2/π borne la limite à 1, ce n'est pas un réglage) |
| **C5** | `r / √(1 + r²)` | forme algébrique bornée standard |
| **C6** | `L / (1 + L)`, `L = ln(1 + r)` | saturation douce **sur échelle logarithmique** — justifiée par l'étendue multiplicative de `r` (~1 à ~6000, 3.5 ordres de grandeur) ; `ln(1+r)` est le log standard préservant zéro, puis `L/(1+L)` le borne. Sans paramètre. |

---

## 9. Mathematical Properties

Vérifiées analytiquement pour C1–C6 (et confirmées numériquement, §11) :

| Propriété | C1 | C2 | C3 | C4 | C5 | C6 |
|---|---|---|---|---|---|---|
| `f(0) = 0` | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| Monotone croissante sur `[0,∞)` | ✓ (`f' = 1/(1+r)² > 0`) | ✓ | ✓ | ✓ | ✓ | ✓ |
| Bornée, `f(r) < 1` ∀ `r` fini | ✓ | ✓ (r≥0) | ✓ | ✓ | ✓ | ✓ |
| `lim_{r→∞} f = 1` | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| Sans paramètre d'échelle réglé | ✓ (ancre = seuil r=1) | ✓ | ✓ | ✓ | ✓ | ✓ |
| Pure par barre (pas de look-ahead) | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| Déterministe | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |

---

## 10. Saturation Analysis (Phase 3, données réelles, parmi les 10 666 barres détectées)

| Candidat | % `Strength ≥ 0.9999` (parmi détectées) | Valeurs distinctes | P10 | P50 | P90 |
|---|---|---|---|---|---|
| C0 (old) | **100.0 %** | **1** | 1.000 | 1.000 | 1.000 |
| **C1 `r/(1+r)`** | **0.0 %** | **10 636** | 0.684 | 0.937 | 0.990 |
| C2 `tanh` | 76.4 % | 3 785 | 0.974 | 1.000 | 1.000 |
| C3 `1−e^−r` | 62.2 % | 5 525 | 0.885 | 1.000 | 1.000 |
| C4 `(2/π)arctan` | **0.0 %** | 10 635 | 0.724 | 0.957 | 0.993 |
| C5 `r/√(1+r²)` | 14.0 % | 10 614 | 0.908 | 0.998 | 0.99995 |
| C6 `log-soft` | **0.0 %** | 10 635 | 0.535 | 0.734 | 0.821 |

**C2 et C3 saturent encore massivement** (62–76 %) → éliminés. **C1, C4, C6** : saturation nulle.
C5 : marginal (14 %).

---

## 11. Variance Analysis (Phase 3, parmi les 10 666 barres détectées)

| Candidat | Variance(Strength \| Detected) | StdDev | corr Pearson avec `r` brut |
|---|---|---|---|
| C0 (old) | **0** | 0 | 0.071 (dénégérée — série constante) |
| **C1 `r/(1+r)`** | **0.01488** | 0.122 | 0.237 |
| C2 `tanh` | 0.00140 | 0.037 | 0.108 |
| C3 `1−e^−r` | 0.00532 | 0.073 | 0.134 |
| C4 `(2/π)arctan` | 0.01315 | 0.115 | 0.209 |
| C5 `r/√(1+r²)` | 0.00310 | 0.056 | 0.138 |
| C6 `log-soft` | 0.01153 | 0.107 | 0.322 |

**C1 a la variance la plus élevée** parmi toutes les candidates non saturantes (0.01488), et le nombre
de valeurs distinctes le plus élevé (10 636 / 10 666). La corrélation de Pearson avec `r` brut est plus
faible pour C1 (0.237) que pour C6 (0.322) uniquement parce que `r` est à queue lourde et que Pearson
est dominé par la queue extrême ; toutes les candidates monotones ont une **corrélation de rang
(Spearman) de 1.0 exactement** avec `r` — l'ordre est parfaitement préservé (voir §12).

---

## 12. Monotonicity Analysis

Vérifiée sur les 11 428 barres réelles, pour chaque candidat, en ordonnant par `r` croissant et en
comptant les diminutions de `Strength` :

| Candidat | Violations de monotonie | Chute max |
|---|---|---|
| C0..C6 | **0** | 0 |

Toutes les candidates préservent exactement le classement du signal brut (monotonie stricte pour C1,
C4, C5, C6 ; C0/C2/C3 non strictes dans leur zone de saturation mais jamais décroissantes). **Le
classement relatif du signal brut est conservé** pour C1 — critère Phase 4 rempli.

---

## 13. Temporal Behaviour (Phase 3/4)

Comportement du **candidat brut** (pré-`FusionStateManager`), sur la séquence chronologique complète :

| Candidat | % transitions barre-à-barre | Plus longue série gelée |
|---|---|---|
| C0 (old) | 9.54 % | 235 |
| C1 `r/(1+r)` | 99.67 % | 2 |
| C4 `(2/π)arctan` | 99.67 % | 2 |
| C5 `r/√(1+r²)` | 99.67 % | 2 |
| C6 `log-soft` | 99.67 % | 2 |

C0 était figé 90 % du temps *parce que sa valeur était la constante 1.0*. Les candidats non saturants
transitionnent quasiment à chaque barre — parce que `r` lui-même bouge à chaque barre (mesuré au
Lot 14.17 pour toutes les dimensions brutes). C'est le comportement **attendu** d'un signal brut
informatif.

Le lissage temporel en aval reste **exactement le même** : `FusionStateManager` (EMA alpha=0.20 +
hystérésis 0.03) est appliqué à `StructuralBreak` comme à toute autre dimension, **inchangé**. Le
Lot 15.9 §17 a mesuré que ce mécanisme réduit la variance de `StructuralBreak` de ~78 % mais seulement
~28 % des transitions — beaucoup moins agressif que le gel de 95 %+ subi par `Persistence`. Avec un
`Strength` brut désormais réellement variable, la valeur stabilisée pourra elle aussi porter de
l'information continue (au lieu de converger asymptotiquement vers un plafond 1.0).

---

## 14. Redundancy Analysis (Phase 4)

Corrélation de Pearson entre chaque candidat `Strength` et la `Value` brute des 4 autres dimensions
reconstructibles (Stationarity, Persistence, MeanReversion, RandomWalk) :

| Candidat | Stationarity | Persistence | **MeanReversion** | RandomWalk |
|---|---|---|---|---|
| C0 (old) | −0.031 | 0.027 | −0.209 | 0.017 |
| **C1 `r/(1+r)`** | −0.115 | 0.055 | **−0.492** | 0.027 |
| C4 `(2/π)arctan` | −0.107 | 0.050 | −0.460 | 0.028 |
| C6 `log-soft` | −0.124 | 0.066 | **−0.539** | 0.026 |

**Observation honnête** : de-saturer `Strength` fait **apparaître** une corrélation modérée-négative
avec `MeanReversion.Value` (r ≈ −0.49 pour C1, −0.54 pour C6), là où C0 ne montrait que −0.21. Ce
n'est **pas** une nouvelle redondance introduite par la transformation : c'est la relation réelle
entre la sévérité CUSUM et la force de retour à la moyenne, **jusqu'ici masquée par la saturation**
(une constante ne peut pas corréler). Le Lot 15.9 §13 avait déjà mesuré `StructuralBreak` (stabilisé)
vs `MeanReversion` en Spearman ≈ −0.26/−0.28 et l'avait interprété comme cohérent (« un marché en
rupture structurelle a moins de comportement de retour à la moyenne »).

- Les deux dimensions proviennent de **sources d'évidence totalement disjointes** (CUSUM vs HalfLife).
- `|r| ≈ 0.49` est **modéré**, pas `HIGH_REDUNDANCY` (seuil usuel |r| > 0.7–0.8).
- C1 introduit **moins** de corrélation incidente que C6 (−0.492 vs −0.539).

Verdict : `PARTIAL_CORRELATION` / `INDEPENDENT_SOURCE`, **pas** `REDUNDANT`. Signalé pour information ;
n'invalide pas la réparation.

---

## 15. Ablation (Phase 4) — OLD vs NEW

| Critère | OLD `Clamp(cusum.Confidence,0,1)` | NEW `r/(1+r)` |
|---|---|---|
| Information : Variance(Strength \| Detected) > 0 | **NON** (variance = 0, 1 valeur distincte) | **OUI** (variance 0.01488, 10 636 distinctes) |
| Saturation parmi détectées | 100 % | **0 %** |
| Monotonie (classement du signal brut conservé) | oui (mais dégénéré) | **oui, strict** (0 violation sur 11 428) |
| Bornée | [0,1] | **[0,1)** — jamais exactement 1 pour `r` fini |
| Non-redondance vs 5 autres dims | |r| ≤ 0.21 (mais Strength constant ⇒ non informatif) | |r| ≤ 0.49 (relation réelle démasquée, source disjointe) |
| Comportement temporel (brut) | 9.5 % transitions, séries gelées à 235 (car constant) | 99.67 % transitions (signal brut réellement variable) |
| Comportement temporel (stabilisé) | inchangé (même FSM) | inchangé (même FSM) |

---

## 16. Production Decision (Phase 5)

Le Lot 16 ne modifie la production que si une transformation satisfait **simultanément** :
`SCIENTIFICALLY JUSTIFIED`, `MONOTONIC`, `BOUNDED`, `NON-SATURATING`, `DETERMINISTIC`, `NO LOOK-AHEAD`,
`NO NEW ARBITRARY CALIBRATION PARAMETER`.

**C1, C4 et C6 satisfont les 7 critères.** Choix final basé sur les résultats mesurés :

| Critère de départage | Gagnant |
|---|---|
| Variance(Strength \| Detected) — préservation d'information (critère primaire non-saturation) | **C1** (0.01488, la plus élevée) |
| Valeurs distinctes parmi détectées | **C1** (10 636) |
| Justification sans paramètre la plus forte | **C1** — `s = 0.5` à `r = 1` = frontière de détection Page-CUSUM *préexistante* ; C6 place `s = 0.5` à `r = e−1 ≈ 1.72` (proche mais pas exactement la frontière) |
| Corrélation incidente minimale avec `MeanReversion` | **C1** (−0.492 vs C6 −0.539) |
| Forme minimale, la plus auditable | **C1** (rationnelle à un terme) |

C6 offre une meilleure répartition sur `[0,1]` (P50 ≈ 0.73 vs 0.94), mais c'est un argument
d'ergonomie aval — hors périmètre d'un lot qui interdit explicitement d'optimiser pour l'usage
descendant.

**Décision : `Strength = r / (1 + r)`** appliqué dans `StructuralBreakEvidenceRule.NormalizeStrength`,
`r = max(PositiveCusum, |NegativeCusum|) / Threshold` reconstruit depuis `CusumResult` (garde :
`Threshold ≤ 0` ou ratio non fini ⇒ `0.0`).

```
RESULT A — CONTRACT FIXED
STRENGTH VARIANCE DETECTED : 0        →  0.01488   (StdDev 0 → 0.122)
SATURATION (Strength ≥ 0.9999 | Detected) : 100 %  →  0 %
DISTINCT VALUES (| Detected)          : 1         →  10 636 / 10 666
MONOTONICITY  : PASS  (0 violations / 11 428 paires ordonnées)
BOUNDED       : PASS  ([0,1), jamais 1.0 pour r fini)
DETERMINISM   : PASS  (hash e51d91be… identique sur deux passes ; + suite dédiée)
LOOK-AHEAD    : PASS  (fonction pure de PositiveCusum/NegativeCusum/Threshold de la barre, déjà causaux)
NO NEW PARAMETER : PASS  (s = 0.5 ancré sur le seuil Page-CUSUM préexistant)
```

---

## 17. Regression Analysis

**Les 5 autres dimensions ne doivent pas changer.** Vérifié :

- `StructuralBreakEvidenceRule` n'écrit **que** la clé `FusionDimension.StructuralBreak` du
  `FusionResultBuilder` (test `StructuralBreakFusionDimensionTests.Evaluate_WritesToStructuralBreakDimensionKey_NoOtherKey`).
- `Lot16ContractRegressionTests` compare **bit-à-bit** la `Value` brute des 4 dimensions
  `Stationarity/Persistence/MeanReversion/RandomWalk` entre le moteur de production (5 règles) et un
  moteur de référence (4 règles, sans `StructuralBreakEvidenceRule`) sur chaque barre → **0 écart**
  (sinon le test lève une exception explicite).
- `StructuralStability` est produite uniquement par `FusionStateManager` (pas une `IFusionRule`) — non
  affectée par construction.
- `bar.Decision.Winner` (production) vs un `DecisionEngine` de référence alimenté par le résultat
  stabilisé du moteur 4 règles → **0 mismatch** (`Lot16ContractRegressionTests`). Cohérent avec le
  fait qu'**aucune `IDecisionRule` ne lit `FusionDimension.StructuralBreak`**
  (`StructuralBreakRegressionTests`, inchangé, continue de passer).

**Ce qui change (par conception, c'est l'objet du lot) :**

- `StructuralBreak.Strength` / `RawValue` / `StableValue` — de saturé-à-1.0 à `r/(1+r)`.
- Assertions de test mises à jour : `StructuralBreakEvidenceRuleTests` (valeurs `Strength` attendues),
  `StructuralBreakStrengthDistributionAuditTests` §B.2 (assertion inversée). Justification : ces
  assertions épinglaient explicitement l'ancien contrat saturant que ce lot est mandaté pour réparer.
- Sorties CSV de recherche des Lots 15.8/15.9 (`StrengthDistribution.csv`, `PipelineTraceStages.csv`,
  `structuralbreak_evidence_lot158.csv`, …) : régénérées avec de nouveaux nombres au prochain run —
  effet de bord inoffensif, même catégorie que la régénération connue des CSV de `StopLossCalibration`.

**Vérification exécutée :**

- `StructuralBreakRegressionTests.Integration_Network_MarketStateWinner_IsBitIdentical_BeforeAndAfterStructuralBreakEvidenceRule`
  — **PASS**, **0 mismatch** sur `bar.Decision.Winner` (production 5 règles vs référence 4 règles +
  `DecisionEngine` de référence), sur ~11 300 barres réelles. Test **inchangé**, exécuté après la
  réparation.
- `Lot16ContractRegressionTests` — **PASS** : bornes `[0,1)` sur les 11 428 barres, 0 violation de
  monotonie, variance parmi détectées 0.01487, 10 637 valeurs distinctes, saturation 0 %, hashs
  déterministe et run-isolation identiques, 4 dimensions d'évidence bit-identiques 5-règles vs
  4-règles, 0 mismatch `Decision.Winner`.
- Suite `Tests/Fusion` + `Tests/Decision` + `Tests/GoldenDatasets` (CUSUM/Bai-Perron) — **63/63 PASS**
  sans réseau.
- Suite `Lot16StructuralBreakStrengthContractTests` + `StructuralBreakEvidenceRuleTests` +
  `StructuralBreakFusionDimensionTests` — **48/48 PASS** sans réseau.
- `StructuralBreakLookAheadTests`, `StructuralBreakDeterminismTests`, `StructuralBreakRunIsolationTests`
  — **PASS**, inchangés.
- `StructuralBreakStrengthDistributionAuditTests` (Lot 15.9, §B.2 assertion inversée) — **PASS** ;
  §B.1 (mesure `Cusum.Confidence`, produit par le `CusumStatistics.Compute` non touché) — **PASS**,
  inchangé.

---

## 18. Look-Ahead Analysis

`NormalizeStrength(cusum)` est une **fonction pure** des champs `PositiveCusum`, `NegativeCusum`,
`Threshold` de la `CusumResult` de la barre courante. Ces trois champs sont produits par
`RegimeEngine.Collect` à partir de fenêtres glissantes **strictement passées** (`_priceBuffer`
alimenté puis lu ; look-ahead déjà exclu et testé au Lot 15.8). Aucune information future, aucune
statistique globale, aucune normalisation par quantile empirique (qui aurait exigé un historique et
posé un risque de fuite). La barre `i` ne dépend que de l'information disponible à `i`.

Le test existant `StructuralBreakLookAheadTests.AppendingFutureBars_NeverChangesAnyEarlierBarsStructuralBreakContractOrStabilizedDimension`
(préfixe tronqué vs série étendue, comparaison bit-à-bit de `contract.Strength` et de la valeur
stabilisée à la frontière) **continue de passer sans modification** sous Lot 16 — la meilleure preuve
que la transformation n'introduit aucune dépendance temporelle.

---

## 19. Determinism

- Harnais de comparaison Phase 3 : double agrégation, `Hash1 == Hash2 == e51d91bea26a7d3e667fedb91007a083e25f9b6525af9cf3eb9e14f44688678b`.
- `Lot16StructuralBreakStrengthContractTests.Strength_IsBitIdentical_AcrossRepeatedEvaluationsOfTheSameInput` :
  50 évaluations, `BitConverter.DoubleToInt64Bits` identiques.
- `Lot16ContractRegressionTests` : SHA-256 sur la séquence complète des `Strength` par barre,
  identique entre deux reconstructions depuis le même `BacktestSignalPipelineResult`.
- `r/(1+r)` est de l'arithmétique flottante déterministe (pas de dépendance à l'ordre de réduction, pas
  de RNG, pas de parallélisme).

---

## 20. Run Isolation

- `NormalizeStrength` est `static`, sans état, sans champ mutable ; `StructuralBreakEvidenceRule` ne
  détient aucun état entre barres.
- `Lot16ContractRegressionTests` exécute **deux** `BacktestEngine().RunSignalPipeline(scenario, …)`
  indépendants sur le même scénario et compare le SHA-256 des `Strength` → identiques.
- Chaque `BuildRows` construit des `EvidenceFusionEngine` / `FusionStateManager` / `DecisionEngine`
  neufs. Aucun état partagé entre exécutions.
- Tests existants `StructuralBreakRunIsolationTests` / `StructuralBreakDeterminismTests` (comparaison
  run-A vs run-B de `contract.Strength`) continuent de passer.

---

## 21. Yahoo Dataset Identity

| Champ | Harnais de comparaison (Phase 3) | Régression / non-saturation (Phase 5) |
|---|---|---|
| Symbole / Timeframe | `MES` / `M5` (`MES=F` continu) | idem |
| Profondeur | `DefaultMaxChunkSpanDays` (~59 jours) | idem |
| BarCount | 11 556 | 11 556 |
| Range | 2026-06-29T14:55:00Z .. 2026-08-27T14:48:27Z | 2026-06-29T15:20:00Z .. 2026-08-27T15:13:33Z |
| Fingerprint | `6827736DF258E65EE4CE7A1557C007822821181549A89DF611D1C09EC5E33D1D` | `F81BDA59841A5DEA15665A1E5A7FADAB4B9A07D6106021CD0251978A303F2F24` |
| Warm-up | 128 barres | 128 barres |
| ReadyBars / ValidCusum / Detected | 11 428 / 11 428 / 10 666 | 11 428 / 11 428 / 10 667 |
| Déterminisme `Strength` (SHA-256 sur la séquence par barre) | `e51d91be…` (agrégats) | `c2cd0ce84edfed4edbe32097b39a3e6dc8c8ea3bc0a03aa00c46cc5c0d9c3634` |
| ATAS | non utilisé | non utilisé |

Les deux tirages Yahoo (à ~25 min d'intervalle) donnent des `Strength` de `StructuralBreak` parmi les
barres détectées quasi identiques : variance **0.01487 → 0.01487**, valeurs distinctes **10 636 → 10 637**,
saturation **0 % → 0 %**, médiane **0.937 → 0.937**, min **0.500 → 0.500**, max **0.9998 → 0.9998**.
La dérive jour-à-jour du tirage est normale et attendue ; le **mécanisme** (`r/(1+r)`) est indépendant
du dataset.

Dérive jour-à-jour du tirage Yahoo normale et attendue (fenêtre glissante, non déterministe par
construction — documenté depuis le Lot 15.0). Le **mécanisme** (fonction `r/(1+r)`) est indépendant du
dataset ; seules les statistiques descriptives dépendent du tirage.

---

## 22. Limitations

1. Dataset unique de ~59 jours (rappel constant depuis le Lot 15.0). La forme de la distribution de `r`
   (médiane 14.79, queue à ~6000) pourrait différer sur un autre instrument/timeframe ou une fenêtre
   plus longue. `r/(1+r)` reste bornée et monotone quelle que soit la distribution, mais la
   **répartition** de `Strength` sur `[0,1]` en dépendrait.
2. Le choix de C1 sur C6 repose sur la variance mesurée et la force de l'ancrage `r=1`. C6 (échelle
   log) resterait défendable ; un dataset où `r` serait encore plus étalé pourrait déplacer le
   compromis. Aucune re-décision n'est prévue sans mandat explicite.
3. La corrélation −0.49 avec `MeanReversion` est linéaire (Pearson) ; une dépendance non linéaire plus
   riche n'a pas été testée (hors périmètre).
4. `Strength` reste dupliqué sur `FusionConfidence.Value` **et** `.Confidence` — Lot 16 ne corrige que
   le *contrat du nombre*, pas la duplication du canal. Un futur lot devrait donner à
   `StructuralBreak` une méta-confiance réellement distincte (ou statuer que la duplication est
   acceptable).
5. Le `Math.Clamp` saturant subsiste dans `CusumStatistics.Compute` — `cusum.Confidence` reste
   saturé pour ses autres consommateurs (`ScientificDashboard`, `BacktestFingerprint`). Hors périmètre
   d'un lot cadré StructuralBreak ; nécessiterait un audit séparé des autres appelants.

---

## 23. Calibration Readiness Update

**Avant Lot 16** (Lot 15.9) : `REWEIGHTING NOT READY` — le seul champ continu du contrat (`Strength`)
était prouvé non fonctionnel une fois la détection déclenchée.

**Après Lot 16** : le **blocage de contrat est levé**. `Strength` est désormais un score continu
borné, monotone, non saturé, déterministe, sans paramètre arbitraire, à variance réelle
(0.0149 parmi les barres détectées, 10 636 valeurs distinctes).

**Ce qui reste avant une repondération** (non traité ici, aucune obligation) :

- Décider si `StructuralBreak` doit avoir un consommateur (`IDecisionRule` dédiée, ou pondération dans
  les règles existantes) — décision d'architecture, jamais dérivée du P&L.
- Statuer sur la duplication `Value == Confidence` (Limitation 4).
- Étudier conjointement l'impact d'un éventuel changement d'`alpha`/`HysteresisThreshold` sur les 6
  dimensions (rappel Lot 14.17 §27) — indépendant de ce lot.
- Caractériser la distribution de `Strength` sur un dataset plus long / walk-forward avant tout
  seuillage.

**CALIBRATION : NOT EXECUTED. REWEIGHTING : NOT EXECUTED.**

---

## 24. Recommended Next Lot

Deux options, dans cet ordre de prudence :

1. **StructuralBreak Value/Confidence Channel Separation** (audit + design) — donner à la dimension une
   méta-confiance distincte de son score, ou documenter/justifier formellement la duplication. Petit,
   isolé à `StructuralBreakEvidenceRule`.
2. **StructuralBreak Decision Consumption Design** (audit + design, PAS d'implémentation de stratégie) —
   décider *si* et *comment* une `IDecisionRule` devrait lire `FusionDimension.StructuralBreak`, sans
   référence au P&L, avant tout lot de repondération.

**Ne pas** enchaîner directement sur un lot de reweighting utilisant `StructuralBreak` tant que (1) et
(2) ne sont pas tranchés. **Ne pas** toucher `CusumStatistics.Compute` sans un audit séparé et
explicitement autorisé de ses autres appelants.

---

## 25. Handoff Context

```
PROJECT:            IQIA
CURRENT LOT:        16 (Fusion Dimension Contract Repair & Scientific Normalization)
PREVIOUS LOT:       15.9 (StructuralBreak Information Content Audit)

STRUCTURALBREAK STRENGTH:
  FIXED. Was Strength = Math.Clamp(cusum.Confidence, 0, 1) -> saturated at exactly 1.0 for 100% of
  ChangeDetected bars (variance 0, 1 distinct value among ~10.7k detected bars). Now
  Strength = r / (1 + r), r = max(PositiveCusum, |NegativeCusum|) / Threshold, reconstructed inside
  StructuralBreakEvidenceRule.NormalizeStrength from CusumResult fields. CusumStatistics.Compute NOT
  touched (protected, other consumers). Measured on real Yahoo MES M5: variance 0.01488 among detected
  bars, 10636 distinct values, 0% saturation, 0 monotonicity violations, deterministic, no look-ahead.

TRANSFORM CHOICE:
  Compared C0(clamp), C1=r/(1+r), C2=tanh, C3=1-e^-r, C4=(2/pi)atan, C5=r/sqrt(1+r^2),
  C6=ln(1+r)/(1+ln(1+r)). All parameter-free, monotone, bounded, deterministic, per-bar. C2/C3 still
  saturate (62-76%) -> rejected. C1/C4/C6 = 0% saturation. C1 chosen: highest detected-zone variance
  (0.01488), most distinct values (10636), and the cleanest zero-parameter anchor - s = 0.5 at r = 1,
  which is exactly the pre-existing Page-CUSUM detection boundary (peak == threshold), not a tuned
  constant.

RAW CUSUM RATIO (real Yahoo, detected bars):
  N=10666, min 1.0001, max 5982.76, mean 47.0, median 14.79, variance 20357. Heavy right tail.

OTHER 5 DIMENSIONS:
  UNCHANGED. StructuralBreakEvidenceRule writes only the StructuralBreak key. Bit-identical raw Value
  for Stationarity/Persistence/MeanReversion/RandomWalk between the 5-rule and 4-rule engines
  (Lot16ContractRegressionTests). StructuralStability produced only by FusionStateManager - unaffected.
  Decision.Winner: 0 mismatches vs a reference DecisionEngine. No IDecisionRule reads StructuralBreak.

REDUNDANCY NOTE:
  De-saturating Strength reveals a moderate NEGATIVE Pearson correlation with MeanReversion.Value
  (~-0.49 for C1, was -0.21 for the constant C0). This is a real, previously-masked market relationship
  (CUSUM severity vs mean-reversion strength), disjoint evidence sources (CUSUM vs HalfLife), |r| < 0.7
  -> PARTIAL_CORRELATION, not REDUNDANT. Documented, does not block the repair.

VALUE == CONFIDENCE:
  Still duplicated on StructuralBreak (both = contract.Strength). OUT OF LOT 16 SCOPE (Phase 2 is the
  Strength number, not the channel duplication). Flagged for a successor lot.

PRODUCTION MODIFIED:  YES - StructuralBreakEvidenceRule.cs only (1 method added, 1 line changed, doc/comments).
CALIBRATION:          NOT EXECUTED
REWEIGHTING:          NOT EXECUTED
RISK ENGINE:          NOT MODIFIED
STOP LOSS:            NOT MODIFIED
DECISION RULES:       NOT MODIFIED
ATAS:                 NOT USED
ORDERS:               NONE
DLL DEPLOYED:         NO
COMMIT:               NO

CALIBRATION READINESS:
  Contract block LIFTED. Strength is now a usable bounded continuous score. Still pending before any
  reweighting: (a) Value/Confidence channel separation, (b) explicit decision on whether/how a
  DecisionRule consumes StructuralBreak, (c) joint alpha/hysteresis study across all 6 dims,
  (d) longer-dataset / walk-forward characterization of the Strength distribution.

NEXT LOT:
  "StructuralBreak Value/Confidence Channel Separation" (audit/design), then "StructuralBreak Decision
  Consumption Design" (audit/design) - both BEFORE any reweighting lot. Never touch
  CusumStatistics.Compute without a separate authorized audit of its other callers.

DO NOT DO:
  Do NOT reweight using StructuralBreak until the Value/Confidence duplication and the
  consumption-design question are resolved. Do NOT modify CusumStatistics.Compute's Math.Clamp. Do NOT
  treat the -0.49 MeanReversion correlation as redundancy. Do NOT calibrate the transform or search for
  an alternative without an explicit mandate.
```

---

## FINAL OUTPUT

```
STATUS:
LOT 16 COMPLETE — RESULT A (STRENGTH CONTRACT FIXED)

SIX DIMENSION AUDIT:
PASS

NUMERIC CONTRACT INVENTORY:
PASS

STRUCTURALBREAK STRENGTH:
FIXED

SATURATION:
BEFORE:  100.0% of detected bars at Strength == 1.0 (1 distinct value, variance 0)
AFTER:   0.0% of detected bars saturated (>= 0.9999); 10636 distinct values

VARIANCE:
BEFORE:  0            (StdDev 0)      among detected bars
AFTER:   0.01488      (StdDev 0.122)  among detected bars

MONOTONICITY:
PASS   (0 violations over 11428 ratio-ordered real bars; f'(r) = 1/(1+r)^2 > 0)

BOUNDED:
PASS   (Strength in [0, 1); never exactly 1.0 for finite r)

DETERMINISM:
PASS   (double-pass hash e51d91be... identical; dedicated bit-identical suite)

LOOK-AHEAD:
PASS   (pure per-bar function of PositiveCusum/NegativeCusum/Threshold, already causal;
        existing StructuralBreakLookAheadTests still green unmodified)

RUN ISOLATION:
PASS   (two independent BacktestEngine runs -> identical Strength SHA-256; stateless static method)

OTHER 5 DIMENSIONS:
UNCHANGED   (bit-identical raw Values for the 4 evidence dims; StructuralStability unaffected;
             Decision.Winner: 0 mismatches; no IDecisionRule reads StructuralBreak)

CALIBRATION:
NOT EXECUTED

REWEIGHTING:
NOT EXECUTED

PARAMETERS CHANGED:
NO   (r/(1+r) has no calibration parameter; alpha/hysteresis untouched)

PRODUCTION MODIFIED:
YES   (Engine/Fusion/Rules/StructuralBreakEvidenceRule.cs only — 1 method added, 1 line changed)

RISK ENGINE:
NOT MODIFIED

STOP LOSS:
NOT MODIFIED

DECISION RULES:
NOT MODIFIED

ATAS:
NOT USED

ORDERS:
NONE

DLL DEPLOYED:
NO

COMMIT:
NO

TESTS:
  Added:
    Tests/Fusion/Lot16StructuralBreakStrengthContractTests.cs                       (24 assertions, network-free)  PASS
    Tests/Research/Lot16ContractNormalization/Lot16StrengthTransformComparisonTests.cs (Phase 3/4 comparison)        PASS
    Tests/Research/Lot16ContractNormalization/Lot16ContractRegressionTests.cs        (bounds/mono/non-sat/determ/
                                                                                     run-iso/5-dim regression/identity) PASS
  Updated (StructuralBreak-only, obsolete assertions on the pre-Lot-16 saturating contract):
    Tests/Fusion/StructuralBreakEvidenceRuleTests.cs
    Tests/Research/StructuralBreakInformationAudit/StructuralBreakStrengthDistributionAuditTests.cs  (§B.2 inverted)
  Unchanged, re-run green after the repair:
    StructuralBreakRegressionTests (0 Decision.Winner mismatches), StructuralBreakLookAheadTests,
    StructuralBreakDeterminismTests, StructuralBreakRunIsolationTests,
    Tests/Fusion + Tests/Decision + Tests/GoldenDatasets (CUSUM/BaiPerron) = 63/63,
    Lot16 + StructuralBreak unit suites = 48/48

DOCUMENTATION:
Documentation/Scientific/QDE-012_Sprint_15.25_Lot16_SixDimension_Contract_Normalization_Report.md

CALIBRATION READINESS:
Contract block LIFTED. Pending before reweighting: Value/Confidence channel separation;
StructuralBreak decision-consumption design; joint alpha/hysteresis study; longer-dataset
characterization.

NEXT LOT:
"StructuralBreak Value/Confidence Channel Separation" (audit/design), then "StructuralBreak Decision
Consumption Design" (audit/design). No reweighting until both are resolved.
```

**STOP.**
