# QDE-012 — Sprint 15.25 — Lot 14.14 — Decision / Fusion / Ambiguity : Investigation Scientifique

**Date** : 2026-08-24
**Branche** : `feature/structural-stability-v2`
**Lots précédents** : Lot 14.9 (audit global), Lot 14.10 (binding), Lot 14.11 (protocole), Lot 14.12 (dataset), Lot 14.13 (revalidation AmbiguityGateThreshold)
**Statut** : **IMPLEMENTED**
**Type** : **AUDIT / INVESTIGATION — AUCUNE MODIFICATION DE PRODUCTION, AUCUNE CALIBRATION**

---

# 1. EXECUTIVE SUMMARY

Ce lot répond à la question posée par le Lot 14.13 : **pourquoi `AmbiguityScore` est-il concentré près de 1.0 ?**

Réponse, établie par lecture directe du code réel puis confirmée empiriquement sur le dataset MES=F/M5/59 jours (10 930 barres analysées) :

**Cause principale (catégorie C — Weighting Issue, confirmée empiriquement)** : `AmbiguityScore = Clamp(1 - (WinnerScore - RunnerUpScore), 0, 1)` (`DecisionArbitrator.Arbitrate`). Sur CHAQUE barre du dataset, les **5 `IDecisionRule` produisent systématiquement un candidat** (100% des 10 930 barres, `CandidateCount=5` toujours — voir §12, un artefact de câblage confirmé, pas un choix de conception documenté). Deux de ces cinq règles — `StableRangeRule` et `MeanRevertingRule` — partagent l'essentiel de leur poids sur les DEUX MÊMES dimensions de Fusion (`Stationarity`, `MeanReversion`) : leurs `FinalScore` sont corrélés à **0.9652** sur ce dataset (§14). Comme `MeanReverting` est le régime gagnant le plus fréquent (59.7%) et que son concurrent le plus proche est presque toujours `StableRange` (structurellement quasi-identique par construction des poids), le `Difference` entre Winner et RunnerUp reste mécaniquement petit — d'où `AmbiguityScore` élevé. Ce n'est PAS un problème de formule d'arbitrage (testée séparément, elle réagit correctement à une vraie séparation — §22), ni principalement un problème de normalisation — c'est la **corrélation structurelle entre les fonctions de score des règles de Decision**, causée par leur partage de poids sur les mêmes dimensions Fusion.

**Cause secondaire (catégorie B — Score Distribution)** : même isolément, les 5 `FinalScore` par barre occupent une bande étroite (écarts-types 0.04-0.09, moyennes 0.41-0.58 — §7), jamais les extrêmes [0, 1] — cohérent avec des poids provisoires (Sprint 14, non calibrés) appliqués à des dimensions de Fusion elles-mêmes jamais extrêmes sur ce dataset (§6).

**Hypothèse initiale invalidée par les données** : la lecture du code suggérait que `StructuralStability` pourrait rester proche de 1.0 en permanence (valeurs sentinelles de démarrage `Value=1.0` dans `StructuralStabilityRule.EvaluateAnalysis` et `FusionStateManager.BuildInitialStableResult`). **Mesuré sur 10 930 barres : `StructuralStability` a une moyenne de 0.6447 et un écart-type de 0.0433 (plage [0.5357, 0.7951])** — donc PAS pinné près de 1.0 en pratique ; les valeurs sentinelles ne s'appliquent qu'aux 1-2 premières barres. Cette hypothèse est documentée comme testée et écartée (§9), pas simplement omise.

**Défaut d'implémentation confirmé (catégorie G, P3, sans impact comportemental)** : chaque `IDecisionRule.Evaluate` compare `finalScore > builder.Confidence`, mais `DecisionEngine.Evaluate` crée un `DecisionResultBuilder` NEUF à chaque itération de règle — `builder.Confidence` vaut donc toujours 0.0 au moment de la comparaison, rendant celle-ci vestigiale (toujours vraie en pratique). C'est la cause directe, au niveau code, du `CandidateCount=5` systématique (§12).

**`OverallConfidence` et `AmbiguityScore` sont structurellement indépendants** mais tous deux downstream de `Decision.Winner` : `OverallConfidence` est un indicateur quasi-binaire (1.0 exactement quand Winner=MeanReverting, 0.0 exactement sinon — `stdDev=0.0` dans les deux groupes, §10) — confirmant et durcissant le constat du Lot 14.9 ("ratio de complétude, pas une confiance statistique").

**`AmbiguityGateThreshold` reste inchangé (0.95). Aucune calibration exécutée. Aucun fichier protégé modifié.**

---

# 2. LOT 14.13 FINDINGS (repris tels quels)

- Grille 0.80→0.99 testée ; zone morte (0 signal) pour θ≤0.90 ; transition raide de 0.925 à 0.99 ; qualité par position stable de 0.95 à 0.99 malgré le triplement du nombre de signaux ; classement économique instable entre TRAIN/VALIDATION/OOS ; aucune sélection de seuil final.
- Toutes les propriétés de framework (binding, effet comportemental, déterminisme, isolation, fingerprint, look-ahead) : PASS.

---

# 3. SCIENTIFIC QUESTION

> Pourquoi `AmbiguityScore` est-il concentré près de 1.0 ?

