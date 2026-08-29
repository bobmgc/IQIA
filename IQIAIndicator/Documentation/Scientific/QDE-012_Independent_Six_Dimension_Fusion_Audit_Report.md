# QDE-012 — Audit scientifique indépendant des SIX dimensions de Fusion

> **Type :** Independent Scientific Audit — **Mode :** READ ONLY
> **Production modifiée :** NON — **Calibration / Optimisation / Repondération :** NON
> **ATAS :** non utilisé — **Ordres :** aucun — **DLL déployée :** non — **Commit :** non
>
> Ce document est une **baseline scientifique indépendante**. Ses conclusions ne doivent
> influencer la roadmap, les poids, `FusionStateManager`, `RegimeEngine`, `DecisionEngine`,
> l'entrée, le risque ou le stop-loss **que sur décision explicite de l'utilisateur**.
> Aucune recommandation d'un lot antérieur n'a été appliquée ; l'identité et le verdict de
> chaque dimension sont dérivés uniquement de l'état réel du code et des données mesurées.

---

## 0. Méthode

1. **Lecture du code de production** (aucune modification) :
   `Engine/Fusion/Core/FusionDimension.cs`, les 6 règles de `Engine/Fusion/Rules/`,
   `EvidenceFusionEngine`, `FusionResultBuilder`, `FusionStateManager`,
   `FusionProfileAnalyzer`, les 5 règles de `Engine/Decision/Rules/`, `DecisionEngine`,
   le câblage dans `IQIAIndicator.cs`, et les producteurs d'évidence
   (`Engine/Regime/Evidence/**`).
2. **Données empiriques** : sorties CSV déjà produites par le pipeline de production
   **non modifié** sur le dataset réel Yahoo `MES=F` M5 (~59 jours), présentes dans le
   dépôt :
   - `Tests/Research/RegimeCoverageAudit/Output/regime_evidence_stats.csv`
   - `Tests/Research/RegimeCoverageAudit/Output/regime_fusion_raw_vs_stable.csv`
   - `Tests/Research/RegimeCoverageAudit/Output/regime_decision_stats.csv`
   - `Tests/Research/RegimeCoverageAudit/Output/structuralbreak_evidence_ablation_lot152.csv`
   - `Tests/Research/StructuralBreakAudit/Output/structuralbreak_evidence_lot158.csv`
   - `Tests/Research/StructuralBreakAudit/Output/*.csv`
3. **Test additif isolé** (observation only, ne modifie aucune production) :
   `Tests/Research/SixDimensionAudit/SixDimensionIndependentAuditTests.cs` — rejoue le
   pipeline de signal non modifié, reconstruit l'état brut et l'état stabilisé de la
   fusion barre par barre avec une liste de règles **équivalente à la production** (mêmes
   règles, même ordre), et mesure : distributions `Value`/`Confidence` par dimension,
   taux de saturation (`==0`, `==1`, bande `[0.45,0.55]`), `corr(Value, Confidence)`,
   gel temporel brut→stabilisé, et la matrice 6×6 de corrélation inter-dimensions.
   Sorties : `Tests/Research/SixDimensionAudit/Output/`.

---

## 1. Les SIX dimensions — état réel du code

`Engine/Fusion/Core/FusionDimension.cs` déclare exactement, **dans cet ordre** :

| # | `FusionDimension` | Producteur (règle) | Entrées brutes | Consommé par la Decision ? |
|---|---|---|---|---|
| 1 | `Stationarity` | `StationarityRule` | `Evidence.Adf` + `Evidence.Kpss` | **Oui** (4 règles) |
| 2 | `Persistence` | `PersistenceRule` | `Evidence.Dfa` + `Evidence.VarianceRatio` | **Oui** (4 règles) |
| 3 | `MeanReversion` | `MeanReversionRule` | `Evidence.HalfLife` | **Oui** (4 règles) |
| 4 | `StructuralStability` | `StructuralStabilityRule` **via** `FusionStateManager` | `FusionProfileAnalysis` (profil temporel des dimensions stabilisées) — **aucune évidence statistique directe** | **Oui** (4 règles) |
| 5 | `RandomWalk` | `RandomWalkRule` | `Evidence.VarianceRatio` | **Oui** (2 règles) |
| 6 | `StructuralBreak` | `StructuralBreakEvidenceRule` | `Evidence.Cusum` (+ `Evidence.BaiPerron` diagnostique) | **NON — zéro consommateur** |

Observations structurelles immédiates :

- **`EvidenceFusionEngine` de production** (`Engine/Fusion/EvidenceFusionEngine.cs`) est
  construit dans `IQIAIndicator.cs` avec **5 règles** : `StationarityRule`,
  `PersistenceRule`, `MeanReversionRule`, `RandomWalkRule`, `StructuralBreakEvidenceRule`.
  `StructuralStabilityRule` **n'est pas** dans cette liste : elle est invoquée séparément
  dans `FusionStateManager.Update`, qui **remplace** la dimension `StructuralStability` du
  résultat stabilisé par `StructuralStabilityRule.EvaluateAnalysis(analysis)`.
- Il existe un second type `Engine/Regime/Core/EvidenceFusionEngine.cs` : c'est un **stub
  mort** (« Fusion non implémentée — Sprint 2.4 »), non référencé par le pipeline. Hors
  périmètre.
- `FusionStateManager.Dimensions` liste les **6** dimensions et applique le même chemin
  EMA + hystérésis à 5 d'entre elles (`StructuralStability` est traitée à part :
  `newConfidence = previousConfidence` puis écrasée).
- `FusionProfileAnalyzer.Dimensions` ne liste que **5** dimensions
  (`Stationarity, Persistence, MeanReversion, StructuralStability, RandomWalk`) :
  `StructuralBreak` est **délibérément exclu** du calcul de `StructuralStability`.

---

## 2. Chaîne complète par dimension (SOURCE → EFFET OBSERVABLE)

### D1 — `Stationarity`

```
SOURCE      Adf (ADF p-value, IsStationary, Confidence=n/Window, SampleSize)
            Kpss (KPSS p-value, IsStationary, Confidence=n/Window, SampleSize)
CALCUL      EvidenceStrength(p) = logistique lissée autour de p=0.05 (pente 50)
            adfDir = ±strength selon IsStationary ; idem kpss
            scientificScore = clamp(0.5 + 0.25*(adfDir + kpssDir), 0, 1)
            confidenceQuality = 0.5*(clamp(adf.Conf) + clamp(kpss.Conf))
            sampleSizeQuality = 0.5*(1-e^(-n/60) ...)
            qualityScore = 0.70*confidenceQuality + 0.30*sampleSizeQuality
NORMALISATION  bornée [0,1] par construction (clamp explicite)
VALUE       = scientificScore            (PAS le Blend : le Blend est calculé puis jeté)
CONFIDENCE  = qualityScore
FSM         EMA alpha=0.20 + hystérésis 0.03 sur Value et Confidence séparément
STABILIZED  Value/Confidence lissées, figées si |Δ| < 0.03
DECISION    StableRangeRule (poids sci 0.40), TrendingRule (terme 1-Value, 0.20),
            MeanRevertingRule (0.30), StructuralBreakRule (terme 1-Value, 0.10)
EFFET       influence le FinalScore de 4 des 5 règles de régime → arbitrage → Winner
```

### D2 — `Persistence`

```
SOURCE      Dfa (Hurst∈[0,2], RSquared, Confidence = R²·min(1,valid/6)·min(1,n/Window),
                 WindowCount)
            VarianceRatio (VR, ZStat, PValue, Confidence = 1 - pValue, SampleSize)
CALCUL      dfaDir = tanh(6·(Hurst-0.5)) ; vrDir = tanh(2·ln(VR))
            PersistenceSupport(x) = x²·sigmoid(4x)         ← terme QUADRATIQUE
            dfaPersistence = PersistenceSupport(dfaDir)
            vrPersistence  = PValueStrength(VR.PValue)·PersistenceSupport(vrDir)
            scientificScore = clamp(0.5·(dfaPersistence + vrPersistence), 0, 1)
            qualityScore = 0.5·(dfaQuality + vrQuality)   (poids sci 0.90 / qual 0.10 dans le Blend jeté)
VALUE       = scientificScore
CONFIDENCE  = qualityScore
FSM         EMA + hystérésis identiques
DECISION    TrendingRule (poids 0.50), MeanRevertingRule (terme 1-Value, 0.20),
            StructuralBreakRule (terme 1-Value, 0.30), RandomWalkRule (terme 1-Value, 0.20)
EFFET       influence le FinalScore des 5 règles de régime
```

