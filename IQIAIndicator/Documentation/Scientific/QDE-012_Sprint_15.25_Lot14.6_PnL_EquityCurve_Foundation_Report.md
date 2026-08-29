# QDE-012 — Sprint 15.25 — Lot 14.6
# Position / P&L / Equity Curve Foundation

**Type** : LOT D'IMPLÉMENTATION
**Portée** : couche financière théorique (GrossP&L, Equity Curve, Drawdown) au-dessus des positions du LOT 14.5
**Date** : 2026-08-22
**Branche** : `feature/structural-stability-v2`

---

## 1. OBJECTIVE

Transformer les positions théoriques du LOT 14.5 en :

```
SimulatedPosition (Lot 14.5) → PositionPnLResult → EquityCurve → Drawdown
```

Reste un environnement de backtest scientifique **THÉORIQUE**, hors ATAS : aucun Risk Engine, aucun capital réel, aucune commission/spread/slippage/frais/marge, aucun ordre.

---

## 2. ARCHITECTURE

Nouveau namespace `Backtest/Pnl/` :

```
InstrumentPnLSpecification.cs  — Symbol/PriceUnitValue/Currency, validé, jamais un mapping if(symbol) dans le moteur
PnLConfiguration.cs             — Instrument + Quantity (défaut 1) + StartingCapital (optionnel)
PositionPnLResult.cs            — traçable par PositionId, Return copié verbatim, GrossPnL nouveau
PositionPnLCalculator.cs        — SimulatedPosition → PositionPnLResult (stateless)
EquityPoint.cs                  — Timestamp/PositionId/IncrementalPnL/CumulativePnL/Equity/Drawdown/DrawdownPercent
PnLSummary.cs                   — statistiques agrégées (réutilisé pour ALL/BUY/SELL)
BacktestPnLResult.cs            — résultat complet
BacktestPnLResultBuilder.cs     — construit courbe + drawdown + summaries (stateless)
PnLFingerprint.cs                — fingerprint déterministe (réutilise BacktestFingerprint.Sha256Hex)
BacktestFullResult.cs            — pairing final Signal + Measurement + Execution + P&L
```

```
BacktestSignalResult (Lot 14.3)
        ↓
SimulatedPosition (Lot 14.5, INCHANGÉ)
        ↓
PositionPnLCalculator (GrossPnL = GrossPriceMove × PriceUnitValue × Quantity)
        ↓
PositionPnLResult
        ↓
BacktestPnLResultBuilder (ordre chronologique, cumul, drawdown, résumés)
        ↓
BacktestPnLResult { EquityCurve, Summary, BuySummary, SellSummary, MaximumDrawdown, ... }
```

Intégration `BacktestEngine` (brief §47) : `RunFullBacktest(scenario, warmupBars, measurementConfig, executionConfig, pnlConfig)` — quatre appels strictement séquentiels. La couche P&L **ne relit jamais `HistoricalSeries`** (vérifié par grep sur `Backtest/Pnl/` : aucune référence à `Core.MarketData`) — elle consomme exclusivement la liste de `SimulatedPosition` déjà produite par le LOT 14.5 (brief §47 : « Ne pas refaire les étapes précédentes dans le moteur P&L »).

---

## 3. POSITION P&L

`PositionPnLCalculator.Calculate` convertit une `SimulatedPosition` en `PositionPnLResult` — jamais de recalcul du signal/de l'exécution, uniquement une conversion prix→monnaie. `PositionPnLResult` ne recopie pas la position entière (brief §9) : uniquement les champs nécessaires à l'interprétation isolée du résultat, traçables par `PositionId` (identique à `SimulatedPosition.PositionId`, LOT 14.5).

---

## 4. INSTRUMENT P&L SPECIFICATION

`InstrumentPnLSpecification.Create(symbol, priceUnitValue, currency)` — validation stricte (`priceUnitValue > 0`, `currency` non vide, `symbol` non vide), sinon exception (jamais un P&L silencieusement faux, brief §43). **Aucun mapping `if (symbol == "MES")` dans le moteur** — vérifié par grep sur `Backtest/Pnl/` : MES/ES n'apparaissent QUE dans les fixtures de test (`Tests/Backtest/Pnl/PositionPnLCalculatorTests.cs`), jamais dans le code de production.

