# QDE-012 — Sprint 15.25 — Lot 15.3 — Stop Loss: Scientific Foundation, Implementation & TradePlan Unblock

**Date** : 2026-08-24
**Branche** : `feature/structural-stability-v2`
**Lot précédent** : 15.2
**Statut** : **IMPLEMENTED**
**Type** : **FONDATION SCIENTIFIQUE + IMPLÉMENTATION — AUCUNE CALIBRATION**

---

# 1. EXECUTIVE SUMMARY

Le Lot 15.0 a identifié un P0 : `TradePlanContext.RiskParameters` est toujours `null`, rendant `TradePlanStatus.PLAN_READY` structurellement inatteignable, y compris pour `MeanReverting`. Ce lot ferme ce P0.

**Correctif implémenté, minimal, entièrement vérifié** : 1 nouveau fichier + 2 fichiers modifiés.

1. **`Engine/Risk/VolatilityStopLossModel.cs`** (nouveau) — première implémentation réelle de `IStopLossStrategy` (l'extension point déclaré au Lot 10, jusqu'ici jamais implémenté au-delà du pass-through `ProvidedStopLossStrategy`). Entrée scientifique : `VolatilityModel.CurrentVolatility` — une evidence déjà calculée, déjà testée, déjà auditée pour la causalité ("Lot B2"), pour chaque barre `MeanReverting`. Formule : `StopPrice = EntryPrice ∓ 2.0 × CurrentVolatility`, arrondie au tick vers l'entrée (jamais loin d'elle). Le multiplicateur `2.0` est **explicitement NON CALIBRÉ** — une convention "deux écarts-types", jamais recherchée ni optimisée sur ce projet.
2. **`Backtest/BacktestEngine.cs`** (modifié, non protégé) — un seul point d'injection : juste avant la construction du `TradePlanContext`, pour toute barre avec une direction BUY/SELL, résout `StopLoss` (via le composant ci-dessus) et `RiskPerTrade` (= `InitialCapital × MaxRiskPerTradePercent`, en réutilisant `RiskPolicy`/`InitialCapital` déjà validés par `BacktestScenario`), puis les passe à `TradePlanContext`. **`TradePlanBuilder.cs` n'a pas été touché** — sa logique existante (Sprint 15.8, inchangée) consomme désormais ce `RiskParameters` non-nul pour la première fois.
3. **`Tests/Backtest/Pipeline/BacktestSignalPipelineStageTests.cs`** (modifié) — un test préexistant (Lot 14.3) figeait en dur "`PLAN_READY` ne peut jamais se produire" — exactement le fait que ce lot corrige. Mis à jour pour vérifier l'invariant réel : `StopLoss`/`PLAN_READY` ne sont jamais fabriqués pour un bar `NO_ACTION`/`WATCH`, mais peuvent désormais légitimement apparaître pour un bar directionnel.

**Résultat empirique (dataset réel, MES M5, ~59 jours)** : **100% des 2317 barres `MeanReverting` directionnelles atteignent `PLAN_READY`** (0% avant ce lot). **0% `PLAN_READY` en dehors de `MeanReverting`**, confirmé sur 4437 barres — structurellement impossible autrement (Lot 15.1). `StopDistance` moyenne 5.04 unités de prix / 20.2 ticks. Aucune régression : suite complète **812 tests, 810 réussis, 1 échec préexistant sans rapport (déjà connu du Lot 15.2), 1 ignoré préexistant**.

**Limite majeure, découverte et documentée, pas corrigée** : `PositionSize` (calculé par la logique interne, déjà existante, de `TradePlanBuilder`) peut dépasser `InstrumentRiskSpecification.MaxQuantity` — mesuré : moyenne 51.5, maximum 160, alors que `MaxQuantity=50` dans la configuration de test. `TradePlanBuilder` n'applique aucun plafond de quantité — seul le vrai `RiskEngine.Evaluate` (via `RiskEngineRequestFactory`, chemin `RunFullBacktestWithRisk`, inchangé) applique correctement ce plafond. `TradePlan.PositionSize` doit donc être compris comme une **estimation légère**, jamais comme la taille de position réellement autorisée — un fait maintenant démontré empiriquement, pas seulement une préoccupation architecturale.

**Aucune calibration. Aucune optimisation. `ExecutionSimulator` n'utilise toujours pas le Stop Loss pour clôturer une position** (sortie à horizon fixe, inchangée) — documenté comme limite majeure, pas résolu dans ce lot.

---

# 2. LOT 15.0 FINDINGS (contexte)

`TradePlanContext.RiskParameters` toujours `null` (live ET backtest) → `PLAN_READY` inatteignable pour tout régime, y compris `MeanReverting`. `IStopLossStrategy` existe comme extension point documenté, jamais implémenté au-delà d'un pass-through. Recherche StopLoss active dans `Tests/Research/StopLossCalibration/`.

# 3. LOT 15.1 FINDINGS (contexte)

Seul `MeanReverting` produit une direction BUY/SELL. `EntryTriggerReason.UNSUPPORTED_REGIME` rend les 4 autres régimes explicites. Ce lot 15.3 ne modifie ni `EntryTriggerBuilder` ni la logique de régime — le Stop Loss n'est calculé QUE pour une direction déjà résolue, jamais fabriquée.