Décomposée par le brief en 11 causes candidates (formule, distribution des Decision Scores, poids Decision, poids Fusion, normalisation, structure Winner/RunnerUp, perte de dispersion, concentration réelle des preuves, problème sémantique, problème d'implémentation, combinaison) — traitées successivement §5 à §20.

---

# 4. RUNTIME ARCHITECTURE (chaîne réelle, tracée par lecture directe)

```
RegimeEngine.Collect(context)
    → EvidenceSet (Adf, Kpss, Dfa, HalfLife, VarianceRatio, Cusum, Volatility, BaiPerron — Backtest/BacktestEngine.cs:287)
        ↓
EvidenceFusionEngine.Fuse(FusionContext{Evidence})
    → itère 4 IFusionRule réels : StationarityRule, PersistenceRule, MeanReversionRule, RandomWalkRule
      (Backtest/BacktestEngine.cs:211-217 — EXACTEMENT ces 4, StructuralStabilityRule ABSENT de cette liste)
    → FusionResult{Dimensions: Dictionary<FusionDimension,FusionConfidence>} — 4 clés seulement à ce stade
        ↓
FusionStateManager.Update(rawResult, timestamp)
    → EMA (alpha=0.20) + hystérésis (0.03) sur les 4 dimensions déjà présentes
    → FusionProfileAnalyzer.Analyze(snapshot) sur un historique glissant de 6 snapshots
    → StructuralStabilityRule.EvaluateAnalysis(analysis) → 5ème dimension (StructuralStability),
      calculée SÉPARÉMENT à partir de l'HISTORIQUE temporel des 4 autres, jamais d'une EvidenceSet directe
    → FusionSnapshot.StableResult : FusionResult à 5 dimensions, désormais complet
        ↓
DecisionEngine.Evaluate(DecisionContext{FusionResult: StableResult})
    → itère 5 IDecisionRule réels : StableRangeRule, TrendingRule, MeanRevertingRule, StructuralBreakRule,
      RandomWalkRule (Backtest/BacktestEngine.cs:219-226)
    → CHAQUE règle reçoit un DecisionResultBuilder NEUF (Confidence initial = 0.0) — voir §12
    → 5 DecisionCandidate (empiriquement CONFIRMÉ : 100% des barres, §12)
        ↓
DecisionArbitrator.Arbitrate(candidates)
    → tri décroissant par FinalScore ; Winner=candidates[0] ; RunnerUp=candidates[1] ou score=0.0 si absent
    → Difference = WinnerScore - RunnerUpScore
    → AmbiguityScore = Clamp(1.0 - Difference, 0.0, 1.0)
        ↓
EntryTriggerBuilder.DetermineDirection
    → si Winner != MeanReverting → NO_ACTION (le gate d'ambiguïté n'est JAMAIS atteint — confirmé Lot 14.13 §12)
    → si Winner == MeanReverting ET AmbiguityScore >= AmbiguityGateThreshold → NO_ACTION (DECISION_AMBIGUOUS)
    → sinon → BUY_CANDIDATE / SELL_CANDIDATE selon le signe de DynamicZScore
```

**Confirmation empirique de ce tracé** : ce lot a rejoué indépendamment `EvidenceFusionEngine`+`FusionStateManager` (mêmes classes réelles, mêmes 4 `IFusionRule`, même ordre bar-par-bar) sur les 10 930 `EvidenceSet` déjà calculées par `BacktestEngine.RunSignalPipeline` (appelé UNE fois, non modifié). Un second rejeu indépendant (instances fraîches) reproduit `StructuralStability` bit-identique sur les 10 930 barres (§ReplayDeterminism, PASS) — validant que la relecture est fidèle au calcul réel, pas une approximation.

---

# 5. AMBIGUITYSCORE FORMULA

Source exacte : `Engine/Decision/Arbitration/DecisionArbitrator.cs:31-38`.

```
orderedCandidates = candidates.OrderByDescending(c => c.FinalScore)
Winner       = orderedCandidates[0]
RunnerUp     = orderedCandidates.Length > 1 ? orderedCandidates[1] : null
RunnerUpScore = RunnerUp?.FinalScore ?? 0.0        ← PAS null-propagé, sentinelle 0.0
Difference    = WinnerScore - RunnerUpScore
AmbiguityScore = Math.Clamp(1.0 - Difference, 0.0, 1.0)
```

- **Numérateur/dénominateur** : pas de ratio — une SOUSTRACTION simple, jamais divisée par rien.
- **Normalisation** : `Clamp(·, 0, 1)` en sortie uniquement ; aucune normalisation en amont de la soustraction elle-même.
- **Bornes** : `AmbiguityScore ∈ [0, 1]` garanti par construction (`Math.Clamp`), vérifié invariant sur les 10 930 barres et par test synthétique avec des `FinalScore` hors [0,1] (§22, `ExtremeScores_NoNaNNoInfinity`).
- **Cas limite : un seul candidat** — `RunnerUpScore = 0.0` (jamais `null`/`NaN`), donc `Difference = WinnerScore`, donc `AmbiguityScore = 1 - WinnerScore`. **Effet observé (§22)** : un candidat SEUL et FORT (WinnerScore=0.90) produit une ambiguïté FAIBLE (0.10) ; un candidat seul et FAIBLE (0.10) produit une ambiguïté FORTE (0.90) — la métrique s'inverse selon la force du seul candidat, sans jamais avoir comparé à un vrai concurrent.
- **Cas limite : zéro candidat** — `DecisionArbitrator.Arbitrate` retourne `AmbiguityScore=0.0`, `Winner=Unknown` explicitement (pas de division par zéro, pas de branche manquante).
- **Scores négatifs / hors [0,1]** : non rejetés à ce niveau (`DecisionCandidate.FinalScore` n'a pas de contrainte de plage) — testé synthétiquement (`FinalScore=5.0` et `-3.0`) : `Difference=8.0`, `AmbiguityScore=Clamp(1-8,0,1)=0.0` — pas de NaN/Infinity (§22).
- **Traitement des scores proches** : `Difference→0` ⇒ `AmbiguityScore→1`, testé exactement (deux candidats à 0.65 bit-identiques ⇒ `Difference=0.0`, `AmbiguityScore=1.0`, égalité EXACTE, pas approchée — §22).

**Verdict formule** : la formule elle-même est simple, correctement bornée, déterministe, sans division ni cas non couvert. **Elle n'est pas la cause de la compression** — elle rapporte fidèlement un `Difference` qui, lui, est structurellement petit pour les raisons tracées §6-§14.

---

# 6. SEMANTIC ANALYSIS

| Question | Réponse |
|---|---|
| Mesure-t-elle une "ambiguïté" au sens statistique (ex. entropie sur une distribution de probabilité) ? | **Non** — aucune notion de probabilité/distribution n'intervient ; c'est une différence brute entre deux scores hétérogènes. |
| Mesure-t-elle la dominance du Winner ? | **Oui, par construction inversée** : `AmbiguityScore = 1 - (dominance du Winner sur son plus proche concurrent)`. C'est un score de MARGE (au sens "margin" d'un classifieur), présenté comme une "ambiguïté". |
| Mesure-t-elle une différence relative ? | **Non** — c'est une différence ABSOLUE (`Winner - RunnerUp`), jamais divisée par une échelle (ex. `(Winner-RunnerUp)/Winner`) — deux candidats à (0.10, 0.05) et (0.90, 0.85) produisent le MÊME `Difference` (0.05) malgré une différence relative très différente (100% vs 5.9%). |
| Le nom "Ambiguity" correspond-il à l'usage réel (le gate `EntryTriggerBuilder`) ? | **Partiellement** — le gate l'utilise bien comme "le régime arbitré est-il suffisamment net pour agir", ce qui EST une lecture raisonnable de marge-donc-fiabilité. Le problème sémantique se situe au CAS LIMITE (candidat unique, §5/§22), où la métrique s'écarte de son intention : sans concurrent réel, elle ne mesure plus une "ambiguïté entre hypothèses" mais littéralement `1 - WinnerScore`. |

**Incohérence identifiée (classée D14.14-6, §21)** : le nom "AmbiguityScore" suggère une mesure d'incertitude générale sur le régime détecté ; la formule mesure une MARGE relative au concurrent le plus proche SPÉCIFIQUEMENT. Ces deux notions coïncident la plupart du temps (régime net ⇒ marge large ⇒ faible ambiguïté) mais divergent explicitement dans le cas à un seul candidat et, plus largement, chaque fois que le concurrent le plus proche est structurellement corrélé au Winner (§14) plutôt que réellement indépendant.

---

# 7. OVERALLCONFIDENCE ANALYSIS

Source : `Engine/ScientificFusion/ScientificAssessmentBuilder.cs:100-102` — `OverallConfidence = SuccessfulModels.Count / (double)ExpectedModels.Count` (`ExpectedModels` = 5 noms fixes : KalmanFilterModel, OrnsteinUhlenbeckModel, DynamicZScoreModel, VolatilityModel, SPRTModel).

**Source amont** : `ScientificModelRegistry.Resolve(methodologySelection)` (`Engine/ScientificModels/Registry/ScientificModelRegistry.cs:31-68`) — audité directement : **seule `"MeanReversionMethodology"` retourne les 5 modèles réels** ; les 6 autres méthodologies (`TrendFollowingMethodology`, `StructuralBreakMethodology`, `RandomWalkMethodology`, `StableRangeMethodology`, `TransitionalMethodology`, tout nom non reconnu) retournent `Array.Empty<IScientificModel>()` — documenté dans le code comme un choix audité (Sprint 15.5/C2), pas un oubli : les modèles manquants (`BOCPDModel`, `TimeSeriesMomentumModel`) sont des stubs qui retournent toujours `Success=false`.

**Distribution mesurée (10 930 barres)** :

| Groupe | n | min | max | mean | stdDev |
|---|---|---|---|---|---|
| Tous | 10 930 | 0.0000 | 1.0000 | 0.5967 | 0.4906 |
| Winner=MeanReverting | 6 522 | **1.0000** | **1.0000** | **1.0000** | **0.0000** |
| Winner≠MeanReverting | 4 408 | **0.0000** | **0.0000** | **0.0000** | **0.0000** |

**Constat, plus net que ce que le code seul suggérait** : `OverallConfidence` n'est pas un ratio graduel dans la pratique — il vaut **exactement** 1.0 ou **exactement** 0.0 sur les 10 930 barres observées, sans aucune valeur intermédiaire (`stdDev=0.0` dans les deux groupes). Cela signifie que, sur ce dataset, **les 5 modèles réussissent TOUJOURS ensemble quand ils s'exécutent** (jamais un succès partiel 3/5 ou 4/5) — un fait empirique non anticipé par la lecture du code seul (qui n'excluait pas des échecs partiels de modèle). `OverallConfidence` est donc, empiriquement, un **indicateur binaire déguisé en ratio** : il encode uniquement "Winner == MeanReverting ?", rien de plus, sur cette fenêtre.

**Indépendance vis-à-vis d'`AmbiguityScore`** : structurellement indépendant (chemins de calcul disjoints, §4) — `OverallConfidence` ne consomme jamais `Decision.Candidates`/`Winner`/`AmbiguityScore`, et `DecisionArbitrator` ne consomme jamais `ScientificAssessment`. `Corr(OverallConfidence, AmbiguityScore) = 0.2670` — faible et positive, entièrement explicable comme un effet de CONFOUNDING via le régime partagé (MeanReverting a une distribution d'AmbiguityScore légèrement plus resserrée que Trending, §11) plutôt qu'un lien causal direct.

---

# 8. WINNER/RUNNER-UP ANALYSIS

- **Sélection du Winner** : `orderedCandidates[0]` après tri décroissant par `FinalScore` — jamais de règle de priorité secondaire en cas d'égalité (`OrderByDescending` de LINQ est stable, donc l'ordre d'insertion — l'ordre du tableau `IDecisionRule[]` passé au constructeur de `DecisionEngine` — départage silencieusement les ex-æquo exacts). Non testé explicitement dans ce lot (aucune égalité EXACTE entre 2 des 5 règles n'a été observée sur les 10 930 barres réelles), mais confirmé par lecture de `List<T>.OrderByDescending`.
- **RunnerUp toujours disponible ?** **Non** — seulement si `candidates.Length > 1`. Sur ce dataset, `CandidateCount=5` sur 100% des barres (§12), donc RunnerUp EST toujours disponible en pratique ici — mais la sentinelle `0.0` (§5) reste un cas réel du code pour tout scénario à 0-1 candidat (ex. si une future modification des règles en désactive plusieurs).
- **Même unité, comparables ?** Oui — tous les `FinalScore` sont dans `[0,1]` par construction (`Math.Clamp` en fin de chaque règle), donc directement comparables numériquement. Mais "comparables numériquement" ≠ "mesurant la même chose" : chaque règle a ses propres poids/dimensions (§13) — comparer leurs sorties revient à comparer 5 régressions linéaires différentes bornées sur la même échelle, pas 5 mesures de la même quantité.
- **Nombre de candidats influence-t-il le résultat ?** Oui, mécaniquement — un RunnerUp absent ⇒ `Difference=WinnerScore` (§5) ; avec 5 candidats systématiques (§12), ce cas ne s'est jamais produit sur ce dataset, mais reste un chemin de code actif.

---

# 9. DECISION SCORE DISTRIBUTION (10 930 barres, production θ=0.95)

## FinalScore par règle (barres où la règle a produit un candidat)

| Règle | n | min | max | mean | stdDev | P05 | P25 | P50 | P75 | P95 | P99 |
|---|---|---|---|---|---|---|---|---|---|---|---|
| StableRange | 10 930 | 0.3055 | 0.7285 | 0.5251 | 0.0859 | 0.3767 | 0.4721 | 0.5221 | 0.5765 | 0.6791 | 0.7119 |
| Trending | 10 930 | 0.3157 | 0.7101 | 0.4331 | 0.0542 | 0.3561 | 0.4065 | 0.4319 | 0.4480 | 0.5348 | 0.6577 |
| MeanReverting | 10 930 | 0.3036 | 0.7661 | 0.5809 | 0.0822 | 0.4317 | 0.5305 | 0.5857 | 0.6333 | 0.7164 | 0.7494 |
| StructuralBreak | 10 930 | 0.3568 | 0.6566 | 0.5485 | 0.0388 | 0.4855 | 0.5292 | 0.5513 | 0.5689 | 0.6121 | 0.6362 |
| RandomWalk | 10 930 | 0.2540 | 0.7233 | 0.4081 | 0.0842 | 0.3201 | 0.3416 | 0.3826 | 0.4537 | 0.5837 | 0.6504 |

**Constat** : les 5 distributions sont TOUTES confinées à `[0.25, 0.77]` environ — jamais proches des bornes théoriques `[0, 1]` — et leurs moyennes sont resserrées (`0.41` à `0.58`, écart de 0.17 seulement). Aucune règle n'atteint un régime "quasi-certain" (>0.90) ni "quasi-nul" (<0.10) sur ce dataset — cohérent avec la catégorie B (Score Distribution Issue).

## Winner/RunnerUp/Difference/AmbiguityScore (trace de compression complète, brief §8)

| Étage | n | min | max | mean | stdDev | P05 | P25 | P50 | P75 | P95 | P99 |
|---|---|---|---|---|---|---|---|---|---|---|---|
| WinnerScore | 10 930 | 0.5106 | 0.7661 | 0.6158 | 0.0494 | 0.5570 | 0.5780 | 0.6043 | 0.6428 | 0.7165 | 0.7494 |
| RunnerUpScore | 10 930 | 0.4309 | 0.7285 | 0.5646 | 0.0550 | 0.4766 | 0.5371 | 0.5571 | 0.5876 | 0.6769 | 0.7119 |
| Difference | 10 930 | 0.0000 | 0.2671 | 0.0512 | 0.0381 | 0.0054 | 0.0286 | 0.0455 | 0.0625 | 0.1376 | 0.1934 |
| AmbiguityScore | 10 930 | 0.7329 | 1.0000 | 0.9488 | 0.0381 | 0.8624 | 0.9375 | 0.9545 | 0.9714 | 0.9946 | 0.9990 |

**Où la compression apparaît** : PAS entre WinnerScore et RunnerUpScore individuellement (chacun a un écart-type raisonnable, ~0.05) — elle apparaît dans leur DIFFÉRENCE, parce que WinnerScore et RunnerUpScore sont eux-mêmes fortement co-variants bar à bar (ils dérivent souvent des mêmes dimensions Fusion sous-jacentes, §14). `Difference` ne dépasse JAMAIS 0.2671 sur les 10 930 barres — donc `AmbiguityScore` ne descend JAMAIS sous 0.7329, quel que soit le régime. **La compression est déjà entièrement présente au moment où Winner/RunnerUp sont calculés — elle n'est pas introduite par le Clamp final ni par une étape de normalisation supplémentaire.**

## Raw Evidence (avant Fusion) — non comparable directement

Les 9 evidences brutes (`AdfResult.PValue`, `KpssResult.PValue`, `DfaResult.Hurst`, `VarianceRatioResult.VarianceRatio`/`ZStatistic`, `HalfLifeResult.HalfLife`, etc., `Engine/Regime/Evidence/*`) vivent sur des ÉCHELLES HÉTÉROGÈNES (p-values dans [0,1] mais interprétées par seuil à 0.05, Hurst autour de 0.5, Variance Ratio en échelle log, demi-vie en nombre de barres non borné). Le premier point où une échelle [0,1] commune et directement comparable existe est la sortie de Fusion (`FusionConfidence.Value`) — construire une table de percentiles "Raw Evidence" à ce stade produirait une comparaison trompeuse (des unités différentes cote à cote). Ce lot documente ce fait plutôt que de fabriquer un score agrégé non justifié (brief §15, discipline anti-invention).

---

# 10. FUSION SCORE DISTRIBUTION (10 930 barres)

| Dimension | n | min | max | mean | stdDev | P05 | P25 | P50 | P75 | P95 | P99 | IsAvailable=false |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Stationarity | 10 930 | 0.2467 | 0.7891 | 0.4195 | 0.1456 | 0.2611 | 0.2921 | 0.3751 | 0.5297 | 0.7136 | 0.7795 | 0 |
| Persistence | 10 930 | 0.0000 | 0.7466 | 0.1686 | 0.0920 | 0.1237 | 0.1327 | 0.1397 | 0.1480 | 0.3566 | 0.5983 | 0 |
| MeanReversion | 10 930 | 0.1202 | 0.7994 | 0.5582 | 0.1829 | 0.1674 | 0.4482 | 0.6131 | 0.7055 | 0.7723 | 0.7936 | 0 |
| RandomWalk | 10 930 | 0.1201 | 0.8490 | 0.2426 | 0.1456 | 0.1240 | 0.1384 | 0.1680 | 0.3123 | 0.5643 | 0.6788 | 0 |
| StructuralStability | 10 930 | 0.5357 | 0.7951 | 0.6447 | 0.0433 | 0.5718 | 0.6108 | 0.6488 | 0.6770 | 0.7090 | 0.7414 | 0 |

**Aucune dimension n'a de valeur `IsAvailable=false`** sur ce dataset (0/10930 partout) — le mécanisme "Missing Evidence" (Sprint 14, §Correlation/§Configuration) n'est jamais activé ici, donc ne contribue pas à la compression observée sur cette fenêtre précise.

**Constat par dimension** :
- `Persistence` a la distribution la plus étroite et la plus basse (P25-P75 = [0.133, 0.148], quasi ponctuelle) — cette dimension varie très peu la plupart du temps, avec une queue haute rare (P99=0.598).
- `MeanReversion` a la distribution la plus étalée et la plus haute en moyenne (0.558) — cohérent avec le fait que MeanReverting gagne 59.7% du temps.
- `StructuralStability` occupe une bande resserrée [0.54, 0.80] — ni proche de 0 ni de 1, contredisant l'hypothèse initiale de pinning à 1.0 (§ci-dessous, §Compression Trace amont).

## StructuralStability — hypothèse initiale testée et écartée

La lecture du code (`StructuralStabilityRule.EvaluateAnalysis`, `FusionStateManager.BuildInitialStableResult`) montrait des valeurs SENTINELLES à exactement `1.0` pour les 1-2 premières barres (démarrage). L'hypothèse formée à la lecture du code était que ce pinning pourrait dominer sur la durée, étant donné le double lissage (fenêtre glissante des tests statistiques + EMA α=0.20 + hystérésis 0.03 à l'intérieur de `FusionStateManager`) qui ralentit fortement toute variation bar-à-bar.

