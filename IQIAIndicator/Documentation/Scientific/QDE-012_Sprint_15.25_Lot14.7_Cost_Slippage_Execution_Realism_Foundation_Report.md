# QDE-012 — Sprint 15.25 — Lot 14.7 — Cost / Slippage / Execution Realism Foundation

**Date** : 2026-08-23
**Branche** : `feature/structural-stability-v2`
**Lot précédent** : Lot 14.6 — PnL & Equity Curve Foundation (VALIDÉ, GELÉ, PROTÉGÉ)
**Statut** : IMPLEMENTED

---

## §1. Objectif

Le Lot 14.6 produit un P&L **théorique brut** (`GrossPnL`), calculé directement à partir des prix théoriques
d'entrée/sortie de `SimulatedPosition` (Lot 14.5), sans aucune notion de coût. Le Lot 14.7 introduit la
fondation permettant de transformer ces prix théoriques en prix d'exécution réalistes, puis d'en dériver des
coûts et un P&L net :

```
Signal Price → Execution Model → Executed Price → Cost Model → Net PnL
```

Ce lot **n'implémente pas** un moteur d'exécution de type broker (voir §6 « Limites »). Il livre une couche
déterministe, testable et extensible pour : commissions, frais, spread, slippage, prix d'exécution, coûts
d'entrée/sortie, coût total du trade, et PnL net après coûts.

**Contrainte absolue** : le Lot 14.6 (et tout ce qui le précède) reste intact. Aucune méthode, aucun fichier
existant de `Backtest/Pnl/`, `Backtest/Execution/` n'a été modifié. L'intégration est strictement additive.

---

## §2. Audit préalable (état observé avant implémentation)

Avant toute écriture de code, l'architecture existante a été inspectée en détail :

- **`BacktestEngine.cs`** (`IQIAIndicator/Backtest/BacktestEngine.cs`) : chaque lot (14.1 → 14.6) ajoute EXACTEMENT
  une méthode publique, sans jamais modifier les précédentes (`Run` → `RunSignalPipeline` →
  `RunMeasuredSignalPipeline` → `RunSimulation` → `RunFullBacktest`). `RunFullBacktest` (Lot 14.6) enchaîne
  Signal → Measurement → Execution → P&L de façon strictement séquentielle et unidirectionnelle.
- **`Backtest/Execution/`** (Lot 14.5, gelé) : `ExecutionSimulator` produit des `SimulatedPosition` avec des prix
  **théoriques purs** (`EntryPrice = TradePlan.EntryPrice`, `ExitPrice = bars[exitBarIndex].Close`, convention
  THEORETICAL_CLOSE_EXIT). Aucune commission, spread, slippage nulle part (vérifié par grep).
- **`Backtest/Pnl/`** (Lot 14.6, gelé) : `PositionPnLCalculator.Calculate` implémente
  `GrossPnL = GrossPriceMove × PriceUnitValue × Quantity`. `PnLSummary`/`BacktestPnLResult` sont explicitement
  documentés comme « Gross » car aucun coût n'est encore soustrait (doc comment de `PnLSummary.cs` : « the name
  is kept literal for when a future lot subtracts costs »).
- **Instrument** : trois specs distinctes et volontairement séparées existent déjà — `Core.InstrumentInfo`
  (TickSize/TickValue/PointValue), `Engine.Risk.InstrumentRiskSpecification` (+ bornes de quantité +
  ContractMultiplier), `Backtest.Pnl.InstrumentPnLSpecification` (Symbol/PriceUnitValue/Currency). Aucun symbole
  (MES/ES/NQ) n'est hardcodé dans le moteur de backtest — confirmé par grep exhaustif.
- **Recherche d'une notion de coût préexistante** : recherche exhaustive de `slippage|commission|spread|fee` dans
  tout le repository. **Aucune abstraction de coût d'exécution n'existe nulle part** — les seules occurrences de
  « spread » sont statistiques (ADF/KPSS, cointégration), sans rapport avec un coût de trading. **Conclusion : le
  Lot 14.7 construit cette notion entièrement à partir de zéro, sans doublon possible.**
- **Tests/conventions** : xUnit, namespace de production `IQIAIndicator.Backtest.<Sous-dossier>`, namespace de
  test `IQIAIndicator.Tests.BacktestTests.<Sous-dossier>`, tests nommés `Sujet_Comportement_Condition`, helpers
  factories statiques privés, bannières `// ── §N: description ──`.
