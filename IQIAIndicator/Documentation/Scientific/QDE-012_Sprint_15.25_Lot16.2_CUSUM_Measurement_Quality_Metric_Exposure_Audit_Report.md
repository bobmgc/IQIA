# QDE-012 — Sprint 15.25 — Lot 16.2 — CUSUM Measurement-Quality Metric Exposure Audit

**Date** : 2026-08-27
**Branche** : `feature/structural-stability-v2`
**Lot précédent** : 16.1 (StructuralBreak Confidence Decoupling — verdict UNRESOLVED)
**Type** : AUDIT / RESEARCH / CONTRACT DESIGN — **aucune modification de production**
**Verdict** : _(voir §25 / FINAL OUTPUT)_

---

## 1. Executive Summary

Question : **l'implémentation CUSUM actuelle calcule-t-elle déjà une quantité pouvant servir de
métrique de qualité de mesure (fiabilité, stabilité, support statistique, robustesse de détection)
indépendante de la magnitude de l'événement détecté ?**

Réponse (démonstration §8–§16) : **NON.** Le trace statique complet montre que toute quantité calculée
par `CusumStatistics.Compute` appartient à l'une des catégories suivantes :

- **magnitude** : `PositiveCusum` (S⁺), `NegativeCusum` (S⁻), `peakMagnitude`, `Confidence` (=`min(r,1)`),
  `levelScore`, `varianceScore` ;
- **échelle de bruit × constante** : `referenceValue` k, `Threshold` h, `referenceStandardDeviation`
  σ₀ — non bornées, en unités de prix, estimées sur 7 points ;
- **localisation** : `positiveCandidateIndex`, `negativeCandidateIndex`, `EstimatedBreakIndex` ;
- **étiquette catégorielle** : `SeriesKind` (« niveau » vs « variance ») — nature du changement, pas sa
  fiabilité ;
- **dégénérée après warm-up** : `SampleSize` (= 30 constant), `calibrationSize` (= 7 constant),
  `IsValid` (= true parmi les barres disponibles).

Aucune p-value, aucun compte de confirmation post-détection, aucune dispersion bootstrap, aucune mesure
de persistance du franchissement de seuil n'est calculée. Sept candidats dérivés (Q1–Q7) ont été
évalués empiriquement sur 9 familles de séries synthétiques déterministes à propriétés structurelles
connues (§11) via une **réplique fidèle bit-à-bit** de `CusumStatistics.Compute`. Aucun ne satisfait
simultanément : indépendance de la magnitude + sémantique de fiabilité défendable + non-dégénérescence.