**Mesure réelle sur 10 930 barres : moyenne 0.6447, écart-type 0.0433 — PAS pinné à 1.0.** Le mécanisme réel (`FusionProfileAnalyzer.CalculateBehaviourConsistency = 0.65×ProfileStability + 0.35×(1-ProfileVelocity)`, sur une fenêtre glissante de 6 snapshots) produit en pratique une valeur modérée et modestement variable, jamais extrême, sur cette fenêtre de marché. **Hypothèse documentée comme testée puis invalidée par les données**, conformément à la discipline du brief (§9 "ne pas modifier la formule ; documenter").

---

# 11. COMPRESSION TRACE — table consolidée

| Étage | mean | stdDev | P05 | P50 | P95 |
|---|---|---|---|---|---|
| Fusion — Stationarity | 0.4195 | 0.1456 | 0.2611 | 0.3751 | 0.7136 |
| Fusion — MeanReversion | 0.5582 | 0.1829 | 0.1674 | 0.6131 | 0.7723 |
| Fusion — StructuralStability | 0.6447 | 0.0433 | 0.5718 | 0.6488 | 0.7090 |
| Decision — WinnerScore | 0.6158 | 0.0494 | 0.5570 | 0.6043 | 0.7165 |
| Decision — RunnerUpScore | 0.5646 | 0.0550 | 0.4766 | 0.5571 | 0.6769 |
| Decision — Difference | 0.0512 | 0.0381 | 0.0054 | 0.0455 | 0.1376 |
| **AmbiguityScore** | **0.9488** | **0.0381** | **0.8624** | **0.9545** | **0.9946** |