- **Harnais Yahoo** : pas d'outil séparé — chaque lot ajoute un test d'intégration xUnit
  (`<Lot>YahooIntegrationTests.cs`) qui télécharge `MES=F`/`M5` sur les 45 derniers jours via
  `YahooHistoricalBarSource`, construit un `BacktestScenario`, appelle la méthode du lot, et écrit les résultats
  via `ITestOutputHelper` ; soft-skip sur erreur réseau (jamais un échec de suite).

**Stratégie d'intégration retenue** : nouveau dossier `IQIAIndicator/Backtest/Cost/` (namespace
`IQIAIndicator.Backtest.Cost`), sibling de `Pnl`/`Execution`/`Measurement` — pas une sous-structure de
`Backtest/Execution/`, pour ne jamais mélanger de nouveaux fichiers dans un dossier gelé du Lot 14.5. Une seule
méthode ajoutée à `BacktestEngine.cs` : `RunFullBacktestWithCosts`, qui **appelle `RunFullBacktest` tel quel**
puis superpose le calcul de coût sur les résultats déjà produits.

---

## §3. Architecture livrée

```
Backtest
   │
   ├── Signal            (Lot 14.3, inchangé)
   ├── Measurement        (Lot 14.4, inchangé)
   ├── Execution          (Lot 14.5, inchangé)  — prix théoriques purs
   ├── Pnl                (Lot 14.6, inchangé)  — GrossPnL, EquityCurve, Drawdown
   │
   └── Cost               (Lot 14.7, NOUVEAU)
          ├── SlippageConfiguration / SpreadConfiguration
          ├── CommissionConfiguration / FeesConfiguration
          ├── ExecutionCostConfiguration        (config unifiée + interrupteur Enabled)
          ├── OrderSide / ExecutedPrice          (le "ExecutionPrice" du brief)
          ├── ExecutionPriceModel                (Signal Price -> Executed Price)
          ├── ExecutionCost                      (commission + fees + spreadCost + slippageCost = total)
          ├── PositionCostResult                 (le "ExecutionResult" du brief, par position)
          ├── PositionCostCalculator             (Executed Price -> Cost -> Net PnL)
          ├── NetEquityPoint / BacktestCostResult / BacktestCostResultBuilder
          ├── CostFingerprint                     (hash déterministe, réutilise BacktestFingerprint.Sha256Hex)
          └── BacktestFullResultWithCosts          (résultat de RunFullBacktestWithCosts)
```

`BacktestEngine.RunFullBacktestWithCosts` (nouvelle méthode, ajoutée en fin de classe, aucune ligne existante
modifiée) :

```csharp
public BacktestFullResultWithCosts RunFullBacktestWithCosts(
    BacktestScenario scenario, int warmupBars,
    MeasurementConfiguration measurementConfiguration, ExecutionConfiguration executionConfiguration,
    PnLConfiguration pnlConfiguration, ExecutionCostConfiguration costConfiguration)
{
    BacktestFullResult fullResult = RunFullBacktest(scenario, warmupBars, measurementConfiguration, executionConfiguration, pnlConfiguration);
    var positionCostResults = PositionCostCalculator.CalculateAll(
        fullResult.ExecutionResult.Positions, fullResult.PnLResult.PositionPnLResults, pnlConfiguration, costConfiguration);
    BacktestCostResult costResult = BacktestCostResultBuilder.Build(positionCostResults, pnlConfiguration.StartingCapital);
    return new BacktestFullResultWithCosts(fullResult, costResult);
}
```

`RunFullBacktest` (Lot 14.6) est appelée **telle quelle**, sans aucune modification de signature ni de corps —
c'est la garantie structurelle de non-régression : le côté « Gross » de `BacktestFullResultWithCosts.FullResult`
est, par construction, exactement ce que produirait un appel direct à `RunFullBacktest`.

---

## §4. Modèles

### ExecutedPrice (le « ExecutionPrice » du brief)

```csharp
public enum OrderSide { Buy, Sell }

public sealed record ExecutedPrice(
    decimal TheoreticalPrice, decimal Price, OrderSide Side, int Quantity,
    DateTime Timestamp, decimal SlippageApplied, decimal SpreadApplied);
```

