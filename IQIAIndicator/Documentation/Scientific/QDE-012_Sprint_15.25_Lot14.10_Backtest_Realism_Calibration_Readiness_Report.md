# QDE-012 — Sprint 15.25 — Lot 14.10 — P0.1 — Backtest Realism & Calibration Readiness

**Date** : 2026-08-23
**Branche** : `feature/structural-stability-v2`
**Lot précédent** : Lot 14.9 — Global Calibration & Scientific Readiness Audit (`QDE-012_Sprint_15.25_Lot14.9_Global_Calibration_Audit_Report.md`)
**Statut** : **IMPLEMENTED**
**Portée** : exclusivement les trois blocages P0 identifiés par l'audit Lot 14.9 — biais de fill d'exécution (P0-1), activation contrôlable des coûts (P0-2), binding réel du framework de calibration (P0-3). Aucune calibration de paramètre de production n'a été effectuée. Aucun ordre, aucune DLL déployée, aucun commit.

---

# 1. EXECUTIVE SUMMARY

Les trois blocages P0 de l'audit Lot 14.9 sont résolus :

- **P0-1** : le backtest entre désormais à `Open[SignalBarIndex+1]` (jamais `Close[SignalBarIndex]`), corrigeant le biais confirmé depuis le Lot 13 (défaut L-2) et jamais appliqué par le Lot 14.5. Correctif entièrement confiné à `Backtest/Execution/` — `RegimeEngine`, `DecisionEngine`, `SignalEngine` (logique de génération du signal), `EntryTriggerBuilder` (logique de direction), `TradePlanBuilder` et `RiskEngine` sont inchangés dans leur rôle de production du signal.
- **P0-2** : le modèle de coûts (Lot 14.7) était déjà correctement câblé jusqu'au chemin utilisé par le laboratoire de calibration (`BacktestEngine.RunFullBacktestWithRisk`) — aucun défaut de câblage n'a été trouvé. Ce qui manquait était la PREUVE, par test, que `Costs ON` produit réellement `NetPnL != GrossPnL` à travers cette surface précise, et que `Costs OFF` préserve le comportement historique. Cette preuve existe maintenant. Aucune valeur de coût réaliste n'a été inventée — tout est explicitement marqué FIXTURE DE TEST.
- **P0-3** : `AmbiguityGateThreshold` est maintenant réellement injectable depuis un `CalibrationParameterSet`, via un mécanisme explicite, typé, sans réflexion, sans état global, jusqu'au consommateur réel (`EntryTriggerBuilder.DetermineDirection`). Un test end-to-end prouve qu'une grille de deux valeurs de seuil produit un `PositionCount` réellement différent — pas seulement un fingerprint différent. Le comportement par défaut (aucune configuration de calibration fournie) reste bit-pour-bit identique à avant ce lot.

