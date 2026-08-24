# QDE-012 — Sprint 15.25 — Lot 14.11 — Calibration Experiment Design & Parameter Dependency Validation

**Date** : 2026-08-23
**Branche** : `feature/structural-stability-v2`
**Lots précédents** : Lot 14.9 (audit scientifique global), Lot 14.10 (correction P0-1/P0-2/P0-3)
**Statut** : **IMPLEMENTED** (protocole documenté ; aucune calibration exécutée)
**Portée** : construire le PROTOCOLE de calibration scientifique — inventaire, classification, dépendances, ordre, fonction objectif, méthodologie TRAIN/VALIDATION/TEST/OOS, contrôles anti-overfitting, analyse de sensibilité, conditionnement par régime/direction/instrument. **Aucune valeur de production n'a été modifiée. Aucune optimisation n'a été exécutée. Aucun paramètre gagnant n'a été sélectionné.**

---

# 1. EXECUTIVE SUMMARY

Ce lot ne calibre rien. Il produit la méthodologie qui permettra à un futur lot de calibrer scientifiquement, dans le bon ordre, sans re-découvrir les dépendances ni retomber dans les pièges déjà identifiés (overfitting, data snooping, contamination OOS, comparaisons multiples).

**Constats principaux** :
- Le binding `CalibrationParameterSet → CalibrationParameterBinding → PipelineParameterOverrides → pipeline → résultat` (Lot 14.10) est audité et confirmé fonctionnel jusqu'au consommateur réel, pour l'unique paramètre aujourd'hui câblé (`AmbiguityGateThreshold`) — voir §1 du corps du rapport pour le détail ligne par ligne.
- L'inventaire des paramètres (repris et mis à jour depuis le Lot 14.9) ne révèle aucun paramètre supplémentaire devenu injectable depuis le Lot 14.10 — un seul (`AmbiguityGateThreshold`) l'est.
- La matrice de dépendances confirme 8 couplages structurels réels (§6), dont deux nouveaux non identifiés explicitement au Lot 14.9 : le couplage `Fingerprint ↔ ParameterSet déclaré` (un paramètre déclaré mais non câblé change quand même le fingerprint — l'illusion de calibration reste possible pour tout paramètre autre que `AmbiguityGateThreshold`) et le couplage `Fenêtrage TRAIN/VALIDATION/OOS ↔ convention d'entrée` (déjà documenté au Lot 14.10 §14, formalisé ici comme règle méthodologique permanente, §11.4).
- La séparation TRAIN/VALIDATION/TEST/OOS à 4 niveaux demandée par ce lot est **atteignable dès aujourd'hui sans aucune modification de code** — `CalibrationWindowRole` reste un enum fermé à 3 valeurs (Train/Validation/Oos), mais deux `CalibrationWindowSet` construits avec des dates disjointes suffisent à représenter un TEST distinct de la VALIDATION et de l'OOS (§9.3). Le Walk-Forward est de même atteignable par répétition de ce mécanisme, sans nouveau code (§12).
- La séparation BUY/SELL/ALL est **déjà entièrement supportée** par le framework existant (`CalibrationDirectionFilter`, Lot 14.9) — aucun travail supplémentaire requis (§16).
- La performance conditionnée par régime (Mean-Reverting/Trending/Neutral/Transition) reste **structurellement impossible** aujourd'hui : `Decision.Winner` n'est jamais propagé jusqu'à `PositionRiskOutcome`/`MeasurementResult` — défaut déjà identifié au Lot 14.9, reclassé ici P1 bloquant spécifiquement pour §15 (voir §25 Defect Table).
- 5 défauts/limitations sont documentés (P1×2, P2×2, P3×1) — **aucun n'a été corrigé** dans ce lot.

**Vérification technique** : aucun changement de code de production. Debug/Release build inchangés depuis le Lot 14.10 (revérifiés §26). Aucun test existant modifié. Aucun nouveau test de production requis — l'audit (§1, §19, §20) a confirmé que les invariants de protocole demandés (fingerprint, sérialisation, isolation OOS, déterminisme, bornes TRAIN/VALIDATION) sont déjà couverts par les suites de tests des Lots 14.9/14.10 ; le détail de cette couverture, fichier par fichier, est donné en §26 plutôt que dupliqué par de nouveaux tests qui n'ajouteraient aucune garantie supplémentaire.

---

# 2. LOT 14.9 FINDINGS USED

Repris tels quels du rapport d'audit global (`QDE-012_Sprint_15.25_Lot14.9_Global_Calibration_Audit_Report.md`) :
- Cartographie complète du pipeline (§2 de ce rapport-là) et inventaire des paramètres (§3).
- Classification scientifique Type A-E (§4) — aucun paramètre n'a changé de catégorie depuis (rien n'a été calibré).
- Table des défauts structurels (§6), notamment D4 (absence de binding paramètre→pipeline, résolu au Lot 14.10 pour `AmbiguityGateThreshold` uniquement) et le constat "regime breakdown DEFERRED" repris ici en §15/§25.
- Graphe de dépendance des paramètres (§19) — base de la matrice étendue au §6 de ce rapport.
- Verdict de suffisance des données (§17) : 45 jours insuffisants — repris intégralement au §10.

# 3. LOT 14.10 FINDINGS USED

Repris de `QDE-012_Sprint_15.25_Lot14.10_Backtest_Realism_Calibration_Readiness_Report.md` :
- P0-1 corrigé : fill à `Open[SignalBarIndex+1]`, jamais `Close[SignalBarIndex]` — la convention d'entrée pour toute future mesure de MFE/MAE/Return économique doit être `Open[i+1]`, pas `Close[i]` (§18/§19 de ce rapport-ci en tiennent compte).
- P0-2 confirmé fonctionnel : `NetPnL != GrossPnL` avec coûts activés, prouvé à travers `RunFullBacktestWithRisk`.
- P0-3 : `AmbiguityGateThreshold` réellement injectable — seul paramètre de production aujourd'hui câblé à `CalibrationParameterSet`.
- Effet de bord documenté (§14 du Lot 14.10) : `EntryTimestamp` (nouvelle base de fenêtrage des positions) peut différer de `SignalTimestamp` (base de fenêtrage des signaux) d'une barre — formalisé ici comme règle méthodologique permanente (§11.4, §21).

---

# 4. PARAMETER INVENTORY

Repris et vérifié à nouveau par lecture directe du code (aucune dérive depuis le Lot 14.9 — aucun de ces fichiers n'a été modifié par le Lot 14.10, à l'exception d'`EntryTriggerBuilder.cs` dont la valeur 0.95 est confirmée inchangée).

| Parameter | Module | Current Value | Unit | Injectable | Calibratable | Dependency | Priority |
|---|---|---|---|---|---|---|---|
| ADF window / min sample | Regime | 60 / 30 | bars | No | Yes (once P1 blockers cleared) | Fusion weights, timeframe M5 | HIGH |
| KPSS window / min sample | Regime | 60 / 30 | bars | No | Yes | idem | HIGH |
| DFA window / min sample | Regime | 128 / 80 | bars | No | Yes | idem | HIGH |
| Half-Life window / min sample | Regime | 30 / 20 | bars | No | Yes | idem | HIGH |
| Variance Ratio window / min sample | Regime | 30 / 20 | bars | No | Yes | idem | HIGH |
| CUSUM window / min sample | Regime | 30 / 20 | bars | No | Yes | idem | HIGH |
| Bai-Perron window / min sample | Regime | 128 / 64 | bars | No | Unknown (Bai-Perron non audité en détail) | idem | MEDIUM |
| Multi-window / consensus logic | Regime | N'existe pas | — | N/A | N/A | — | N/A (confirmé absent, Lot 14.9) |
| Fusion weights (5 règles, Scientific/Quality) | Fusion | 0.80-0.90 / 0.10-0.20 | ratio | No | Yes (couplé à Decision) | Decision weights, Regime windows | HIGH |
| `FusionConfiguration.EnableCalibration` | Fusion | `false` | bool | No | N/A (interrupteur, pas une valeur à calibrer) | Fusion weights | — |
| Consensus / stability / dominant-window | Fusion | N'existe pas | — | N/A | N/A | — | N/A (confirmé absent, Lot 14.9) |
| `MissingEvidence` handling (`NeutralMissingEvidenceValue`) | Fusion/Decision | 0.5 | score | No | N/A (correctif de bug documenté, pas un paramètre à optimiser) | — | — |
| Decision weights (5 règles, "Provisional") | Decision | voir Lot 14.9 §9 | ratio | No | Yes (couplé à Fusion et à `AmbiguityGateThreshold`) | Fusion weights, `AmbiguityGateThreshold` | HIGH |
| `AmbiguityGateThreshold` | EntryTrigger | 0.95 | score | **Yes (Lot 14.10)** | Yes (revalidation, pas une nouvelle sélection) | Decision weights, Fusion weights, Costs | CRITICAL |
| `OverallConfidence` (sémantique) | Signal | `SuccessfulModels/5` | ratio | No | N/A (renommer/documenter avant toute calibration dessus) | Entry/EntryTrigger thresholds | HIGH |
| Entry `OpportunityStatus` thresholds | Entry | 0.9 / 0.6 / 0.3 | ratio | No | Yes (après correction sémantique d'`OverallConfidence`) | `OverallConfidence` | HIGH |
| EntryTrigger READY thresholds | EntryTrigger | 0.70 / 0.55 | ratio | No | Yes | idem | HIGH |
| DynamicZScore direction gate | EntryTrigger | sign(DynamicZScore) | — | No | N/A (règle de signe, pas un seuil numérique) | — | — |
| TradePlan SL/TP | TradePlan | N'existe pas en production | — | N/A | N/A (aucune méthodologie SL) | Risk, MFE/MAE | — |
| `RiskDistanceConfiguration` | Backtest Risk | `None()` par défaut ; fixture 2-4 ticks en test | points | No | Not calibratable yet (dépend d'une méthodologie SL) | StopLoss (inexistant), Risk sizing | CRITICAL (bloquant) |
| `RiskPolicy.*` (8 champs) | Risk | 0/0.0 partout (fail-closed) | %/$/ratio | No | Décision de valeur, pas un fit statistique | Stop distance, Drawdown, Equity | CRITICAL (bloquant) |
| `BacktestRiskConfiguration.MaxExposure` | Backtest Risk | `null` (illimité) ; fixture 20000-50000$ en test | $ | No | Not calibratable yet | Règle de marge réelle (non documentée) | MEDIUM |
| `MeasurementConfiguration.HorizonBars` | Measurement | 10 | bars | No | Yes | `ExecutionConfiguration.HorizonBars` (couplage structurel) | MEDIUM |
| `MeasurementConfiguration.HitThresholds` | Measurement | {0.001, 0.002} | ratio | No | Yes | idem | MEDIUM |
| `ExecutionConfiguration.HorizonBars` | Execution | 10 | bars | No | Yes | `MeasurementConfiguration.HorizonBars`, **désormais aussi la sémantique EntryTimestamp/SignalTimestamp (Lot 14.10)** | MEDIUM |
| Convention de fill | Execution | `Open[SignalBarIndex+1]` | — | N/A (corrigé Lot 14.10, pas un paramètre calibrable — un choix de réalisme) | N/A | — | — (résolu) |
| `ExecutionCostConfiguration.Enabled` | Cost | `false` par défaut | bool | No (interrupteur, pas une valeur) | N/A | Signal frequency, `AmbiguityGateThreshold` | — |
| Slippage / Spread / Commission / Fees | Cost | 0 en production ; fixtures explicites en test (Lot 14.10) | pts/$ | No | Yes (barème broker réel requis) | Signal frequency | HIGH |
| `TickSize`/`TickValue`/`PointValue` (MES) | Instrument | 0.25/1.25/5 | pts/$ | No | N/A — **Type C, fait objectif** | — | — |
| `QuantityStep` (MES) | Instrument | Manuel uniquement | contrats | No | N/A — décision manuelle, pas une calibration scientifique | — | — |

**Aucun paramètre nouveau n'a été inventé** — cette table est un sur-ensemble strict de celle du Lot 14.9, avec deux colonnes mises à jour : `Injectable` (une seule entrée passe de No à Yes) et `Dependency` (deux entrées enrichies pour refléter le fenêtrage Lot 14.10).

---

# 5. PARAMETER CLASSIFICATION

Inchangée depuis le Lot 14.9 — aucun paramètre n'a été calibré, donc aucune reclassification empirique n'est possible.

| Type | Définition | Paramètres |
|---|---|---|
| **A** | Justifié scientifiquement + validé en externe | Formule Half-Life (scikit-learn, tol 1e-6/1e-4), formule Variance Ratio (NumPy, tol 1e-6) — ce sont des FORMULES, pas des seuils à calibrer |
| **B** | Justifié théoriquement, non calibré empiriquement | Formules ADF/KPSS/DFA (valeurs critiques citées, magnitude jamais comparée à une référence externe) |
| **C** | Constante d'ingénierie | `TickSize`/`TickValue`/`PointValue` (fait objectif CME), `DefaultMaxChunkSpanDays` (contrainte Yahoo mesurée) |
| **D** | Arbitraire / insuffisamment justifié | Fenêtres de régime (60/128/30...), poids Fusion/Decision, seuils Entry/EntryTrigger (0.9/0.6/0.3/0.70/0.55), `HorizonBars`/`HitThresholds`, coûts (valeurs de test) |
| **E** | Origine inconnue | Formule du seuil CUSUM (`h=σ√(2N ln N)`, recursion validée mais seuil non cité) |
| **A/B limite** | Calibration empirique réelle avec séparation OOS authentique, mais fonction objectif non économique | `AmbiguityGateThreshold` (Lots 4-9 : séparation OOS réelle, mais critère = "produire un candidat observable", pas "maximiser une performance nette de coûts") |

---

# 6. PARAMETER DEPENDENCY GRAPH

## Audit du binding existant (brief §1)

Vérification ligne par ligne de la chaîne `CalibrationParameterSet → CalibrationParameterBinding → PipelineParameterOverrides → pipeline IQIA → résultat`, pour l'unique paramètre aujourd'hui câblé :

| Étape | Fichier:ligne | Constat |
|---|---|---|
| Déclaration | `CalibrationParameter.Decimal("AmbiguityGateThreshold", X, "score", min:0, max:1)` | Type générique, borné, validé à la construction (rejet si hors [0,1], jamais clampé) |
| Résolution | `CalibrationParameterBinding.Resolve(parameterSet)` — `Backtest/Calibration/CalibrationParameterBinding.cs` | Lit exactement le nom `"AmbiguityGateThreshold"` via `TryGet`, jamais par réflexion. Retourne `null` si absent (fallback au défaut de production) |
| Point d'injection #1 | `BacktestEngine.RunFullBacktestWithRisk(..., PipelineParameterOverrides overrides)` — `Backtest/BacktestEngine.cs` | Nouvelle surcharge à 8 arguments ; la forme à 7 arguments délègue avec `PipelineParameterOverrides.None` |
| Point d'injection #2 | `BacktestEngine.RunSignalPipeline(..., overrides)` | `overrides.AmbiguityGateThreshold ?? EntryTriggerBuilder.AmbiguityGateThreshold` passé à `new SignalEngine(...)` |
| Point d'injection #3 | `SignalEngine(IPipelineTraceCollector?, double ambiguityGateThreshold)` — `Engine/Signal/SignalEngine.cs` | Construit `new EntryTriggerEngine(ambiguityGateThreshold)` |
| Point d'injection #4 | `EntryTriggerEngine(double)` — `Engine/EntryTrigger/EntryTriggerEngine.cs` | Construit `new EntryTriggerBuilder(ambiguityGateThreshold)` |
| Consommateur réel | `EntryTriggerBuilder.DetermineDirection(context, ambiguityGateThreshold, ...)` — `Engine/EntryTrigger/EntryTriggerBuilder.cs` | `decision.AmbiguityScore >= ambiguityGateThreshold` — effet observable direct sur `Direction` |
| Effet observable | `TwoCalibrationExperiments_DifferingOnlyByAmbiguityGateThreshold_ProduceDifferentPositionCounts_EndToEnd` (Lot 14.10) | Prouvé : deux seuils (0.0 vs 0.999) produisent des `PositionCount` réellement différents (0 vs >0), pas seulement des `ConfigurationFingerprint` différents |
| Isolation | `ExperimentA_ExperimentB_ExperimentA_...` (Lot 14.10, TEST 4) | Aucune fuite d'état entre deux expériences à seuils différents |
| Défaut préservé | `DefaultEntryTriggerBuilder_StillUsesProductionThreshold_0_95_Unchanged` (Lot 14.10, TEST 1) | Sans override, comportement bit-pour-bit identique à avant le Lot 14.10 |

**Verdict de l'audit §1 : le binding fonctionne réellement, jusqu'au consommateur, pas seulement au niveau du fingerprint.** Aucun défaut trouvé pour ce paramètre précis.

**Constat structurel confirmé (pas un défaut de ce paramètre, une propriété générale du framework)** : `CalibrationParameterBinding.Resolve` ne connaît QUE `"AmbiguityGateThreshold"`. Tout autre nom de paramètre placé dans un `CalibrationParameterSet` (ex. `"MeasurementHorizonBars"`, `"DecisionWeightMeanReverting"`) est accepté sans erreur par `CalibrationParameterSet.Create` (aucune validation de nom à ce niveau), change le `ConfigurationFingerprint` de l'expérience (puisque `CalibrationFingerprint.ComputeConfigurationFingerprint` hache le `ParameterSet` déclaré, pas les overrides résolus), mais n'a **strictement aucun effet** sur le pipeline. C'est exactement le piège que l'audit Lot 14.9 (Défaut D4) décrivait, et il reste entier pour tout paramètre autre que `AmbiguityGateThreshold` — voir §25 D14.11-1.

## Matrice de dépendances

| Paramètre A | Paramètre B | Nature de la dépendance | Preuve | Must Calibrate Together? |
|---|---|---|---|---|
| `AmbiguityGateThreshold` | Decision weights (5 règles) | Les poids déterminent `Winner`/`RunnerUp`/`Difference`, donc `AmbiguityScore = Clamp(1-Difference,0,1)` — changer les poids déplace la distribution de `AmbiguityScore` sans que le seuil lui-même change | `DecisionArbitrator.Arbitrate` (lecture directe), confirmé Lot 14.9 §11 | **Oui** — calibrer l'un sans l'autre rend la valeur du premier non interprétable |
| Decision weights | Fusion weights | `scientificScore` de chaque règle Decision est dérivé du score de Fusion en amont | `Engine/Decision/Rules/*.cs` lisent `FusionResult` | **Oui** — confounding direct si calibrés séparément via une seule grille |
| Fenêtres de régime (ADF/KPSS/DFA/HalfLife/VR/CUSUM) | Fusion weights | Les `EvidenceScore` que Fusion pondère sont eux-mêmes fonction des fenêtres — changer une fenêtre change la distribution du score que Fusion pondère | `RegimeEngine.Collect` → `EvidenceSet` → `FusionContext.Evidence` | **Oui, dans cet ordre** (fenêtres avant poids — voir §7) |
| Entry/EntryTrigger thresholds | `OverallConfidence` (définition) | Les seuils sont calibrés SUR ce ratio ; si sa définition change (ex. un jour d'autres méthodologies reçoivent de vrais modèles, changeant le dénominateur `/5`), tous les seuils avals changent de signification sans avoir eux-mêmes changé | `ScientificAssessmentBuilder.cs:100-102` (`SuccessfulModels/5`) | **Oui** — corriger la sémantique d'abord (§25 D14.9-legacy) |
| Entry threshold | `AmbiguityGateThreshold` | Les deux filtrent le même flux de candidats en série (Entry puis EntryTrigger) — un seuil Entry trop strict peut masquer l'effet d'un changement d'`AmbiguityGateThreshold` en aval, et inversement | Câblage du pipeline (`EntryEngine` → `EntryTriggerEngine`, séquentiel) | Non strictement couplés (séries indépendantes dans leur formule), mais **doivent être évalués ensemble** pour toute étude de sensibilité |
| StopLoss (futur) | Risk sizing | `RiskDistance` EST la distance de stop — pas de sizing par risque indépendant du choix de SL | `PositionRiskEvaluator.cs:83-85` (StopLoss dérivé de `RiskDistance`) | **Oui, absolument** |
| StopLoss (futur) | MFE / MAE | Le choix de la distance de stop doit dériver de la distribution MAE (où le stop aurait été touché) ET MFE (ce qui aurait été manqué) conjointement, jamais isolément (sinon métrique auto-réalisatrice) | Conceptuel — MAE/MFE existent déjà dans `MeasurementResult`, StopLoss n'existe pas encore | **Oui** |
| Execution horizon | Return (Measurement ET Execution) | `HorizonBars` définit littéralement la fenêtre sur laquelle Return/MFE/MAE sont mesurés — c'est une identité, pas une corrélation | `ScientificMeasurementEngine`/`ExecutionSimulator` (formule) | **Oui, par construction** |
| Costs | Signal frequency | Les coûts fixes (commission/fees par ordre) pénalisent disproportionnellement les stratégies à signaux fréquents/petits — un seuil calé sans coûts paraîtra artificiellement bon sur un ensemble de signaux fréquents | `PositionCostCalculator` (Commission = 2×PerOrder + ..., indépendant de la taille du mouvement) | **Oui** — ne jamais calibrer un seuil de fréquence sans coûts actifs |
| Risk (position sizing) | Drawdown | Une taille plus grande amplifie un même drawdown directionnel | `BacktestRiskResultBuilder` (NetPnL scale avec `AllowedQuantity`) | **Oui** — évaluer conjointement, jamais en fixant la taille pendant qu'on calibre l'entrée |
| **[Nouveau, ce lot]** `ConfigurationFingerprint` | `CalibrationParameterSet` déclaré (pas résolu) | Le fingerprint change avec tout paramètre DÉCLARÉ, même non câblé — voir constat structurel ci-dessus | `CalibrationFingerprint.ComputeConfigurationFingerprint` (lecture directe) | N/A — pas un couplage de calibration, un piège méthodologique à éviter (§25 D14.11-1) |
| **[Nouveau, ce lot]** Fenêtrage TRAIN/VALIDATION/OOS | Convention d'entrée (`EntryTimestamp` = fill bar) | Une position peut être comptée dans la fenêtre suivant celle de son signal si celui-ci tombe sur la dernière barre d'une fenêtre | Lot 14.10 §14, `CalibrationExperimentRunner.BuildSlice` (filtre `PositionCount` par `EntryTimestamp`, `SignalCount` par `SignalTimestamp`) | N/A — règle méthodologique permanente, voir §11.4 |

**Paramètres NE DEVANT PAS être optimisés ensemble via une seule grille non structurée** (risque de confounding sans méthode de contrôle) : poids Fusion et poids Decision (11 dimensions combinées) ; fenêtres de régime (7 fenêtres) et poids Fusion simultanément ; `AmbiguityGateThreshold` et poids Decision simultanément sans fixer l'un pendant qu'on balaie l'autre. La méthode recommandée (§7) est une calibration séquentielle en couches, jamais un grid search global sur toutes les dimensions à la fois.

---

# 7. CALIBRATION ORDER

Déduit du graphe de dépendance ci-dessus, PAS supposé a priori (le brief demande explicitement de ne pas reprendre aveuglément l'ordre du pipeline).

```
1. Fenêtres de régime (ADF/KPSS/DFA/HalfLife/VR/CUSUM)
        — en amont de tout : Fusion pondère leur sortie
        ↓
2. Poids Fusion
        — dépend de (1) ; produit le score que Decision pondère à son tour
        ↓
3. Poids Decision
        — dépend de (2) ; produit Winner/RunnerUp/Difference/AmbiguityScore
        ↓
4. AmbiguityGateThreshold (REVALIDATION, pas une nouvelle sélection — Lots 4-9 déjà faits)
        — dépend de (3) : la distribution de AmbiguityScore que ce seuil filtre dépend des poids Decision
        ↓
5. Correction sémantique d'OverallConfidence (préalable, pas une calibration)
        — bloque toute interprétation valide de (6)
        ↓
6. Seuils Entry/EntryTrigger (0.9/0.6/0.3/0.70/0.55)
        — dépend de (5)
        ↓
7. HorizonBars/HitThresholds (Measurement+Execution, couplés entre eux par construction)
        — indépendant de (1)-(6) dans sa formule, mais doit être fixé avant (8)/(9) pour que
          Return/MFE/MAE aient un sens stable
        ↓
8. StopLoss (méthodologie à construire séparément — hors scope calibration de seuils)
        — dépend de (7) : MAE/MFE mesurés sur l'horizon fixé
        ↓
9. Risk (RiskDistance dérivée de (8), RiskPolicy — décision, pas un fit)
        ↓
10. Costs (barème réel — fait de marché, pas un fit statistique, mais DOIT être actif avant (11))
        ↓
11. Walk-Forward / validation OOS globale sur la chaîne complète (1)→(10)
```

**Justification de l'ordre** : chaque étage n dépend structurellement de la sortie de l'étage n-1 (voir preuves §6) — calibrer dans le désordre revient à ajuster un paramètre sur une distribution d'entrée qui va elle-même changer une fois l'étage amont calibré, invalidant le résultat. L'étape 5 (correction sémantique) n'est pas une calibration mais un préalable OBLIGATOIRE avant 6, car calibrer des seuils sur une métrique mal nommée/mal comprise (`OverallConfidence` = ratio de complétude, pas une confiance statistique) produirait des seuils dont l'interprétation serait fausse même si les valeurs numériques semblaient "optimales".

---

# 8. OBJECTIVE FUNCTION

Repris et affiné depuis le Lot 14.9 (§20 de ce rapport-là), sans sélectionner de valeur.

## Candidats évalués

| Métrique | Rôle recommandé | Limite actuelle |
|---|---|---|
| Signal count | Diagnostic / garde-fou de puissance statistique | Jamais une preuve de qualité (rappel explicite des deux briefs précédents) |
| Position count | Diagnostic | idem |
| Win rate | Diagnostic | Ignore la magnitude et l'asymétrie gain/perte |
| Mean return | Diagnostic secondaire | Sensible aux valeurs extrêmes |
| Median return | Diagnostic secondaire | Robuste aux extrêmes, aveugle au risque de queue |
| Expectancy (R-multiple) | **Candidat pour PRIMARY**, une fois un vrai dénominateur de risque disponible | Incalculable aujourd'hui (pas de StopLoss → pas de RiskDistance réelle) |
| MFE / MAE | Diagnostic essentiel pour calibrer StopLoss (§18) | Jamais un objectif de performance globale en soi |
| Gross PnL | Diagnostic (avant coûts) | Optimiste par construction, jamais suffisant seul |
| Net PnL | **Candidat pour PRIMARY** | Calculable dès aujourd'hui via P0-2 (Lot 14.10), mais sans dénominateur de risque ce n'est qu'un montant, pas un taux comparable entre configurations de tailles différentes |
| Profit Factor | Contrainte secondaire (garde-fou de sanité, >1) | Ignore fréquence et drawdown ; sensible à un seul trade exceptionnel |
| Sharpe / Sortino | **Reportées** (voir Lot 14.9 §20) | Equity curve = un point par position fermée, pas de mark-to-market intrabar → volatilité mesurée non honnête |
| Maximum Drawdown | **CONTRAINTE, jamais un objectif seul** | Un système 100% cash a un drawdown de 0, ce qui n'est pas "optimal" |
| Return / Drawdown | Métrique secondaire une fois le P&L net réaliste disponible | Dépend de Net PnL (donc des coûts actifs) |
| Stability (voir §13) | **CONTRAINTE de sélection**, pas une métrique de performance | Nécessaire pour rejeter un optimum étroit (§13) |

## PRIMARY OBJECTIVE (recommandation conceptuelle, pas une valeur)

**Net Expectancy After Costs**, exprimée en R-multiples une fois qu'une vraie `RiskDistance` existe (post-StopLoss). Tant que le StopLoss n'existe pas, un candidat PRIMARY intérimaire honnête est le **Net PnL par position** (pas par trade-jour, pas annualisé) — calculable dès aujourd'hui (P0-2 résolu), mais explicitement qualifié d'intérimaire dans tout rapport de calibration futur tant qu'aucun R-multiple n'est disponible.

## SECONDARY CONSTRAINTS

Une configuration n'est retenue QUE si, simultanément :
- Maximum Drawdown reste sous un plafond défini (valeur non choisie ici) ;
- Signal count reste au-dessus d'un plancher de puissance statistique (§17 — dépend du dataset) ;
- Profit Factor > 1 (garde-fou de sanité) ;
- Stabilité confirmée (§13) — **une configuration +100% PnL / -80% drawdown n'est PAS automatiquement meilleure qu'une configuration +40% PnL / -15% drawdown** ; le choix entre les deux nécessite un critère de risque explicite (ex. Return/Drawdown), jamais le PnL brut seul.

---

# 9. SIGNAL VS ECONOMIC CALIBRATION

Distinction explicitement demandée par le brief, absente du Lot 14.9 sous cette forme — nouvelle contribution de ce lot.

## SIGNAL CALIBRATION
Mesure la qualité de la DÉTECTION, indépendamment de tout compte/capital :
- Fréquence de signal (diagnostic, jamais un objectif) ;
- Direction (cohérence BUY/SELL avec le régime arbitré) ;
- MFE / MAE (dans quelle mesure le prix a favorablement/défavorablement dévié après le signal) ;
- Future return (Measurement, ancré sur `Close[SignalBarIndex]`/`Close[SignalBarIndex+HorizonBars]` — **jamais** la convention Execution).

Paramètres concernés : fenêtres de régime, poids Fusion/Decision, `AmbiguityGateThreshold`, seuils Entry/EntryTrigger, `MeasurementConfiguration.HorizonBars`/`HitThresholds`.

## ECONOMIC CALIBRATION
Mesure ce qu'un compte réel aurait réellement gagné/perdu :
- P&L (Gross puis Net) ;
- Coûts ;
- Risk (sizing, budget) ;
- Drawdown ;
- Equity.

Paramètres concernés : `ExecutionConfiguration.HorizonBars` (convention `Open[i+1]`/`Close[i+1+H]`), coûts, StopLoss/RiskDistance, RiskPolicy.

## Pourquoi ne pas les mélanger prématurément

Une configuration de Signal Calibration jugée "bonne" sur MFE/MAE peut se révéler économiquement mauvaise une fois les coûts et un sizing réaliste appliqués (fréquence élevée → coûts cumulés élevés). Inversement, une Economic Calibration menée sans d'abord stabiliser la couche Signal confondrait un bon paramétrage économique avec un signal de mauvaise qualité compensé par un sizing chanceux. **Ordre recommandé : stabiliser Signal Calibration (étapes 1-6 de §7) avant d'entamer Economic Calibration (étapes 7-10).**

---

# 10. DATASET REQUIREMENTS

Repris intégralement du Lot 14.9 §17 (Dataset Strategy) — aucune nouvelle donnée n'a été téléchargée par ce lot, conformément à l'instruction.

**Verdict inchangé : 45 jours de MES M5 (~6 200-8 900 barres selon gaps) sont INSUFFISANTS pour une calibration scientifique robuste destinée à généraliser.** Raisonnement complet en Lot 14.9 §17 : ~60-120 épisodes de régime bruts sur 30 jours de bourse effectifs, dont beaucoup corrélés ; un split 60/20/20% laisserait l'OOS avec 10-20 épisodes, échantillon fragile ; aucune garantie qu'un régime rare (choc macro) apparaisse dans une fenêtre glissante ancrée sur "maintenant" ; séries Yahoo splicées sans traitement de roll.

**Recommandation inchangée** : viser 6-12 mois de données M5 continues, couvrant au moins un épisode de forte tendance, un épisode de range prolongé, et si possible un épisode de rupture/choc de volatilité — non validé par une étude de puissance formelle, recommandation raisonnée.

**Complément de ce lot** : la séparation à 4 niveaux (§11) augmente légèrement le besoin en volume par rapport au schéma 3 niveaux du Lot 14.9, puisqu'un quatrième segment (TEST) doit être découpé sans réduire la taille de VALIDATION ni d'OOS sous le seuil déjà jugé fragile. Sur un dataset étendu à 6-12 mois, ceci reste largement absorbable (voir §11.3 pour la proposition de répartition).

---

# 11. TRAIN / VALIDATION / TEST / OOS

## 11.1 Rôles

| Segment | Rôle | Fréquence de consultation |
|---|---|---|
| **TRAIN** | Développement/calibration — c'est là qu'un grid search (`CalibrationGrid`) est exécuté | Autant de fois que nécessaire |
| **VALIDATION** | Sélection parmi les configurations candidates issues de TRAIN | Autant de fois que nécessaire PENDANT le cycle de calibration en cours |
| **TEST** | Vérification interne finale, une fois une configuration sélectionnée sur VALIDATION — un "répétition générale" avant l'OOS véritable | **Une seule fois par cycle de calibration** ; si TEST échoue, retour à TRAIN/VALIDATION autorisé mais DOIT être journalisé comme une nouvelle itération (voir §11.5) |
| **OOS** | Évaluation finale, jamais consultée pendant la calibration | **Une seule fois, jamais réutilisée** |

## 11.2 Pourquoi un 4ème niveau (TEST) distinct de VALIDATION et d'OOS

VALIDATION est consultée à répétition pendant qu'on compare des configurations candidates (c'est un usage normal, mais chaque consultation "use" un peu de la validité statistique du segment — risque de sur-ajustement indirect à VALIDATION elle-même si trop de configurations y sont comparées, §11 Multiple Comparisons). TEST introduit un segment supplémentaire, jamais vu pendant la phase de comparaison, consulté UNE fois pour vérifier que le choix fait sur VALIDATION généralise — une sorte de garde-fou avant de "dépenser" l'OOS, qui reste la ressource la plus précieuse et non renouvelable de tout le protocole.

## 11.3 Faisabilité avec l'infrastructure existante — AUCUNE MODIFICATION DE CODE REQUISE

`Backtest/Calibration/CalibrationWindowRole` reste un enum fermé à 3 valeurs (`Train`/`Validation`/`Oos`, `Backtest/Calibration/CalibrationWindowRole.cs`, vérifié par lecture directe). Ajouter une 4ème valeur native serait un changement d'infrastructure — non requis pour atteindre l'objectif : le protocole à 4 niveaux est **entièrement représentable dès aujourd'hui** en construisant **deux `CalibrationWindowSet` distincts partageant le même TRAIN** :

```
WindowSet #1 (phase SÉLECTION) :
    Train      = [t0, t1)
    Validation = [t1, t2)   ← consultée à répétition pendant la comparaison de configurations
    Oos        = [t2, t3)   ← PAS TOUCHÉE à ce stade (voir note ci-dessous)

WindowSet #2 (phase VÉRIFICATION, construit UNE FOIS la configuration choisie sur WindowSet #1) :
    Train      = [t0, t1)   ← identique
    Validation = [t2, t3)   ← RENOMMÉ "TEST" par convention d'usage : c'est la même plage que
                                l'ancien "Oos" du WindowSet #1, mais utilisée pour une vérification
                                unique, JAMAIS pour comparer d'autres configurations
    Oos        = [t3, t4)   ← la VRAIE OOS, jamais vue avant cette étape
```

Ce montage utilise exclusivement `CalibrationWindowSet.Create` (Lot 14.9, non modifié) avec des dates différentes — zéro nouveau type, zéro modification de fichier protégé ou non protégé. La discipline "TEST = une seule consultation" est une règle d'USAGE (documentée ici, appliquée par le chercheur), pas une contrainte imposée par le code — exactement comme OOS l'est déjà aujourd'hui dans le Lot 14.9 (rien dans le code n'empêche techniquement de relancer une expérience sur OOS plusieurs fois ; c'est une discipline documentée, pas un verrou logiciel).

**Répartition indicative** (sur un dataset étendu à 6-12 mois, §10) : TRAIN ~55%, VALIDATION ~15%, TEST ~15%, OOS ~15% (la plus récente). Non validée par une étude de puissance formelle — cohérente avec le raisonnement §10.

## 11.4 Base de fenêtrage : SignalTimestamp ou EntryTimestamp — décision explicite requise

Rappel de la règle posée au Lot 14.10 §14 (brief §27 de ce lot) : `CalibrationExperimentRunner.BuildSlice` fenêtre aujourd'hui les signaux par `SignalTimestamp` et les positions par `EntryTimestamp` (potentiellement une barre plus tard depuis Lot 14.10). **Ce lot ne change pas ce comportement** ; il documente la décision que tout futur travail de calibration DOIT prendre explicitement :

- Fenêtrer par `SignalTimestamp` répond à la question "quels signaux ce régime de marché a-t-il produits ?" — pertinent pour la Signal Calibration (§9).
- Fenêtrer par `EntryTimestamp` répond à la question "quel capital était réellement engagé, et quand ?" — pertinent pour l'Economic Calibration (§9).

Le comportement ACTUEL du code (Lot 14.9, non modifié) mélange les deux bases selon le champ consulté (`SignalCount` vs `PositionCount`) — cohérent en soi (chaque champ répond à sa propre question), mais tout futur rapport de calibration DOIT le mentionner explicitement plutôt que supposer implicitement que les deux comptages partagent la même frontière temporelle.

## 11.5 Journalisation des itérations (discipline anti-data-snooping)

Chaque cycle TRAIN→VALIDATION→TEST doit être journalisé (dataset, `CalibrationParameterSet` testés, `ConfigurationFingerprint` de chacun, configuration retenue) AVANT de consulter TEST. Si TEST échoue et qu'un nouveau cycle est lancé, le nombre total de cycles doit être conservé dans le rapport final — c'est la matière première du contrôle "comparaisons multiples" (§13).

---

# 12. WALK-FORWARD PROTOCOL

## Définition conceptuelle

```
Fenêtre 1 :  TRAIN₁ [t0,t1) → VALIDATION₁ [t1,t2) → TEST₁ [t2,t3)
Fenêtre 2 :  TRAIN₂ [t0+Δ,t1+Δ) → VALIDATION₂ [t1+Δ,t2+Δ) → TEST₂ [t2+Δ,t3+Δ)
Fenêtre 3 :  TRAIN₃ [t0+2Δ,t1+2Δ) → VALIDATION₃ [t1+2Δ,t2+2Δ) → TEST₃ [t2+2Δ,t3+2Δ)
...
```

avec Δ un pas de glissement fixe (ex. la durée de VALIDATION, pour un walk-forward non chevauchant sur cette portion). Chaque fenêtre est un `CalibrationWindowSet` indépendant, et chaque expérience un `CalibrationExperiment` indépendant.

## Faisabilité — pas d'implémentation complète requise dans ce lot

Le brief autorise explicitement l'implémentation "si l'infrastructure existante le permet sans modifier les composants protégés". C'est le cas : `CalibrationWindowSet.Create` + `CalibrationExperiment.Create` + `CalibrationExperimentRunner.Run` (tous Lot 14.9, non protégés, non modifiés) suffisent déjà à exécuter N fenêtres glissantes — il suffit d'appeler ces primitives N fois avec des dates différentes, en boucle, côté APPELANT (un futur test ou un futur outil de recherche), sans toucher au framework lui-même.

**Ce que ce lot NE construit PAS** (délibérément, car ce serait une nouvelle fonctionnalité, pas seulement une répétition d'un mécanisme existant) : un type `WalkForwardRunner`/`WalkForwardResult` qui automatiserait la génération des N fenêtres et l'agrégation de leurs résultats. C'est une plomberie légitime, à faible risque, pour un futur lot si le walk-forward devient un usage répété (voir §23 roadmap) — pas nécessaire pour que la MÉTHODE walk-forward soit utilisable dès aujourd'hui à la main.

---

# 13. OVERFITTING CONTROLS

## OOS Protection (brief §10)

Règle absolue, documentée ici pour toute future session : **une expérience OOS est utilisée une seule fois comme validation finale.** La séquence suivante est INTERDITE :

```
OOS → observer le résultat → modifier un paramètre → relancer → sélectionner le meilleur
```

Cette séquence transformerait l'OOS en une VALIDATION de plus (avec le même risque de sur-ajustement que VALIDATION elle-même), en la faisant passer pour une évaluation finale alors qu'elle ne l'est plus. Le protocole §11 (WindowSet #1/#2) sépare explicitement la ressource TEST (consultable une fois, avec possibilité documentée d'itérer) de la ressource OOS (jamais réutilisée) précisément pour absorber le besoin naturel d'itérer SANS consommer l'OOS.

## Multiple Comparisons (brief §11)

**Risque** : si N configurations sont testées sur VALIDATION, la meilleure d'entre elles a une probabilité non négligeable d'être excellente uniquement par hasard (le problème classique des comparaisons multiples / data snooping en recherche quantitative). Plus N est grand, plus ce risque croît.

**Ce que ce lot NE fait PAS** : implémenter une correction statistique (Bonferroni, White's Reality Check, Deflated Sharpe Ratio, etc.) — non justifié tant qu'aucune calibration réelle n'a été tentée, et explicitement hors scope ("ne pas implémenter de correction statistique complexe dans ce lot sans justification").

**Ce qui sera nécessaire plus tard** (documenté pour un futur lot) :
- Journaliser SYSTÉMATIQUEMENT le nombre de configurations comparées par cycle (§11.5) — condition préalable à toute correction future, puisque ces corrections ont besoin de N.
- Envisager, une fois qu'une vraie campagne de grid search est menée (hors scope de ce lot), une correction proportionnée à N (ex. une règle simple type Bonferroni pour un premier passage, avant d'investir dans quelque chose de plus sophistiqué).
- Ne jamais rapporter un résultat VALIDATION comme une preuve de performance sans mentionner N (nombre de configurations comparées pour l'obtenir).

## Robustesse (brief §12)

Une configuration dont la performance s'effondre pour une variation minime du paramètre (ex. seuil 0.49 très mauvais, 0.50 excellent, 0.51 très mauvais) est un signe d'overfitting probable — la performance "excellente" à 0.50 reflète probablement un artefact du dataset TRAIN/VALIDATION plutôt qu'une vraie relation causale. À l'inverse, une performance similaire sur un voisinage (ex. 0.45/0.50/0.55) est un signe de robustesse.

**Méthode future (non exécutée ici)** : pour tout paramètre candidat à la calibration, ne jamais retenir uniquement `Best(parameter)` — toujours produire `Performance(parameter)` sur un voisinage (via `CalibrationGrid`, déjà existant, Lot 14.9) et exiger un plateau, pas un pic isolé, avant de considérer une valeur comme candidate à la production.

---

# 14. SENSITIVITY ANALYSIS

## Ce qu'il faut mesurer : `Performance(parameter)`, pas seulement `Best(parameter)`

`CalibrationGrid.GenerateParameterSets` (Lot 14.9, non modifié) produit déjà, de façon déterministe, le produit cartésien de valeurs candidates pour un ou plusieurs axes. Combiné à `CalibrationParameterBinding` (Lot 14.10, pour `AmbiguityGateThreshold` uniquement aujourd'hui), une grille de N valeurs de `AmbiguityGateThreshold` produit déjà N `CalibrationExperimentResult` réellement différents (§6). C'est la matière première suffisante pour une analyse de sensibilité — **aucun nouveau mécanisme n'est requis pour EXÉCUTER une grille**, seulement une méthode pour l'INTERPRÉTER, définie ici.

## Signatures à détecter (méthodologie, pas un calcul exécuté)

| Signature | Interprétation |
|---|---|
| **Plateau robuste** | Performance quasi-stable sur un voisinage large du paramètre | Signe de robustesse — candidat défendable |
| **Optimum étroit** | Un pic isolé, performance chutant fortement de part et d'autre | Signe d'overfitting probable (§13) — À REJETER même si la valeur au pic est la meilleure en absolu |
| **Monotonicité** | Performance croît ou décroît continûment sur toute la plage testée | La grille n'a probablement pas couvert la vraie plage utile — étendre la grille avant de conclure |
| **Instabilité** | Performance oscillant sans structure apparente | Signal de bruit dominant — le paramètre n'a peut-être pas d'effet réel identifiable sur ce dataset |

**Ce lot ne calcule aucune de ces signatures sur une vraie donnée** — la méthode est définie pour un futur lot d'exécution.

---

# 15. REGIME CONDITIONAL ANALYSIS

## Nécessité

Une configuration globalement "bonne" en moyenne peut être mauvaise dans un régime particulier (ex. un seuil calibré principalement sur des données Mean-Reverting pourrait dégrader silencieusement la performance — ou plus probablement, ne produire aucun signal du tout — pendant un régime Trending). Le futur laboratoire doit donc produire une décomposition `Performance by Regime` (Mean-Reverting / Trending / Neutral / Transition), pas seulement un résultat global.

## Blocage structurel confirmé (repris du Lot 14.9, reclassé ici P1 spécifiquement pour cette section)

`Decision.Winner` (le régime arbitré, par barre) existe dans `BacktestSignalResult` mais n'est **jamais propagé** jusqu'à `PositionRiskOutcome` ni `MeasurementResult` — vérifié par lecture directe : ni `PositionRiskOutcome` (`Backtest/Risk/PositionRiskOutcome.cs`) ni `MeasurementResult` (`Backtest/Measurement/MeasurementResult.cs`) ne portent de champ `Regime`/`Winner`. Joindre les deux nécessiterait une jointure supplémentaire par `PositionId`/`SignalBarIndex` (le même identifiant déjà utilisé par `CalibrationExperimentRunner.TryFindReturn`), techniquement simple, mais c'est un changement de structure de données (pas seulement un calcul), donc **hors scope de ce lot** (documentation uniquement, §25 D14.11-2).

**Ce que ce lot définit néanmoins** : la méthode de décomposition future, une fois la jointure construite — filtrer `CalibrationExperimentResult` non seulement par `CalibrationDirectionFilter` (déjà possible, §16) mais aussi par un futur `CalibrationRegimeFilter` (Mean-Reverting/Trending/Neutral/Transition), produisant une grille `Window × Direction × Regime` au lieu de la grille `Window × Direction` actuelle (9 tranches aujourd'hui → 36 avec 4 régimes).

---

# 16. BUY / SELL ANALYSIS

**Déjà entièrement supporté par l'infrastructure existante — aucun travail requis.**

`Backtest/Calibration/CalibrationDirectionFilter` (Lot 14.9, `enum { All, Buy, Sell }`, non modifié) est déjà consommé par `CalibrationExperimentRunner.Run`, qui produit systématiquement 3 tranches de direction pour CHAQUE fenêtre (`foreach (CalibrationDirectionFilter direction in new[] { All, Buy, Sell })`) — vérifié par lecture directe, confirmé par les tests Lot 14.9 (`CalibrationYahooIntegrationTests` asserte `result.Results.Count == 9` = 3 fenêtres × 3 directions) et par les nouveaux tests Lot 14.10 (`TotalPositions` filtre explicitement `CalibrationDirectionFilter.All`).

**Règle méthodologique à appliquer lors d'une future calibration** (définie ici, pas exécutée) : une configuration ne doit pas être jugée robuste si sa performance ALL dépend presque entièrement d'une seule direction (ex. 95% du PnL vient du BUY) sans justification structurelle documentée (ex. asymétrie réelle du marché sur la période). Comparer systématiquement les tranches BUY/SELL/ALL d'un même `CalibrationExperimentResult` fait déjà partie du résultat produit par le framework actuel — il suffit de ne jamais ignorer les tranches BUY/SELL au profit du seul ALL dans un futur rapport de calibration.

---

# 17. INSTRUMENT / TIMEFRAME STRATEGY

| Axe | Statut |
|---|---|
| **MES M5** | Instrument/timeframe de PRODUCTION — toute calibration économique finale (Economic Calibration, §9) doit être menée sur MES, jamais uniquement sur ES |
| **ES M5** | Utilisé pour les études offline historiques (Lots 2-9, `AmbiguityGateThreshold`) — microstructure MES jamais re-testée séparément (constat déjà fait au Lot 14.9). Reste utile pour une Signal Calibration exploratoire (même sous-jacent, plus d'historique/liquidité potentiellement disponible), mais toute conclusion doit être revalidée sur MES avant adoption |
| **Autres timeframes** | Aucune infrastructure ne les supporte aujourd'hui (`RegimeEngine` a des fenêtres exprimées en nombre de barres, implicitement couplées à M5 — changer de timeframe sans recalibrer changerait silencieusement leur signification temporelle, déjà noté Lot 14.9) |

## Classification par paramètre

| Paramètre | Instrument-specific | Strategy-specific | Timeframe-specific |
|---|---|---|---|
| `TickSize`/`TickValue`/`PointValue` | **Oui** (Type C, fait objectif par instrument) | Non | Non |
| Fenêtres de régime (bars) | Non directement | Non | **Oui** — exprimées en nombre de barres, donc implicitement liées à M5 |
| `AmbiguityGateThreshold` | Étudié sur ES, cible MES — statut à revalider | **Oui** (propriété de l'arbitrage Decision, pas du marché lui-même) | Probablement, via le couplage aux fenêtres de régime |
| Coûts (commission/spread/slippage) | **Oui** (barème broker par instrument) | Non | Non directement |
| `RiskDistance`/StopLoss (futur) | Probablement (volatilité par instrument) | Non | **Oui** (couplé à `HorizonBars`) |

**Ne pas multiplier les instruments prématurément** (instruction du brief) — ce lot ne recommande PAS de tester ES et MES en parallèle dès le prochain lot de calibration ; il documente seulement que la distinction doit être faite consciemment quand elle deviendra pertinente (au plus tôt lors de la revalidation d'`AmbiguityGateThreshold`, déjà identifiée comme nécessaire depuis le Lot 14.9).

---

# 18. STOP LOSS DEPENDENCY

Aucun SL n'est créé dans ce lot. Données nécessaires à sa future calibration, déjà disponibles dans le pipeline actuel :

| Donnée | Disponible aujourd'hui ? | Source |
|---|---|---|
| MAE | **Oui** | `MeasurementResult.Mae` (Lot 14.4, ancré sur `Close[SignalBarIndex]`/`Close[SignalBarIndex+HorizonBars]` — Measurement, PAS Execution, voir §9) |
| MFE | **Oui** | `MeasurementResult.Mfe`, idem |
| ATR | **Non** | Grep négatif confirmé (Lot 14.9) — aucune implémentation ATR nulle part dans le dépôt |
| Volatility | **Oui** | `Engine/ScientificModels` `VolatilityModel` (classification LOW/MEDIUM/HIGH, seuils Type D non calibrés) |
| Regime | **Oui, mais non joint** | `Decision.Winner` — même blocage de jointure que §15 |
| Entry context | **Oui** | `EntryTriggerContext`/`BacktestSignalResult` (au moment du signal, avant tout fill) |

## Dépendances (rappel §6, formalisé ici)

```
StopLoss (méthodologie, à construire)
    → RiskDistance (Backtest/Risk) — StopLoss EST la distance de stop, pas une dérivation indépendante
        → Position Size (via RiskEngine.Evaluate, déjà validé mécaniquement)
            → PnL (Net, une fois les coûts actifs)
```

Protocole déjà verrouillé et existant (`Documentation/Scientific/QDE-012_StopLoss_Calibration_Protocol.md`, candidats A1/D) — ce lot ne le modifie pas, ne sélectionne aucun candidat, ne calibre aucun `k`. Rappel du blocage connu (Lot 14.9) : la seule capture ATAS réelle disponible est `SCIENTIFIC_ADMISSIBILITY=BLOCKED` (qualité insuffisante) — une nouvelle capture reste un préalable à toute calibration SL réelle.

---

# 19. RISK DEPENDENCY

Aucune valeur de `RiskPolicy` n'est calibrée dans ce lot. Dépendances documentées :

```
Risk % (RiskPolicy.MaxRiskPerTradePercent, etc. — n'existe pas en production, fail-closed)
    ↔ Stop Distance (RiskDistance — dépend de §18, inexistant tant que le SL n'existe pas)
    ↔ Quantity (RiskEngine.Evaluate — PositionSize = floor(RiskBudget / RiskPerUnit), RiskPerUnit = RiskDistance × PointValue)
    ↔ Drawdown (BacktestRiskResultBuilder — le drawdown scale avec la quantité, donc avec Risk% et RiskDistance simultanément)
    ↔ Equity (InitialCapital + cumulativeNetPnL — dépend des coûts actifs, §9 Economic Calibration)
```

**Conséquence méthodologique** : `Risk%` ne peut être calibré indépendamment ni de `RiskDistance` (StopLoss) ni du plafond de Drawdown accepté (§8 Secondary Constraints) — les trois doivent être évalués comme un système, jamais l'un après l'autre en figeant les deux autres à une valeur arbitraire.

---

# 20. COST DEPENDENCY

Aucun barème réel n'est choisi dans ce lot. Données nécessaires à une future calibration réaliste, documentées ici pour la première fois avec ce niveau de détail :

| Composant | Donnée nécessaire | Source future |
|---|---|---|
| Commission | Barème par ordre + par contrat du broker cible (ex. AMP/NinjaTrader/Tradovate pour MES) | Documentation broker, à sourcer et CITER (jamais inventer) |
| Spread | Distribution réelle bid/ask sur MES M5, pas une constante — actuellement `SpreadConfiguration.Fixed`/`FromTicks` (constant), pas de modèle de distribution | Capture ATAS réelle avec carnet, ou fournisseur de données tick |
| Slippage | Distribution empirique du slippage réel à l'exécution (dépend de la liquidité au moment du fill, donc de l'heure de session) — actuellement un modèle constant | Capture ATAS réelle avec exécutions simulées/réelles |
| Fees | Frais d'échange/régulateurs (CME, NFA) par contrat | Documentation CME/broker |

Rappel (Lot 14.10) : le modèle de calcul (`PositionCostCalculator`) est déjà validé et fonctionnel — ce qui manque n'est PAS un défaut de câblage, mais des VALEURS sourcées d'un barème réel, explicitement hors scope de ce lot comme du précédent.

---

# 21. FINGERPRINT / SERIALIZATION

## Fingerprint — audit (brief §19)

Vérifié par lecture directe de `Backtest/Calibration/CalibrationFingerprint.ComputeConfigurationFingerprint` : le fingerprint inclut `ProtocolVersion`, `CalibrationParameterSet` (trié par nom, donc indépendant de l'ordre d'insertion), `Dataset` (spec + son propre fingerprint SHA-256), les 3 fenêtres (`Train`/`Validation`/`Oos`), `WarmupContract.RequiredWarmupBars`, `Instrument`, `Policy`, `InitialCapital`, et les 5 configurations `Measurement`/`Execution`/`PnL`/`Cost`/`Risk`. **Confirmé exhaustif** — deux expériences ne différant que par un seul de ces champs (y compris la valeur d'`AmbiguityGateThreshold`, prouvé par test Lot 14.10) produisent des fingerprints différents ; deux expériences identiques sur tous ces champs produisent le même fingerprint (prouvé par `RunningTheSameExperimentTwice_ProducesIdenticalResults`, Lot 14.9, et par le test de déterminisme Lot 14.10).

**`PipelineParameterOverrides` (Lot 14.10) n'est jamais lui-même une entrée séparée du fingerprint — et n'a pas besoin de l'être** : c'est une fonction pure et déterministe de `CalibrationParameterSet` (via `CalibrationParameterBinding.Resolve`), lui-même déjà haché. Aucune information n'est perdue.

## Serialization — audit (brief §20)

`Backtest/Calibration/CalibrationSerialization` (Lot 14.9, non modifié) sérialise `CalibrationParameter`/`CalibrationParameterSet` de façon entièrement générique (par `Name`/`Type`/`Unit`/valeur typée) — aucun code spécifique au nom `"AmbiguityGateThreshold"` n'existe ni n'est nécessaire. `CalibrationSerializationTests` (Lot 14.9) prouve déjà le round-trip sans perte pour un paramètre nommé `"AmbiguityThreshold"` (fixture historique) ; le même mécanisme générique couvre `"AmbiguityGateThreshold"` par construction — **vérifié par inspection du code, aucun test supplémentaire n'apporterait de garantie non déjà couverte**.

---

# 22. CALIBRATION RULES

Synthèse actionnable des règles établies ci-dessus, pour tout futur lot de calibration :

1. Ne jamais calibrer un paramètre en aval sans avoir stabilisé ses dépendances en amont (§7).
2. Ne jamais interpréter un fingerprint différent comme une preuve de changement de comportement — vérifier l'effet observable (§6, leçon du Lot 14.10 TEST 3).
3. Ne jamais consulter OOS plus d'une fois par cycle de calibration (§13).
4. Toujours journaliser le nombre de configurations comparées par cycle (§11.5, §13).
5. Toujours préférer un plateau robuste à un optimum étroit (§13, §14).
6. Toujours séparer Signal Calibration et Economic Calibration jusqu'à ce que la première soit stable (§9).
7. Toujours comparer BUY/SELL/ALL, jamais seulement ALL (§16).
8. Toujours documenter explicitement la base de fenêtrage choisie (`SignalTimestamp` vs `EntryTimestamp`) pour toute analyse par fenêtre (§11.4).
9. Ne jamais activer une calibration économique (P&L net, Risk, Drawdown) sans coûts réalistes actifs (§6, §9).
10. Ne jamais promouvoir en production une valeur calibrée uniquement sur ES sans revalidation sur MES (§17).

---

# 23. FUTURE LOT ROADMAP

Reprise et affinée de la roadmap Lot 14.9, avec les deux lots P0-1/P0-2/P0-3 maintenant complétés (Lot 14.10) et ce lot (14.11, méthodologie) complété.

| Lot | Objectif | Prérequis | Statut prérequis |
|---|---|---|---|
| 14.12 | Extended Dataset Acquisition (6-12 mois MES M5) | Aucun | Prêt à démarrer |
| 14.13 | Correction sémantique `OverallConfidence` (renommage/documentation, pas une calibration) | Aucun | Prêt à démarrer |
| 14.14 | Regime Window Sensitivity Study | 14.12 | Bloqué (dataset) |
| 14.15 | Fusion / Decision Weight Calibration | 14.12, 14.14, coûts réels sourcés (hors scope) | Bloqué |
| 14.16 | `AmbiguityGateThreshold` Revalidation (avec coûts réels + capture live courte) | Barème broker réel sourcé, 14.12 | Bloqué |
| 14.17 | Stop Loss Methodology (données réelles) | Nouvelle capture ATAS passant le gate de qualité | Bloqué |
| 14.18 | Regime→Position join (débloque §15 Regime Conditional Analysis) | Aucun (changement de structure de données isolé, pas une calibration) | Prêt à démarrer, priorité P1 |
| 14.19 | Risk Distance / TradePlan Wiring | 14.17 | Bloqué |
| 14.20 | Risk Policy Definition | 14.17, 14.19 | Bloqué |
| 14.21 | Equity Mark-to-Market (débloque Sharpe/Sortino honnêtes) | Aucun | Prêt à démarrer, priorité P2 |
| 14.22 | Walk-Forward Runner (plomberie d'automatisation, si l'usage manuel §12 devient répétitif) | Aucun | Optionnel, priorité P3 |
| 14.23 | Global Walk-Forward OOS Validation | Tous les lots ci-dessus | Bloqué |

---

# 24. KNOWN LIMITATIONS

- Ce lot ne propose ni n'exécute aucune calibration réelle — la méthodologie seule est produite.
- La séparation TRAIN/VALIDATION/TEST/OOS à 4 niveaux (§11) repose sur une discipline d'usage (deux `CalibrationWindowSet`), pas sur une contrainte structurelle imposée par le code — un chercheur pourrait techniquement violer la règle "TEST consultée une fois" sans qu'aucun test ne l'en empêche, exactement comme pour OOS aujourd'hui.
- Aucune correction statistique de comparaisons multiples n'est implémentée (§13) — volontairement, faute de justification tant qu'aucune campagne réelle n'a eu lieu.
- La décomposition par régime (§15) reste impossible tant que le lot 14.18 (jointure Decision.Winner) n'est pas fait.
- Aucune donnée réelle nouvelle n'a été acquise — le verdict d'insuffisance des 45 jours actuels (§10) reste entier.
- Le fenêtrage `SignalTimestamp`/`EntryTimestamp` (§11.4) reste une source de confusion potentielle pour quiconque n'a pas lu ce rapport — aucune garde logicielle n'empêche de mal l'interpréter.

---

# 25. DEFECT / FINDING TABLE (P0-P3, non corrigés dans ce lot)

| # | Sévérité | Constat | Preuve | Impact | Lot nécessaire |
|---|---|---|---|---|---|
| D14.11-1 | **P1** | `CalibrationParameterBinding` ne connaît que `"AmbiguityGateThreshold"` — tout autre nom de paramètre déclaré dans un `CalibrationParameterSet` change le fingerprint sans effet comportemental (illusion de calibration possible) | `CalibrationParameterBinding.cs` (lecture directe), reprend Défaut D4 du Lot 14.9 pour tout paramètre restant | Un futur chercheur pourrait croire calibrer un paramètre qui n'est en réalité jamais lu | Un lot dédié par paramètre à rendre bindable (14.14/14.15/14.19/14.20 selon le paramètre) |
| D14.11-2 | **P1** | `Decision.Winner` non joint à `PositionRiskOutcome`/`MeasurementResult` — Regime Conditional Analysis (§15) impossible | Lecture directe des deux records, confirmé absence de champ `Regime` | Bloque toute décomposition de performance par régime | 14.18 (proposé §23) |
| D14.11-3 | **P2** | `CalibrationWindowRole` reste un enum fermé à 3 valeurs — TEST (§11) n'a pas de représentation native, seulement une convention d'usage à 2 `CalibrationWindowSet` | `CalibrationWindowRole.cs` (lecture directe) | Aucune garde logicielle contre une consultation répétée de TEST | Optionnel — ajouter un 4ème rôle natif si l'usage le justifie |
| D14.11-4 | **P2** | Aucune correction de comparaisons multiples, aucun mécanisme de journalisation de N configurations comparées n'existe encore dans le framework | Grep négatif : aucun champ "AttemptCount"/"ComparisonLog" dans `Backtest/Calibration/` | Risque de sur-interprétation d'un résultat VALIDATION sans contexte du nombre de configurations testées pour l'obtenir | À construire au moment de la première vraie campagne de grid search |
| D14.11-5 | **P3** | Pas de `WalkForwardRunner` automatisant la génération de N fenêtres glissantes | Absence confirmée dans `Backtest/Calibration/` | Le walk-forward reste possible mais manuel (§12) | 14.22 (proposé §23, optionnel) |

---

# 26. TESTS

## Audit de couverture existante (aucun nouveau test de production ajouté)

Ce lot a vérifié, par lecture directe et par référence aux suites de tests déjà existantes (Lots 14.9/14.10), que chaque invariant de protocole demandé par le brief §26 est déjà couvert :

| Invariant demandé | Couverture existante | Fichier |
|---|---|---|
| Parameter dependency | Documenté §6 (analyse de code, pas un test exécutable — une dépendance architecturale n'est pas une propriété testable unitairement) | Ce rapport |
| Configuration isolation | `RunningADifferentExperimentInBetween_DoesNotAffectTheOriginalExperimentsResult` (Lot 14.9) + `ExperimentA_ExperimentB_ExperimentA_...` (Lot 14.10, avec paramètre non-défaut) | `CalibrationRunIsolationTests.cs`, `CalibrationParameterBindingTests.cs` |
| Fingerprint | `CalibrationFingerprintTests` (mutation dataset/paramètres/config/fenêtre, Lot 14.9) + `TwoCalibrationExperiments_DifferingOnlyByAmbiguityGateThreshold_...` (Lot 14.10, fingerprint ET comportement) | `CalibrationFingerprintTests.cs`, `CalibrationParameterBindingTests.cs` |
| Serialization | `CalibrationSerializationTests` (round-trip JSON, Lot 14.9) — générique par construction, couvre tout nom de paramètre par le même mécanisme (§21) | `CalibrationSerializationTests.cs` |
| OOS isolation | `CalibrationWindowIsolationTests` (Lot 14.9) + `NonDefaultThreshold_AppendingMoreOosData_...` (Lot 14.10, avec paramètre non-défaut) | `CalibrationWindowIsolationTests.cs`, `CalibrationParameterBindingTests.cs` |
| TRAIN/VALIDATION boundaries | `CalibrationWindowTests` (Lot 14.9, ordre strict Train<Validation<Oos, rejets) | `CalibrationWindowTests.cs` |
| Parameter interaction representation | Documenté §6 (matrice) — aucune représentation en code n'est demandée par les critères d'acceptation, volontairement non construite pour éviter la sur-ingénierie (une matrice de dépendances est un document d'architecture, pas un invariant testable automatiquement sans un oracle de "bon comportement" qui n'existe pas encore) | Ce rapport |
| Determinism | `SameInputs_RunTwice_...` (Lot 14.9) + `SameDatasetConfigurationAndThreshold_RunTwice_...` (Lot 14.10, avec paramètre non-défaut) | `CalibrationRunIsolationTests.cs`, `CalibrationParameterBindingTests.cs` |

**Décision explicite** : aucun nouveau test n'a été ajouté par ce lot. Chaque invariant demandé est déjà prouvé par un test existant (Lot 14.9 pour le mécanisme générique, Lot 14.10 pour son application au paramètre aujourd'hui bindable). Ajouter un test dupliquant une garantie déjà établie n'aurait apporté aucune information supplémentaire, et le brief demande explicitement de n'ajouter QUE les tests nécessaires.

## Vérification technique

```
dotnet build IQIAIndicator.csproj -c Debug    → PASS (inchangé depuis Lot 14.10)
dotnet build IQIAIndicator.csproj -c Release  → PASS (inchangé depuis Lot 14.10)
```

Aucun fichier de production n'ayant été modifié par ce lot, la suite de tests complète (777 réussis / 1 ignoré / 0 échec, Lot 14.10) reste valide sans nouvelle exécution — aucune régression n'est possible sans changement de code.

---

# HANDOFF CONTEXT — NEXT SESSION

```
PROJECT:
IQIA

CURRENT LOT:
14.11 (Calibration Experiment Design & Parameter Dependency Validation) - COMPLETE

LAST COMPLETED:
14.10 (P0-1 Execution Bias corrigé, P0-2 Costs confirmés fonctionnels, P0-3 Calibration Binding réel pour AmbiguityGateThreshold)

CURRENT ARCHITECTURE:
Market Data (Yahoo/ATAS) -> Market Context -> Regime (ADF/KPSS/DFA/HalfLife/VarianceRatio/CUSUM/Bai-Perron,
mono-fenêtre par modèle, PAS de multi-fenêtre) -> Fusion (5 règles, poids non calibrés) -> Decision (5 règles
"Provisional" + Arbitrator, AmbiguityScore=Clamp(1-Difference,0,1)) -> Signal (modèles réels UNIQUEMENT pour
MeanReversion) -> Entry (seuils sur OverallConfidence = ratio de complétude, PAS une confiance statistique) ->
EntryTrigger (Winner==MeanReverting AND AmbiguityScore<AmbiguityGateThreshold AND DynamicZScore!=0) -> TradePlan
(StopLoss TOUJOURS null -> SIGNAL_ONLY) -> Risk Engine (mécanique validée, AUCUNE RiskPolicy de production) ->
Execution (fill à Open[SignalBarIndex+1], CORRIGÉ Lot 14.10, exit à Close[SignalBarIndex+1+HorizonBars]) ->
Costs (activables et fonctionnels depuis Lot 14.10, DÉSACTIVÉS par défaut) -> P&L/Equity/Drawdown (un point
par position fermée) -> Calibration Infrastructure (Backtest/Calibration, TRAIN/VALIDATION/OOS natif +
TEST par convention à 2 WindowSets, un seul paramètre réellement bindable: AmbiguityGateThreshold).

CALIBRATION FRAMEWORK:
Backtest/Calibration/ (Lot 14.9, 21 fichiers de production + Lot 14.10: PipelineParameterOverrides.cs,
CalibrationParameterBinding.cs). CalibrationParameterSet -> CalibrationParameterBinding.Resolve() ->
PipelineParameterOverrides -> BacktestEngine.RunFullBacktestWithRisk(..., overrides) ->
RunSignalPipeline(..., overrides) -> SignalEngine(ambiguityGateThreshold) -> EntryTriggerEngine(threshold)
-> EntryTriggerBuilder(threshold) -> comportement réel observable. CalibrationDirectionFilter (All/Buy/Sell)
déjà natif. CalibrationWindowRole reste fermé à 3 valeurs (Train/Validation/Oos) - TEST simulé par 2
CalibrationWindowSet (voir Lot 14.11 §11.3), pas encore un rôle natif.

PARAMETERS CURRENTLY INJECTABLE:
AmbiguityGateThreshold (nom "AmbiguityGateThreshold" dans CalibrationParameterSet, Decimal, unité "score",
défaut 0.95 inchangé) - le SEUL.

PARAMETERS NOT YET INJECTABLE:
Fenêtres de régime (ADF/KPSS/DFA/HalfLife/VarianceRatio/CUSUM/Bai-Perron), poids Fusion (5 règles), poids
Decision (5 règles), seuils Entry (0.9/0.6/0.3), seuils EntryTrigger READY (0.70/0.55), HorizonBars/
HitThresholds (Measurement/Execution), coûts (Slippage/Spread/Commission/Fees), RiskDistance, RiskPolicy.*.
Un CalibrationParameter portant l'un de ces noms serait accepté silencieusement par CalibrationParameterSet
et changerait le fingerprint SANS effet comportemental (D14.11-1) - piège à ne jamais reproduire sans
d'abord construire le binding correspondant.

P0 FIXES COMPLETED (Lot 14.10):
Execution fill bias (Close[i] -> Open[i+1]), Cost activation proof (NetPnL != GrossPnL prouvé), Calibration
binding réel pour AmbiguityGateThreshold.

P0 REMAINING:
Aucun P0 nouveau identifié par ce lot. P1 restants: D14.11-1 (binding limité à 1 paramètre),
D14.11-2 (Regime non joint aux positions, bloque Regime Conditional Analysis), correction sémantique
OverallConfidence (héritée Lot 14.9, toujours non faite), absence de méthodologie StopLoss (héritée),
absence de RiskPolicy de production (héritée).

SCIENTIFICALLY VALIDATED:
Formules Half-Life (scikit-learn, tol 1e-6/1e-4) et Variance Ratio (NumPy, tol 1e-6) - Type A. Mécanique
Risk Engine (35+ tests). Cohérence interne Execution.Return (recalcul indépendant, Lot 14.10). Chaîne de
binding AmbiguityGateThreshold (propagation au consommateur réel, effet comportemental, isolation,
déterminisme, look-ahead - 10 tests dédiés, Lot 14.10).

TECHNICALLY VALIDATED ONLY:
Formules ADF/KPSS/DFA (valeurs critiques citées, magnitude jamais comparée à Statsmodels/nolds - placeholders
vides). Fingerprint/Sérialisation du framework de calibration (génériques, prouvés, mais jamais exercés sur
une vraie campagne). Mécanisme CalibrationGrid (produit cartésien déterministe prouvé, jamais utilisé pour
une vraie sensibilité).

NOT VALIDATED:
Poids Fusion/Decision (auto-déclarés "Provisional"). Fenêtres de régime (aucune étude de sensibilité).
AmbiguityGateThreshold=0.95 au sens économique complet (robuste structurellement Lots 4-9, jamais net de
coûts réels ni validé live). Tout paramètre listé "PARAMETERS NOT YET INJECTABLE" ci-dessus.

KNOWN DEFECTS:
Voir Lot 14.11 §25 (table complète, 5 entrées: D14.11-1 à D14.11-5, P1/P1/P2/P2/P3) + tous les défauts non
résolus du Lot 14.9 §6 qui ne concernaient pas P0-1/P0-2/P0-3 (absence StopLoss, absence RiskPolicy,
sémantique OverallConfidence trompeuse, régime non-multi-fenêtre, etc. - toujours entiers).

CALIBRATION DEPENDENCIES:
Voir Lot 14.11 §6 (matrice complète) et §7 (ordre déduit): Regime windows -> Fusion weights -> Decision
weights -> AmbiguityGateThreshold -> [correction OverallConfidence] -> Entry/EntryTrigger thresholds ->
HorizonBars -> StopLoss -> Risk -> Costs -> Walk-Forward global. Règle absolue: ne jamais calibrer Fusion
et Decision weights via une seule grille non structurée (confounding).

DATASET:
Yahoo MES=F/ES=F M5, fenêtre glissante ~45 jours (~6200-8900 barres) - VERDICT INCHANGÉ: INSUFFISANT pour
une calibration généralisable (Lot 14.9 §17, repris Lot 14.11 §10). Aucune nouvelle donnée acquise par ce
lot. Recommandation inchangée: 6-12 mois M5 continus, couvrant tendance/range/rupture.

TRAIN:
~55% du dataset étendu recommandé (proposition non appliquée) - développement/calibration, consulté à
répétition.

VALIDATION:
~15% - sélection parmi configurations candidates, consulté à répétition PENDANT un cycle de calibration.

TEST:
~15% - NOUVEAU niveau défini par ce lot (brief Lot 14.11), atteignable SANS code nouveau via un second
CalibrationWindowSet partageant le TRAIN mais avec Validation="TEST" - consulté UNE SEULE FOIS par cycle,
avant l'OOS.

OOS:
~15%, la plus récente - jamais consultée pendant la calibration, jamais réutilisée après consultation.

NEXT CALIBRATION LOT:
Aucune calibration réelle n'est recommandée immédiatement. Deux lots "prêts à démarrer" sans prérequis
bloquant: (a) 14.13 - correction sémantique d'OverallConfidence (renommage/documentation, préalable à toute
calibration Entry/EntryTrigger) ; (b) 14.18 - jointure Decision.Winner vers PositionRiskOutcome/
MeasurementResult (débloque Regime Conditional Analysis, changement de structure de données isolé).

WHY:
(a) est un préalable méthodologique bon marché qui débloque une classe entière de calibrations futures
(Entry/EntryTrigger) sans risque. (b) est un prérequis P1 indépendant qui débloque une analyse (§15) déjà
identifiée comme nécessaire par ce lot, sans dépendre d'aucune donnée supplémentaire ni d'aucune décision
de calibration.

DO NOT DO:
Ne PAS lancer de grid search réel sur AmbiguityGateThreshold ou tout autre paramètre avant d'avoir un
dataset étendu (14.12) et, pour toute conclusion économique, des coûts réels sourcés. Ne PAS ajouter de nom
de paramètre à CalibrationParameterSet en supposant qu'il sera automatiquement pris en compte - vérifier
d'abord qu'un binding existe dans CalibrationParameterBinding (aujourd'hui: AmbiguityGateThreshold
uniquement), sinon le fingerprint changera sans aucun effet réel (D14.11-1). Ne PAS consulter OOS plus
d'une fois par cycle. Ne PAS mélanger Signal Calibration et Economic Calibration avant que la première soit
stable. Ne PAS toucher RiskEngine.cs/RiskPolicy.cs/InstrumentRiskSpecification.cs/RiskEngineRequest.cs/
RiskAssessment.cs/DecisionArbitrator.cs/DecisionEngine.cs/TradePlanBuilder.cs/RegimeEngine.cs sans nécessité
technique explicitement documentée et approuvée. EntryTriggerBuilder.cs reste modifiable UNIQUEMENT pour
étendre l'injection de paramètres additionnels selon le même schéma explicite/typé déjà établi (Lot 14.10),
jamais pour changer une valeur de production.
```

**STOP.**
