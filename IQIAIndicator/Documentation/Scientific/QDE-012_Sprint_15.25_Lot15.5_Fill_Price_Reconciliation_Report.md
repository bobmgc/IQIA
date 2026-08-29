# QDE-012 — Sprint 15.25 — Lot 15.5 — Fill-Price Reconciliation

**Date** : 2026-08-24/26
**Branche** : `feature/structural-stability-v2`
**Lot précédent** : 15.4
**Statut** : **IMPLEMENTED**
**Type** : **DÉCISION D'ARCHITECTURE SCIENTIFIQUE — AUCUNE CALIBRATION, AUCUNE OPTIMISATION**

---

# 1. EXECUTIVE SUMMARY

Le Lot 15.4 a découvert que `TradePlan.StopLoss`/`TakeProfit` — calculés contre le prix de référence de la barre signal — peuvent atterrir du mauvais côté du prix de remplissage RÉEL (`Open[SignalBarIndex+1]`, convention Lot 14.10), rejetant 3.35% des positions directionnelles `MeanReverting` (`PositionStatus.InvalidStopTarget`). Ce lot répond à la question posée explicitement : **quel doit être le contrat scientifique entre prix de référence, prix de remplissage réel, Stop Loss et Take Profit ?**

**Décision retenue, après comparaison rigoureuse de 5 options (§8-12), justifiée exclusivement par la causalité, la cohérence architecturale et la source unique de vérité — jamais par la performance** : **Option D — préserver la DISTANCE, jamais le niveau absolu.**

`CurrentVolatility` (Lot 15.3) est fondamentalement une mesure de DISPERSION — une distance — jamais un niveau de prix absolu. La formule de réconciliation :

```
StopDistance  = |SignalReferencePrice - TradePlan.StopLoss|      (déjà > 0 par construction, Lot 15.3)
TargetDistance = |SignalReferencePrice - TradePlan.TakeProfit|    (idem)

StopLossForExecution   = RealFillPrice ∓ StopDistance
TakeProfitForExecution = RealFillPrice ± TargetDistance
```

`TradePlan.StopLoss`/`.TakeProfit` eux-mêmes **ne sont jamais modifiés** — la réconciliation est une dérivation fraîche, formelle, documentée, calculée dans `ExecutionSimulator` (le seul endroit où le prix de remplissage réel est connu), jamais une seconde source de vérité silencieuse.

**Découverte de vérification, non anticipée avant l'implémentation** : la formule naïve (`Math.Abs` seul) masquait aussi un `TradePlan` authentiquement malformé dès l'origine (Stop du mauvais côté de son PROPRE prix de référence — un cas qui ne devrait jamais se produire, mais que le Lot 15.4 avait explicitement pour rôle de détecter). Corrigé en ajoutant un contrôle de cohérence **préalable**, contre le prix de référence du signal, **avant** toute réconciliation — préservant intégralement le garde-fou du Lot 15.4 pour ce cas distinct.

**Résultat empirique, confirmé sur données réelles fraîches (2436 barres directionnelles `MeanReverting`)** :
- `InvalidStopTarget` : **3.35% (Lot 15.4) → 0% (Lot 15.5)** — le phénomène de l'écart signal→fill est éliminé structurellement, pas par calibration.
- Identité de distance vérifiée empiriquement (pas seulement par construction) : **2398/2398 correspondances exactes, écart maximal = 0** entre la distance post-réconciliation et la distance d'origine.
- Distribution des raisons de sortie quasi inchangée (StopLoss 23.39%→22.54%, TakeProfit 72.49%→73.03%, Ambiguous 2.44%→2.87%, TimeHorizon 1.67%→1.56%) — cohérent avec l'attente que la réconciliation ne fait que RÉCUPÉRER la population auparavant rejetée, sans redistribuer le reste.
- Les 81 positions auparavant `InvalidStopTarget` se répartissent maintenant : TakeProfit 64 (79.0%), StopLoss 13 (16.0%), Ambiguous 4 (4.9%), TimeHorizon 0.