**Conséquence** : le découplage `Confidence`/`Strength` de `StructuralBreak` reste **impossible sans
redesign du calcul CUSUM lui-même** (ajout d'une vraie statistique de support). Ce redesign n'est **pas**
réalisé dans ce lot.

---

## 2. Scope

**Ajouté (observationnel, ne modifie aucune production) :**

- `Tests/Research/Lot16ContractNormalization/Lot162CusumInternalQualityAuditTests.cs` — réplique fidèle
  de `CusumStatistics.Compute` (portée verbatim, réutilise le vrai `CusumMath`), **gate de fidélité
  bit-à-bit** contre `CusumValidation.RunOnSeries` sur chaque fenêtre, puis exposition et évaluation des
  quantités internes que `CusumResult` jette.
- ce rapport.

**Production modifiée :** **AUCUNE.** `CusumStatistics`, `CusumResult`, `CusumEvidence`, `CusumMath`,
`FusionStateManager`, `StructuralBreakEvidenceRule`, `DecisionEngine`, toutes les `IDecisionRule`,
`SignalEngine`, `EntryTriggerBuilder`, `EntryTriggerEngine`, `TradePlanBuilder`, `RiskEngine`,
`RiskPolicy`, `ExecutionSimulator` : non touchés.

**Interdictions respectées :** NO CALIBRATION, NO REWEIGHTING, NO OPTIMIZATION, NO PARAMETER SEARCH, NO
FUSION CHANGE, NO DECISION/SIGNAL/ENTRY/RISK/STOP-LOSS/EXECUTION CHANGE, NO ATAS, NO ORDERS, NO DLL, NO
COMMIT.

**Yahoo :** **non utilisé.** Rate-limit HTTP 429 actif depuis le Lot 16.1 ; une sonde risquerait un
hang de retry/backoff (brief §19). L'audit tourne sur des séries synthétiques déterministes graines
fixes — substrat **plus fort** pour un audit de qualité de mesure (scénarios structurels connus :
bruit blanc, marche aléatoire, AR(1), OU, tendance, rupture de moyenne, rupture de variance, haute/basse
volatilité).

---

## 3. Previous Lot Context

- **Lot 15.9** — audit de contenu informationnel de `StructuralBreak`. Détection CUSUM active et
  informative ; ratio brut CUSUM à forte variance ; `Cusum.Confidence` sature à 1.0 après détection ;
  saturation tracée au `Math.Clamp(r, 0, 1)` **à l'intérieur** de `CusumStatistics.Compute` (protégé) ;
  Bai-Perron porte une variation indépendante ; `StructuralBreak` non redondant avec les 5 autres
  dimensions ; **reweighting NOT READY** car le contrat continu `Strength` était dégénéré.
- **Lot 16** — correction du contrat `Strength` **sans** toucher CUSUM : `Strength = r / (1 + r)`,
  `r = peakMagnitude / threshold` reconstruit dans `StructuralBreakEvidenceRule` depuis les champs déjà
  exposés. Résultats : saturation 100 %→0 %, variance restaurée (0→0.0149), >10 000 valeurs distinctes,
  monotonie/déterminisme/look-ahead/run-isolation PASS, 5 autres dimensions bit-identiques,
  `Decision.Winner` bit-identique. `Strength` est désormais un **signal de magnitude** informatif.
- **Lot 16.1** — audit des sources de `Confidence` indépendantes. `Evaluate` pose toujours
  `Value = Confidence = contract.Strength` ⇒ `corr(Value, Confidence) = 1.000` (RAW + STABLE,
  11 428/11 428 bit-identiques). 5 candidats (agreement Bai-Perron, vieux clamp, BreakCount/8, adéquation
  SampleSize, stabilité `EstimatedBreakIndex`) — **aucun viable**. Cause racine identifiée : **le contrat
  CUSUM n'expose pas de métrique de qualité de mesure indépendante de la magnitude.** Verdict :
  `CONFIDENCE CONTRACT: UNRESOLVED`. Recommandation : ce lot 16.2.

---

## 4. CUSUM Dataflow (Phase 0 — trace statique du code réel)

Source : `Engine/Regime/Evidence/CUSUM/CusumStatistics.cs` (+ `CusumMath.cs`, `CusumResult.cs`,
`CusumEvidence.cs`). Fenêtre de production : `RegimeEngine.CusumWindowSize = 30`,
`CusumMinimumSampleSize = 20`. Entrée = fenêtre glissante de `context.Price.Close` (prix bruts,
oldest-first). Aucune sémantique inférée d'un nom : chaque ligne est tracée.

```
INPUT
  series : IReadOnlyList<double>  — fenêtre de 30 prix bruts (oldest-first)
        │
PREPROCESSING
  guard  series.Count < 8                       → Invalid("court")
  guard  any !double.IsFinite(series[i])        → Invalid("non finie")
  calibrationSize = Math.Max(4, series.Count/4) → 7  (CONSTANT pour n=30)   [discret]
  guard  calibrationSize >= series.Count        → Invalid("pre-echantillon")
        │
STATISTICAL BASELINE   (calculé sur series[0 .. calibrationSize-1] — les 7 PREMIÈRES barres uniquement)
  referenceMean  μ₀ = CusumMath.Mean(series, calib)                 [double, unités prix, LOCALISATION]
  referenceVariance σ₀² = CusumMath.Variance(series, calib, μ₀)     [double ≥ 0, unités prix², BRUIT de calib]
  guard  σ₀² ≤ 1e-15  →  Invalid("variance de reference nulle")
  referenceStandardDeviation σ₀ = sqrt(σ₀²)
        │
CUMULATIVE STATISTICS   (Page-CUSUM, exécuté DEUX fois)
  ── Run 1 (NIVEAU) : RunPageCusum(series, calib, μ₀, σ₀) ──
       k (slack)     = σ₀ · sqrt(2·ln n / n) / 2        = σ₀ · c₁(n)        [= h / (2n) — COLINÉAIRE à h]
       h (threshold) = σ₀ · sqrt(2·n·ln n)              = σ₀ · c₂(n)        [ÉCHELLE de bruit × const]
       boucle i = calib .. n-1 :
          dev            = series[i] − μ₀
          positiveCusum  = max(0, positiveCusum + dev − k)                  [MAGNITUDE, somme unilatérale]
          negativeCusum  = min(0, negativeCusum + dev + k)                  [MAGNITUDE, somme unilatérale]
          si positiveCusum==0 : positiveCandidateIndex = i+1               [LOCALISATION — début du run courant]
          si negativeCusum==0 : negativeCandidateIndex = i+1               [LOCALISATION]
          maximumPositiveCusum = max(…, positiveCusum)   → S⁺              [MAGNITUDE, pic]
          minimumNegativeCusum = min(…, negativeCusum)   → S⁻              [MAGNITUDE, pic]
          détection : (S⁺>h) ou (|S⁻|>h)  → changeDetected latch, estimatedBreakIndex = candidate au 1er franchissement
  ── Run 2 (VARIANCE) : sur squaredDeviations[i] = (series[i]−μ₀)² ──
       varianceOfSquaredDeviations = CusumMath.Variance(sqDev, calib, σ₀²) [dispersion 4e-moment de la calib]
       si  varianceOfSquaredDeviations ≤ 1e-15  →  varianceRun = default (tout à 0) — RUN NON EXÉCUTÉ
       sinon RunPageCusum(sqDev, calib, σ₀², sqrt(varianceOfSquaredDeviations))
        │
SELECTION
  levelScore    = max(S⁺_L,|S⁻_L|) / h_L                              [RATIO magnitude / seuil]
  varianceScore = max(S⁺_V,|S⁻_V|) / h_V   (0 si h_V ≤ 0)             [RATIO magnitude / seuil]
  selectedRun   = (varianceScore ≤ levelScore) ? levelRun : varianceRun
  SeriesKind    = « niveau » ou « variance »                          [ÉTIQUETTE catégorielle — nature du changement]
        │
PEAK / EXTREMUM
  peakMagnitude = max(selectedRun.PositiveCusum, |selectedRun.NegativeCusum|)   [MAGNITUDE]
        │
DETECTION DECISION
  Confidence = selectedRun.Threshold ≤ 0 ? 0 : Math.Clamp(peakMagnitude / selectedRun.Threshold, 0, 1)
             = min(r, 1)                                              [DÉRIVÉE de magnitude — champ saturant du Lot 15.9]
        │
CUSUM RESULT  (champs exposés)
  ChangeDetected, EstimatedBreakIndex, PositiveCusum, NegativeCusum, Threshold, Confidence, SampleSize,
  IsValid, Explanation ("CUSUM {SeriesKind}: S+=…, S-=…, h=…, k=…")
```

**Relations algébriques établies par la trace :**

- `h = 2n · k` exactement (n = `SampleSize`) ⇒ `k` ne porte **aucune** information que `h` n'a pas.
- `h = σ_run · sqrt(2 n ln n)` ⇒ pour n fixe, `h` est **une fonction affine de σ_run seul** (échelle de
  bruit de la calibration, ou du 4e-moment pour le run variance).
- `Confidence = min(peakMagnitude / h, 1)` ⇒ **dérivée pure de magnitude** (c'est le champ que le Lot 16
  a contourné).

---

## 5. Internal Quantity Inventory (Phase 1)

| # | Quantité | Signification | Type | Plage | Continu ? | Dépend de la magnitude ? | Peut représenter la qualité de mesure ? | Déjà exposée ? | Exposer ⇒ modifier le contrat ? |
|---|---|---|---|---|---|---|---|---|---|
| 1 | `series` | fenêtre de prix bruts | `double[]` | prix > 0 | oui | — (entrée) | non | n/a | n/a |
| 2 | `calibrationSize` | taille du pré-échantillon = `max(4, n/4)` | int | 7 (n=30) | non | non | non — **constant** | non | oui (nouveau champ) |
| 3 | `referenceMean` μ₀ | moyenne des 7 premières barres | double | unités prix | oui | non (localisation) | non | non | oui |
| 4 | `referenceVariance` σ₀² | **bruit du sous-échantillon de calibration** | double ≥ 0 | unités prix² | oui | **non** (sous-fenêtre disjointe) | **partiellement** — non bornée, unités prix², estimée sur 7 pts | non | oui |
| 5 | `referenceStandardDeviation` σ₀ | `sqrt(σ₀²)` | double ≥ 0 | unités prix | oui | non | idem #4 | non | oui |
| 6 | `referenceValue` k (slack) | `σ_run · c₁(n)` | double ≥ 0 | unités prix | oui | non | non — `= h/(2n)`, colinéaire à h | non (dans `Explanation`) | oui |
| 7 | `threshold` h | `σ_run · sqrt(2 n ln n)` | double ≥ 0 | unités prix | oui | non | non — échelle de bruit × const, non bornée | **oui** (`Threshold`) | non |
| 8 | `positiveCusum` / `negativeCusum` (courant) | sommes cumulées unilatérales | double | ≥ 0 / ≤ 0 | oui | **OUI** | non | non (seuls les pics) | oui |
| 9 | S⁺ `maximumPositiveCusum` | pic d'excursion positive | double ≥ 0 | ≥ 0 | oui | **OUI — magnitude** | non | **oui** (`PositiveCusum`) | non |
| 10 | S⁻ `minimumNegativeCusum` | pic d'excursion négative | double ≤ 0 | ≤ 0 | oui | **OUI — magnitude** | non (mais symétrie S⁺/S⁻ → Q1) | **oui** (`NegativeCusum`) | non |
| 11 | `positiveCandidateIndex` / `negativeCandidateIndex` | index de début du run d'accumulation courant | int | [calib, n] | non | non (localisation) | non — forme du changement | non | oui |
| 12 | `changeDetected` (par run) | franchissement de seuil latché | bool | {F,T} | non | seuil (magnitude vs h) | non — c'est la détection, pas sa qualité | **oui** (`ChangeDetected`, run sélectionné) | non |
| 13 | `estimatedBreakIndex` | index candidat au 1er franchissement | int | [calib, n] ou −1 | non | non (localisation) | **partiellement** — sa *stabilité* image-à-image (Q7) ; rejetée Lot 16.1 | **oui** | non |
| 14 | `varianceOfSquaredDeviations` | dispersion 4e-moment de la calibration | double ≥ 0 | unités prix⁴ | oui | non | pilote l'exécution du run variance ; pas un score [0,1] | non | oui |
| 15 | `levelScore` / `varianceScore` | ratios `peak/seuil` des deux détecteurs | double ≥ 0 | ≥ 0 | oui | **OUI** (chacun) | leur **accord** (Q3) — partiellement indépendant, souvent dégénéré | non | oui |
| 16 | `SeriesKind` | détecteur retenu (« niveau » / « variance ») | enum-string | {niveau, variance} | non | non (catégoriel) | non — **nature** du changement, pas fiabilité (Q4) | **oui** (`Explanation`, string) | oui (si champ typé) |
| 17 | `peakMagnitude` | `max(S⁺_sel, \|S⁻_sel\|)` | double ≥ 0 | ≥ 0 | oui | **OUI — magnitude** | non | non (recalculable) | non |
| 18 | `Confidence` | `min(peakMagnitude / h_sel, 1)` | double | [0,1] | oui | **OUI — dérivée de magnitude** | non — champ saturant (Lot 15.9) | **oui** | non |
| 19 | `SampleSize` | longueur de fenêtre | int | 30 (constant) | non | non | non — **constant après warm-up** (Lot 16.1 D) | **oui** | non |
| 20 | `IsValid` | calcul abouti | bool | {F,T} | non | non | non — binaire, `false` filtré en amont ⇒ constant parmi les disponibles | **oui** | non |

### Réponses aux sous-questions Phase 1

- **A — Support d'échantillon.** `SampleSize` = 30 constant après warm-up (`calibrationSize` = 7
  constant). `IsValid` = true parmi les barres disponibles (le `false` est traité en amont comme
  « Missing Evidence »). **Aucune de ces quantités ne varie utilement** — même mode d'échec que la
  confiance `n/Window` d'ADF/KPSS, déjà signalé §3.D de l'audit indépendant et testée (candidat D) au
  Lot 16.1.