**Où la compression apparaît, précisément** : entre l'étage "Fusion" (dispersion modérée, stdDev 0.04-0.18 selon dimension) et l'étage "Difference" (stdDev réduit à 0.038, mais surtout MOYENNE écrasée à 0.051 sur une plage possible de [0,1]). Ce n'est pas la dispersion de WinnerScore/RunnerUpScore qui s'effondre (chacun garde un stdDev ~0.05, comparable aux dimensions Fusion) — c'est leur COVARIANCE qui est élevée (§14), ce qui réduit mécaniquement la variance de leur différence bien plus que ne le suggérerait la variance de chaque terme pris isolément. `Var(A-B) = Var(A) + Var(B) - 2·Cov(A,B)` : avec `Cov` élevée (candidats corrélés), `Var(Difference)` est structurellement écrasée — exactement le mécanisme statistique confirmé empiriquement §14.

---

# 12. NORMALIZATION AUDIT — et défaut d'implémentation D14.14-3

Opérations de normalisation recensées par lecture directe :

| Opération | Où | Effet |
|---|---|---|
| `Math.Clamp(·, 0, 1)` | Chaque `IFusionRule.Evaluate` (fin de blend scientifique+qualité) | Borne chaque dimension à [0,1] — ne comprime pas la dispersion interne, seulement les dépassements |
| `Math.Clamp(·, 0, 1)` | Chaque `IDecisionRule.Evaluate` (fin de blend) | Idem, au niveau candidat |
| `Math.Clamp(1-Difference, 0, 1)` | `DecisionArbitrator.Arbitrate` | Borne AmbiguityScore, discuté §5 — pas la cause de la compression (Difference lui-même est petit) |
| `EMA(α=0.20)` + hystérésis (0.03) | `FusionStateManager.BuildStableResult` | Lisse temporellement, RÉDUIT la variance bar-à-bar de chaque dimension (hors StructuralStability) — contribue à des dimensions Fusion moins bruitées, donc indirectement à des `FinalScore` de règles moins bruités, mais ne cause pas leur CORRÉLATION mutuelle (§14) |
| Logistic/tanh (`StationarityRule`, `PersistenceRule` côté Fusion) | `Engine/Fusion/Rules/*.cs` | Transformations non-linéaires DANS le calcul de chaque dimension Fusion — ne concernent pas la comparaison inter-règles au niveau Decision |

