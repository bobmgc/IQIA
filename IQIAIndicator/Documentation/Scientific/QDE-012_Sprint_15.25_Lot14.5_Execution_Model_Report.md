# QDE-012 — Sprint 15.25 — Lot 14.5
# Execution Model / Position Simulation Preparation

**Type** : LOT D'IMPLÉMENTATION
**Portée** : première couche de simulation de position théorique (TIME_HORIZON) sur données historiques Yahoo/synthétiques
**Date** : 2026-08-22
**Branche** : `feature/structural-stability-v2`

---

## 1. OBJECTIVE

Représenter une POSITION THÉORIQUE déterministe :

```
SIGNAL → ENTRY → POSITION OUVERTE → OBSERVATION FUTURE → EXIT THÉORIQUE → RESULTAT DE POSITION
```

Ce lot n'est PAS un moteur de trading : pas de Risk Engine, pas de capital, pas de coûts, pas de Stop Loss, pas de Take Profit, pas d'ordre, pas de broker, pas d'ATAS. Le résultat est une **THEORETICAL MARKET EXECUTION**, jamais un P&L de compte réel.

---

## 2. ARCHITECTURE

Nouveau namespace `Backtest/Execution/`, strictement séparé de `Backtest/Measurement/` (LOT 14.4) :

```
ExecutionCandidate.cs      — SignalBarIndex/SignalTimestamp/Direction/EntryPrice/TradePlanStatus, extraits de BacktestSignalResult
SimulatedPosition.cs       — PositionStatus (5 valeurs) + ExitReason (1 valeur : TimeHorizon) + SimulatedPosition (immutable record)
ExecutionConfiguration.cs  — HorizonBars, validé, jamais codé en dur
ExecutionSimulator.cs      — SimulateFromSignal / Simulate / SimulateCore / SimulateAll (le moteur, stateless)
BacktestExecutionResult.cs — compteurs techniques + positions
ExecutionFingerprint.cs    — fingerprint déterministe (réutilise BacktestFingerprint.Sha256Hex)
BacktestSimulationResult.cs — pairing Signal + Measurement + Execution, trois listes parallèles
```

```
BacktestSignalResult (Lot 14.3)
        ↓
ExecutionCandidate (extraction pure, brief §5)
        ↓
ExecutionSimulator (TIME_HORIZON, brief §10)
        ↓
SimulatedPosition (immutable, brief §6)
```

`Measurement` (LOT 14.4) et `Execution` (LOT 14.5) répondent à deux questions distinctes et **ne s'appellent jamais l'une l'autre** (vérifié par grep : aucun `using IQIAIndicator.Backtest.Execution` dans `Backtest/Measurement/`, aucun appel à `ScientificMeasurementEngine` depuis `Backtest/Execution/`) :
- Measurement : « Que s'est-il passé après le signal ? » (MFE/MAE/Return sur l'horizon complet, sans convention de sortie).
- Execution : « Selon une convention d'entrée/sortie donnée, quelle position théorique aurait existé ? » (une seule sortie effective, à `i+HorizonBars`).

Intégration `BacktestEngine` (brief §36) : `RunSimulation(scenario, warmupBars, measurementConfiguration, executionConfiguration)` — trois appels strictement séquentiels (`RunSignalPipeline` → `ScientificMeasurementEngine.MeasureAll` → `ExecutionSimulator.SimulateAll`), chacun retournant complètement avant le suivant. `BacktestSimulationResult` relie `SignalId`/`Measurement`/`Position` par **alignement d'index**, sans jamais recopier un champ d'un objet dans l'autre.

---

## 3. EXECUTION CANDIDATE

`ExecutionCandidate.FromSignal(BacktestSignalResult)` extrait exactement 5 champs (`SignalBarIndex`, `SignalTimestamp`, `Direction`, `EntryPrice`, `TradePlanStatus`) — jamais le `TradePlan` entier, jamais un recalcul du signal (brief §5). Vérifié structurellement par `ExecutionCandidateTests.FromSignal_NeverCopiesTheFullTradePlan_OnlyTheFourRelevantFields` (5 propriétés exactement, via réflexion).

---

## 4. SIMULATED POSITION

`sealed record`, immuable par construction (brief §2/§6). Champs : `PositionId`, `Status`, `Reason`, `Direction`, `EntryTimestamp`, `EntryPrice`, `EntryBarIndex`, `ExitTimestamp`, `ExitPrice`, `ExitBarIndex`, `ExitReason`, `HoldingBars`, `GrossPriceMove`, `Return`.

