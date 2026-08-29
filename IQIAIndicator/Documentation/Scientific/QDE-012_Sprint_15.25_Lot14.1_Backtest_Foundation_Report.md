# QDE-012 — Sprint 15.25 — Lot 14.1
# Backtest Engine Foundation — Historical Data + MarketContext + Scenario

**Type** : LOT D'IMPLÉMENTATION
**Portée** : fondation du Backtest Engine (données historiques, contexte de marché partagé, scénario, squelette d'orchestration)
**Date** : 2026-08-19
**Branche** : `feature/structural-stability-v2`

---

## 1. OBJECTIF

Poser la fondation du Backtest Engine IQIA : une base historique propre, déterministe et sans dépendance ATAS, sur laquelle les lots suivants (14.2 à 14.10) pourront brancher le pipeline scientifique existant.

Conformément au brief, ce lot **ne simule ni ordre, ni compte, ni PnL, ni position, ni coût**, et **ne crée aucune stratégie de stop-loss**. Il construit exclusivement : `HistoricalBar`, `HistoricalSeries`, `IHistoricalBarSource`, `MarketContextFactory`, `BacktestWindow`, `BacktestScenario`, et un squelette d'orchestration (`BacktestEngine`) qui parcourt les barres, construit un `Core.MarketContext`, le valide, et exécute le `RegimeEngine` réel — rien de plus.

Le rapport LOT 13 avait conclu que le pipeline scientifique est déjà largement indépendant d'ATAS et que le Backtest Engine devait devenir un **hôte alternatif** du pipeline, jamais un second IQIA. Ce lot applique cette conclusion : `MarketContextBuilder` (ATAS) et `BacktestEngine` (historique) appellent désormais **la même** fabrique de contexte.

---

## 2. ARCHITECTURE IMPLÉMENTÉE

```
Yahoo/CSV (futur)              ATAS IndicatorCandle
        │                              │
        ▼                              ▼
IHistoricalBarSource          MarketContextBuilder.Build
        │                              │
        ▼                              │
  HistoricalSeries                     │
        │                              │
        └──────────┬───────────────────┘
                    ▼
          Core.MarketContextFactory      ← LOGIQUE PARTAGÉE UNIQUE
          (Median, TypicalPrice, ElapsedMinutes,
           SessionInfo placeholder, forme Clock/Execution)
                    │
                    ▼
              Core.MarketContext
                    │
                    ▼
          MarketContextValidator          ← EXISTANT, inchangé
                    │
                    ▼
              RegimeEngine.Collect        ← EXISTANT, inchangé
                    │
                    ▼
              EvidenceSet
                    │
                    ▼
   (point de branchement Lot 14.3 : Fusion → Decision → Signal →
    Entry → EntryTrigger → TradePlan → Risk → Execution)
```

`BacktestEngine.Run` orchestre le tout et produit un `BacktestFoundationResult` minimal (compteurs + empreinte déterministe), sans aucune notion de trade.

---

## 3. FICHIERS CRÉÉS

### Production (10 fichiers, 1261 lignes)

| Fichier | Rôle |
|---|---|
| `Core/MarketData/HistoricalBar.cs` | Barre OHLCV immuable, indépendante de tout fournisseur |
| `Core/MarketData/HistoricalSeries.cs` | Séquence validée (Symbol/TimeFrame/TimeZone/Provider + barres) |
| `Core/MarketData/IHistoricalBarSource.cs` | Port neutre vis-à-vis du fournisseur de données |
| `Core/MarketData/CsvHistoricalBarSource.cs` | Adapter de développement lisant le format OHLCV CSV déjà produit par `ScientificDatasetCollector` |
| `Core/MarketContextFactory.cs` | Fabrique unique de `Core.MarketContext`, partagée ATAS/Backtest |
| `Backtest/BacktestWindow.cs` | Fenêtre temporelle nommée, immuable, demi-ouverte |
| `Backtest/BacktestScenario.cs` | Transport immuable et validé (série, fenêtre, capital, instrument, policy) |
| `Backtest/IBacktestBarObserver.cs` | Hook d'observation passif (test uniquement, n'influence jamais le moteur) |
| `Backtest/BacktestFingerprint.cs` | Canonicalisation déterministe (ScenarioId + hash), sans horloge système ni GUID |
| `Backtest/BacktestEngine.cs` | Orchestrateur : parcours des barres, contexte, validation, RegimeEngine |
| `Backtest/BacktestFoundationResult.cs` | Résultat minimal de ce lot (compteurs + empreintes) |