**Régression : PASS.** Suite complète : **898 tests, 896 réussis, 1 échec préexistant sans rapport (déjà connu), 1 ignoré préexistant.** Un seul fichier de production modifié (`ExecutionSimulator.cs`). Aucun test préexistant n'a nécessité de correction.

---

# 2. LOT 15.4 CONTEXT

`ExecutionSimulator` consomme désormais `TradePlan.StopLoss`/`.TakeProfit` intrabar. `PositionStatus.InvalidStopTarget` (nouveau à ce lot) rejette explicitement — jamais silencieusement — toute position dont le Stop/Target est du mauvais côté du prix de remplissage réel. Mesuré : 81/2415 (3.35%), reconfirmé 62/8803 (0.70%) sur un pull indépendant, corrélé à l'écart |Fill réel − Prix de référence signal| (1.48 vs 0.14).

---

# 3. CURRENT ENTRY CONTRACT

Cartographie complète (brief §1), confirmée par lecture de code :

```
A = SignalReferencePrice  = EntryTriggerCandidate.CurrentPrice
                          = ScientificMarketContext.CurrentPrice (BuildScientificMarketContext)
                          = Close[SignalBarIndex]  (le dernier prix connu au moment de la décision)

B = TradePlan.EntryPrice  = A  (TradePlanBuilder.Build, Phase 3 : "the only real price the pipeline offers today")
    TradePlan.StopLoss    = VolatilityStopLossModel.Resolve(direction, A) = A ∓ 2×CurrentVolatility, tick-arrondi vers A
    TradePlan.TakeProfit  = EstimatedEquilibrium (Kalman), si cohérent avec A

C = RealFillPrice         = Open[SignalBarIndex + 1]  (ExecutionSimulator.SimulateCore, convention Lot 14.10)
```

`A` et `B` (le prix de référence) sont **identiques par construction** — `TradePlan.EntryPrice` n'est PAS une troisième valeur distincte, c'est littéralement `A`. Le seul écart réel est entre `A`/`B` (fixés au moment du signal) et `C` (connu une barre plus tard).

---

# 4. SIGNAL REFERENCE PRICE

`A = Close[SignalBarIndex]` — le dernier prix RÉELLEMENT connu au moment où la décision (Regime→Fusion→Decision→Signal→Entry→EntryTrigger) est prise. Ce n'est ni une erreur ni une approximation : dans un système live, c'est LA SEULE information disponible à cet instant — aucun système, live ou backtest, ne peut connaître `Open[SignalBarIndex+1]` avant que cette barre n'existe.

---

# 5. TRADEPLAN ENTRY PRICE

Confirmé par lecture directe (`TradePlanBuilder.cs`, Phase 3) : `TradePlan.EntryPrice = context.EntryTriggerCandidate.CurrentPrice` — **exactement** `A`, jamais une valeur distincte, jamais le `Open` futur. `TradePlanBuilder` construit un plan **au moment du signal**, honnêtement, à partir de ce qui est connu à cet instant — c'est cohérent avec son propre docstring ("Produced only for observability").

---

# 6. REAL FILL PRICE

`ExecutionSimulator.SimulateCore` : `entryPrice = bars[SignalBarIndex+1].Open` — établi par le Lot 14.10 (P0-1) précisément parce qu'un ordre réel ne peut honnêtement s'exécuter à `Close[SignalBarIndex]`. `TradePlan.EntryPrice` est lu une fois comme garde-fou de qualité des données (jamais comme prix tradé) — confirmé, comportement inchangé par ce lot.

---

# 7. ROOT CAUSE

**La séparation entre `A`/`B` et `C` est délibérée et documentée (Lot 14.10) — ce n'est pas la cause du problème.** La cause précise : `VolatilityStopLossModel`/`TradePlanBuilder` calculent `StopLoss`/`TakeProfit` en **niveaux absolus** ancrés sur `A`, puis ces niveaux absolus étaient utilisés **tels quels** par `ExecutionSimulator` (Lot 15.4) sans jamais être reconciliés avec `C` — une omission, jamais une intention documentée. Aucune trace dans le dépôt n'indique que cette séparation ait été anticipée avant le Lot 15.4 lui-même.