`OrderSide` est volontairement distinct de `DirectionCandidate` (qui nomme la direction de la **position**, pas
le sens d'un **ordre** individuel) : une position longue (BUY_CANDIDATE) s'ouvre par un ordre Buy mais se ferme
par un ordre Sell — et inversement pour une position courte. `ExecutionPriceModel.EntryFillSide`/`ExitFillSide`
résolvent ce mapping.

### ExecutionCost

```csharp
public sealed record ExecutionCost(decimal Commission, decimal Fees, decimal SpreadCost, decimal SlippageCost, decimal TotalCost);
```

### PositionCostResult (le « ExecutionResult » du brief, renommé pour éviter la collision avec
`Backtest.Execution.BacktestExecutionResult` déjà existant)

```csharp
public sealed record PositionCostResult(
    int PositionId, PositionStatus Status, DirectionCandidate Direction,
    DateTime EntryTimestamp, decimal? TheoreticalEntryPrice, decimal? ExecutedEntryPrice,
    DateTime? ExitTimestamp, decimal? TheoreticalExitPrice, decimal? ExecutedExitPrice,
    decimal? GrossPnL, ExecutionCost? Cost, decimal? NetPnL,
    string Currency, int Quantity);
```

`GrossPnL` est copié **verbatim** depuis `PositionPnLResult.GrossPnL` (Lot 14.6) — jamais recalculé, exactement
la même discipline que Lot 14.6 avait déjà appliquée à `Return` (jamais recalculé depuis `SimulatedPosition`).

### ExecutionCostConfiguration

```csharp
public sealed record ExecutionCostConfiguration
{
    public required bool Enabled { get; init; }
    public required SlippageConfiguration Slippage { get; init; }
    public required SpreadConfiguration Spread { get; init; }
    public required CommissionConfiguration Commission { get; init; }
    public required FeesConfiguration Fees { get; init; }

    public static ExecutionCostConfiguration Disabled() => new() { Enabled = false, ... }; // tout à zéro
}
```

`Disabled()` est la configuration recommandée par défaut : `Enabled = false` est un véritable interrupteur,
indépendant des valeurs des sous-configurations — quand il est à `false`, `PositionCostCalculator` court-circuite
entièrement la logique de coût, quelles que soient les valeurs configurées par ailleurs.

---

## §5. Formules

Pour une position **Closed**, quantité Q, `PriceUnitValue` V (depuis `PnLConfiguration.Instrument`, réutilisé
sans duplication), spread complet S et slippage K (en unités de prix) :

```
adverse         = S/2 + K                                  (par jambe : entrée, sortie)

ExecutedEntry   = TheoreticalEntry  +/- adverse             (signe = sens de l'ordre d'entrée)
ExecutedExit    = TheoreticalExit   +/- adverse             (signe = sens de l'ordre de sortie)

SpreadCost      = 2 × (S/2) × V × Q  =  S × V × Q            (les deux jambes)
SlippageCost    = 2 × K × V × Q                              (les deux jambes)
Commission      = 2 × PerOrder + 2 × Q × PerUnit             (les deux jambes)
Fees            = 2 × PerOrder                                (les deux jambes)
TotalCost       = SpreadCost + SlippageCost + Commission + Fees

NetPnL          = GrossPnL - TotalCost
```