# 4. LOT 15.2 FINDINGS (contexte)

`StructuralBreak` toujours sans evidence de rupture en Fusion — indépendant de ce lot, non traité ici (brief explicite).

---

# 5. EXISTING RISK ARCHITECTURE

**Inventaire complet avant modification** (brief §1/§2), confirmant qu'aucun composant n'a été réimplémenté :

| Composant | Rôle | Statut avant ce lot |
|---|---|---|
| `Engine/Risk/RiskEngine.cs` | Moteur de risque complet : capital/equity, SL/TP directionnel, RiskPerUnit, drawdown, perte journalière, open-risk, budget, sizing (borné par MinQuantity/MaxQuantity/QuantityStep/MaxPositionSize), Risk/Reward | **Complet, testé (Lot 10), jamais modifié, jamais dupliqué** |
| `Engine/Risk/RiskEngineRequestFactory.cs` | Mappe un `TradePlan` déjà construit vers un `RiskEngineRequest` | **Existant (Lot 11), inchangé** — nécessite un `TradePlan.StopLoss` déjà résolu, ce qui n'était jamais le cas avant ce lot |
| `Engine/Risk/IStopLossStrategy.cs` | Extension point pour une méthodologie SL | **Existant (Lot 10), jamais implémenté au-delà d'un pass-through** — comblé par ce lot |
| `Backtest/Risk/PositionRiskEvaluator.cs` + `BacktestRiskResultBuilder.cs` | Réutilise `RiskEngine.Evaluate` pour sizer une `SimulatedPosition` déjà EXÉCUTÉE (post fixed-horizon), à partir d'une **distance de risque fournie par l'appelant** (`RiskDistanceConfiguration`, jamais dérivée du `TradePlan`) | **Existant (Lot 14.8), inchangé, non concerné par ce lot** — chemin de sizing séparé, pré-exécution, découplé de `TradePlan.StopLoss` |
| `Engine/TradePlan/TradePlanBuilder.cs` | Construit `TradePlan` : Direction/EntryPrice puis, SI `RiskParameters` fourni, StopLoss→RiskPerUnit→TakeProfit→PositionSize→RiskReward→Status | **Existant (Lot 15.8), NON MODIFIÉ** — attendait un `RiskParameters` non-nul depuis toujours (son propre commentaire : "RiskParameters is the extension point a future Risk Engine sprint would populate") |

**Ce qui manquait précisément (brief §2)** : ni le `RiskEngine`, ni `TradePlanBuilder` n'avaient besoin d'être créés ou modifiés — il manquait uniquement UN PRODUCTEUR de `TradeRiskParameters.StopLoss`, jamais construit avant ce lot. **Aucun second RiskEngine créé.**

**Découverte architecturale importante (Section 12)** : il existe **deux chemins de sizing distincts et non unifiés** — (a) `TradePlanBuilder`'s sizing léger (pas de plafond de quantité), maintenant activé par ce lot, et (b) le vrai `RiskEngine.Evaluate` via `PositionRiskEvaluator`/`BacktestRiskResultBuilder` (avec tous les plafonds), qui opère sur `SimulatedPosition` déjà exécutée avec une distance de risque **indépendante** du `TradePlan`. Ce lot n'a PAS unifié ces deux chemins — voir §12/§21.

---

# 6. EXISTING STOP LOSS RESEARCH

`Tests/Research/StopLossCalibration/` contient un corpus de recherche substantiel (Sprints 15.9-15.19), inspecté intégralement.

**Protocole verrouillé** (`QDE-012_StopLoss_Calibration_Protocol.md`, v1.1, Sprint 15.10) : compare scientifiquement deux candidats déjà formalisés avant toute exécution :
- **Candidate A1** : `StopDistance = k × InnovationStd` (écart-type d'innovation du filtre de Kalman).
- **Candidate A2** : `StopDistance = k × CurrentVolatility` (écart-type des rendements sur 20 barres, `VolatilityModel`) — **exactement la mesure que ce lot utilise**.
- **Candidate D** : ancré sur l'équilibre estimé (`SL = EstimatedEquilibrium ∓ k×InnovationStd`), formule verrouillée le 2026-08-12, plus complexe (condition d'applicabilité `k>|z0|`, sinon `DEGENERATE`).