### D3 — `MeanReversion`

```
SOURCE      HalfLife (HalfLife≥0, RSquared, Confidence = RSquared, SampleSize)
CALCUL      scientificScore = exp(-max(0,HalfLife)/10)        ← décroissance exponentielle
            qualityScore = (clamp(Conf) + clamp(R²) + (1-e^(-n/50)))/3
VALUE       = scientificScore
CONFIDENCE  = qualityScore
DISPONIBILITÉ  la seule dimension avec un taux d'indisponibilité non négligeable :
            HalfLife.IsValid false ⇒ « Missing Evidence » (IsAvailable=false, Value=0)
            PercentValid mesuré : 88.0 % (Trending) … 99.1 % (StableRange)
FSM         EMA + hystérésis
DECISION    StableRangeRule (0.40), MeanRevertingRule (0.40), StructuralBreakRule
            (terme 1-Value, 0.20), RandomWalkRule (terme 1-Value, 0.20)
EFFET       influence le FinalScore des 5 règles de régime
```

### D4 — `StructuralStability`

```
SOURCE      AUCUNE évidence statistique. Entrée = FusionProfileAnalysis calculée par
            FusionProfileAnalyzer sur une fenêtre glissante de 6 snapshots STABILISÉS
            des 5 dimensions (Stationarity, Persistence, MeanReversion,
            StructuralStability elle-même, RandomWalk).
CALCUL      ProfileVelocity  = distance L1 moyenne inter-snapshots / 5   (clamp [0,1])
            ProfileStability = 1 - MAD(valeurs)/0.5                      (clamp [0,1])
            BehaviourConsistency = 0.65·ProfileStability + 0.35·(1-ProfileVelocity)
            warm-up (SnapshotCount<2) ⇒ Value=1.0, Confidence=1.0
VALUE       = clamp(BehaviourConsistency, 0, 1)
CONFIDENCE  = clamp(0.5·ProfileStability + 0.5·(1-ProfileVelocity), 0, 1)
FSM         chemin EMA/hystérésis SAUTÉ pour cette dimension ; la valeur du snapshot
            précédent est portée puis ÉCRASÉE par EvaluateAnalysis(analysis) via
            ReplaceStructuralStability (donc pas d'hystérésis propre)
DECISION    StableRangeRule (0.20), TrendingRule (0.30), MeanRevertingRule (0.10),
            StructuralBreakRule (terme 1-Value, 0.40)
EFFET       influence le FinalScore des 4 règles de régime qui la lisent
PARTICULARITÉ  auto-référentielle (entre dans sa propre fenêtre de profil) et
            entièrement DÉRIVÉE des 4 autres dimensions consommées.
```

### D5 — `RandomWalk`

```
SOURCE      VarianceRatio (VR, ZStat, PValue, Confidence = 1 - pValue, SampleSize)
CALCUL      ratioCompat = exp(-|ln(VR)| / 0.25)
            statCompat  = exp(-|Z| / 2)
            pValueCompat = clamp(VR.PValue, 0, 1)
            scientificScore = ratioCompat · statCompat · pValueCompat   ← PRODUIT de 3 termes [0,1]
            qualityScore = 0.5·(clamp(VR.Conf) + (1-e^(-n/50)))
VALUE       = scientificScore   (biais structurel vers 0 : produit de facteurs < 1)
CONFIDENCE  = qualityScore
FSM         EMA + hystérésis
DECISION    RandomWalkRule (poids 0.60 sur Value)
EFFET       influence UNIQUEMENT le FinalScore de RandomWalkRule
SOURCE PARTAGÉE  VarianceRatio alimente AUSSI Persistence (D2) : co-dépendance structurelle.
```

### D6 — `StructuralBreak`

```
SOURCE      Cusum (ChangeDetected, Confidence = clamp(peakMagnitude / threshold, 0, 1),
                   EstimatedBreakIndex, SampleSize, IsValid)
            BaiPerron (BreakCount) — DIAGNOSTIQUE UNIQUEMENT
CALCUL      contract.Strength = clamp(Cusum.Confidence, 0, 1)
            si ChangeDetected ⇒ peakMagnitude > threshold ⇒ Strength SATURE à 1.0
VALUE       = contract.Strength
CONFIDENCE  = contract.Strength         ← IDENTIQUE à Value par construction
NORMALISATION  aucune transformation supplémentaire ; « 0 breaks » et « Bai-Perron
            indisponible » ne sont volontairement pas distingués dans Value
FSM         EMA + hystérésis (même chemin que D1/D2/D3/D5)
DECISION    AUCUNE. Aucune IDecisionRule ne lit FusionDimension.StructuralBreak.
            Le dashboard (DecisionDashboard.Dimensions) ne l'affiche pas non plus.
EFFET       NO_OBSERVED_DOWNSTREAM_EFFECT — produit et stabilisé, jamais consommé.
```

---

## 3. Réponses aux questions scientifiques (A / B / C / D)

### A — Existence réelle

Toutes les 6 dimensions sont **réellement calculées à chaque barre prête** (aucune n'est
un simple placeholder permanent). Nuances mesurées (dataset ~11 400 barres analysées) :

| Dimension | Réellement calculée | Fallback « Missing Evidence » | Remarque |
|---|---|---|---|
| Stationarity | oui, 100 % | ~0 % après warm-up | ADF & KPSS `PercentValid` = 100 % |
| Persistence | oui, 100 % | ~0 % après warm-up | DFA & VR `PercentValid` = 100 % |
| MeanReversion | oui | **1 – 12 %** selon régime | seul cas notable d'indisponibilité (HalfLife invalide) |
| StructuralStability | oui, mais **reconstruite** à partir des autres dimensions | n/a | valeur par défaut 1.0 pendant le warm-up profil |
| RandomWalk | oui, 100 % | ~0 % | VR `PercentValid` = 100 % |
| StructuralBreak | oui, `IsAvailable` = **99.84 %** | 0.16 % (CUSUM invalide) | `Detected` = 93.1 % des barres |

### B — Variabilité (dimension calculée ≠ dimension informative)