**Sens du slippage/spread (§5/§6 du brief)** : un ordre **Buy** est toujours exécuté au prix ASK (mid + S/2) et
subit un slippage toujours défavorable (+K) ; un ordre **Sell** est toujours exécuté au BID (mid - S/2) et subit
un slippage toujours défavorable (-K). Le sens de l'ordre dépend du **côté du fill** (entrée ou sortie), jamais
directement de la direction de la position : une position longue paie le ASK à l'entrée et reçoit le BID à la
sortie (perd le spread complet sur l'aller-retour) ; une position courte reçoit le BID à l'entrée et paie le ASK
à la sortie — même coût, par symétrie.

**Invariant de non double-comptage (§7 du brief), prouvé algébriquement et testé** :

```
GrossPnL(prix exécutés) = GrossPnL(prix théoriques) - (SpreadCost + SlippageCost)
NetPnL                  = GrossPnL(prix exécutés) - Commission - Fees
```

C'est-à-dire que calculer le P&L directement à partir des prix EXÉCUTÉS puis soustraire seulement
Commission+Fees donne EXACTEMENT le même résultat que `GrossPnL - TotalCost`. Ceci est vérifié explicitement par
`PositionCostCalculatorTests.CombinedCosts_Long_MatchesTheFullManualCalculation`.

**Exemple chiffré** (MES-like, V=5 $/point, Q=1, slippage=0.25, spread=0.5, commission=$2.00/ordre +
$0.50/contrat, fees=$0.10/ordre, Long entrée=100 → sortie=110) :

| Terme | Valeur |
|---|---|
| GrossPnL | 10 pts × $5 = **$50.00** |
| SpreadCost | 2 × 0.25 × $5 = **$2.50** |
| SlippageCost | 2 × 0.25 × $5 = **$2.50** |
| Commission | 2×$2.00 + 2×1×$0.50 = **$5.00** |
| Fees | 2×$0.10 = **$0.20** |
| TotalCost | **$10.20** |
| **NetPnL** | 50.00 − 10.20 = **$39.80** |

Le même calcul, exécuté en position courte symétrique (entrée=110 → sortie=100), produit exactement les mêmes
$50.00 / $10.20 / $39.80 — testé par `Short_EntryOneTen_ExitOneHundred_WithCosts`.

---

## §6. Hypothèses et limites

- Le spread est un **modèle synthétique configuré**, jamais dérivé de données Bid/Ask réelles (la série
  `HistoricalSeries` ne contient que OHLC) — conformément au brief §6 : « Ne pas prétendre simuler un spread réel
  si les données disponibles ne contiennent pas Bid/Ask ».
- Le slippage est **strictement déterministe** (aucune randomisation, aucune horloge système) — configuré en
  unités de prix, ou en ticks via `FromTicks(ticks, tickSize)` qui résout `ticks × tickSize` une seule fois à la
  construction.
- La commission/les frais sont appliqués **par ordre** (entrée ET sortie séparément) — convention standard d'un
  aller-retour (round-turn), jamais une seule fois par trade.
- **Hors scope (identique au brief §15)** : broker réel, ordres live, NinjaTrader/Rithmic/ATAS execution, order
  book, market impact avancé, partial fills, latency simulation, queue position, VWAP/TWAP, Risk Engine complet,
  optimisation des coûts, calibration empirique du slippage. Le Lot 14.7 est une fondation déterministe, pas un
  simulateur de microstructure.
- Le coût configuré dans le scénario Yahoo documenté au §11 (`commission: $0.85/ordre`, `fees: $0.10/ordre`,
  `slippage/spread : 1 tick`) est **illustratif** — un paramètre de simulation, pas une calibration empirique
  d'un broker réel (explicitement hors scope, brief §15).

---

## §7. Tests livrés

Nouveau dossier `IQIAIndicator/Tests/Backtest/Cost/` (namespace `IQIAIndicator.Tests.BacktestTests.Cost`), 10
fichiers :

| Fichier | Couverture |
|---|---|
| `SlippageConfigurationTests.cs` | None/Fixed/FromTicks, rejet valeurs négatives, tick size invalide, déterminisme |
| `SpreadConfigurationTests.cs` | Idem pour le spread |
| `CommissionConfigurationTests.cs` | Zéro, fixe par ordre, par contrat, rejets |
| `FeesConfigurationTests.cs` | Zéro, fixe, rejet |
| `ExecutionCostConfigurationTests.cs` | `Disabled()` = tout à zéro, `Create` avec défauts, composants explicites |
| `ExecutionPriceModelTests.cs` | BUY/SELL, slippage nul/positif, spread nul/symétrique, ASK/BID, quantité>1, résolution du sens de fill, rejet WATCH/NO_ACTION, déterminisme |
| `PositionCostCalculatorTests.cs` | Commission/fees isolés, coûts combinés + calcul manuel, Long/Short chiffrés, scaling quantité (1/2/10), équivalence coût-zéro (Disabled ET Enabled-mais-tout-à-zéro), positions non-Closed, validation `PnLConfiguration`, déterminisme, garde d'alignement d'index |
| `BacktestCostResultBuilderTests.cs` | Exemples chiffrés du Lot 14.6 réappliqués au NetPnL, agrégation TotalCost, tri chronologique + tie-break, StartingCapital invalide, équivalence numérique complète avec `BacktestPnLResultBuilder` |
| `BacktestFullResultWithCostsIntegrationTests.cs` | Déterminisme et isolation via `RunFullBacktestWithCosts`, équivalence Disabled ↔ `RunFullBacktest` direct, coûts non-nuls ne changent jamais le côté Gross |
| `CostYahooIntegrationTests.cs` | MES=F/M5/45 jours réel via `RunFullBacktestWithCosts` : baseline coût-zéro puis scénario coûts réalistes |

**Résultat de l'exécution ciblée** (`--filter FullyQualifiedName~BacktestTests.Cost`) :

```
Nombre total de tests : 78
Réussi(s)              : 78
Échoué(s)               : 0
Durée totale            : 8,14 minutes (dominé par l'unique test réseau Yahoo, ~8 min)
```

**Résultat de la suite complète du repository** (`dotnet test IQIAIndicator/Tests/IQIAIndicator.Tests.csproj -c
Debug`, tous les tests, tous lots confondus, tous tests réseau Yahoo inclus) :

```
Nombre total de tests : 642
Réussi(s)              : 641
Ignoré(s)               : 1   (test de performance pré-existant, déjà signalé au Lot 14.6, non modifié)
Échoué(s)               : 0
Durée totale            : 19,48 minutes
```

642 − 564 (total à la fin du Lot 14.6) = 78, exactement le nombre de tests ajoutés par ce lot — confirmation
que la totalité de la suite préexistante (564 tests) est ré-exécutée sans aucune modification et reste
intégralement au vert. Voir §9/§11 pour le détail de la non-régression et des résultats Yahoo.

---

## §8. Invariants vérifiés

1. **Déterminisme** : mêmes entrées + même configuration → même `ExecutedPrice`, même `ExecutionCost`, même
   `NetPnL`, même `DeterministicHash` — vérifié à chaque niveau (modèle de prix, calculateur de position,
   builder d'agrégat, moteur complet).
2. **Isolation d'exécution** : `RunA → RunB → RunA` reproduit exactement le premier résultat de RunA —
   `BacktestFullResultWithCostsIntegrationTests.RunA_RunB_RunA_ProduceIdenticalResultsForTheRepeatedRunA`.
3. **Non double-comptage** : preuve algébrique + test (`CombinedCosts_Long_MatchesTheFullManualCalculation`) que
   `NetPnL` calculé via `GrossPnL - TotalCost` coïncide exactement avec un calcul indépendant à partir des prix
   exécutés moins Commission/Fees seuls.
4. **Équivalence coût-zéro (§8/§9 du brief)** : `ExecutionCostConfiguration.Disabled()` produit, pour chaque
   position, `NetPnL == GrossPnL`, `ExecutedPrice == TheoreticalPrice`, `TotalCost == 0` — vérifié à trois
   niveaux : `PositionCostCalculatorTests` (position isolée), `BacktestCostResultBuilderTests` (courbe d'équité
   complète, comparée point par point à `BacktestPnLResultBuilder`), et
   `BacktestFullResultWithCostsIntegrationTests`/`CostYahooIntegrationTests` (moteur complet, données réelles).
5. **Le côté Gross n'est jamais modifié par la configuration de coût** — même avec des coûts non-nuls actifs,
   `FullResult.PnLResult.DeterministicHash` reste identique à celui produit par `RunFullBacktest` seul
   (`NonZeroCosts_NeverChangeTheGrossSide`, et confirmé sur données Yahoo réelles au §11).

---

## §9. Compatibilité Lot 14.6 — non-régression

Aucun fichier de `Backtest/Pnl/` ni de `Backtest/Execution/` n'a été modifié. Le seul changement apporté à un
fichier existant est l'ajout, en fin de `BacktestEngine.cs`, de la méthode `RunFullBacktestWithCosts` (plus une
ligne `using IQIAIndicator.Backtest.Cost;`) — aucune ligne des méthodes `Run`/`RunSignalPipeline`/
`RunMeasuredSignalPipeline`/`RunSimulation`/`RunFullBacktest` n'a été touchée.

La preuve de non-régression est directe : `RunFullBacktestWithCosts` **appelle** `RunFullBacktest` (jamais une
réimplémentation) — son champ `FullResult` est donc, par construction du code, identique à ce que produirait un
appel `RunFullBacktest` isolé sur le même scénario. Ceci est vérifié explicitement (hash bit-à-bit identique)
par `BacktestFullResultWithCostsIntegrationTests.Disabled_FullResultMatchesPlainRunFullBacktest_AndNetEquivalentToGross`
et `NonZeroCosts_NeverChangeTheGrossSide`, ainsi que sur données Yahoo réelles (§11).

La suite de tests complète du Lot 14.6 (`Tests/Backtest/Pnl/*`) n'a reçu aucune modification et a été
ré-exécutée dans le cadre de la suite complète du repository (§11).

---

## §10. Configuration — comportement par défaut = Lot 14.6

```csharp
ExecutionCostConfiguration.Disabled()
// Enabled = false, Slippage/Spread/Commission/Fees tous à zéro
```

Avec cette configuration (ou toute configuration avec `Enabled = true` mais chaque composant à zéro — les deux
chemins sont testés indépendamment), `RunFullBacktestWithCosts` produit un `CostResult` dont chaque `NetPnL`,
`FinalNetPnL`, `MaximumNetDrawdown`, `FinalNetEquity` est numériquement identique à son homologue `Gross*` du
`FullResult` — reproduisant exactement le comportement Lot 14.6, comme l'exige le brief §9.

---

## §11. Résultats Yahoo (MES=F, M5, 45 jours)

Le test a été exécuté à trois reprises indépendantes le 2026-08-23 (une fois isolément, une fois dans la suite
complète du repository), aux côtés d'un nouvel appel du test **original, non modifié**, du Lot 14.6
(`PnLYahooIntegrationTests`). Les quatre exécutions, plus le chiffre publié dans le rapport Lot 14.6
(2026-08-22), donnent :

| Source | Date/heure | Bars | GrossPnL | MaximumDrawdown | FinalEquity | ClosedCount |
|---|---|---|---|---|---|---|
| Rapport Lot 14.6 (référence figée) | 2026-08-22 | 8795 | +920.00 | -3545.00 | 100920.00 | 1905 |
| `CostYahooIntegrationTests` (run isolé) | 2026-08-23 ~05:44 | 8699 | +846.25 | -3545.00 | 100846.25 | 1922 |
| `PnLYahooIntegrationTests` — **Lot 14.6, intouché** | 2026-08-23 ~05:54 | 8697 | +943.75 | -3551.25 | 100943.75 | 1927 |
| `CostYahooIntegrationTests` (suite complète) | 2026-08-23 ~06:04 | 8695 | +1002.50 | -3545.00 | 101002.50 | 1915 |

**Constat et explication** : le test Yahoo (celui du Lot 14.6 comme celui du Lot 14.7) interroge une fenêtre
**glissante** de 45 jours ancrée sur `DateTime.UtcNow` — jamais une plage historique figée. Chaque exécution,
même à quelques minutes d'intervalle, capture un nombre de barres légèrement différent (8795 → 8699 → 8697 →
8695), ce qui fait cascader des régimes détectés, des signaux, des positions et un P&L différents à chaque
appel. **Ce n'est pas une régression de code introduite par le Lot 14.7** : le test `PnLYahooIntegrationTests`
du Lot 14.6, strictement inchangé depuis sa création, produit LUI-MÊME un troisième jeu de chiffres différent
(+943.75 / 1927 / 8697) lorsqu'il est ré-exécuté aujourd'hui — la preuve directe que cette variabilité est une
caractéristique déjà présente, inhérente à la conception « fenêtre glissante sur données réseau réelles »
héritée du Lot 14.3, et non quelque chose que ce lot introduit.

