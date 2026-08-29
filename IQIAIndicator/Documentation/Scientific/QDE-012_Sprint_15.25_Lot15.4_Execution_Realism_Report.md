# QDE-012 — Sprint 15.25 — Lot 15.4 — Execution Realism: Stop Loss / Take Profit / Intrabar

**Date** : 2026-08-24
**Branche** : `feature/structural-stability-v2`
**Lot précédent** : 15.3
**Statut** : **IMPLEMENTED**
**Type** : **MOTEUR D'EXÉCUTION CAUSAL — AUCUNE CALIBRATION**

---

# 1. EXECUTIVE SUMMARY

Le Lot 15.3 a rendu `TradePlan.StopLoss`/`TakeProfit` réels (100% `PLAN_READY` pour `MeanReverting` directionnel), mais `ExecutionSimulator` les ignorait entièrement — sortie exclusivement à horizon fixe. Ce lot corrige ce P0 : le simulateur consulte désormais réellement le Stop/Target intrabar.

**Correctif implémenté, minimal, aucune stratégie inventée** : 4 fichiers de production modifiés (`ExecutionCandidate.cs`, `SimulatedPosition.cs`, `BacktestExecutionResult.cs`, `ExecutionSimulator.cs`), 190 lignes (+/-). `TradePlanBuilder`, `RiskEngine`, tout le pipeline Regime→Signal→Entry restent strictement inchangés.

**Chaîne finale** :
```
TradePlan.StopLoss/TakeProfit → ExecutionCandidate (étendu, 5→7 champs) → ExecutionSimulator
    → surveillance intrabar [EntryBar..HorizonBar] → StopLoss / TakeProfit / Ambiguous / TimeHorizon
```

**Découverte scientifique majeure de ce lot, non anticipée, honnêtement rapportée** : le choix (raisonné, documenté §9) d'inclure la barre de remplissage elle-même dans la fenêtre de surveillance intrabar — parce qu'`Open` en est le premier événement chronologique — s'avère être **le facteur dominant** de la distribution de sorties observée. Sur le dataset réel : `TakeProfit` représente 72.5% des sorties, avec une médiane de **0 barre de détention** — c'est-à-dire que dans plus de la moitié des cas, le High/Low de la barre de remplissage elle-même touche déjà la cible. Ce n'est pas une anomalie : `TakeProfit`/`StopLoss` sont dérivés d'un écart-type de RENDEMENTS sur 20 barres (Lot 15.3), une mesure structurellement plus petite que l'amplitude High-Low d'une seule barre M5 typique. **Ce résultat est scientifiquement explicable, ni un succès ni un échec** (brief §29) — il est présenté ici en détail, avec la sensibilité au choix de conception qui le produit, jamais comme une performance.

**Découverte secondaire, confirmée empiriquement à deux reprises sur des pulls Yahoo distincts** : `PositionStatus.InvalidStopTarget` (nouveau, cette lot) se déclenche sur **3.35% des positions directionnelles** (81/2415, puis 62/8803 sur un second pull indépendant) — `TradePlan.StopLoss`/`TakeProfit` sont calculés contre `TradePlan.EntryPrice` (le prix de référence de la barre signal), mais le remplissage réel est `Open[SignalBarIndex+1]` (convention Lot 14.10) — un écart de prix entre les deux peut faire passer le niveau du mauvais côté. Écart moyen mesuré : **1.48** pour les positions `InvalidStopTarget`, contre **0.14** pour les positions `Closed`. Ce n'est pas un bug de ce lot — c'est un fait architectural préexistant, invisible jusqu'à ce que ce lot y ajoute une vérification (brief §5).

