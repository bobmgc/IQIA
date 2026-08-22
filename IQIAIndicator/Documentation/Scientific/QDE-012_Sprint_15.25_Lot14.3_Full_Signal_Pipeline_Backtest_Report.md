# QDE-012 — Sprint 15.25 — Lot 14.3
# Full IQIA Signal Pipeline Backtest — Yahoo MES M5 → MarketContext → Regime → Decision → Signal → Entry → EntryTrigger → TradePlan

**Type** : LOT D'IMPLÉMENTATION
**Portée** : câblage du pipeline scientifique complet (Regime → Fusion → Decision → Methodology → Signal → Entry → EntryTrigger → TradePlan) dans le Backtest Engine, sur données historiques Yahoo/synthétiques
**Date** : 2026-08-22
**Branche** : `feature/structural-stability-v2`

---

## 1. OBJECTIVE

Faire traverser l'intégralité du pipeline scientifique IQIA existant — jusqu'au `TradePlan` — à des données historiques, à travers le `BacktestEngine` posé par le LOT 14.1 et la source Yahoo posée par le LOT 14.2 :

```
Yahoo MES M5 → YahooHistoricalBarSource → HistoricalSeries → BacktestEngine
  → MarketContext → RegimeEngine → EvidenceFusionEngine(Fusion) → FusionStateManager
  → DecisionEngine → MethodologyEngine → SignalEngine (ScientificModels → EntryEngine → EntryTriggerEngine)
  → TradePlanEngine → BacktestSignalResult
```

Ce lot est un LOT DE REPLAY SCIENTIFIQUE : aucun ordre envoyé, aucun broker, aucun TradingManager, aucune API ATAS, aucun Risk Engine, aucun P&L, aucun coût/slippage/exécution simulés, aucun seuil scientifique modifié.

---

## 2. EXISTING PIPELINE DISCOVERED (inspection avant codage)

Inspection directe de `IQIAIndicator.OnCalculate` (IQIAIndicator.cs:436-886) et des classes qu'il appelle — aucune architecture supposée. Ordre exact découvert :

1. `MarketContextBuilder.Build` (ATAS) / `MarketContextFactory.CreateHistorical` (Backtest, LOT 14.1) → `Core.MarketContext`, puis `MarketContextValidator.Validate`.
2. `RegimeEngine.Collect(context)` → `EvidenceSet` (buffer interne borné à 128 barres — `DfaWindowSize`/`BaiPerronWindowSize`).
3. `EvidenceFusionEngine` (namespace `Engine.Fusion`, aliasé `FusionEngine` dans IQIAIndicator.cs) `.Fuse(FusionContext{Evidence, Timestamp, Symbol, TimeFrame, EvaluationId})` → `FusionResult`. **`EvaluationId` est un `Guid` déclaré sur `FusionContext` mais jamais lu par aucune règle ni aucun consommateur** (vérifié par grep exhaustif sur `Engine/`) — un simple tag de corrélation télémétrique côté ATAS (`Guid.NewGuid()`).
4. `FusionStateManager.Update(fusionResult, timestamp)` → `FusionSnapshot`. **Seul composant du pipeline câblé par ce lot qui porte un état mutable entre barres** (`_previousStableResult`, `_updateCount` — lissage EMA + hystérésis).
5. `DecisionEngine.Evaluate(DecisionContext{FusionResult=snapshot.StableResult, Evidence})` → `DecisionResult` (Winner/AmbiguityScore/Candidates...). Construit avec exactement les 5 mêmes `IDecisionRule` qu'IQIAIndicator.cs (StableRange/Trending/MeanReverting/StructuralBreak/RandomWalk), via son propre `DecisionArbitrator` interne (protégé, non modifié).
6. `MethodologyEngine.Evaluate(decisionResult)` → `MethodologySelection` (résolution via `MethodologyRegistry`, non modifié).
7. `CreateScientificMarketContext` (IQIAIndicator.cs:1226) construit une fenêtre glissante de 500 closes se terminant à la barre courante → `Engine.ScientificModels.Abstractions.MarketContext`.
8. `SignalEngine.Process(scientificContext, methodologySelection)` — orchestrateur interne découvert par lecture : `ScientificModelRegistry.Resolve` (seule `MeanReversionMethodology` a de vrais modèles : Kalman/OU/DynamicZScore/Volatility/SPRT — les 4 autres méthodologies retournent `Array.Empty<IScientificModel>()`, `BOCPDModel`/`TimeSeriesMomentumModel` étant des stubs explicitement non câblés) → `ScientificFusionEngine.Assess` → `EntryEngine.Process(EntryContext(scientificAssessment))` → `EntryCandidate` → construction d'`EntryBusinessContext` → `EntryTriggerEngine.Process(EntryTriggerContext(...))` → `EntryTriggerCandidate` (via `EntryTriggerBuilder`, protégé) → Visualization/ChartAnnotation/OpportunityPresentation (hors périmètre de ce lot, non câblés dans le Backtest).
9. Retour dans `OnCalculate` : si `signalEngine.LastEntryTriggerCandidate` non nul → `TradePlanEngine.Process(TradePlanContext(entryTriggerCandidate, instrumentInfo))` → `TradePlan` (via `TradePlanBuilder`, protégé). Sinon `TradePlan = null`.
10. RiskEngine (Lot 11/12) : strictement après TradePlan, purement observationnel, **explicitement hors périmètre de ce lot** (brief §31).

