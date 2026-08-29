# QDE-012 — Sprint 15.25 (Lot 10) — Risk Engine : architecture et noyau de calcul

Ce lot implémente le Risk Engine d'IQIA : un module autonome, déterministe et testable qui transforme
un trade candidat (direction, entrée, stop, cible) en une décision de risque structurée (ACCEPTED /
REJECTED + raisons), en tenant compte du capital, de l'equity, du budget de risque, du drawdown, des
limites de compte et de la spécification de l'instrument. **Ce lot ne modifie ni ne calibre aucune
valeur de risque de production** — il construit uniquement le moteur.

---

## 1. Audit initial

Recherche exhaustive (grep sur tout `IQIAIndicator/`, hors `bin/obj`) avant toute modification :

- **Aucune classe Capital/Equity/Balance/Drawdown/RiskBudget/RiskPolicy/Portfolio/Account/OpenRisk/
  OpenPosition n'existait nulle part** dans le dépôt (code de production comme tests). Confirmé par
  zéro occurrence de ces mots-clés.
- **Aucun `RiskEngine.cs`/`RiskModel.cs`/`RiskAssessment.cs`** n'existait. Seul
  [QDE-011_Risk_Engine_Theory.md](QDE-011_Risk_Engine_Theory.md) documente la conception théorique
  (aucun code).
- **`TradePlan`** ([TradePlan.cs](../../Engine/TradePlan/TradePlan.cs)) porte déjà les champs
  `StopLoss`, `TakeProfit`, `RiskPerUnit`, `RiskAmount`, `PositionSize`, `RiskRewardRatio`, avec un
  statut `NO_TRADE/SIGNAL_ONLY/PLAN_READY/PLAN_BLOCKED`.
- **`TradePlanBuilder`** ([TradePlanBuilder.cs](../../Engine/TradePlan/TradePlanBuilder.cs)) calcule
  déjà, de façon testée (8 tests A-H dans `TradePlanBuilderTests.cs`), `RiskPerUnit = |Entry-SL| x
  PointValue`, `PositionSize = floor(RiskPerTrade/RiskPerUnit)`, `RiskAmount`, `RiskRewardRatio`, à
  partir de `TradeRiskParameters(StopLoss, RiskPerTrade)` — le point d'extension déjà prévu pour un
  futur Risk Engine, toujours `null` en production
  ([IQIAIndicator.cs:409](../../IQIAIndicator.cs)).
- **`InstrumentInfo`** ([InstrumentInfo.cs](../../Core/InstrumentInfo.cs)) est le seul modèle
  instrument (`Symbol, TickSize, TickValue, PointValue, Decimals`) — pas de `MinQuantity/MaxQuantity/
  QuantityStep/ContractMultiplier`, pas de table ES/MES.
- **Recherche SL/TP** : aucun calcul de SL/TP n'existe en production (`StopLoss` n'est qu'un
  `decimal?` transporté depuis `TradeRiskParameters`, jamais sourcé). Une recherche offline de
  méthodologie SL existe dans `Tests/Research/StopLossCalibration/` (candidats A1/A2/D1/D2), documentée
  dans [QDE-012_StopLoss_Calibration_Protocol.md](QDE-012_StopLoss_Calibration_Protocol.md) — **aucun k
  n'a été retenu pour la production** (voir `QDE-012_Sprint_15.14_A1_K_Calibration_Report.md`, §21).
  Architecturalement isolée, sans dépendance vers `TradePlan`/`Decision`.
- **Convention de nommage confirmée** : enums `XxxStatus`/`XxxReason` en SCREAMING_SNAKE_CASE
  (`TradePlanStatus`, `EntryTriggerReason`) + `Diagnostics`/`Warnings` en accompagnement — suivie pour
  ce lot.
- **Seuil `AmbiguityScore >= 0.95`** confirmé dans
  [EntryTriggerBuilder.cs:19](../../Engine/EntryTrigger/EntryTriggerBuilder.cs)
  (`AmbiguityGateThreshold`) — hors périmètre, non touché.