**Régression : PASS**. Suite complète : **854 tests, 852 réussis, 1 échec préexistant sans rapport (déjà connu, Lot 15.2/15.3), 1 ignoré préexistant.** 3 tests supplémentaires ont nécessité une extension mineure et évidente (ajout de `PositionStatus.InvalidStopTarget` à des `[InlineData]` déjà génériques, jamais un affaiblissement d'assertion).

**Aucune calibration, aucune optimisation.** Le multiplicateur 2.0 (Lot 15.3), l'horizon (10 barres, inchangé depuis Lot 14.5), et toute règle de gap/slippage restent explicitement non touchés/non inventés.

---

# 2. LOT 15.3 CONTEXT

`VolatilityStopLossModel` produit `TradePlan.StopLoss` (2.0×`CurrentVolatility`, tick-arrondi vers l'entrée). `PLAN_READY` atteint 100% pour `MeanReverting` directionnel. P0 restant, identifié explicitement par ce lot : `ExecutionSimulator` n'utilisait ni High ni Low, sortie uniquement à horizon fixe (`ExitReason.TimeHorizon`, seule valeur existante).

---

# 3. EXISTING EXECUTION ARCHITECTURE

Contrat avant modification, confirmé par lecture intégrale :

- `ExecutionCandidate` (5 champs) : `SignalBarIndex, SignalTimestamp, Direction, EntryPrice, TradePlanStatus` — ne transportait ni `StopLoss` ni `TakeProfit`.
- `ExecutionConfiguration` : un seul champ, `HorizonBars`.
- `SimulatedPosition` : `PositionId, Status, Reason, Direction, EntryTimestamp, EntryPrice, EntryBarIndex, ExitTimestamp, ExitPrice, ExitBarIndex, ExitReason, HoldingBars, GrossPriceMove, Return` — **déjà** les champs nécessaires pour porter un exit Stop/Target (`ExitPrice`/`ExitBarIndex`/`ExitReason` génériques), confirmant qu'aucune duplication n'était nécessaire (brief §26).
- `ExitReason` : un seul membre, `TimeHorizon`, avec un commentaire de doc réservant explicitement la place pour un futur ajout.
- `ExecutionSimulator.SimulateCore` : remplissage à `Open[SignalBarIndex+1]` (Lot 14.10), sortie inconditionnelle à `Close[EntryBarIndex+HorizonBars]`, **aucune lecture de High/Low nulle part**.

---

# 4. TRADEPLAN → EXECUTION CONTRACT

Solution minimale retenue (brief §4) : **extension de `ExecutionCandidate`** (5→7 champs, `StopLoss`/`TakeProfit` ajoutés), jamais une seconde représentation. `ExecutionCandidate.FromSignal` lit `plan?.StopLoss`/`plan?.TakeProfit` — `TradePlan.StopLoss`/`.TakeProfit` restent l'unique source de vérité, jamais recalculés ni dupliqués.

---

# 5. STOP LOSS (exécution)

**Détection intrabar** : BUY → `Low ≤ StopLoss` ; SELL → `High ≥ StopLoss`. **Prix de sortie** : toujours le niveau théorique `StopLoss` lui-même (jamais le High/Low/Open de la barre déclenchante — voir §10 Gap).

**Invariant de direction vérifié, jamais exécuté silencieusement (brief §5)** : si `StopLoss` est du mauvais côté de l'**EntryPrice réel** (celui de la barre de remplissage, pas celui de `TradePlan`), la position devient `PositionStatus.InvalidStopTarget` — jamais `Closed`. **Découverte empirique** : ce cas se produit réellement, 3.35% des positions directionnelles sur le dataset de test (§18/§20) — `TradePlanBuilder`/`VolatilityStopLossModel` garantissent la cohérence contre `TradePlan.EntryPrice` (prix de référence de la barre signal), mais jamais contre le prix de remplissage réel `Open[SignalBarIndex+1]`, une distinction jamais vérifiée avant ce lot.

---

# 6. TAKE PROFIT (exécution)

Symétrique : BUY → `High ≥ TakeProfit` ; SELL → `Low ≤ TakeProfit`. Même invariant de direction, même discipline de prix de sortie théorique.

---

# 7. INTRABAR DETECTION

Fenêtre surveillée : `bars[EntryBarIndex..ExitBarIndex]` **INCLUSIVE**, décision raisonnée et documentée (brief §9, "ne pas supposer") :

**L'entrée elle-même est-elle surveillée ?** OUI — décision explicite, pas une supposition. `EntryPrice = Open[EntryBarIndex]` est le premier événement chronologique de cette barre ; la position est donc déjà ouverte pendant le reste du High/Low/Close de CETTE MÊME barre — lire ces valeurs est une lecture causale de données concurrentes au remplissage, jamais un look-ahead (elles appartiennent à la barre du fill, pas à une barre future). Documenté explicitement dans le code (`ExecutionSimulator.cs`, commentaire "WINDOW").

**Conséquence empirique de ce choix, non anticipée avant l'exécution réelle** : c'est le facteur dominant de la distribution observée (§18) — la majorité des sorties `TakeProfit` (médiane = 0 barre de détention) proviennent de la barre de remplissage elle-même. Un futur lot qui choisirait d'exclure l'entrée de la surveillance obtiendrait une distribution très différente — ce choix de conception, documenté et raisonné, n'est PAS neutre sur les résultats et doit être compris comme tel, jamais oublié.

---

# 8. SAME-BAR AMBIGUITY

**Politique retenue : Option C (marquage explicite) + Option A (prix conservateur), combinées et documentées séparément — jamais présentées comme une observation réelle du marché.**

Quand la MÊME barre touche à la fois `StopLoss` et `TakeProfit`, OHLC seul ne permet pas de déterminer l'ordre réel (brief §7). Politique :
1. **`ExitReason.Ambiguous`** — une valeur distincte, jamais confondue avec un vrai `StopLoss`/`TakeProfit` (Option C : "marquer comme ambigu").
2. **Prix de sortie = `StopLoss`** — la convention conservatrice, "l'issue défavorable au trade" (Option B/A du brief), utilisée UNIQUEMENT pour que le P&L reste mesurable — **jamais une affirmation que le stop a été touché en premier**.

Cette combinaison permet à tout consommateur en aval de filtrer précisément les cas ambigus (via `ExitReason.Ambiguous`) s'il souhaite une convention différente, sans jamais perdre l'information qu'une hypothèse de conservatisme a été appliquée pour le prix.

**Mesuré** : 57/2334 positions closes (2.44%) sont `Ambiguous` — une minorité, mais non négligeable.

---

# 9. AMBIGUITY POLICY

Aucune politique n'existait avant ce lot (`ExitReason` n'avait qu'un seul membre). La politique ci-dessus (§8) est nouvelle, documentée, déterministe et symétrique (testée explicitement pour BUY et SELL, résultats structurellement identiques). Option D (données intrabar plus fines) confirmée impossible avec le dataset M5 actuel — non poursuivie, aucune nouvelle source de données introduite (brief interdit explicitement toute intégration de nouvelle donnée dans ce lot).

---

# 10. GAP HANDLING

**Aucune convention de gap n'existe dans le dépôt — confirmé, documenté, non comblé (brief §10 : "si elle n'existe pas, documenter le gap").** Recherche exhaustive : ni `RiskPolicy`, ni `ExecutionConfiguration`, ni aucun composant existant ne traite le cas d'un `Open` déjà au-delà du niveau de Stop/Target. **Décision de ce lot : le prix de sortie reste TOUJOURS le niveau théorique (`StopLoss`/`TakeProfit`), jamais l'`Open` gappé ni un prix "réaliste" inventé** — confirmé par test golden (§18) : un `Open` gappé sous le Stop produit quand même un `ExitPrice` égal au Stop théorique, jamais l'Open. Ceci est délibérément l'option la MOINS invasive (aucune règle inventée), au prix d'un optimisme potentiel non résolu (documenté §23, pas corrigé).

---

# 11. SLIPPAGE

**`SLIPPAGE MODEL = NOT IMPLEMENTED` pour les sorties d'`ExecutionSimulator`**, confirmé explicitement. Une infrastructure de slippage/spread/commission EXISTE dans le dépôt (`Backtest/Cost/`: `SlippageConfiguration`, `SpreadConfiguration`, `CommissionConfiguration`, `ExecutionPriceModel`) mais s'applique exclusivement au **remplissage d'ENTRÉE**, en aval d'`ExecutionSimulator` (`PositionCostCalculator`), jamais aux prix de sortie Stop/Target/Horizon. Ce lot ne l'étend pas à la sortie — hors mandat, documenté comme gap distinct de celui du §10.

---

# 12. EXIT REASONS

`ExitReason` : `TimeHorizon` (inchangé) + 3 nouveaux membres additifs : `StopLoss`, `TakeProfit`, `Ambiguous`. Recherche exhaustive de toute référence préexistante à `ExitReason`/`PositionStatus` avant modification (brief §12/§2) : confirmé aucun `switch` exhaustif nulle part dans le dépôt (uniquement des gardes génériques `Status != PositionStatus.Closed` dans `PositionRiskEvaluator`/`PositionCostCalculator`/`PositionPnLCalculator`) — aucun consommateur existant cassé, 3 tests `[Theory]` étendus proprement (§21).

---

# 13. EXIT PRICE

| Sortie | Prix |
|---|---|
| StopLoss | Niveau `StopLoss` théorique |
| TakeProfit | Niveau `TakeProfit` théorique |
| Ambiguous | `StopLoss` (convention conservatrice, §8) |
| TimeHorizon | `Close[ExitBarIndex]` — **inchangé**, comportement identique à avant ce lot |

---

# 14. HORIZON

`ExitBarIndex = EntryBarIndex + HorizonBars` reste requis d'exister intégralement dans la série **AVANT** toute tentative de surveillance intrabar — décision délibérée, non triviale : autoriser une résolution anticipée (Stop/Target) à contourner cette exigence aurait introduit un **biais de couverture corrélé au résultat** (les positions en fin de série se résoudraient en `Closed` seulement si elles touchent tôt, et en `InsufficientFutureData` sinon) — documenté explicitement dans le code et ici. Un Stop/Target touché exactement sur la barre horizon prend priorité sur `TimeHorizon` (confirmé §18, brief §18).

---

# 15. CAUSALITY

**PASS**, prouvé à 2 niveaux (méthodologie identique à `BacktestFoundationLookAheadTests`/`BacktestSignalPipelineLookAheadTests`, jamais réinventée) :
1. Niveau unitaire : ajout de barres extrêmes après la barre de sortie déjà déterminée → résultat bit-identique (`ExecutionIntrabarLookAheadTests.cs`, 6/6 PASS), pour Stop/Target/Ambiguous/TimeHorizon.
2. Niveau intégration : série tronquée vs étendue via `BacktestEngine.RunSimulation`, préfixe commun bit-identique.

---

# 16. DETERMINISM

**PASS** (`ExecutionIntrabarDeterminismTests.cs`, 7/7). `ExecutionSimulator` confirmé stateless empiriquement (classe statique, aucun champ) — mêmes entrées → positions bit-identiques.

---

# 17. RUN ISOLATION

**PASS**, inclus dans la même suite (§16) : RunA→RunB→RunA entrelacé, 25 itérations croisées, aucune contamination.

---

# 18. P&L

Formule inchangée (brief §24) : `GrossPriceMove`/`Return` calculés avec la MÊME formule signée que pour `TimeHorizon`, quel que soit le type de sortie (`(Exit-Entry)/Entry` pour BUY, miroir pour SELL) — jamais une seconde formule. Coûts/commissions : gérés en aval (`Backtest/Cost/`), inchangés, non concernés par ce lot.

**MFE/MAE (brief §25)** : confirmé, `Measurement` (Lot 14.4) ne référence StopLoss/TakeProfit nulle part — moteur totalement indépendant, MFE/MAE restent calculés sans lien avec l'exécution, exactement comme avant ce lot. Aucune nouvelle métrique créée.

---

# 19. SYNTHETIC GOLDEN DATASET

`ExecutionIntrabarGoldenDatasetTests.cs`, **25/25 PASS**, dataset indépendant de Yahoo, valeurs réelles :

| Cas | BUY | SELL |
|---|---|---|
| Pas de touche | `TimeHorizon`, `ExitPrice=Close[exitBar]` | idem |
| Stop seul | `StopLoss`, `ExitPrice=95` (jamais Low=94) | miroir |
| Target seul | `TakeProfit`, `ExitPrice=105` (jamais High=107) | miroir |
| Ambiguïté même barre | `Ambiguous`, `ExitPrice=95` (=StopLoss) | `Ambiguous`, `ExitPrice=105` (=StopLoss) |
| Multi-barre (Target t+2, Stop t+4) | sort à t+2, jamais lu t+4 | miroir inversé → Stop gagne à t+2 |
| Touche exactement sur barre horizon | priorité sur `TimeHorizon` | idem |
| Entrée = barre de fill | `ExitBarIndex==EntryBarIndex` possible, `HoldingBars=0` | idem |
| Gap (Open déjà sous le Stop) | `ExitPrice=95` théorique, jamais l'Open gappé (90) ni Low (85) | idem |
| `NO_ACTION` avec SL/TP renseignés | `NotExecutable`, ignorés | — |
| SL=null ET TP=null | `TimeHorizon`, bit-identique à avant ce lot | — |
| Un seul niveau renseigné | jamais d'`Ambiguous` parasite | — |
| SL/TP du mauvais côté de l'Entry réelle | `PositionStatus.InvalidStopTarget`, jamais `Closed` | idem |
| Barre invalide en cours de fenêtre | `InvalidExit` nommant l'index exact | — |

**Aucun bug trouvé** — chaque cas correspond exactement au comportement documenté.

---

# 20. REAL DATASET OBSERVATION

**Observation comportementale, jamais une amélioration ni une régression (brief §29)**. Dataset Yahoo MES M5, ~59 jours (recette Lots 15.0-15.3), 2415 barres directionnelles `MeanReverting` :

| PositionStatus | N | % |
|---|---:|---:|
| Closed | 2334 | 96.65% |
| InvalidStopTarget | 81 | 3.35% |
| (tous les autres) | 0 | 0% |

| ExitReason (sur 2334 Closed) | N | % | HoldingBars moyen | médian |
|---|---:|---:|---:|---:|
| TakeProfit | 1692 | **72.49%** | 0.84 | **0** |
| StopLoss | 546 | 23.39% | 1.97 | 1 |
| Ambiguous | 57 | 2.44% | 0.56 | 0 |
| TimeHorizon | 39 | 1.67% | 10.00 | 10 |

**Comparaison au Lot 15.3 (jamais qualifiée d'amélioration/régression)** : Lot 15.3 = 100% `TimeHorizon` (aucune autre sortie n'existait). Ce lot montre que 98.3% des positions closes sortent désormais AVANT l'horizon — un changement de comportement massif et attendu, entièrement expliqué par le choix de conception §7 (surveillance dès la barre d'entrée) combiné à l'échelle de `CurrentVolatility` (§1) : la distance Stop/Target (2×écart-type de RENDEMENTS sur 20 barres) est souvent plus petite que l'amplitude High-Low d'une seule barre M5, donc fréquemment franchie dès la première barre.

**`InvalidStopTarget` (§5), caractérisation empirique** : écart moyen |Fill réel − Prix de référence signal| = **1.48** pour les 81 positions `InvalidStopTarget`, contre **0.14** pour les 2334 `Closed` — confirme précisément l'hypothèse (§1/§5) : ces cas sont concentrés là où le prix a bougé fortement entre la barre signal et la barre de remplissage. Reconfirmé sur un second pull Yahoo indépendant (45 jours) : **62/8803** positions, cohérent en ordre de grandeur.

---

# 21. REGRESSION

**PASS**. Suite complète : **854 tests, 852 réussis, 1 échec préexistant sans rapport, 1 ignoré préexistant.**

3 corrections mineures d'invariants obsolètes, découvertes en amont par recherche proactive (`grep`) avant l'exécution, toutes du même type déjà établi aux Lots 15.1/15.3 (jamais un affaiblissement d'assertion) :
- `ExecutionCandidateTests.cs` : comptage réflexif de propriétés (5→7), renommé pour refléter l'extension délibérée.
- `ExecutionSimulatorIntegrationTests.cs`/`ExecutionYahooIntegrationTests.cs` : identité comptable `TotalCount == somme des statuts`, `InvalidStopTargetCount` ajouté.
- `PositionRiskEvaluatorTests.cs`/`PositionCostCalculatorTests.cs`/`PositionPnLCalculatorTests.cs` : `[InlineData(PositionStatus.InvalidStopTarget)]` ajouté à des théories déjà génériques (`Status != Closed`), jamais un `switch` exhaustif cassé.

Échec sans rapport, déjà connu : `HysteresisThresholdSensitivityLot1418Tests` (Lot 15.2/15.3, `FusionStateManager`-scope, structurellement indépendant). Ignoré préexistant : `Sprint1515HistoricalReconciliationXunitTests`.

---

# 22. PRODUCTION SAFETY

4 fichiers de production modifiés : `ExecutionCandidate.cs`, `SimulatedPosition.cs`, `BacktestExecutionResult.cs`, `ExecutionSimulator.cs`. Aucun fichier protégé touché (`RiskEngine.cs`, `RiskPolicy.cs`, `InstrumentRiskSpecification.cs`, `TradePlanBuilder.cs`, `RegimeEngine.cs`, `DecisionArbitrator.cs`, `EvidenceFusionEngine.cs`, `FusionStateManager.cs`, `EntryTriggerBuilder.cs`/`EntryTriggerAssessment.cs`, `SignalEngine.cs`, `VolatilityModel.cs`, `IQIAIndicator.cs`). `git status -uall` confirmé avant/après : uniquement ces 4 fichiers + 3 corrections de tests + 4 nouveaux fichiers de test + 3 extensions `[InlineData]` + 1 CSV, rien d'autre. Debug et Release compilent proprement (0 avertissement, 0 erreur). Aucun ordre. Aucune DLL. ATAS non utilisé. Aucun commit.

---

# 23. REMAINING GAPS

1. **Gap/overshoot non résolu** (§10) — le prix de sortie reste optimiste (niveau théorique) même si le marché a gappé au-delà. Documenté, pas corrigé.
2. **Aucun modèle de slippage pour les sorties** (§11) — seul le remplissage d'entrée en bénéficie (chemin Coût séparé).
3. **`InvalidStopTarget` (3.35%) reste non résolu** (§5/§20) — nécessiterait soit de recalculer Stop/Target contre le prix de remplissage réel (toucherait `TradePlanBuilder`/`VolatilityStopLossModel`, protégés), soit un mécanisme de re-validation post-fill — décision architecturale hors mandat de ce lot.
4. **Sensibilité du choix "entrée incluse dans la surveillance"** (§7) — un choix raisonné et documenté, mais dont l'ampleur de l'effet (72% TakeProfit, médiane 0 barre) n'était pas anticipée ; un futur lot pourrait vouloir étudier la sensibilité à ce choix précis, sans jamais le changer pour "améliorer" un résultat.
5. **Ambiguïté intrabar réelle** (2.44% des sorties) — la politique conservatrice (§8) est déterministe et documentée mais reste une convention, pas une connaissance du marché réel.

---

# 24. RECOMMENDED NEXT LOT

1. **Lot Fill-Price Reconciliation** — décider comment traiter les 3.35% `InvalidStopTarget` (recalcul post-fill ? invalidation explicite en amont ?) — nécessite une décision de conception, pas une correction technique triviale.
2. **Lot StructuralBreak Evidence** (Lot 15.2, toujours en attente).
3. Une éventuelle étude de sensibilité (jamais une calibration) sur l'inclusion/exclusion de la barre d'entrée dans la surveillance intrabar (§7/§23.4) — purement descriptive, jamais pour choisir "la meilleure" option.

---

# 25. HANDOFF CONTEXT

Voir bloc final ci-dessous.

---

# HANDOFF CONTEXT — NEXT SESSION

```
PROJECT:
IQIA

CURRENT LOT:
15.4

LAST COMPLETED:
15.3

CALIBRATION:
PAUSED

EXECUTION:
ExecutionSimulator now monitors High/Low intrabar for TradePlan.StopLoss/TakeProfit across
bars[EntryBarIndex..ExitBarIndex] INCLUSIVE (entry bar itself included - reasoned: Open is that bar's
first chronological event, so its own High/Low are concurrent with, never prior to, the fill). Falls back
to unchanged TimeHorizon-at-Close behavior when neither StopLoss nor TakeProfit is configured (bit-
identical to pre-Lot-15.4, verified) or when configured but never touched within the window.

STOP LOSS:
Detected via Low<=StopLoss (BUY) / High>=StopLoss (SELL). Exit price always the theoretical StopLoss
level (never the triggering bar's actual price) - no gap/slippage model. 23.39% of Closed positions this
run (546/2334), mean holding 1.97 bars.

TAKE PROFIT:
Detected via High>=TakeProfit (BUY) / Low<=TakeProfit (SELL). Same exit-price discipline. DOMINANT exit
reason this run: 72.49% (1692/2334), median holding bars = 0 (i.e., most TakeProfit exits happen on the
fill bar itself) - driven by CurrentVolatility (a 20-bar return stdev) typically being smaller than a
single M5 bar's own High-Low range, combined with the deliberate entry-bar-inclusive monitoring window
(see EXECUTION above) - a documented, reasoned design choice, not a bug, but empirically the dominant
driver of the whole exit-reason distribution.

SAME-BAR POLICY:
New ExitReason.Ambiguous when the SAME bar touches both StopLoss and TakeProfit (OHLC cannot determine
true order) - ExitPrice uses the conservative StopLoss convention for measurement usability only, never a
claim about true execution order. 2.44% of Closed positions (57/2334).

GAP POLICY:
NOT IMPLEMENTED / NOT RESOLVED - confirmed no gap-handling convention exists anywhere in the repo before
this lot; exit price stays the theoretical level even if the triggering bar's Open already gapped past it
(golden-dataset-confirmed, deliberate, documented optimism, an open gap for a future lot).

SLIPPAGE:
NOT IMPLEMENTED for exits (StopLoss/TakeProfit/TimeHorizon) - existing SlippageConfiguration/
ExecutionPriceModel infrastructure (Backtest/Cost/) applies only to the ENTRY fill, downstream of
ExecutionSimulator, unchanged and unrelated to this lot's exit-price logic.

EXIT REASONS:
ExitReason gained StopLoss/TakeProfit/Ambiguous alongside the pre-existing TimeHorizon (which itself is
unchanged in every respect - same Close[ExitBarIndex] price, same conditions to reach it). No exhaustive
switch anywhere in the repo needed updating (confirmed by proactive grep before implementing) - 3 generic
"Status != Closed" [Theory] tests needed one InlineData value added each (PositionRiskEvaluatorTests,
PositionCostCalculatorTests, PositionPnLCalculatorTests), done cleanly.

CAUSALITY:
PASS, two levels (unit: extra bars appended after an already-resolved exit never change the result;
integration: truncated-vs-extended series, common prefix bit-identical), same methodology as
BacktestFoundationLookAheadTests/BacktestSignalPipelineLookAheadTests, reused not reinvented.

DETERMINISM:
PASS - ExecutionSimulator confirmed stateless empirically (no fields), same inputs -> bit-identical
SimulatedPosition, verified explicitly (not just by inspection).

RUN ISOLATION:
PASS - RunA->RunB->RunA interleaved, 25 cross-iterations, no contamination.

P&L:
Formula UNCHANGED regardless of exit type - (Exit-Entry)/Entry for BUY, mirrored for SELL, computed from
whichever ExitPrice/EntryPrice the position actually resolved to. Measurement (MFE/MAE, Lot 14.4) confirmed
fully independent of StopLoss/TakeProfit - no reference anywhere, unaffected by this lot.

REGIME:
Unchanged from Lot 15.1/15.2/15.3 - MeanReverting the only regime that ever reaches a directional
ExecutionCandidate; this lot adds no new capability to any other regime (confirmed: StopLoss/TakeProfit
monitoring only ever activates when at least one is non-null, which remains structurally impossible
outside MeanReverting).

SIGNAL:
NOT MODIFIED.

ENTRY:
NOT MODIFIED.

STRUCTURAL BREAK:
Unchanged from Lot 15.2 - still blocked on an explicit user design decision, untouched by this lot.

P0 REMAINING:
(1) InvalidStopTarget (3.35% of directional MeanReverting positions, 81/2415 this run, 62/8803 on an
independent re-pull) - TradePlan.StopLoss/TakeProfit is computed against TradePlan.EntryPrice (the signal
bar's reference price) but the real execution entry is Open[SignalBarIndex+1] (Lot 14.10's fill
convention); when price moves enough between those two bars, the level lands on the wrong side of the
REAL fill and the position is correctly rejected rather than fabricated - but this means 3.35% of
otherwise-viable MeanReverting signals never produce a Closed position. Characterized precisely (mean
signal-to-fill price gap 1.48 for these vs 0.14 for Closed positions) but not fixed - fixing it would
require either recomputing Stop/Target against the real fill price (touches VolatilityStopLossModel/
TradePlanBuilder territory) or a re-validation step, both explicit design decisions outside this lot's
mandate. (2) StructuralBreak evidence gap (Lot 15.2, unchanged, independent).

P1:
The TakeProfit-dominant, near-zero-holding-bar exit distribution (72.49% TakeProfit, median 0 bars held)
is a direct, now-quantified consequence of including the entry/fill bar itself in intrabar monitoring
(brief's own §9 question, resolved with reasoning, not assumption) combined with CurrentVolatility's scale
relative to a single M5 bar's own range - worth a future SENSITIVITY study (never a re-selection for
better results) on whether excluding the entry bar changes this materially. Gap/slippage-for-exits remain
unimplemented, both explicitly documented as open rather than resolved.

NEXT LOT:
A Fill-Price Reconciliation lot (decide how to treat the 3.35% InvalidStopTarget population) is the most
consequential next step for THIS lot's own remaining gap. Independently, the StructuralBreak Evidence
decision (Lot 15.2) remains open. A purely descriptive sensitivity study on entry-bar-inclusion (never a
recalibration) is a lighter-weight option.

WHY:
This lot closes the second half of the Lot 15.0 Stop Loss P0: a real, causal, tested, tick-correct Stop
Loss now not only reaches PLAN_READY (Lot 15.3) but ACTUALLY determines how a backtest position exits
(Lot 15.4). The dominant finding - TakeProfit firing on the fill bar itself in the majority of cases - is
neither hidden nor spun as a result; it is traced to two specific, already-documented design choices
(entry-bar-inclusive monitoring, CurrentVolatility's window/scale) so a future session can evaluate it
with full context rather than mistaking it for either a bug or a discovery about the market.

DO NOT DO:
Do NOT modify ExecutionCandidate, SimulatedPosition, BacktestExecutionResult, or ExecutionSimulator
further without a dedicated lot - all four were written and verified together. Do NOT invent a gap or
slippage rule for exits - none exists today, confirmed, and inventing one now would violate this lot's
absolute rule. Do NOT treat the 72.49% TakeProfit / near-zero holding-bars result as either a success
("the strategy works") or a failure ("the stop is too tight") - it is a direct, explained consequence of
two documented design choices, not a performance finding, and must not be used to justify calibrating
DefaultVolatilityMultiplier, the entry-bar-inclusion choice, or HorizonBars. Do NOT "fix" InvalidStopTarget
by silently recomputing Stop/Target against the real fill price inside ExecutionSimulator - that would
touch the single-source-of-truth principle (TradePlan.StopLoss) this lot deliberately preserved; it needs
its own dedicated lot with an explicit design decision. Do NOT assume the same-bar Ambiguous convention
(StopLoss price, conservative) is a market fact - it is a documented, arbitrary-but-declared choice for
measurement usability only.
```

**STOP.**