**Logique directionnelle confirmée par lecture directe de `EntryTriggerBuilder.DetermineDirection`** (protégé, non modifié) : `Winner != MeanReverting` → `NO_ACTION` ; `AmbiguityScore >= 0.95` (Difference ≤ 0.05) → `NO_ACTION`/`DECISION_AMBIGUOUS` ; sinon `DynamicZScore < 0 → BUY_CANDIDATE`, `> 0 → SELL_CANDIDATE`, `== 0 → NO_ACTION`/`PRICE_AT_EQUILIBRIUM`. `AmbiguityGateThreshold = 0.95` intact.

**Composants stateful identifiés** (inspection champ par champ) :
- `RegimeEngine` — buffer 128 barres (déjà isolé par le LOT 14.1).
- `FusionStateManager` — `_previousStableResult`/`_updateCount` (**nouveau dans ce lot**).
- `DecisionEngine`, `EvidenceFusionEngine` (Fusion), `MethodologyEngine`, `SignalEngine`, `TradePlanEngine`, `EntryEngine`, `EntryTriggerEngine` — **aucun champ mutable** (vérifié par lecture de chaque classe : `EntryTriggerBuilder`/`EntryAssessmentBuilder` sont ré-instanciés à chaque appel, `ScientificModelRegistry.Resolve` instancie un modèle scientifique neuf à chaque résolution).

**Champs `DateTime.UtcNow` recensés** (non-déterminisme wall-clock, jamais un look-ahead) : `EntryCandidate.CreatedAt`, `EntryTriggerAssessment.Timestamp`, `EntryTriggerCandidate.CreatedAt`, `MethodologySelection.Timestamp`, `TradePlan.Timestamp`, plus les timestamps internes de `VisualizationEngine`/`ChartAnnotationBuilder`/`OpportunityPresentationBuilder` (non câblés par ce lot). Exclus du fingerprint déterministe — voir §8.

---

## 3. BACKTEST ARCHITECTURE

Nouvelle méthode `BacktestEngine.RunSignalPipeline(BacktestScenario, int warmupBars) → BacktestSignalPipelineResult`, **distincte** de `Run()` (LOT 14.1, inchangée dans son contrat — voir §16.2). Raison : `Run()` est exercée par 87 tests LOT 14.1 sur son contrat exact `BacktestFoundationResult` ; en créer une variante plutôt que la modifier respecte le brief §37 (« ne pas modifier les anciens tests ») et §43 (« ne pas refactorer »).

Le seul changement apporté à `Run()` : extraction pure de la construction+validation du `MarketContext` vers `BuildValidatedContext` (méthode privée statique), partagée par `Run()` et `RunSignalPipeline`. Diff strictement une extraction — deux lignes déplacées, aucune valeur, aucune branche modifiée. Preuve de non-régression : les 153 tests LOT 14.1+14.2 (`IQIAIndicator.Tests.BacktestTests`) restent 153/153 PASS après l'extraction (§14).

`RunSignalPipeline` instancie, **localement à l'appel, jamais en champ** : `RegimeEngine`, `MarketContextValidator`, `EvidenceFusionEngine` (4 règles Fusion identiques à IQIAIndicator.cs), `FusionStateManager`, `DecisionEngine` (5 règles Decision identiques), `MethodologyEngine`, `SignalEngine`, `TradePlanEngine`. Aucune classe scientifique n'est réimplémentée ni sous-classée — les mêmes classes, les mêmes listes de règles, dans le même ordre que le chemin ATAS live.

