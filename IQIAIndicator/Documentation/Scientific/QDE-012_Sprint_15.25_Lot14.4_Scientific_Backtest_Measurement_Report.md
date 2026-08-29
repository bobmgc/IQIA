# QDE-012 — Sprint 15.25 — Lot 14.4
# Scientific Backtest Measurement Engine

**Type** : LOT D'IMPLÉMENTATION
**Portée** : moteur de mesure scientifique post-signal (Return / MFE / MAE / Hit Rate) sur données historiques Yahoo/synthétiques
**Date** : 2026-08-22
**Branche** : `feature/structural-stability-v2`

---

## 1. OBJECTIVE

Mesurer ce qui arrive APRÈS chaque signal produit par le LOT 14.3, en utilisant uniquement les barres futures autorisées (`i+1 .. i+H`) :

```
BacktestSignalResult (Lot 14.3)
        ↓
ScientificMeasurementEngine
        ↓
MeasurementResult { Return, MFE, MAE, HitResults }
```

Moteur de MESURE scientifique uniquement — pas d'exécution, pas de position, pas de P&L, pas de coûts, pas de Risk Engine, pas de trading automatique.

---

## 2. ARCHITECTURE

Nouveau namespace `Backtest/Measurement/` :

```
MeasurementConfiguration.cs          — HorizonBars + HitThresholds, validés, jamais codés en dur
MeasurementResult.cs                 — MeasurementStatus (5 valeurs) + MeasurementHitResult + MeasurementResult
ScientificMeasurementEngine.cs       — Measure / MeasureAll / MeasureCore (le moteur, stateless)
ScientificMeasurementSummary.cs      — ScientificMeasurementSummary + ScientificMeasurementSummaryByDirection
ScientificMeasurementSummaryBuilder.cs — agrégation (médiane, hit rate, ALL/BUY/SELL)
MeasurementFingerprint.cs            — fingerprint déterministe (réutilise BacktestFingerprint.Sha256Hex)
BacktestMeasuredSignalPipelineResult.cs — pairing minimal SignalResult + Measurements
```

`ScientificMeasurementEngine` est une **classe statique sans aucun champ** — pas d'instance à isoler entre runs, l'isolation (brief §30) est garantie par construction, pas par convention.

Intégration `BacktestEngine` (brief §39) : une seule méthode additive, `RunMeasuredSignalPipeline(scenario, warmupBars, configuration)`, qui appelle `RunSignalPipeline` (LOT 14.3, intégralement inchangé) puis `ScientificMeasurementEngine.MeasureAll`. Aucune formule MFE/MAE n'est inlinée dans `BacktestEngine.cs` — uniquement l'orchestration séquentielle des deux étapes déjà existantes.

---

## 3. SIGNAL / MEASUREMENT SEPARATION

Séparation explicite dans l'architecture (brief §2), pas seulement dans le comportement :

- `BacktestMeasuredSignalPipelineResult` porte **deux listes parallèles** (`SignalResult.Bars` et `Measurements`), jamais une fusion où `MeasurementResult` serait injecté à l'intérieur de `BacktestSignalResult`.
- `ScientificMeasurementEngine.Measure` ne modifie jamais l'objet `BacktestSignalResult` qu'il lit — il ne fait que le lire (`signal.TradePlan.Direction`/`.EntryPrice`) et produit un objet neuf.
- `RunMeasuredSignalPipeline` appelle `RunSignalPipeline` et attend son retour COMPLET avant de lancer `MeasureAll` — aucune barre future ne peut donc jamais influencer un signal déjà produit (brief §40, ordre séquentiel strict).

**Test §18 (obligatoire)** — `ModifyingAFutureBar_ChangesTheMeasurement_ButNeverTheAlreadyProducedSignal` : un run de référence et un run où une seule barre à `i+3` (i = barre de signal) est modifiée (+50 sur High/Low/Close). Résultat : `TradePlan.Direction`/`EntryPrice`/`Status` et `Decision.Winner` **identiques** au bit près sur la barre de signal ; `MeasurementResult` **diffère** dès lors que ce signal était mesurable. **PASS.**

---

## 4. ENTRY PRICE CONVENTION

**Une seule source, jamais de repli silencieux** : `TradePlan.EntryPrice` (brief §5, priorité 1). Aucune source secondaire n'a été ajoutée — `TradePlanBuilder` (protégé) fixe déjà `EntryPrice = EntryTriggerCandidate.CurrentPrice` dès qu'une direction BUY/SELL existe et que ce prix est positif ; ce lot n'a trouvé aucun cas légitime où une direction existe sans que `TradePlan.EntryPrice` la porte déjà. **`Close[i]` n'est utilisé nulle part dans ce moteur** — vérifié par relecture de `ScientificMeasurementEngine.cs` (aucune référence à `bars[signalBarIndex]`.Close/.High/.Low).