**Effet de bord découvert et documenté** (non un défaut de ce lot, une conséquence structurelle légitime de P0-1) : `Backtest/Calibration/CalibrationExperimentRunner` fenêtre les positions par `EntryTimestamp` et les signaux par `SignalTimestamp`. Ces deux timestamps coïncidaient toujours avant ce lot (l'entrée était la barre du signal) ; ils peuvent maintenant différer d'une barre, ce qui peut déplacer une position d'une fenêtre TRAIN/VALIDATION/OOS vers la suivante lorsque son signal tombe exactement à la frontière. Documenté §14, un test existant a été ajusté en conséquence (§9).

**Vérification technique** : Debug build PASS, Release build PASS (projet principal + tests), suite complète **777 réussis / 1 ignoré / 0 échec** sur 778 tests (durée totale du run : voir §9 — inclut les tests réseau Yahoo).

---

# 2. ÉTAT AVANT LE LOT

Repris de l'audit Lot 14.9 (`QDE-012_Sprint_15.25_Lot14.9_Global_Calibration_Audit_Report.md`, §6 Structural Defects) :

| # | Défaut | Sévérité | Statut avant ce lot |
|---|---|---|---|
| D1 | Fill d'exécution au `Close[i]`, biais confirmé depuis le Lot 13 (défaut L-2) | CRITICAL | Jamais corrigé |
| D2 | Coûts désactivés par défaut partout, y compris dans la campagne de calibration | CRITICAL | Désactivé par défaut (comportement conservé), mais jamais prouvé activable à travers le chemin Risk/Calibration |
| D4 | `CalibrationParameterSet` jamais lié à `CalibrationExperimentSetup` — une grille ne change rien au comportement réel | CRITICAL | Confirmé par lecture directe du code (`CalibrationExperimentRunner.cs:46-49` ne lisait jamais `experiment.ParameterSet`) |

Également noté au démarrage de ce lot : le framework `Backtest/Calibration/` (21 fichiers de production + 14 fichiers de tests) et deux brouillons de rapport Lot 14.9 étaient déjà présents dans l'arbre de travail, non committés, produits par une session antérieure. Ce lot les traite comme un état existant du dépôt (audité, pas recréé) — voir la note d'état du rapport Lot 14.9 pour le détail.

---

# 3. P0-1 — EXECUTION BIAS

## Correctif

`Backtest/Execution/ExecutionSimulator.SimulateCore` :

**Avant** : `EntryPrice = candidate.EntryPrice` (= `TradePlan.EntryPrice`, dérivé de `CurrentPrice` = Close de la barre du signal). `EntryBarIndex = SignalBarIndex`. `ExitBarIndex = SignalBarIndex + HorizonBars`.

**Après** : `EntryPrice = bars[SignalBarIndex + 1].Open` (jamais `Close`, jamais une valeur du `TradePlan`). `EntryBarIndex = SignalBarIndex + 1`. `ExitBarIndex = EntryBarIndex + HorizonBars = SignalBarIndex + 1 + HorizonBars` — le modèle d'exit TIME_HORIZON existant (Lot 14.5, `ExitPrice = ExitBar.Close`) est inchangé, seul le point de départ du compte à rebours se décale d'une barre.

`candidate.EntryPrice` (le `TradePlan.EntryPrice` de la barre du signal) reste lu **une fois**, comme garde de qualité de données sur la barre du signal elle-même (défense en profondeur : si le pipeline n'a même pas pu établir un prix de référence à la barre du signal, le candidat est rejeté avant toute tentative de fill) — il n'est plus jamais utilisé comme prix tradé.

## Cas limites traités explicitement

| Cas | Statut produit |
|---|---|
| `SignalBarIndex + 1` n'existe pas (signal sur la dernière barre) | `InsufficientFutureData` (jamais un fill fabriqué) |
| La barre de fill existe mais échoue `HistoricalBar.Validate()` ou son `Open` n'est pas strictement positif | `InvalidEntry` |
| La barre de fill est valide mais `SignalBarIndex + 1 + HorizonBars` n'existe pas | `InsufficientFutureData` (comme avant, décalé d'une barre) |
| La barre d'exit échoue `Validate()` | `InvalidExit` (inchangé) |

## Fichiers protégés — respect vérifié

Aucun de `RegimeEngine.cs`, `DecisionEngine.cs`, `SignalEngine.cs` (logique de génération), `EntryEngine.cs`, `EntryTriggerBuilder.cs` (logique de direction — voir §5 pour l'exception P0-3, sans rapport avec P0-1), `TradePlanBuilder.cs`, `RiskEngine.cs` n'a été modifié pour P0-1. Le correctif est confiné à `Backtest/Execution/ExecutionSimulator.cs` et aux doc-comments de `Backtest/Execution/SimulatedPosition.cs` (`PositionStatus`).

## Conséquence assumée : divergence Measurement ↔ Execution

Avant ce lot, `MeasurementResult.Return` (Lot 14.4) et `SimulatedPosition.Return` (Lot 14.5) étaient bit-identiques — non parce qu'ils répondaient à la même question, mais parce que les deux utilisaient par coïncidence la même convention biaisée (`Close[i]`). Measurement répond toujours à *"qu'est-il arrivé après le signal, indépendamment de toute convention d'exécution"* (inchangé, ancré sur `Close[SignalBarIndex]`/`Close[SignalBarIndex+HorizonBars]`). Execution répond maintenant à *"quel aurait été le résultat d'une position réellement exécutable"* (ancré sur `Open[SignalBarIndex+1]`/`Close[SignalBarIndex+1+HorizonBars]`). Cette divergence est **voulue et prouvée par test** (§9).

---

# 4. P0-2 — EXECUTION COSTS

## Constat

Contrairement à une hypothèse initiale, **le modèle de coûts n'avait pas de défaut de câblage**. Lecture directe de `Backtest/Risk/BacktestRiskResultBuilder.Build` (le chemin utilisé par `RunFullBacktestWithRisk`, donc par `CalibrationExperimentRunner`) : chaque position autorisée passe déjà par `PositionCostCalculator.Calculate(position, pnlResult, perPositionConfig, costConfiguration)`, qui applique intégralement `ExecutionCostConfiguration` (spread, slippage, commission, fees) et calcule `NetPnL = GrossPnL - TotalCost`. Ceci était déjà vrai avant ce lot.

Ce qui manquait : **la preuve, par test, à travers cette surface précise** (Risk/Calibration, pas seulement les tests unitaires de coût du Lot 14.7). Cette preuve est maintenant ajoutée (voir §9) :

- `CostsOff_NetPnLEqualsGrossPnL_ForEveryAllowedPosition` — comportement historique confirmé conservé.
- `CostsOn_NetPnLDiffersFromGrossPnL_ByExactlyTheConfiguredCost_ForEveryAllowedPosition` — `NetPnL = GrossPnL - TotalCost` exactement, avec `TotalCost > 0` prouvé pour chaque position, en utilisant la formule déjà existante (`PositionCostCalculator`), jamais réinventée.
- `CostsOn_FinalNetPnL_DiffersFromFinalGrossPnL_AtTheAggregateLevel` — même signaux/positions (`SignalResult`/`ExecutionResult` fingerprints identiques) qu'avec coûts désactivés ; seule l'économie nette change, et diminue (jamais n'invente un gain).
- `CostsOn_EquityAfter_ReflectsNetPnL_NeverGrossPnL` — la chaîne complète signal → exécution → GrossPnL → coûts → NetPnL → equity est vérifiée bar par bar.

## Discipline "aucune valeur inventée"

`BacktestFullResultWithRiskCostActivationTests.EnabledTestCosts()` utilise des valeurs explicitement documentées comme fixtures de test (`SlippageConfiguration.Fixed(0.25m)`, `SpreadConfiguration.Fixed(0.5m)`, `CommissionConfiguration.Create(2m, 0.5m)`, `FeesConfiguration.Create(0.25m)`) — aucun barème broker réel n'est cité ni implicite. Le défaut de production reste `ExecutionCostConfiguration.Disabled()`, inchangé par ce lot.

---

# 5. P0-3 — CALIBRATION PARAMETER BINDING

## Architecture du binding

```
CalibrationParameterSet ("AmbiguityGateThreshold" = X)
        ↓ CalibrationParameterBinding.Resolve(...)   [Backtest/Calibration, nouveau fichier]
PipelineParameterOverrides { AmbiguityGateThreshold = X }   [Backtest, nouveau fichier]
        ↓ BacktestEngine.RunFullBacktestWithRisk(..., overrides)   [nouvelle surcharge]
        ↓ BacktestEngine.RunSignalPipeline(..., overrides)          [nouvelle surcharge]
new SignalEngine(traceCollector: null, ambiguityGateThreshold: X)   [nouveau paramètre optionnel]
        ↓
new EntryTriggerEngine(X)   [nouveau constructeur]
        ↓
new EntryTriggerBuilder(X)   [nouveau constructeur — X remplace la constante 0.95 UNIQUEMENT pour cet appel]
        ↓
DetermineDirection(context, X, ...)   [X remplace l'ancienne référence directe à la constante]
        ↓
decision.AmbiguityScore >= X  →  comportement réellement différent (Direction BUY/SELL/NO_ACTION)
```

## Fichier protégé touché — exception explicitement autorisée

`Engine/EntryTrigger/EntryTriggerBuilder.cs` fait partie de la liste des fichiers protégés de ce lot, avec une exception explicite prévue par le brief : *"La seule exception est de rendre AmbiguityGateThreshold injectable par une expérience de calibration, tout en conservant son défaut historique de 0.95."*

Changement exact : le champ `private const double AmbiguityGateThreshold = 0.95` devient `public const double AmbiguityGateThreshold = 0.95` (valeur et nom **inchangés** — seule la visibilité change, pour que `SignalEngine`/`EntryTriggerEngine` puissent la référencer comme valeur par défaut). Deux constructeurs sont ajoutés : `EntryTriggerBuilder()` (délègue à la constante, comportement identique à avant ce lot) et `EntryTriggerBuilder(double ambiguityGateThreshold)` (le point d'injection). `DetermineDirection` reçoit le seuil en paramètre au lieu de lire la constante directement. **Aucune autre logique du fichier n'a été touchée** — les seuils READY (0.70/0.55), la condition de cohérence de régime, la gestion `DynamicZScore`, `BuildReason` sont bit-pour-bit identiques à avant ce lot.

## Contrainte "pas de magie" — respectée

- **Pas de réflexion** : `CalibrationParameterBinding.Resolve` lit exactement un nom de paramètre documenté (`"AmbiguityGateThreshold"`) via `CalibrationParameterSet.TryGet` — jamais une itération dynamique de propriétés.
- **Pas d'état global/statique mutable** : `PipelineParameterOverrides` est un `record` immuable, passé explicitement en paramètre à chaque appel ; aucune variable statique n'est écrite.
- **Pas de service locator** : le chemin d'injection est un enchaînement de constructeurs/paramètres explicites, visible statiquement dans le code, jamais résolu par nom à l'exécution.
- **Local à une expérience** : chaque appel à `RunFullBacktestWithRisk(..., overrides)` construit son propre `SignalEngine`/`EntryTriggerEngine`/`EntryTriggerBuilder` — rien n'est partagé ni réutilisé entre deux expériences (voir §11 Run Isolation).

## Règle de non-régression

Toute méthode `BacktestEngine` pré-existante (`RunSignalPipeline(scenario, warmupBars)`, `RunFullBacktestWithRisk(scenario, warmupBars, ..., riskConfiguration)` à 7 arguments) est **conservée intacte** et délègue vers une nouvelle surcharge à 8 arguments avec `PipelineParameterOverrides.None` — reproduisant le comportement exact d'avant ce lot pour tout appelant qui ne fournit pas explicitement d'overrides (`RunMeasuredSignalPipeline`, `RunSimulation`, `RunFullBacktest`, `RunFullBacktestWithCosts` n'ont pas été modifiés et continuent d'appeler la forme à 2 arguments). Preuve par test : §9 TEST 1.

---

# 6. ARCHITECTURE AVANT / APRÈS

## Avant ce lot

```
Signal(i) → TradePlan.EntryPrice (= Close[i]) → Execution.EntryPrice = Close[i] → Exit = Close[i+H]
CalibrationParameterSet → [AUCUN LIEN] → CalibrationExperimentSetup → BacktestEngine (comportement fixe)
```

## Après ce lot

```
Signal(i) → Candidate(i) [Direction/TradePlanStatus depuis TradePlan, jamais son EntryPrice]
          → Fill = Open(i+1)  [garde: bar i+1 doit exister et être valide]
          → Exit = Close(i+1+H)  [garde: bar i+1+H doit exister et être valide]

CalibrationParameterSet["AmbiguityGateThreshold"] → CalibrationParameterBinding
          → PipelineParameterOverrides → BacktestEngine.RunFullBacktestWithRisk(..., overrides)
          → SignalEngine → EntryTriggerEngine → EntryTriggerBuilder → comportement réellement différent
```

---

# 7. PARAMÈTRES RÉELLEMENT INJECTABLES

| Paramètre | Nom dans `CalibrationParameterSet` | Type | Point d'injection réel | Défaut si absent |
|---|---|---|---|---|
| Seuil d'ambiguïté de `EntryTriggerBuilder` | `"AmbiguityGateThreshold"` | Decimal, unité "score" | `EntryTriggerBuilder(double)` via `SignalEngine`/`EntryTriggerEngine`/`BacktestEngine.RunSignalPipeline(..., overrides)` | `EntryTriggerBuilder.AmbiguityGateThreshold` (0.95, inchangé) |

C'est le **seul** paramètre rendu réellement injectable par ce lot, conformément au brief ("Le premier paramètre à rendre réellement injectable doit être : AmbiguityGateThreshold").

---

# 8. PARAMÈTRES ENCORE NOT BINDABLE YET

Documenté explicitement dans `CalibrationParameterBinding`'s propre doc-comment (jamais un hack silencieux) :

- **Fenêtres de régime** (ADF/KPSS/DFA/HalfLife/VarianceRatio/CUSUM/Bai-Perron) : aucun champ correspondant sur `PipelineParameterOverrides`. Un `CalibrationParameter` portant un tel nom serait accepté par `CalibrationParameterSet.Create` (aucune validation de nom n'existe à ce niveau), changerait le `ConfigurationFingerprint` de l'expérience, mais n'aurait **aucun** effet sur le comportement réel — exactement le piège que l'audit Lot 14.9 (Défaut D4) mettait en garde.
- **Poids Fusion/Decision** : idem, non bindable.
- **Seuils Entry/EntryTrigger READY** (0.9/0.6/0.3/0.70/0.55) : idem, non bindable.
- **StopLoss/RiskDistance/RiskPolicy** : idem, non bindable — et de toute façon hors-scope de ce lot (voir Section STOP LOSS / RISK POLICY du brief).

**Ce qui N'EST PAS un candidat pour ce binding du tout** (distinction importante, pas une lacune) : `Measurement`/`Execution`/`PnL`/`Cost`/`Risk` configuration ne sont **pas** supposés être balayés via un axe `CalibrationParameterSet`/`CalibrationGrid` — ils sont fournis directement, une fois par grille, via les champs déjà existants de `CalibrationExperimentSetup`, que `CalibrationExperimentRunner` transmet déjà tels quels à `BacktestEngine`. Construire le binding pour les paramètres listés ci-dessus (fenêtres de régime, poids, etc.) est explicitement hors du périmètre de ce lot — voir §14 pour la proposition de lot dédié.

---

# 9. TESTS

## Nouveaux fichiers de test

| Fichier | Contenu |
|---|---|
| `Tests/Backtest/Calibration/CalibrationParameterBindingTests.cs` | TEST 1-6 du brief (voir détail ci-dessous), 10 tests |
| `Tests/Backtest/Cost/BacktestFullResultWithRiskCostActivationTests.cs` | P0-2 : Costs OFF/ON à travers `RunFullBacktestWithRisk`, 4 tests |

## Fichiers de test modifiés

| Fichier | Raison |
|---|---|
| `Tests/Backtest/Execution/ExecutionSimulatorFormulaTests.cs` | Réécrit : chaque fixture gagne une barre (signal/fill/exit au lieu de signal/exit) ; 3 tests ajoutés (`Entry_UsesFillBarOpen_NeverFillBarClose_NeverSignalBarAnything`, `InsufficientFutureData_FillBarMissing_SignalAtLastBar_NeverFabricatesAFill`, `InvalidFillBar_HighBelowLow_IsRejectedAsInvalidEntry_NeverComputedSilently`) |
| `Tests/Backtest/Execution/ExecutionSimulatorIntegrationTests.cs` | Le test de non-look-ahead sur bar[i+1] est scindé en deux : un test prouvant que bar[i+1] influence maintenant DÉLIBÉRÉMENT l'entrée, un test prouvant que bar[i+2]+ ne l'influence toujours PAS. Borne de boucle 210→209 (voir §14) |
| `Tests/Backtest/Execution/ExecutionMeasurementReturnCoherenceTests.cs` | L'ancien test d'égalité bit-exacte est remplacé par deux tests : cohérence interne d'`Execution.Return` (recalcul indépendant), et divergence prouvée entre `Execution.Return` et `Measurement.Return` |
| `Tests/Backtest/Pnl/BacktestFullResultIntegrationTests.cs` | Borne de boucle 210→209 (même cause, voir §14) |
| `Tests/Backtest/Calibration/CalibrationYahooIntegrationTests.cs` | Invariant `PositionCount <= SignalCount` par tranche retiré et documenté comme n'étant plus garanti par construction (voir §14) |

## Détail des 6 tests obligatoires (brief P0-3)

| # | Test | Fichier |
|---|---|---|
| TEST 1 — Default behaviour | `DefaultEntryTriggerBuilder_StillUsesProductionThreshold_0_95_Unchanged`, `CalibrationParameterBinding_EmptyParameterSet_ResolvesToNullOverride_FallsBackToProductionDefault` | `CalibrationParameterBindingTests.cs` |
| TEST 2 — Parameter propagation (au consommateur réel) | `CalibrationParameterBinding_ExplicitThreshold_PropagatesToTheRealConsumer_EntryTriggerBuilder` | idem |
| TEST 3 — Behavioural difference (pas seulement un fingerprint) | `TwoThresholds_ProduceGenuinelyDifferentDirection_ForTheIdenticalDecision`, `TwoCalibrationExperiments_DifferingOnlyByAmbiguityGateThreshold_ProduceDifferentPositionCounts_EndToEnd` | idem |
| TEST 4 — Run isolation | `ExperimentA_ExperimentB_ExperimentA_DifferingOnlyByThreshold_ProduceIdenticalResultsForTheRepeatedA` | idem |
| TEST 5 — Determinism | `SameDatasetConfigurationAndThreshold_RunTwice_ProducesIdenticalResults` | idem |
| TEST 6 — Look-ahead | `NonDefaultThreshold_AppendingMoreOosData_NeverChangesTrainOrValidationResults` | idem |

`TwoCalibrationExperiments_DifferingOnlyByAmbiguityGateThreshold_ProduceDifferentPositionCounts_EndToEnd` est le test central de TEST 3 : deux expériences identiques sauf `AmbiguityGateThreshold` (0.999 vs 0.0) produisent des `ConfigurationFingerprint` différents **et** des `PositionCount` totaux réellement différents (0 positions à seuil 0.0, strictement plus à seuil 0.999) — la preuve explicitement exigée par le brief qu'"un fingerprint différent sans changement comportemental est INSUFFISANT" n'est pas ici le cas observé.

## Résultat d'exécution

```
dotnet build IQIAIndicator.csproj -c Debug    → PASS, 0 avertissement, 0 erreur
dotnet build IQIAIndicator.csproj -c Release  → PASS, 0 avertissement, 0 erreur
dotnet build IQIAIndicator.Tests.csproj -c Debug    → PASS, 0 avertissement, 0 erreur
dotnet build IQIAIndicator.Tests.csproj -c Release  → PASS, 0 avertissement, 0 erreur
dotnet test IQIAIndicator.Tests.csproj (suite complète) → Réussi : échec 0, réussite 777, ignorée(s) 1, total 778
```

Le seul test ignoré (`Sprint1515HistoricalReconciliationXunitTests.VerifySprint1514Unaffected`) est un `[SKIP]` explicite préexistant, sans rapport avec ce lot (déjà documenté ainsi dans le Lot 14.9). Effet de bord connu : le run complet a réécrit les fichiers `Tests/Research/StopLossCalibration/Output/*.txt`, un artefact inoffensif déjà observé lors de runs précédents.

**Itérations de correction pendant ce lot** : le premier run après le correctif P0-1 a révélé 15 échecs (11 fixtures Execution à recalibrer sur la nouvelle convention, 2 bornes de boucle décalées d'une barre, 1 divergence Measurement/Execution à documenter au lieu de corriger). Après correction, 3 échecs résiduels (1 précondition de test invalide sur données synthétiques sans gap, 1 arrondi décimal par ordre d'accumulation différent de la production, 1 invariant de fenêtrage devenu obsolète). Après correction finale : 0 échec. Chaque correction est documentée dans le fichier de test concerné.

---

# 10. LOOK-AHEAD

- **P0-1** : le nouveau point de fill (`bars[SignalBarIndex+1].Open`) reste look-ahead-safe — une barre placée APRÈS celle du signal, jamais une donnée que le signal lui-même a utilisée pour se former. `RegimeEngine`/`DecisionEngine`/`SignalEngine` continuent de ne lire que `bars[0..SignalBarIndex]` (inchangé, non touché par ce lot). Preuve directe : `ModifyingBarITwoPlusOne_NeverChangesTheEntryPriceOfAPositionSignalledAtBarI` (bar i+2 et au-delà n'influence jamais l'entrée), et `ModifyingBarIPlusOne_ChangesTheEntryPriceOfAPositionSignalledAtBarI_ByDesign` (bar i+1 l'influence désormais, par construction).
- **P0-3** : `NonDefaultThreshold_AppendingMoreOosData_NeverChangesTrainOrValidationResults` (TEST 6) prouve que le binding n'introduit aucun accès à une donnée future — élargir la fenêtre OOS ne change jamais les résultats TRAIN/VALIDATION, même avec un seuil non-défaut actif. Ceci découle directement du fait que `PipelineParameterOverrides` est un scalaire statique par expérience, sans dépendance à aucune barre particulière.

---

# 11. DETERMINISM

Prouvé par `SameDatasetConfigurationAndThreshold_RunTwice_ProducesIdenticalResults` (TEST 5) : mêmes dataset/configuration/paramètres → `ExperimentId` et métriques identiques sur deux exécutions. Aucune horloge murale, aucun `Guid.NewGuid()`, aucun `System.Random` non-seedé n'a été introduit par ce lot (vérifié par relecture de chaque fichier créé/modifié).

---

# 12. RUN ISOLATION

Prouvé par `ExperimentA_ExperimentB_ExperimentA_DifferingOnlyByThreshold_ProduceIdenticalResultsForTheRepeatedA` (TEST 4) : exécuter une expérience à seuil 0.05, puis une à seuil 0.95, puis revenir à 0.05, donne un résultat identique pour les deux exécutions du seuil 0.05 — aucune contamination inter-expérience. Ceci découle de la construction : chaque appel à `RunFullBacktestWithRisk(..., overrides)` construit un `SignalEngine`/`EntryTriggerEngine`/`EntryTriggerBuilder` frais, jamais partagé ni mis en cache.

---

# 13. YAHOO VALIDATION

`CalibrationYahooIntegrationTests.Integration_Network_YahooMesFiveMinuteBars_RunsOneCalibrationExperimentAcrossTrainValidationOos` (réseau, MES/M5/45 jours) exécute le pipeline complet avec le correctif P0-1 actif. Un invariant devenu obsolète a été retiré et documenté (§14) ; le test passe désormais (durée observée : ~4 minutes, dépendante du réseau). Aucune donnée réseau n'a été utilisée pour choisir ou calibrer une valeur — uniquement pour vérifier des invariants structurels, conformément à la discipline déjà établie par les lots précédents.

---

# 14. IMPACT SUR LES LOTS SUIVANTS

## Découverte structurelle : fenêtrage TRAIN/VALIDATION/OOS et décalage d'une barre

`CalibrationExperimentRunner.BuildSlice` (Lot 14.9, non modifié dans sa logique de fenêtrage par ce lot) compte `SignalCount` par `MeasurementResult.SignalTimestamp` mais `PositionCount` par `PositionRiskOutcome.EntryTimestamp`. Avant ce lot, ces deux timestamps étaient toujours identiques (l'entrée était la barre du signal elle-même), donc l'invariant "`PositionCount` d'une fenêtre ≤ `SignalCount` de cette même fenêtre" tenait toujours. Après P0-1, `EntryTimestamp` est désormais la barre du signal **+1** — un signal qui se déclenche sur la toute dernière barre d'une fenêtre peut donc voir sa position comptée dans la fenêtre **suivante**. Ce n'est pas un défaut (une position est comptée là où elle s'est réellement ouverte, ce qui est plus correct), mais c'est une **conséquence architecturale à documenter pour tout futur lot qui interpréterait `PositionCount`/`SignalCount` par fenêtre comme devant nécessairement coïncider**. Deux tests ont dû être ajustés en conséquence (`CalibrationYahooIntegrationTests`, boucles à borne 210→209 dans les tests Execution/Pnl).

## Recalibrage nécessaire de tout seuil dérivé de `HorizonBars`

`MeasurementConfiguration.HorizonBars`/`ExecutionConfiguration.HorizonBars` restent à 10, inchangés par ce lot — mais leur signification a subtilement changé côté Execution (la fenêtre glisse d'une barre). Tout futur lot de calibration de `HorizonBars` (Lot 14.9 roadmap, §22) doit tenir compte de cette redéfinition.

## Prérequis maintenant satisfaits pour la suite de la roadmap Lot 14.9

- Lot "Cost Activation & Calibration" (anciennement 14.11) : l'infrastructure est maintenant PROUVÉE fonctionnelle à travers le chemin Risk/Calibration — reste à sourcer un barème broker réel (hors scope de ce lot, explicitement interdit par le brief P0-2).
- Lot "Calibration Binding" (anciennement 14.12) : **complété par ce lot**, pour `AmbiguityGateThreshold` uniquement. Étendre à d'autres paramètres (fenêtres de régime, poids Fusion/Decision) reste un lot séparé (voir §8).
- Toute future calibration de `AmbiguityGateThreshold` (Lot "AmbiguityGateThreshold Revalidation" de la roadmap Lot 14.9) peut maintenant s'appuyer sur un fill réaliste ET des coûts activables — les deux biais qui invalidaient structurellement l'étude Lots 4-9 sont levés.

---

# 15. LIMITATIONS

- Ce lot ne calibre, ne sélectionne, ni ne recommande aucune valeur de `AmbiguityGateThreshold`, de coût, ou de tout autre paramètre — conformément au brief. `0.95` reste la valeur de production.
- Le binding P0-3 ne couvre qu'**un seul** paramètre. Étendre le mécanisme à d'autres paramètres (fenêtres de régime, poids Fusion/Decision, seuils Entry/EntryTrigger) nécessite un travail similaire, non fait ici, et documenté comme `NOT BINDABLE YET` plutôt que contourné.
- Le modèle de coûts activé dans les nouveaux tests utilise des valeurs FIXTURES DE TEST, pas un barème broker réel — aucune conclusion de performance nette réaliste ne peut être tirée de ces tests.
- La méthodologie Stop Loss reste absente en production (hors scope explicite de ce lot).
- Aucune valeur de `RiskPolicy` de production n'a été définie (hors scope explicite de ce lot).
- L'effet de bord §14 (fenêtrage par `EntryTimestamp` vs `SignalTimestamp`) n'a pas été "corrigé" — il a été documenté et les tests concernés ajustés, car le changer reviendrait à modifier la sémantique de fenêtrage de `CalibrationExperimentRunner` (Lot 14.9), hors du périmètre strict de ce lot (P0-1/P0-2/P0-3 uniquement).

---

# 16. VERDICT FINAL

**PASS.** Les trois objectifs P0 du lot sont atteints, chacun avec preuve par test :

1. **Execution Bias corrigé** : fill à `Open[i+1]`, jamais `Close[i]` ; garde explicite pour donnée future insuffisante ; aucune donnée future utilisée pour produire le signal lui-même.
2. **Costs activables et prouvés** : `Costs OFF` préserve le comportement historique ; `Costs ON` produit un `NetPnL` distinct de `GrossPnL`, exactement selon la formule déjà existante ; aucun coût réaliste inventé.
3. **Calibration Binding réel** : `AmbiguityGateThreshold` est injectable de bout en bout, de façon explicite/typée/sans réflexion/sans état global, avec un effet comportemental réellement observable (pas seulement un fingerprint), une isolation et un déterminisme prouvés, et un comportement par défaut strictement préservé.

Aucun fichier protégé n'a été modifié en dehors de l'exception explicitement accordée (`EntryTriggerBuilder.cs`, pour l'unique fin de rendre `AmbiguityGateThreshold` injectable, valeur par défaut inchangée). Aucune méthodologie Stop Loss n'a été créée. Aucune valeur de `RiskPolicy` n'a été choisie. Aucun ordre, aucune DLL déployée, aucun commit. Debug/Release build PASS. Suite complète : 777 réussis, 1 ignoré (préexistant, sans rapport), 0 échec.

Le laboratoire de calibration (`Backtest/Calibration/`) est désormais scientifiquement exploitable pour le seul paramètre `AmbiguityGateThreshold`, sur un pipeline dont le biais de fill le plus critique est corrigé et dont le modèle de coûts est prouvé activable. La prochaine étape recommandée par l'audit Lot 14.9 (revalidation de `AmbiguityGateThreshold` avec coûts réels et fenêtre de données étendue) peut maintenant être envisagée sur des bases techniques saines — mais reste, comme avant, une décision hors du périmètre de ce lot.

**STOP.**