---

# 8. OPTION A (statu quo Lot 15.4)

Conserver les niveaux absolus, rejeter après fill si incohérent.

**Avantages** : le plus simple, déjà implémenté, jamais un niveau exécuté qui ne corresponde pas exactement à ce que `VolatilityStopLossModel` a calculé.
**Inconvénients** : perd 3.35% de signaux par ailleurs valides, uniquement à cause d'un artefact de timing (le décalage d'une barre entre `A` et `C`), pas d'un défaut du signal lui-même. Le taux de perte dépend de la volatilité intrabar entre `A` et `C` — un phénomène de MARCHÉ, pas de STRATÉGIE, qui n'a aucune raison scientifique de faire échouer une décision par ailleurs valide.
**Verdict** : causalement correct mais économiquement/scientifiquement insatisfaisant — rejette sur la base d'un artefact de timing, pas d'un défaut de signal.

---

# 9. OPTION B (recalcul après fill)

Différer le calcul de `StopLoss`/`TakeProfit` jusqu'à ce que `C` soit connu.

**Analyse (brief §17 Live/Backtest)** : en BACKTEST, `C` est déjà disponible dans le tableau de barres — trivial à implémenter. Mais en LIVE, `C` n'existe PAS au moment où `TradePlan` serait construit — un système live ne peut connaître son propre prix de remplissage avant que l'ordre ne soit exécuté et confirmé. Implémenter Option B exigerait de restructurer fondamentalement QUAND `TradePlan` est construit (après confirmation de fill, pas au moment du signal) — un changement architectural majeur touchant `TradePlanBuilder`, la séquence même du pipeline (`Signal→Entry→EntryTrigger→TradePlan` deviendrait `Signal→Entry→EntryTrigger→[attente fill]→TradePlan`), et n'aurait tout simplement pas d'équivalent live sans un mécanisme de callback asynchrone post-exécution — hors du mandat "modification minimale" de ce lot.
**Verdict : REJETÉE** — architecturalement incompatible avec la symétrie live/backtest exigée (brief §17), pas seulement plus complexe.

---

# 10. OPTION C (nouveaux champs explicites)

Séparer `SignalReferencePrice`/`ExecutionEntryPrice` en champs distincts.

**Analyse** : cette séparation **existe déjà** — `ExecutionCandidate.EntryPrice` (=`A`) et `SimulatedPosition.EntryPrice` (=`C`) sont déjà deux champs distincts dans deux records distincts (confirmé §3). Créer un troisième concept serait une abstraction sans nécessité démontrée (brief §4 : "ne pas créer une abstraction simplement parce qu'elle semble plus propre").
**Verdict : REJETÉE** — le modèle de données actuel est déjà suffisant ; le problème n'est pas un manque de champs, c'est une absence de RECONCILIATION entre deux champs déjà distincts.

---

# 11. OPTION D (translation par distance) — RETENUE

Voir §1 pour la formule. **Analyse détaillée :**

**Cohérence scientifique** : `CurrentVolatility` (Lot 15.3) est un écart-type de RENDEMENTS — une mesure de dispersion, donc une DISTANCE par nature. Un niveau de prix absolu dérivé d'une distance n'a de sens que relativement à SON point d'ancrage ; changer le point d'ancrage (de `A` à `C`) tout en préservant la distance est la transformation qui respecte le mieux la nature mathématique de la grandeur d'origine.
**Causalité (brief §6/§14)** : la distance est figée au moment du signal (`A`, `TradePlan.StopLoss`/`.TakeProfit`, tous déjà connus) ; `C` est déjà légitimement utilisée pour le remplissage lui-même (même barre, aucune donnée nouvelle). Aucun look-ahead.
**Source unique de vérité (brief §14)** : `TradePlan.StopLoss`/`.TakeProfit` restent **inchangés** dans `TradePlan` lui-même — la réconciliation est une dérivation FORMELLE, documentée, calculée une seule fois, jamais une seconde définition silencieuse.
**Élimine structurellement le problème** : `StopLossForExecution` est TOUJOURS du bon côté de `C`, par construction (`RealFillPrice ∓ |distance positive|`), pour toute distance strictement positive — ce qui est déjà garanti à la source (Lot 15.3).

