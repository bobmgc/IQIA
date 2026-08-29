# QDE-012 — Sprint 15.25 — Lot 14.9 — Scientific Calibration Foundation

**Date** : 2026-08-23
**Branche** : `feature/structural-stability-v2`
**Lots précédents** : Lots 14.1 → 14.8 (VALIDÉS, TESTÉS, PROTÉGÉS)
**Statut** : IMPLEMENTED

---

## §1. Objectif

Le Lot 14.9 construit le **LABORATOIRE** de calibration scientifique du backtest IQIA — pas un moteur de
sélection de paramètres. Les Lots 14.1 → 14.8 ont établi une chaîne complète (Yahoo → HistoricalSeries →
MarketContext → Regime → Decision → Signal → Entry → EntryTrigger → TradePlan → Measurement → Execution →
Costs → Risk → Position Sizing → P&L → Equity → Drawdown). Ce lot ajoute la couche permettant de :

1. définir des paramètres calibrables (`CalibrationParameter`/`CalibrationParameterSet`) ;
2. définir un dataset et des fenêtres temporelles (`CalibrationDatasetSpecification`/`CalibrationWindow`) ;
3. séparer TRAIN / VALIDATION / OOS avec garantie d'ordre (`CalibrationWindowSet`) ;
4. exécuter une ou plusieurs configurations (`CalibrationExperiment`/`CalibrationExperimentRunner`) ;
5. mesurer et comparer les résultats (`CalibrationExperimentResult`) ;
6. garantir l'absence de look-ahead et la reproductibilité (fingerprints + tests dédiés) ;
7. sérialiser les résultats (`CalibrationSerialization`).

**Aucune sélection de paramètre n'est faite dans ce lot.** Aucun seuil de production n'est modifié.

---

## §2. Architecture livrée

```
Backtest/Calibration/                         (NOUVEAU, namespace IQIAIndicator.Backtest.Calibration)
   ├── CalibrationProtocolVersion              ("14.9.1", jamais une DateTime)
   ├── CalibrationParameterType / CalibrationParameter / CalibrationParameterSet
   ├── CalibrationDatasetSpecification / CalibrationDataset
   ├── CalibrationWindowRole / CalibrationWindow / CalibrationWindowSet
   ├── CalibrationWarmupContract
   ├── CalibrationExperimentSetup              (Instrument/Policy/Measurement/Execution/PnL/Cost/Risk config)
   ├── CalibrationExperiment                   (ParameterSet + Dataset + Windows + Setup -> Id + Fingerprint)
   ├── CalibrationFingerprint                  (internal - réutilise BacktestFingerprint.Sha256Hex)
   ├── CalibrationDirectionFilter / CalibrationExperimentResultStatus
   ├── CalibrationExperimentResult / CalibrationExperimentRunResult
   ├── CalibrationExperimentRunner             (exécute UNE fois, tranche par fenêtre/direction)
   ├── CalibrationGrid / CalibrationGridAxis   (produit cartésien déterministe)
   ├── CalibrationExperimentSet                (ParameterSets + Setup -> Experiments)
   └── CalibrationSerialization                (JSON, System.Text.Json)

Tests/Backtest/Calibration/                   (NOUVEAU, namespace IQIAIndicator.Tests.BacktestTests.Calibration)
   └── 13 fichiers de tests (voir §24)
```

Architecture conceptuelle (brief §2) :

```
CalibrationParameterSet → CalibrationExperiment → (Dataset Window) → Backtest → CalibrationExperimentResult → Comparison
```

`CalibrationExperimentResult` **ne devient jamais** automatiquement une configuration de production — aucun
type/fichier de ce lot n'écrit dans `appsettings`, une constante, `RiskPolicy`, `EntryTriggerBuilder` ou
`RegimeEngine` (brief §43, vérifié §17/§18 ci-dessous).

Aucun fichier protégé (§26) n'a été modifié. Aucune méthode de `BacktestEngine` n'a été changée :
`CalibrationExperimentRunner` appelle `BacktestEngine.RunFullBacktestWithRisk` (Lot 14.8) **exactement comme
elle existe déjà**, une seule fois par expérience.

