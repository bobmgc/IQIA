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
4. Le **Lot 15.3** a créé `Engine/Risk/VolatilityStopLossModel.cs` (`StopPrice = EntryPrice ∓ 2.0 × CurrentVolatility`) mais l'a câblé **uniquement dans `BacktestEngine.cs:374`, jamais dans `IQIAIndicator.cs`** (live). Le multiplicateur `2.0` est **non calibré** (`K_SELECTED: NO`, Lot 15.14). `ExecutionSimulator` **n'utilise pas** le SL (sortie à horizon temporel fixe).

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
| P0-2 | **Décision utilisateur** : implémenter au moins un modèle réel pour `Trending` (régime le plus fréquent après MeanReverting/StructuralBreak) — **ou** assumer explicitement « MeanReverting-only » et l'afficher clairement. | `Engine/ScientificModels/Trend/`, `ScientificModelRegistry.cs` |
| P0-3 | **Décision utilisateur** : réparer la sémantique `StructuralBreak` — soit `StructuralBreakRule` consomme la dimension D6 (CUSUM/Bai-Perron), soit renommer le régime pour ne pas prétendre détecter une rupture. | `StructuralBreakRule.cs`, câblage `IQIAIndicator.cs` |
| P0-4 | `ExecutionSimulator` : clôturer une position via le **Stop Loss** (aujourd'hui horizon fixe) — sinon toute mesure de perf du SL est fictive. | `Backtest/Execution/ExecutionSimulator.cs` |
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