### Tests (14 fichiers, 1415 lignes, 87 tests)

| Fichier | Couverture |
|---|---|
| `Tests/Backtest/HistoricalBarTests.cs` | Invariants de barre (§18) |
| `Tests/Backtest/HistoricalSeriesTests.cs` | Invariants de série, rejet strict (§4/§18) |
| `Tests/Backtest/CsvHistoricalBarSourceTests.cs` | Lecture de la capture ATAS réelle, fenêtrage, erreurs |
| `Tests/Backtest/BacktestTestPaths.cs` | Résolution du chemin de la capture réelle |
| `Tests/Backtest/BacktestTestSeriesBuilder.cs` | Construction de séries synthétiques déterministes (réutilise `SyntheticSeriesCatalog`) |
| `Tests/Backtest/MarketContextFactoryTests.cs` | Chaque champ dérivé, valeurs vérifiées à la main (§19) |
| `Tests/Backtest/MarketContextParityTests.cs` | Parité de formules ATAS/Backtest ; PROVEN vs NOT PROVEN (§20) |
| `Tests/Backtest/BacktestScenarioTests.cs` | Absence de champ = scénario invalide (§10) |
| `Tests/Backtest/BacktestWindowTests.cs` | Fenêtre demi-ouverte, immuable |
| `Tests/Backtest/CapturingBarObserver.cs` | Observateur de test partagé |
| `Tests/Backtest/BacktestEngineTests.cs` | Comptage, warmup, BarsRejected toujours nul |
| `Tests/Backtest/BacktestFoundationLookAheadTests.cs` | **Test obligatoire §15** |
| `Tests/Backtest/BacktestRunIsolationTests.cs` | **Test obligatoire §16** |
| `Tests/Backtest/BacktestDeterminismTests.cs` | **Test obligatoire §17** |

---

## 4. FICHIERS MODIFIÉS

**Un seul fichier de production modifié** : `IQIAIndicator/Core/MarketContextBuilder.cs`.

Changement strictement une extraction : les ~30 lignes qui assemblaient inline le `MarketContext` (Median, TypicalPrice, ElapsedMinutes, `SessionInfo` vide, forme de `MarketClock`/`ExecutionContext`) ont été remplacées par un appel à `MarketContextFactory.Create(...)`, avec exactement les mêmes valeurs en entrée. Aucune formule, aucun comportement, aucune heuristique Replay/Live n'a changé — voir le diff en §16.2. Ce fichier n'est **pas** protégé par le brief.

Deux fichiers de données de test préexistants avaient déjà des modifications non liées à ce lot (héritées du working tree au démarrage) : `Tests/Research/StopLossCalibration/Output/A1_calibration_summary.txt` et `campaign_summary.txt`. Ce lot ne les a pas touchés.

---

## 5. HistoricalBar

`readonly record struct` cohérent avec les modèles `Core` existants (`PriceInfo`, `VolumeInfo`, `InstrumentInfo`). Contient exactement les champs demandés : `Timestamp`, `Open/High/Low/Close`, `Volume`, plus `BidVolume?/AskVolume?/Delta?/OpenInterest?` nullables — jamais zéro-remplis quand absents.

`Symbol`/`TimeFrame`/`Provider`/`SessionId`/`CurrentBar` sont volontairement absents (appartiennent à la série).

**Validation détectable, jamais imposée par le constructeur** (un `struct` C# accepte toujours `default`) : `Validate()` retourne la liste de toutes les violations ; `IsValid` en dérive. Le jeu de règles est un **sur-ensemble** de `MarketContextValidator` (ajoute `High>0`, `Low>0`, `Timestamp!=default`) mais reste **aussi permissif** sur `High<Close`/`Low>Close` (avertissements chez `MarketContextValidator`, jamais des erreurs ici) — le type ne doit jamais être plus strict que le pipeline qu'il alimente.

## 6. HistoricalSeries

Séquence validée d'une seule paire (Symbol, TimeFrame), d'un seul fournisseur. **Ne trie jamais, ne déduplique jamais, ne répare jamais** : `TryCreate`/`Create` rapportent l'intégralité des violations (identité vide, série vide, barre invalide, timestamp dupliqué **distingué** d'un timestamp désordonné) sans jamais construire un objet partiellement corrigé.