**`PositionId` = `SignalBarIndex`** (un `int`, jamais un `Guid.NewGuid()`) — ce lot produit exactement une position candidate par signal (brief §31 : « chaque signal doit être traité comme une position théorique indépendante »), donc l'index de barre est déjà un identifiant unique et déterministe.

**Aucun statut « EXECUTED » séparé** (décision documentée, brief §16 « selon le design retenu ») : le moteur résout entrée ET sortie en un seul appel synchrone (pas de flux temps réel/streaming dans ce lot, brief §41) — un état « entrée acceptée mais pas encore fermée » ne serait jamais observable. `PositionStatus` retenu : `Closed`, `NotExecutable`, `InvalidEntry`, `InsufficientFutureData`, `InvalidExit`.

---

## 5. ENTRY CONVENTION

**Une seule source, jamais de repli silencieux** (brief §8, point critique du lot) : `TradePlan.EntryPrice`, exactement la même convention que le LOT 14.4. Aucune source secondaire, aucun usage de `Close[i]`/`Open[i]`/`High[i]`/`Low[i]` (vérifié par relecture de `ExecutionSimulator.cs` — la barre `i` elle-même n'est jamais lue par `SimulateCore`, seule `bars[i+HorizonBars]` l'est).

Si `EntryPrice` est absent ou `<= 0` : `PositionStatus.InvalidEntry`, jamais de position fabriquée. `decimal` ne pouvant représenter NaN/Infinity, cette partie du contrôle du brief §20 est structurellement inatteignable via `TradePlan.EntryPrice` — documenté explicitement plutôt que défendu par du code mort.

---

## 6. EXIT CONVENTION

**TIME_HORIZON, seule politique de sortie implémentée** (brief §10/§45/§46 — aucun Stop Loss, aucun Take Profit, aucun trailing stop dans ce lot). Une position ouverte à `i` se ferme à `i + HorizonBars` si cette barre existe.