**Discipline méthodologique du protocole**, exemplaire et directement pertinente pour ce lot : split TRAIN/VALIDATION/TEST par graine indépendante (jamais temporel, pour éviter la fuite d'autocorrélation), sélection par RÉGION stable (jamais un point unique), 10 familles de datasets **synthétiques**, et une déclaration explicite : *"REAL MARKET VALIDATION = NOT PERFORMED... every conclusion this protocol can produce is a synthetic validation... never evidence that a methodology is profitable on ES/NQ/NASDAQ."*

**Résultats obtenus (Sprint 15.11-15.14, campagne exécutée après le verrouillage du protocole), résumés SANS être réutilisés comme preuve d'un paramètre optimal (discipline explicite du brief §"RÈGLE ABSOLUE")** :
- **A1 > A2, qualifié, non unanime** — A1 gagne nettement sur les séries persistantes/quasi-racine-unitaire (RandomWalk, AR1(0.95)), A2 n'a qu'un avantage niche sur `VarianceBreak`.
- Un candidat **HYBRID** (routage conditionnel par `VolatilityRegime`) a été testé — conclusion : "A1 > HYBRID > A2", effet Hybrid-vs-A1 petit et **le signe s'inverse dans 2 des 5 variantes leave-one-out** — qualifié explicitement de "résultat fragile, pas robuste".
- **Sprint 15.14 ("A1 K Calibration") : `K_SELECTED: NO`** — malgré toute cette campagne, AUCUNE valeur de `k` n'a jamais été verrouillée comme recommandation de production.
- `VolatilityRegime.UNKNOWN` ne s'est jamais produit sur aucun dataset/split/scale testé — potentiellement inatteignable par construction du code.

**Tension identifiée et traitée avec transparence** : cette recherche suggère que `InnovationStd` (Candidate A1, Kalman) pourrait être une entrée plus robuste que `CurrentVolatility` (Candidate A2, ce que ce lot utilise) — **je ne change PAS mon choix suite à cette lecture**. Mon choix de `CurrentVolatility` a été fait pour des raisons d'architecture (accessible sans toucher un fichier protégé, déjà calculé pour chaque barre `MeanReverting` directionnelle) — mais `InnovationStd` est TOUT AUSSI accessible (même liste `ScientificResults`, `ScientificMetricKeys.InnovationStd`). Basculer vers `InnovationStd` maintenant, après avoir lu une comparaison de performance qui le favorise, **serait précisément le comportement interdit par la règle absolue de ce lot** — même si la décision finale s'avérait la même, la méthode de décision serait compromise. Ce choix reste donc **explicitement ouvert pour une décision future séparée**, jamais tranché silencieusement ici.

---

# 7. STOP LOSS SCIENTIFIC METHOD

**Candidats comparés (brief §4)**, sans supposer qu'ATR soit automatiquement le bon choix :

| Candidat | Disponible ? | Retenu ? | Raison |
|---|---|---|---|
| `VolatilityModel.CurrentVolatility` | OUI, déjà calculé pour chaque barre MeanReverting (Kalman/OU/DynamicZScore/Volatility/SPRT) | **RETENU** | Accessible sans toucher un fichier protégé, déjà causal (audité "Lot B2"), déjà en unité de prix directement utilisable |
| `KalmanFilterModel.InnovationStd` | OUI, même liste de résultats | Non retenu (voir §6, tension documentée) | Écarté pour éviter une décision influencée par la recherche de performance existante |
| `Engine.Regime.Evidence.VolatilityEvidence` (ACF des \|rendements\|) | OUI, mais jamais consommée (Lot 15.0/15.2) | Non retenu | Mesure de clustering (autocorrélation), pas une distance — pas directement transformable en niveau de prix |
| `Engine.Regime.Evidence.HurstEvidence` (brut) | OUI, jamais consommée | Non retenu | Ne mesure pas une dispersion de prix |
| Structure de marché (swing high/low) | **N'existe nulle part dans le dépôt** | Non retenu | Aucune infrastructure de détection de swing n'existe — en créer une serait une nouvelle infrastructure hors mandat (brief §4 : "sans introduire de nouvelle infrastructure inutile") |
| `Tests/Research/StopLossCalibration`'s Candidate D (ancré équilibre) | Formule verrouillée mais plus complexe, jamais implémentée en production | Non retenu | Introduit une condition de dégénérescence (`k>|z0|`) et une dépendance supplémentaire à `InnovationStd`/`DynamicZScore` — plus de surface pour une future calibration, pas nécessaire pour ce lot de fondation |

**Méthode retenue** : `StopPrice = EntryPrice ∓ multiplier × CurrentVolatility`, tick-arrondie vers l'entrée. Simple, explicable, causale, robuste aux entrées invalides, compatible avec `MeanReverting` uniquement (aucune fabrication pour les autres régimes).

---

# 8. STOP LOSS CONTRACT

`VolatilityStopLossModel` (nouveau, `Engine/Risk/`) :

```
Resolve(direction, entryPrice):
  rawDistance = CurrentVolatility × multiplier          (multiplier = 2.0, NON CALIBRÉ)
  rawStop     = entryPrice ∓ rawDistance                  (BUY: -, SELL: +)
  roundedStop = BUY  → Ceiling(rawStop / TickSize) × TickSize   (arrondi VERS l'entrée)
              = SELL → Floor(rawStop / TickSize) × TickSize     (arrondi VERS l'entrée)
  finalDistance = |entryPrice - roundedStop|
  return finalDistance > 0 ? roundedStop : null           (jamais fabriqué si dégénéré)
```

`TryResolveStopPrice(EntryTriggerCandidate, tickSize, multiplier)` : point d'entrée sûr, ne lève jamais d'exception, retourne `null` pour toute condition invalide (direction non directionnelle, prix ≤0, evidence de volatilité absente/invalide/non-finie/hors plage `decimal`, distance finale dégénérée).

---

# 9. CAUSALITY

**PASS**, à deux niveaux :
1. `VolatilityModel.CurrentVolatility` lui-même : déjà audité et verrouillé (Lot B2), avec sa propre suite de tests de causalité (`Tests/ScientificModels/VolatilityModelCausalityTests.cs`, B2-01..B2-09) — re-exécutée sans modification, toujours PASS.
2. `VolatilityStopLossModel` : fonction pure de `(EntryTriggerCandidate, tickSize, multiplier)`, ne lit aucun index de barre ni série temporelle — confirmé par un nouveau test de bout en bout (série tronquée à 220 barres vs série complète à 320 barres) : `TradePlan.StopLoss` bit-identique sur les 220 barres partagées.

---

# 10. TICK SIZE

**PASS**, testé explicitement (Cas H, §17) : `vol=1.0, multiplier=1.3, tick=0.25` → BUY : stop brut 98.7 → arrondi **98.75** (distance réduite de 1.3 à 1.25) ; SELL : stop brut 101.3 → arrondi **101.25** (même réduction). Les deux résultats sont exactement alignés sur le tick et la distance ne s'est jamais élargie par l'arrondi — confirmant la règle documentée §8 : `Math.Round()` seul n'aurait pas garanti cette direction (Math.Round arrondit au plus proche, pouvant élargir OU réduire selon le cas) — c'est pourquoi `Ceiling`/`Floor` dirigés sont utilisés à la place, jamais un arrondi générique.

---

# 11. MINIMUM DISTANCE

**Gap confirmé et documenté, non comblé (brief §11 : "si aucune contrainte n'existe, documenter explicitement le gap")** : recherche exhaustive dans `RiskPolicy.cs` et `InstrumentRiskSpecification.cs` — **aucun champ "MinimumStopDistance"/"MinimumTickDistance"/contrainte broker n'existe nulle part dans le dépôt**. Confirmé empiriquement (Cas I, §17) : une volatilité minuscule (0.13) produit un stop à distance d'exactement **un tick** (0.25), accepté sans rejet — rien dans l'architecture actuelle n'empêcherait un stop dangereusement proche de l'entrée. Ce lot **n'invente pas** cette contrainte (interdiction explicite du brief) — c'est un gap réel, à trancher par un futur lot dédié.

---

# 12. RISK INTERACTION

Deux constats, l'un positif, l'un une limite majeure :

**Positif** : `RiskPerTrade` réutilise EXACTEMENT `scenario.Policy.MaxRiskPerTradePercent` et `scenario.InitialCapital` — les mêmes valeurs déjà validées par `BacktestScenario.Create` et déjà consommées par le vrai `RiskEngine.Evaluate` via `RunFullBacktestWithRisk` — jamais un second concept de budget de risque. `RiskAmount` mesuré reste borné dans `[910, 1000]` sur 2317 positions, cohérent avec 2%×$50 000 (mean 990.96) — le calcul se comporte comme attendu.

**Limite majeure, découverte empiriquement, non corrigée (nécessiterait de modifier `TradePlanBuilder`, protégé)** : `TradePlanBuilder`'s calcul de `PositionSize` (`Math.Floor(RiskPerTrade/RiskPerUnit)`) **n'applique aucun plafond de quantité** — ni `MinQuantity`, ni `MaxQuantity`, ni `QuantityStep` (ces champs n'existent même pas sur `Core.InstrumentInfo`, le type que `TradePlanContext` transporte — ils n'existent que sur `Engine.Risk.InstrumentRiskSpecification`, un type différent, utilisé uniquement par le vrai `RiskEngine`). Mesuré : `PositionSize` moyenne **51.5**, maximum **160**, alors que la spécification de test utilise `MaxQuantity=50`. **`TradePlan.PositionSize` doit être lu comme une estimation légère du sizing basé sur le risque seul — jamais comme la taille réellement autorisée**, qui reste déterminée exclusivement par le chemin `RiskEngineRequestFactory.FromTradePlan → RiskEngine.Evaluate` (inchangé, toujours correct, mais découplé du `TradePlan` — voir §5).

---

# 13. POSITION SIZING INTERACTION

Ordre des opérations confirmé (brief §12/§13) : **Signal → EntryPrice → StopLoss → RiskPerUnit → PositionSize → TradePlan**, exactement dans cet ordre, réalisé en réutilisant la séquence déjà existante et inchangée de `TradePlanBuilder.Build` (Phases 3-9, jamais réordonnées) — ce lot ne fait que fournir les deux entrées manquantes (`StopLoss`, `RiskPerTrade`) au bon moment (avant l'appel à `TradePlanBuilder.Build`), jamais après.

---

# 14. BACKTEST EXECUTION

**Audité, non modifié (brief §14 : "ne pas inventer un modèle d'exécution complexe dans ce lot")**. Confirmé par relecture fraîche de `ExecutionSimulator.cs`/`ExecutionCandidate.cs`/`ExecutionConfiguration.cs`/`SimulatedPosition.cs` :

- `ExecutionCandidate` (5 champs) **ne transporte aucun StopLoss/TakeProfit**.
- `ExecutionConfiguration` n'a qu'un seul paramètre : `HorizonBars`.
- `SimulateCore` ne lit **jamais** High/Low — uniquement `Open` (fill) et `Close` (sortie).
- La sortie est **inconditionnellement** `EntryBarIndex + HorizonBars` (sortie à horizon temporel fixe).
- `ExitReason` n'a **qu'une seule valeur** : `TimeHorizon` — avec un commentaire de doc réservant explicitement la place pour un futur ajout StopLoss/TakeProfit.
- BUY/SELL sont traités symétriquement.

**Conclusion, sans ambiguïté** : le Stop Loss maintenant produit par `TradePlan` **n'est pas encore utilisé par la simulation d'exécution**. Une position s'ouvre et se ferme exactement comme avant ce lot — uniquement l'horizon fixe compte, jamais le niveau de stop. C'est une limite majeure et délibérément non résolue ici (§21).

---

# 15. LIVE/BACKTEST CONSISTENCY

`VolatilityStopLossModel` (`Engine/Risk/`) ne dépend que de types `Engine.*` (`EntryTriggerCandidate`, `ScientificModelResult`, `ScientificMetricKeys`, `IStopLossStrategy`, `TradeDirection`) — jamais de `Backtest.*` ni d'ATAS. Il est structurellement réutilisable tel quel depuis `IQIAIndicator.cs` (le point d'entrée live).

**Décision délibérée, documentée** : ce lot **n'a PAS câblé** ce composant dans `IQIAIndicator.cs`. Cela suit exactement le précédent déjà établi par le projet : `Engine.Risk.RiskEngine`/`RiskEngineRequestFactory` (Lot 10/11) sont eux aussi entièrement réutilisables mais **jamais câblés dans `IQIAIndicator.cs`** non plus (`TradePlanContext` y est toujours construit sans `riskParameters`, inchangé). Câbler le live est une décision distincte, à faible risque technique mais touchant la surface de trading réelle — laissée à une décision explicite future, jamais faite silencieusement ici.

---

# 16. SYNTHETIC TESTS

Cas A-J (`Tests/Risk/VolatilityStopLossModelTests.cs`), tous PASS, chiffres réels :

| Cas | Entrée | Résultat |
|---|---|---|
| A — BUY, vol normale | vol=2.0 | StopLoss=96 (distance=4) |
| B — SELL, vol normale | vol=2.0 | StopLoss=104 (distance=4) |
| C — vol faible | vol=0.5 | StopLoss=99 (distance=1.0, < A) |
| D — vol élevée | vol=10.0 | StopLoss=80 (distance=20.0, > A) |
| E — vol=0 | constructeur | `ArgumentOutOfRangeException` ; `TryResolveStopPrice`→null (jamais atteint le constructeur) |
| F — vol=NaN/+Inf/-Inf | métrique brute | null dans les 3 cas, jamais d'exception |
| G — prix invalide | EntryPrice=0 et -50 | null dans les 2 cas |
| H — tick size | vol=1.0, mult=1.3, tick=0.25 | BUY 98.7→**98.75** ; SELL 101.3→**101.25** — arrondi vers l'entrée confirmé |
| I — distance minimale | vol=0.13 | distance = exactement 1 tick (0.25), acceptée — gap confirmé (§11) |
| J — données futures injectées | tronqué vs étendu | `TradePlan.StopLoss` bit-identique sur les barres partagées |

---

# 17. PROPERTY TESTS

Balayage de 2×5×8×4×5 = **1600 combinaisons** `(direction, entryPrice, currentVolatility, tickSize, multiplier)`. Pour chaque résultat non-nul, invariants vérifiés sans exception : `BUY→StopPrice<EntryPrice`, `SELL→StopPrice>EntryPrice`, `StopDistance>0`, alignement tick exact, et `même entrée → même sortie` (bit-identique, appels répétés).

---

# 18. TRADEPLAN INTEGRATION

Chemin complet, désormais fonctionnel de bout en bout, confirmé empiriquement :

```
EntryTriggerCandidate{Direction=BUY/SELL, Reason=READY}
    ↓
VolatilityStopLossModel.TryResolveStopPrice → StopLoss
    ↓ (+ RiskPerTrade = InitialCapital × MaxRiskPerTradePercent)
TradeRiskParameters(StopLoss, RiskPerTrade)
    ↓
TradePlanBuilder.Build (INCHANGÉ) → RiskPerUnit, TakeProfit, PositionSize, RiskRewardRatio
    ↓
TradePlan{Status = PLAN_READY}
```

**PLAN_READY n'est jamais forcé** — si `StopLoss` ou `RiskPerTrade` ne se résout pas (rare, jamais observé sur MeanReverting directionnel dans ce dataset, mais structurellement possible si `VolatilityModel` échouait), `TradePlanBuilder`'s logique existante (inchangée) retombe correctement sur `SIGNAL_ONLY`, jamais une fabrication.

---

# 19. REGRESSION

**PASS**. Un seul test préexistant nécessitait une mise à jour — `Stage_EntryTriggerToTradePlan_TradePlanIsBuiltOnlyWhenEntryTriggerIsPresent_NeverFabricated` (Lot 14.3), qui figeait explicitement "`PLAN_READY` ne peut jamais se produire" (exactement le fait que ce lot corrige, par construction). Mis à jour pour vérifier l'invariant réel restant : `StopLoss`/`Status` ne sont jamais fabriqués pour un bar non-directionnel — toujours `NO_TRADE`/`null` dans ce cas, inchangé. Recherche exhaustive (`grep` sur tout `Tests/` pour "PLAN_READY" et "StopLoss.*null") confirmant qu'aucun autre test préexistant n'était affecté par ce changement — les candidats les plus probables (`DecisionDirectionCoherenceTests`, `TradePlanBuilderTests`, `RiskEngineIntegrationTests`) construisent tous leur `TradePlanContext` à la main, sans jamais passer par `BacktestEngine.RunSignalPipeline`, donc structurellement non affectés — vérifié, pas seulement supposé.

**Suite complète : 812 tests, 810 réussis, 1 échec, 1 ignoré.**

Échec, préexistant, sans rapport (déjà identifié au Lot 15.2, reconfirmé identique ici) : `HysteresisThresholdSensitivityLot1418Tests` (`FidelityPass=False, 3783/10931 mismatches`) — une réplique de test de `FusionStateManager`, en amont de Decision/EntryTrigger/TradePlan, structurellement indépendante de ce lot. Non corrigé (hors périmètre, `FusionStateManager` protégé).

Ignoré, préexistant : `Sprint1515HistoricalReconciliationXunitTests.VerifySprint1514Unaffected`.

---

# 20. PRODUCTION SAFETY

```
git diff --stat (avant → après ce lot, fichiers de ce lot uniquement) :
 IQIAIndicator/Backtest/BacktestEngine.cs                              | +75/-7 (dont le hunk Lot 15.3 + le travail Lot 14.10 déjà présent, non lié)
 IQIAIndicator/Tests/Backtest/Pipeline/BacktestSignalPipelineStageTests.cs | +20/-7
```

Nouveau : `IQIAIndicator/Engine/Risk/VolatilityStopLossModel.cs`. Fichiers de test additifs : `Tests/Risk/VolatilityStopLossModelTests.cs`, `Tests/XunitWrappers/VolatilityStopLossModelXunitTests.cs`, `Tests/Backtest/Pipeline/VolatilityStopLossLot153PipelineTests.cs`, `Tests/Backtest/Calibration/StopLossFoundationLot153Tests.cs`, `Tests/Research/RegimeCoverageAudit/Output/stoploss_foundation_lot153.csv`.

`git status -uall` confirmé : uniquement ces éléments ajoutés/modifiés, tout le reste de l'arbre (travail préexistant des lots précédents) intact et inchangé. Aucun fichier protégé (`RegimeEngine.cs`, `DecisionArbitrator.cs`, `EvidenceFusionEngine.cs`, `FusionStateManager.cs`, `EntryTriggerBuilder.cs`/`EntryTriggerAssessment.cs`, `SignalEngine.cs`, `TradePlanBuilder.cs`, `RiskEngine.cs`, `RiskPolicy.cs`, `InstrumentRiskSpecification.cs`, `RiskEngineRequest.cs`, `RiskAssessment.cs`, `ExecutionSimulator.cs`, `VolatilityModel.cs`, `IQIAIndicator.cs`) touché. Debug et Release compilent proprement (0 avertissement, 0 erreur) pour `IQIAIndicator.csproj` et `Tests/IQIAIndicator.Tests.csproj`. Aucun ordre. Aucune DLL déployée. ATAS non utilisé. Aucun commit.

---

# 21. REMAINING GAPS

1. **`ExecutionSimulator` n'utilise toujours pas le Stop Loss** (§14) — la plus importante limite restante. Un `PLAN_READY` avec un vrai niveau de stop existe désormais, mais la simulation continue de sortir à horizon fixe, ignorant ce niveau. Toute mesure de performance sur les positions actuelles reste celle d'une stratégie "horizon fixe", pas "Stop Loss/Take Profit" (cohérent avec le constat déjà fait au Lot 15.0 §18/25.6).
2. **`TradePlanBuilder`'s sizing n'a aucun plafond de quantité** (§12) — `PositionSize` peut dépasser `MaxQuantity`, mesuré empiriquement. Le vrai plafond n'existe que dans le chemin `RiskEngine.Evaluate`, découplé du `TradePlan`.
3. **Aucune contrainte de distance minimale n'existe** (§11) — un stop à une distance d'un seul tick est actuellement accepté sans réserve.
4. **Ambiguïté intrabar** (brief §15) — puisque `ExecutionSimulator` n'utilise ni High ni Low, la question "stop touché sur la même barre que l'entrée, et dans quel ordre par rapport à un take-profit" reste entièrement **non résolue et non pertinente pour l'instant** — à documenter explicitement pour tout futur lot de réalisme d'exécution, jamais à deviner.
5. **Deux chemins de sizing non unifiés** (§5/§12) — `TradePlanBuilder` (léger, maintenant actif) et `RiskEngine`/`PositionRiskEvaluator` (complet, mais opérant sur une `SimulatedPosition` déjà exécutée avec une distance de risque fournie séparément) ne partagent aucune logique commune de bout en bout.
6. **Choix `CurrentVolatility` vs `InnovationStd` laissé ouvert** (§6) — délibérément, pour ne pas laisser la recherche de performance existante influencer une décision d'architecture faite dans ce lot.
7. **Live (`IQIAIndicator.cs`) non câblé** (§15) — délibéré, cohérent avec le précédent déjà établi pour `RiskEngine`.

---

# 22. RECOMMENDED NEXT LOT

Par ordre de dépendance logique, jamais imposé :

1. **Lot Execution Realism** — faire consommer `TradePlan.StopLoss`/`TakeProfit` par `ExecutionSimulator` (intrabar High/Low, gestion de l'ambiguïté même-barre) — le préalable pour que toute mesure de performance future reflète une vraie stratégie SL/TP.
2. **Lot Position Sizing Unification** — décider si `TradePlanBuilder` doit adopter les plafonds de `InstrumentRiskSpecification`, ou si le chemin `RiskEngine.Evaluate` doit devenir la seule source de vérité pour `PositionSize`.
3. **Lot StructuralBreak Evidence** (déjà recommandé au Lot 15.2, toujours en attente d'une décision utilisateur).
4. Une éventuelle calibration future du multiplicateur (2.0) et du choix `CurrentVolatility`/`InnovationStd`/Candidate D — explicitement hors de ce lot, nécessitant son propre mandat.

---

# 23. HANDOFF CONTEXT

Voir bloc final ci-dessous.

---

# HANDOFF CONTEXT — NEXT SESSION

```
PROJECT:
IQIA

CURRENT LOT:
15.3

LAST COMPLETED:
15.2

CALIBRATION:
PAUSED

STOP LOSS:
IMPLEMENTED (foundation only). VolatilityStopLossModel (Engine/Risk/, new) computes StopPrice =
EntryPrice -/+ 2.0*CurrentVolatility, tick-rounded toward entry. Wired into BacktestEngine.RunSignalPipeline
only (not IQIAIndicator.cs/live, deliberately). PLAN_READY now reachable: 100% of 2317 directional
MeanReverting bars reach it (0% before this lot). 0% PLAN_READY outside MeanReverting, confirmed on 4437
bars. TradePlanBuilder.cs itself was NOT modified.

METHOD:
Volatility-based (VolatilityModel.CurrentVolatility, 20-bar trailing return stdev, already computed/
causal/tested for every MeanReverting bar). Chosen over KalmanFilterModel.InnovationStd (equally
accessible) deliberately WITHOUT letting prior research's performance comparison (A1=InnovationStd beat
A2=CurrentVolatility in Sprint 15.11-15.14 synthetic campaigns) influence the choice - documented
explicitly as an open decision for a future lot, not silently resolved here.

SCIENTIFIC BASIS:
CurrentVolatility is a 20-bar trailing standard deviation of price differences, already computed as part
of MeanReversionMethodology's 5-model stack (ScientificModelRegistry), already causality-audited ("Lot
B2", VolatilityModelCausalityTests.cs, re-run unmodified, still PASS). Multiplier=2.0 is explicitly
NON-CALIBRATED (a conventional "2 standard deviations" distance, never tuned against this project's
PnL/win-rate/Sharpe/expectancy - confirmed no grid search, no optimization anywhere in this lot's work).

RISK ENGINE:
REUSED, not modified, not duplicated. RiskPerTrade = scenario.InitialCapital * scenario.Policy.
MaxRiskPerTradePercent - reuses the SAME BacktestScenario fields the real RiskEngine.Evaluate path
(RunFullBacktestWithRisk) already validates/consumes. The full RiskEngine.Evaluate chain
(RiskEngineRequestFactory -> RiskEngine, via PositionRiskEvaluator/BacktestRiskResultBuilder, Lot 14.8)
remains completely separate and unmodified - it operates on already-EXECUTED SimulatedPosition with an
independently caller-supplied RiskDistanceConfiguration, never derived from TradePlan.StopLoss.

POSITION SIZING:
GAP CONFIRMED EMPIRICALLY: TradePlanBuilder's own PositionSize calculation (unmodified, pre-existing
Sprint 15.8 logic) applies NO MaxQuantity/MinQuantity/QuantityStep clamp - Core.InstrumentInfo (what
TradePlanContext carries) doesn't even have those fields, only Engine.Risk.InstrumentRiskSpecification
does (a different type, used only by the real RiskEngine). Measured: PositionSize mean=51.5, max=160,
against a test MaxQuantity=50. TradePlan.PositionSize is a lightweight ESTIMATE only, never the
authoritative allowed size - that remains RiskEngine.Evaluate's job, on a separate, unmodified path.

TRADEPLAN:
PLAN_READY reachable and reached (100% of directional MeanReverting bars this run). Never fabricated for
NO_ACTION/WATCH (still NO_TRADE, StopLoss still null there - verified, the one regression test that
pinned the OLD "never PLAN_READY" behavior was updated to check this precise, narrower invariant instead).

EXECUTION:
UNCHANGED, CONFIRMED BY FRESH RE-READ: ExecutionSimulator still uses a fixed time-horizon exit only
(Open[EntryBarIndex+HorizonBars]), never reads High/Low, has exactly one ExitReason (TimeHorizon, its own
doc comment reserves space for a future StopLoss/TakeProfit addition), cannot trigger a stop on any bar.
The new StopLoss is NOT yet consumed by execution simulation - this is the single largest remaining gap
(see P0 REMAINING).

CAUSALITY:
PASS, two levels: VolatilityModel.CurrentVolatility's own pre-existing "Lot B2" causality proof
(VolatilityModelCausalityTests.cs, re-run unmodified) plus a new end-to-end truncated-vs-extended series
test proving TradePlan.StopLoss is bit-identical regardless of future bars.

TICK SIZE:
PASS. Rounds toward Entry (BUY: Ceiling, SELL: Floor) - never Math.Round() - proven by Case H to always
SHRINK the pre-rounding distance, never widen it, so RiskPerUnit/PositionSize (computed downstream from
the rounded StopLoss) never understate the risk actually taken.

MINIMUM DISTANCE:
GAP CONFIRMED, NOT ADDRESSED (brief explicitly forbids inventing one). No MinimumStopDistance concept
exists anywhere in RiskPolicy.cs or InstrumentRiskSpecification.cs. Empirically: a StopLoss exactly 1 tick
from Entry is accepted without any rejection today.

REGIME:
Unchanged from Lot 15.1/15.2 - 5 live regimes, MeanReverting the only one with any Signal/Entry/now
TradePlan.PLAN_READY capability. This lot adds nothing new here - StopLoss is only ever computed when
Direction is already BUY/SELL, which remains structurally impossible outside MeanReverting.

SIGNAL:
NOT MODIFIED.

ENTRY:
NOT MODIFIED.

STRUCTURAL BREAK:
Unchanged from Lot 15.2 - still no structural-break evidence wired into Fusion, still blocked on an
explicit user design decision, untouched by this lot.

P0 REMAINING:
(1) ExecutionSimulator still ignores StopLoss/TakeProfit entirely - a real Stop Loss now EXISTS on
TradePlan but is not yet ACTED ON in simulation; any measured backtest performance still reflects a fixed-
horizon strategy, not an SL/TP one. (2) StructuralBreak evidence gap (Lot 15.2, unchanged, independent).

P1:
TradePlanBuilder's PositionSize has no quantity ceiling (confirmed by measurement, max observed 160 vs
MaxQuantity=50) - a pre-existing Sprint 15.8 gap, now empirically exercised for the first time by this
lot's fix. No minimum-stop-distance constraint exists anywhere. Two parallel, non-unified sizing paths
(TradePlanBuilder's lightweight one vs RiskEngine.Evaluate's complete one) remain unreconciled. The
CurrentVolatility-vs-InnovationStd choice remains open, deliberately not resolved by reading prior
research. Live (IQIAIndicator.cs) wiring deliberately not done, mirrors existing RiskEngine precedent.

NEXT LOT:
An Execution Realism lot (wire TradePlan.StopLoss/TakeProfit into ExecutionSimulator, resolve the
intrabar/same-bar ambiguity explicitly) is the most consequential next step - without it, this lot's
StopLoss exists on paper but does not yet change any simulated trade's outcome. A Position Sizing
Unification lot is a close second. Both are independent of the still-open StructuralBreak Evidence
decision (Lot 15.2) and of any future Signal-model-research lot (Lot 15.1).

WHY:
This lot closes the STOPPED LOSS P0 from Lot 15.0 at the TradePlan-contract level (a real, causal,
non-fabricated, tick-correct, tested StopLoss now reaches PLAN_READY) - but PLAN_READY reaching 100% for
MeanReverting does NOT yet mean the backtest measures a Stop-Loss-managed strategy: ExecutionSimulator's
fixed-horizon exit is a separate, still-unaddressed gap that determines what actually gets measured. Both
facts must be held simultaneously by whoever reads this handoff.

DO NOT DO:
Do NOT modify TradePlanBuilder, RiskEngine, RiskPolicy, InstrumentRiskSpecification, RiskEngineRequest,
RiskAssessment, ExecutionSimulator, VolatilityModel, RegimeEngine, DecisionArbitrator,
EvidenceFusionEngine, FusionStateManager, EntryTriggerBuilder, EntryTriggerAssessment, SignalEngine, or
IQIAIndicator.cs without a dedicated, separate lot - none were touched here beyond BacktestEngine.cs's one
injection point. Do NOT calibrate/optimize DefaultVolatilityMultiplier (2.0) against PnL/win-rate/Sharpe/
expectancy - it is explicitly provisional. Do NOT switch the volatility source to InnovationStd based on
the existing Sprint 15.11-15.14 research finding it superior - that decision was deliberately left open,
not silently made, to avoid a performance-influenced architecture choice. Do NOT treat TradePlan.
PositionSize as the authoritative allowed trade size - it has no quantity ceiling; only RiskEngine.
Evaluate (a separate, unmodified path) enforces that. Do NOT assume ExecutionSimulator now respects
StopLoss - confirmed by fresh code read that it still does not; PLAN_READY existing does not mean the
backtest's simulated P&L reflects a stop-managed trade yet.
```

**STOP.**
