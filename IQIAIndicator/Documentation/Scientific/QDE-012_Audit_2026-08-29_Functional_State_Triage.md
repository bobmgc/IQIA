# QDE-012 — Audit d'état fonctionnel & triage — 2026-08-29

**Date** : 2026-08-29
**Branche** : `main` (HEAD `437fca0`, après merge de `feature/structural-stability-v2`)
**Type** : **AUDIT EN LECTURE SEULE** — aucun fichier de production modifié, aucune calibration, aucun déploiement.
**Méthode** : lecture directe du code (`Engine/`, `Core/`, `Visualization/`, `Backtest/`, `IQIAIndicator.cs`), recoupement avec 3 captures ScientificDataset ATAS produites le 2026-08-29 (MNQ M5 17:53, MES M5 18:01, MES M5 18:12 — post-déploiement de la DLL du jour), et avec les audits internes existants **Lot 14.9** (Global Calibration Audit), **Lot 15.0** (Regime Coverage & System Maturity), **Lot 17** (StructuralBreak End-to-End). Toutes leurs conclusions clés restent valides à `437fca0`.
**Portée** : répond à la question « qu'est-ce qui marche, qu'est-ce qui ne marche pas, avant de continuer le développement », sur 3 axes : (1) stratégies/régimes, (2) calibration scientifique, (3) dashboard.

---

## 1. Verdict

L'**infrastructure mécanique est solide** : déterminisme, discipline « ne jamais fabriquer une valeur » (statuts explicites plutôt que zéros silencieux), séparation stricte des étages, couverture de tests abondante, look-ahead prouvé par test exécutable. C'est un socle rare et de qualité, confirmé par les audits internes.

Mais l'**état fonctionnel bloque toute calibration sérieuse** :

| Constat | Preuve |
|---|---|
| Le système ne peut placer **aucun trade** en live (ni backtest « réaliste ») : `TradePlan` plafonne à `SIGNAL_ONLY`, jamais `PLAN_READY`. | `IQIAIndicator.cs:575` ; `TradePlan.StopLoss = NOT AVAILABLE` sur 100 % des barres des 3 captures ATAS |
| **1 régime sur 5** produit un signal directionnel (`MeanReverting`). Les 4 autres sont inertes en aval de Decision. | `ScientificModelRegistry.cs` ; Lot 15.0 §1 (11 059 barres) ; captures ATAS (`Entry.OpportunityStatus = NOT_QUALIFIED` hors MeanReverting) |
| Le régime `StructuralBreak` (≈30 % des barres) est détecté **sans aucune evidence de rupture**. | Lot 15.0 §1, Lot 17 §1 ; `StructuralBreakRule.cs` |
| Poids Fusion & Decision **jamais calibrés** (commentés `Provisional` dans le code). | 9 fichiers `Engine/**/Rules/*.cs` |
| Le framework de calibration ne relie **aucun paramètre** à un point d'injection réel du pipeline (sauf `AmbiguityGateThreshold`). | Lot 14.9 §1 pt 6 |

**Readiness technique : ÉLEVÉE. Readiness scientifique : FAIBLE–MOYENNE. Readiness calibration : FAIBLE (NOT READY).**

---

## 2. Axe 1 — Stratégies / régimes

### 2.1 Deux taxonomies de « régime »

| Enum | État | Détail |
|---|---|---|
| `Engine/Regime/RegimeType.cs` (`TrendBull`, `TrendBear`, `Range`, `Compression`…) | **MORTE** | Consommée seulement par `Engine/Regime/Core/EvidenceFusionEngine.cs`, un stub Sprint 2.4 sans aucun appelant. Jamais dans un pipeline exécuté. |
| `Engine/Decision/States/MarketState.cs` (`MeanReverting`, `Trending`, `StructuralBreak`, `RandomWalk`, `StableRange`, `Transitional`, `Unknown`) | **VIVANTE** | Produite par `DecisionArbitrator.Arbitrate().Winner`. C'est « le régime » partout ailleurs. |