**ExitPrice = Close[i + HorizonBars]** — convention nommée explicitement **`THEORETICAL_CLOSE_EXIT`** (brief §11) : ceci NE signifie PAS qu'un ordre réel aurait été exécuté exactement à ce prix, c'est une convention de simulation scientifique, documentée comme telle dans le code (`ExecutionSimulator.SimulateCore`, commentaire précédant le calcul d'`ExitPrice`).

**ExitTimestamp** = le vrai `Timestamp` de la barre de sortie — jamais `DateTime.Now`/`UtcNow` (brief §12, vérifié par grep sur `Backtest/Execution/` : aucune occurrence de `DateTime.UtcNow`/`DateTime.Now`).

---

## 7. TIME HORIZON

`ExecutionConfiguration.HorizonBars`, jamais codé en dur. Testé à `Horizon=1/5/10`, avec une barre `i+11` contenant un mouvement énorme (High=1000) qui n'influence strictement rien le résultat à `Horizon=10` (`Horizon_ExitIsExactlyAtEntryPlusHorizon_BarBeyondItNeverInfluencesTheResult`). **PASS.**

---

## 8. EXIT REASON

Enum à un seul membre pour ce lot : `ExitReason.TimeHorizon` — `TIME_HORIZON` est la seule raison normale de fermeture (brief §17). Le type existe pour qu'un lot futur puisse ajouter `StopLoss`/`TakeProfit`/`TrailingStop`/`BreakEven` sans changer la forme de `SimulatedPosition`.

---

## 9. GROSS PRICE MOVEMENT

```
BUY  : GrossPriceMove = ExitPrice - EntryPrice
SELL : GrossPriceMove = EntryPrice - ExitPrice
```

Calculé en `decimal` (jamais converti en `double` avant ce calcul), indépendant du capital (brief §14). Testé exactement : BUY gagne (+5), SELL gagne (+5), BUY perd (-5), SELL perd (-5), break-even (0) pour les deux directions.

---

## 10. RETURN

```
BUY  : Return = (ExitPrice - EntryPrice) / EntryPrice
SELL : Return = (EntryPrice - ExitPrice) / EntryPrice
```

**Convention identique au LOT 14.4** (brief §15 : « Ne pas créer une deuxième définition du Return ») — prouvé empiriquement, pas seulement par relecture du code : `ExecutionMeasurementReturnCoherenceTests.Return_MatchesMeasurementResultReturn_ExactlyForEveryClosedPosition_GivenTheSameHorizon` exécute `RunSimulation` (Measurement + Execution avec le même `HorizonBars`) sur 300 barres OU synthétiques et vérifie, pour chaque barre où les deux moteurs atteignent un état comparable (`Measured`/`Closed`), l'égalité **exacte** (12 décimales) entre `MeasurementResult.Return` et `SimulatedPosition.Return`. **PASS**, `comparedCount > 0` confirmé.

---

## 11/12. BUY / SELL

`SimulatedPosition.Direction` porte directement le `DirectionCandidate` réellement produit par le pipeline — jamais un enum parallèle, jamais une traduction inversible. Testé exactement (brief §22/§23) : Entry=100, BUY, Exit=105 → GrossPriceMove=+5, Return=+5% ; Entry=100, SELL, Exit=95 → GrossPriceMove=+5, Return=+5%. **PASS.**

---

## 13. INVALID DATA

`PositionStatus.InvalidExit` si la barre de sortie échoue `HistoricalBar.Validate()` (High<Low, prix ≤ 0). **Inatteignable via une `HistoricalSeries` construite par `HistoricalSeries.Create`** (même situation que `BarsRejected` au LOT 14.1 et `InvalidFutureData` au LOT 14.4) — testé directement contre une liste de barres construite à la main, en contournant délibérément la validation de `HistoricalSeries` (`InvalidExitBar_HighBelowLow_IsRejectedExplicitly_NeverComputedSilently`). **PASS.**

---

## 14. INSUFFICIENT FUTURE DATA

Si `EntryBarIndex + HorizonBars >= series.Count` : `PositionStatus.InsufficientFutureData`, **aucune position `Closed` n'est jamais créée**, aucun horizon tronqué silencieusement (brief §18). Testé au cas limite exact `i = N-2`, `Horizon=10`. **PASS.**

---

## 15. NO_ACTION

`NO_ACTION`/`WATCH` → `PositionStatus.NotExecutable`, jamais `EntryPrice = 0`, jamais de position vide (brief §19/§7). Testé pour les deux valeurs. **PASS.**

---

## 16. LOOK-AHEAD PROTECTION

Quatre niveaux, tous testés :

1. **Formule pure** (§26) : barre `i+11` (mouvement énorme) n'influence jamais le résultat à `Horizon=10`.
2. **Look-ahead au niveau série** (§27) : Dataset A (220 barres) vs Dataset A+100 barres futures — chaque position dont l'exit tient déjà dans les 220 barres (`i<210`) est **bit-identique** entre les deux runs (`AppendingFutureBars_NeverChangesAPositionWhoseExitAlreadyFitInsideTheOriginalSeries`, comparaison `record` complète, 210 positions comparées).
3. **Barre `i+11` modifiée directement** (§27) : ne change jamais une position dont l'exit est à la barre 10 (`ModifyingBarEleven_NeverChangesAPositionWithExitAtBarTen`).
4. **Entry look-ahead (§28, OBLIGATOIRE)** : `ModifyingBarIPlusOne_NeverChangesTheEntryPriceOfAPositionCreatedAtBarI` — une barre `i+1` modifiée de façon extrême (+200 sur High/Low/Close) à travers le pipeline complet (`RunSimulation`) ne change ni `EntryPrice`, ni `EntryTimestamp`, ni `Direction`, ni `Status` de la position créée à `i`. **PASS** sur les quatre niveaux.

---

## 17. DETERMINISM

`SimulatedPosition` ne porte **aucun champ `DateTime.UtcNow`** — fonction pure de `(HistoricalSeries, ExecutionCandidate, ExecutionConfiguration)`. `ExecutionFingerprint.ComputeHash` (réutilise `BacktestFingerprint.Sha256Hex`, fichier LOT 14.1 non modifié) hash chaque champ directement. Testé : même scénario deux fois (y compris avec un délai d'horloge murale réel de 50 ms) → même hash ; une seule barre future modifiée → hash différent. **PASS.**

---

## 18. RUN ISOLATION

`ExecutionSimulator` est une classe statique sans aucun champ — rien à isoler par construction. Vérifié empiriquement : Run A → Run B → Run A → second Run A identique ; deux instances `BacktestEngine` indépendantes → résultats identiques. **PASS.**

---

## 19. MULTIPLE SIGNALS

`SimulateAll` traite chaque signal indépendamment — documenté explicitement **NO PORTFOLIO ACCOUNTING** dans le commentaire de classe (brief §31). `TotalCount` de `BacktestExecutionResult` est toujours égal à la somme exacte de chaque compartiment de statut, prouvant qu'aucune position n'est jamais fusionnée ou perdue.

---

## 20. OVERLAPPING POSITIONS

Aucune politique de positionnement inventée (brief §32) : si deux signaux se chevauchent (fenêtres `[i, i+H]` qui s'intersectent), les deux positions sont simulées intégralement, sans contrainte de capital/quantité/marge. Testé positivement (pas seulement documenté) : `MultipleSignals_EachProducesAnIndependentPosition_OverlapsAreNeverSuppressed` trouve au moins une paire de positions `Closed` chevauchantes dans un run réel (300 barres OU synthétiques) et confirme qu'aucune n'a été supprimée.

---

## 21. POSITION SIZE EXCLUSION

Aucun `ContractCount = 1` introduit comme logique métier (brief §33) — vérifié par grep sur `Backtest/Execution/` : aucune notion de quantité/taille de position n'existe nulle part dans ce namespace. Les métriques primaires sont `GrossPriceMove` (prix) et `Return` (relatif), jamais une quantité de contrats.

---

## 22. CAPITAL EXCLUSION

Aucun `InitialCapital`/`CurrentEquity`/`Balance`/`BuyingPower`/`RiskBudget` — vérifié par grep. `ExecutionConfiguration` ne contient que `HorizonBars`.

---

## 23. RISK ENGINE EXCLUSION

`RiskEngine`/`AccountState`/`PortfolioState`/`RiskPolicy`/`InstrumentRiskSpecification` : **aucune référence** dans `Backtest/Execution/` (vérifié par grep — les seuls imports sont `Core.MarketData` et `Engine.EntryTrigger`/`Engine.TradePlan`, en lecture seule pour les types `DirectionCandidate`/`TradePlan`).

---

## 24. COST EXCLUSION

Aucune commission, spread, slippage, frais, financement — vérifié par grep. Le résultat est un **GROSS THEORETICAL EXECUTION**, jamais un net.

---

## 25. ATAS EXCLUSION

Aucun `TradingManager`/`Portfolio`/`TradingStatisticsProvider`/type ATAS — vérifié par grep. Ce lot fonctionne entièrement avec `HistoricalSeries` (Yahoo ou synthétique).

---

## 26. YAHOO INTEGRATION

Réutilise les **mêmes paramètres exacts** validés au LOT 14.4 (MES=F, M5, fenêtre de 45 jours) — Yahoo n'offrant pas d'API de snapshot historique, un nouveau pull avec des paramètres IDENTIQUES est la façon la plus proche de « réutiliser la série déjà validée » (brief §40) ; le contenu exact des barres diffère légèrement de celui capturé au LOT 14.4 puisque les deux ancrent sur un « maintenant » glissant — documenté explicitement plutôt que supposé bit-identique.

Résultat mesuré (2026-08-22) :

| Paramètre | Valeur |
|---|---|
| Ticker | `MES=F` |
| Timeframe | M5 |
| From | 2026-07-08T17:24:41Z |
| To | 2026-08-22T17:24:41Z (45 jours) |
| Bars | **8833** |
| Fingerprint | `5375FEB5F1FA916BC84C1D1120BB2D858FEB271E792197CA21E8F52F1F7F2174` |

Exécution (`HorizonBars=10`) : `TotalCount=8833`, `Closed=1912`, `BUY=990`, `SELL=922`, `NotExecutable=6921`, `InvalidEntry=0`, `InsufficientFutureData=0`, `InvalidExit=0`. `InsufficientFutureData=0` s'explique simplement : les dernières barres de la série n'ont, par hasard, produit aucun signal directionnel dans cette capture — cohérent avec le fait que seuls les candidats directionnels avec `EntryPrice` valide atteignent le contrôle d'horizon. `DeterministicHash = 40735BF23DDCFE4C7265B800AEBD606801A295E2BD4D6D78982E991090A6122D`, re-vérifié identique par un second passage (`ExecutionSimulator.SimulateAll`) sur la même série déjà téléchargée. Aucune exception, aucune donnée ATAS.

---

## 27. TEST RESULTS

### Suite ciblée Lot 14.5 (`Tests/Backtest/Execution/`, 6 fichiers)

| Fichier | Couverture |
|---|---|
| `ExecutionCandidateTests.cs` | Extraction depuis BacktestSignalResult, non-copie du TradePlan complet |
| `ExecutionSimulatorFormulaTests.cs` | BUY/SELL/perte/break-even, holding bars, THEORETICAL_CLOSE_EXIT, horizon, InsufficientFutureData, InvalidExit, InvalidEntry, NO_ACTION/WATCH, validation, PositionId, immutabilité |
| `ExecutionSimulatorIntegrationTests.cs` | Look-ahead entrée (§28, obligatoire), look-ahead série, déterminisme, isolation, signaux multiples/chevauchement |
| `ExecutionMeasurementReturnCoherenceTests.cs` | Return identique bit-à-bit entre Measurement (Lot 14.4) et Execution (Lot 14.5) |
| `ExecutionYahooIntegrationTests.cs` | Réseau réel, MES M5 45 jours |

### 27.1 Résultats d'exécution

```
Suite ciblée Lot 14.5 (hors réseau) : 35/35 PASS (1 m 26 s)
Test réseau Yahoo (ExecutionYahooIntegrationTests) : 1/1 PASS (11 m 46 s)
Suite Lot 14.4 (non-régression, hors réseau) : 43/43 PASS
Suite Lot 14.3 (non-régression) : 35/35 PASS
```

### 27.2 Suite complète du dépôt

```
Total : 517 tests
Réussi(s) : 515
Ignoré(s) : 1
Échec(s) : 1
Durée : 49 min 46 s
```

517 = 481 (base LOT 14.1-14.4) + 36 (nouveaux tests Lot 14.5 : 35 hors-réseau + 1 réseau). L'ignoré est le même test préexistant déjà documenté. **Un échec** : `AdfLagSelectionScaleStabilityXunitTests.RunAll` (« lag selection at 128 bars... observed 146,05 ms/call ») — le même test de performance ADF déjà documenté comme flaky sous contention au LOT 14.3 (§14.4 de ce rapport-là), hors périmètre de ce lot (`Tests/GoldenDatasets/`, jamais touché). Ré-exécuté **seul** immédiatement après : **PASS en 7 s**. La suite complète de ce lot exécute désormais TROIS tests réseau réels en parallèle (Lot 14.3/14.4/14.5) en plus du pipeline scientifique complet sur des milliers de barres — la contention CPU qui en résulte est la cause documentée, pas une régression fonctionnelle de ce lot.

---

## 28. BUILD RESULTS

| Cible | Résultat | Avertissements | Erreurs |
|---|---|---|---|
| `IQIAIndicator.csproj` — Debug | **SUCCÈS** | 0 | 0 |
| `IQIAIndicator.csproj` — Release | **SUCCÈS** | 0 | 0 |
| `IQIAIndicator.Tests.csproj` — Debug | **SUCCÈS** | 0 | 0 |
| `IQIAIndicator.Tests.csproj` — Release | **SUCCÈS** | 0 | 0 |

Une occurrence transitoire de crash du compilateur (`csc.exe`, code -1073741523) a été rencontrée lors d'un premier essai de build des tests, sans rapport avec le code de ce lot — un second essai immédiat a réussi sans aucune modification (0 erreur, 0 avertissement), confirmant une contention machine ponctuelle plutôt qu'une erreur de compilation réelle.

---

## 29. LIMITATIONS

1. **Aucun statut `EXECUTED` séparé de `CLOSED`** — décision documentée en §4 : ce moteur batch/synchrone ne peut jamais observer un état intermédiaire, donc aucun test ne l'exerce (rien à exercer).
2. **`ExecutionSimulator.SimulateCore` lève `ArgumentOutOfRangeException` si `SignalBarIndex` ne correspond à aucune barre de `bars`** — un contrat d'appelant, pas un `PositionStatus`, puisque ce cas ne peut survenir qu'avec un appel manuel incohérent (jamais via `RunSimulation`/`SimulateAll`, qui dérivent toujours `SignalBarIndex` d'un `BacktestSignalResult` réellement produit pour une barre existante).
3. **`GrossPriceMove`/`Return` peuvent être négatifs** — conséquence directe et voulue des formules du brief §14/§15, pas un bug.
4. **Aucune politique de chevauchement/portefeuille** (brief §31/§32, explicitement différé) — chaque position est indépendante ; un futur lot devra introduire une politique de capital/quantité s'il souhaite en contraindre certaines.
5. **La cohérence Return Measurement/Execution (§10) n'est garantie que lorsque les deux moteurs partagent le même `HorizonBars`** — un appelant qui configurerait des horizons différents pour Measurement et Execution obtiendrait légitimement des `Return` différents ; ce n'est pas un défaut, seulement une conséquence documentée du fait que les deux moteurs restent indépendants (brief §4).

---

## 30. NEXT LOT

**LOT 14.6 — Position / P&L / Equity Curve Foundation**, conformément à l'ordre déjà fixé.

---

## STATUS
**IMPLEMENTED**