Décision d'architecture qui en découle : le Risk Engine est construit comme un **module entièrement
autonome** (`Engine/Risk/`), produisant une nouvelle abstraction `RiskAssessment` plutôt qu'une
surcharge de `TradePlan` (conformément à la Section 12 du lot, qui laisse ce choix ouvert et le
recommande). `TradePlan.cs`, `TradePlanBuilder.cs`, `TradePlanContext.cs`, `EntryTrigger*.cs`,
`DecisionArbitrator.cs` et `IQIAIndicator.cs` ne sont **pas touchés**.

---

## 2. Architecture existante (rappel)

Pipeline : `Evidence → Fusion → Decision (AmbiguityScore) → Methodology → Signal → Entry →
EntryTrigger (Direction, gate 0.95) → TradePlan (Entry/SL/TP/Size/RR, toujours SIGNAL_ONLY faute de
Risk Engine) → Presentation`. Le Risk Engine se positionne, architecturalement, entre `EntryTrigger` et
`TradePlan` — mais ce lot ne câble pas cette intégration dans `IQIAIndicator.cs` (voir §15 Limites).

---

## 3. Architecture créée

Nouveau dossier `IQIAIndicator/Engine/Risk/`, namespace `IQIAIndicator.Engine.Risk`, aucune dépendance
ATAS :

```
Capital / Account State  (AccountState)
        ↓
Risk Policy               (RiskPolicy)
        ↓
Instrument Risk Spec       (InstrumentRiskSpecification, étend Core.InstrumentInfo)
        ↓
RiskEngine.Evaluate(RiskEngineRequest)
        ├─ Phase 1-3 : validation Capital / Equity / Instrument / Entry
        ├─ Phase 4-5 : Stop Loss → RiskDistance → RiskPerUnit
        ├─ Phase 6   : Take Profit → RewardDistance
        ├─ Phase 7   : contraintes Drawdown / Daily Loss / Open Risk (indépendantes)
        ├─ Phase 8   : Risk Budget (min des plafonds applicables)
        ├─ Phase 9   : Position Sizing (floor, QuantityStep, Min/Max)
        ├─ Phase 10  : Risk/Reward + MinRiskReward
        └─ Phase 11  : garde-fou de cohérence
        ↓
RiskAssessment (Status ACCEPTED/REJECTED + RejectionReasons structurées)
```

Le moteur est pur : une seule méthode `Evaluate`, aucun état mutable, aucun accès horloge/réseau/ATAS.

---

## 4. Modèles ajoutés

| Fichier | Rôle |
|---|---|
| [TradeDirection.cs](../../Engine/Risk/TradeDirection.cs) | `enum { Buy, Sell }` — indépendant de `DirectionCandidate` (qui porte aussi WATCH/NO_ACTION, sans objet pour un calcul de risque) |
| [AccountState.cs](../../Engine/Risk/AccountState.cs) | `InitialCapital, CurrentEquity, CurrentBalance?, PeakEquity, DailyStartingEquity, DailyPnL, RiskUsedToday, OpenRisk` + `CurrentDrawdown`/`CurrentDrawdownPercent` calculés |
| [RiskPolicy.cs](../../Engine/Risk/RiskPolicy.cs) | Tous les plafonds de la Section 4, tous nullable/opt-in, aucune règle PropFirm codée en dur |
| [InstrumentRiskSpecification.cs](../../Engine/Risk/InstrumentRiskSpecification.cs) | Étend `InstrumentInfo` (factory `FromInstrumentInfo`) avec `MinQuantity/MaxQuantity/QuantityStep/ContractMultiplier` + `IsValid` |
| [PortfolioState.cs](../../Engine/Risk/PortfolioState.cs) | `OpenPosition` + `PortfolioState` — interfaces Section 13 uniquement, non consommées par `Evaluate` (voir §15) |
| [IStopLossStrategy.cs](../../Engine/Risk/IStopLossStrategy.cs) | Interface d'extension future (ATR/structure/volatility) + `ProvidedStopLossStrategy` (pass-through, seule implémentation) |
| [RiskRejectionReason.cs](../../Engine/Risk/RiskRejectionReason.cs) | Les 14 raisons structurées de la Section 11 |
| [RiskDecisionStatus.cs](../../Engine/Risk/RiskDecisionStatus.cs) | `enum { ACCEPTED, REJECTED }` |
| [RiskEngineRequest.cs](../../Engine/Risk/RiskEngineRequest.cs) | Entrée de `Evaluate` |
| [RiskAssessment.cs](../../Engine/Risk/RiskAssessment.cs) | Sortie structurée de `Evaluate` |
| [RiskEngine.cs](../../Engine/Risk/RiskEngine.cs) | Le moteur lui-même |