`TimeZone` est **obligatoire, sans valeur par défaut** — le rapport LOT 13 (§3) avait établi qu'aucune gestion de fuseau horaire n'existe nulle part dans ce dépôt ; un défaut ici aurait reproduit silencieusement cette ambiguïté sur des données réelles. Une copie défensive protège la série contre toute mutation ultérieure de la liste appelante.

## 7. IHistoricalBarSource

Port à une seule méthode (`Load(symbol, timeframe, from, to) → HistoricalSeries`), délibérément indépendant de tout fournisseur — conformément au brief, aucune abstraction `IYahooProvider` n'a été créée. Convention demi-ouverte `[from, to)`, identique à celle de `BacktestWindow`.

**`CsvHistoricalBarSource`** (adapter de développement) lit le format OHLCV exact déjà produit par `ScientificDatasetCollector.ToOhlcvCsv()` — le même format que les captures ATAS réelles déjà committées sous `Tests/Research/StopLossCalibration/RealMarket/RawCapture/Sprint15_18/`. Il **ne réutilise pas** directement `RealMarketOhlcvCsvReader` (qui vit dans l'assembly de tests, référençant le projet de production — l'inverser aurait créé une dépendance circulaire) ; conformément à la clause de repli du brief §6, un petit adapter autonome a été écrit plutôt qu'un refactoring de masse de l'infrastructure de tests existante, qui reste intégralement intacte.

Validé end-to-end contre la capture réelle `ScientificDataset_ES_M5_20260814_165421_ohlcv.csv` (1732 barres, ES/M5).

## 8. MarketContextFactory

**La seule modification recommandée par le LOT 13 (§6.3), appliquée ici.** Contient : `Median`, `TypicalPrice`, `ElapsedMinutes`, `UnknownSession()` (placeholder exact déjà utilisé, jamais remplacé par une vraie logique de session — brief §7), `Create` (primitive bas niveau, tout est paramètre explicite y compris les booléens de plateforme Realtime/Historical/Replay) et `CreateHistorical` (convenience backtest : `CurrentBar=index+1`, `IsRealtime=false`, `IsHistorical=true`, `IsReplay=false`, exactement les conventions fixées par le brief §7).

`MarketContextBuilder.Build` (ATAS) appelle désormais cette même fabrique — voir §16.2 pour la preuve par le diff qu'aucun comportement n'a changé.

## 9. BacktestWindow

Classe immuable `{ Name, From, To }`, convention demi-ouverte. `TRAIN`/`VALIDATION`/`OOS`/`HOLDOUT` ne sont que des exemples en commentaire — aucune constante n'impose ces noms.

## 10. BacktestScenario

Transport immuable `{ HistoricalSeries, BacktestWindow, InitialCapital, InstrumentRiskSpecification, RiskPolicy }`. **Ce lot ne calcule rien avec ces valeurs** — elles sont uniquement validées pour présence : `InitialCapital` doit être strictement positif (miroir exact de la règle `RiskEngine.Evaluate` Phase 1 — Section 3, `InitialCapital > 0`), `InstrumentRiskSpecification` doit exister **et** satisfaire son propre `IsValid`, `RiskPolicy` doit exister (ses champs individuels restent librement nullables, c'est leur contrat déjà établi — nullable = non contraint). Aucune de ces trois validations n'invente une valeur : l'absence est toujours un scénario invalide, jamais un défaut silencieux.

## 11. BacktestEngine

Orchestrateur sans état (aucun champ), donc chaque `Run()` construit ses propres instances de `RegimeEngine`/`MarketContextValidator` — l'isolation entre runs (§12) est garantie par construction, pas par convention. Boucle :

1. reçoit un `BacktestScenario` ;
2. pour chaque barre, dans l'ordre : construit `Core.MarketContext` via `MarketContextFactory.CreateHistorical` (jamais `series.Bars[index+1..]`) ;
3. valide via `MarketContextValidator` (inchangé) ;
4. si invalide : compte le rejet, **ne fait pas avancer l'état de `RegimeEngine`** (miroir exact du `return` anticipé d'`IQIAIndicator.OnCalculate` sur un contexte invalide) ;
5. si valide : exécute `RegimeEngine.Collect` (réel, inchangé) ;
6. le point exact où les lots 14.3+ brancheront Fusion → Decision → Signal → Entry → EntryTrigger → TradePlan → Risk → Execution est marqué par un commentaire dans le code, à l'endroit précis où `context`/`evidence` sont déjà disponibles.