`MarketState` : **5 valeurs atteignables** comme Winner (une règle `Engine/Decision/Rules/*.cs` chacune). `Transitional` n'est assigné par **aucune** règle → maillon mort par construction, bien qu'il ait une méthodologie dédiée en aval. `Unknown` = fallback si `candidates.Count == 0`, jamais observé.

### 2.2 Couverture des modèles scientifiques

`Engine/ScientificModels/Registry/ScientificModelRegistry.cs` — `Resolve()` :

| Régime → méthodologie | Modèles scientifiques réels | Capacité de trading |
|---|---|---|
| `MeanReverting` → `MeanReversionMethodology` | **5 réels, testés** : Kalman, Ornstein-Uhlenbeck, DynamicZScore, Volatility, SPRT | **SEUL régime qui produit une direction BUY/SELL** |
| `Trending` → `TrendFollowingMethodology` | **0** — `TimeSeriesMomentumModel` + `BOCPDModel` sont des stubs (`Evaluate()` renvoie toujours `Success=false, Score=0`) | inerte — `EntryTriggerReason.UNSUPPORTED_REGIME` |
| `StructuralBreak` → `StructuralBreakMethodology` | **0** — `BOCPDModel` stub | inerte pour le **signal** ; mais la **règle de décision** protège le compte (Lot 18 : la retirer triple la perte) |
| `RandomWalk` → `RandomWalkMethodology` | **0** — aucune classe « Random Walk Null Model » n'existe | inerte |
| `StableRange` → `StableRangeMethodology` | **0** | inerte |
| `Transitional` → `TransitionalMethodology` | **0** | inatteignable |

**Preuve empirique** (captures ATAS 2026-08-29, cohérent avec Lot 15.0 sur 11 059 barres) :

```
Decision.Winner (MES 18:12) : MeanReverting 1505 | StructuralBreak 734 | RandomWalk 126 | Trending 112 | StableRange 92
Entry.OpportunityStatus     : HIGH_PRIORITY 1505 (= exactement les barres MeanReverting) | NOT_QUALIFIED 1064 (tout le reste)
EntryTrigger.Direction      : NO_ACTION 1898 | BUY_CANDIDATE 339 | SELL_CANDIDATE 332  (toutes sur MeanReverting)
Fusion.MissingEvidence      : vide 1505 | "Kalman;OU;DynamicZScore;Volatility;SPRT" 1064 (tout le reste)
```

### 2.3 Chaîne de blocage — même `MeanReverting` ne trade pas

1. **Gate d'ambiguïté** : ≈50 % des barres directionnelles rejetées `DECISION_AMBIGUOUS` (captures : 495/672 MNQ, 834/1505 MES). `EntryTriggerBuilder` : `Winner==MeanReverting AND AmbiguityScore<0.95 AND DynamicZScore≠0`.
2. **Stop Loss jamais produit en live** : `IQIAIndicator.cs:575` construit `new TradePlanContext(entryTriggerCandidate, instrumentInfo)` **sans `RiskParameters`** → `context.RiskParameters?.StopLoss` toujours `null` (`TradePlanBuilder.cs:57`, message : *« no stop-loss methodology is implemented in the system yet »*) → `TradePlan` plafonne à `SIGNAL_ONLY`. Confirmé : `TradePlan.StopLoss = NOT AVAILABLE` / `StopLossAvailable = False` sur **100 %** des 3 captures.
3. **Sans SL → pas de sizing** : `Risk.Status` jamais `ACCEPTED` (uniquement `NOT AVAILABLE` ou `REJECTED`), `Risk.PositionSize` toujours `NOT AVAILABLE`.
4. Le **Lot 15.3** a créé `Engine/Risk/VolatilityStopLossModel.cs` (`StopPrice = EntryPrice ∓ 2.0 × CurrentVolatility`) mais l'a câblé **uniquement dans `BacktestEngine.cs:374`, jamais dans `IQIAIndicator.cs`** (live). Le multiplicateur `2.0` est **non calibré** (`K_SELECTED: NO`, Lot 15.14).