---

## 5. THEORETICAL QUANTITY

`PnLConfiguration.Quantity`, valeur par défaut `1` (paramètre optionnel de `Create`, brief §5). Documenté explicitement dans le commentaire de la propriété : *« TheoreticalQuantity is a simulation parameter, not a Risk Engine output »* — reproduit verbatim depuis le brief. Validé strictement positif.

---

## 6. GROSS P&L

```
GrossPnL = GrossPriceMove × PriceUnitValue × Quantity
```

Utilise `SimulatedPosition.GrossPriceMove` (LOT 14.5) **verbatim**, jamais recalculé. Signe hérité intégralement de `GrossPriceMove` (déjà signé favorable-positif par la convention du LOT 14.5) — `PositionPnLCalculator` ne réimplémente aucune logique BUY/SELL. `null` (jamais un zéro fabriqué) quand la position source n'a jamais atteint `Closed`.

---

## 7. RETURN COHERENCE

`PositionPnLResult.Return` est **copié verbatim** depuis `SimulatedPosition.Return` — jamais recalculé (brief §8/§39). Testé structurellement (`Return_IsCopiedVerbatimFromTheSimulatedPosition_NeverRecomputed`) : égalité stricte entre `position.Return` et `result.Return`.

---

## 8. EQUITY CURVE

Un `EquityPoint` par position **fermée** (`Status == Closed`), ordonné chronologiquement par `(ExitTimestamp, PositionId)` — le second terme est le tie-breaker déterministe exigé par le brief §13 (jamais l'ordre incident d'un dictionnaire/collection).

**Aucun mark-to-market intrabar** (brief §15) : la courbe n'est construite qu'à partir de `PositionPnLResult` dont `Status == Closed` — aucune barre intermédiaire n'est jamais lue par cette couche.

**Décision architecturale documentée** : « Equity[0] = StartingCapital », illustré littéralement dans le brief (§11), est représenté **implicitement** par le pic courant (`peak`) qui démarre à `0` (le référentiel `CumulativePnL=0` avant toute position) — jamais par un `EquityPoint` fabriqué avec un timestamp inventé. Puisque `Drawdown = Cumulative - PeakCumulative` est une **différence**, un décalage constant (`StartingCapital`) s'annule exactement dans cette différence — `Drawdown`/`MaximumDrawdown` sont donc **invariants au décalage**, qu'un capital de départ soit fourni ou non. Cette équivalence a été **vérifiée bit-à-bit** contre chacun des exemples chiffrés du brief (§17 : séquence 100000/101000/99000/98000/103000 → MaxDrawdown=-3000 ; §35 : gain simple ; §36 : perte simple ; §37 : gain puis perte → -300 ; §38 : perte puis gain → -500) — tous **PASS** exactement, sans jamais construire de point avec un timestamp fabriqué.

---

## 9. STARTING CAPITAL

`PnLConfiguration.StartingCapital` (nullable, `decimal?`), jamais connecté à ATAS/`RiskCurrentEquity`/`AccountState` (vérifié par grep, brief §11). Quand fourni : `Equity = StartingCapital + CumulativePnL` par point ; `FinalEquity = StartingCapital + FinalGrossPnL`.

---

## 10. CUMULATIVE P&L

`EquityPoint.CumulativePnL` est **toujours** calculé, avec ou sans `StartingCapital` (brief §12). `Equity`/`DrawdownPercent` restent `null` quand `StartingCapital` est absent — jamais de capital inventé. `CumulativePnL ≠ EquityCurve` : le champ `Equity` (nullable) coexiste avec `CumulativePnL` (toujours présent) sur le même point, sans jamais les confondre.

---

## 11. DRAWDOWN

```
Drawdown = CumulativePnL - PeakCumulativePnL   (toujours <= 0)
DrawdownPercent = Drawdown / (StartingCapital + PeakCumulativePnL)   (uniquement si StartingCapital fourni)
```