`EvaluationId` (Guid inerte, §2) est fixé à `Guid.Empty` plutôt que `Guid.NewGuid()` — un run déterministe n'a pas besoin d'un identifiant de corrélation aléatoire, et ce champ n'influence aucune règle (vérifié par grep).

---

## 4. DATA FLOW

```
HistoricalSeries.Bars[index]
  → MarketContextFactory.CreateHistorical(bar, index, count, ...)     [BuildValidatedContext, partagé avec Run()]
  → MarketContextValidator.Validate                                    → si invalide : BacktestSignalStatus.Rejected, pipeline jamais exécuté
  → RegimeEngine.Collect                                                → EvidenceSet
  → EvidenceFusionEngine.Fuse(FusionContext{Evidence, EvaluationId=Guid.Empty})
  → FusionStateManager.Update                                          → FusionSnapshot (état porté par CETTE instance uniquement)
  → DecisionEngine.Evaluate(DecisionContext{FusionResult=snapshot.StableResult, Evidence})
  → MethodologyEngine.Evaluate                                         → MethodologySelection
  → BuildScientificMarketContext(series, index, context)                [fenêtre 500 closes, jamais series.Bars[index+1..]]
  → SignalEngine.Process                                                → OpportunityPresentation, LastEntryCandidate, LastEntryTriggerCandidate
  → (si EntryTriggerCandidate non nul) TradePlanEngine.Process          → TradePlan
  → BacktestSignalResult (un par barre) → BacktestSignalPipelineResult
```

Aucune étape ne lit `series.Bars[index+1..]` — voir §7.

---

## 5. WARM-UP

Aucun nombre n'a été inventé. `warmupBars` reste, comme au LOT 14.1, un paramètre explicite fourni par l'appelant — pas une constante câblée dans le moteur. La seule contrainte réelle découverte dans le code : `RegimeEngine` a un buffer interne borné à **128 barres** (`DfaWindowSize`/`BaiPerronWindowSize`, `RegimeEngine.cs`), la fenêtre la plus longue de tout le pipeline de Regime — c'est la valeur utilisée dans tous les tests de ce lot comme seuil de warmup recommandé.

Avant ce seuil, `BacktestSignalStatus.Warmup` est un label **Backtest-level uniquement** (brief §5) — les moteurs réels tournent quand même, sans version dégradée artificielle. Le pipeline réutilise la notion déjà existante `EntryTriggerStatus.NOT_READY` (enum pré-existant, `EntryTriggerBuilder.DetermineTriggerStatus`) plutôt que d'inventer un état équivalent : aucune décision artificielle n'est créée pendant le warmup, seule l'étiquette de comptage `Warmup` distingue ces barres des barres `Ready`.

Testé par `BacktestSignalPipelineStageTests.Warmup_BarsBelowThreshold_AreLabeledWarmup_ButStillCarryRealPipelineOutput` et `FixtureB_ShortSeries_NeverProducesReadyBars_ButStillRunsThePipeline`.

---

## 6. STATEFUL COMPONENTS

| Composant | État | Traitement |
|---|---|---|
| `RegimeEngine` | Buffer 128 barres | Instance neuve par `RunSignalPipeline` (hérité LOT 14.1) |
| `FusionStateManager` | `_previousStableResult`, `_updateCount` (EMA+hystérésis) | **Instance neuve par `RunSignalPipeline`** (nouveau ce lot) |
| `DecisionEngine`/`EvidenceFusionEngine`(Fusion)/`MethodologyEngine`/`SignalEngine`/`TradePlanEngine` | Aucun champ mutable (vérifié par lecture) | Instanciés localement par discipline, pas par nécessité |
| `EntryEngine`/`EntryTriggerEngine` | Aucun champ ; `EntryTriggerBuilder`/`EntryAssessmentBuilder` ré-instanciés à chaque appel interne | Aucune action requise |
| Modèles scientifiques (Kalman/OU/DynamicZScore/Volatility/SPRT) | Aucun champ ; `ScientificModelRegistry.Resolve` instancie un modèle neuf à chaque résolution | Aucune action requise |

**INTERDIT respecté** : aucun état ATAS, aucun run précédent, aucune autre simulation n'est jamais réutilisé silencieusement — chaque `RunSignalPipeline` construit son propre graphe d'instances de zéro.