**Risque identifié et corrigé pendant la vérification** : préserver la distance via `Math.Abs()` masque un `TradePlan.StopLoss` déjà malformé RELATIVEMENT à `A` (un cas qui ne devrait jamais se produire, mais que le garde-fou du Lot 15.4 existait pour détecter). **Corrigé** : un contrôle de cohérence est effectué contre `A` (`referencePrice`) **avant** toute réconciliation — si `TradePlan.StopLoss`/`.TakeProfit` est déjà du mauvais côté de `A`, la position est rejetée `InvalidStopTarget` avec un message explicite ("malformed before any fill-price reconciliation was even attempted"), **avant** que la formule de distance ne puisse la "corriger" silencieusement. Un second contrôle (post-réconciliation, hérité du Lot 15.4) reste en place par défense en profondeur, désormais structurellement toujours vrai si le premier contrôle passe — confirmé empiriquement (§21).

**Verdict : RETENUE.**

---

# 12. OPTION E

Recherche exhaustive dans le dépôt (brief §4.E) : aucune autre architecture de réconciliation prix-signal/prix-fill n'existe nulle part ailleurs dans le code (`Backtest/Cost/ExecutionPriceModel` gère slippage/spread sur le remplissage d'ENTRÉE, un concept orthogonal, jamais appliqué à Stop/Target). Rien à documenter au-delà de A-D.

---

# 13. SCIENTIFIC COMPARISON

| Critère | A (statu quo) | B (recalcul post-fill) | C (nouveaux champs) | D (distance, retenue) |
|---|---|---|---|---|
| Causalité | OK | OK en backtest, impossible en live | OK | OK |
| Cohérence architecturale | OK mais perd des signaux | Restructure tout l'ordre du pipeline | Redondant (déjà existant) | Minimal, un seul fichier |
| Source unique de vérité | OK | OK | OK | OK (dérivation formelle) |
| Symétrie live/backtest | OK | **Rompue** | OK | OK |
| Cohérence BUY/SELL | OK | OK | OK | OK, prouvé (§16) |
| Élimine le problème | Non (le documente) | Oui, mais au prix de B | N/A | **Oui, structurellement** |
| Complexité | Aucune | Élevée | Aucune (déjà là) | Faible |

---

# 14. CAUSALITY

**PASS**, prouvé (`FillPriceReconciliationLookAheadTests.cs`, 6/6) : les niveaux réconciliés à la barre de fill ne dépendent d'aucune barre postérieure — mêmes 2 niveaux de preuve que les lots précédents (unitaire : barres futures extrêmes ajoutées après la sortie déjà déterminée → résultat bit-identique ; intégration : série tronquée vs étendue).

---

# 15. LOOK-AHEAD

Voir §14 — confondu intentionnellement, même preuve.

---

# 16. BUY/SELL SYMMETRY

**PASS** (`FillPriceReconciliationSymmetryTests.cs`, 6/6) : chaque cas BUY a un miroir SELL testé explicitement (petit déplacement, gap favorable, gap défavorable, extrême) — confirmé qu'aucun miroir ne diverge en catégorie de statut (jamais un `Closed` d'un côté et un `InvalidStopTarget` de l'autre pour une configuration symétrique).

---

# 17. RISK INTERACTION