---

## §3. CalibrationParameterSet

`CalibrationParameter` est une abstraction générique typée (`Integer`/`Decimal`/`Boolean`/`Enum`), jamais un
type bespoke par constante de production. Chaque paramètre porte un `Unit` documenté ("score", "bars",
"ratio", "points", "ticks") et des bornes optionnelles `Min`/`Max`/`Step` — une valeur hors plage est
**rejetée à la construction**, jamais clampée (brief §41).

```csharp
CalibrationParameter.Decimal("AmbiguityThreshold", 0.95m, "score", min: 0m, max: 1m);
CalibrationParameter.Integer("MeasurementHorizonBars", 10, "bars", min: 1, max: 50);
```

`CalibrationParameterSet` regroupe des paramètres nommés de façon unique, est immuable (aucune méthode de
mutation), et son fingerprint est indépendant de l'ordre d'insertion (tri par nom avant hachage).

Seul un paramètre exploratoire (`AmbiguityThreshold`, informational) est utilisé dans les tests/l'expérience
de démonstration — voir §17 pour pourquoi ce paramètre précis n'est **pas** réellement injecté dans le
pipeline.

---

## §4. DatasetSpecification

`CalibrationDatasetSpecification` déclare Provider/Symbol/Timeframe/Start/End (brief §9). `CalibrationDataset`
associe cette déclaration à une `HistoricalSeries` déjà chargée et calcule son `Fingerprint` en réutilisant
**verbatim** `HistoricalSeriesFingerprint.Compute` (Lot 14.2) — brief §10 : "NE PAS créer un deuxième
algorithme SHA". Une même instance de `CalibrationDataset` est partagée entre toutes les expériences d'une
même grille : le dataset n'est jamais rechargé/retéléchargé par expérience (`CalibrationExperimentSet.Build`).

---

## §5. Window model

`CalibrationWindow` (Role/Start/End, demi-ouvert `[Start, End)`) est le pendant calibration-spécifique de
`BacktestWindow` (Lot 14.1), avec un `Role` en enum fermé (`Train`/`Validation`/`Oos`) plutôt qu'une chaîne
libre — brief §11.

## §6. TRAIN / §7. VALIDATION / §8. OOS

`CalibrationWindowSet.TryCreate` valide, en un seul point, TOUTES les règles du brief §12/§14 :

- chaque fenêtre est dans les bornes du dataset ;
- `TRAIN.End <= VALIDATION.Start` ;
- `VALIDATION.End <= OOS.Start` ;
- `TRAIN.End <= OOS.Start` (interdit explicitement OOS avant TRAIN, même si VALIDATION est absent du chemin).

Ces trois inégalités, combinées à l'invariant `Start < End` de chaque fenêtre, couvrent aussi bien le
chevauchement que "VALIDATION avant TRAIN" et "OOS avant TRAIN" — testé exhaustivement
(`CalibrationWindowTests`). **Rejeté, jamais auto-corrigé** (brief §14).

---

## §9. Warmup