---

## 7. LOOK-AHEAD PROTECTION

`BuildValidatedContext` (partagé avec `Run()`) ne lit jamais `series.Bars[index+1..]` — hérité tel quel du LOT 14.1. `BuildScientificMarketContext` (nouveau) applique la même discipline : `startIndex = Max(0, index-499)`, boucle `for (i = startIndex; i <= index; i++)` — jamais `index+1` ou au-delà.

**Test bloquant** `BacktestSignalPipelineLookAheadTests.AppendingFutureBars_NeverChangesAnyDecisionAlreadyProducedForAnEarlierBar` : Dataset A = 320 barres OU mean-reverting (seed 13) ; Dataset B (le run « court ») = `Truncate` des 220 premières — garantit un préfixe strictement identique (même technique que le test look-ahead du LOT 14.1) plutôt que deux séries indépendamment générées. Comparaison, pour les 220 barres communes, de `Decision.{Winner, AmbiguityScore, WinnerScore, State, Confidence, Candidates.Length}`, `Methodology.SelectedMethodology.Name`, `Entry.{OpportunityStatus, OpportunityPriority}`, `EntryTrigger.Assessment.{Direction, TriggerStatus, Reason, ScientificConfidence, EstimatedEquilibrium, DistanceToEquilibrium}`, `EntryTrigger.CurrentPrice`, `TradePlan.{Status, Direction, EntryPrice, StopLoss, TakeProfit, RiskPerUnit, PositionSize, RiskRewardRatio}` — **220/220 identiques, 0 divergence**.

Ce test ne re-compare pas `EvidenceSet` champ par champ : le LOT 14.1 l'a déjà exhaustivement prouvé au niveau Regime, et `RunSignalPipeline` réutilise le même `BuildValidatedContext`/`RegimeEngine.Collect` que `Run()` (extraction pure, §3) — re-dériver cette preuve aurait été redondant (brief §29, priorité à l'exactitude sans re-dérivation inutile).

---

## 8. DETERMINISM

Nouveau fichier `Backtest/BacktestSignalFingerprint.cs` (ne modifie pas `BacktestFingerprint.cs` du LOT 14.1 — réutilise sa primitive publique `Sha256Hex` et sa méthode `AppendBar`, même stratégie que `HistoricalSeriesFingerprint` au LOT 14.2). Canonicalise, par barre : Status/Reason, la ligne `AppendBar` existante (Regime+Context), puis Decision (Winner/WinnerScore/AmbiguityScore/State/Confidence/comptes), Methodology (Name/Version), Entry (Status/Priority/comptes), EntryTrigger (TriggerStatus/Direction/Confidence/Priority/Reason/Equilibrium/Distance/CurrentPrice/comptes), TradePlan (Valid/Status/Direction/Entry/SL/TP/RiskPerUnit/RiskAmount/PositionSize/RR/comptes), Exception (Stage/Type).

**Exclu délibérément** (même logique que le LOT 14.1 pour `Explanation`) : tous les champs `DateTime.UtcNow` recensés en §2 (`CreatedAt`/`Timestamp` sur Entry/EntryTrigger/Methodology/TradePlan) et le contenu textuel libre (`Diagnostics`/`Warnings`/`InvalidationReason`/`Explanation`) — seul leur **compte** est hashé, comme structure, jamais leur texte.