`RiskPerUnit`/`PositionSize`/`RiskAmount` (`TradePlanBuilder`, inchangé) restent calculés contre `A` (le prix de référence du signal), **jamais** contre `C` (le fill réel) ni contre les niveaux réconciliés de ce lot — une limite déjà identifiée au Lot 15.3 ("TradePlan.PositionSize doit être lu comme une estimation légère, jamais autoritaire"), non aggravée ni corrigée par ce lot (`TradePlanBuilder`/`RiskEngine` protégés, aucune nécessité démontrée de les toucher — la réconciliation ne change QUE ce qu'`ExecutionSimulator` surveille pour la sortie, jamais le sizing d'entrée).

---

# 18. POSITION SIZING

`StopDistance` avant fill (utilisée pour `RiskPerUnit`/`PositionSize` dans `TradePlan`) reste la distance ORIGINALE (contre `A`) — **identique** à la distance après réconciliation par construction (§11, confirmé empiriquement §21 : 2398/2398 correspondances exactes). Le `PositionSize` déjà calculé au moment du `TradePlan` reste donc une **estimation valide** de la distance de risque réellement surveillée à l'exécution — la réconciliation ne change QUE l'ancrage absolu, jamais la magnitude, donc `PositionSize` n'est ni obsolète ni dangereux de ce point de vue précis (la limite plus générale déjà documentée au Lot 15.3, absence de plafond de quantité, reste inchangée et non traitée ici).

---

# 19. LIVE/BACKTEST

La formule de réconciliation ne dépend que de valeurs qu'un système live connaîtrait EXACTEMENT au même moment qu'un backtest : `A`/`TradePlan.StopLoss`/`.TakeProfit` (fixés au signal) et `C` (connu dès que le remplissage a lieu, live ou backtest). Aucune astuce spécifique au backtest. `IQIAIndicator.cs` **non câblé** (comme pour tous les lots précédents) — la logique vit entièrement dans `ExecutionSimulator.cs` (Backtest), pas dans un composant `Engine.*` partagé cette fois (contrairement à `VolatilityStopLossModel` au Lot 15.3) car elle a spécifiquement besoin de `C`, qui n'existe structurellement que côté Execution.

---

# 20. REAL DATASET CHARACTERIZATION

Dataset Yahoo MES M5, ~59 jours, 2436 barres directionnelles `MeanReverting` (comparable au N=2415 du Lot 15.4).

**Écart |Prix de référence signal − Prix de remplissage réel|** :

| Groupe | N | Mean | Médiane | P10 | P25 | P75 | P90 | Max |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| Tous | 2436 | 0.1830 | 0.25 | 0.00 | 0.00 | 0.25 | 0.25 | 45.25 |
| BUY | 1221 | 0.1732 | 0.25 | 0.00 | 0.00 | 0.25 | 0.25 | 18.75 |
| SELL | 1215 | 0.1928 | 0.00 | 0.00 | 0.00 | 0.25 | 0.25 | 45.25 |
| Mouvement favorable | 596 | 0.3733 | 0.25 | 0.25 | 0.25 | 0.25 | 0.25 | 45.25 |
| Mouvement défavorable | 1840 | 0.1213 | 0.00 | 0.00 | 0.00 | 0.25 | 0.25 | 18.75 |

La médiane (0.25 = un tick MES) confirme que la majorité des écarts sont minimes — mais la queue (max=45.25, 181 ticks) explique la queue des cas `InvalidStopTarget` du Lot 15.4.

**Vérification empirique de l'invariant central (§11)** : sur les niveaux réellement utilisés (`ExitReason` = StopLoss/TakeProfit/Ambiguous, N=2398), la distance post-réconciliation `|ExitPrice − RealFillPrice|` correspond **exactement** à la distance d'origine `|A − TradePlan.Level|` dans **2398/2398 cas (100%), écart maximal = 0**. Ce n'est pas une preuve "par construction" seulement — c'est confirmé sur le comportement réel compilé.

---

# 21. INVALIDSTOPTARGET

| | N | InvalidStopTarget | % |
|---|---:|---:|---:|
| Lot 15.4 (premier pull) | 2415 | 81 | 3.35% |
| Lot 15.4 (pull indépendant) | 8803 | 62 | 0.70% |
| **Lot 15.5 (ce lot)** | **2436** | **0** | **0%** |