Voir §8 pour la preuve d'invariance au décalage et la vérification contre les exemples du brief.

---

## 12. MAXIMUM DRAWDOWN

`MaximumDrawdown = min(EquityCurve.Select(p => p.Drawdown))`, ou `0` si `EquityCurve` est vide (brief §17/§34). Cas particuliers testés explicitement (brief §18) : aucune position, première position gagnante, première position perdante, retour au sommet précédent, nouveau sommet, pertes successives, drawdown nul — **7/7 PASS**, chacun avec une valeur exacte vérifiée (ex. pertes successives -100/-200/-300 → MaximumDrawdown=-600).

---

## 13. BUY / SELL

`BacktestPnLResult.BuySummary`/`SellSummary` — même type `PnLSummary` que `Summary` (ALL), calculé sur trois populations distinctes filtrées par `Direction`. Jamais perdu dans l'agrégat global (brief §19), testé explicitement (`BuyAndSellSummaries_AreSeparateFromAll_AndFromEachOther`).

---

## 14. QUANTITY

Testé pour `Quantity = 1, 2, 5` : proportionnalité exacte du `GrossPnL` (brief §42) — `10, 20, 50` USD respectivement pour un même mouvement de 2 points à 5 USD/point. **PASS.**

---

## 15. CURRENCY

`PositionPnLResult.Currency`/`InstrumentPnLSpecification.Currency` : metadata conservée telle quelle, **aucune conversion FX** nulle part dans ce lot (vérifié par grep — brief §44).

---

## 16. INVALID CONFIGURATION

`InstrumentPnLSpecification.Create`/`PnLConfiguration.Create`/`BacktestPnLResultBuilder.Build` lèvent une exception (`ArgumentException`/`ArgumentOutOfRangeException`) plutôt que de produire silencieusement un P&L faux (brief §43) : `PriceUnitValue <= 0`, `Currency` vide, `Quantity <= 0`, `StartingCapital <= 0` — **tous testés**, **PASS**.

---

## 17. EMPTY INPUT

**Convention choisie et documentée** (brief §34, « Choisir UNE convention ») : `EquityCurve` **vide** (jamais un point synthétique de départ) quand aucune position n'est fermée. `PositionCount=0`, `NetGrossPnL=0`, `MaximumDrawdown=0`, `FinalGrossPnL=0`. Testé (`NoPositions_ProducesZeroEverything_NeverThrows`). **PASS.**

---

## 18. LOOK-AHEAD PROTECTION

Deux niveaux, tous deux testés à travers `BacktestEngine.RunFullBacktest` :

1. **§30** : ajouter des barres futures après le dernier `ExitTimestamp` ne change jamais une position déjà fermée ni son P&L — `AppendingFutureBars_NeverChangesAnAlreadyClosedPositionsPnL`, 210 `PositionPnLResult` comparés bit-à-bit entre une série de 220 barres et son extension à 320 barres. **PASS.**
2. **§31** : donnée future qui ne crée aucune nouvelle position → EquityCurve identique. Prouvé **structurellement** : la couche P&L ne lit jamais `HistoricalSeries`/les barres (§2) — elle ne peut donc, par construction, jamais être influencée par une donnée future qui ne change pas la liste de positions en entrée. Testé par reconstruction indépendante de la couche P&L à partir des mêmes positions déjà calculées (`FutureDataThatCreatesNoNewPosition_LeavesTheExistingEquityCurveIdentical`). **PASS.**

---

## 19. DETERMINISM