`CalibrationWarmupContract(RequiredWarmupBars)` distingue explicitement DATA REQUIRED FOR WARMUP (les bars
strictement avant `TRAIN.Start`) de DATA USED FOR CALIBRATION RESULT (les bars à l'intérieur des fenêtres).
`TryValidate` rejette un dataset dont le lead-in avant TRAIN est insuffisant — **jamais** satisfait en
empruntant des bars de VALIDATION/OOS (brief §16).

Le paramètre `warmupBars` passé à `BacktestEngine.RunFullBacktestWithRisk` (convention indexée héritée du
Lot 14.1) est calculé comme le nombre de bars strictement avant `TRAIN.Start` — toujours `>=
RequiredWarmupBars` une fois l'expérience construite. Ce paramètre n'affecte que le `BacktestSignalResult.Status`
(Warmup vs Ready, purement informatif — vérifié par lecture de `BacktestEngine.RunSignalPipeline` : aucune
branche ne dépend de `isWarmup` pour la production du signal lui-même) ; le découpage TRAIN/VALIDATION/OOS
du Lot 14.9 se fait **entièrement par timestamp**, indépendamment de cette valeur.

---

## §10. Experiment

`CalibrationExperiment.TryCreate(parameterSet, dataset, windows, setup)` :

1. valide le contrat de warmup (§9) — échoue explicitement sinon ;
2. calcule `ConfigurationFingerprint` (§12) ;
3. dérive `ExperimentId = "CAL-" + fingerprint[..16]` — **jamais** un `Guid.NewGuid()` (brief §19).

`CalibrationExperimentSetup` regroupe tout ce qu'il faut pour rejouer `BacktestEngine.RunFullBacktestWithRisk`
au-delà du paramètre calibré et des données : `InstrumentRiskSpecification`, `RiskPolicy`, `InitialCapital`,
et les configurations `Measurement`/`Execution`/`PnL`/`Cost`/`Risk` **déjà existantes**, inchangées (Lots
14.1/14.4-14.8) — aucun type de configuration n'est dupliqué.

---

## §11. Grid

`CalibrationGridAxis` regroupe les valeurs candidates d'UN paramètre (même Name/Type/Unit). `CalibrationGrid.
GenerateParameterSets` produit le produit cartésien de façon déterministe : le premier axe varie le plus
lentement, le dernier le plus vite (brief §29, vérifié bit à bit par `CalibrationGridTests` sur l'exemple du
brief : A={0.90,0.95,1.00} × B={10,20} → 6 combinaisons dans l'ordre exact
`(0.90,10)(0.90,20)(0.95,10)(0.95,20)(1.00,10)(1.00,20)`). Aucune sélection, aucun classement — la grille ne
fait que générer (brief §5/§28).

`CalibrationExperimentSet.Build` convertit une liste de `CalibrationParameterSet` (issue de la grille, ou une
liste explicite d'un seul élément pour une expérience unique — brief §27) en `CalibrationExperiment`s
partageant le même Dataset/Windows/Setup, dans l'ordre d'entrée exact.

---

## §12. Fingerprints

`CalibrationFingerprint` (internal, réutilise `BacktestFingerprint.Sha256Hex`) calcule :

- `ComputeParameterSetFingerprint` : paramètres triés par nom → SHA-256 ;
- `ComputeConfigurationFingerprint` : `ProtocolVersion` + ParameterSet + Dataset (spec + fingerprint réel) +
  les 3 fenêtres + WarmupContract + Instrument + Policy + InitialCapital + Measurement/Execution/PnL/Cost/Risk
  configuration — un **surensemble** de la liste minimale du brief §20 (qui ne cite explicitement que
  paramètres/dataset/timeframe/window/measurement/execution/risk/protocole), pour que TOUT ce qui influence
  réellement `RunFullBacktestWithRisk` soit couvert par les tests de mutation (§37/§38) ;
- `ComputeExperimentId` : `"CAL-" + fingerprint[..16]` ;
- `ComputeResultFingerprint` : par `(Window, Direction)`, inclut délibérément `ExperimentId` — un résultat
  appartient à SON expérience. Conséquence documentée et testée : comparer le `ResultFingerprint` de TRAIN
  entre DEUX expériences différentes (même si leurs métriques TRAIN sont identiques) donnera des hachages
  différents ; les tests de look-ahead/isolation de fenêtre (§15) comparent donc les **métriques** elles-mêmes,
  jamais ce hash inter-expérience.

---

## §13. Determinism

Deux expériences construites à partir d'entrées identiques produisent le même `ExperimentId` et le même
`ConfigurationFingerprint` (`CalibrationExperimentTests.TwoExperiments_WithIdenticalInputs_...`). La grille
produit la même séquence de fingerprints à chaque appel (`CalibrationGridDeterminismTests`). Aucune
`DateTime.UtcNow`, aucun `Guid.NewGuid()`, aucun `System.Random` non seedé n'existe dans
`Backtest/Calibration/` (vérifié par relecture de chaque fichier).

---

## §14. Run isolation

`CalibrationExperimentRunner.Run` est une méthode **statique et sans état** : chaque appel construit son
propre `BacktestScenario` et délègue à un `new BacktestEngine()` (Lot 14.1/14.3 : déjà sans état par
construction). `CalibrationRunIsolationTests` prouve qu'exécuter Expérience A, puis B, puis A à nouveau,
donne un résultat A identique à chaque fois — aucune contamination inter-expérience.

---

## §15. Look-ahead

Stratégie d'exécution (voir la doc du code, `CalibrationExperimentRunner`) : le pipeline complet
(`RunFullBacktestWithRisk`) tourne **une seule fois** sur tout le dataset, jamais une fois par fenêtre. Ceci
repose sur la garantie déjà prouvée par `BacktestFoundationLookAheadTests`/`BacktestSignalPipelineLookAheadTests`
(Lots 14.1/14.3, toujours en vigueur, non modifiés) : le bar `i` ne dépend que de `[0..i]`. Trancher les
résultats déjà calculés par fenêtre calendaire, après coup, est donc équivalent à ré-exécuter par fenêtre —
vérifié directement (pas seulement affirmé) par deux tests :

- `CalibrationLookAheadTests` ("Future Data Test", brief §35) : ajouter davantage de bars OOS après une
  exécution ne change ni TRAIN ni VALIDATION (comparaison par métriques, cf. §12) ;
- `CalibrationWindowIsolationTests` ("OOS Isolation Test", brief §36) : réduire VALIDATION/OOS à une fenêtre
  minimale (mais suffisante pour l'horizon de mesure) ne change pas TRAIN.

**Découverte documentée en cours de développement** : réduire la queue du dataset EN DESSOUS du
`HorizonBars` de mesure/exécution fait légitimement chuter `PositionCount` pour les derniers signaux de
TRAIN (ils passent en `InsufficientFutureData`) — un comportement déjà établi et testé au Lot 14.4
(`ScientificMeasurementEngineIntegrationTests` : "le signal ne change jamais, la mesure peut différer près
d'une frontière de données"), pas un look-ahead. `CalibrationWindowIsolationTests` laisse délibérément au
moins `HorizonBars` bars après `TRAIN.End` pour ne pas confondre les deux phénomènes.

---

## §16. Configuration immutability

`CalibrationParameterSet`/`CalibrationParameter`/`CalibrationExperimentSetup` sont des `record` immuables
(pas de setter) ; `CalibrationExperiment` est une classe scellée sans setter public. Une "modification"
produit toujours une nouvelle instance (`CalibrationParameterSetTests.ConfigurationMutationTest_...`) — la
fingerprint change, l'instance d'origine reste inchangée.

---

## §17. Production immutability

`AmbiguityGateThreshold` reste `private const double ... = 0.95` dans `EntryTriggerBuilder.cs`, **non
modifié**. Ce paramètre est représentable dans le framework (`CalibrationParameter.Decimal("AmbiguityThreshold",
0.95m, "score")`, utilisé dans les tests/l'expérience de démonstration), mais il est
**NOT CALIBRATABLE IN LOT 14.9** au sens strict du brief §7 : rien dans `CalibrationExperimentRunner` ne
l'injecte dans `EntryTriggerBuilder` (qui ne fournit aucun point d'injection - c'est une constante privée
d'un fichier protégé), donc faire varier ce `CalibrationParameter` d'une expérience à l'autre change bien le
`ConfigurationFingerprint` (`CalibrationExperimentTests.ConfigurationIsolation_...`) mais n'a **aucun** effet
sur le comportement réel du pipeline. Documenté ici plutôt que contourné (brief §62).

`CalibrationProtectedFilesImmutabilityTests` vérifie, au niveau fichier, que la constante `0.95` et les
signatures publiques de tous les fichiers protégés (§26) sont toujours présentes après une série
d'expériences.

---

## §18. Risk immutability

`RiskEngine.cs`, `RiskPolicy.cs`, `InstrumentRiskSpecification.cs`, `RiskEngineRequest.cs`, `RiskAssessment.cs`
ne sont référencés qu'en LECTURE par `Backtest/Calibration/` (aucune modification). `BacktestRiskConfiguration`
(Lot 14.8) est transportée telle quelle dans `CalibrationExperimentSetup.RiskConfiguration` — jamais
recréée/réinterprétée. Vérifié par `CalibrationProtectedFilesImmutabilityTests.RiskEngineFamily_FilesAreIntact`.

---

## §19. Yahoo dataset

`CalibrationYahooIntegrationTests` réutilise exactement la recette MES=F/M5/45 jours des lots précédents
(`YahooHistoricalBarSource`, ~8900 barres) — aucune dépendance ATAS, aucune capture live (brief §46). La
fenêtre étant glissante (`DateTime.UtcNow`), le test n'affirme que des invariants structurels (9 résultats,
Id/fingerprint cohérents, `PositionCount <= SignalCount`), jamais un P&L/nombre de barres figé — même
discipline que `RiskYahooIntegrationTests` (Lot 14.8).

---

## §20. First experiment

Une expérience unique (`CalibrationExperimentSet`/`CalibrationExperiment` avec un seul `CalibrationParameterSet`)
traverse Yahoo → Signal → Measurement → Execution → Cost → Risk → PnL via
`BacktestEngine.RunFullBacktestWithRisk`, puis produit 9 `CalibrationExperimentResult` (TRAIN/VALIDATION/OOS
× ALL/BUY/SELL). Cette expérience sert **uniquement** à valider le framework (brief §47) — aucun seuil n'est
recommandé à partir de son résultat.

---

## §21. Résultats

`CalibrationExperimentResult` porte `SignalCount`, `PositionCount`, `GrossPnL`, `FinalEquity`,
`MaximumDrawdown`, `WinRate`, `MedianReturn`, `MedianMfe`, `MedianMae`, `HitRates` (une entrée par seuil de
`MeasurementConfiguration.HitThresholds`), `Status` (`NoData` explicite si zéro signal, jamais un zéro
silencieux) et son propre `ResultFingerprint`. Toutes ces valeurs sont des **agrégations** (comptage / somme /
médiane / ratio) de résultats déjà calculés par les Lots 14.4-14.8 (`MeasurementResult`, `PositionRiskOutcome`),
filtrés par fenêtre/direction — **aucune formule de Return/MFE/MAE/PnL/Drawdown n'est réimplémentée** (brief
§22/§26).

`GrossPnL`/`FinalEquity`/`MaximumDrawdown` sont dérivés du `NetPnL` par position du Lot 14.8 (post
sizing-par-risque ET coûts) — le nom `GrossPnL` suit la nomenclature du brief §22, mais aucune valeur "brute"
pré-coût ne survit à la substitution de quantité du Lot 14.8 ; c'est documenté explicitement plutôt qu'un
second calcul parallèle inventé. **Aucun score combiné/pondéré n'existe** (brief §26 - interdit).

---

## §22. Limitations

- Le breakdown par régime (brief §24) est **DEFERRED** : `Decision.Winner` existe par bar dans
  `BacktestSignalResult` mais n'est pas propagé jusqu'à `PositionRiskOutcome`/`MeasurementResult`. Le
  joindre nécessiterait une jointure supplémentaire par `PositionId`/`SignalBarIndex` que ce lot ne construit
  pas (non demandé comme obligatoire par le brief, qui autorise explicitement ce report).
- `AmbiguityThreshold` est représentable mais **NOT CALIBRATABLE YET** (§17) : aucun point d'injection
  n'existe dans `EntryTriggerBuilder` sans modifier un fichier protégé.
- Les autres paramètres candidats du brief §6 (regime windows, signal thresholds au sens Engine.Signal,
  quantity constraints au-delà de `RiskPolicy`/`InstrumentRiskSpecification`) ne sont pas exposés dans ce
  lot — seuls Measurement.HorizonBars/HitThresholds, Execution.HorizonBars, et le triplet Risk
  (EnableRiskControls/RiskDistance/MaxExposure) sont réellement injectables aujourd'hui, via les
  configurations Lot 14.4/14.5/14.8 existantes.
- Pas de parallélisation (brief §50 : exactitude/isolation/reproductibilité/déterminisme d'abord).

---

## §23. What is NOT calibrated

Aucune sélection automatique de paramètre, aucun `BestThreshold`/`OptimalThreshold`, aucun score pondéré,
aucune modification de `AmbiguityGateThreshold` (reste 0.95), aucune calibration de Stop Loss/Risk, aucun
walk-forward automatique, aucun Monte Carlo, aucun machine learning, aucune mise à jour de configuration de
production.

---

## §24. Tests

`Tests/Backtest/Calibration/` — 13 fichiers, 61 tests, tous PASS :

| Fichier | Couvre |
|---|---|
| `CalibrationParameterTests` | typage, unités, bornes/step rejetés jamais clampés (§39-42) |
| `CalibrationParameterSetTests` | unicité, immutabilité, fingerprint identité/mutation (§8/§37/§52) |
| `CalibrationDatasetSpecificationTests` | validation dataset (§9) |
| `CalibrationWindowTests` | demi-ouvert, ordre TRAIN<VALIDATION<OOS, rejets (§11/§12/§14/§54) |
| `CalibrationGridTests` | 2×3=6, ordre déterministe (§28/§29/§53) |
| `CalibrationGridDeterminismTests` | grille = fonction pure (§29/§30) |
| `CalibrationExperimentTests` | construction, id/fingerprint déterministes, warmup rejeté, isolation de configuration (§15/§18-20/§56) |
| `CalibrationFingerprintTests` | mutation dataset/paramètres/config/fenêtre (§37/§38/§45) |
| `CalibrationRunIsolationTests` | A/B/A sans contamination (§32) |
| `CalibrationLookAheadTests` | Future Data Test (§35) |
| `CalibrationWindowIsolationTests` | OOS Isolation Test (§36) |
| `CalibrationSerializationTests` | round-trip JSON sans perte (§44) |
| `CalibrationYahooIntegrationTests` | intégration réseau MES=F/M5/45j (§46/§47/§51) |
| `CalibrationProtectedFilesImmutabilityTests` | §57/§58/§61 |

Fixtures partagées : `CalibrationTestFixtures` (instrument/policy/setup/dataset/fenêtres synthétiques,
même discipline seedée que `BacktestTestSeriesBuilder`, Lot 14.1).

---

## §25. Build

DEBUG : succès, 0 avertissement, 0 erreur.
RELEASE : succès, 0 avertissement, 0 erreur.

---

## §26. Protected files

Vérifiés intacts (contenu + signatures publiques, `CalibrationProtectedFilesImmutabilityTests`) :

`RiskEngine.cs`, `RiskPolicy.cs`, `InstrumentRiskSpecification.cs`, `RiskEngineRequest.cs`,
`RiskAssessment.cs`, `DecisionArbitrator.cs`, `EntryTriggerBuilder.cs` (y compris
`AmbiguityGateThreshold = 0.95`), `TradePlanBuilder.cs`, `RegimeEngine.cs`, `EvidenceFusionEngine.cs`,
`FusionStateManager.cs`, `DecisionEngine.cs`, `SignalEngine.cs`, `EntryEngine.cs`, `EntryTriggerEngine.cs`,
`YahooHistoricalBarSource.cs`, `MarketContextFactory.cs`, `BacktestEngine.cs`.

Aucun de ces fichiers n'apparaît dans le diff de ce lot.

---

## §27. Next lot

**LOT 14.10 — SCIENTIFIC STOP LOSS / RISK DISTANCE CALIBRATION**