**Devenir des 81 positions autrefois `InvalidStopTarget`** (répliqué sur ce run via l'ancienne logique Option A) : TakeProfit 64 (79.0%), StopLoss 13 (16.0%), Ambiguous 4 (4.9%), TimeHorizon 0 (0%) — toutes les 81 sont désormais résolues et surveillées, aucune n'a disparu dans un autre statut non lié.

**Distribution des raisons de sortie, comparaison purement descriptive (brief §29 — jamais une amélioration)** :

| | StopLoss | TakeProfit | Ambiguous | TimeHorizon |
|---|---:|---:|---:|---:|
| Lot 15.4 | 23.39% | 72.49% | 2.44% | 1.67% |
| Lot 15.5 | 22.54% | 73.03% | 2.87% | 1.56% |

Écarts faibles, cohérents avec l'attente que la réconciliation ne fait que récupérer la population auparavant rejetée sans redistribuer le reste.

---

# 22. DECISION GATE

**STATUS = IMPLEMENTATION JUSTIFIED.** Le contrat était suffisamment défini pour agir : Option D est causalement propre, architecturalement minimale (un seul fichier), préserve la source unique de vérité, symétrique BUY/SELL et live/backtest, et élimine structurellement (pas statistiquement) le phénomène identifié au Lot 15.4 — confirmé empiriquement à 0% sur données fraîches. **RECOMMANDATION : Option D, implémentée.**

---

# 23. IMPLEMENTATION

Un seul fichier de production modifié : `IQIAIndicator/Backtest/Execution/ExecutionSimulator.cs`. Aucun fichier protégé touché (`RiskEngine.cs`, `RiskPolicy.cs`, `InstrumentRiskSpecification.cs`, `TradePlanBuilder.cs`, `VolatilityStopLossModel.cs`, `RegimeEngine.cs`, `DecisionArbitrator.cs`, `EvidenceFusionEngine.cs`, `FusionStateManager.cs`, `EntryTriggerBuilder.cs`/`EntryTriggerAssessment.cs`, `SignalEngine.cs`, `ExecutionCandidate.cs`, `SimulatedPosition.cs`, `BacktestExecutionResult.cs`, `IQIAIndicator.cs`).

---

# 24. REGRESSION

**PASS**. Suite complète : **898 tests, 896 réussis, 1 échec préexistant sans rapport, 1 ignoré préexistant.** Aucun test préexistant n'a nécessité de correction cette fois (contrairement aux lots précédents) — le seul "échec" rencontré pendant le développement (4 tests du Lot 15.4 attendant `InvalidStopTarget`) a été résolu en **corrigeant ma propre implémentation** (ajout du contrôle préalable, §11), jamais en affaiblissant un test.

Échec sans rapport, déjà connu : `HysteresisThresholdSensitivityLot1418Tests` (`FusionStateManager`-scope, 3708/11430, signature identique aux lots précédents). Ignoré préexistant : `Sprint1515HistoricalReconciliationXunitTests`.

5 nouveaux fichiers de test (43 tests) + 1 test de caractérisation dataset réel : tous PASS.

---

# 25. PRODUCTION SAFETY

`git status -uall`/`git diff --stat` confirmés avant/après : 1 seul fichier de production modifié, 6 nouveaux fichiers de test + 1 CSV additifs, rien d'autre. Debug et Release compilent proprement (0 avertissement, 0 erreur). Aucun ordre. Aucune DLL. ATAS non utilisé. Aucun commit.

---

# 26. REMAINING GAPS

1. **`TradePlan.PositionSize`/`RiskPerUnit` restent ancrés sur `A`, jamais sur `C` ou les niveaux réconciliés** (§17/§18) — sans conséquence sur la MAGNITUDE du risque (identique par construction, confirmé §20), mais un futur lot pourrait vouloir exposer explicitement le prix de remplissage réel au calcul de sizing pour plus de transparence.
2. **`IQIAIndicator.cs` non câblé** — la logique de réconciliation vit uniquement dans `ExecutionSimulator.cs` (Backtest), pas dans un composant partagé — un système live devrait implémenter la même formule séparément (documentée ici précisément pour ça).
3. **Le gap/slippage à l'exécution reste non résolu** (Lot 15.4, §23.1/§23.2, inchangé) — orthogonal à ce lot.
4. **Les autres P0 hérités** (StructuralBreak Evidence, Lot 15.2) restent en attente d'une décision utilisateur, non affectés par ce lot.

---

# 27. RECOMMENDED NEXT LOT

1. **Lot StructuralBreak Evidence** (Lot 15.2, toujours en attente d'une décision explicite).
2. Un futur lot pourrait envisager d'exposer `RealFillPrice` au calcul de `PositionSize`/`RiskPerUnit` pour une transparence accrue (§26.1) — jamais pour "améliorer" un résultat, uniquement pour la cohérence documentaire.
3. Un lot Gap/Slippage à l'exécution (Lot 15.4, §23) reste ouvert, indépendant.

---

# 28. HANDOFF CONTEXT

Voir bloc final ci-dessous.

---

# HANDOFF CONTEXT — NEXT SESSION

```
PROJECT:
IQIA

CURRENT LOT:
15.5

LAST COMPLETED:
15.4

CALIBRATION:
PAUSED

ENTRY CONTRACT:
Fully mapped: SignalReferencePrice (A, =Close[SignalBarIndex], =TradePlan.EntryPrice) is set at signal
time; RealFillPrice (C, =Open[SignalBarIndex+1], Lot 14.10) is only known one bar later. TradePlan.
StopLoss/TakeProfit are computed against A (VolatilityStopLossModel/TradePlanBuilder, unmodified). No
third "planned entry price" concept exists or was needed - A and C were already two distinct, correctly
named existing fields (ExecutionCandidate.EntryPrice vs SimulatedPosition.EntryPrice).

SIGNAL REFERENCE:
A = Close[SignalBarIndex], the last price genuinely known at decision time - not an error, the
fundamental nature of any non-instantaneous execution system, live or backtest.

TRADEPLAN ENTRY:
Confirmed identical to A by direct code reading (TradePlanBuilder.Build Phase 3). Unchanged by this lot.

REAL FILL:
C = Open[SignalBarIndex+1], unchanged (Lot 14.10). Only known inside ExecutionSimulator, never earlier.

STOP LOSS:
TradePlan.StopLoss itself NEVER mutated (single source of truth). ExecutionSimulator now derives
stopLossForExecution = C -/+ |A - TradePlan.StopLoss| (distance preserved, re-anchored on C) - computed
fresh, documented, deterministic, no look-ahead.

TAKE PROFIT:
Symmetric: takeProfitForExecution = C +/- |A - TradePlan.TakeProfit|.

INVALID STOP TARGET:
Empirically eliminated for the "signal-to-fill gap" cause: 3.35% (Lot 15.4, N=2415) -> 0.70% (independent
pull, N=8803) -> 0% (Lot 15.5, N=2436, fresh pull). PositionStatus.InvalidStopTarget is RETAINED for a
narrower, now-primary purpose: catching a TradePlan.StopLoss/.TakeProfit already malformed relative to ITS
OWN referencePrice A (checked BEFORE any distance is derived, since Math.Abs() would otherwise silently
"fix" such a case rather than reject it) - structurally should never fire in practice given
VolatilityStopLossModel/TradePlanBuilder's own upstream guarantees, confirmed empirically (0 occurrences
on this run).

DECISION:
Option D (distance-preserving reconciliation), chosen over A (status quo, rejects on mismatch - keeps
losing viable signals to a timing artifact), B (recompute after fill - architecturally incompatible with
live, which cannot know its own fill before it happens), C (new explicit fields - unnecessary, the
distinction already existed in the data model), E (no other architecture found in the repo).

RATIONALE:
CurrentVolatility-derived Stop/Target are fundamentally a DISTANCE (dispersion) concept, not an
absolute-price concept - preserving the distance and re-anchoring it on the causally-known real fill price
is the scientifically coherent choice. Justified ONLY by causality/architecture/single-source-of-truth/
live-backtest-symmetry - NEVER by any PnL/win-rate/performance comparison, confirmed nothing of that kind
was consulted before deciding.

CAUSALITY:
PASS, 2 levels (unit + integration), identical methodology to prior lots.

LOOK-AHEAD:
PASS (same proof as CAUSALITY above).

DETERMINISM:
PASS.

RUN ISOLATION:
PASS.

RISK:
TradePlan.RiskPerUnit/PositionSize/RiskAmount remain anchored on A (TradePlanBuilder unmodified) - not on
the reconciled levels. No consequence to the MAGNITUDE of risk sized (identical by construction, confirmed
empirically: 2398/2398 exact distance matches, max delta=0) - the pre-existing Lot 15.3 caveat
("PositionSize is a lightweight estimate, never authoritative") remains the governing statement, not
worsened nor fixed by this lot.

POSITION SIZING:
StopDistance used for TradePlan-level sizing (against A) is numerically identical to the distance actually
monitored at execution (against C, post-reconciliation) - confirmed on real data, not just by construction.

LIVE/BACKTEST:
Reconciliation formula uses only values a live system would know at exactly the same moment as a backtest
(A/TradePlan.StopLoss/.TakeProfit fixed at signal time, C known as soon as the fill happens) - no
backtest-only shortcut. Lives entirely in ExecutionSimulator.cs (Backtest namespace) this time, not a
shared Engine.* component (unlike VolatilityStopLossModel, Lot 15.3) - it structurally needs C, which only
exists on the Execution side. IQIAIndicator.cs NOT wired (consistent with every prior lot's precedent).

STRUCTURAL BREAK:
Unchanged from Lot 15.2 - still blocked on an explicit user design decision, untouched by this lot.

P0 REMAINING:
(1) StructuralBreak evidence gap (Lot 15.2, unchanged, independent) - the only P0 remaining from the
entire Lot 15.0 audit lineage that has not yet been closed or explicitly deferred pending a user decision.
Both Stop Loss (Lot 15.3) and Execution Realism/Fill-Price Reconciliation (Lots 15.4/15.5) P0s from Lot
15.0 are now CLOSED.

NEXT LOT:
StructuralBreak Evidence (Lot 15.2) remains the sole open P0, pending an explicit user decision on the 3
design questions raised there (replace-vs-augment StructuralStability, BreakCount-vs-Confidence,
freshness handling) - this lot did not and could not resolve it, it is orthogonal to Execution/TradePlan.
Optionally, a lighter transparency lot could expose RealFillPrice to TradePlan-level sizing (never to
"improve" a result, purely for documentation coherence) or address the still-open Execution gap/slippage
question (Lot 15.4 §23).

DO NOT DO:
Do NOT modify ExecutionSimulator.cs further without a dedicated lot - it was written and verified together
with its full test suite (43 new tests + full regression, 896/898 passing). Do NOT treat the ExitReason
distribution shift (StopLoss/TakeProfit/Ambiguous/TimeHorizon percentages) as evidence the reconciliation
"improved" anything - it is a structural side effect of rescuing the InvalidStopTarget population, never a
performance claim, and must not be used to justify any further tuning. Do NOT revisit Option A/B/C - each
was rejected on architectural grounds (B specifically breaks live/backtest symmetry, a hard constraint, not
a preference) documented in this report, not on a coin flip. Do NOT wire the reconciliation logic (or
VolatilityStopLossModel) into IQIAIndicator.cs without a dedicated, explicit decision - this lot
deliberately did not, mirroring the established project precedent from Lots 15.3/15.4. Do NOT assume
TradePlan.PositionSize/RiskPerUnit now reflect the real fill price - they still don't (anchored on A,
unchanged) - only the reconciled Stop/Target levels do.
```

**STOP.**