`BacktestFoundationResult` contient uniquement `BarsProcessed`, `BarsRejected`, `WarmupBars`, `FirstTimestamp`, `LastTimestamp`, `ScenarioId`, `DeterministicHash` — aucun `BacktestTrade`, aucun PnL, aucune courbe d'equity.

---

## 12. LOOK-AHEAD PROTECTION

Deux tests obligatoires dans `BacktestFoundationLookAheadTests.cs`, **tous deux PASS** :

1. **Troncature** (§15, littéral du brief) : un run sur `Bars[0..200)` et un run sur `Bars[0..140)` (série synthétique identique jusqu'à 140) produisent des `MarketContext`/`EvidenceSet` **identiques champ par champ** pour toute barre commune — à l'exception documentée d'`IsLastBar` sur la seule barre `139` (légitimement vraie dans le run court, fausse dans le run long ; ce champ n'est lu par aucun modèle d'évidence, confirmé par inspection).
2. **Perturbation future** : deux séries de graines différentes sont épissées à l'index 140 (barres `< 140` identiques, barres `≥ 140` divergentes) ; les 140 premières barres produisent des contextes et évidences strictement identiques entre les deux runs.

La comparaison est **indépendante** de `BacktestFingerprint` (le mécanisme de hash de l'ENGINE) : elle appelle `Assert.Equal` champ par champ sur les objets réels produits par le moteur, pour ne jamais valider le hash avec lui-même.

Contrat respecté : `Signal(bar i)` = fonction de `Bars[0..i]` uniquement. La fenêtre scientifique complète (500 closes) n'est pas encore branchée dans ce lot (elle appartient au Lot 14.3, avec `SignalEngine`) ; ce lot garantit la même discipline au niveau de `RegimeEngine` (buffer 128 barres, borné par construction).

---

## 13. RUN ISOLATION

Trois tests obligatoires dans `BacktestRunIsolationTests.cs`, **tous PASS** :

- `ValidationResult_IsIdentical_WhetherRunAlone_OrAfterTrain_OnTheSameEngineInstance` — VALIDATION exécutée seule vs TRAIN→VALIDATION sur **la même instance** de `BacktestEngine` : résultats bit-identiques.
- `TrainResult_IsIdentical_WhetherRunAlone_OrBeforeValidation` — direction complémentaire, TRAIN n'est pas rétroactivement modifié par ce qui s'exécute après lui.
- `TwoIndependentEnginesOnTheSameScenario_ProduceIdenticalResults` — deux instances de `BacktestEngine` indépendantes, même scénario, résultats identiques.

Ceci est garanti par construction (`BacktestEngine` ne porte aucun champ, `RegimeEngine` est instancié localement à chaque `Run()`), mais était explicitement exigé comme preuve empirique par le brief §16 plutôt que comme simple constat d'architecture — les trois tests l'établissent.

Le risque RISK-10 du LOT 13 (`FusionStateManager` sans mécanisme de reset) reste d'actualité pour un futur lot : ce lot ne câble pas `FusionStateManager`, donc le risque n'est ni résolu ni réintroduit — il est simplement encore hors de portée. La règle « une instance par run, jamais de champ partagé » posée ici s'appliquera identiquement quand ce composant sera branché.

---

## 14. DETERMINISM

Quatre tests dans `BacktestDeterminismTests.cs`, **tous PASS** :

- Même scénario exécuté deux fois → résultat identique.
- Même scénario exécuté deux fois **avec un vrai délai d'horloge murale (50 ms) entre les deux** → `BarsProcessed`/`BarsRejected`/`WarmupBars`/timestamps/`ScenarioId`/`DeterministicHash` tous identiques. Ce test ne se contente pas d'inspecter le code pour l'absence de `DateTime.UtcNow` : il prouve empiriquement qu'un écart d'horloge réel ne peut pas faire diverger le résultat.
- Deux scénarios construits indépendamment à partir d'entrées identiques → même `ScenarioId` (fonction pure du contenu, jamais un GUID aléatoire).
- `InitialCapital` différent → `ScenarioId` différent ; `WarmupBars` différent → `DeterministicHash` différent mais **même** `ScenarioId` (le warmup est un paramètre du run, pas de l'identité du scénario).

`BacktestFingerprint` (SHA-256, `System.Security.Cryptography`) canonicalise l'intégralité des champs numériques/booléens des 9 modèles d'évidence (hors `Explanation` textuelle, dérivée et redondante, et hors les séries diagnostiques `WindowSizes`/`Fluctuations` de DFA — choix documenté dans le code) avec un formatage `CultureInfo.InvariantCulture` explicite. Aucun `DateTime.UtcNow`, aucun `Guid.NewGuid()` n'entre dans le calcul.

---

## 15. TESTS

**87 tests créés, 87 PASS, 0 FAIL, 0 SKIP** sur la suite ciblée `IQIAIndicator.Tests.BacktestTests` (Debug, exécution complète en 25,97 s).

| Catégorie | Fichier | Tests |
|---|---|---|
| Données | `HistoricalBarTests`, `HistoricalSeriesTests` | 24 |
| Source CSV | `CsvHistoricalBarSourceTests` | 7 |
| MarketContext | `MarketContextFactoryTests`, `MarketContextParityTests` | 20 |
| Scénario / Fenêtre | `BacktestScenarioTests`, `BacktestWindowTests` | 15 |
| Moteur | `BacktestEngineTests` | 9 |
| **Look-ahead (obligatoire)** | `BacktestFoundationLookAheadTests` | 2 |
| **Isolation (obligatoire)** | `BacktestRunIsolationTests` | 3 |
| **Déterminisme (obligatoire)** | `BacktestDeterminismTests` | 5 |
| Autres cas limites | — | 2 |

### Suite complète du dépôt (non-régression)

La suite complète (`dotnet test`, Debug, sans filtre) a été exécutée jusqu'à son terme :

```
Nombre total de tests : 336
     Réussi(s) : 335
    Ignoré(s) : 1
 Durée totale : 7,8015 Minutes
[exited with code 0]
```

**Aucun échec.** Le seul test non exécuté (`XunitWrappers.Sprint1515HistoricalReconciliationXunitTests.VerifySprint1514Unaffected`) est **préexistant et sans rapport avec ce lot** : marqué `[Fact(Skip=...)]` depuis le Sprint 15.22, avec la justification explicite dans le code « *Retired Sprint 15.22: premise (reproduces pre-15.22 Kalman numbers exactly) is now permanently false by design* ». Ce lot n'a touché ni ce fichier ni la classe qu'il documente. Aucune régression, aucun test préexistant modifié pour le faire passer.

Les 87 tests du LOT 14.1 (dont les 10 tests obligatoires de look-ahead/isolation/déterminisme, chacun s'exécutant sur des séries de 140 à 200 barres à travers le pipeline Regime réel — d'où des durées de plusieurs secondes) figurent tous parmi les 335 réussis, entremêlés naturellement avec l'ensemble des suites préexistantes (`GoldenDatasets`, `Risk`, `Decision`, `Fusion`, `Calibration`, `Research/StopLossCalibration`, `Infrastructure/ATAS`, etc.).

---

## 16. BUILD

### 16.1 Résultats

| Cible | Résultat | Avertissements | Erreurs |
|---|---|---|---|
| `IQIAIndicator.csproj` — Debug | **SUCCÈS** | 0 | 0 |
| `IQIAIndicator.csproj` — Release | **SUCCÈS** | 0 | 0 |
| `IQIAIndicator.Tests.csproj` — Debug | **SUCCÈS** | 0 | 0 |
| `IQIAIndicator.Tests.csproj` — Release | **SUCCÈS** | 0 | 0 |

Deux catégories d'erreurs de compilation ont été rencontrées et corrigées pendant ce lot (documentées ici par transparence, aucune n'a atteint l'état final) :

1. **`BacktestFingerprint.cs`** référençait `AdfResult`/`KpssResult`/etc. sans les `using` de leurs sous-espaces de noms respectifs (`Engine.Regime.Evidence.ADF`, `.KPSS`, `.HalfLife`, `.VarianceRatio`, `.CUSUM`, `.BaiPerron`, `.DFA`) — corrigé par ajout des 7 directives manquantes.
2. **`BacktestScenarioTests.cs`** écrivait `new IQIAIndicator.Core.InstrumentInfo(...)` en ligne : cas exact du piège déjà documenté dans `ScientificDatasetRecord.cs` et `IQIAIndicator.cs` (la classe `IQIAIndicator` masque le namespace racine pour toute référence qualifiée écrite directement dans du code appartenant à un sous-namespace d'`IQIAIndicator`) — corrigé par un `using IQIAIndicator.Core;` et le nom court `InstrumentInfo`.
3. Deux avertissements nullable (`CS8604`) dans `BacktestScenario.TryCreate` — l'analyse de flux du compilateur ne peut pas corréler « `violations.Count == 0` » avec « les paramètres sont non-null », alors que la logique le garantit. Résolu par un opérateur `!` documenté, au seul endroit où c'est sûr.
4. Un avertissement d'analyseur xUnit (`xUnit2013`, `Assert.Equal(0, collection.Count)` au lieu d'`Assert.Empty`) — corrigé.

### 16.2 Preuve de non-régression sur `MarketContextBuilder.cs`

Le diff complet (reproduit en annexe ci-dessous) montre une **pure extraction** : la construction inline de `MarketContext` est remplacée par un appel à `MarketContextFactory.Create` avec exactement les mêmes valeurs sources (`c.Open`, `c.High`, `c.Low`, `c.Close`, `c.Volume`, `c.Bid`, `c.Ask`, `c.Delta`, `c.Time`, `_symbol`, `_tickSize`, `_tickValue`, `_pointValue`, `_decimals`, `_firstBarTime`, `bar == 0`, `bar == currentBar - 1`, `isRealtime`, `bar < currentBar - 1`, `isReplay`). Aucune formule, aucune branche conditionnelle, aucune heuristique Replay/Live n'a été touchée.

```diff
-        return new MarketContext
-        {
-            BarIndex   = bar,
-            TimeFrame  = _timeFrame,
-            Price      = new PriceInfo(
-                c.Open, c.High, c.Low, c.Close,
-                (c.High + c.Low) / 2m,
-                (c.High + c.Low + c.Close) / 3m),
-            Volume     = new VolumeInfo(c.Volume, c.Bid, c.Ask, c.Delta),
-            Instrument = new InstrumentInfo(_symbol, _tickSize, _tickValue, _pointValue, _decimals),
-            Clock      = new MarketClock
-            {
-                CurrentTime    = c.Time,
-                CurrentDate    = DateOnly.FromDateTime(c.Time),
-                DayOfWeek      = c.Time.DayOfWeek,
-                Session        = new SessionInfo(string.Empty, DateTime.MinValue, DateTime.MaxValue),
-                ElapsedMinutes = bar == 0 ? 0 : (int)(c.Time - _firstBarTime).TotalMinutes,
-                IsFirstBar     = bar == 0,
-                IsLastBar      = bar == currentBar - 1
-            },
-            Execution  = new ExecutionContext
-            {
-                CurrentBar        = currentBar,
-                LastCalculatedBar = bar,
-                IsRealtime        = isRealtime,
-                IsHistorical      = bar < currentBar - 1,
-                IsReplay          = isReplay
-            }
-        };
+        return MarketContextFactory.Create(
+            barIndex:     bar,
+            currentBar:   currentBar,
+            timeFrame:    _timeFrame,
+            open:         c.Open, high: c.High, low: c.Low, close: c.Close, volume: c.Volume,
+            bidVolume:    c.Bid, askVolume: c.Ask, delta: c.Delta,
+            instrument:   new InstrumentInfo(_symbol, _tickSize, _tickValue, _pointValue, _decimals),
+            barTime:      c.Time, firstBarTime: _firstBarTime,
+            isFirstBar:   bar == 0, isLastBar: bar == currentBar - 1,
+            isRealtime:   isRealtime, isHistorical: bar < currentBar - 1, isReplay: isReplay);
```

*(Reformaté pour lisibilité dans ce rapport ; le diff réel appliqué au dépôt est visible via `git diff`.)*

Ce fichier n'a **aucun test qui l'exerce directement** (`MarketContextBuilder.Build` reste, comme documenté par `Tests/Core/MarketContextBuilderSelfHealingTests.cs`, un chemin nécessitant un `IndicatorCandle` réel, jamais construit dans ce dépôt) — cette absence de couverture est **antérieure à ce lot**, pas introduite par lui. La garantie de non-régression ici repose sur la lecture directe du diff (une extraction pure, sans changement de valeur) et sur `MarketContextParityTests.cs`, qui vérifie indépendamment que chaque formule de `MarketContextFactory` correspond exactement à celle que le code inline utilisait avant l'extraction.

---

## 17. VÉRIFICATION DES FICHIERS PROTÉGÉS

Vérifié par `git status --porcelain` filtré sur chacun des neuf noms de fichiers protégés, individuellement :

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
| `ScientificDatasetRecord.cs` | **INTACT** |

Également vérifiés (protection élargie du brief §1, RegimeEngine/EvidenceFusionEngine/FusionStateManager/DecisionEngine/SignalEngine/EntryEngine/EntryTriggerEngine) : **tous INTACT**.

Seul fichier de production modifié : `Core/MarketContextBuilder.cs` (non protégé, extraction pure — §16.2).

Seuil `AmbiguityGateThreshold = 0.95` (`EntryTriggerBuilder.cs:19`) : **non touché** — le fichier entier est intact.

---

## 18. LIMITATIONS

1. **`BarsRejected` est actuellement du code mort pour tout scénario construit via l'API publique.** `HistoricalSeries.Create` applique déjà, à la construction, chaque contrôle par-barre de `MarketContextValidator` (et davantage), et `BacktestScenario.Create` exige `InstrumentRiskSpecification.IsValid` (donc `TickSize > 0`, le seul contrôle de `MarketContextValidator` qui ne soit pas par-barre). Il en résulte qu'aucune barre construite à partir d'une série et d'un scénario valides ne peut jamais être rejetée par `MarketContextValidator` en aval. Le chemin de rejet reste dans `BacktestEngine` en défense en profondeur (et comme contrat pour un futur chemin de construction de contexte alternatif), mais n'est aujourd'hui exercé par aucun scénario réaliste — documenté explicitement par le test `BarsRejected_IsAlwaysZero_ForAScenarioBuiltThroughTheValidatingConstructors`.

2. **La parité ATAS/Backtest n'est prouvée qu'au niveau formule, jamais bout-en-bout.** Comme documenté en détail dans `MarketContextParityTests.cs` et rappelé au §16.2, aucun test de ce dépôt ne construit un `IndicatorCandle` réel — contrainte préexistante, confirmée par le LOT 13 et non levée par ce lot. Ce qui est PROUVÉ : `MarketContextFactory` calcule les mêmes formules que celles que `MarketContextBuilder` utilisait avant l'extraction (vérifié par lecture du diff + tests indépendants), et il n'existe plus qu'une seule implémentation de cette arithmétique. Ce qui N'EST PAS PROUVÉ : qu'un `IndicatorCandle` réel, dans une session ATAS réelle, produit effectivement les champs (`Open`/`High`/`Low`/`Close`/`Volume`/`Bid`/`Ask`/`Delta`/`Time`) que ce code suppose. Le test de parité bout-en-bout réellement probant (LOT 13 §15.4, critère d'acceptation LOT 14 §19.4 : rejouer `RawCapture/Sprint15_18` et comparer `Decision.Winner`/`EntryTrigger.Direction`/`TradePlan.Status` contre ce qu'ATAS a réellement enregistré) nécessite le pipeline Decision→EntryTrigger→TradePlan, explicitement hors périmètre de ce lot (§22/§23 du brief).

3. **`MarketContextFactory.CreateHistorical` projette les champs microstructure absents (`BidVolume`/`AskVolume`/`Delta`) sur `0m`.** `HistoricalBar` les garde `null` quand absents (jamais zéro-remplis) — la projection vers `VolumeInfo` (qui n'a pas de représentation nullable, Sprint 1 inchangé) doit choisir une valeur concrète. Sans conséquence fonctionnelle aujourd'hui : le LOT 13 avait établi qu'aucun modèle scientifique, aucune évidence, aucune règle de décision ne lit `VolumeInfo` — seul le dataset scientifique le ferait, hors périmètre de ce lot.

4. **`InstrumentInfo.Decimals` est fixé à `0` dans `BacktestEngine.Run`** faute de source dans `BacktestScenario` (qui transporte une `InstrumentRiskSpecification`, sans champ `Decimals`). Vérifié par recherche exhaustive : aucun consommateur de `.Decimals` n'existe nulle part dans le dépôt hors la déclaration elle-même — champ d'affichage UI sans effet sur aucun calcul.

5. **La fenêtre scientifique de 500 closes (RISK-12 du LOT 13) n'est pas encore branchée.** Ce lot exécute `RegimeEngine.Collect` (buffer interne de 128 barres, borné par construction), mais pas encore `SignalEngine`/les modèles scientifiques individuels, qui dépendront de cette fenêtre au Lot 14.3.

6. **`BacktestWindow` est transporté mais pas encore appliqué comme filtre.** Conformément au brief §10 (« ces valeurs sont uniquement transportées »), `BacktestEngine.Run` traite l'intégralité de `scenario.Series.Bars`, sans découper selon `scenario.Window`. Le filtrage par fenêtre (pour permettre à une seule grande série d'être découpée en TRAIN/VALIDATION/OOS/HOLDOUT) est une décision explicitement différée, pas un oubli.

Aucune de ces limitations ne contredit une conclusion du LOT 13 ; toutes sont soit des conséquences directement documentées de son périmètre, soit des choix explicitement dictés par ce brief.

---

## 19. NEXT LOT RECOMMENDATION

**LOT 14.2 — Yahoo Historical Data Source**, conformément à l'ordre déjà fixé par le brief (§28). `IHistoricalBarSource` est prêt à recevoir un `YahooHistoricalBarSource` sans aucune modification de `HistoricalBar`/`HistoricalSeries`/`BacktestEngine`.

---

## 20. FINAL VERDICT

### STATUS
**IMPLEMENTED**

### FILES CREATED
10 fichiers de production (1261 lignes), 14 fichiers de tests (1415 lignes, 87 tests) — liste complète en §3.

### FILES MODIFIED
`IQIAIndicator/Core/MarketContextBuilder.cs` uniquement — extraction pure, comportement inchangé (§16.2).

### PROTECTED FILES
**ALL INTACT** — vérifié individuellement (§17).

### BUILD DEBUG
**SUCCÈS** — 0 erreur, 0 avertissement (IQIAIndicator.csproj et IQIAIndicator.Tests.csproj).

### BUILD RELEASE
**SUCCÈS** — 0 erreur, 0 avertissement (IQIAIndicator.csproj et IQIAIndicator.Tests.csproj).

### TESTS
87/87 PASS sur la suite ciblée du LOT 14.1 (`IQIAIndicator.Tests.BacktestTests`). Suite complète du dépôt : 336 tests, 335 réussis, 1 ignoré (préexistant, sans rapport avec ce lot — `Sprint1515HistoricalReconciliationXunitTests`, retiré depuis le Sprint 15.22), **0 échec** (§15).

### LOOK-AHEAD
**PASS**

### DETERMINISM
**PASS**

### RUN ISOLATION
**PASS**

### YAHOO
**NOT IMPLEMENTED**

### RISK ENGINE
**NOT MODIFIED**

### ORDERS
**NONE**

### DLL
**NOT DEPLOYED**

### COMMIT
**NO**

### NEXT LOT
**LOT 14.2 — Yahoo Historical Data Source**