> **Correction 2026-08-30** : contrairement à ce que l'audit Lot 15.0/15.3 laissait entendre, `ExecutionSimulator` **utilise bien le SL/TP** — les **Lots 15.4/15.5** (commit `669391a`, postérieurs) ont ajouté le monitoring intrabar : sortie sur `StopLoss`/`TakeProfit`/`Ambiguous` dès qu'un niveau est touché, `TimeHorizon` n'étant plus qu'un fallback. Vérifié empiriquement (backtest MES=F M5, ~45 j) : **96 % des sorties sont sur SL/TP** (TP 74 %, SL 21 %, Ambiguous 3 %, TimeHorizon 2 %). Le **P0-4 ci-dessous est donc caduc** ; il reste seulement à faire que le backtest **ne simule pas** un plan `PLAN_REJECTED` (fait le 2026-08-30, `ExecutionSimulator` guard).

### 2.4 Anomalie de conception `StructuralBreak`

Deux objets disjoints portent ce nom (Lot 17 §1) :

| | `FusionDimension.StructuralBreak` (D6) | `MarketState.StructuralBreak` (régime) |
|---|---|---|
| Producteur | `StructuralBreakEvidenceRule` (fusion) | `StructuralBreakRule` (décision) |
| Entrées | `Evidence.Cusum` (+ `BaiPerron`) — **vraie evidence de rupture** | `1 − {StructuralStability, Persistence, MeanReversion, Stationarity}` — **aucune evidence de rupture** |
| Consommé en aval ? | **par personne** (aucune `IDecisionRule`, aucun dashboard) | par l'arbitrage → `Decision.Winner` |

Donc : CUSUM et Bai-Perron sont calculés à chaque barre **pour rien**, et le régime `StructuralBreak` est sélectionné sur un signal auto-référentiel. De plus, `StructuralStabilityRule` **n'est pas dans la liste des `IFusionRule` câblées** (`IQIAIndicator.cs:66-72` en câble 5 ; celle-ci en est absente) — la dimension `StructuralStability` est injectée séparément par `FusionStateManager` à partir de la variabilité *récente des 4 autres dimensions* (`FusionProfileAnalyzer`).

### 2.5 Bug de libellé dans les explications de décision

Les 5 règles écrivent `Decision Confidence : {FormatScore(qualityScore)}` alors que `builder.Confidence` reçoit `finalScore` (ex. `MeanRevertingRule.cs:99`, idem `Trending/StructuralBreak/RandomWalk/StableRange`). La valeur affichée « Decision Confidence » est en réalité le `qualityScore`, déjà affiché à la ligne au-dessus. Se propage au dashboard et au dataset.

---

## 3. Axe 2 — Calibration scientifique

### 3.1 Ce qui est solide

- **Evidence de régime** (`Engine/Regime/Evidence/`) : ADF, KPSS, Hurst, HalfLife, VarianceRatio, CUSUM, Volatility, Bai-Perron, DFA — implémentations réelles (math, régression, golden datasets, validation). HalfLife et VarianceRatio ont une **validation numérique externe** (scikit-learn / NumPy, tolérance 1e-6). Limite : mono-fenêtre par modèle, pas de multi-fenêtre / consensus.
- Les 5 modèles scientifiques de `MeanReversion` sont testés individuellement (`Tests/ScientificModels/*Tests.cs`).

### 3.2 Ce qui bloque la calibration