Écarts-types **bruts** puis **stabilisés** de `Value`, mesurés globalement sur les 11 428
barres prêtes (`sixdim_distribution.csv`, run d'audit dédié) :

| Dimension | RawStdDev | StableStdDev | Distinct (RAW) | RunMax identique (STABLE) | Lecture |
|---|---|---|---|---|---|
| Stationarity | **0.203** | 0.148 | 10 752 | 182 | **la plus variable** des 5 « vivantes » ; couverture [0.12, 0.91] |
| Persistence | 0.140 | 0.092 | 11 420 | **924** | brut varie ; **stable quasi-figé** (plateau jusqu'à 924 barres) |
| MeanReversion | 0.250 | 0.183 | 10 925 | 109 | forte variabilité brute (exp(−HL/10) très sensible) |
| StructuralStability | **pas de contrepartie brute** | **0.043** | 1 (absente) | 42 | **variabilité effective très faible** ; bande [0.53, 0.81] autour de 0.65 |
| RandomWalk | 0.255 | 0.146 | 11 425 | 119 | variabilité brute élevée mais **médiane 0.064** (niveau moyen bas) |
| StructuralBreak | 0.086 | 0.026 | 756 | 260 | `Value == 1.0` sur **93.3 %** des barres ; ~6.7 % non-détectées portent [0.22, 1) |

> **`ZERO VARIANCE` explicite : `StructuralBreak.Value` conditionnellement à `Detected`.**
> `structuralbreak_evidence_lot158.csv` → `STRENGTH_WHEN_DETECTED` : N=10 758,
> Mean=Median=Min=Max=**1.0**. Le run d'audit dédié confirme : `%Value==1 = 93.3 %`,
> `DistinctCount` chute de ~11 000 (autres dimensions) à **756**.

### C — Normalisation

| Dimension | Plage contrôlée | Saturation | Division instable | Constante non documentée | Granularité |
|---|---|---|---|---|---|
| Stationarity | oui (`clamp` explicite) | modérée aux bornes via logistique pente 50 (transition quasi-binaire autour de p=0.05) | non (lissage `ThresholdSmoothingRadius`) | pentes 50 / 0.80-0.20 / 0.70-0.30 « provisoires » | **réduite** par la logistique raide : p-values loin de 0.05 ⇒ ±1 |
| Persistence | oui (`clamp`) | vers **0** : terme `x²·sigmoid` écrase les faibles directions | `ln(max(VR,1e-12))` protégé | pentes 6 / 2 / 4, `HurstRandomWalkLevel`=0.5 | **compressée vers le bas** ; peu de masse en zone haute |
| MeanReversion | `exp(-HL/10)` ∈ (0,1] borné | vers **1** si HL→0, vers **0** si HL grand (outliers HL jusqu'à 12 401) | non | `HalfLifeDecayScale`=10, `SampleSizeScale`=50 | correcte sur HL∈[0,~30], **effondrée** au-delà |
| StructuralStability | `clamp` sur `BehaviourConsistency` | vers **1** (défaut warm-up + profil lent) | `MAD/0.5` : le diviseur 0.5 est une constante **non documentée** | 0.65/0.35, 0.50/0.50, fenêtre 6, diviseur 0.5 | **détruite** : bande étroite proche de 1 |
| RandomWalk | produit de 3 termes ∈ [0,1] | vers **0** (produit) | `ln(max(VR,1e-12))` protégé | échelles 0.25 / 2 / 50 | **fortement compressée vers 0** |
| StructuralBreak | `clamp(peak/threshold)` | vers **1** : dès `Detected`, `peak > threshold` ⇒ clamp à 1.0 | `peak / threshold` (threshold>0 garanti par `IsValid`) | seuil CUSUM `σ·√(2n·ln n)` (dérivé analytiquement, documenté dans le code) | **détruite quand Detected** (constante 1.0) ; utile uniquement sur la fraction non-détectée |

**Taux de saturation mesurés** (`sixdim_distribution.csv`, `Value` RAW) :

| Dimension | % `Value == 0` | % `Value == 1` | % `Value ∈ [0.45,0.55]` | Médiane | Lecture |
|---|---|---|---|---|---|
| Stationarity | 0 | 0 | 29.9 % | 0.385 | pas de saturation aux bornes ; concentration modérée au centre |
| Persistence | 0 | 0 | 1.9 % | **0.015** | **masse écrasée juste au-dessus de 0** |
| MeanReversion | **4.37 %** | 0 | 9.8 % | 0.651 | les 4.37 % à 0 = sentinelle « Missing Evidence » |
| RandomWalk | 0.02 % | 0 | 3.9 % | 0.064 | **masse concentrée près de 0** (produit de 3 facteurs < 1) |
| StructuralStability (stable) | 0 | 0 | 0.3 % | 0.649 | bande étroite (StdDev 0.043) proche de 0.65 |
| StructuralBreak | 0 | **93.3 %** | 0.8 % | **1.000** | **`SATURATED` à 1.0** |

`RandomWalk` n'atteint pas exactement `0` mais sa **médiane 0.064** et son mode près de 0
constituent une saturation basse effective. `Persistence` : médiane `0.015`.

### D — Confidence (auditée séparément de Value)

| Dimension | Ce que `Confidence` représente réellement | Catégorie |
|---|---|---|
| Stationarity | `0.70·qualité(conf. ADF/KPSS) + 0.30·qualité(taille échantillon)` ; les conf. ADF/KPSS sont `n/Window` ⇒ **constantes ≈ 1.0 après warm-up** ⇒ `Confidence` ≈ constante | `CONFIDENCE_MOSTLY_CONSTANT` (après warm-up) |
| Persistence | `0.5·(dfaQuality + vrQuality)` ; `dfaQuality` mêle R², couverture, taille ; `vrQuality` mêle `1-pValue` et taille | partiellement informative ; **partage `pValue` avec `Value`** ⇒ corrélation non nulle attendue |
| MeanReversion | `(conf. HalfLife=R² + R² + taille)/3` ⇒ dominée par le R² de la régression demi-vie | informative (qualité d'ajustement) ; peut co-varier avec `Value` |
| StructuralStability | `0.5·ProfileStability + 0.5·(1-ProfileVelocity)` ⇒ fonction des **mêmes** grandeurs de profil que `Value` (`BehaviourConsistency` = `0.65·ProfileStability + 0.35·(1-ProfileVelocity)`) | **`CONFIDENCE_DUPLICATES_VALUE`** (quasi-colinéaire par construction) |
| RandomWalk | `0.5·(conf. VR + taille)` | informative mais faible amplitude |
| StructuralBreak | `= Value` (littéralement `contract.Strength` pour les deux champs) | **`CONFIDENCE_DUPLICATES_VALUE` (exact)** + **`CONFIDENCE_SATURATED`** à 1.0 quand `Detected` |

`corr(Value, Confidence)` **mesuré** (RAW, §7.1) :

| Dimension | corr(V,C) RAW | Interprétation |
|---|---|---|
| Stationarity | **−0.018** | indépendants (mais `Confidence` quasi-constante ⇒ corr peu signifiante) |
| Persistence | +0.211 | faible couplage positif |
| MeanReversion | **+0.822** | **Value et Confidence co-varient fortement** (les deux montent quand HL est court + bien ajusté) |
| StructuralStability | **+0.999** (stable) | **`CONFIDENCE_DUPLICATES_VALUE`** — colinéarité de définition |
| RandomWalk | **−0.898** | **anti-corrélation forte** : `Value` contient `VR.PValue` en facteur, `Confidence = 1 − VR.PValue` ⇒ couplage inverse quasi mécanique |
| StructuralBreak | **+1.000** (exact) | **`CONFIDENCE_DUPLICATES_VALUE` exact** + `CONFIDENCE_SATURATED` |

⇒ Le canal `Confidence` ne transporte une information **indépendante de `Value`** que
pour `Persistence` (faiblement) et, en partie, pour `Stationarity` en phase transitoire.
Pour `MeanReversion` il est redondant (positivement), pour `RandomWalk` il est
contradictoire (négativement), pour `StructuralStability` et `StructuralBreak` il est une
copie.

---

## 4. Stabilisation temporelle (avant / après `FusionStateManager`)

`FusionStateManager` applique, à D1/D2/D3/D5 : `EMA(alpha=0.20)` puis **hystérésis** :
la nouvelle valeur n'est retenue que si `|smoothed − previous| ≥ 0.03`, sinon la valeur
précédente est **rejouée à l'identique** (gel). `Alpha` et `HysteresisThreshold` sont
marqués `Provisional` dans le code (non calibrés).

Mesures globales (`sixdim_raw_vs_stable_freeze.csv`, `PctRawChangedStableFrozen` =
% de paires de barres où le brut change mais le stabilisé reste figé) :

| Dimension | % (brut change / stable figé) | Plateau figé moyen / max (barres) | Verdict |
|---|---|---|---|
| Stationarity | **80.4 %** | 5.7 / 182 | `PARTIALLY_DAMPED` |
| Persistence | **94.9 %** | **19.6 / 924** | `HEAVILY_DAMPED` (≈ `FROZEN`) |
| MeanReversion | 68.2 % | 3.3 / 109 | `PARTIALLY_DAMPED` |
| RandomWalk | 66.9 % | 3.0 / 119 | `PARTIALLY_DAMPED` |
| StructuralStability | 0 % (pas de brut) ; `RawFrozenStableChanged` = 10 553 | 1.1 / 42 | `FROZEN` **intentionnel** (mesure de constance) |
| StructuralBreak | **4.8 %** ; `BothFrozen` = 10 103 (88 %) | 14.7 / 260 | `SATURATED` (à 1.0), pas `FROZEN` par le FSM : l'entrée brute est déjà constante |

Plage par régime (`regime_fusion_raw_vs_stable.csv`) cohérente : Persistence 73–98 %,
Stationarity 48–86 %, MeanReversion 53–79 %, RandomWalk 42–78 %.

**`TEMPORAL_FREEZE`** avéré et **non pathologique** : `StructuralStability` (par
définition). **`TEMPORAL_FREEZE` avéré et potentiellement destructeur d'information** :
`Persistence` — l'hystérésis 0.03 dépasse souvent l'amplitude brute de la dimension
(RawStdDev ≈ 0.05–0.09 sur les régimes dominants), donc l'essentiel du signal brut de
`Persistence` n'atteint jamais l'aval.

---

## 5. Hysteresis sensitivity — observation only

`Raw Information → State Stabilization → Information Preserved ?`

| Dimension | Verdict |
|---|---|
| D1 `Stationarity` | `PARTIALLY_DAMPED` — signal directionnel préservé, micro-variations coupées |
| D2 `Persistence` | `HEAVILY_DAMPED` — amplitude brute < seuil d'hystérésis sur les régimes dominants |
| D3 `MeanReversion` | `PARTIALLY_DAMPED` — préservé sur HL modérés, non pertinent sur HL extrêmes |
| D4 `StructuralStability` | `FROZEN` **by design** — mesure de constance, faible amplitude attendue |
| D5 `RandomWalk` | `PARTIALLY_DAMPED` + `SATURATED_LOW` — niveau moyen proche de 0 |
| D6 `StructuralBreak` | `SATURATED` — l'EMA/hystérésis n'a presque rien à faire : l'entrée vaut déjà 1.0 en permanence |

Aucun paramètre modifié. `alpha=0.20` et `hysteresis=0.03` restent tels quels.

---

## 6. Redondance entre dimensions

### Dépendances de source (déduites du code, indiscutables)

- **`VarianceRatio` alimente `Persistence` ET `RandomWalk`** ⇒ `SHARED_SOURCE`.
- **`StructuralStability` est calculée à partir des valeurs stabilisées de
  `Stationarity`, `Persistence`, `MeanReversion`, `RandomWalk` (et d'elle-même)** ⇒
  `DERIVED_FROM_OTHER_DIMENSION`.
- `StructuralBreak` (CUSUM) et `StructuralStability` visent conceptuellement le même
  phénomène (rupture / instabilité de régime) par deux chemins totalement disjoints.

### Matrice conceptuelle (source partagée / dérivation)

```
                 D1 Stat  D2 Pers  D3 MR   D4 SS   D5 RW   D6 SB
D1 Stationarity   —       indép.   indép.  DÉRIVE  indép.  indép.
D2 Persistence    indép.   —       indép.  DÉRIVE  SHARED  indép.
D3 MeanReversion  indép.   indép.   —      DÉRIVE  indép.  indép.
D4 StructStab     entrée   entrée   entrée  —      entrée  EXCLU
D5 RandomWalk     indép.   SHARED   indép.  DÉRIVE  —      indép.
D6 StructBreak    indép.   indép.   indép.  (même but, chemin disjoint)  —
```

- `SHARED_SOURCE` (structurel) : (D2, D5) via `VarianceRatio` — **mais** corrélation
  numérique mesurée quasi nulle (`−0.067` brut, `−0.044` stable). Pas de `HIGH_REDUNDANCY`
  empirique : les transformations `PersistenceSupport(·)` et `ratioCompat·statCompat·pValueCompat`
  sont trop différentes pour produire de la colinéarité.
- `DERIVED_FROM_OTHER_DIMENSION` : D4 ← {D1, D2, D3, D5, D4}. Confirmé numériquement :
  D4(stable) corrèle `−0.474 / +0.465 / +0.342 / −0.163` avec D3/D5/D2/D1.
- `INDEPENDENT` (sources disjointes **et** `|r| ≤ 0.24`) : toutes les autres paires de
  dimensions « vivantes ».
- **Matrice Pearson 6×6 complète** (brut + stabilisé) : §7.3.

> Bilan redondance : la seule redondance **prouvée** est celle de D4 `StructuralStability`
> (dérivée par construction des autres). La source partagée D2/D5 n'engendre pas de
> redondance numérique observable, mais reste une co-dépendance d'erreur potentielle (un
> `VarianceRatio` aberrant biaise deux dimensions traitées comme indépendantes par
> l'arbitrage).

---

## 7. Mesures du test additif `SixDimensionIndependentAudit`

Exécution réussie. Dataset : `MES` M5, `BarCount=11556`,
`Range 2026-06-29T11:10:00Z .. 2026-08-27T11:01:21Z`,
`Fingerprint=F2678EA44DDEBDBC05437FCFF4D7A5EC3559221699C34C34F06986C1E5F2E172`,
`ReadyBars=11428`, `WarmupBars=128`. **Déterminisme confirmé** :
`Hash1 == Hash2 == fdd4260a461edddaa1cd2278e979623279ded2a9864ba376e15c0d3bdb2b1c42`.
Durée ≈ 3 min 14 s. Sorties dans `Tests/Research/SixDimensionAudit/Output/`.

> Note méthodologique : le moteur de reconstruction du test contient les **5 règles
> basées évidence** (`Stationarity, Persistence, MeanReversion, RandomWalk,
> StructuralBreak`). `StructuralStability` n'ayant **pas** de contrepartie « brute »
> (elle n'est injectée que dans `FusionStateManager`), ses colonnes `RAW` valent 0 /
> `AvailableN=0` — c'est attendu et non un défaut ; seule sa colonne `STABLE` est
> significative.

### 7.1 Distribution `Value` / `Confidence` — `sixdim_distribution.csv`

`Value` :

| Dimension | Stage | AvailN/N | Min | Max | Mean | Median | StdDev | Distinct | RunMax | %=0 | %=1 | %∈[0.45,0.55] | corr(V,C) |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Stationarity | RAW | 11428/11428 | 0.119 | 0.913 | 0.389 | 0.385 | **0.203** | 10752 | 17 | 0 | 0 | 29.9 % | −0.018 |
| Stationarity | STABLE | 11428 | 0.247 | 0.793 | 0.424 | 0.377 | 0.148 | 1997 | 182 | 0 | 0 | 10.7 % | ~0 |
| Persistence | RAW | 11428/11428 | 0.000 | 0.901 | **0.078** | **0.015** | 0.140 | 11420 | 1 | 0 | 0 | 1.9 % | 0.211 |
| Persistence | STABLE | 11428 | 0.047 | 0.746 | 0.165 | 0.136 | 0.092 | 584 | **924** | 0 | 0 | 1.3 % | 0.168 |
| MeanReversion | RAW | **10970**/11428 | 0.000 | 0.938 | 0.581 | 0.651 | **0.250** | 10925 | 17 | **4.37 %** | 0 | 9.8 % | **0.822** |
| MeanReversion | STABLE | 11428 | 0.120 | 0.800 | 0.555 | 0.607 | 0.183 | 3503 | 109 | 0 | 0 | 14.4 % | 0.564 |
| StructuralStability | RAW | 0/11428 | — | — | — | — | 0 | 1 | 11428 | 100 % | 0 | 0 | (n/a) |
| StructuralStability | STABLE | 11428 | 0.527 | 0.810 | 0.645 | 0.649 | **0.043** | 10554 | 42 | 0 | 0 | 0.3 % | **0.999** |
| RandomWalk | RAW | 11428/11428 | 0.000 | 1.000 | 0.190 | 0.064 | **0.255** | 11425 | 2 | 0.02 % | 0 | 3.9 % | **−0.898** |
| RandomWalk | STABLE | 11428 | 0.120 | 0.849 | 0.243 | 0.168 | 0.146 | 3784 | 119 | 0 | 0 | 6.4 % | −0.801 |
| StructuralBreak | RAW | 11428/11428 | 0.218 | 1.000 | **0.980** | **1.000** | 0.086 | 756 | 235 | 0 | **93.3 %** | 0.8 % | **1.000** |
| StructuralBreak | STABLE | 11428 | 0.478 | 0.880 | 0.861 | 0.865 | 0.026 | 778 | 260 | 0 | 0 | 0.04 % | 1.000 |

`Confidence` (points saillants) :

| Dimension | Stage | Min | Max | Mean | StdDev | Distinct | RunMax | Lecture |
|---|---|---|---|---|---|---|---|---|
| Stationarity | RAW | 0.879 | 0.889 | 0.887 | **0.0024** | 11 | 133 | quasi-constante |
| Stationarity | STABLE | 0.7577 | 0.7577 | 0.7577 | **0** | **1** | **11428** | **`CONFIDENCE_CONSTANT` (une seule valeur sur tout le dataset)** |
| Persistence | RAW | 0.571 | 0.984 | 0.854 | 0.071 | 11428 | 1 | informative mais amplitude limitée |
| Persistence | STABLE | 0.713 | 0.863 | 0.839 | 0.019 | 47 | 2320 | fortement figée |
| MeanReversion | RAW | 0.000 | 0.737 | 0.217 | 0.072 | 10965 | 17 | informative (R²) ; corr(V,C)=0.82 |
| MeanReversion | STABLE | 0.121 | 0.381 | 0.217 | 0.058 | 444 | 313 | — |
| RandomWalk | RAW | 0.220 | 0.720 | 0.496 | 0.141 | 11428 | 1 | **anti-corrélée à Value (−0.90)** |
| StructuralStability | STABLE | 0.636 | 0.851 | 0.725 | 0.033 | 10552 | 42 | **duplique Value (corr 0.999)** |
| StructuralBreak | RAW/STABLE | = Value | = Value | = Value | = Value | = Value | = Value | **identique à Value (corr 1.000)** |

### 7.2 Gel brut → stabilisé — `sixdim_raw_vs_stable_freeze.csv`

| Dimension | RawStdDev | StableStdDev | RawChg/StableFrozen | BothChanged | BothFrozen | RawFrozen/StableChg | % RawChg/StableFrozen | MeanStableFrozenRun | MaxStableFrozenRun |
|---|---|---|---|---|---|---|---|---|---|
| Stationarity | 0.203 | 0.148 | 9191 | 1971 | 240 | 25 | **80.4 %** | 5.7 | 182 |
| Persistence | 0.140 | 0.092 | 10844 | 583 | 0 | 0 | **94.9 %** | **19.6** | 924 |
| MeanReversion | 0.250 | 0.183 | 7797 | 3306 | 128 | 196 | 68.2 % | 3.3 | 109 |
| StructuralStability | 0 (pas de brut) | 0.043 | 0 | 0 | 874 | 10553 | 0 % | 1.1 | 42 |
| RandomWalk | 0.255 | 0.146 | 7643 | 3783 | 1 | 0 | 66.9 % | 3.0 | 119 |
| StructuralBreak | 0.086 | 0.026 | 547 | 547 | **10103** | 230 | 4.8 % | 14.7 | 260 |

Lecture : `Persistence` perd **94.9 %** de ses changements bruts dans l'hystérésis, avec
des plateaux figés de **~20 barres en moyenne** (jusqu'à 924). `StructuralBreak` a
`BothFrozen = 10103` (88 % des paires) : la valeur stabilisée ne bouge pas parce que
l'entrée brute est déjà constante à 1.0, pas parce que le FSM la bloque. `Stationarity`
est la plus « transmissive » relative (mais fige quand même 80 % des micro-changements).

### 7.3 Matrice de corrélation inter-dimensions (`Value`) — `sixdim_correlation_{raw,stable}.csv`

**RAW** (Pearson) :

```
                 Stat    Pers    MR      SS   RW      SB
Stationarity      1.000   0.031   0.137   0  -0.058  -0.032
Persistence       0.031   1.000  -0.217   0  -0.067   0.027
MeanReversion     0.137  -0.217   1.000   0  -0.072  -0.210
StructuralStab    0       0       0       0   0       0        (absente en brut)
RandomWalk       -0.058  -0.067  -0.072   0   1.000   0.021
StructuralBreak  -0.032   0.027  -0.210   0   0.021   1.000
```

**STABLE** (Pearson) :

```
                 Stat    Pers    MR      SS      RW      SB
Stationarity      1.000   0.023   0.198  -0.163  -0.072  -0.050
Persistence       0.023   1.000  -0.217   0.342  -0.044   0.057
MeanReversion     0.198  -0.217   1.000  -0.474  -0.060  -0.232
StructuralStab   -0.163   0.342  -0.474   1.000   0.465   0.180
RandomWalk       -0.072  -0.044  -0.060   0.465   1.000   0.040
StructuralBreak  -0.050   0.057  -0.232   0.180   0.040   1.000
```

Faits :

- **`Persistence` et `RandomWalk` sont numériquement quasi indépendantes** malgré la
  source `VarianceRatio` partagée : `−0.067` (brut), `−0.044` (stable). La source
  partagée n'induit **pas** de redondance numérique observable ici (transformations très
  différentes). ⇒ `SHARED_SOURCE` **structurel**, pas `HIGH_REDUNDANCY` empirique.
- **`StructuralStability` (stable) est la seule dimension avec des corrélations
  marquées** : `−0.474` avec `MeanReversion`, `+0.465` avec `RandomWalk`, `+0.342` avec
  `Persistence`. Cohérent avec sa nature : combinaison (quasi-)linéaire du profil des
  autres dimensions. ⇒ `DERIVED_FROM_OTHER_DIMENSION` confirmé numériquement.
- Toutes les autres paires : `|r| ≤ 0.24`. Pas de `HIGH_REDUNDANCY` entre dimensions
  « vivantes ».
- `MeanReversion`–`StructuralBreak` (stable) `= −0.232` : faible ; les barres à demi-vie
  courte (mean-reversion forte) coïncident un peu moins avec `Detected` CUSUM.

---

## 8. Trace « maillon manquant / inutilisé »

| Dimension | Maillon `MISSING` | Maillon `UNUSED` | `NO_OBSERVED_DOWNSTREAM_EFFECT` |
|---|---|---|---|
| D1 Stationarity | — | le `Blend()` interne est calculé puis jeté (`value` local non retourné) | non |
| D2 Persistence | — | idem `Blend()` jeté | non |
| D3 MeanReversion | — | idem `Blend()` jeté | non |
| D4 StructuralStability | pas de source d'évidence dédiée (par conception) | — | non |
| D5 RandomWalk | — | idem `Blend()` jeté | non |
| D6 StructuralBreak | pas de consommateur Decision | `BreakCountMagnitude`, `BreakLocationBarIndex`, `Agreement` (exposés, jamais lus) ; dimension entière non lue | **OUI** |

Note transverse : dans les 5 règles de fusion basées évidence, un `Blend(scientificScore,
qualityScore)` est systématiquement calculé (`value` local) puis **inséré uniquement dans
la chaîne d'explication** ; `FusionConfidence.Value` reçoit `scientificScore` **brut**.
Ce n'est pas un bug (le `Blend` n'est pas censé être la sortie), mais c'est du code mort
numérique qui prête à confusion.

---

## 9. Dataset

| Champ | Valeur |
|---|---|
| Source | Yahoo Finance, `YahooHistoricalBarSource.Load("MES", "M5", …)` |
| Symbole résolu | `MES=F` (Continuous) |
| Timeframe | M5 |
| Profondeur | `DefaultMaxChunkSpanDays` (~59 jours calendaires) |
| Barres totales série | ≈ 11 555 (run `structuralbreak_evidence_lot158`) / 11 556 (run `RegimeCoverageAudit`) — dérive jour-à-jour attendue (fenêtre glissante Yahoo) |
| Warm-up | 128 barres |
| Barres réellement analysées (`Ready`) | ≈ 11 400 |
| Gaps / fingerprint | `HistoricalSeriesFingerprint.Compute` (voir sortie test) |
| ATAS | **non utilisé** |

Les nombres exacts du run d'audit dédié sont dans
`Tests/Research/SixDimensionAudit/Output/` (en-tête `DATASET IDENTITY`).

---

## 10. Look-ahead

- L'audit n'utilise **que** des statistiques descriptives globales hors simulation
  (moyennes, écarts-types, corrélations, taux de gel). Aucune de ces mesures n'est
  réinjectée dans le pipeline.
- La reconstruction brut/stabilisé est strictement **causale** : pour la barre `i`, seule
  l'`EvidenceSet` de la barre `i` (déjà calculée par la production) est fournie à
  `EvidenceFusionEngine.Fuse`, et `FusionStateManager.Update` est appelé dans l'ordre
  chronologique, exactement comme en production. Aucune barre future n'intervient.
- La corrélation `Pearson(Value_i, Confidence_i)` est intra-barre : pas de décalage
  temporel, pas de contamination.
- Les producteurs d'évidence utilisent des fenêtres se terminant à la barre courante
  (`_priceBuffer` alimenté puis lu, `RegimeEngine.Collect`) — pas de fuite.

**Conclusion look-ahead : l'audit lui-même n'introduit aucune contamination temporelle.**

---

## 11. Tests

`Tests/Research/SixDimensionAudit/SixDimensionIndependentAuditTests.cs` — additif, isolé,
déterministe, ne modifie aucune production. Couvre :

1. **Disponibilité réelle** des 6 dimensions (`AvailableN` par dimension).
2. **Variabilité** (Min/Max/Mean/Median/StdDev/DistinctCount/LongestIdenticalRun).
3. **Déterminisme** (double agrégation + hash SHA-256, `Assert.Equal(h1, h2)`).
4. **Run isolation** : `EvidenceFusionEngine` + `FusionStateManager` neufs, alimentés
   uniquement par les `EvidenceSet` du run courant.
5. **Absence de look-ahead** dans la capture : barre `i` ⇐ évidence `i` seulement,
   ordre chronologique.

Le test est protégé réseau (`try/catch` sur `HttpRequestException` /
`TaskCanceledException` / `InvalidOperationException`) : si Yahoo est indisponible
(rate-limit `429` observé pendant cet audit), il journalise `SKIPPED` sans échouer la
suite. Résultat d'exécution : voir §17.

---

## 12–13. Classification finale et tableau obligatoire

### Tableau final obligatoire

| Dimension | Producer | Variability | Saturation | Confidence | Stabilization | Redundancy | Downstream | Verdict |
|---|---|---|---|---|---|---|---|---|
| **Stationarity** | `StationarityRule` (ADF+KPSS) | moyenne (StdDev brut ≈ 0.15–0.21) | modérée (logistique raide autour p=0.05) | `≈ constante` après warm-up (conf. ADF/KPSS = n/Window) | `PARTIALLY_DAMPED` | source disjointe ; sert d'entrée à D4 | 4 règles Decision | `PARTIALLY_INFORMATIVE` |
| **Persistence** | `PersistenceRule` (DFA+VR) | **faible** (StdDev brut ≈ 0.09–0.18, amplitude < hystérésis) | vers 0 (terme quadratique) | partielle (partage `pValue` VR avec Value) | `HEAVILY_DAMPED` (≈ `FROZEN` sur régimes dominants) | `SHARED_SOURCE` avec D5 (VR) ; entrée de D4 | 5 règles Decision | `LOW_INFORMATION` (après stabilisation) |
| **MeanReversion** | `MeanReversionRule` (HalfLife) | **élevée** brute (StdDev ≈ 0.18–0.28) | vers 1 (HL→0) et vers 0 (HL extrêmes) | qualité = R² régression (informative) | `PARTIALLY_DAMPED` | source disjointe ; entrée de D4 ; 1–12 % indispo | 5 règles Decision | `PARTIALLY_INFORMATIVE` |
| **StructuralStability** | `StructuralStabilityRule` via `FusionStateManager` | **très faible** (StableStdDev 0.027–0.044) | vers 1 (défaut warm-up + profil lent) | **`CONFIDENCE_DUPLICATES_VALUE`** (même grandeurs de profil) | `FROZEN` **by design** | **`DERIVED_FROM_OTHER_DIMENSION`** (D1,D2,D3,D5,+soi) | 4 règles Decision | `LOW_INFORMATION` + `REDUNDANT` (dérivée) |
| **RandomWalk** | `RandomWalkRule` (VR) | brute élevée, **niveau moyen bas** (produit de 3 termes) | **vers 0** (saturation basse) | faible amplitude | `PARTIALLY_DAMPED` + `SATURATED_LOW` | `SHARED_SOURCE` avec D2 (VR) ; entrée de D4 | **1 seule** règle (`RandomWalkRule`, poids 0.60) | `PARTIALLY_INFORMATIVE` |
| **StructuralBreak** | `StructuralBreakEvidenceRule` (CUSUM) | **`ZERO_VARIANCE`** quand `Detected` (93 % des barres ; min=max=mean=1.0) | **`SATURATED`** à 1.0 | **`CONFIDENCE_DUPLICATES_VALUE` (exact)** + `SATURATED` | `SATURATED` (l'EMA n'a rien à lisser) | but commun avec D4, chemin disjoint | **AUCUN** | `SATURATED` + `UNUSED_DOWNSTREAM` |

### Fiches de classification

```
DIMENSION: Stationarity
STATUS: calculée à chaque barre, ~100% disponible
INFORMATION CONTENT: moyenne — signal directionnel ADF/KPSS réel, granularité réduite par la logistique raide
VARIABILITY: RawStdDev ≈ 0.15–0.21 ; StableStdDev ≈ 0.09–0.15
NORMALIZATION: clamp explicite [0,1] ; transition quasi-binaire autour de p=0.05
SATURATION: modérée aux extrêmes
CONFIDENCE QUALITY: quasi-constante après warm-up (conf. ADF/KPSS = n/Window)
TEMPORAL STABILITY: PARTIALLY_DAMPED (47–86% des changements bruts figés)
REDUNDANCY: source disjointe ; sert d'entrée à StructuralStability
DOWNSTREAM CONSUMPTION: StableRange, Trending, MeanReverting, StructuralBreak
SCIENTIFIC MATURITY: PARTIALLY_INFORMATIVE

DIMENSION: Persistence
STATUS: calculée à chaque barre, ~100% disponible
INFORMATION CONTENT: faible après stabilisation — amplitude brute souvent < seuil d'hystérésis
VARIABILITY: RawStdDev ≈ 0.09–0.18 ; StableStdDev ≈ 0.045–0.12 ; ~97% transitions brut-change/stable-figé
NORMALIZATION: clamp [0,1] ; terme quadratique x²·sigmoid comprime vers 0
SATURATION: vers 0
CONFIDENCE QUALITY: partielle — partage la p-value VR avec Value
TEMPORAL STABILITY: HEAVILY_DAMPED (proche FROZEN sur MeanReverting/RandomWalk)
REDUNDANCY: SHARED_SOURCE avec RandomWalk (VarianceRatio) ; entrée de StructuralStability
DOWNSTREAM CONSUMPTION: Trending, MeanReverting, StructuralBreak, RandomWalk
SCIENTIFIC MATURITY: LOW_INFORMATION

DIMENSION: MeanReversion
STATUS: calculée ; 1–12% de barres « Missing Evidence » selon régime (HalfLife invalide)
INFORMATION CONTENT: moyenne — forte sensibilité sur HalfLife∈[0,~30]
VARIABILITY: RawStdDev ≈ 0.18–0.28 (la plus variable des dimensions « vivantes »)
NORMALIZATION: exp(-HL/10) borné (0,1] ; s'effondre pour HL extrêmes (outliers > 700, > 12000)
SATURATION: vers 1 (HL→0) et vers 0 (HL grand)
CONFIDENCE QUALITY: R² de la régression demi-vie — informative
TEMPORAL STABILITY: PARTIALLY_DAMPED
REDUNDANCY: source disjointe ; entrée de StructuralStability
DOWNSTREAM CONSUMPTION: StableRange, MeanReverting, StructuralBreak, RandomWalk
SCIENTIFIC MATURITY: PARTIALLY_INFORMATIVE

DIMENSION: StructuralStability
STATUS: reconstruite chaque barre à partir du profil temporel des autres dimensions
INFORMATION CONTENT: faible — bande étroite près de 1.0
VARIABILITY: StableStdDev 0.027–0.044 ; pas de contrepartie brute
NORMALIZATION: clamp sur BehaviourConsistency ; diviseur 0.5 dans MAD/0.5 non documenté
SATURATION: vers 1
CONFIDENCE QUALITY: CONFIDENCE_DUPLICATES_VALUE (mêmes ProfileStability/ProfileVelocity que Value)
TEMPORAL STABILITY: FROZEN by design (c'est une mesure de constance)
REDUNDANCY: DERIVED_FROM_OTHER_DIMENSION (Stationarity, Persistence, MeanReversion, RandomWalk, + soi-même)
DOWNSTREAM CONSUMPTION: StableRange, Trending, MeanReverting, StructuralBreak
SCIENTIFIC MATURITY: LOW_INFORMATION + REDUNDANT

DIMENSION: RandomWalk
STATUS: calculée à chaque barre, ~100% disponible
INFORMATION CONTENT: moyenne, mais concentrée près de 0
VARIABILITY: RawStdDev ≈ 0.21–0.28 ; niveau moyen bas
NORMALIZATION: produit de 3 termes ∈ [0,1] ⇒ biais structurel vers 0
SATURATION: SATURATED_LOW (vers 0)
CONFIDENCE QUALITY: 0.5·(conf. VR + taille) — faible amplitude
TEMPORAL STABILITY: PARTIALLY_DAMPED
REDUNDANCY: SHARED_SOURCE avec Persistence (VarianceRatio) ; entrée de StructuralStability
DOWNSTREAM CONSUMPTION: RandomWalkRule uniquement (poids 0.60)
SCIENTIFIC MATURITY: PARTIALLY_INFORMATIVE

DIMENSION: StructuralBreak
STATUS: calculée, IsAvailable = 99.84% ; Detected = 93.1%
INFORMATION CONTENT: quasi nul sous forme actuelle — constante 1.0 sur 93% des barres
VARIABILITY: ZERO_VARIANCE quand Detected (min=max=mean=median=1.0, N=10758)
NORMALIZATION: clamp(peak/threshold) ; sature à 1.0 dès qu'une rupture est détectée
SATURATION: SATURATED à 1.0
CONFIDENCE QUALITY: CONFIDENCE_DUPLICATES_VALUE exact (Value et Confidence = même contract.Strength)
TEMPORAL STABILITY: SATURATED (l'EMA/hystérésis n'a rien à lisser)
REDUNDANCY: but commun avec StructuralStability, chemin de calcul disjoint
DOWNSTREAM CONSUMPTION: AUCUNE (aucune IDecisionRule, pas d'affichage dashboard)
SCIENTIFIC MATURITY: SATURATED + UNUSED_DOWNSTREAM
```

---

## 14. Critical findings

### OBSERVED FACTS (démontrés par code + données + test)

1. **6 dimensions déclarées, ordre : `Stationarity, Persistence, MeanReversion,
   StructuralStability, RandomWalk, StructuralBreak`.** Le pipeline de production
   construit `EvidenceFusionEngine` avec 5 règles ; `StructuralStabilityRule` est
   appliquée séparément dans `FusionStateManager` ; `StructuralBreakEvidenceRule` est la
   5ᵉ règle de la liste.
2. **`StructuralBreak` (D6) n'a aucun consommateur aval.** `grep` sur
   `FusionDimension.StructuralBreak` hors tests : seulement `FusionStateManager` (liste)
   et `StructuralBreakEvidenceRule` (production). Aucune `IDecisionRule` ne la lit ;
   `DecisionDashboard.Dimensions` ne l'affiche pas. ⇒ `NO_OBSERVED_DOWNSTREAM_EFFECT`.
3. **`StructuralBreak.Value` est saturé à 1.0 sur 100 % des barres détectées**
   (`STRENGTH_WHEN_DETECTED` : N=10 758, min=max=mean=median=1.0). Cause :
   `CUSUM.Confidence = clamp(peak/threshold, 0, 1)` et `peak > threshold` dès
   `ChangeDetected`. `Detected` = 93.1 % des barres.
4. **`StructuralBreak.Value` == `StructuralBreak.Confidence`** exactement, par
   construction (`contract.Strength` affecté aux deux champs dans
   `StructuralBreakEvidenceRule.Evaluate`).
5. **`StructuralStability` (D4) est entièrement dérivée des 4 autres dimensions
   consommées** (+ elle-même) via `FusionProfileAnalyzer`, qui exclut délibérément D6.
   Sa `Confidence` est fonction des mêmes `ProfileStability`/`ProfileVelocity` que sa
   `Value`. `StableStdDev` mesuré 0.027–0.044.
6. **`VarianceRatio` est la source unique de deux dimensions** : `Persistence` (via DFA +
   VR) et `RandomWalk` (via VR seul). ⇒ `SHARED_SOURCE`.
7. **`Persistence` est fortement amortie par la stabilisation** : 73–98 % des barres où
   la valeur brute change voient la valeur stabilisée rester figée (hystérésis 0.03 ≥
   amplitude brute typique ≈ 0.05–0.09 sur les régimes dominants).
8. **Les confidences ADF et KPSS valent `n/WindowSize`** ⇒ constantes ≈ 1.0 après le
   warm-up (fenêtre 60) ⇒ la `Confidence` de `Stationarity` est quasi constante en
   régime établi.
9. **`BaiPerron.Confidence` = 1.0 sur 100 % des barres** (`ablation_lot152` :
   Mean=1, StdDev=0). Bai-Perron `BreakCount > 0` sur 99.98 % du dataset (déjà noté
   Lot 15.6/15.7) ⇒ non discriminant ; c'est pourquoi D6 ne s'appuie que sur CUSUM.
10. **`alpha=0.20` et `HysteresisThreshold=0.03` de `FusionStateManager` sont marqués
    `Provisional`** dans le code (jamais calibrés).
10b. **`corr(Value, Confidence)` mesuré** : `StructuralBreak` = **+1.000** (exact,
    identité) ; `StructuralStability` = **+0.999** (stable) ; `MeanReversion` = **+0.822**
    (RAW) ; `RandomWalk` = **−0.898** (RAW, couplage inverse via `VR.PValue`) ;
    `Persistence` = +0.21 ; `Stationarity` = −0.02.
10c. **`Stationarity.Confidence` stabilisée prend UNE seule valeur** (0.7577) sur les
    11 428 barres prêtes (`DistinctCount = 1`), car les confidences ADF/KPSS = `n/Window`
    sont constantes après warm-up et le lissage/hystérésis les fige définitivement.
10d. **`Persistence` stabilisée : plateaux figés de ~20 barres en moyenne (jusqu'à 924)** ;
    94.9 % des changements bruts n'atteignent jamais l'aval.
11. **Dans les 5 règles de fusion basées évidence, `Blend(scientificScore, qualityScore)`
    est calculé puis jeté** ; `FusionConfidence.Value = scientificScore` brut. Sans
    effet fonctionnel, mais trompeur.
12. **`MeanReversion` est la seule dimension avec une indisponibilité mesurable**
    (1–12 % selon régime), portée par `HalfLife.IsValid == false`.

### SCIENTIFIC INTERPRETATIONS (hypothèses raisonnables)

- **D6 `StructuralBreak`, sous sa forme actuelle, n'apporte aucune information
  exploitable** : elle est à la fois saturée (donc `Value` ne distingue pas les
  contextes) et non consommée (donc même une valeur informative n'aurait aucun effet).
  C'est cohérent avec son intention déclarée (« additive evidence only, available for a
  future explicitly-scoped reweighting lot »).
- **La saturation de D6 vient du choix `Confidence = peak/threshold`** : cette grandeur
  est un ratio « dépassement », pas une probabilité calibrée. Une mesure non saturante
  (p-value CUSUM, distance normalisée, âge de rupture décroissant) redonnerait de la
  granularité — mais c'est une décision de conception hors périmètre de cet audit.
- **D4 `StructuralStability` est proche d'un artefact de lissage** : dérivée de dimensions
  déjà lissées, avec une amplitude effective très faible, elle risque d'apporter surtout
  un biais quasi constant (~0.9–1.0) aux 4 règles qui la pondèrent, plutôt qu'un signal.
- **D2 `Persistence` est probablement « calculée mais non informative » en aval** :
  l'hystérésis la fige avant qu'elle n'atteigne la Decision sur les régimes dominants.
- **La co-dépendance D2/D5 via `VarianceRatio`** peut créer une corrélation d'erreurs :
  un VR aberrant biaise simultanément deux dimensions traitées comme indépendantes par
  l'arbitrage.
- **La `Confidence` globale de la fusion est largement dégénérée** : littéralement
  constante pour D1 (une seule valeur sur 11 428 barres), copie exacte de `Value` pour D6,
  copie à 99.9 % pour D4, co-variation forte (+0.82) pour D3. Le canal « qualité de
  mesure » n'apporte une information **indépendante de `Value`** que pour D2 (faiblement).
- **D5 `RandomWalk` : `Value` et `Confidence` sont mécaniquement anti-corrélées (−0.90)**.
  `Value` a `VR.PValue` en facteur multiplicatif ; `Confidence` (issue de VR) vaut
  `1 − VR.PValue`. Quand le marché « ressemble » le plus à une marche aléatoire (p-value
  élevée), la dimension se déclare la plus confiante… avec la confiance la plus basse.
  C'est une incohérence de conception du couple (Value, Confidence) pour cette dimension.
- **D6 est la seule dimension nourrie par CUSUM ; BaiPerron est inerte** (Confidence=1,
  BreakCount>0 quasi partout). Le « croisement » CUSUM×Bai-Perron annoncé par
  `StructuralBreakAgreement` n'apporte donc aucune discrimination.

### POTENTIAL FUTURE ACTIONS (pistes uniquement — NE PAS exécuter)

- Décider explicitement du statut de D6 : soit lui donner un consommateur
  (`IDecisionRule` dédiée ou pondération dans les règles existantes), soit une mesure
  non saturante, soit la retirer — sur décision utilisateur, dans un lot dédié.
- Réexaminer la mesure de D4 (source d'évidence propre plutôt que dérivation du profil ;
  ou documenter/justifier le diviseur `0.5` de `MAD/0.5`).
- Étudier une hystérésis adaptée à l'amplitude propre de D2 `Persistence`, ou une
  renormalisation de `PersistenceSupport` pour élargir sa plage utile.
- Introduire un canal `Confidence` réellement distinct de `Value` pour D4 et D6.
- Documenter/dériver empiriquement `alpha` et `HysteresisThreshold`.
- Supprimer le `Blend()` mort des 5 règles, ou l'utiliser réellement comme sortie
  (décision de conception).
- Neutraliser la corrélation d'erreurs D2/D5 (p. ex. traiter `VarianceRatio` comme une
  seule évidence partagée dans l'arbitrage).

Aucune de ces actions n'est engagée par le présent document.

---

## 15. Fichiers de production — non modifiés

`FusionDimension.cs`, `FusionStateManager.cs`, `FusionProfileAnalyzer.cs`, les 6 règles
de `Engine/Fusion/Rules/`, `EvidenceFusionEngine.cs`, `RegimeEngine.cs`,
`DecisionEngine.cs`, `DecisionArbitrator.cs`, `SignalEngine.cs`, `EntryTriggerBuilder.cs`,
`EntryTriggerEngine.cs`, `TradePlanBuilder.cs`, `RiskEngine.cs`, `RiskPolicy.cs` :
**aucune modification**. Seuls ajouts : ce rapport +
`Tests/Research/SixDimensionAudit/SixDimensionIndependentAuditTests.cs` + ses sorties
`Output/`.

---

## 16. FINAL OUTPUT

```
STATUS:
INDEPENDENT AUDIT COMPLETE

DIMENSIONS AUDITED:
6/6

PRODUCTION MODIFIED:
NO

CALIBRATION:
NO

OPTIMIZATION:
NO

REWEIGHTING:
NO

PARAMETERS CHANGED:
NO

ATAS:
NOT USED

ORDERS:
NONE

DLL DEPLOYED:
NO

COMMIT:
NO

TESTS:
Tests/Research/SixDimensionAudit/SixDimensionIndependentAuditTests.cs (additif, isolé,
déterministe, réseau-safe). Résultat d'exécution : voir §17.

DATASET:
Yahoo MES=F M5, ~59 jours, ≈ 11 555 barres série / ≈ 11 400 barres analysées, warm-up 128.

DOCUMENTATION:
Documentation/Scientific/QDE-012_Independent_Six_Dimension_Fusion_Audit_Report.md

KEY FINDINGS:
1. StructuralBreak (D6) : zéro consommateur aval + Value saturée à 1.0 sur 93% des barres
   + Value == Confidence par construction ⇒ SATURATED + UNUSED_DOWNSTREAM.
2. StructuralStability (D4) : entièrement dérivée des 4 autres dimensions (+ soi-même),
   amplitude effective 0.027–0.044, Confidence = duplication de Value ⇒ LOW_INFORMATION + REDUNDANT.
3. Persistence (D2) : source partagée avec RandomWalk (VarianceRatio) et amortie par
   l'hystérésis 0.03 au point d'être quasi figée avant la Decision ⇒ LOW_INFORMATION en aval.

CONCLUSIONS:
OBSERVATIONAL ONLY

ROADMAP IMPACT:
NONE UNLESS EXPLICITLY APPROVED BY USER

NEXT ACTION:
STOP — WAIT FOR USER DECISION
```

---

## 17. Résultat d'exécution du test additif

```
dotnet test --filter FullyQualifiedName~SixDimensionIndependentAuditTests

Réussi  IQIAIndicator.Tests.Research.SixDimensionAudit.
        SixDimensionIndependentAuditTests.Integration_Network_SixDimensionIndependentAudit  [3 m 14 s]

=== DATASET IDENTITY ===
Symbol=MES, Timeframe=M5, BarCount=11556
Range: 2026-06-29T11:10:00Z .. 2026-08-27T11:01:21Z
Fingerprint=F2678EA44DDEBDBC05437FCFF4D7A5EC3559221699C34C34F06986C1E5F2E172
BarsProcessed=11556, ReadyBars=11428, WarmupBars=128
ObservedReadyBars=11428

=== DETERMINISM === Hash1=Hash2=fdd4260a461edddaa1cd2278e979623279ded2a9864ba376e15c0d3bdb2b1c42  Identical=True

Nombre total de tests : 1   Réussi(s) : 1
```

- Réseau Yahoo disponible lors de l'exécution (aucun `SKIPPED`). Un `429` transitoire a
  été observé pendant la préparation de l'audit ; le garde-fou réseau du test le
  couvrirait sans faire échouer la suite.
- Aucune assertion violée : pas de `NaN`/`Infinity` dans les cellules produites, double
  agrégation bit-identique.
- Fichiers produits : `Tests/Research/SixDimensionAudit/Output/sixdim_distribution.csv`,
  `sixdim_raw_vs_stable_freeze.csv`, `sixdim_correlation_raw.csv`,
  `sixdim_correlation_stable.csv`.
