# QDE-012 — Sprint 15.25 — Lot 14.8 — Risk Constraints / Position Sizing / Backtest Risk Foundation

**Date** : 2026-08-23
**Branche** : `feature/structural-stability-v2`
**Lots précédents** : Lots 14.1 → 14.7 (VALIDÉS, TESTÉS, PROTÉGÉS)
**Statut** : IMPLEMENTED

---

## §1. Objectif

Le Lot 14.8 introduit une première fondation Risk Engine intégrable au Backtest : permettre au backtest de
déterminer, de manière déterministe, explicite, testable et reproductible, **si une position est autorisée et
quelle quantité peut être prise**, compte tenu de contraintes de risque configurées.

```
Signal → Risk Evaluation → Position Sizing → Execution → Costs → PnL → Equity → Drawdown
```

Ce n'est **pas** un moteur de portfolio risk complet (pas de VaR, corrélation, Kelly, Monte Carlo, multi-
compte — voir §6). C'est la fondation minimale répondant à : *« Étant donné mon equity actuelle, mon risque
maximal par trade, mon instrument, mon stop et mes contraintes d'exposition, quelle quantité puis-je
réellement prendre ? »*

---

## §2. Audit préalable (état observé avant implémentation)

L'audit a révélé un fait déterminant : **un `RiskEngine` complet, déterministe et sans dépendance ATAS existe
déjà** (`IQIAIndicator/Engine/Risk/RiskEngine.cs`, Sprint 15.25 Lot 10), utilisé aujourd'hui uniquement par le
chemin ATAS live (`IQIAIndicator.cs`), jamais par le backtest.

- **`RiskEngine.Evaluate(RiskEngineRequest) -> RiskAssessment`** implémente déjà exactement le calcul demandé
  aux §5/§6/§7 du présent brief : budget de risque (`CurrentEquity × MaxRiskPerTradePercent`, plafonné par
  `MaxRiskPerTradeAmount`/pertes journalières/risque ouvert), `RiskPerUnit = RiskDistance × PointValue`
  (direction-aware), sizing `floor(budget / RiskPerUnit)` arrondi au `QuantityStep`, puis clampé par
  `[MinQuantity, MaxQuantity]` et `Policy.MaxPositionSize`. Types associés déjà présents et réutilisables tels
  quels : `RiskPolicy`, `AccountState`, `InstrumentRiskSpecification` (déjà porté par
  `BacktestScenario.Instrument`/`.Policy` depuis le Lot 14.1), `RiskAssessment`, `RiskRejectionReason`.
- **Vérification d'absence de couplage ATAS** : recherche exhaustive de `ATAS`/`Ecng`/`OFT.` dans
  `Engine/Risk/*` et `Engine/TradePlan/*` — zéro référence de type, seulement des commentaires de
  documentation. Ces types sont directement appelables depuis un backtest déterministe, sans adaptateur.
- **Constat critique** : dans le pipeline backtest actuel, `BacktestEngine.RunSignalPipeline` construit
  `TradePlanContext` **sans jamais fournir `RiskParameters`** — `TradePlan.StopLoss` est donc **toujours
  null** dans le backtest (`TradePlanBuilder.Build`, Phase 4 : `stopLoss = context.RiskParameters?.StopLoss`).
  Aucun stop n'est disponible aujourd'hui côté backtest. Conformément au §8 du brief (« ne pas inventer un
  stop »), le Lot 14.8 introduit une **distance de risque explicitement configurée par l'appelant**
  (`RiskDistanceConfiguration`, même pattern que `SlippageConfiguration`/`SpreadConfiguration` du Lot 14.7),
  jamais dérivée du `TradePlan` existant.