| # | Problème | Source |
|---|---|---|
| 1 | Le framework `Backtest/Calibration/` (21 fichiers) ne relie **aucun `CalibrationParameterSet`** à un point d'injection réel du pipeline → une grille de N configs produit N fingerprints pour un comportement **strictement identique**. Seule exception : `AmbiguityGateThreshold` (via `PipelineParameterOverrides`), et il n'agit **que** sur `MeanReverting`. | Lot 14.9 §1 pt 6 ; `SignalEngine` |
| 2 | `OverallConfidence = SuccessfulModels / 5` — un **ratio de complétude d'exécution**, pas une confiance statistique — mais alimente **5 seuils de production** comme si c'en était une. Captures : valeur binaire exacte `1.0` / `0.0`, jamais intermédiaire. | Lot 14.9 faiblesse #4 |
| 3 | Poids Fusion (4 règles) et Decision (5 règles) : **`Provisional`**, jamais calibrés empiriquement. | 9 × `Engine/**/Rules/*.cs` |
| 4 | **Biais de mesure de performance** : fill au `Close` de la **barre du signal** (biais optimiste, connu depuis Lot 13 défaut L-2, jamais corrigé). Coûts + slippage **désactivés par défaut** partout, y compris dans la campagne de calibration existante. → toute mesure P&L actuelle est optimiste. | Lot 14.9 faiblesses #1-2 |
| 5 | `TradePlanBuilder.PositionSize` peut dépasser `MaxQuantity` (mesuré moy 51.5, max 160, cap 50) — aucun plafond de quantité dans `TradePlanBuilder` ; seul le vrai `RiskEngine.Evaluate` plafonne. Deux chemins de sizing **non unifiés**. | Lot 15.3 §1, §12 |
| 6 | Aucune **`RiskPolicy` de production**. `RiskEngine` mécaniquement validé (Lot 10) mais jamais alimenté par une politique réelle. | Lot 14.9 |
| 7 | Recherche StopLoss (`Tests/Research/StopLossCalibration/`, Sprints 15.9-15.19) : campagne complète mais **100 % synthétique**, verdict `K_SELECTED: NO`. *« REAL MARKET VALIDATION = NOT PERFORMED »*. | `QDE-012_StopLoss_Calibration_Protocol.md` |

### 3.3 Ordre de calibration (une fois les P0 levés)

Dérivé du code (Lot 14.9 §19), **jamais avant** que la capacité de trading soit débloquée :
`Evidence → Fusion → Decision → Entry/Trigger → Stop Loss → Risk`.

### 3.4 Effet de bord connu

La suite de tests complète réécrit `Tests/Research/StopLossCalibration/Output/{A1_calibration_summary.txt, campaign_summary.txt, A1_A2_Hybrid_trending.csv}` — artefact inoffensif, pas une régression.

---

## 4. Axe 3 — Dashboard (audit du code de rendu)

Le code (`Visualization/`) est **défensif et globalement propre** — pas de crash évident. Problèmes trouvés :

| # | Gravité | Problème | Emplacement |
|---|---|---|---|
| 1 | Moyen | **Glyphes emoji/Unicode probablement non rendus par ATAS.** `Badge` (🟢🟡🔴), `StatusLine` (✓/✗), `Gauge` (`██`/`░░`), marqueurs `●`. `RenderContext.DrawString` + `RenderFont("Arial", 8.5f)` : les codepoints emoji (U+1F7E2…) s'affichent en général comme des carrés vides dans le moteur texte d'ATAS. La barre « System Health » en haut (via `Badge`) est la plus exposée. **À confirmer visuellement.** | `Visualization/Rendering/DashboardCanvas.cs` (`Badge`, `StatusLine`, `Gauge`) |
| 2 | Faible | **6ᵉ dimension manquante.** Le panneau « Dimensions (raw vs. stable) » liste 5 dimensions, omet `FusionDimension.StructuralBreak` (ajoutée Lot 15.8). | `Visualization/Dashboards/DecisionDashboard.cs:25-32` |
| 3 | Faible | Étape 1 « SCIENTIFIC ASSESSMENT » : « Score » **et** « Confidence » affichent tous deux `OverallConfidence` (doublon / libellé trompeur). | `DecisionDashboard.cs:52-53` |
| 4 | Faible | Panneaux 900-990 px + barres 990 px : sur un chart < ~1000 px visibles, `WidthWarning` remplace le panneau par « chart trop étroit (Xpx visibles / Ypx requis) ». Voulu, mais peut surprendre. | `Visualization/Rendering/DashboardLayout.cs` |
| 5 | À vérifier | Débordement vertical possible : `ScientificDashboard.Height = 560` fixe ; `DrawEvidence` (9 lignes) démarre après les cartes modèles dépliées — si 2 lignes de cartes dépassent, la section « Preuves de régime » déborde sur les chandeliers. | `Visualization/Dashboards/ScientificDashboard.cs:54` |
| 6 | Faible | `KeyNotFoundException` possible : `rendererDetails["Position"|"Color"|"Text"]` indexés sans garde ; si `_atasRenderer.Describe()` n'a pas la clé → exception dans `OnRender`, catchée puis **re-lancée**. Uniquement si le tracing pipeline est actif. | `IQIAIndicator.cs:1136-1138` |
| 7 | Corrigé | Le catch du stage Risk n'enregistrait que `exception.Message` (perdait `InnerException`). **Corrigé le 2026-08-29** (`DescribeExceptionChain`, commit `9475fbc`). | `IQIAIndicator.cs:898` |