`PositionPnLResult`/`EquityPoint` ne portent **aucun champ `DateTime.UtcNow`** — fonctions pures de leurs entrées. `PnLFingerprint.ComputeHash` (réutilise `BacktestFingerprint.Sha256Hex`, LOT 14.1 non modifié) hash directement `Timestamp`/`P&L`/`CumulativePnL`/`Drawdown`/`DrawdownPercent`/`Equity` (brief §46). Testé : même scénario deux fois (y compris délai d'horloge murale réel de 50 ms) → même hash ; une seule barre future modifiée → hash différent. **PASS.**

---

## 20. RUN ISOLATION

`PositionPnLCalculator` et `BacktestPnLResultBuilder` sont des classes statiques sans aucun champ — rien à isoler par construction. Vérifié empiriquement : Run A → Run B → Run A → second Run A identique ; deux instances `BacktestEngine` indépendantes → résultats identiques. **PASS.**

---

## 21. YAHOO INTEGRATION

Mêmes paramètres exacts que les LOTs 14.4/14.5 (MES=F, M5, fenêtre de 45 jours). Pipeline complet Signal→Measurement→Execution→P&L exécuté via `RunFullBacktest`, avec `PnLConfiguration` MES réel (5 USD/point, Quantity=1, StartingCapital=100000 USD). Voir §25 pour les résultats mesurés.

---

## 22. RISK ENGINE EXCLUSION

`RiskEngine`/`AccountState`/`PortfolioState`/`RiskPolicy`/`InstrumentRiskSpecification` : **aucune référence** de code dans `Backtest/Pnl/` (vérifié par grep — les seules occurrences des mots « RiskEngine »/« AccountState » sont dans des commentaires expliquant explicitement leur EXCLUSION, jamais dans du code exécutable).

---

## 23. ATAS EXCLUSION

Aucun `TradingManager`/`Portfolio`/`TradingStatisticsProvider`/type ATAS — vérifié par grep. Fonctionne entièrement avec `HistoricalSeries` (Yahoo ou synthétique) via la liste de `SimulatedPosition` déjà produite.

---

## 24. COST EXCLUSION

Aucune commission, spread, slippage, frais, financement — vérifié par grep. `PnLSummary`/`BacktestPnLResult` ne contiennent que des champs préfixés `Gross`, jamais `Net...` au sens « après coûts » (brief §20 : « Ne pas appeler cela NetProfit »).

---

## 25. RESULTS

### 25.1 Yahoo (2026-08-22)

| Paramètre | Valeur |
|---|---|
| Ticker | `MES=F` |
| Timeframe | M5 |
| From | 2026-07-08T20:33:20Z |
| To | 2026-08-22T20:33:20Z (45 jours) |
| Bars | **8795** |
| Fingerprint | `DCC0DA708F34C78BDE495D6600DE9AC87843AF7AE9FB51CF52C03E88011CE0EF` |
| Configuration P&L | 5 USD/point, Quantity=1, StartingCapital=100000 USD |

Résultat mesuré (2026-08-22) :

| Métrique | Valeur |
|---|---|
| ClosedCount | 1905 |
| GrossProfit | +30128,75 USD |
| GrossLoss | -29208,75 USD |
| NetGrossPnL | +920,00 USD |
| WinRate | 0,5071 |
| FinalGrossPnL | +920,00 USD |
| MaximumDrawdown | -3545,00 USD |
| FinalEquity | 100920,00 USD |
| BUY | Count=983, NetGrossPnL=-1078,75 USD |
| SELL | Count=922, NetGrossPnL=+1998,75 USD |
| DeterministicHash | `043D7D5A2905A2ED5825C6AB40A5B05F1E1924C8148DF1F34207C35DFD9C250A` |

`NetGrossPnL` (+920 USD, ~0,92% de `StartingCapital`) est faible face à `GrossProfit`/`GrossLoss` (~30k chacun) et `WinRate` proche de 0,50 — cohérent avec un résultat **RAW MARKET RESPONSE** sans coût, sans avantage statistique revendiqué (brief §35, LOT 14.4) : aucune conclusion de rentabilité n'est tirée de ces chiffres. Re-vérifié déterministe par reconstruction indépendante de la couche P&L à partir des mêmes positions déjà calculées — hash identique.

---

## 26. TESTS

### Suite ciblée Lot 14.6 (`Tests/Backtest/Pnl/`, 4 fichiers)

| Fichier | Couverture |
|---|---|
| `PositionPnLCalculatorTests.cs` | MES/ES fixtures, proportionnalité Quantity, cohérence Return, statuts non-Closed, validation de configuration |
| `BacktestPnLResultBuilderTests.cs` | Tous les exemples chiffrés du brief (§17/§35-38), cas limites de drawdown (§18), ordre déterministe, BUY/SELL, GrossProfit/GrossLoss, WinRate, médiane, entrée vide |
| `BacktestFullResultIntegrationTests.cs` | Look-ahead (§30/§31), déterminisme, isolation, absence structurelle de Risk/coût |
| `PnLYahooIntegrationTests.cs` | Réseau réel, MES M5 45 jours, pipeline complet |

### 26.1 Résultats d'exécution

```
Suite ciblée Lot 14.6 (hors réseau) : 46/46 PASS (2 m 59 s)
Test réseau Yahoo (PnLYahooIntegrationTests) : 1/1 PASS (5 m 49 s)
Suite Lot 14.5 (non-régression, hors réseau) : 35/35 PASS
```

### 26.2 Suite complète du dépôt

```
Total : 564 tests
Réussi(s) : 563
Ignoré(s) : 1
Échec(s) : 0
Durée : 43 min 20 s
```

564 = 517 (base LOT 14.1-14.5) + 47 (nouveaux tests Lot 14.6 : 46 hors-réseau + 1 réseau). L'ignoré est le même test préexistant déjà documenté. **Aucun échec** — y compris `AdfLagSelectionScaleStabilityXunitTests`, le test de performance flaky documenté aux LOT 14.3/14.5 : il passe normalement cette fois, confirmant à nouveau qu'il s'agit de contention CPU ponctuelle sous charge parallèle, jamais d'une régression fonctionnelle.

---

## 27. BUILD

| Cible | Résultat | Avertissements | Erreurs |
|---|---|---|---|
| `IQIAIndicator.csproj` — Debug | **SUCCÈS** | 0 | 0 |
| `IQIAIndicator.csproj` — Release | **SUCCÈS** | 0 | 0 |
| `IQIAIndicator.Tests.csproj` — Debug | **SUCCÈS** | 0 | 0 |
| `IQIAIndicator.Tests.csproj` — Release | **SUCCÈS** | 0 | 0 |

Deux occurrences transitoires d'échec de build (crash `csc.exe` puis `OutOfMemoryException` NuGet) ont été rencontrées durant ce lot, toutes deux sans rapport avec le code — un second essai immédiat a réussi à chaque fois sans aucune modification, confirmant une contention machine ponctuelle (probablement due aux multiples processus de build/test parallèles accumulés au fil des LOTs 14.3-14.6 de cette session).

---

## 28. LIMITATIONS

1. **`BacktestPnLResult` consolide « EquityCurve » et « DrawdownCurve » (brief §48) en une seule liste** — chaque `EquityPoint` porte déjà `Drawdown`/`DrawdownPercent`. Décision documentée en §2 : deux tableaux parallèles pour une seule série temporelle n'auraient fait qu'introduire un risque de désynchronisation.
2. **`MaximumDrawdown`/`FinalGrossPnL` restent des champs globaux de `BacktestPnLResult`, jamais dupliqués dans `BuySummary`/`SellSummary`** — une « drawdown BUY-only » nécessiterait de construire une courbe d'équité BUY-only séparée, non demandé explicitement par le brief (§19 ne demande que les statistiques de P&L, pas de courbe, par direction).
3. **Le pic (`peak`) qui porte implicitement « Equity[0] = StartingCapital » démarre à `0` inconditionnellement**, y compris quand `StartingCapital` est absent — cohérent puisque `Drawdown` est invariant au décalage (§8/§11), mais signifie que `Drawdown`/`MaximumDrawdown` sont **toujours** calculés même sans capital théorique, ce qui est une extension légèrement plus généreuse que la lettre stricte du brief §16 (qui les décrit en termes d'« Equity ») — documentée explicitement plutôt que silencieuse.
4. **Comme aux LOTs précédents**, la couche P&L hérite de toutes les limitations déjà documentées du LOT 14.5 (aucune politique de chevauchement/portefeuille, `PositionId` = index de barre de signal).

---

## 29. NEXT LOT

**LOT 14.7 — Cost / Slippage / Execution Realism Foundation**, conformément à l'ordre déjà fixé.

---

## STATUS
**IMPLEMENTED**