- **Exposure** : recherche exhaustive de `Exposure`/`MaxExposure` dans tout le repository — **zéro
  occurrence** dans le code ; le concept n'existe que dans un document de conception (`QDE-011_Risk_Engine_
  Theory.md`), jamais implémenté. Terrain entièrement nouveau pour ce lot.
- **`Backtest/Risk/`** n'existait pas avant ce lot (confirmé par listing complet de `Backtest/`).
- **`Strategy/`/`Position/`** : aucun dossier de ce nom n'existe nulle part dans le repository.
- **Conventions de tests** : `Tests/Risk/`/`Tests/TradePlan/` (namespace `Tests.RiskTests`/`Tests.TradePlanTests`)
  suivent un style ancien (`RunAll()` + wrapper xUnit séparé, Sprint 15.8/Lot 10) ; les dossiers `Tests/Backtest/*`
  actuels (Pnl/Cost) utilisent xUnit direct (`[Fact]`/`[Theory]`). Le nouveau `Tests/Backtest/Risk/` suit ce
  second style, cohérent avec le reste du Lot 14.x.

**Stratégie d'intégration retenue** :
- Nouveau dossier `Backtest/Risk/` (namespace `IQIAIndicator.Backtest.Risk`), sibling de `Pnl`/`Execution`/`Cost`.
- Réutilisation directe de `RiskEngine.Evaluate` — aucune réimplémentation du calcul de budget/sizing.
- `BacktestRiskConfiguration` n'ajoute **que** ce qui n'existe pas déjà : `EnableRiskControls`,
  `RiskDistance`, `MaxExposure`. `InitialCapital` reste `BacktestScenario.InitialCapital` ; `RiskPerTrade`
  reste `RiskPolicy.MaxRiskPerTradePercent`/`MaxRiskPerTradeAmount` ; `MaxPositionQuantity` reste
  `RiskPolicy.MaxPositionSize`/`InstrumentRiskSpecification.MaxQuantity` — tous déjà portés par
  `BacktestScenario` et déjà appliqués par `RiskEngine.Evaluate`.
- Nouvel enum `PositionRiskReason`, **local à `Backtest.Risk`** — `Engine.Risk.RiskRejectionReason` (Lot 10,
  partagé avec le chemin live) n'est pas modifié.
- Nouvelle méthode `BacktestEngine.RunFullBacktestWithRisk`, sœur de `RunFullBacktestWithCosts` (même
  discipline d'appel direct à `RunSignalPipeline`/`MeasureAll`/`SimulateAll`), aucune méthode existante
  modifiée.

---

## §3. Architecture livrée

```
Backtest
   │
   ├── Signal / Measurement / Execution   (Lots 14.3-14.5, inchangés)
   ├── Pnl / Cost                          (Lots 14.6-14.7, inchangés)
   │
   └── Risk                                (Lot 14.8, NOUVEAU)
          ├── RiskDistanceConfiguration     (le stop/risk distance manquant du pipeline backtest)
          ├── BacktestRiskConfiguration     (EnableRiskControls + RiskDistance + MaxExposure)
          ├── PositionRiskReason            (enum local, jamais Engine.Risk.RiskRejectionReason)
          ├── RiskEvaluationResult          (le "RiskEvaluationResult" du brief §9)
          ├── PositionRiskEvaluator         (Signal -> Risk Evaluation -> Position Sizing, par position)
          ├── PositionRiskOutcome / BacktestRiskResult / BacktestRiskResultBuilder
          ├── RiskResultFingerprint         (hash déterministe, réutilise BacktestFingerprint.Sha256Hex)
          └── BacktestFullResultWithRisk    (résultat de RunFullBacktestWithRisk)
```

`BacktestEngine.RunFullBacktestWithRisk` appelle `RunSignalPipeline`/`ScientificMeasurementEngine.MeasureAll`/
`ExecutionSimulator.SimulateAll` **exactement comme `RunFullBacktest`/`RunFullBacktestWithCosts` le font déjà**
(zéro modification de ces méthodes), puis délègue à `BacktestRiskResultBuilder.Build` — elle n'appelle **pas**
`RunFullBacktest`/`RunFullBacktestWithCosts` eux-mêmes : ces deux méthodes appliquent une quantité **uniforme**
(`PnLConfiguration.Quantity`) à toutes les positions via `PositionPnLCalculator.CalculateAll`/
`PositionCostCalculator.CalculateAll` (méthodes « liste »), alors que le Lot 14.8 a une quantité **qui varie
par position** (déterminée par le risque) — le bon outil est donc les méthodes **unitaires**
`PositionPnLCalculator.Calculate`/`PositionCostCalculator.Calculate` (Lots 14.6/14.7, réutilisées telles
quelles), appelées une fois par position avec une `PnLConfiguration` dont la quantité est substituée.

---

## §4. Configuration

```csharp
public sealed record BacktestRiskConfiguration
{
    public required bool EnableRiskControls { get; init; }
    public required RiskDistanceConfiguration RiskDistance { get; init; }
    public decimal? MaxExposure { get; init; }

    public static BacktestRiskConfiguration Disabled() => new()
    { EnableRiskControls = false, RiskDistance = RiskDistanceConfiguration.None(), MaxExposure = null };
}
```

`Disabled()` (défaut recommandé) reproduit exactement le comportement Lot 14.6/14.7 (§18). `EnableRiskControls`
est un véritable interrupteur, indépendant des sous-valeurs — quand `false`, `PositionRiskEvaluator` court-
circuite entièrement la logique de risque, quelles que soient les valeurs configurées par ailleurs.

`RiskDistanceConfiguration` (mêmes trois constructeurs que `SlippageConfiguration`/`SpreadConfiguration` du
Lot 14.7) : `None()` (0, aucune distance disponible), `Fixed(decimal)`, `FromTicks(ticks, tickSize)`.

---

## §5. Risk per trade

Réutilisé tel quel depuis `RiskEngine.Evaluate` (`Engine/Risk/RiskEngine.cs:158-159`) :

```
MaximumRisk = min(CurrentEquity × MaxRiskPerTradePercent, MaxRiskPerTradeAmount, DailyLossRemaining, OpenRiskRemaining)
```

`CurrentEquity` est **toujours** l'équité courante suivie par la marche séquentielle du Lot 14.8 (§13), jamais
`InitialCapital` de façon systématique. Exemple vérifié par test (`MaximumAllowedRisk_MatchesTheBriefsOwnWorkedExample_OnePercentOfEquity`) :
Capital=100000, RiskPerTrade=1% → MaximumRisk=1000.

---

## §6. Risk per unit / Position sizing

Réutilisé tel quel depuis `RiskEngine.Evaluate` :

```
RiskDistance  = Buy: EntryPrice - StopLoss ; Sell: StopLoss - EntryPrice     (toujours > 0, sinon rejeté)
RiskPerUnit   = RiskDistance × PointValue                                     (instrument-aware, jamais hardcodé)
AllowedByRisk = floor(MaximumRisk / RiskPerUnit), arrondi au QuantityStep, clampé [MinQuantity, MaxQuantity] et MaxPositionSize
```

Exemple du brief §7, vérifié bit-à-bit par test (`RiskPerUnitAndPositionSizing_MatchTheBriefsOwnWorkedExample`) :
Entry=100, Stop=98 (RiskDistance=2), PointValue=5 → RiskPerUnit=10 ; MaximumRisk=100 → AllowedQuantity=10.

`StopLoss` est résolu une seule fois, ici, à partir de `RiskDistanceConfiguration.PriceUnits` et du prix
théorique d'entrée (`SimulatedPosition.EntryPrice`, Lot 14.5, jamais recalculé) :

```
Buy  : StopLoss = TheoreticalEntry - RiskDistance
Sell : StopLoss = TheoreticalEntry + RiskDistance
```

Jamais inventé depuis un ATR ou une distance fixe arbitraire (brief §8) : `RiskDistanceConfiguration` est un
paramètre de simulation explicite, fourni par l'appelant. Une distance non configurée (`None()`, ou
`PriceUnits <= 0`) produit `PositionRiskReason.InvalidRiskDistance` — jamais un stop fabriqué.

---

## §7. Exposure

Entièrement nouveau (confirmé absent partout ailleurs, §2). Calculé sur le prix **exécuté** (Lot 14.7,
`ExecutionPriceModel.Apply`, réutilisé — jamais recalculé), pas le prix théorique, conformément à la formule
du brief :

```
Exposure       = ExecutedEntryPrice × Quantity × ContractMultiplier    (ContractMultiplier null => 1, jamais fabriqué différemment)
AllowedByExposure = floor(MaxExposure / (ExecutedEntryPrice × ContractMultiplier))
```

**Ordre de priorité** (brief §10, respecté et testé) :

```
FinalQuantity = min(RequestedQuantity, AllowedByRisk+Quantity[via RiskEngine], AllowedByExposure)
```

`RiskEngine.Evaluate` combine déjà Risk-limit et Quantity-limit en une seule passe (Phase 9) ; le Lot 14.8
applique Requested puis Exposure comme les deux derniers clamps, dans cet ordre. Les deux exemples chiffrés du
brief §11 sont testés bit-à-bit :

| Requested | Risk allows | Exposure allows | Final |
|---|---|---|---|
| 10 | 6 | 8 | **6** |
| 10 | 10 | 4 | **4** |

---

## §8. Position eligibility — `RiskEvaluationResult`

```csharp
public sealed record RiskEvaluationResult(
    bool IsAllowed, int RequestedQuantity, int AllowedQuantity,
    int? RiskConstrainedQuantity, int? ExposureConstrainedQuantity,
    decimal? RiskPerUnit, decimal? TotalEstimatedRisk, decimal? MaximumAllowedRisk,
    decimal? Exposure, decimal? MaxExposure,
    PositionRiskReason Reason, IReadOnlyList<string> Diagnostics);
```

`PositionRiskReason` (enum local, jamais `Engine.Risk.RiskRejectionReason`) : `Allowed`,
`PositionNotExecutable`, `InvalidConfiguration` (réservé, inatteignable aujourd'hui — validation à la
construction, même précédent que `RiskRejectionReason.UNKNOWN_ERROR`), `InvalidInstrument`,
`InvalidRiskDistance`, `RiskLimitExceeded`, `MaxQuantityExceeded`, `ExposureLimitExceeded`, `ZeroRisk`.
`Engine.Risk.RiskAssessment.RejectionReasons` (qui peut porter plusieurs raisons simultanées) est mappé sur
cette raison primaire unique via une table de priorité documentée dans `PositionRiskEvaluator.MapReason`.

---

## §9. Ordre des calculs et équité dynamique

Ordre implémenté, respecté sans exception (brief §16) :

```
1. Signal (Lot 14.3, inchangé)           6. Execution Costs (Lot 14.7, réutilisé)
2. Requested Position (PnLConfiguration.Quantity) 7. Position PnL (Lot 14.6, réutilisé)
3. Risk Evaluation (RiskEngine.Evaluate) 8. Net PnL (Lot 14.7, réutilisé)
4. Final Quantity                         9. Equity Update
5. Execution Price (Lot 14.7, réutilisé) 10. Next Risk Decision
```

`BacktestRiskResultBuilder` marche séquentiellement sur les positions **Closed**, dans le même ordre
chronologique (ExitTimestamp, puis PositionId) que les courbes d'équité des Lots 14.6/14.7, en enchaînant les
étapes 1-9 pour chaque position avant de passer à la suivante (étape 10 = étape 3 de la position suivante).

**Simplification assumée et documentée** (brief §3 exclut explicitement le portfolio VaR/corrélation/multi-
compte) : les positions sont traitées de façon strictement séquentielle, une par une, dans cet ordre — pas un
modèle de portefeuille avec chevauchement réel. `AccountState.OpenRisk` est donc toujours 0 (le stub
`PortfolioState` du Lot 10 n'est pas câblé). Les champs « journaliers » (`DailyStartingEquity`/`DailyPnL`) ne
sont jamais réinitialisés (dégénèrent en champs « sur toute la durée du run ») — le brief §22 exclut
explicitement une gestion complexe de limite de perte journalière ; un `RiskPolicy.MaxDailyLossPercent/Amount`
configuré agit donc comme une limite au niveau du run entier sous ce lot.

Exemple chiffré du brief §13, vérifié bit-à-bit (`DynamicEquity_SecondTradesMaximumAllowedRisk_ReflectsTheFirstTradesOutcome`) :

```
Trade 1: perte de $2000 (Equity 100000 -> 98000)
Trade 2: MaximumAllowedRisk = 98000 × 1% = 980   (jamais 100000 × 1% = 1000)
```

---

## §10. Intégration au coût/PnL (compatibilité Lot 14.7)

```
RequestedQuantity = 10, RiskQuantity = 6 -> ExecutionQuantity = CostQuantity = PnLQuantity = 6
```

jamais `RiskQuantity=6, CostQuantity=10` — garanti par construction : `BacktestRiskResultBuilder` construit une
`PnLConfiguration` **fraîche par position**, avec `Quantity = riskEval.AllowedQuantity`, avant d'appeler
`PositionPnLCalculator.Calculate`/`PositionCostCalculator.Calculate` (Lots 14.6/14.7, inchangés) — la même
quantité alimente les deux appels, dans le même passage. Vérifié explicitement par
`CostAndPnlQuantity_MatchesTheRiskDeterminedAllowedQuantity_NeverTheRawRequestedQuantity`.

---

## §11. Hypothèses et limites

- Traitement séquentiel, pas de vrai chevauchement de portefeuille (voir §9).
- `InstrumentRiskSpecification.PointValue` et `InstrumentPnLSpecification.PriceUnitValue` (Lot 14.6) sont deux
  champs séparés censés représenter la même valeur économique (ex: MES=5 $/point) ; ce lot ne les réconcilie
  pas automatiquement — à l'appelant de les configurer de façon cohérente (comme c'était déjà implicitement le
  cas pour `Engine.Risk.InstrumentRiskSpecification` vs `Backtest.Pnl.InstrumentPnLSpecification` avant ce lot).
- `pnlConfiguration.StartingCapital` est ignoré par `RunFullBacktestWithRisk` : `scenario.InitialCapital` est
  l'unique source de vérité pour le capital (risque + équité), évitant deux nombres de capital potentiellement
  divergents.
- `ContractMultiplier` absent (`null`) est traité comme 1 (valeur neutre), jamais fabriqué différemment.
- **Hors scope (identique au brief §22)** : VaR, CVaR, portfolio VaR, covariance, corrélation, Kelly, Monte
  Carlo, stress testing, margin engine complet, liquidation, portfolio optimizer, live risk, broker risk, daily
  loss limit complexe, prop-firm rule engine complet, ML risk model.

---

## §12. Tests livrés

Nouveau dossier `IQIAIndicator/Tests/Backtest/Risk/` (namespace `IQIAIndicator.Tests.BacktestTests.Risk`), 6
fichiers, 62 tests (57 unitaires/intégration synthétique + 1 intégration réseau Yahoo, dont plusieurs `[Theory]`) :

| Fichier | Couverture |
|---|---|
| `RiskDistanceConfigurationTests.cs` | None/Fixed/FromTicks, zéro accepté (signifie "pas de stop"), rejet valeurs négatives, tick size invalide |
| `BacktestRiskConfigurationTests.cs` | `Disabled()`, `Create` avec défauts/valeurs explicites, rejet MaxExposure non-positif |
| `PositionRiskEvaluatorTests.cs` | Risk per trade (0/1/2%, exemple du brief), RiskPerUnit/sizing (exemple du brief), risk distance (manquant/zéro/valide), Long=Short, instrument invalide, % négatif géré proprement, quantité=0 (input invalide) vs budget arrondi à 0 (output géré), requested>allowed / allowed>requested, exposure (sous/à/au-dessus de la limite, multiplicateur null=1), les deux exemples combinés du brief, invariants 1/3/4 en `[Theory]`, instruments différents (MES/ES, jamais hardcodé), déterminisme, position non-Closed |
| `BacktestRiskResultBuilderTests.cs` | Équité dynamique (exemple exact du brief §13), rejet sans effet de bord, équivalence Risk+Cost désactivés ↔ Lot 14.6/14.7, isolation d'exécution, déterminisme, cohérence quantité Cost/PnL, positions non-Closed exclues du suivi d'équité |
| `BacktestFullResultWithRiskIntegrationTests.cs` | Déterminisme et isolation via `RunFullBacktestWithRisk`, équivalence exacte avec `RunFullBacktestWithCosts` (Risk+Cost désactivés), Signal/Execution jamais modifiés par l'activation du risque, invariants 1/2/3/4 sur un run réel, invariant 5 (rejet => aucun effet de bord) |
| `RiskYahooIntegrationTests.cs` | MES=F/M5/45 jours réel via `RunFullBacktestWithRisk` — invariants uniquement (jamais de valeur PnL figée, brief §19), cohérence Requested→Risk→Execution→Cost→PnL |

**Résultat de l'exécution ciblée** (`--filter FullyQualifiedName~BacktestTests.Risk`, hors Yahoo) :

```
Nombre total de tests : 57
Réussi(s)              : 57
Échoué(s)               : 0
```

**Note technique (précision décimale)** : la première implémentation accumulait l'équité de façon incrémentale
(`equity += netPnL` en partant du capital initial), ce qui produisait un résultat mathématiquement identique
mais **pas bit-identique** à `BacktestPnLResultBuilder`/`BacktestCostResultBuilder` (Lots 14.6/14.7) sur des
séries longues à haute précision décimale — un écart de l'ordre de 10⁻²⁴ est apparu au 24ᵉ chiffre après la
virgule dans un test de non-régression. Corrigé en accumulant `CumulativeNetPnL` séparément (démarrant à 0,
comme les Lots 14.6/14.7), puis en dérivant `Equity = InitialCapital + CumulativeNetPnL` par une seule
addition — reproduisant exactement le même ordre d'opérations que les builders existants. Résultat : équivalence
bit-à-bit vérifiée (`RiskAndCostDisabled_MatchesRunFullBacktestWithCostsExactly`).

---

## §13. Invariants scientifiques vérifiés

1. **FinalQuantity ≤ RequestedQuantity** — vérifié pour chaque position acceptée.
2. **FinalQuantity ≤ MaxPositionQuantity** — vérifié quand une limite existe.
3. **EstimatedRisk ≤ MaximumAllowedRisk** — prouvé par construction (`FinalQuantity ≤ RiskEngineQuantity`
   implique `RiskPerUnit × FinalQuantity ≤ RiskPerUnit × RiskEngineQuantity ≤ RiskBudget`) et vérifié par test
   `[Theory]` sur plusieurs combinaisons de contraintes.
4. **Exposure ≤ MaxExposure** — vérifié pour chaque position acceptée, y compris sur données Yahoo réelles.
5. **Position rejetée ⇒ aucune exécution, aucun coût, aucun PnL, aucun changement d'équité** — vérifié
   explicitement (`EquityBefore == EquityAfter`, `CostResult == null`, `NetPnL == null`).
6. **Déterminisme** (même configuration + mêmes données → même résultat) — vérifié à trois niveaux (évaluateur
   de position, builder d'agrégat, moteur complet via hash).

---

## §14. Compatibilité Lot 14.6 / Lot 14.7 — non-régression

Aucun fichier de `Backtest/Pnl/`, `Backtest/Execution/`, `Backtest/Cost/` n'a été modifié (vérifié par
`git diff` : zéro sortie). `Engine/Risk/*` et `Engine/TradePlan/*` (hors périmètre explicite de ce lot, utilisés
par le chemin live) n'ont également reçu aucune modification. Le seul changement à un fichier existant est
l'ajout, en fin de `BacktestEngine.cs`, de la méthode `RunFullBacktestWithRisk` (+ une ligne `using`) — `git
diff --stat` confirme **84 insertions, 0 suppression** sur ce fichier.

Preuve de non-régression : `RiskAndCostDisabled_MatchesRunFullBacktestWithCostsExactly` (et son équivalent au
niveau du builder) démontrent que, coûts et risque désactivés, `RunFullBacktestWithRisk` produit un
`FinalNetPnL`/`MaximumDrawdown`/`FinalEquity` **bit-identiques** à `RunFullBacktestWithCosts`/`RunFullBacktest`
sur le même scénario. `RiskEnabled_NeverChangesTheUnderlyingSignalMeasurementExecutionResults` confirme de plus
que les étapes Signal/Execution restent bit-identiques que le risque soit actif ou non.

---

## §15. Résultats Yahoo (MES=F, M5, 45 jours)

Conformément au brief §19, aucune valeur de PnL n'est figée dans une assertion (la fenêtre de 45 jours étant
glissante sur `DateTime.UtcNow` — dérive déjà documentée et démontrée empiriquement dans le rapport du Lot
14.7). Le test vérifie exclusivement les invariants du §13 sur chaque position autorisée, plus la cohérence de
la chaîne Requested → Risk → Execution → Cost → PnL (`CostResult.Quantity == RiskEvaluation.AllowedQuantity`
pour chaque position exécutée).

Exécution du 2026-08-23 (fenêtre glissante, 8673 barres, fingerprint série `FCE496B9...`), configuration :
`RiskDistance = 4 ticks (1.0 pt)`, `RiskPerTradePercent = 1%`, `MaxExposure = 50000 USD`, `StartingCapital =
100000 USD`, `Quantity requested = 1` :

| Métrique | Valeur |
|---|---|
| RequestedCount (positions Closed évaluées) | 1930 |
| AllowedCount | **1930** |
| RejectedCount | **0** |
| FinalNetPnL | +1016.25 USD |
| FinalEquity | 101016.25 USD |
| MaximumDrawdown | -3543.75 USD |
| RiskResult.DeterministicHash | `15FE82DCC9781924BD3C5A18F3FF08D3C8F557723DE84633B73C45AC310FEA31` |

Avec cette configuration illustrative (budget de risque ≈1000 USD, RiskPerUnit≈5 USD, affordant jusqu'à 200
contrats ; exposition par contrat ≈5000-6500 USD, bien en-deçà de la limite de 50000 USD), aucune contrainte ne
s'active jamais sur MES à quantité demandée=1 — les 1930 positions closes sont toutes autorisées, avec
`AllowedQuantity == RequestedQuantity == 1` pour chacune. **Tous les invariants du §13 ont été vérifiés pour
chacune des 1930 positions autorisées** (assertions dans `RiskYahooIntegrationTests`, aucun échec). La chaîne
`Requested → Risk → Execution → Cost → PnL` reste cohérente sur toute la série : `CostResult.Quantity ==
RiskEvaluation.AllowedQuantity` pour chaque position, vérifié explicitement.

Conformément au §19, ces chiffres ne sont **pas** des constantes scientifiques figées (fenêtre glissante sur
`DateTime.UtcNow`, cf. rapport Lot 14.7) — ils documentent un run réel, reproductible dans sa **logique** (même
configuration + mêmes données → même hash), pas dans ses valeurs numériques absolues d'un jour sur l'autre.

---

## §16. Décision finale

Le Lot 14.8 est livré comme fondation additive, déterministe et testée. `RiskEngine` (Lot 10) est réutilisé
sans modification pour tout le calcul de budget/sizing ; seuls l'Exposure (entièrement nouveau) et
l'orchestration séquentielle equity-aware sont ajoutés. Le comportement des Lots 14.1-14.7 est préservé
bit-à-bit. La configuration par défaut (`BacktestRiskConfiguration.Disabled()`) reproduit exactement le
comportement historique.

**STATUS : IMPLEMENTED.**

**Prochain lot suggéré** : Lot 14.9 (portée à définir — candidats naturels selon l'ordre déjà fixé : premher
pas vers un modèle de portefeuille avec positions concurrentes réelles, ou calibration empirique du
slippage/spread/risk distance à partir de données réelles).