Tests bloquants (`BacktestSignalPipelineDeterminismTests`) :
- Même scénario, deux exécutions → `DeterministicHash` identique (incl. BuyCount/SellCount/NoActionCount/TradePlanSignalOnlyCount).
- Même scénario, deux exécutions séparées par un **vrai délai d'horloge murale (50ms)** → hash identique — preuve empirique, pas seulement une inspection de code, que les `DateTime.UtcNow` recensés ne fuient jamais dans le hash.
- Une seule barre modifiée (Close +5) → hash différent.
- `warmupBars` différent → hash différent, `ScenarioId` identique (le warmup est un paramètre du run, pas de l'identité du scénario — hérité LOT 14.1).

---

## 9. RUN ISOLATION

`FusionStateManager` étant le seul état neuf de ce lot, l'isolation par run repose entièrement sur la discipline « une instance locale par appel » (§6). Tests bloquants (`BacktestSignalPipelineRunIsolationTests`) :
- Run A → Run B (scénario différent) → Run A à nouveau, sur **la même instance de `BacktestEngine`** : le second Run A est bit-identique au premier.
- Run « MES-like » → autre fixture → Run « MES-like » à nouveau : second run identique au premier.
- Deux instances `BacktestEngine` indépendantes, même scénario → résultats identiques.
- Enchaînement long run → court run → court run à nouveau : aucune exception, résultats du court run stables.

---

## 10. STAGE OBSERVABILITY

`BacktestSignalResult` (un par barre) porte des **références directes** aux objets réels produits par chaque étage (`EvidenceSet`, `DecisionResult`, `MethodologySelection`, `OpportunityPresentation`, `EntryCandidate`, `EntryTriggerCandidate`, `TradePlan?`) — jamais une copie de leurs champs (brief §16 : « ne pas copier inutilement tous les objets internes »). Un champ `null` signifie « cet étage n'a jamais été atteint pour cette barre » ; `Status`/`Reason` expliquent pourquoi (`Rejected` avec les erreurs de validation, `Exception` avec l'étage/type/message qui a échoué). Toute barre est retrouvable par `Timestamp → Regime → Decision → Methodology → Signal → Entry → EntryTrigger → TradePlan` en O(1) via `BacktestSignalPipelineResult.Bars[index]`.

Compteurs techniques exposés par `BacktestSignalPipelineResult` (§17 du brief) : `BarsProcessed`, `BarsRejected`, `WarmupBars`, `ReadyBars`, `RegimeDetectedCount` (Winner≠Unknown), `DecisionCount` (Candidates.Length>0 — même population que RegimeDetectedCount par construction de `DecisionEngine.Evaluate`, documenté explicitement plutôt que masqué), `SignalCount` (`ExecutedModels.Count>0`), `EntryCandidateCount` (OpportunityStatus≠NOT_QUALIFIED), `BuyCount`/`SellCount`/`NoActionCount`/`WatchCount`, `TradePlanSignalOnlyCount`/`TradePlanReadyCount`/`TradePlanNoTradeCount`/`TradePlanBlockedCount`, `ExceptionCount`. Aucun P&L, win rate, Sharpe, drawdown ou expectancy — vérifié par relecture du fichier.

---

## 11. YAHOO LIMITATIONS

Aucune limitation nouvelle par rapport au LOT 14.2 (§7-16 de ce rapport) — ce lot ne touche pas `Core/MarketData/Yahoo/`. Rappel appliqué ici : `BidVolume`/`AskVolume`/`Delta`/`OpenInterest` restent absents sur toute barre Yahoo, jamais approximés (`Delta = Volume` proscrit) ; série continue (`Provider="Yahoo(Continuous)"`), jamais un contrat individuel ; M5 uniquement, ES/MES uniquement. Le test réseau de ce lot (`BacktestSignalPipelineYahooIntegrationTests`) réutilise la même fenêtre courte (2 jours) que le LOT 14.2, sans téléchargement de gros volume (brief §26).

---

## 12. TRADEPLAN BEHAVIOR

Comportement de `TradePlanBuilder` (protégé, non modifié) préservé à l'identique : sans `RiskParameters` (Risk Engine hors périmètre), `TradePlan.Status` ne peut **jamais** être `PLAN_READY` — au mieux `SIGNAL_ONLY` (une direction existe mais StopLoss/PositionSize catégoriquement indisponibles), confirmé par `Stage_EntryTriggerToTradePlan_TradePlanIsBuiltOnlyWhenEntryTriggerIsPresent_NeverFabricated` (`Assert.NotEqual(TradePlanStatus.PLAN_READY, ...)`, `Assert.Null(StopLoss)`). Aucun Stop Loss artificiel n'est jamais inventé (brief §13/§32) — `RiskEngine` n'est appelé nulle part dans ce lot (vérifié par grep sur `Backtest/`).

---

## 13. TESTS

### Nouveaux fichiers (`Tests/Backtest/Pipeline/`, 6 fichiers, 35 tests, ~870 lignes)