**Aucune opération de normalisation ne force mécaniquement `AmbiguityScore → 1` ou `Difference → 0` de façon directe et inconditionnelle** (contrairement à l'hypothèse D possible du brief) — l'EMA/hystérésis RÉDUIT le bruit (ce qui, en soi, réduirait plutôt la dispersion de CHAQUE dimension sans nécessairement les rapprocher les unes des autres), mais la cause dominante reste le partage de poids (§13/§14), pas la normalisation en tant que telle.

## Défaut D14.14-3 (P3, sans impact comportemental)

`Engine/Decision/Rules/*.cs` (les 5 fichiers) : `if (finalScore > builder.Confidence) { ... }`. Mais `DecisionEngine.Evaluate` (`Engine/Decision/Core/DecisionEngine.cs:35`) crée `var builder = new DecisionResultBuilder();` À L'INTÉRIEUR de la boucle `foreach (IDecisionRule rule in Rules)` — un builder FRAIS par règle, `Confidence` initial 0.0. La comparaison est donc TOUJOURS vraie dès que `finalScore > 0` (quasi toujours, vu les poids et la borne inférieure du blend). **Confirmé empiriquement : `CandidateCount=5` sur 100% des 10 930 barres** — chaque règle produit TOUJOURS un candidat, jamais un rejet par cette comparaison. Le code lit comme s'il comparait plusieurs hypothèses internes à une même règle et ne gardait que la meilleure ; en réalité chaque règle ne propose jamais qu'une seule hypothèse par barre, rendant la comparaison vestigiale. **Aucun impact comportemental** (chaque règle n'a de toute façon qu'une seule branche à évaluer) — classé P3, lisibilité/maintenabilité uniquement.

---

# 13. WEIGHT AUDIT

## Poids Fusion (par dimension, hardcodés en constantes privées)

| Règle | Poids scientifiques | Poids blend final |
|---|---|---|
| StationarityRule | — (ADF/KPSS combinés en interne) | Scientific=0.80 / Quality=0.20 |
| PersistenceRule | — (DFA/VarianceRatio combinés en interne) | Scientific=0.90 / Quality=0.10 |
| MeanReversionRule | — (HalfLife seul) | Scientific=0.80 / Quality=0.20 |
| RandomWalkRule | — (VarianceRatio seul) | Scientific=0.80 / Quality=0.20 |
| StructuralStabilityRule | — (FusionProfileAnalysis) | N/A (Value=BehaviourConsistency directement) |

## Poids Decision (par règle, hardcodés en constantes privées, AUCUNE configuration externe)

| Règle | Composantes scientifiques (somme=1.0) | Blend final |
|---|---|---|
| StableRangeRule | Stationarity=0.40, MeanReversion=0.40, StructuralStability=0.20 | 0.90/0.10 |
| TrendingRule | Persistence=0.50, StructuralStability=0.30, (1-Stationarity)=0.20 | 0.90/0.10 |
| MeanRevertingRule | MeanReversion=0.40, Stationarity=0.30, (1-Persistence)=0.20, StructuralStability=0.10 | 0.90/0.10 |
| StructuralBreakRule | (1-StructuralStability)=0.40, (1-Persistence)=0.30, (1-MeanReversion)=0.20, (1-Stationarity)=0.10 | 0.90/0.10 |
| RandomWalkRule | RandomWalk=0.60, (1-Persistence)=0.20, (1-MeanReversion)=0.20 | 0.90/0.10 |

**Recoupement de poids, source directe de la corrélation §14** : `StableRangeRule` et `MeanRevertingRule` mettent ENSEMBLE 0.70-0.80 de leur poids scientifique sur `Stationarity`+`MeanReversion` — les deux mêmes dimensions, dans le même sens (jamais inversées entre les deux règles). C'est un recoupement STRUCTUREL, pas accidentel : les deux régimes ("marché stable" et "retour à la moyenne") sont conceptuellement proches, et leurs formules le reflètent — mais cette proximité conceptuelle se traduit ici en quasi-DUPLICATION de la fonction de score, jamais atténuée par un terme différenciateur à poids suffisant.

**Effet structurel potentiel (jamais calibré ici, brief §10 respecté)** : si `Stationarity` et `MeanReversion` co-varient (ce qui est plausible — les deux mesurent des propriétés liées de la même série de prix), alors `StableRangeRule` et `MeanRevertingRule` co-varient encore plus fortement que leurs entrées individuelles, amplifiant leur corrélation mutuelle au-delà de ce qu'un partage de poids seul produirait — cohérent avec le `0.9652` mesuré (§14), plus élevé que ce qu'un partage de poids sur des entrées indépendantes produirait typiquement.

---

# 14. CONFIGURATION WIRING — Dead Parameter Audit

| Paramètre déclaré | Existe | Sérialisé/exposé | Consommé au runtime | Classification |
|---|---|---|---|---|
| `AmbiguityGateThreshold` | Oui | Oui (`PipelineParameterOverrides`, `CalibrationParameterSet`) | Oui, confirmé Lot 14.10/14.13 | **CONNECTED** |
| `FusionConfiguration.Weights/Thresholds/EnableCalibration` | Oui (`Engine/Fusion/Core/FusionConfiguration.cs`) | Non (aucun constructeur de `EvidenceFusionEngine`/`IFusionRule`/`FusionStateManager` ne l'accepte) | **Aucun consommateur trouvé** (grep exhaustif : seuls les rapports Lot 14.9/14.11 et des fichiers `build_diag*.txt` — pas de code source) | **DEAD** |
| Poids Decision (5 règles, 15 constantes) | Oui, en `private const double` dans chaque fichier de règle | Non — aucun type `DecisionConfiguration` n'existe même dans le dépôt | N/A | **DEAD** — plus profondément déconnecté que Fusion (pas même un type inutilisé) |
| Poids Fusion (blend scientifique/qualité, 4 règles) | Oui, en `private const double` | Non | N/A | **DEAD** |
| `StructuralStabilityRule` | Oui, classe complète | N/A (pas un paramètre, un composant) | **Oui, mais via un mécanisme SÉPARÉ** (`FusionStateManager`, pas `IFusionRule`/`EvidenceFusionEngine`) | **CONNECTED (chemin asymétrique — D14.14-5)** |

**Nouveau constat par rapport au Lot 14.11** : ce lot confirme par grep exhaustif (`grep -r "FusionConfiguration"`) qu'il n'existe **aucun** consommateur runtime, dans **aucun** fichier source de `Engine/` ou `Backtest/` — le Lot 14.11 l'avait déjà noté comme "interrupteur, pas une valeur à calibrer" ; ce lot précise qu'il s'agit d'un DEAD CODE complet, pas d'un simple interrupteur non activé.

---

# 15. CORRELATION ANALYSIS

```
Corr(Difference, AmbiguityScore)      = -1.0000  (attendu, identité mathématique via Clamp)
Corr(WinnerScore, AmbiguityScore)     = -0.2295  (faible)
Corr(OverallConfidence, AmbiguityScore) = 0.2670  (faible, confounding par régime, §7)
```

## Corrélations inter-règles (FinalScore, n=10 930 pour toutes les paires)

| Paire | Corrélation |
|---|---|
| StableRange ↔ MeanReverting | **0.9652** |
| StableRange ↔ StructuralBreak | -0.6688 |
| Trending ↔ MeanReverting | -0.6547 |
| StableRange ↔ Trending | -0.5169 |
| MeanReverting ↔ StructuralBreak | -0.4983 |
| StableRange ↔ RandomWalk | -0.3218 |
| MeanReverting ↔ RandomWalk | -0.3090 |
| Trending ↔ StructuralBreak | -0.2840 |
| StructuralBreak ↔ RandomWalk | 0.2490 |
| Trending ↔ RandomWalk | 0.0780 |

**C'est le résultat le plus important de ce lot.** `StableRange` et `MeanReverting` sont corrélés à 0.9652 — quasiment colinéaires. Toutes les autres paires sont MODÉRÉMENT à FORTEMENT anti-corrélées (souvent -0.3 à -0.67), ce qui est cohérent avec leurs poids largement opposés (§13) — une anti-corrélation forte entre deux règles ne les rapproche PAS en valeur absolue en général, sauf quand leurs poids ET leurs entrées se combinent pour produire des valeurs proches malgré une relation inverse (observé pour `MeanReverting ↔ StructuralBreak`, -0.4983, moins extrême).

**Conséquence directe sur la compression** : puisque `MeanReverting` gagne le plus souvent (59.7%) et que son concurrent structurellement le plus proche (`StableRange`, corrélation 0.9652) a une moyenne de FinalScore proche de la sienne (0.5251 vs 0.5809, écart de 0.056 seulement) mais ne gagne quasiment jamais (`StableRange` Winner = 1.1% des barres seulement), **`StableRange` agit très souvent comme un RunnerUp structurellement condamné à rester proche du Winner sans jamais le dépasser** — le scénario exact qui maximise `AmbiguityScore` sans jamais produire d'inversion de Winner (donc sans instabilité de régime détecté, juste une ambiguïté mesurée constamment élevée).

---

# 16. DIFFERENCE ANALYSIS

Voir §9/§11 pour la distribution complète. Rappel des bornes observées : `Difference ∈ [0.0000, 0.2671]` sur 10 930 barres, TOUS régimes confondus — jamais plus de 26.71% de marge entre Winner et RunnerUp sur ce dataset. `AmbiguityScore` ne peut donc structurellement jamais descendre sous `1 - 0.2671 = 0.7329` sur cette fenêtre — cohérent exactement avec le minimum observé (`AmbiguityScore.min = 0.7329`, §9).

---

# 17. REGIME CONDITIONAL ANALYSIS

| Winner | n | % | AmbiguityScore mean | stdDev | P05 | P50 | Prudence |
|---|---|---|---|---|---|---|---|
| MeanReverting | 6 522 | 59.7% | 0.9571 | 0.0180 | 0.9308 | 0.9553 | REPRESENTED |
| StructuralBreak | 3 357 | 30.7% | 0.9357 | 0.0517 | 0.8266 | 0.9494 | REPRESENTED |
| RandomWalk | 483 | 4.4% | 0.9594 | 0.0317 | 0.8956 | 0.9650 | **UNDERREPRESENTED — pas de conclusion forte** |
| Trending | 448 | 4.1% | 0.9064 | 0.0716 | 0.7794 | 0.9273 | **UNDERREPRESENTED — pas de conclusion forte** |
| StableRange | 120 | 1.1% | 0.9757 | 0.0204 | 0.9379 | 0.9816 | **TRÈS UNDERREPRESENTED — descriptif uniquement** |

Répartition cohérente avec le Lot 14.12 (58.7%/30.5%/4.8%/4.7%/1.3% sur son propre dataset — écart <1.5 point partout, deux fenêtres Yahoo voisines mais non identiques).

**Constat descriptif (régimes bien représentés seulement)** : `MeanReverting` (le seul régime qui compte pour le gate d'entrée) a une AmbiguityScore MOYENNE plus élevée (0.9571) et un écart-type plus FAIBLE (0.0180) que `StructuralBreak` (0.9357, stdDev 0.0517) — c'est-à-dire que le régime le plus important pour le trading est aussi celui où l'ambiguïté est la plus systématiquement haute ET la plus stable (moins de variabilité d'une barre à l'autre). Cela est cohérent avec la cause structurelle §13/§15 : `MeanRevertingRule` a le concurrent le plus corrélé (`StableRangeRule`) parmi tous les régimes.

`Transitional`/`Unknown` : absents du dataset (0 barre) — aucune conclusion inventée (Lot 14.12, inchangé).

---

# 18. BUY/SELL ANALYSIS

Barres à Winner=MeanReverting uniquement (seul régime produisant une Direction) :

| Direction | n | AmbiguityScore mean | stdDev | min | max |
|---|---|---|---|---|---|
| BUY_CANDIDATE | 1 189 | 0.9392 | 0.0073 | 0.9141 | 0.9500 |
| SELL_CANDIDATE | 1 158 | 0.9392 | 0.0069 | 0.9141 | 0.9500 |
| NO_ACTION | 4 175 | 0.9672 | 0.0140 | 0.9500 | 1.0000 |

**Compression parfaitement SYMÉTRIQUE entre BUY et SELL** — moyennes identiques à 4 décimales (0.9392), écarts-types quasi identiques (0.0073 vs 0.0069). Aucun signe d'asymétrie directionnelle dans le mécanisme de compression lui-même (contraste avec l'asymétrie économique observée au Lot 14.13, qui provient d'ailleurs dans le pipeline — Execution/PnL, pas de la Decision/Fusion). Le plafond exact à 0.9500 pour BUY/SELL et le plancher exact à 0.9500 pour NO_ACTION sont mécaniques : ce run utilise le seuil de production 0.95, donc par construction seule une barre avec `AmbiguityScore < 0.95` peut devenir BUY/SELL.

---

# 19. TEMPORAL ANALYSIS

```
Bars with AmbiguityScore>=0.95: runs=520, longestRun=105 bars (~8.75h de M5 consécutives)
Bars with AmbiguityScore<0.95:  runs=520, longestRun=71 bars (~5.9h de M5 consécutives)
```

**Clustering réel confirmé** — l'AmbiguityScore n'alterne pas aléatoirement bar à bar : il forme des ÉPISODES de plusieurs heures au-dessus ou en dessous du seuil de production, cohérent avec l'autocorrélation naturelle des tests statistiques à fenêtre glissante (ADF/KPSS/DFA changent lentement, §Compression Trace) et le lissage EMA/hystérésis (§12). 520 runs de chaque côté sur 10 930 barres donnent une longueur de run moyenne d'environ 10.5 barres (~52 minutes) — mais avec une queue longue (jusqu'à 105 barres), signe d'une dynamique non uniforme, pas d'un simple bruit blanc filtré. Aucune tentative de corréler ces épisodes à un calendrier de marché (heures de session, etc.) — hors scope, non demandé explicitement au-delà du clustering brut.

---

# 20. THRESHOLD RELATIONSHIP (reproduction du Lot 14.13, sans re-calibration)

Reconstruit directement à partir de la distribution `AmbiguityScore` de CE lot (barres MeanReverting uniquement, n=6 522), sans ré-exécuter le pipeline complet par seuil :

| θ | Accepté (cette lecture) | Lot 14.13 (rappel, dataset voisin) |
|---|---|---|
| 0.80 | 0.00% | 0.00% |
| 0.85 | 0.00% | 0.00% |
| 0.90 | 0.00% | 0.00% |
| 0.925 | 1.10% | 0.82% |
| 0.95 | 35.99% | 37.32% |
| 0.975 | 82.78% | 82.58% |
| 0.99 | 92.78% | 93.38% |

**Concordance quasi-exacte** avec le Lot 14.13 (écarts <1.5 point partout, dataset Yahoo voisin mais non identique — rolling window) — confirme que la courbe d'acceptation mesurée au Lot 14.13 est un reflet DIRECT et FIDÈLE de la distribution `AmbiguityScore` tracée dans ce lot, pas un artefact d'une autre couche du pipeline (Execution/Measurement/Risk). Le minimum `AmbiguityScore` observé pour `MeanReverting` (0.9141, §17) explique exactement pourquoi θ=0.90 n'accepte encore aucun candidat (0.90 < 0.9141) alors que θ=0.925 en accepte déjà quelques-uns.

---

# 21. ROOT CAUSE CLASSIFICATION

| Catégorie | Verdict | Preuve |
|---|---|---|
| A — Formula Issue | **NON confirmée comme cause primaire** | Formule simple, bornée, testée synthétiquement — réagit correctement (baisse) à une vraie séparation d'entrée (§22) |
| B — Score Distribution Issue | **CONFIRMÉE, secondaire** | 5 FinalScore confinés à [0.25, 0.77], stdDev 0.04-0.09 (§9) |
| **C — Weighting Issue** | **CONFIRMÉE, PRIMAIRE** | StableRange↔MeanReverting Corr=0.9652 (§15), traçable directement aux poids partagés (§13) |
| D — Normalization Issue | **Contributeur mineur, pas primaire** | EMA/hystérésis réduit le bruit mais ne cause pas la corrélation inter-règles (§12) |
| E — Dataset Issue | **NON EXCLUE, NON CONFIRMÉE** | Une seule fenêtre de 59 jours testée (règle du brief) — impossible de savoir si une autre période produirait une distribution Fusion différente |
| F — Regime Issue | **PARTIELLEMENT pertinente** | MeanReverting (régime clé) a l'AmbiguityScore le plus stable/élevé de tous les régimes bien représentés (§17), mais la compression touche TOUS les régimes, pas un seul |
| **G — Implementation Issue** | **CONFIRMÉE (mineure, P3)** | `if (finalScore > builder.Confidence)` vestigial (§12) ; parsing de texte fragile (§Défauts D14.14-2) |
| **H — Semantic Issue** | **CONFIRMÉE** | Cas à candidat unique (§5/§22) ; le nom "Ambiguity" suggère une incertitude générale, la formule mesure une marge au concurrent le plus proche (§6) |
| I — Unknown | Non retenue | Suffisamment de preuves directes obtenues (C et H comme causes primaires) |

**Synthèse** : la cause primaire est **C (Weighting Issue)**, avec **B (Score Distribution)** comme contributeur secondaire et **H (Semantic Issue)** comme défaut de nommage/interprétation indépendant mais aggravant (une métrique mal nommée rend le symptôme C plus facile à mal interpréter comme "le marché est structurellement ambigu" plutôt que "deux fonctions de score se chevauchent").

---

# 22. SYNTHETIC TESTS

Fichier : `Tests/Decision/AmbiguityScoreSemanticSyntheticTests.cs` (+ wrapper xUnit) — 7 cas, tous PASS, aucune formule de production modifiée pour les faire passer :

1. `SingleCandidate_HighScore_ProducesLowAmbiguity` — WinnerScore=0.90 seul ⇒ AmbiguityScore=0.10.
2. `SingleCandidate_LowScore_ProducesHighAmbiguity` — WinnerScore=0.10 seul ⇒ AmbiguityScore=0.90.
3. `IdenticalScores_ProduceExactMaximumAmbiguity` — deux candidats à 0.65 ⇒ Difference=0.0 EXACT, AmbiguityScore=1.0 EXACT.
4. `ExtremeScores_NoNaNNoInfinity_ClampHoldsAtBoundaries` — FinalScore=5.0/-3.0 ⇒ AmbiguityScore=0.0 (pas NaN) ; FinalScore=0.0 seul ⇒ AmbiguityScore=1.0.
5. `RealisticNearNeutralFusion_UsingRealDecisionRules_ProducesHighAmbiguity` — Fusion synthétique proche-neutre à travers les 5 VRAIES règles ⇒ AmbiguityScore=0.9856, 5 candidats déclenchés.
6. `JointlyExtremeSharedDimensions_UsingRealDecisionRules_DoesNotReduceAmbiguity` — **résultat inattendu, documenté tel quel** : pousser Stationarity+MeanReversion CONJOINTEMENT vers des valeurs "décisives" (0.92/0.90) ne réduit PAS l'ambiguïté (0.9856→0.9919, légère HAUSSE) — parce que StableRangeRule et MeanRevertingRule montent ENSEMBLE (même mécanisme que §15, reproduit avec des vraies classes de règles).
7. `DivergingOnPersistence_UsingRealDecisionRules_DoesReduceAmbiguity` — diverger SPÉCIFIQUEMENT sur `Persistence` (dimension que `StableRangeRule` ignore mais que `MeanRevertingRule` pénalise) sépare effectivement Winner et RunnerUp (AmbiguityScore chute à ~0.87) — confirme que la séparation EST possible, mais seulement en ciblant une dimension où les poids des deux règles DIVERGENT, pas en intensifiant les dimensions qu'elles PARTAGENT.

Le test #6 a d'abord été écrit avec l'hypothèse inverse (attendant une baisse d'ambiguïté) et a ÉCHOUÉ à l'exécution — conservé et corrigé pour documenter le résultat réel plutôt que rejeté, conformément à la discipline du brief (§22 : observer, pas décider ce qui devrait se passer).

Invariants supplémentaires vérifiés sur les 10 930 barres réelles (test réseau, §Framework Properties) : `AmbiguityScore ∈ [0,1]` toujours, jamais NaN/Infinity, présence de RunnerUp cohérente avec `CandidateCount`, déterminisme du rejeu Fusion (0 divergence sur 10 930 barres re-calculées indépendamment).

---

# 23. DEFECT TABLE

| ID | Priorité | Constat | Impact |
|---|---|---|---|
| D14.14-1 | **P1** | `StableRangeRule`/`MeanRevertingRule` quasi-colinéaires (Corr=0.9652) par partage de poids sur Stationarity+MeanReversion | Cause primaire de la compression d'AmbiguityScore pour le régime MeanReverting (le seul qui compte pour le trading) |
| D14.14-2 | P2 | `DecisionEngine.TryCreateCandidate` extrait les scores par PARSING DE TEXTE (`"Scientific Score : "`/`"Quality Score : "`) sur `Explanation`, jamais via un champ structuré | Fragile — tout changement de formatage d'`Explanation` dans une des 5 règles casserait silencieusement la création de candidat, sans erreur de compilation |
| D14.14-3 | P3 | `if (finalScore > builder.Confidence)` vestigial dans les 5 `IDecisionRule` (builder toujours frais, Confidence toujours 0.0 au moment du test) | Aucun (confirmé 100% des barres produisent 5 candidats) — lisibilité/maintenabilité uniquement |
| D14.14-4 | P2 | `FusionConfiguration` entièrement dead code (0 consommateur runtime, confirmé par grep exhaustif) | Aucun aujourd'hui — risque de confusion pour un futur développeur pensant ce type actif |
| D14.14-5 | P3 | `StructuralStabilityRule` suit un chemin de câblage différent (profil temporel via `FusionStateManager`) des 4 autres dimensions (evidence par barre via `EvidenceFusionEngine`), non documenté comme asymétrie architecturale explicite | Piège de lecture pour un futur mainteneur (le `FusionDimension` enum à 5 valeurs plates suggère une homogénéité qui n'existe pas) |
| D14.14-6 | P1 | Écart sémantique entre le nom "AmbiguityScore" et ce que la formule mesure réellement (marge au concurrent le plus proche, pas incertitude générale) — le cas à candidat unique l'illustre nettement | Risque d'interprétation erronée dans toute future décision de calibration s'appuyant sur ce nom sans relire la formule |

**Aucun défaut classé P0** — rien ne casse le pipeline, ne produit de résultat incorrect au sens strict, ni ne viole une garantie déjà testée.

---

# 24. LIMITATIONS

- Un seul dataset (MES=F/M5/59 jours, cette exécution) — catégorie E (Dataset Issue) ni confirmée ni exclue.
- Aucune p-value/intervalle de confiance calculé (brief §15 respecté) — toutes les statistiques sont descriptives.
- Le rejeu Fusion (§4) reproduit `StructuralStability` bit-identique mais n'a pas été vérifié bit-identique sur LES 4 AUTRES dimensions individuellement (seule `StructuralStability`, la plus complexe à rejouer correctement à cause de son état temporel, a été explicitement vérifiée en déterminisme — les 4 autres sont des fonctions pures du même `EvidenceSet` déjà validé Lot 14.9, donc leur déterminisme est hérité, pas re-testé ici).
- `Corr(OverallConfidence, AmbiguityScore)=0.2670` est un effet de confounding par régime, jamais formellement isolé par une régression partielle (hors scope, pas demandé par le brief à ce niveau de détail).
- Aucun calendrier de marché (sessions CME) n'a été croisé avec le clustering temporel (§19) — la classification "épisode" reste brute, sans interprétation de cause.
- La classification "P1/P2/P3" est un jugement qualitatif de ce lot, pas une méthode formelle de scoring de risque.

---

# 25. RECOMMENDED FUTURE LOT

**Lot proposé** : investigation ciblée du chevauchement de poids `StableRangeRule`/`MeanRevertingRule` (D14.14-1) — PAS une recalibration des poids (interdite ici), mais une étude structurelle : que produirait un ré-agencement des poids qui différencie mieux les deux régimes sur une dimension où ils divergent aujourd'hui (ex. `Persistence`, actuellement absent de `StableRangeRule` — §22 test #7 le démontre déjà synthétiquement) ? Un tel lot devrait rester dans le même esprit d'INVESTIGATION avant toute modification de production, avec approbation explicite avant de toucher `Engine/Decision/Rules/*.cs` (fichiers non protégés par défaut mais visiblement sensibles).

**Alternative** : corriger D14.14-2 (fragilité du parsing de texte) et D14.14-3 (comparaison vestigiale) comme un lot de nettoyage à faible risque, indépendant de toute question de calibration — ces deux défauts n'affectent aucun résultat scientifique observé mais fragilisent la base de code pour tout futur lot qui toucherait aux règles.

**Ne pas encore lancer** : toute calibration des poids Decision/Fusion tant que D14.14-1/D14.14-6 ne sont pas explicitement pris en compte dans la méthodologie (calibrer des poids sans d'abord traiter leur chevauchement structurel reviendrait à optimiser un système dont deux composantes bougent ensemble par construction).

---

# 26. TESTS

## Nouveaux fichiers

- `Tests/Decision/AmbiguityScoreSemanticSyntheticTests.cs` + `Tests/XunitWrappers/AmbiguityScoreSemanticSyntheticXunitTests.cs` — 7 cas synthétiques, PASS (~5s, aucun réseau).
- `Tests/Backtest/Calibration/DecisionFusionAmbiguityInvestigationLot1414Tests.cs` — 1 test réseau, PASS (3m4s), rejoue `EvidenceFusionEngine`/`FusionStateManager` réels sur les 10 930 barres du pipeline réel unique (`RunSignalPipeline`, non modifié), calcule toutes les statistiques de ce rapport.

## Discipline anti-duplication

`DecisionArbitrationTests.cs` (existant) couvrait déjà : gagnant clair, candidats proches, scores faibles, zéro candidat — non reproduits ici. `MissingEvidenceSemanticsTests.cs` (existant, Sprint 14) couvrait déjà la sentinelle "Missing Evidence" et sa survie au lissage `FusionStateManager` — non reproduit ici, seulement cité (§10 : confirmé 0 occurrence sur ce dataset réel).

## Vérification technique

```
dotnet build IQIAIndicator.csproj -c Debug    → PASS, 0 avertissement, 0 erreur
dotnet build IQIAIndicator.csproj -c Release  → (voir FINAL OUTPUT)
dotnet test (AmbiguityScoreSemanticSyntheticXunitTests) → PASS, 1/1, 4.8s
dotnet test (DecisionFusionAmbiguityInvestigationLot1414Tests) → PASS, 1/1, 3m4s (réseau réel)
```

Aucun fichier de production modifié — seuls 4 fichiers de test ajoutés. Aucune régression possible ailleurs dans la suite existante (même raisonnement que Lot 14.12/14.13 : rien d'autre n'a changé).

---

# 27. HANDOFF CONTEXT — NEXT SESSION

```
PROJECT:
IQIA

CURRENT LOT:
14.14

LAST COMPLETED:
14.13

DATASET:
MES=F
M5
59 days
11058 bars (this session's own download - fingerprint CFEEFB0CA469D2956EA057AAC2BB5EBC779C7AAC995D97ACA6E8E2CD4C8C9B19,
range 2026-06-26T01:15Z .. 2026-08-24T01:07:17Z - rolling window, not bit-identical to Lot 14.13's own capture)

AMBIGUITY BASELINE:
0.95

LOT 14.13 RESULT:
AmbiguityScore concentrated near 1.0, steep signal-count transition, relatively stable per-position
quality from 0.95 to 0.99, no justified final threshold selection.

CURRENT QUESTION:
Why is AmbiguityScore concentrated near 1.0? ANSWERED.

ROOT CAUSE:
PRIMARY = C (Weighting Issue): StableRangeRule and MeanRevertingRule share ~70-80% of their scientific
weight on the same two Fusion dimensions (Stationarity, MeanReversion), producing FinalScore correlation
of 0.9652 measured empirically on 10930 real bars. Since MeanReverting wins 59.7% of bars and StableRange
is its structurally closest (but almost never winning, 1.1%) competitor, Difference stays mechanically
small. SECONDARY = B (Score Distribution): all 5 rule FinalScores confined to [0.25,0.77], never near [0,1]
extremes. INDEPENDENT contributing factor = H (Semantic Issue): the metric measures margin-to-nearest-
competitor, not general regime uncertainty - starkly visible in the single-candidate edge case.

FORMULA:
AmbiguityScore = Clamp(1 - (WinnerScore - RunnerUpScore), 0, 1), RunnerUpScore defaults to 0.0 (never
null-propagated) when <2 candidates exist. Verified correct and bounded via 7 synthetic tests - not the
cause of the compression.

FUSION:
4 real IFusionRule wired in production (Stationarity/Persistence/MeanReversion/RandomWalk) +
StructuralStabilityRule via a SEPARATE mechanism (FusionStateManager + FusionProfileAnalyzer, temporal
profile over a 6-snapshot window) - StructuralStabilityRule does NOT implement IFusionRule. Measured
distributions: Persistence narrowest (P25-P75=[0.133,0.148]), MeanReversion widest/highest (mean 0.558),
StructuralStability moderate (mean 0.6447, NOT pinned near 1.0 as code-reading alone suggested -
hypothesis tested and invalidated empirically).

DECISION:
5 IDecisionRule always ALL produce a candidate (100% of 10930 bars, CandidateCount=5 always) due to a
vestigial per-rule "finalScore > builder.Confidence" comparison against a builder that is always fresh
(Confidence=0.0) - confirmed root cause at the code level (DecisionEngine.Evaluate creates a new builder
per rule iteration). No behavioural impact (each rule only ever proposes one hypothesis anyway).

OVERALL CONFIDENCE:
Confirmed empirically as a near-perfect binary indicator on this dataset: EXACTLY 1.0 (stdDev=0.0) when
Winner=MeanReverting (n=6522), EXACTLY 0.0 (stdDev=0.0) otherwise (n=4408) - because only
MeanReversionMethodology gets real ScientificModelRegistry models (ARC-003, confirmed by code read), and
those 5 models apparently always succeed together when they run (never a partial 3/5 or 4/5 on this
dataset). Structurally independent of AmbiguityScore (disjoint code paths); Corr=0.2670 is regime
confounding, not causal.

WINNER:
orderedCandidates[0] after descending sort by FinalScore; ties broken silently by IDecisionRule[] array
order (LINQ OrderByDescending is stable) - never observed exactly tied on this real dataset.

RUNNER-UP:
Candidates[1] if CandidateCount>1, else defaults to score 0.0 (never null-propagated) - always present in
practice here (CandidateCount=5 always) but the sentinel-0.0 code path is real and untested on this
dataset (0-1 candidate scenarios never occurred).

DIFFERENCE:
Bounded [0.0000, 0.2671] across ALL 10930 bars, all regimes - AmbiguityScore can never fall below 0.7329
on this dataset, matching the observed minimum exactly.

NORMALIZATION:
No single normalization step mechanically forces AmbiguityScore->1 or Difference->0 unconditionally.
EMA(alpha=0.20)+hysteresis(0.03) in FusionStateManager reduces bar-to-bar noise but does not itself cause
inter-rule correlation - weight sharing (§13 of report) is the dominant mechanism.

WEIGHTS:
Fusion and Decision weights are ALL hardcoded private consts, zero external configuration surface.
FusionConfiguration record exists but is 100% dead code (zero runtime consumer, confirmed by exhaustive
grep). No DecisionConfiguration type exists at all - Decision weights are even more deeply unconfigurable
than Fusion weights.

REGIME EFFECT:
MeanReverting (59.7%, n=6522): AmbiguityScore mean=0.9571, stdDev=0.0180 (most compressed AND most stable
of the well-represented regimes). StructuralBreak (30.7%, n=3357): mean=0.9357, stdDev=0.0517 (more
spread). Trending/RandomWalk/StableRange underrepresented (n=448/483/120) - no strong conclusion drawn.

BUY/SELL:
Compression perfectly symmetric: BUY mean=0.9392 (n=1189), SELL mean=0.9392 (n=1158), stdDev within
0.0004 of each other. No directional asymmetry in the compression mechanism itself.

PRODUCTION PARAMETERS:
UNCHANGED

CALIBRATION:
NOT PERFORMED

KNOWN DEFECTS:
D14.14-1 (P1): StableRangeRule/MeanRevertingRule near-collinear (Corr=0.9652) via shared Stationarity+
MeanReversion weight - primary compression driver. D14.14-2 (P2): DecisionEngine.TryCreateCandidate
extracts scores via fragile text-parsing of Explanation strings, not structured fields. D14.14-3 (P3):
vestigial "finalScore > builder.Confidence" self-comparison in all 5 IDecisionRule (no behavioural
impact, confirmed). D14.14-4 (P2): FusionConfiguration fully dead code. D14.14-5 (P3): StructuralStability
wired via an architecturally different, undocumented path than the other 4 dimensions. D14.14-6 (P1):
"AmbiguityScore" name implies general regime uncertainty; formula measures margin-to-nearest-competitor
specifically - starkly visible in the single-candidate edge case.

NEXT RECOMMENDED LOT:
A structural investigation lot (not calibration) into re-weighting StableRangeRule vs MeanRevertingRule to
reduce their shared-dimension collinearity (D14.14-1) - or, lower-risk, a cleanup lot fixing D14.14-2/
D14.14-3 independent of any calibration question.

WHY:
This lot's own evidence (§13/§15/§22) shows that calibrating AmbiguityGateThreshold or Decision/Fusion
weights without first addressing the StableRange/MeanReverting collinearity would optimize a system whose
two dominant competing rules move together by construction - any calibration result would be confounded by
this structural property, not a clean signal.

DO NOT DO:
Do NOT modify Engine/Decision/Rules/*.cs, Engine/Fusion/Rules/*.cs, Engine/Fusion/State/FusionStateManager.cs,
Engine/Decision/Arbitration/DecisionArbitrator.cs, Engine/Decision/Core/DecisionEngine.cs, or
EntryTriggerBuilder.cs without explicit approval - this lot is investigation-only, nothing here was
changed. Do NOT calibrate Fusion/Decision weights, AmbiguityGateThreshold, or OverallConfidence based on
this report alone - it explains a mechanism, it does not select any new value. Do NOT interpret D14.14-3
(vestigial comparison) as safe to silently "fix" without re-verifying CandidateCount stays at 5 for every
regime combination - confirmed only on THIS dataset. Do NOT assume StructuralStability's measured
0.5357-0.7951 range generalizes to other market periods (category E, dataset issue, neither confirmed nor
excluded).
```

**STOP.**