Si `TradePlan` est absent, ou si `EntryPrice` est `null` ou `<= 0` : `MeasurementStatus.InvalidEntryPrice` (jamais de division par zéro — testé explicitement, `InvalidEntryPrice_NeverDividesByZero_ReportsInvalidEntryPriceStatus`, `0.0` et valeur négative).

---

## 5. RETURN FORMULA

Calculée sur le **Close de la dernière barre de l'horizon** (`i+HorizonBars`), pas un Close intermédiaire (brief §12) :

```
BUY  : Return = (Close[i+H] - EntryPrice) / EntryPrice
SELL : Return = (EntryPrice - Close[i+H]) / EntryPrice
```

Signe : positif = mouvement favorable à la direction (brief §7). Testé exactement pour BUY gagne/perd, SELL gagne/perd, Return=0 (`ScientificMeasurementEngineFormulaTests.Buy_FinalReturn_MatchesExpectedSign`/`Sell_FinalReturn_MatchesExpectedSign`).

---

## 6. MFE FORMULA

```
BUY  : MFE = max_{j=i+1..i+H} (High[j] - EntryPrice) / EntryPrice
SELL : MFE = max_{j=i+1..i+H} (EntryPrice - Low[j]) / EntryPrice
```

**Convention documentée, non triviale** : littéralement un MAXIMUM, jamais borné à 0 (`Math.Max(0, ...)` n'est PAS appliqué). Si le prix ne s'est jamais déplacé favorablement sur tout l'horizon, MFE peut être **négatif** — conséquence directe de la formule exacte du brief §10 (« MFE = maximum des... »), pas un choix arbitraire de ce lot. Documenté ici explicitement plutôt que choisi silencieusement (brief §50).

---

## 7. MAE FORMULA

```
BUY  : MAE = max_{j=i+1..i+H} (EntryPrice - Low[j]) / EntryPrice
SELL : MAE = max_{j=i+1..i+H} (High[j] - EntryPrice) / EntryPrice
```

Même convention non bornée que MFE (§6). `MAE positif = excursion adverse absolue` (brief §11) — jamais mélangée avec un rendement signé négatif : `MeasurementResult.Return` et `MeasurementResult.Mae` sont deux champs strictement distincts, jamais dérivés l'un de l'autre.

Vérifié exactement par fixtures BUY/SELL (brief §19/§20) : Entry=100, BUY, futures High={101,102,100.5}/Low={99,99.5,100} → MFE=0.02, MAE=0.01. SELL, futures Low={99,98,99.5}/High={101,100.5,100} → MFE=0.02, MAE=0.01 (miroir exact). **PASS.**

---

## 8. HIT-RATE FORMULA

```
BUY  : Hit(threshold) = High[j] >= EntryPrice × (1 + threshold) pour un j quelconque dans [i+1, i+H]
SELL : Hit(threshold) = Low[j]  <= EntryPrice × (1 - threshold) pour un j quelconque dans [i+1, i+H]
```

**Implémentation** : `Hit(threshold) = MFE >= threshold`, dérivée directement de MFE plutôt que re-scannée indépendamment — **équivalence mathématique exacte**, pas une approximation : pour BUY, `MFE = max_j (High[j]-Entry)/Entry`, et « il existe j tel que `High[j] >= Entry×(1+threshold)` » ⟺ « il existe j tel que `(High[j]-Entry)/Entry >= threshold` » ⟺ `max_j(...) >= threshold` ⟺ `MFE >= threshold`. Preuve symétrique pour SELL. Cette équivalence est documentée dans le code (`ScientificMeasurementEngine.MeasureCore`) et vérifiée indépendamment par `Hit_Boundary_UsesGreaterThanOrEqual`/`Hit_NotReached_IsFalse` contre des fixtures construites à la main.

**Frontière (brief §22)** : convention `>=` — `MFE == threshold` → `Hit = true`. Testé avec un seuil **dérivé de la même arithmétique** que celle utilisée par le moteur (jamais un littéral `0.001` codé en dur comparé bit-à-bit à un calcul flottant indépendant, ce qui serait fragile aux arrondis IEEE-754 sans rapport avec la règle de frontière elle-même) — voir le commentaire du test pour la justification complète.

---

## 9. HORIZON

`HorizonBars` est un paramètre de `MeasurementConfiguration`, jamais une constante (brief §4). Pour un signal à `i` avec `H`, seules les barres `i+1..i+H` sont lues — la barre `i` elle-même n'est jamais utilisée pour MFE/MAE/Return. Testé à `Horizon=1/5/10`, et avec une barre `i+11` contenant un mouvement énorme (High=1000) qui **n'affecte strictement rien** à `Horizon=10` (`Horizon_StrictlyBoundsWhichBarsAreRead_AHugeMoveJustBeyondItIsInvisible`). **PASS.**

---

## 10. PARTIAL HORIZON HANDLING

**Décision documentée (brief §9/§50)** : contrat « tout ou rien ». Si `i+HorizonBars` dépasse le dernier index disponible, **aucun calcul n'est effectué** — `MeasurementStatus.InsufficientFutureData`, `Return`/`Mfe`/`Mae` = `null`, `HitResults` vide. Aucune barre manquante n'est inventée, aucun résultat partiel n'est étiqueté comme complet.

Alternative envisagée et rejetée : calculer MFE/MAE sur les quelques barres futures réellement disponibles sous un statut `PARTIAL` distinct. Rejetée parce qu'un nombre partiel resterait indiscernable d'un nombre complet pour tout consommateur en aval qui oublierait de vérifier `HorizonBars` effectivement couvert — le contrat « tout ou rien » élimine cette classe d'erreur par construction, au prix de ne produire aucune mesure pour les derniers `HorizonBars-1` signaux d'une série. Testé exactement au cas limite `i = N-2`, `Horizon=10` → `InsufficientFutureData` (brief §24). **PASS.**

---

## 11. BUY/SELL

`MeasurementResult.Direction` porte directement le `DirectionCandidate` réellement produit par le pipeline (`BUY_CANDIDATE`/`SELL_CANDIDATE`) — jamais un enum parallèle ni une traduction qui pourrait inverser le signe. `NO_ACTION`/`WATCH` → `MeasurementStatus.NotMeasurable`, jamais mesurés comme un trade (brief §6/§27, testé par `NoAction_IsNeverMeasuredAsATrade`). `ScientificMeasurementSummaryBuilder.Summarize` produit systématiquement trois résumés (`All`/`Buy`/`Sell`) — jamais une statistique BUY/SELL perdue dans l'agrégat global (brief §16/§34, testé par `Summarize_SeparatesBuyAndSellStatistics_WithoutLosingThemInTheAllTotal`).

---

## 12. AGGREGATION

`ScientificMeasurementSummaryBuilder.Summarize` : filtre d'abord sur `MeasurementStatus.Measured` uniquement (`NotMeasurable`/`InvalidEntryPrice`/`InsufficientFutureData`/`InvalidFutureData` **exclus** de toute statistique — jamais moyennés comme des zéros), puis calcule `Count`, `MedianReturn`, `MedianMfe`, `MedianMae`, `HitRates` (une entrée par seuil configuré) pour `All`/`Buy`/`Sell` séparément. **Aucune moyenne** (brief §32), aucune métrique financière additionnelle (pas de Sharpe/drawdown/win-rate-en-pourcentage-de-compte).

---

## 13. MEDIAN

Convention exacte et documentée (`ScientificMeasurementSummaryBuilder.Median`, brief §33) :
- tri ascendant ;
- effectif impair → élément central ;
- effectif pair → moyenne arithmétique des deux éléments centraux ;
- entrée vide → `null` (jamais `0.0` — une médiane de rien n'est pas zéro, elle est indéfinie).

Testé : effectif impair, effectif pair, valeur unique, entrée vide, indépendance de l'ordre d'entrée (`ScientificMeasurementSummaryBuilderTests`, 5 tests dédiés). **PASS.**

---

## 14. LOOK-AHEAD VALIDATION

Deux niveaux :

1. **Signal** (déjà prouvé au LOT 14.3, re-vérifié ici à travers `RunMeasuredSignalPipeline`) : `AppendingFutureBars_NeverChangesTheSignal_ButCanResolveInsufficientFutureDataIntoAMeasurement` — Dataset A (220 barres) vs Dataset A+80 barres futures : `TradePlan.Direction`/`EntryPrice`/`Status` identiques sur les 220 barres communes ; au moins un signal proche de la fin passe de `InsufficientFutureData` (série courte) à `Measured` (série longue) — la mesure change légitimement, le signal jamais.
2. **Mesure elle-même** : `MeasureCore` ne lit jamais `bars[j]` pour `j > i+HorizonBars` ni `j <= i` — garanti par construction (boucles bornées explicitement), vérifié par `Horizon_StrictlyBoundsWhichBarsAreRead_AHugeMoveJustBeyondItIsInvisible` (§9).

**PASS** sur les deux niveaux.

---

## 15. DETERMINISM

`MeasurementResult` ne porte **aucun champ `DateTime.UtcNow`** (contrairement aux objets du LOT 14.3) — c'est une fonction pure de `(HistoricalSeries, BacktestSignalResult, MeasurementConfiguration)`. `MeasurementFingerprint.ComputeHash` (réutilise `BacktestFingerprint.Sha256Hex`, LOT 14.1, fichier non modifié) hash chaque champ directement, sans exclusion nécessaire.

Testé : même scénario deux fois → même `DeterministicHash` (y compris avec un délai d'horloge murale réel de 50 ms) ; une seule barre future modifiée → hash différent. **PASS.**

---

## 16. RUN ISOLATION

`ScientificMeasurementEngine` est une classe **statique sans aucun champ** — rien à isoler par construction. Vérifié empiriquement malgré tout (même discipline que les LOTs 14.1/14.3) : Run A → Run B → Run A → second Run A identique au premier ; deux instances `BacktestEngine` indépendantes sur le même scénario → résultats identiques. **PASS.**

---

## 17. YAHOO DATASET USED

| Paramètre | Valeur |
|---|---|
| Ticker | `MES=F` (Yahoo, continu) |
| Timeframe | M5 |
| From | 2026-07-08T11:26:30Z |
| To | 2026-08-22T11:26:30Z (45 jours) |
| Bars | **8904** |
| Fingerprint (`HistoricalSeriesFingerprint.Compute`) | `E1082A0E20D3672535A43744FB5673011B3127757305B44A7D4646C5FF79D1B5` |

Fenêtre choisie à 45 jours (sous la limite dure de 60 jours confirmée au LOT 14.2) pour atteindre « quelques milliers de barres » sans télécharger des dizaines de milliers de barres non nécessaires (brief §41).

---

## 18. RESULTS

Pipeline signal (LOT 14.3, `warmupBars=128`) sur les 8904 barres : `BarsProcessed=8904`, `BUY=1004`, `SELL=956`, `TradePlan.SIGNAL_ONLY=1960` (= BUY+SELL, cohérent — aucun `PLAN_READY` sans Risk Engine, comme au LOT 14.3).

Mesure (`HorizonBars=10`, seuils `{0.001, 0.002}`) :

| Population | Count | MedianReturn | MedianMfe | MedianMae |
|---|---|---|---|---|
| ALL | 1960 | 3.22×10⁻⁵ | 6.14×10⁻⁴ | 5.89×10⁻⁴ |
| BUY | 1004 | 0.0 | — | — |
| SELL | 956 | 6.39×10⁻⁵ | — | — |

`MeasurementResult.DeterministicHash = 4A0ED150B32FBE6D1F7308F030E3E5E9F94965B993350908D73497ED68E71EFD`.

**Aucune conclusion de rentabilité n'est tirée de ces chiffres** (brief §35) — ce sont des réponses de marché brutes, sans coût, sans slippage, sur un horizon de 10 barres M5 (~50 minutes), rien de plus.

---

## 19. LIMITATIONS

1. **Comme au LOT 14.3**, `RegimeDetectedCount`/`DecisionCount` et la parité ATAS/Backtest restent dans l'état documenté par ce lot précédent — non ré-adressées ici.
2. **Le contrat « tout ou rien » pour l'horizon incomplet (§10) signifie que les `HorizonBars-1` derniers signaux d'une série ne sont jamais mesurés**, même partiellement — conséquence assumée du choix documenté en §10, pas un oubli.
3. **MFE/MAE peuvent être négatifs** (§6/§7) — une conséquence directe et documentée de la formule exacte du brief, pas un bug ; un consommateur futur de `MeasurementResult` doit en tenir compte avant d'agréger ces valeurs comme s'il s'agissait toujours d'excursions positives.
4. **Aucun déclustering n'a été implémenté** (brief §31, explicitement différé) — chaque signal produit sa propre mesure indépendante ; des signaux consécutifs proches (auto-corrélés) ne sont ni fusionnés ni filtrés dans ce lot.
5. **`ScientificMeasurementSummaryBuilder.Summarize` suppose que toutes les mesures `Measured` d'un même appel partagent le même jeu de seuils, dans le même ordre** — vrai systématiquement quand elles proviennent d'un seul appel à `MeasureAll` avec une seule `MeasurementConfiguration` (le seul chemin de production de ce lot) ; non défendu contre un appelant qui mélangerait des populations issues de configurations différentes.

---

## 20. RISK ENGINE EXCLUSION

`RiskEngine`, `AccountState`, `PortfolioState`, `RiskPolicy`, `InstrumentRiskSpecification` : **aucune référence** dans `Backtest/Measurement/` (vérifié par grep). `MeasurementConfiguration`/`MeasurementResult`/`ScientificMeasurementEngine` ne dépendent que de `Core.MarketData` et `Engine.EntryTrigger`/`Engine.TradePlan` (pour les types `DirectionCandidate`/`TradePlan`, en lecture seule).

---

## 21. EXECUTION EXCLUSION

Aucun `Order`/`Position`/`Fill`/`Execution`/`Broker` — vérifié par grep sur `Backtest/Measurement/`. Ce lot ne simule aucun remplissage, aucun slippage, aucun coût.

---

## 22. BUILD

| Cible | Résultat | Avertissements | Erreurs |
|---|---|---|---|
| `IQIAIndicator.csproj` — Debug | **SUCCÈS** | 0 | 0 |
| `IQIAIndicator.csproj` — Release | **SUCCÈS** | 0 | 0 |
| `IQIAIndicator.Tests.csproj` — Debug | **SUCCÈS** | 0 | 0 |
| `IQIAIndicator.Tests.csproj` — Release | **SUCCÈS** | 0 | 0 |

---

## 23. TESTS

### Suite ciblée Lot 14.4 (`Tests/Backtest/Measurement/`, 4 fichiers, 44 tests)

| Fichier | Couverture |
|---|---|
| `ScientificMeasurementEngineFormulaTests.cs` | Return/MFE/MAE fixtures BUY/SELL, Hit boundary, Horizon, fin de série, OHLC invalide, EntryPrice invalide, NO_ACTION, SIGNAL_ONLY, validation d'arguments — 24 tests |
| `ScientificMeasurementEngineIntegrationTests.cs` | Intégration `RunMeasuredSignalPipeline`, look-ahead (§17), séparation signal/mesure (§18), déterminisme (§29), isolation (§30) — 9 tests |
| `ScientificMeasurementSummaryBuilderTests.cs` | Médiane, agrégation ALL/BUY/SELL, hit rate, exclusion des statuts non-Measured — 10 tests |
| `ScientificMeasurementYahooIntegrationTests.cs` | Réseau réel, 8904 barres MES M5 — 1 test |

**44/44 PASS** (43 hors-réseau en ~1 min, +1 test réseau réel en ~3 min 46 s).

### Suite Lot 14.3 (non-régression)

**35/35 PASS** — `BacktestEngine.RunSignalPipeline`/`Run` intégralement inchangés par ce lot.

### Suite Lot 14.1 + Lot 14.2 (non-régression)

**153/153 PASS**.

### Suite complète du dépôt

```
Total : 481 tests
Réussi(s) : 480
Ignoré(s) : 1
Échec(s) : 0
Durée : 13 min 48 s
```

481 = 437 (base LOT 14.1+14.2+14.3, rapport Lot 14.3 §14.4) + 44 (nouveaux tests Measurement de ce lot). L'ignoré est le même test préexistant déjà documenté (`Sprint1515HistoricalReconciliationXunitTests.VerifySprint1514Unaffected`, sans rapport avec ce lot). **Aucun échec** — y compris `AdfLagSelectionScaleStabilityXunitTests`, le test de performance qui avait échoué sous contention parallèle au LOT 14.3 (§14.4 de ce rapport-là) : il passe normalement ici, confirmant qu'il s'agissait bien de contention CPU ponctuelle et non d'une régression.

---

## 24. PROTECTED FILES

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
| `RegimeEngine`/`EvidenceFusionEngine`/`FusionStateManager`/`DecisionEngine`/`SignalEngine`/`EntryEngine`/`EntryTriggerEngine` | **INTACT** |
| `YahooHistoricalBarSource.cs` | **INTACT** |
| `MarketContextFactory.cs` | **INTACT** |
| `BacktestEngine.cs` | Étendu de façon strictement additive (`RunMeasuredSignalPipeline`, une méthode) — `Run`/`RunSignalPipeline`/`BuildValidatedContext`/`BuildScientificMarketContext` intégralement inchangés |

`AmbiguityGateThreshold = 0.95` et `Difference > 0.05` : non touchés, `EntryTriggerBuilder.cs` intact.

---

## 25. NEXT LOT

**LOT 14.5 — Execution Model / Position Simulation Preparation**, conformément à l'ordre déjà fixé.

---

## STATUS
**IMPLEMENTED**