---

## 5. Formules implémentées

- `RiskDistance` = `Entry - SL` (BUY) / `SL - Entry` (SELL) — rejet `INVALID_STOP_LOSS` si `<= 0` ou SL absent.
- `RiskPerUnit` = `RiskDistance x PointValue`.
- `RewardDistance` = `TP - Entry` (BUY) / `Entry - TP` (SELL) — rejet `INVALID_TAKE_PROFIT` si fourni mais `<= 0`.
- `RiskRewardRatio` = `RewardDistance / RiskDistance` (distances de prix, cohérent avec le calcul déjà
  existant dans `TradePlanBuilder`).
- `RiskBudget` = `min(Equity x MaxRiskPerTradePercent, MaxRiskPerTradeAmount, DailyLossRemaining,
  OpenRiskRemaining)`, forcé à `0` si le drawdown maximum est atteint.
- `PositionSize` = `floor(RiskBudget / RiskPerUnit)`, arrondi ensuite au multiple de `QuantityStep`
  inférieur, puis borné à `[MinQuantity, MaxQuantity]` et `MaxPositionSize` (arrondi **toujours vers le
  bas**, jamais vers le haut) — un budget produisant 2.8 contrats donne 2, jamais 3 (testé explicitement).
- `RiskAmount` = `RiskPerUnit x PositionSize`. `RewardAmount` = `RewardDistance x PointValue x
  PositionSize`.

---

## 6. Règles de sécurité (Section 8)

Un trade est REJECTED si : capital/equity invalide, spécification instrument invalide, entrée invalide,
stop-loss invalide/absent, drawdown maximum atteint, perte journalière maximale atteinte, risque ouvert
déjà trop élevé, budget de risque indéfini ou nul, taille de position résultante nulle ou sous
`MinQuantity`, take-profit invalide, R:R insuffisant. Chaque violation produit un
`RiskRejectionReason` structuré (jamais de string libre) ; **plusieurs raisons peuvent être rapportées
simultanément** (testé — voir TEST 32). Une garde de cohérence explicite empêche tout `RiskAssessment`
`ACCEPTED` sans `PositionSize` résolu.

---

## 7. Gestion du capital

`AccountState` porte l'état brut (jamais de valeur par défaut inventée) ; `CurrentDrawdown` et
`CurrentDrawdownPercent` sont calculés en propriétés pures. `DailyLossRemaining` et
`AvailableRiskBudget` (Section 3) dépendent aussi de `RiskPolicy` — ils sont donc produits comme
résultat du calcul de `RiskBudget` dans `RiskEngine.Evaluate` plutôt que stockés sur `AccountState`
(choix documenté dans le code).

---

## 8. Gestion ES/MES

`InstrumentRiskSpecification` ne code aucune valeur par symbole — chaque instrument est configuré
explicitement par l'appelant. Testé avec des fixtures ES (`PointValue=50`, 5 points de stop →
`RiskPerUnit=250`) et MES (`PointValue=5`, mêmes 5 points → `RiskPerUnit=25`) : les deux résultats sont
distincts et vérifiés `!=` l'un de l'autre (TEST 27-28), prouvant l'absence de logique partagée
hardcodée.

---

## 9. Position sizing