**Ce qui EST garanti et vérifié, indépendamment de la dérive de la fenêtre Yahoo, sur CHACUNE des exécutions
ci-dessus** : avec `costs disabled`, `CostResult.FinalNetPnL == FullResult.PnLResult.FinalGrossPnL`,
`MaximumNetDrawdown == MaximumDrawdown`, `FinalNetEquity == FinalEquity`, `TotalCost == 0` — la propriété exigée
par le brief §9/§13. Et surtout : `FullResult.PnLResult.DeterministicHash` reste **identique**, sur les mêmes
données, que l'appel passe par `RunFullBacktestWithCosts` (coûts activés ou non) ou par `RunFullBacktest`
directement — la preuve que le Lot 14.7 ne modifie jamais le calcul Lot 14.6 sous-jacent, quel que soit le jour
d'exécution ou la configuration de coût.

### Scénario avec coûts non nuls (dernière exécution, suite complète, 2026-08-23 ~06:04, 8695 bars)

Configuration illustrative : slippage = 1 tick (0.25 pt), spread = 1 tick (0.25 pt), commission = $0.85/ordre,
fees = $0.10/ordre.

| Métrique | Valeur |
|---|---|
| GrossPnL (inchangé vs baseline) | +1002.50 USD |
| TotalCommission | 3255.50 USD |
| TotalFees | 383.00 USD |
| TotalSpreadCost | 2393.75 USD |
| TotalSlippageCost | 4787.50 USD |
| **TotalCost** | **10819.75 USD** |
| **NetPnL** | **-9817.25 USD** |
| FinalNetEquity | 90182.75 USD |
| MaximumNetDrawdown | -10254.35 USD |