| Fichier | Couverture |
|---|---|
| `BacktestSignalPipelineStageTests.cs` | Fixtures A/B/C/D/E (§20), transitions étage par étage (§21), bout-en-bout HistoricalSeries→TradePlan, warmup (§5), validations d'arguments |
| `BacktestSignalPipelineLookAheadTests.cs` | **Test bloquant §22** |
| `BacktestSignalPipelineRunIsolationTests.cs` | **Test bloquant §23** |
| `BacktestSignalPipelineDeterminismTests.cs` | **Test bloquant §24** |
| `BacktestSignalPipelineDirectionConsistencyTests.cs` | §14/§15 — invariants de cohérence direction/ambiguïté/équilibre au niveau Backtest, sans re-dériver la couverture déjà exhaustive de `Tests/EntryTrigger/DecisionDirectionCoherenceTests.cs` |
| `BacktestSignalPipelineExceptionTests.cs` | §18 — politique d'exception, recherche empirique (série constante/zéro-variance, série à 1 barre) d'un déclencheur réel : aucun trouvé, dégradation gracieuse confirmée partout |
| `BacktestSignalPipelineYahooIntegrationTests.cs` | §25/§26 — réseau réel, petite fenêtre, non-bloquant si Yahoo indisponible |

`Tests/Backtest/BacktestTestSeriesBuilder.cs` (LOT 14.1) étendu d'une seule méthode additive (`MeanRevertingOu`) — aucun test existant modifié.

### Résultat de la suite ciblée Lot 14.3

```
IQIAIndicator.Tests.BacktestTests.Pipeline : 35/35 PASS, 0 FAIL, 0 SKIP
```

### Résultat de la suite LOT 14.1 + LOT 14.2 (non-régression de l'extraction `BuildValidatedContext`)

```
IQIAIndicator.Tests.BacktestTests (hors Pipeline) : 153/153 PASS
```

### Suite complète du dépôt

Voir §14 (Build/Tests) — résultat inséré après exécution complète.

---

## 14. TEST RESULTS / BUILD RESULTS

### 14.1 Build

| Cible | Résultat | Avertissements | Erreurs |
|---|---|---|---|
| `IQIAIndicator.csproj` — Debug | **SUCCÈS** | 0 | 0 |
| `IQIAIndicator.csproj` — Release | **SUCCÈS** | 0 | 0 |
| `IQIAIndicator.Tests.csproj` — Debug | **SUCCÈS** | 0 | 0 |
| `IQIAIndicator.Tests.csproj` — Release | **SUCCÈS** | 0 | 0 |

### 14.2 Suite ciblée Lot 14.3

```
IQIAIndicator.Tests.BacktestTests.Pipeline : 35/35 PASS, 0 FAIL, 0 SKIP (58 s)
```

### 14.3 Suite Lot 14.1 + Lot 14.2 (non-régression de l'extraction `BuildValidatedContext`)

```
IQIAIndicator.Tests.BacktestTests (hors Pipeline) : 153/153 PASS (43 s)
```

### 14.4 Suite complète du dépôt

```
Total : 437 tests
Réussi(s) : 435
Ignoré(s) : 1
Échec(s) : 1
Durée : 13 min 58 s
```

437 = 402 (base LOT 14.1+14.2, rapport Lot 14.2 §20) + 35 (nouveaux tests Pipeline de ce lot). L'ignoré est le même test préexistant déjà documenté aux LOT 14.1/14.2 (`Sprint1515HistoricalReconciliationXunitTests.VerifySprint1514Unaffected`, retiré depuis le Sprint 15.22, sans rapport avec ce lot).

**Un échec** : `AdfLagSelectionScaleStabilityXunitTests.RunAll` (`Tests/GoldenDatasets/AdfLagSelectionScaleStabilityTests.cs`), une assertion de PERFORMANCE (« lag selection at 100 bars must not regress to pathological cost », mesuré 269,69 ms/appel contre un seuil implicite) — **aucun rapport avec ce lot** : ce fichier n'appartient pas à `Backtest/`/`Engine/Fusion`/`Engine/Decision`/etc., n'a pas été touché, et ne teste aucune fonctionnalité câblée par ce lot. Ré-exécuté **seul** immédiatement après : **PASS en 3 s**. Diagnostic : un test de timing sensible à la charge CPU, entré en contention avec l'exécution parallèle des 35 nouveaux tests de ce lot (dont des filtres de Kalman réels et un appel réseau Yahoo) pendant l'unique passage de la suite complète — pas une régression fonctionnelle introduite par ce lot. Non corrigé (fichier hors périmètre, brief §37 : « ne pas modifier les anciens tests »), documenté ici par transparence.

---

## 15. PROTECTED FILES