Floor déterministe → arrondi au `QuantityStep` inférieur → clamp `[MinQuantity, MaxQuantity]` /
`MaxPositionSize`. Distinction entre deux échecs de sizing : `POSITION_SIZE_INVALID` (le budget ne
couvre même pas un `QuantityStep`, arrondit à 0) et `QUANTITY_LIMIT` (le budget couvrirait une taille
non nulle mais inférieure à `MinQuantity` — impossible de respecter le minimum sans dépasser le budget).
Un dépassement de `MaxQuantity`/`MaxPositionSize` est en revanche un **clamp silencieux, non un rejet**
(toujours plus sûr, jamais plus risqué) — testé (TEST 22).

---

## 10. Risk/Reward

Calculé uniquement quand `RiskDistance` et `RewardDistance` sont tous deux valides et positifs, jamais
de division par zéro. `MinRiskReward` est optionnel (jamais inventé) : s'il est configuré et qu'aucun
TakeProfit valide n'est fourni pour le vérifier, le trade est rejeté `INVALID_TAKE_PROFIT` (impossible
de prouver la conformité) ; s'il est configuré et que le R:R calculé est inférieur, `INVALID_RISK_REWARD`.

---

## 11. Tests ajoutés

[Tests/Risk/RiskEngineTests.cs](../../Tests/Risk/RiskEngineTests.cs) (35 scénarios, numérotés
TEST 01-35 en correspondance directe avec la liste de la Section 15 du lot) +
[Tests/XunitWrappers/RiskEngineXunitTests.cs](../../Tests/XunitWrappers/RiskEngineXunitTests.cs)
(wrapper `[Fact]` suivant exactement le motif `TradePlanBuilderXunitTests`). Couverture : Capital (5),
Risk budget (5), BUY (4), SELL (4), Position sizing (5), Risk/Reward (3), Instrument (5), Safety (4) =
35/35. Toutes les valeurs ES/MES sont des littéraux de fixture explicites (`EsSpec()`/`MesSpec()`),
jamais des constantes internes au moteur.

---

## 12. Résultats build

| Commande | Résultat |
|---|---|
| `dotnet build IQIAIndicator/IQIAIndicator.csproj -c Debug` | **Réussi** — 0 avertissement, 0 erreur |
| `dotnet build IQIAIndicator/IQIAIndicator.csproj -c Release` | **Réussi** — 0 avertissement, 0 erreur |
| `dotnet build IQIAIndicator/Tests/IQIAIndicator.Tests.csproj -c Debug` | **Réussi** — 0 avertissement, 0 erreur |

---

## 13. Résultats tests

`dotnet test IQIAIndicator/Tests/IQIAIndicator.Tests.csproj -c Debug` — suite complète (178 fichiers de
test xUnit, incluant les campagnes de calibration offline) :

- **RiskEngineXunitTests.RunAll** (35 assertions internes) : **PASS**.
- Un premier passage complet avait rapporté 1 échec isolé,
  `AdfLagSelectionScaleStabilityXunitTests` (test de performance ADF pré-existant, sans lien avec ce
  lot — jamais touché). Cause identifiée : ce test mesure un temps d'exécution en ms/call et a été
  exécuté pendant qu'un `dotnet build -c Release` tournait en parallèle dans ce même environnement,
  provoquant une contention CPU. Reproduit isolément immédiatement après (succès, 4s), confirmant qu'il
  ne s'agissait pas d'une régression.
- **Second passage complet, sans charge concurrente : 0 échec, 177 réussite(s), 1 ignoré(e), 178 au
  total, durée 9 min 22 s.** Le test ignoré (`Sprint1515HistoricalReconciliationXunitTests.
  VerifySprint1514Unaffected`) l'est de façon pré-existante, indépendamment de ce lot.
- Aucun test existant n'a été modifié.
- Deux exécutions de la suite ont régénéré des fichiers de sortie de campagne pré-existants
  (`Tests/Research/StopLossCalibration/Output/*.csv`/`*.txt`) comme effet de bord normal de ces tests de
  recherche (non liés à ce lot) — restaurés via `git checkout --` après chaque run pour garder le diff
  strictement scopé au Risk Engine.