- **B — Stabilité de la ligne de base.** `referenceMean` (localisation), `referenceVariance` /
  `referenceStandardDeviation` (bruit de la calibration). σ₀² est calculé sur une sous-fenêtre
  **disjointe** de la zone de détection ⇒ **indépendant de la magnitude de l'événement**. Mais :
  non borné, unités prix², estimé sur **7 points** (erreur d'échantillonnage énorme). Un score borné en
  découlerait par ratio avec la dispersion de la fenêtre entière : **Q2 = σ₀² / σ_window²** (« le
  sous-échantillon de calibration était-il représentatif ? »). Évalué en Phase 3.
- **C — Rapport signal / bruit.** Le seul rapport calculé est `peak / threshold` (= `r`, la magnitude
  normalisée), reproduit par `levelScore` et `varianceScore`. Aucune autre grandeur signal/bruit
  indépendante n'est calculée.
- **D — Marge de détection.** L'algorithme **ne calcule pas** : distance à la frontière de décision
  autre que `r` lui-même ; persistance du franchissement ; **nombre d'observations confirmantes** (la
  boucle latche `changeDetected` au 1er franchissement et ne compte jamais les confirmations
  ultérieures) ; cohérence des excursions positives/négatives (autre que via S⁺/S⁻ eux-mêmes → Q1).
  ⇒ **NOT AVAILABLE** pour tout ce qui n'est pas dérivable de S⁺, S⁻, h, indices.