---

## 5. Autres constats

- `IQIAIndicator` a **zéro couverture de test directe** (ne s'instancie pas hors ATAS) — seul un passage manuel ATAS valide `OnCalculate`/`OnRender`.
- Crash natif intermittent (`0xC0000005`) de la suite xUnit v3 en run complet — **infra**, non reproductible, à re-lancer avant d'investiguer.
- Capture MNQ 17:53 : `TypeInitializationException` sur `ATASEquityReplayDetector` à chaque barre — **artefact du hot-swap de DLL sans redémarrage ATAS**, disparu après redémarrage (captures 18:01 / 18:12 propres). Pas un bug de code.

---

## 6. Feuille de route priorisée

### P0 — débloquer la capacité de trading (rien à calibrer avant)

| # | Action | Fichier(s) |
|---|---|---|
| P0-1 | Câbler une méthodologie **Stop Loss en live**, en miroir de `BacktestEngine.cs:356-381` (`VolatilityStopLossModel`). Sans ça `PLAN_READY` reste inatteignable en production. | `IQIAIndicator.cs` (~575) |
| P0-2 | ~~implémenter un modèle réel pour `Trending`~~ **FAIT le 2026-08-30** (`TimeSeriesMomentumModel` réel + câblage complet + calibration). **Résultat : Trending est fonctionnel mais SANS EDGE** — voir §8. | `Engine/ScientificModels/Trend/`, `ScientificModelRegistry.cs` |
| P0-3 | **Décision utilisateur** : réparer la sémantique `StructuralBreak` — soit `StructuralBreakRule` consomme la dimension D6 (CUSUM/Bai-Perron), soit renommer le régime pour ne pas prétendre détecter une rupture. | `StructuralBreakRule.cs`, câblage `IQIAIndicator.cs` |
| P0-4 | ~~`ExecutionSimulator` : clôturer une position via le **Stop Loss**~~ **CADUC** — déjà fait (Lots 15.4/15.5). Ne restait que : ne pas simuler un plan `PLAN_REJECTED` — **fait le 2026-08-30**. | `Backtest/Execution/ExecutionSimulator.cs` |
| P0-5 | Corriger les biais de mesure : fill à l'**ouverture de la barre suivante** ; activer coûts + slippage par défaut dans la campagne de calibration. | `Backtest/Execution/`, `Backtest/Cost/` |

### P1 — rendre la calibration possible

| # | Action |
|---|---|
| P1-1 | Relier `CalibrationParameterSet` à de **vrais points d'injection** (poids Fusion, poids Decision, seuils Entry) — sinon la grille ne fait rien varier. |
| P1-2 | Remplacer `OverallConfidence = SuccessfulModels/5` par une vraie confiance statistique, **ou** découpler les 5 seuils qui la consomment. |
| P1-3 | Plafonner `PositionSize` dans `TradePlanBuilder`, ou unifier sur le vrai `RiskEngine`. |
| P1-4 | Définir une `RiskPolicy` de production. |

### P2 — dashboard & hygiène

| # | Action |
|---|---|
| P2-1 | Remplacer les glyphes emoji par des marqueurs sûrs pour ATAS (`[OK]` / `[!]` / `[X]`, `#` / `.` pour les jauges). |
| P2-2 | `DecisionDashboard` : ajouter la 6ᵉ dimension ; corriger le libellé Score/Confidence de l'étape 1. |
| P2-3 | Corriger `Decision Confidence : {qualityScore}` → `{finalScore}` dans les 5 règles de décision. |
| P2-4 | Garder l'accès dictionnaire de `IQIAIndicator.cs:1136` (`TryGetValue`). |
| P2-5 | Vérifier les débordements verticaux `ScientificDashboard` / `DecisionDashboard` avec les hauteurs réelles à l'écran. |

---

## 7. Une phrase

Le moteur est bien construit mais il ne trade rien : avant toute calibration, il faut (a) câbler le Stop Loss en live, (b) trancher le sort des 4 régimes non couverts, (c) réparer la sémantique `StructuralBreak`, (d) corriger les biais de mesure du backtest.

---

## 8. Suivi 2026-08-30 — P0-1, P0-4, P2 (partiel) et P0-2 traités

**Commits `9475fbc`..`82518aa` puis le lot P0-2 :**

- **P0-1 (Stop Loss live)** : `IQIAIndicator.cs` résout maintenant `TradeRiskParameters` pour un candidat directionnel (SL via `VolatilityStopLossModel`, RiskPerTrade via `RiskInitialCapital × RiskPolicyMaxRiskPerTradePercent`). Nécessite « Capital initial » + « Risque max par trade (%) » côté ATAS. En **Replay pur**, le RiskEngine rejette toujours `INVALID_EQUITY` (ATAS ne fournit pas de série d'équité replay — décision Lot 12.2) : la validation complète du RiskEngine demande un compte Sim/live.
- **P0-4** : caduc — `ExecutionSimulator` fait déjà le monitoring intrabar SL/TP (Lots 15.4/15.5). Backtest MES M5 ~45 j : **96 % des sorties sur SL/TP**. Ajouté : `ExecutionSimulator` ne simule plus un plan `PLAN_REJECTED`.
- **Gate R:R minimum** : `TradeRiskParameters.MinRiskReward` + nouveau statut `TradePlanStatus.PLAN_REJECTED` (opt-in, câblé sur le paramètre « Risk/Reward minimum »). `RiskEngineRequestFactory` et `ExecutionSimulator` ignorent un plan rejeté.
- **P2 (glyphes dashboard)** : marqueurs ASCII (`[+]/[!]/[x]`, `#`/`-`, `v`/`>`) — les emoji ne sont pas rendus par le moteur texte d'ATAS (confirmé sur capture).
- **#3 Distance équilibre** : `SignalEngine` peuple `DistanceToEquilibrium = CurrentPrice − EstimatedMean`.

### P0-2 — Trending : implémenté, calibré, SANS EDGE

**Implémentation** (6 fichiers production) :
- `TimeSeriesMomentumModel` — vrai TSMOM multi-horizon (Moskowitz et al. 2012, cf. R-005) : t-stat de momentum normalisé par la volatilité sur plusieurs lookbacks, accord inter-horizons, autocorrélation lag-1 des rendements, expose aussi `CurrentVolatility` (unités de prix) pour le stop.
- `ScientificModelRegistry` → `{ TimeSeriesMomentumModel }` pour `TrendFollowingMethodology` (`VolatilityModel`/`SPRTModel` restent hard-gated `MeanReverting`).
- `ScientificAssessmentBuilder.ExpectedModels` devient dépendant de la méthodologie (transmis par `SignalEngine`) — une barre Trending n'est plus jugée « il manque les 5 modèles MeanReversion ».
- `EntryTriggerBuilder` — branche `Winner == Trending` : direction = continuation du momentum, plancher `MinMomentumConfidence` (défaut 0.10), gate d'ambiguïté partagé. Nouvelles raisons `INSUFFICIENT_MOMENTUM` / `MOMENTUM_UNAVAILABLE`.
- `TradePlanBuilder` — Take Profit tendance = R-multiple (`entry ± R × distance_stop`) quand la cible d'équilibre est absente/défavorable.
- `PipelineParameterOverrides` — 4 champs (`MomentumLookbacks`, `MinMomentumConfidence`, `StopVolatilityMultiplier`, `TakeProfitRMultiple`) threadés via `SignalEngine`.
- `YahooSymbolMap` +5 marchés vérifiés live (NQ, YM, RTY, GC, CL).

**Calibration** (`Tests/Research/TrendingCalibration/`) : grille 81 cellules (lookbacks {court/défaut/long} × MinMomentumConfidence {0.05, 0.15, 0.30} × stop {1.5, 2.5, 3.5}×σ × TP {1.5, 2.5, 3.5}R), 6 marchés M5 ~59 j, split croisé TRAIN {NQ, RTY, GC} / OOS {ES, YM, CL} + split temporel purgé 70/30. Métrique : espérance par trade en R.

**Résultat : les 81 cellules ont une espérance-R médiane cross-market NÉGATIVE.** Meilleure cellule `défaut/mc0.30/sl3.5/tp1.5` : **−0,082 R/trade**, ne généralise pas (OOS temporel −0,32). PnL $ (1 % de $25 000 par trade, sans coûts) : défauts = **−$40 675** sur 6 marchés / ~−$6 800 par marché ; meilleure cellule = **−$6 092** total ; **0 cellule profitable sur 81**.

**Conclusion** : Trending est *fonctionnel* mais l'entrée telle que conçue (signe du momentum multi-horizon dans une barre étiquetée « Trending » par des poids `Provisional`) **n'a pas d'edge**, et aucun des 4 leviers ne le corrige. Aucune calibration livrée — les défauts restent inchangés. Rendre Trending (ou MeanReverting) rentable relève d'une refonte de l'entrée/du modèle (P1+), pas d'un réglage.

### P0-2b — Deux expériences supplémentaires 2026-08-30 : ni le R:R ni le stop ne sont le goulot

**(a) Balayage de la porte R:R** (MES=F M5 45 j, pipeline lancé une fois porte OUVERTE, seuils rejoués en post-filtrage — effet identique à régler `MinRiskReward`). MeanReverting est **brut-positif** sur une large bande de bas R:R (pic **+15 428 $** à minR:R 0,25, 1172 trades, 69 % de réussite) mais **ne survit à aucun coût réaliste** : dès 2 $/contrat aller-retour le net tombe à **−17 314 $** (bord brut ~+13 $/trade < coût/trade). Seul minR:R 1,5 (3 trades) est net-positif → non significatif. Trending : la porte R:R est **inerte** (chaque plan Trending fait exactement 2R par construction) — net −10 à −15 k$ avec coûts, identique à tous les seuils.

**(b) Balayage du placement du stop** — `Tests/Research/StopPlacement/StopPlacementSprintRunner.cs` (`[Fact] Run`, réutilise le `_cache` quotidien de TrendingCalibration, ~28 min). **R:R figé à 1,5**, 23 méthodes de stop × 2 régimes × 6 marchés, splits cross-market TRAIN {NQ,RTY,GC} / OOS {ES,YM,CL} + temporel purgé. Familles : A bande de volatilité actuelle, B k×ATR14 (k ∈ 1→3), C swing sur {6/12/24} barres + tampon ATR, D chandelier, E % du prix, F extrême de barre ± ticks.

- **MeanReverting** : meilleure = `B_atr3.0` à **+0,0199 R (~+5 $/trade)** sur TRAIN, mais **échoue en OOS cross-market (−0,0115 R)**. Positive uniquement parce que **NQ** (dans le TRAIN) est le seul marché qui fonctionne (+0,05→0,07 R, 51 % de réussite) — tous les autres marchés sont plats/négatifs. Artefact mono-marché, pas un edge.
- **Trending** : **toutes les méthodes négatives sur TRAIN** (meilleure `C_swing24_b0.5` à **−0,148 R ≈ −37 $/trade**). Seul **GC** est positif (+0,40 R sur 93 trades = bruit) et le réglage du stop tue même ce coup de chance.
- Un stop plus large est marginalement « moins mauvais » dans les deux régimes (sorties sur bruit intra-barre) mais jamais suffisant.

**Conclusion P0-2b** : trois analyses indépendantes (grille 81 cellules, balayage R:R, balayage stop) **convergent** — le goulot n'est ni la porte R:R ni le placement du stop, c'est **l'entrée qui n'a pas d'edge cross-market**. Cause racine : poids `Provisional` de Fusion/Décision non calibrés → les étiquettes de « régime » ne correspondent pas à de vrais régimes. Le prochain levier réel est la **calibration Evidence→Fusion→Decision (P1)**, pas un réglage Entry/Stop/TradePlan.