---

## 14. Fichiers modifiés / créés

**Créés (nouveaux, aucun fichier existant modifié) :**

```
IQIAIndicator/Engine/Risk/TradeDirection.cs
IQIAIndicator/Engine/Risk/AccountState.cs
IQIAIndicator/Engine/Risk/RiskPolicy.cs
IQIAIndicator/Engine/Risk/InstrumentRiskSpecification.cs
IQIAIndicator/Engine/Risk/PortfolioState.cs
IQIAIndicator/Engine/Risk/IStopLossStrategy.cs
IQIAIndicator/Engine/Risk/RiskRejectionReason.cs
IQIAIndicator/Engine/Risk/RiskDecisionStatus.cs
IQIAIndicator/Engine/Risk/RiskEngineRequest.cs
IQIAIndicator/Engine/Risk/RiskAssessment.cs
IQIAIndicator/Engine/Risk/RiskEngine.cs
IQIAIndicator/Tests/Risk/RiskEngineTests.cs
IQIAIndicator/Tests/XunitWrappers/RiskEngineXunitTests.cs
IQIAIndicator/Documentation/Scientific/QDE-012_Sprint_15.25_Lot10_RiskEngine_Report.md
```

**Aucun fichier pré-existant n'a été modifié par ce lot** (`TradePlan.cs`, `TradePlanBuilder.cs`,
`TradePlanContext.cs`, `EntryTriggerBuilder.cs`, `DecisionArbitrator.cs`, `IQIAIndicator.cs` : intacts).
Les modifications déjà présentes sur ces fichiers avant le début de ce lot (visibles dans `git status`)
sont antérieures à ce travail et n'ont pas été touchées.

---

## 15. Limites restantes

- **Aucune intégration câblée dans `IQIAIndicator.cs`** : le Risk Engine est prêt architecturalement
  (le point d'intégration naturel reste `TradeRiskParameters`, entre `EntryTrigger` et `TradePlan`),
  mais le câbler en production exigerait d'ajouter de nouveaux paramètres UI (capital, equity, règles
  de policy) qui ne sont documentés nulle part aujourd'hui — la Section 3/4 du lot interdit explicitement
  d'inventer ces valeurs. L'intégration reste donc au niveau architectural, pas au niveau exécution ATAS.
- **`PortfolioState`/`OpenPosition`** existent comme modèles (Section 13) mais ne sont pas consommés par
  `RiskEngine.Evaluate` — `AccountState.OpenRisk` reste la seule source utilisée pour le calcul cette
  saison, conformément à l'interdiction d'implémenter un portfolio manager complet.
  `PortfolioState.OpenRisk/DailyRiskUsed/CurrentEquity` sont donc actuellement informationnels/no-op.
- **`IStopLossStrategy`** n'a qu'une implémentation triviale (`ProvidedStopLossStrategy`, pass-through) —
  aucune stratégie ATR/structure/volatility, conformément à l'interdiction d'inventer une formule de
  production.
- **`RiskAssessment` n'est pas fusionné dans `TradePlan`** — c'est un choix architectural délibéré
  (Section 12), pas une limitation technique ; un futur lot d'intégration devra décider comment les deux
  cohabitent (probablement en alimentant `TradeRiskParameters` à partir d'un `RiskAssessment` accepté).

---

## 16. Prochaine étape recommandée

Un lot d'intégration séparé pourrait : (a) définir les paramètres UI ATAS nécessaires pour peupler
`AccountState`/`RiskPolicy` réels (capital, règles de compte documentées), (b) câbler
`RiskEngine.Evaluate` entre `EntryTrigger` et `TradePlanEngine.Process` dans `IQIAIndicator.cs`, en
convertissant un `RiskAssessment` `ACCEPTED` en `TradeRiskParameters(StopLoss, RiskPerTrade)`. **Ce lot
ne recommande aucune valeur de risque, de capital ou de policy** — cela relève de la calibration/backtest,
explicitement hors périmètre ici.