- **E — Symétrie positif / négatif.** S⁺ et S⁻ **sont exposés**. Pour un vrai changement de moyenne
  directionnel, un côté domine et l'autre reste ≈ 0 (la récursion `min(0, neg + dev + k)` reste bloquée
  à 0 sous un choc positif). Une série oscillante/bruitée fait accumuler les deux. **Q1 = 1 −
  min(|S⁺|,|S⁻|) / max(|S⁺|,|S⁻|)** — « propreté directionnelle » de la détection. Évalué en Phase 3
  (ne pas présumer que c'est significatif — le prouver).

---

## 6. Candidate Identification (Phase 2)

Sept candidats dérivés uniquement de quantités **déjà calculées** (aucune nouvelle statistique lourde),
tous **paramétriques-libres**, **par fenêtre**, **causals** :

| Code | Définition | Grandeurs utilisées | Sémantique proposée | Classe a priori |
|---|---|---|---|---|
| **Q1** `posNegSymmetry` | `max>0 ? 1 − min(\|S⁺\|,\|S⁻\|)/max(\|S⁺\|,\|S⁻\|) : 0` | champs exposés `PositiveCusum`, `NegativeCusum` | propreté / consistance directionnelle de la détection | **B** — partiellement indépendant |
| **Q2** `baselineDispRatio` | `referenceVariance / windowVariance` | internes σ₀², σ_window² (`CusumMath`) | représentativité du sous-échantillon de calibration | **B** — partiellement indépendant |
| **Q3** `detectorAgreement` | `min(levelScore, varScore) / max(...)`, 0 si run variance dégénéré | internes `levelScore`, `varScore` | accord des deux détecteurs (niveau vs variance) | **B/D** — souvent dégénéré |
| **Q4** `seriesKind` | 0.0 niveau / 1.0 variance | interne `SelectedKind` (croisé avec `Explanation`) | nature du changement (mean-shift vs variance-shift) | **E** — étiquette, pas fiabilité |
| **Q5** `relativeThreshold` | `Threshold / windowMean` | champ exposé `Threshold`, moyenne fenêtre | échelle de bruit relative au niveau de prix | **E / C** — échelle de bruit, dépend du prix |
| **Q6** `runLengthFrac` | détecté ? `clamp((estBreakIdx − calib)/(n − calib), 0, 1)` : n/a | champ exposé `EstimatedBreakIndex`, `calibrationSize` | forme du changement (graduel vs abrupt) | **E** — forme, pas fiabilité |
| **Q7** `estBreakIdxStable` | `estBreakIdx == fenêtre précédente ? 1 : 0` | champ exposé `EstimatedBreakIndex` (+ état inter-fenêtre) | stabilité de la localisation (Lot 16.1 candidat E, re-testé) | déjà rejeté 16.1 |

---

## 7. Candidate Classification (Phase 2, cadre)

Classes : **A** indépendante & sémantiquement valide · **B** partiellement indépendante · **C** dérivée
de magnitude · **D** constante/dégénérée · **E** varie mais sémantiquement invalide comme qualité de
mesure · **F** information non disponible dans le calcul actuel.

Classement **a priori** (issu du trace §4–§5) — confirmé/infirmé empiriquement en §8–§11 :

| Candidat | Classe a priori | Raison |
|---|---|---|
| Q1 `posNegSymmetry` | **B** | partage `max(|S⁺|,|S⁻|)` avec la magnitude mais dépend du **ratio** min/max |
| Q2 `baselineDispRatio` | **B** | σ₀² mesuré sur une sous-fenêtre disjointe (indépendant de l'événement) mais estimé sur 7 pts |
| Q3 `detectorAgreement` | **B → D** | deux ratios magnitude ; run variance `default` quand `varOfSqDev ≤ 1e-15` (fréquent) |
| Q4 `seriesKind` | **E** | étiquette de nature du changement, pas de fiabilité |
| Q5 `relativeThreshold` | **E** | échelle de bruit, dépend du niveau de prix, non bornée en pratique |
| Q6 `runLengthFrac` | **E** | forme du changement (abrupt/graduel), pas fiabilité |
| Q7 `estBreakIdxStable` | déjà rejeté 16.1 | binaire, repositionne un champ « diagnostic only » (Lot 15.7) |
| `calibrationSize`, `SampleSize`, `IsValid` | **D** | constants après warm-up |
| **une vraie statistique de support** (p-value CUSUM, compte de confirmation, dispersion bootstrap) | **F** | **jamais calculée** par l'implémentation actuelle |

---

## 8. Statistical Distributions (Phase 3)

`Lot162CusumInternalQualityAuditTests` — 9 familles de séries synthétiques déterministes (longueur
2 400, graine 42) × fenêtre glissante de 30 barres = **21 339 fenêtres valides**.
**Gate de fidélité réplique ↔ production : 0 mismatch / 21 339** (bit-à-bit sur
`ChangeDetected, EstimatedBreakIndex, PositiveCusum, NegativeCusum, Threshold, Confidence, SampleSize,
IsValid` + croisement `SeriesKind` via `Explanation`). ⇒ **les internes de la réplique sont
fiables.** `VarianceRunDegenerate = 0 %` (les séries synthétiques ont toutes une dispersion de
4e-moment non nulle ; sur données réelles quasi-constantes ce taux serait > 0). `DetectedPct = 56.8 %`.
`Strength` (référence magnitude) : moyenne 0.604, variance 0.0712.

| Candidat | N | Min | Max | Mean | Median | Std | Distinct | Sat ≥ 0.9999 | = 0 | Bande [.45,.55] | Transitions % |
|---|---|---|---|---|---|---|---|---|---|---|---|
| **Q1** `posNegSymmetry` | 21 339 | 0.0017 | 1.000 | 0.820 | 0.923 | 0.232 | 7 521 | 10.4 % | 0 % | 3.8 % | 92.8 % |
| **Q2** `baselineDispRatio` | 21 339 | 0.0033 | 3.070 | 0.787 | 0.714 | 0.560 | 9 542 | 33.0 % | 0 % | 6.1 % | 100 % |
| **Q3** `detectorAgreement` | 21 339 | 0.0178 | 0.9998 | 0.444 | 0.396 | 0.266 | 9 544 | **0 %** | 0 % | 9.2 % | 100 % |
| **Q4** `seriesKind` | 21 339 | 0 | 1 | 0.816 | 1 | 0.388 | **2** | 81.6 % | 18.4 % | 0 % | 13.9 % |
| **Q5** `relativeThreshold` | 21 339 | 6.8e−7 | **14.76** | 0.431 | 0.113 | **1.119** | 16 564 | 9.7 % | 0 % | 1.4 % | ~100 % |
| **Q6** `runLengthFrac` | 12 122 | 0 | 0.957 | 0.094 | 0.043 | 0.150 | **23** | 0 % | **45.8 %** | 2.1 % | ~41 % |
| **Q7** `estBreakIdxStable` | 21 330 | 0 | 1 | 0.501 | 1 | 0.500 | **2** | 50.1 % | 49.9 % | 0 % | 25.3 % |

## 9. Independence Analysis (Phase 3)

Corrélation de chaque candidat avec `Strength = r/(1+r)` (le signal de **magnitude** du Lot 16). Cible
d'indépendance : `|Pearson| < ~0.3` **et** sémantique de fiabilité défendable.

| Candidat | **Pearson(Strength)** | Spearman(Strength) | Égalité exacte à Strength | Verdict indépendance |
|---|---|---|---|---|
| Q1 `posNegSymmetry` | **+0.702** | +0.870 | 0 % | **✗ FAIL** — fortement couplé |
| Q2 `baselineDispRatio` | **−0.820** | −0.888 | 0 % | **✗ FAIL** — proxy inverse de magnitude |
| Q3 `detectorAgreement` | **−0.709** | −0.734 | 0 % | **✗ FAIL** — deux scores = magnitude |
| Q4 `seriesKind` | +0.446 | +0.439 | 13.9 % | **✗ PARTIAL** — couplé + 2 niveaux |
| Q5 `relativeThreshold` | **−0.254** | −0.441 | ~0 % | **~ le moins couplé**, mais voir §14 |
| Q6 `runLengthFrac` | −0.358 | −0.334 | (détecté only) | **✗ PARTIAL** |
| Q7 `estBreakIdxStable` | −0.478 | −0.478 | 25.3 % | **✗** — déjà rejeté Lot 16.1 |

**Aucun candidat n'atteint `|Pearson| < 0.3` avec, simultanément, une sémantique de fiabilité valide.**
Le seul sous le seuil de corrélation (Q5, −0.25) échoue sur la sémantique et le bornage (§14).

## 10. Correlation Analysis (Phase 3)

- **Control 2 (vs magnitude brute `r`)** : identique à Control-Strength à la transformation `r/(1+r)`
  monotone près — les Pearson/Spearman contre `r` suivent les mêmes signes et ordres de grandeur.
- **Control 3 (`Spearman(candidat, rawRatio)`)** — le candidat s'effondre-t-il en transformation
  monotone de la magnitude ?

| Candidat | `Spearman(candidat, rawRatio)` | Lecture |
|---|---|---|
| Q1 | **+0.870** | quasi transformation monotone de la magnitude |
| Q2 | **−0.888** | quasi transformation monotone **inverse** de la magnitude |
| Q3 | **−0.734** | largement une fonction (décroissante) de la magnitude |
| Q4 | +0.439 | partiellement lié |
| Q5 | −0.441 | partiellement lié |
| Q6 | −0.334 | partiellement lié |
| Q7 | −0.478 | partiellement lié |

Q1, Q2, Q3 sont **essentiellement des ré-expressions monotones de la magnitude** (Spearman ≈ ±0.7 à
±0.9). Q4–Q7 ne sont pas de pures transformations monotones, mais restent matériellement couplés
**et** sémantiquement invalides comme fiabilité (§14).

## 11. Temporal Analysis (Phase 3)

- **Q1, Q2, Q3, Q5** : `TransitionPct` ≈ 93–100 % — changent quasiment à chaque fenêtre (comme la
  magnitude brute, qui bouge à chaque barre). Ce ne sont pas « des versions lentes de Strength » — ce
  sont des co-variations rapides de la magnitude.
- **Q4** : `TransitionPct` 13.9 % — plus lent, mais c'est une **étiquette catégorielle** (niveau ↔
  variance), pas un signal continu.
- **Q6** : n/a hors détection (46 % à exactement 0) ; 23 valeurs distinctes.
- **Q7** : binaire, 50/50, `TransitionPct` 25.3 %.
- Comportement autour d'une rupture connue (`MeanBreakMid` / `VarianceBreakMid`) : quand la fenêtre
  englobe la rupture, Q2 chute (σ_window² gonflé par la rupture, σ₀² pré-rupture inchangé) exactement
  quand `Strength` monte ⇒ Q2 ≈ `1/magnitude`. Q1 monte (un côté domine) quand `Strength` monte. Aucun
  candidat ne présente un comportement **découplé** de la magnitude autour de l'événement.

## 12. Raw vs Stabilized Comparison

`FusionStateManager` (EMA α=0.20 + hystérésis 0.03) est un **traitement aval** : appliqué à n'importe
quel signal, il produit de la persistance d'état. La persistance d'état créée par l'EMA/hystérésis
**n'est pas** une confiance de mesure CUSUM. Aucun candidat ne peut être « sauvé » en le déclarant
mesure de qualité après stabilisation. Les candidats sont donc évalués **RAW** (par fenêtre CUSUM), pas
après `FusionStateManager`.

## 13. Bai-Perron Semantic Separation

**La qualité de mesure CUSUM et l'accord inter-détecteurs sont deux concepts distincts.** Si l'accord
CUSUM ↔ Bai-Perron est informatif (Lot 15.9 : Both = 92.78 %, BaiPerronOnly = 6.67 %), il doit être
classé **CROSS-DETECTOR AGREEMENT**, jamais renommé « CUSUM CONFIDENCE ». Le Lot 16.1 a déjà mesuré ce
candidat (`A_agreement`) : `Pearson(Strength) = 0.71`, 93 % saturé, 2 niveaux — **rejeté**. Ce lot ne le
réintroduit pas comme confiance CUSUM. Distinction sémantique explicite : Bai-Perron répond à « un
autre test voit-il aussi une rupture ? » ; la qualité de mesure CUSUM répondrait à « à quel point
l'estimation CUSUM de cette rupture est-elle fiable ? ».

## 14. Scientific Interpretation

Test central (brief §12) : **quelle information ce candidat représente-t-il que `Strength` ne
représente pas ?** Si la réponse n'est pas claire ⇒ REJETÉ.

- **Q1 `posNegSymmetry`** — « la détection est-elle directionnellement propre (un côté domine) ou
  ambiguë (les deux s'accumulent) ? » Réponse a priori intéressante. **Mais** empiriquement : une
  rupture nette et forte produit un côté dominant **et** une grande magnitude ⇒ Q1 ≈ « y a-t-il une
  grosse rupture nette ? » ≈ magnitude (`Pearson 0.70`, `Spearman(rawRatio) 0.87`). Elle ne porte pas
  d'information de fiabilité distincte. **REJET (classe C).**
- **Q2 `baselineDispRatio` = σ₀²/σ_window²** — « le sous-échantillon de calibration (7 barres) était-il
  représentatif de la dispersion de la fenêtre ? » Sémantique de fiabilité plausible. **Mais**
  empiriquement : quand une rupture est dans la fenêtre, σ_window² gonfle pendant que σ₀² (pré-rupture)
  reste bas ⇒ Q2 petit **exactement quand la magnitude est grande** (`Pearson −0.82`,
  `Spearman(rawRatio) −0.89`). Q2 est un **proxy inverse de la magnitude**, pas une mesure de fiabilité
  indépendante. **REJET (classe C).**
- **Q3 `detectorAgreement`** — accord des détecteurs niveau et variance. Non saturant (`0 %`),
  bien distribué. **Mais** `levelScore` et `varScore` sont **eux-mêmes** des ratios `peak/seuil`
  (magnitude) ; leur accord décroît quand un détecteur domine (grande magnitude sélectionnée) ⇒
  `Pearson −0.71`. C'est une fonction de deux magnitudes, pas une fiabilité indépendante. De plus, sur
  données réelles quasi-constantes le run variance dégénère (`default`) ⇒ Q3 → 0. **REJET (classe C,
  D en conditions réelles).**
- **Q4 `seriesKind`** — niveau vs variance. C'est la **nature** du changement (mean-shift ou
  variance-shift), pas sa fiabilité. 2 niveaux, 82 % à une seule valeur, `Pearson 0.45`. À classer
  séparément comme *change-type label*, jamais comme confiance. **REJET (classe E).**
- **Q5 `relativeThreshold` = h/windowMean** — échelle du seuil de détection relative au niveau de prix.
  Le moins couplé à la magnitude (`Pearson −0.25`). **Mais** : (a) **non bornée** (max 14.76, moyenne
  ≫ médiane) — la borner exigerait un paramètre ; (b) **dépend du niveau de prix** (h en unités de
  prix, normalisation grossière par la moyenne) ; (c) sémantiquement c'est la **volatilité relative /
  sensibilité du détecteur**, pas la fiabilité de l'estimation d'une rupture donnée. « Marché
  volatil » ≠ « mesure CUSUM peu fiable ». **REJET (classe E).**
- **Q6 `runLengthFrac`** — position, dans la fenêtre, du début du run d'accumulation qui a déclenché.
  C'est la **forme** du changement (abrupt vs graduel), partiellement liée à la magnitude
  (`Pearson −0.36`), 23 niveaux, 46 % à 0, définie seulement sur les fenêtres détectées. Pas une
  fiabilité. **REJET (classe E).**
- **Q7 `estBreakIdxStable`** — stabilité image-à-image de `EstimatedBreakIndex`. Binaire (50/50),
  `Pearson −0.48`, repositionne un champ « diagnostic only » (Lot 15.7). **Déjà rejeté au Lot 16.1
  (candidat E).** Confirmé.
- **Une vraie statistique de support** — p-value analytique/asymptotique de la statistique CUSUM,
  compte d'observations confirmant le franchissement après détection, dispersion bootstrap/permutation
  du pic. **Aucune n'est calculée** par `CusumStatistics.Compute`. **NOT AVAILABLE (classe F).**

## 15. Rejected Candidates

| Candidat | Classe finale | Motif de rejet |
|---|---|---|
| Q1 `posNegSymmetry` | **C** | ré-expression monotone de la magnitude (`Pearson 0.70`, `Spearman(r) 0.87`) |
| Q2 `baselineDispRatio` | **C** | proxy **inverse** de la magnitude (`Pearson −0.82`, `Spearman(r) −0.89`) |
| Q3 `detectorAgreement` | **C** (D en réel) | fonction de deux ratios magnitude ; run variance dégénère hors synthétique |
| Q4 `seriesKind` | **E** | étiquette de nature de changement, 2 niveaux, couplé `0.45` |
| Q5 `relativeThreshold` | **E** | échelle de bruit/volatilité, non bornée, dépendante du prix |
| Q6 `runLengthFrac` | **E** | forme du changement, 23 niveaux, 46 % à 0, couplé `−0.36` |
| Q7 `estBreakIdxStable` | rejeté 16.1 | binaire, champ « diagnostic only », couplé `−0.48` |
| `calibrationSize`, `SampleSize`, `IsValid` | **D** | constants après warm-up |
| p-value / compte de confirmation / dispersion bootstrap | **F** | **jamais calculés** |

## 16. Viable Candidates

**AUCUN.** Aucune quantité — exposée ou interne, brute ou dérivée — ne satisfait simultanément :
indépendance de la magnitude (`|Pearson| < ~0.3`), sémantique de fiabilité de mesure défendable,
granularité adéquate, bornage sans paramètre arbitraire.

## 17. Contract Options

- **OPTION A** — aucune métrique de qualité indépendante n'existe ⇒ `CONFIDENCE CONTRACT UNRESOLVED`,
  aucun changement de production.
- **OPTION B** — une quantité interne existante peut être exposée directement (nouveau champ
  `CusumResult.MeasurementQuality` correspondant à une grandeur déjà calculée).
- **OPTION C** — l'information existe mais la quantité doit être dérivée de plusieurs valeurs
  statistiques déjà calculées (définition mathématique explicite, sans paramètre arbitraire, sémantique
  claire, indépendance prouvée).
- **OPTION D** — l'implémentation CUSUM manque fondamentalement l'information nécessaire ⇒ un redesign
  futur du calcul CUSUM serait requis. **Non implémenté dans ce lot.**

_(sélection en §25 après preuve empirique.)_

## 18. Fingerprint Compatibility

Analyse statique (aucune modification) des dépendances à `CusumResult` :

- **`Backtest/BacktestFingerprint.cs` → `AppendCusum`** hache : `ChangeDetected`, `EstimatedBreakIndex`,
  `PositiveCusum`, `NegativeCusum`, `Threshold`, `Confidence`, `SampleSize`, `IsValid`. **Un nouveau
  champ additif non ajouté à `AppendCusum` ne change aucun fingerprint** ; l'ajouter à `AppendCusum`
  **changerait tous les fingerprints de backtest** et invaliderait les constantes golden associées.
- **`Visualization/Dashboards/ScientificDashboard.cs`** lit `cusum.Confidence` en affichage (F3) —
  additif sans impact.
- **`StructuralBreakEvidenceRule`** (post-Lot 16) lit `PositiveCusum`, `NegativeCusum`, `Threshold` —
  additif sans impact.
- **`IQIAIndicator.cs:1172`** teste seulement `Cusum is { IsValid: true }`.
- `CusumResult` est un `record` immuable sans sérialisation JSON dédiée trouvée hors fingerprint.

**Conclusion (analyse seule) :** ajouter un champ à `CusumResult` **est** additif et **ne changerait
pas** les fingerprints déterministes de dataset **si et seulement si** on ne l'ajoute pas à
`AppendCusum`. C'est un point de conception à trancher dans un futur lot d'implémentation, pas ici.

## 19. Consumer Impact

`CusumResult` consommé par : `StructuralBreakEvidenceRule` (Fusion), `ScientificDashboard` (affichage),
`BacktestFingerprint` (hash), `IQIAIndicator.cs` (comptage `IsValid`), `CusumValidation` (tests golden
Python). Un champ additif n'impacte aucun de ces consommateurs tant qu'ils ne le lisent pas. Aucun
consommateur ne serait **contraint** de changer.

## 20. Look-Ahead

Chaque fenêtre CUSUM `series[start .. start+29]` n'utilise que des barres ≤ `start+29`. La réplique
porte la boucle Page-CUSUM verbatim (causale par construction). Q7 (`estBreakIdxStable`) n'utilise que
la fenêtre **précédente**. Aucune statistique globale, aucune donnée future. **Look-ahead : PASS.**

## 21. Determinism

Séries synthétiques générées par LCG 64-bit + Box-Muller, graine fixe 42. Réplique = arithmétique
`double` déterministe (mêmes `CusumMath` que la production). Double agrégation ⇒ hash SHA-256 identique.
**Determinism : PASS** _(hash en §25)_.

## 22. Run Isolation

Aucun état statique mutable ; chaque passe reconstruit ses listes. Deux passes d'agrégation sur les
mêmes observations ⇒ hash identique. **Run isolation : PASS.**

## 23. Yahoo Infrastructure Limitation

Yahoo Finance renvoie **HTTP 429** à cet hôte depuis ~1 h de tests réseau (Lot 16.1). `429` =
**limitation d'infrastructure**, pas échec scientifique (brief §19). Ce lot **n'utilise pas** Yahoo :
l'audit de qualité de mesure est **mieux** servi par des séries synthétiques à propriétés structurelles
**connues** (rupture de moyenne à mi-parcours, rupture de variance à mi-parcours, OU stationnaire sans
rupture, etc.) qu'il permet de tester le comportement de chaque candidat **conditionnellement au
scénario vrai**. Aucune infrastructure Yahoo modifiée.

## 24. Risks

- La réplique doit rester bit-fidèle à la production ; le gate de fidélité échoue le test si ce n'est
  pas le cas (contrairement à `HysteresisThresholdSensitivityLot1418Tests` dont la réplique diverge).
- Conclusions établies sur séries synthétiques : le **comportement qualitatif** des candidats (dépendance
  vs indépendance de la magnitude, dégénérescence) est structurel et se transpose ; les **proportions
  exactes** (taux de détection, % run variance dégénéré) dépendraient d'un dataset réel plus long.
- Corrélations Pearson/Spearman uniquement — une dépendance non linéaire résiduelle n'est pas exclue,
  mais le trace statique §4 montre déjà que les candidats non rejetés partagent des grandeurs sources
  avec la magnitude.

---

## 25. Final Verdict

### **D — CUSUM COMPUTATION LACKS REQUIRED INFORMATION**

Le trace statique (§4–§5) et l'évaluation empirique fidèle (§8–§16) établissent que
`CusumStatistics.Compute` **ne calcule aucune quantité** qui soit à la fois (a) indépendante de la
magnitude de l'événement et (b) interprétable comme une qualité / fiabilité de mesure.

- Les quantités les moins couplées à la magnitude (Q5 `Pearson −0.25`, Q6 `−0.36`) sont
  sémantiquement une **échelle de bruit** et une **forme de changement**, pas une fiabilité — et
  échouent aussi sur le bornage/la granularité.
- Les quantités à sémantique de fiabilité plausible (Q1 propreté directionnelle, Q3 accord des
  détecteurs) sont **des ré-expressions monotones de la magnitude** (`|Pearson| ≈ 0.70`,
  `|Spearman(r)| ≥ 0.73`).
- Q2, bien que bornée, est un **proxy inverse de la magnitude** (`Pearson −0.82`).
- Une vraie statistique de support (p-value CUSUM, compte de confirmation post-détection, dispersion
  bootstrap du pic) **n'est jamais calculée** — classe F.

En conséquence : `CONFIDENCE CONTRACT: UNRESOLVED` (le découplage `Value`/`Confidence` de
`StructuralBreak` reste impossible avec le contrat CUSUM actuel). `CUSUM RESULT EXTENSION: NOT
JUSTIFIED` (aucune quantité existante ne mérite d'être exposée — l'exposer ne ferait que déplacer la
dépendance à la magnitude sous un autre nom).

Un vrai découplage exigerait de **modifier `CusumStatistics.Compute`** pour qu'il calcule une
statistique de support qu'il ne produit pas aujourd'hui. C'est un **redesign**, hors périmètre de ce
lot, et conditionné par un audit séparé de **tous** les consommateurs de `CusumResult` — en
particulier `BacktestFingerprint.AppendCusum` (ajouter le champ au hash changerait **tous** les
fingerprints de backtest).

---

## 26. Recommended Next Lot

**Aucun lot de découplage `Confidence` n'est recommandé à court terme.** Deux options, à ne lancer que
sur décision explicite de l'utilisateur :

1. **« CUSUM Support-Statistic Redesign — Feasibility & Consumer Audit »** (audit/design, pas
   d'implémentation) : étudier l'ajout à `CusumResult` d'une **statistique de support réelle** —
   candidates : (a) p-value asymptotique de la statistique de Page (via la distribution de la somme
   cumulée maximale sous H₀), (b) compte d'observations post-détection restant au-dessus du seuil
   (persistance de confirmation), (c) dispersion permutation/bootstrap du pic sur ré-échantillonnage du
   pré-échantillon. Livrable : faisabilité + impact fingerprint/consommateurs + définition
   mathématique sans paramètre. **Ne pas implémenter dans ce lot.**
2. **Accepter formellement la duplication `Value == Confidence` pour `StructuralBreak`** comme un état
   documenté et sans effet aval (aucune `IDecisionRule` ne consomme la dimension), et **ne pas**
   engager de repondération de `StructuralBreak` tant que (1) n'a pas abouti à une métrique de support
   défendable.

**Interdit sans mandat explicite** : modifier `CusumStatistics.Compute` ; ajouter un champ à
`AppendCusum` ; dériver `Confidence` d'`Agreement`/`BreakCount`/`Threshold`/`SeriesKind` (tous rejetés,
Lots 16.1 & 16.2) ; repondérer `StructuralBreak`.

---

## 27. Handoff Context

```
PROJECT: IQIA — branche feature/structural-stability-v2 (fonctionnalité StructuralBreak = travail non commité)

SIX-DIMENSION FUSION: Stationarity, Persistence, MeanReversion, StructuralStability, RandomWalk,
StructuralBreak. StructuralBreak = évidence additive (Lot 15.8), AUCUN IDecisionRule ne la consomme,
AUCUN reweighting, AUCUNE calibration.

STRUCTURALBREAK PIPELINE:
  CUSUM (+ Bai-Perron diagnostique)
    → StructuralBreakEvidenceRule.BuildContract  (Detected, Strength, BreakCountMagnitude,
      BreakLocationBarIndex, Agreement)
    → StructuralBreakEvidenceRule.Evaluate       (FusionConfidence { Value = Confidence = contract.Strength })
    → FusionDimension.StructuralBreak
    → FusionStateManager  (EMA α=0.20 + hystérésis 0.03, comme toute dimension)
    → sortie Fusion  (non lue par la Decision)

LOT 15.9: Strength (= Cusum.Confidence) sature à 1.0 après détection. Cause = Math.Clamp(r,0,1) DANS
CusumStatistics.Compute (protégé). Reweighting NOT READY.

LOT 16: Strength = r/(1+r), r = max(PositiveCusum,|NegativeCusum|)/Threshold, reconstruit dans
StructuralBreakEvidenceRule (CusumStatistics NON touché). Saturation 100%→0%, variance 0→0.0149,
>10000 valeurs distinctes, monotonie/déterminisme/look-ahead/run-isolation PASS, 5 autres dimensions +
Decision.Winner bit-identiques. Strength est un SIGNAL DE MAGNITUDE informatif.

LOT 16.1: Evaluate pose toujours Value = Confidence = contract.Strength EXACTEMENT.
corr(Value, Confidence) = 1.000 (RAW + STABLE, 11428/11428 bit-identiques). 5 candidats de découplage —
aucun viable. Cause: le contrat CUSUM n'expose pas de métrique de qualité indépendante de la magnitude.
CONFIDENCE CONTRACT: UNRESOLVED.

LOT 16.2 (CE LOT): audit du calcul CUSUM INTERNE. Trace statique complète + réplique fidèle bit-à-bit
(0 mismatch / 21339 fenêtres) + 7 candidats dérivés (Q1..Q7) testés sur 9 familles de séries
synthétiques déterministes. RÉSULTAT: VERDICT D — le calcul CUSUM actuel ne produit AUCUNE quantité à
la fois indépendante de la magnitude ET interprétable comme fiabilité de mesure. Q1/Q3 = ré-expressions
monotones de la magnitude (|Pearson| ~0.70) ; Q2 = proxy inverse de la magnitude (Pearson -0.82) ;
Q5/Q6 = échelle de bruit / forme du changement (sémantique invalide, non bornées) ; Q4 = étiquette de
nature ; Q7 déjà rejeté 16.1 ; une vraie statistique de support (p-value CUSUM / compte de confirmation
/ bootstrap) n'est JAMAIS calculée (classe F). CONFIDENCE CONTRACT: UNRESOLVED. CUSUM RESULT EXTENSION:
NOT JUSTIFIED. PRODUCTION MODIFIED: NO.

PRODUCTION PROTÉGÉE (inchangée): CusumStatistics, CusumResult, CusumEvidence, CusumMath,
FusionStateManager, StructuralBreakEvidenceRule, DecisionEngine + toutes IDecisionRule, SignalEngine,
EntryTriggerBuilder, EntryTriggerEngine, TradePlanBuilder, RiskEngine, RiskPolicy, ExecutionSimulator,
BacktestFingerprint.

CE QUI NE DOIT PAS ÊTRE CALIBRÉ / FAIT: aucun poids Fusion, aucune consommation de StructuralBreak par
la Decision, aucune modification de CusumStatistics.Compute sans audit autorisé de TOUS ses
consommateurs (dont le fingerprint backtest), aucun ajout de champ à AppendCusum (changerait tous les
fingerprints).

PROCHAINE ACTION: soit un lot d'audit/design « CUSUM Support-Statistic Redesign — Feasibility &
Consumer Audit » (p-value de Page / compte de confirmation post-détection / dispersion bootstrap ;
impact fingerprint) — NE PAS implémenter ; soit accepter formellement la duplication Value==Confidence
comme état documenté sans effet aval. Aucune repondération de StructuralBreak tant qu'une métrique de
support défendable n'existe pas.

YAHOO: rate-limité (HTTP 429) — audit synthétique-only. Ne pas marteler Yahoo ; ne pas lancer la suite
réseau complète en une passe.
```

---

## FINAL OUTPUT

```
STATUS:
AUDITED

CUSUM INTERNAL INVENTORY:
PASS  (20 quantities traced from source; full dataflow §4, inventory table §5)

CANDIDATES EVALUATED:
7  (Q1..Q7) + calibrationSize / SampleSize / IsValid (constant, class D) + a real support
   statistic (p-value / confirmation count / bootstrap dispersion — class F, never computed)

INDEPENDENT QUALITY METRIC:
NOT FOUND

BEST CANDIDATE:
NONE
  (Q5 relativeThreshold has the lowest magnitude coupling, Pearson -0.25, but is an unbounded
   price-dependent noise-scale quantity, not a measurement-reliability metric — rejected class E)

MAGNITUDE INDEPENDENCE:
FAIL
  (every semantically-plausible candidate is a monotone re-expression of magnitude:
   Q1 Pearson +0.70 / Spearman(r) +0.87 ; Q2 -0.82 / -0.89 ; Q3 -0.71 / -0.73)

SCIENTIFIC SEMANTICS:
INVALID
  (the low-coupling candidates Q5/Q6 are noise-scale / change-shape, not reliability;
   Q4 is a change-type label; Q7 is a re-purposed diagnostic-only field)

SATURATION:
NOT APPLICABLE
  (no metric implemented; Q3 does not saturate on synthetic data but degenerates on real
   near-constant windows, and fails independence regardless)

TEMPORAL INDEPENDENCE:
FAIL
  (Q1/Q2/Q3/Q5 transition ~93-100% of windows — fast co-variation with magnitude, not a
   distinct slow reliability signal)

CONFIDENCE CONTRACT:
UNRESOLVED

CUSUM RESULT EXTENSION:
NOT JUSTIFIED

PRODUCTION MODIFIED:
NO

CALIBRATION:
NOT EXECUTED

REWEIGHTING:
NOT EXECUTED

OPTIMIZATION:
NOT EXECUTED

FUSION:
NOT MODIFIED

DECISION:
NOT MODIFIED

SIGNAL:
NOT MODIFIED

ENTRY:
NOT MODIFIED

RISK:
NOT MODIFIED

STOP LOSS:
NOT MODIFIED

EXECUTION:
NOT MODIFIED

YAHOO:
NOT USED (rate limited)

LOOK-AHEAD:
PASS  (each 30-bar CUSUM window uses only bars <= its end; Q7 uses only the previous window;
       Page-CUSUM loop ported verbatim, causal by construction)

DETERMINISM:
PASS  (seeded LCG synthetic series; double aggregation SHA-256 identical:
       015ae19516229c25426afcc49954f0f49bffdf05f74f87f672317e0e557b9fb1)

RUN ISOLATION:
PASS  (no mutable static state; two aggregation passes over the same observations -> identical hash)

ORDERS:
NONE

DLL DEPLOYED:
NO

COMMIT:
NO

TESTS:
  Lot162CusumInternalQualityAuditTests.Audit_Lot162_CusumInternalQuality_ReplicaFidelity_And_CandidateIndependence
  — PASS (1/1, ~8s, network-free).
  Replica fidelity gate: 0 mismatches / 21339 windows (bit-for-bit vs CusumValidation.RunOnSeries).
  9 synthetic series families, 21339 valid windows, VarianceRunDegenerate 0%, DetectedPct 56.8%.
  Production unchanged -> no regression suite required; the test assembly builds and discovers cleanly
  (necessary condition of this run).

DOCUMENTATION:
Documentation/Scientific/QDE-012_Sprint_15.25_Lot16.2_CUSUM_Measurement_Quality_Metric_Exposure_Audit_Report.md

FINAL VERDICT:
D  (CUSUM COMPUTATION LACKS REQUIRED INFORMATION)

NEXT LOT:
  Only on explicit user decision — either:
  (1) "CUSUM Support-Statistic Redesign — Feasibility & Consumer Audit" (audit/design only:
      Page-statistic p-value / post-detection confirmation count / bootstrap peak dispersion;
      fingerprint & consumer impact) — DO NOT IMPLEMENT; or
  (2) formally accept the Value==Confidence duplication for StructuralBreak as a documented,
      downstream-inert state.
  No StructuralBreak reweighting until a defensible support metric exists.
```

**STOP.**