Le hash déterministe du côté Gross (`FullResult.PnLResult.DeterministicHash`) est **identique** entre le run
baseline et le run avec coûts — confirmation directe, sur données réelles, que le Lot 14.7 ne modifie jamais le
calcul Lot 14.6 sous-jacent, quels que soient les coûts appliqués. Avec 1915 positions fermées, ~3830 ordres
(entrée+sortie), un coût moyen de l'ordre de $2.82/ordre (commission+fees+spread+slippage combinés) suffit à
inverser un résultat brut légèrement positif en résultat net négatif — illustration numérique du rôle du Lot
14.7 : rendre visible l'écart entre un résultat théorique et un résultat réaliste, sans jamais prétendre calibrer
ce coût sur un broker réel (hors scope, §6). Le même effet a été observé de façon cohérente sur les deux autres
exécutions du 2026-08-23 (voir sortie brute des tests).

---

## §12. Validation finale (build et suite complète)

| Vérification | Résultat |
|---|---|
| Build DEBUG (`IQIAIndicator.csproj`) | 0 erreur, 0 avertissement |
| Build DEBUG (`Tests/IQIAIndicator.Tests.csproj`) | 0 erreur, 0 avertissement |
| Build RELEASE (`IQIAIndicator.csproj`) | 0 erreur, 0 avertissement |
| Build RELEASE (`Tests/IQIAIndicator.Tests.csproj`) | 0 erreur, 0 avertissement |
| Suite complète (Debug) | 642 tests, 641 réussis, 1 ignoré (pré-existant), **0 échec** |
| Fichiers protégés (`Backtest/Pnl/*`, `Backtest/Execution/*`, Lot 14.1-14.5) | Intacts — 0 modification (vérifié par `git diff`) |
| `BacktestEngine.cs` | 1 seule modification : ajout d'une ligne `using` + une méthode en fin de classe, aucune ligne existante touchée (vérifié par `git diff`) |

Les tests ont été exécutés contre le build **Debug** (pratique standard) ; le build **Release** a été vérifié
comme compilant proprement (0/0) mais n'a pas été ré-exécuté séparément à travers toute la suite réseau — la
logique C# ne dépend pas de la configuration de compilation, seule la suite Debug fait foi pour les résultats
numériques.

---

## §13. Décision finale

Le Lot 14.7 est livré comme fondation additive, déterministe et testée. Le comportement du Lot 14.6 est
préservé bit-à-bit (même méthode `RunFullBacktest` appelée sans modification ; hash Gross identique avec ou
sans coûts actifs). La configuration par défaut (`ExecutionCostConfiguration.Disabled()`) reproduit exactement
le comportement Lot 14.6. Tous les tests unitaires, d'intégration et réseau livrés pour ce lot sont au vert.

**STATUS : IMPLEMENTED.**

**Prochain lot suggéré** : Lot 14.8 (portée à définir — hors scope de ce document ; candidats naturels selon
l'ordre déjà fixé : calibration empirique du slippage/spread à partir de données Bid/Ask réelles, ou début
d'intégration Risk Engine ↔ Backtest).