Vérifiés individuellement via `git status --porcelain` avant toute modification et à nouveau à la fin du lot :

| Fichier | État |
|---|---|
| `DecisionArbitrator.cs` | **INTACT** |
| `EntryTriggerBuilder.cs` | **INTACT** |
| `TradePlanBuilder.cs` | **INTACT** |
| `RiskEngine.cs` | **INTACT** |
| `RiskPolicy.cs` | **INTACT** |
| `InstrumentRiskSpecification.cs` | **INTACT** |
| `RiskEngineRequest.cs` | **INTACT** |
| `RiskAssessment.cs` | **INTACT** |

`RegimeEngine`/`EvidenceFusionEngine`/`FusionStateManager`/`DecisionEngine`/`SignalEngine`/`EntryEngine`/`EntryTriggerEngine` : **tous INTACT** (aucune de leurs classes n'apparaît dans `git status`).

`AmbiguityGateThreshold = 0.95` (`EntryTriggerBuilder.cs:19`) : **non touché**, fichier entier intact.

Deux fichiers de production affichent une modification dans `git status`, toutes deux **antérieures à ce lot** et déjà documentées : `Core/MarketContextBuilder.cs` (extraction LOT 14.1 vers `MarketContextFactory`) et `Engine/ScientificModels/Context/VolatilityModel.cs` (audit de causalité « Lot B2 », travail préexistant sans rapport avec ce lot — confirmé par lecture : la classe documente elle-même son propre contrat causal, testé par `Tests/ScientificModels/VolatilityModelCausalityTests.cs`, un fichier non créé par ce lot). Ce lot n'a modifié ni l'un ni l'autre davantage.

Seul fichier de production préexistant modifié PAR CE LOT : `Backtest/BacktestEngine.cs` — extension additive (`RunSignalPipeline`, `BuildValidatedContext`, `BuildScientificMarketContext`) plus une extraction pure de deux lignes dans `Run()` (§3, prouvée non-régressive par les 153 tests LOT 14.1/14.2 toujours verts).

---

## 16. KNOWN LIMITATIONS

1. **`FusionStateManager` mute son état interne avant de pouvoir potentiellement lever une exception plus loin dans `Update`** (`_previousStableResult` est assigné avant l'appel à `_profileAnalyzer.Analyze`/`_structuralStabilityRule.EvaluateAnalysis`). Si l'une de ces deux dernières lignes levait une exception (jamais observé dans les tests de ce lot), l'état porté à la barre suivante refléterait un appel en échec. Ce risque n'est pas introduit par ce lot : il existe identiquement, non traité, sur le chemin ATAS live — le corriger exigerait de modifier `FusionStateManager.cs`, un fichier protégé, explicitement hors périmètre (brief §1/§42).
2. **`RegimeDetectedCount` et `DecisionCount` mesurent, par construction de `DecisionEngine.Evaluate`, la même population de barres** (`Winner` ne quitte son défaut `Unknown` que lorsqu'au moins un candidat a été arbitré). Reportés séparément parce qu'ils répondent à deux questions distinctes (une règle a-t-elle déclenché / l'arbitrage a-t-il nommé un régime), documenté explicitement plutôt que masqué en §10.
3. **Aucun scénario n'a été trouvé, dans ce lot, qui fasse réellement lever une exception** à travers le pipeline câblé (§18/§13, `BacktestSignalPipelineExceptionTests`) — recherché avec une série à variance nulle et une série à une seule barre. La machinerie try/catch/continue existe donc en défense en profondeur, non exercée par un cas positif dans ce lot ; documentée comme telle plutôt que simulée artificiellement.
4. **`BacktestWindow` reste transporté mais non appliqué comme filtre** — hérité du LOT 14.1 (limitation déjà documentée), `RunSignalPipeline` traite l'intégralité de `scenario.Series.Bars` comme `Run()`.
5. **La parité champ-par-champ ATAS/Backtest reste non prouvée bout-en-bout** — limitation déjà documentée au LOT 14.1 (§18.2), aucun `IndicatorCandle` réel n'est construit nulle part dans ce dépôt ; ce lot ne la lève pas, il prouve seulement que la même arithmétique/les mêmes classes tournent des deux côtés.

---

## 17. NEXT LOT

**LOT 14.4 — Scientific Backtest Result / Execution Model Preparation**, conformément à l'ordre déjà fixé (brief final output).

---

## STATUS
**IMPLEMENTED**
