# QDE-012 — Sprint 15.25 — Lot 15.1 — Multi-Regime Signal/Entry Foundation Audit & Correction

**Date** : 2026-08-24
**Branche** : `feature/structural-stability-v2`
**Lot précédent** : 15.0
**Statut** : **IMPLEMENTED**
**Type** : **CORRECTION SCIENTIFIQUE CIBLÉE — 2 fichiers modifiés, purement additive ailleurs, aucune stratégie fabriquée**

---

# 1. EXECUTIVE SUMMARY

Le Lot 15.0 a établi que seul `MarketState.MeanReverting` (59.5% du dataset) produit un signal directionnel ; les 4 autres régimes vivants (`StructuralBreak`, `RandomWalk`, `Trending`, `StableRange`, ensemble 40.5% du dataset) sont bloqués par `EntryTriggerBuilder.DetermineDirection`, sans que la raison exacte soit distinguable des autres causes de `NO_ACTION` (marché ambigu, prix à l'équilibre, DynamicZScore indisponible).

Ce lot avait pour mandat de déterminer **précisément** quels régimes disposent d'un modèle de signal scientifiquement défini, et de rendre l'absence des autres **explicite** — jamais de fabriquer une stratégie de repli.

**Inventaire exhaustif confirmé** (recherche `: IScientificModel` sur tout le dépôt — exactement 7 classes existent, aucune autre) :

| Modèle | Régime visé | Statut réel |
|---|---|---|
| KalmanFilterModel, OrnsteinUhlenbeckModel, DynamicZScoreModel, VolatilityModel, SPRTModel | MeanReverting | **IMPLEMENTED**, réels, testés |
| TimeSeriesMomentumModel | Trending | **STUB** (`Success=false` toujours, "Scientific model placeholder") |
| BOCPDModel | Trending (secondaire) + StructuralBreak (primaire) | **STUB** (identique) |
| — | RandomWalk | **DEAD** (aucune classe "Random Walk Null Model" n'existe) |
| — | StableRange, Transitional | **MISSING** (aucun modèle jamais déclaré) |

**Conclusion scientifique, confirmée par cet inventaire, pas supposée** : il n'existe littéralement rien à câbler pour `Trending`/`StructuralBreak`/`RandomWalk`/`StableRange` sans écrire un nouveau modèle scientifique complet — hors du périmètre de ce lot (brief : "NE PAS inventer ce modèle dans ce lot"). Le lot s'est donc concentré exclusivement sur la correction demandée : **rendre le blocage explicite plutôt que silencieux**, sans jamais toucher au comportement de trading lui-même.

**Correctif implémenté, minimal et vérifié** : 2 fichiers modifiés (`EntryTriggerAssessment.cs`, `EntryTriggerBuilder.cs`), 87 lignes (+/-), aucune autre modification de production.

1. Un nouveau `EntryTriggerReason.UNSUPPORTED_REGIME`, additif à l'enum existant.
2. La condition de blocage `if (decision.Winner != MarketState.MeanReverting)` — **inchangée, bit pour bit** — enregistre désormais cette raison au lieu de la laisser `null`.
3. `BuildReason` est élargi pour que cette raison atteigne réellement `EntryTriggerAssessment.Reason` (auparavant impossible : `TriggerStatus` vaut `EXPIRED` pour ces régimes, jamais `READY`, et l'ancien code ne surfaçait la raison détaillée que pour `READY`).

**Résultat mesuré sur le dataset réel (MES M5, ~59 jours, run du jour)** :

| Régime | ReadyBars | UNSUPPORTED_REGIME | % |
|---|---:|---:|---:|
| MeanReverting | 6511 | **0** | 0% |
| StructuralBreak | 3367 | 3367 | **100%** |
| RandomWalk | 487 | 487 | **100%** |
| Trending | 448 | 448 | **100%** |
| StableRange | 118 | 118 | **100%** |

**Aucune régression** : MeanReverting produit toujours BUY=1154/SELL=1145/NO_ACTION=4212 sur 6511 barres — proportions identiques à Lot 15.0 (dérive de fenêtre Yahoo glissante attendue, documentée, jamais une régression). Preuve structurelle ET empirique : `UNSUPPORTED_REGIME` ne peut, par construction du code, jamais être assigné dans la branche MeanReverting.

**793 tests exécutés, 791 réussis, 1 échec (préexistant, sans rapport — `FusionStateManager`, hors scope, protégé), 1 ignoré (préexistant).**

---

# 2. LOT 15.0 FINDINGS (repris, contexte)

- Dataset : MES=F, M5, ~59 jours.
- Régimes vivants : MeanReverting 59.53%, StructuralBreak 30.88%, RandomWalk 4.42%, Trending 4.12%, StableRange 1.06%, Transitional/Unknown 0%.
- `EntryCandidate` : 6507/6507 pour MeanReverting, 0/4424 pour les 4 autres régimes combinés.
- Cause confirmée dans le code : `EntryTriggerBuilder.DetermineDirection` bloque explicitement (commentaire auto-documenté) tout régime ≠ `MeanReverting`, faute de modèle scientifique réel.
- `TimeSeriesMomentumModel`/`BOCPDModel` = stubs.
- Stop Loss absent globalement — non traité ici (§19 du brief, indépendant).

---

# 3. SIGNAL ARCHITECTURE

Chemin réel tracé (fichiers protégés lus, jamais modifiés sauf `EntryTriggerBuilder`/`EntryTriggerAssessment`, explicitement licenciés) :

```
DecisionArbitrator.Winner (MarketState)
    ↓
MethodologyEngine.Evaluate → MethodologyRegistry.Resolve(Winner) → QuantitativeMethodology (nommée, TOUJOURS non-null)
    ↓
SignalEngine.Process → ScientificModelRegistry.Resolve(methodology) → IReadOnlyList<IScientificModel> (0 à 5 modèles réels)
    ↓ (foreach model.Evaluate)
ScientificFusionEngine.Assess → ScientificAssessmentBuilder → ScientificAssessment { OverallConfidence, CoverageStatus, MissingEvidence, ... }
    ↓
EntryEngine.Process → EntryAssessmentBuilder.Build → EntryCandidate { OpportunityStatus }
    ↓
EntryTriggerEngine.Process → EntryTriggerBuilder.Build → EntryTriggerCandidate { Direction, Reason }
    ↓
TradePlanEngine.Process → TradePlan { Status }
```

**Où le régime est utilisé** : (1) `MethodologyRegistry.Resolve(Winner)` — sélection de méthodologie, déterministe, un seul point d'entrée ; (2) `EntryTriggerBuilder.DetermineDirection` — vérification de cohérence explicite (`decision.Winner != MarketState.MeanReverting`), la SEULE branche véritablement régime-conditionnelle de toute la chaîne aval.

**Où le régime est ignoré** : `SignalEngine.Process` exécute inconditionnellement (`activeModels` peut être vide, la boucle ne fait alors simplement rien) ; `EntryEngine`/`EntryAssessmentBuilder` ne lisent JAMAIS le régime — ils réagissent uniquement à la forme du `ScientificAssessment` produit (nombre de modèles exécutés/réussis), sans jamais consulter `MarketState` directement.

**Découverte centrale de ce lot, en amont d'`EntryTriggerBuilder`** : `ScientificAssessmentBuilder.Populate` (fichier non protégé, non modifié) possède DÉJÀ, depuis un sprint antérieur, la distinction exacte demandée par ce lot :

```csharp
public enum ScientificCoverageStatus { ModelsExecuted, NoModelCoverage }
```

Documenté ainsi dans son propre commentaire : *"'no model is registered for the current regime' (a structural pipeline gap - see Sprint 14 / audit finding ARC-003) is never displayed or reasoned about as if it were 'the registered models ran and found insufficient data'"*. Ce mécanisme est **exactement** ce que le brief §13 demande de réutiliser — il existait déjà, non exploité jusqu'à `EntryTriggerReason`. Preuve arithmétique de son équivalence avec l'ancien test : `MethodologyEngine.Evaluate` → `MethodologyRegistry.Resolve(Winner)` → `ScientificModelRegistry.Resolve(methodology)` est une chaîne déterministe, sans branche annexe, keyed uniquement sur `Winner` — donc `Winner == MeanReverting ⟺ CoverageStatus == ModelsExecuted` est une **équivalence par construction du code**, pas une coïncidence empirique.

**Où `EntryCandidate` est créé** : `EntryEngine.Process`, à partir du seul `ScientificAssessment` — quand `CoverageStatus=NoModelCoverage`, `ScientificAssessmentBuilder.Populate` ajoute les 5 noms de modèles MeanReversion attendus à `MissingEvidence` (même pour un régime qui n'a jamais dû les exécuter — un artefact de nommage préexistant, noté §14 mais non corrigé ici, hors du périmètre licencié), ce qui déclenche `_blocking.Count>0` dans `EntryAssessmentBuilder.Build` → `OpportunityStatus.NOT_QUALIFIED` directement, sans jamais passer par le test de priorité générique.

**Où le `TradePlan` est construit** : `TradePlanBuilder.Build` (protégé, non modifié), inchangé par ce lot — un `EntryTriggerCandidate` avec `Direction=NO_ACTION` produit toujours `TradePlanStatus.NO_TRADE`, quel que soit le `Reason`.

---

# 4. EXISTING SIGNAL MODELS

Recherche exhaustive `grep ": IScientificModel"` sur tout `IQIAIndicator/` — 7 résultats, aucun autre :

| Fichier | Nom | Statut | Preuve |
|---|---|---|---|
| `ScientificModels/MeanReversion/KalmanFilterModel.cs` | KalmanFilterModel | **IMPLEMENTED** | Testé (`Tests/ScientificModels/KalmanFilterModelTests.cs`, `KalmanFilterModelWindowTests.cs`) |
| `ScientificModels/MeanReversion/OrnsteinUhlenbeckModel.cs` | OrnsteinUhlenbeckModel | **IMPLEMENTED** | Testé (`Tests/ScientificModels/OrnsteinUhlenbeckModelTests.cs`) |
| `ScientificModels/MeanReversion/DynamicZScoreModel.cs` | DynamicZScoreModel | **IMPLEMENTED** | Testé (`Tests/ScientificModels/DynamicZScoreModelTests.cs`) |
| `ScientificModels/Context/VolatilityModel.cs` | VolatilityModel | **IMPLEMENTED** | Testé (`Tests/ScientificModels/VolatilityModelTests.cs`, `VolatilityModelCausalityTests.cs`) |
| `ScientificModels/Validation/SPRTModel.cs` | SPRTModel | **IMPLEMENTED** | Testé (`Tests/ScientificModels/SPRTModelTests.cs`) |
| `ScientificModels/Trend/TimeSeriesMomentumModel.cs` | TimeSeriesMomentumModel | **STUB** | `Evaluate()` retourne inconditionnellement `(Name, false, 0.0, "Scientific model placeholder")` — lu directement, confirmé |
| `ScientificModels/Context/BOCPDModel.cs` | BOCPDModel | **STUB** | Identique, corps de méthode identique caractère pour caractère |

Aucune classe "Random Walk Null Model", "Breakout Model" ou "Range Model" n'existe nulle part dans le dépôt (recherche par nom et par catégorie `IScientificModel.Category` : seules les catégories "MeanReversion", "Context", "Trend", "Validation" existent, avec 1 seule classe Trend et 2 Context dont 1 stub).

`ScientificModelRegistry.Resolve` (déjà lu au Lot 15.0, reconfirmé) : câble les 5 modèles MeanReversion pour `MeanReversionMethodology`, retourne `Array.Empty<IScientificModel>()` pour les 5 autres méthodologies — **jamais** les stubs, explicitement pour ne pas fabriquer une apparence de couverture scientifique (commentaire du fichier, Sprint 15.5/C2).

---

# 5. REGIME → SIGNAL MATRIX

| Regime | Signal Model | Implemented | Scientific Basis | Entry Possible |
|---|---|---|---|---|
| MeanReverting | KalmanFilterModel + OrnsteinUhlenbeckModel + DynamicZScoreModel + VolatilityModel + SPRTModel | **YES (5/5)** | Filtrage de Kalman, processus Ornstein-Uhlenbeck, Z-score dynamique, régime de volatilité, test séquentiel SPRT — tous testés indépendamment | **OUI** (35.3% des barres Ready, gate d'ambiguïté 0.95) |
| Trending | TimeSeriesMomentumModel (primaire) + BOCPDModel (secondaire) | **NO (0/2, stubs)** | Aucune — le nom de la méthodologie existe, aucun calcul réel ne s'exécute | **NON** — `UNSUPPORTED_REGIME`, 100% |
| RandomWalk | "Random Walk Null Model" (nommé, jamais implémenté) | **NO (0/1, classe absente)** | Aucune | **NON** — `UNSUPPORTED_REGIME`, 100% |
| StableRange | Aucun déclaré | **NO (0/0)** | Aucune — `StableRangeMethodology` explicitement documentée "no scientific methodology is implemented yet" | **NON** — `UNSUPPORTED_REGIME`, 100% |
| StructuralBreak | BOCPDModel (primaire) | **NO (0/1, stub)** | Aucune | **NON** — `UNSUPPORTED_REGIME`, 100% |
| Transitional | Aucun déclaré | **NO (0/0)** | Aucune — et de toute façon inatteignable comme `Decision.Winner` (Lot 15.0 §4) | **NON** — inatteignable en pratique ; `UNSUPPORTED_REGIME` si jamais atteint |
| Unknown | Aucun déclaré (`UnknownMethodology`) | **NO (0/0)** | Aucune | **NON** — `UNSUPPORTED_REGIME` si jamais atteint (jamais observé, 0 occurrence) |

Aucune cellule n'est remplie par supposition : chaque "NO"/"OUI" est une lecture directe de `ScientificModelRegistry.Resolve` (§4) croisée avec la mesure empirique du dataset réel (§9).

---

# 6. MEANREVERSION

Chemin `MeanReverting → Decision → Signal → Entry → EntryTrigger` **strictement inchangé**, confirmé par preuve structurelle et empirique :

- **Structurelle** : `noActionReason = EntryTriggerReason.UNSUPPORTED_REGIME` n'est assigné que dans la branche `if (decision.Winner != MarketState.MeanReverting)` — par définition, cette branche ne s'exécute jamais quand `Winner == MeanReverting`. La condition élargie dans `BuildReason` (`_noActionReason.Value == EntryTriggerReason.UNSUPPORTED_REGIME`) est donc un no-op garanti pour tout bar MeanReverting, quel que soit `_triggerStatus`.
- **Empirique** : `regime_entrytrigger_reason_lot151.csv` — `MeanReverting: UNSUPPORTED_REGIME=0/6511 (0%)`, `BUY=1154, SELL=1145, NO_ACTION=4212` — proportions (17.7%/17.6%/64.7%) cohérentes avec Lot 15.0 (17.9%/17.7%/64.4%) et avec un second run indépendant du même jour (N=6513, 17.8%/17.6%/64.6%) — la dérive résiduelle est de la fenêtre Yahoo glissante (documentée, non une régression), jamais du comportement de `EntryTriggerBuilder`.

Aucune abstraction commune n'a été nécessaire — le brief §4 anticipait cette possibilité, mais le chemin MeanReverting n'a subi aucune modification, directe ou indirecte.

---

# 7. TRENDING

`TrendFollowingMethodology` déclare `TimeSeriesMomentumModel` (primaire) + `BOCPDModel` (secondaire) — les deux confirmés stubs (§4). **Aucun modèle réel n'a été implémenté dans ce lot** (interdit par le brief §5 sauf implémentation complète préexistante ailleurs — recherche confirmée négative). Le système représente maintenant explicitement ce cas :

```
Trending → EntryTriggerReason.UNSUPPORTED_REGIME (jamais BUG, jamais NOT_AVAILABLE générique, jamais silencieux)
```

100% des 448 barres Ready `Trending` du dataset portent ce statut, `Direction=NO_ACTION` dans tous les cas.

---

# 8. RANDOMWALK

Question posée par le brief §7 : `RandomWalk` doit-il produire un signal, ou explicitement `NO TRADE` ?

**Constat honnête** (brief "RÈGLE ABSOLUE" : ne rien fabriquer) : `RandomWalkMethodology` ne déclare AUCUN modèle réel — ni stub, ni implémentation. Le code ne "démontre" donc rien scientifiquement ici ; il n'a simplement rien exécuté. Il serait malhonnête de coder une distinction affirmant "on a prouvé l'absence d'edge" alors qu'aucun test statistique ne tourne réellement. Ce lot traite donc `RandomWalk` **exactement comme `Trending`/`StructuralBreak`/`StableRange`** : `UNSUPPORTED_REGIME`, pas une catégorie de code séparée.

**Nuance scientifique, documentée ici plutôt que codée** : contrairement à `Trending`/`StructuralBreak`, l'absence de modèle pour `RandomWalk` est *moins préoccupante a priori* — un vrai marché aléatoire ne devrait, par définition, offrir aucune edge exploitable, donc l'absence de signal serait probablement le résultat correct même avec un modèle complet. Mais cela reste une hypothèse non vérifiée par le code actuel, pas un fait établi — elle ne justifie donc aucun traitement différencié dans `EntryTriggerBuilder`. 100% des 487 barres Ready `RandomWalk` portent `UNSUPPORTED_REGIME`.

---

# 9. STABLERANGE

`StableRangeRule` (Decision) est une **règle de classification pure** — elle calcule un score de compatibilité à partir de 3 dimensions Fusion (`Stationarity`, `MeanReversion`, `StructuralStability`) pour déterminer si `StableRange` gagne l'arbitrage, mais ne produit, ne référence, ni ne peut produire un `EntryCandidate` ou un signal directionnel — cette capacité n'existe nulle part dans `Engine/Decision/Rules/StableRangeRule.cs`. `MethodologyRegistry.Resolve(MarketState.StableRange)` retourne une méthodologie explicitement documentée *"No scientific methodology is implemented yet... Explicitly unsupported"*.

Confirmé : `StableRangeRule` = classification uniquement, jamais de génération de signal. `UNSUPPORTED_REGIME` sur 100% des 118 barres Ready `StableRange` du dataset — le régime le plus fragile statistiquement (Lot 15.0 §6 : 11 runs, jamais plus de 19 barres consécutives), mais ce constat de couverture Signal n'est PAS une question de taille d'échantillon — 0 modèle existerait identiquement sur un dataset de 10 ans.

---

# 10. TRANSITIONAL

Conformément au brief §9 : **DEFERRED**, non traité dans ce lot. `MarketState.Transitional` reste structurellement inatteignable comme `Decision.Winner` (Lot 15.0 §4, reconfirmé : 0/N occurrences sur ce nouveau run également). `TransitionalMethodology` existe et est explicitement documentée non supportée, mais son inatteignabilité en amont rend la question du Signal moot pour l'instant. Si un futur lot dédié rend `Transitional` atteignable (hors scope ici), le chemin `EntryTriggerBuilder` mis à jour par ce lot gérera automatiquement le cas : `CoverageStatus` serait `NoModelCoverage` (aucun modèle déclaré pour `TransitionalMethodology`) → `UNSUPPORTED_REGIME`, sans modification supplémentaire nécessaire.

---

# 11. UNKNOWN

Conformément au brief §10 : `Unknown` reste `NO TRADE`. Jamais observé comme `Decision.Winner` sur ce dataset (0 occurrence, confirmé de nouveau). Si jamais atteint (0 candidat en Decision), `UnknownMethodology` → 0 modèle → `CoverageStatus=NoModelCoverage` → `UNSUPPORTED_REGIME`, cohérent avec tous les autres régimes non supportés — aucune preuve scientifique contraire n'existe, donc aucun traitement différencié.

---

# 12. ENTRYTRIGGER AUDIT

`DetermineDirection`, branches identifiées et classifiées :

| Branche | Condition | Résultat | Type (brief §11) |
|---|---|---|---|
| 1 | `OpportunityStatus == WATCHLIST` | `WATCH` | Fallback légitime, inchangé |
| 2 | `decision is null` | `NO_ACTION`, pas de `noActionReason` (générique) | Silent rejection historique, hors scope de ce lot (aucune régression observée, pas de régime concerné — `decision` n'est jamais null en pratique une fois `Decision` atteint) |
| 3 | **`decision.Winner != MarketState.MeanReverting`** | `NO_ACTION`, **`noActionReason = UNSUPPORTED_REGIME`** (nouveau) | **Hard-coded restriction, désormais explicit unsupported behavior** |
| 4 | `AmbiguityScore >= 0.95` | `NO_ACTION`, `DECISION_AMBIGUOUS` | Fallback légitime, market-driven, inchangé (n'est atteint que par MeanReverting) |
| 5 | `DynamicZScore` indisponible | `NO_ACTION`, `DYNAMIC_ZSCORE_UNAVAILABLE` | Fallback légitime, inchangé |
| 6 | `DynamicZScore == 0` | `NO_ACTION`, `PRICE_AT_EQUILIBRIUM` | Fallback légitime, inchangé |
| 7 | `DynamicZScore != 0` | `BUY_CANDIDATE`/`SELL_CANDIDATE` | Seule branche directionnelle, inchangée |

Seule la branche 3 était auparavant une "silent rejection" au sens du brief §11 (raison calculée nulle part, message texte seulement, jamais dans le champ structuré `Reason`). C'est la seule corrigée.

---

# 13. UNSUPPORTED REGIME HANDLING (brief §12/§13)

Distinguo demandé (`NO_CANDIDATE`, `UNSUPPORTED_REGIME`, `INVALID_INPUT`, `EXCEPTION`, `VALID_SIGNAL`) — **déjà partiellement existant, réutilisé, jamais reconstruit** :

| Concept brief | Type projet réel | Statut |
|---|---|---|
| VALID_SIGNAL | `DirectionCandidate.BUY_CANDIDATE`/`SELL_CANDIDATE` + `EntryTriggerReason.READY` | Existait déjà |
| UNSUPPORTED_REGIME | `EntryTriggerReason.UNSUPPORTED_REGIME` | **Nouveau (ce lot)**, mais dérivé d'un mécanisme déjà existant (`ScientificCoverageStatus.NoModelCoverage`, référencé dans le message diagnostique) |
| INVALID_INPUT | `EntryTriggerReason.INVALID_CONTEXT` | Existait déjà |
| EXCEPTION | `BacktestStageException` (couche Backtest) / exception .NET non catchée (couche live) | Existait déjà, hors périmètre EntryTrigger |
| NO_CANDIDATE (marché non qualifiant, régime supporté) | `EntryTriggerReason.DECISION_AMBIGUOUS` / `PRICE_AT_EQUILIBRIUM` / `DYNAMIC_ZSCORE_UNAVAILABLE` | Existaient déjà (Sprint 15.7.1) |

Aucune extension n'a été nécessaire au-delà d'une seule valeur d'enum — le mécanisme de granularité (Sprint 15.7.1) et le mécanisme de détection de couverture (`ScientificCoverageStatus`, sprint antérieur, "ARC-003") existaient déjà séparément ; ce lot les a reliés l'un à l'autre, exactement dans l'esprit du brief §13.

---

# 14. OBSERVABILITY

Avant ce lot : un bar `Trending`/`RandomWalk`/`StructuralBreak`/`StableRange` rapportait `EntryTriggerReason.BLOCKED` (générique, dérivé uniquement de `TriggerStatus == EXPIRED`) — **indiscernable** d'un bar `MeanReverting` bloqué pour toute autre raison qui produirait aussi `EXPIRED` (aucune n'existe aujourd'hui en pratique, mais rien dans le type ne l'interdisait). Après ce lot : `UNSUPPORTED_REGIME` est un champ structuré, distinct, lisible sans requête supplémentaire — le dashboard/backtest peut désormais filtrer/compter ce cas précisément (démontré par le CSV §1).

**Limite honnête, documentée plutôt que masquée** : le diagnostic texte de `ScientificAssessmentBuilder.BuildDiagnostics` (fichier non protégé, non modifié par ce lot) continue de citer les 5 noms de modèles MeanReversion ("Missing evidence: KalmanFilterModel, OrnsteinUhlenbeckModel...") même pour un régime qui n'a jamais dû les exécuter — un nommage trompeur préexistant (le message suggère "modèles attendus manquants" plutôt que "aucun modèle n'était attendu pour ce régime"). Non corrigé ici : `ScientificAssessmentBuilder` n'était pas dans le périmètre licencié de ce lot (§30, seul `EntryTriggerBuilder` l'était), et une modification de ce fichier toucherait `EntryEngine`/tous les consommateurs de `ScientificAssessment.Diagnostics` bien au-delà de ce lot — proposé comme candidat pour un lot dédié (§24).

---

# 15. TRADEPLAN INTERACTION

Aucune modification de `TradePlanBuilder` (protégé). Comportement inchangé et vérifié : `EntryTriggerCandidate.Assessment.Direction=NO_ACTION` (quel que soit `Reason`, y compris le nouveau `UNSUPPORTED_REGIME`) produit toujours `TradePlanStatus.NO_TRADE` via `TradePlanBuilder.DescribeNoTradeReason`'s branche `_ => $"No directional candidate (Reason={reason})."` — le switch existant a un cas générique (`_`), donc la nouvelle valeur d'enum ne casse rien à la compilation ni à l'exécution, et produit un message de repli lisible ("No directional candidate (Reason=UNSUPPORTED_REGIME).") sans qu'aucune modification de `TradePlanBuilder` n'ait été nécessaire. Aucun `PLAN_READY` n'a jamais été, ni n'est désormais, fabriqué pour un régime non supporté — conforme au brief §18 ("Ne pas inventer PLAN_READY").

---

# 16. TESTS

3 nouveaux fichiers, purement additifs :

- `Tests/EntryTrigger/UnsupportedRegimeReasonTests.cs` + son wrapper xUnit `Tests/XunitWrappers/UnsupportedRegimeReasonXunitTests.cs` : couvre les 7 valeurs de `MarketState` (`MeanReverting`, `Trending`, `RandomWalk`, `StructuralBreak`, `StableRange`, `Unknown`, `Transitional`) — pour chacun des 6 non-MeanReverting, prouve `Direction=NO_ACTION` (jamais BUY/SELL/WATCH sauf override WATCHLIST testé séparément), `Reason=UNSUPPORTED_REGIME`, message citant le régime et `CoverageStatus`. Pour MeanReverting, prouve `Reason` n'est jamais `UNSUPPORTED_REGIME`. Exerce aussi la chaîne réelle complète (`MethodologyEngine` → `ScientificModelRegistry` → `SignalEngine.Process`), pas seulement des fixtures à la main, pour prouver qu'`EntryCandidate` ne peut jamais être créé avec un statut directionnel pour un modèle absent (brief §31).
- `Tests/Backtest/Calibration/UnsupportedRegimeObservabilityLot151Tests.cs` : re-parcourt le dataset réel (~59 jours), produit `regime_entrytrigger_reason_lot151.csv`, compare structurellement (pas par égalité stricte, cf. §18) aux chiffres du Lot 15.0.

**793 tests au total (suite complète, sans filtre), 791 réussis, 1 échec, 1 ignoré.**

**Échec, préexistant, sans rapport avec ce lot** : `HysteresisThresholdSensitivityLot1418Tests.Integration_Network_HysteresisThresholdSensitivity_Lot1418` — une réplique locale de test (`PersistenceHysteresisReplica`) diverge numériquement du vrai `FusionStateManager.Update` sur la dimension `Persistence` (3449/10931 barres). Preuve d'indépendance : l'assertion en échec compare exclusivement des valeurs produites par `EvidenceFusionEngine.Fuse`/`FusionStateManager.Update` — deux étages entièrement en AMONT de `Decision`/`EntryTrigger`, jamais touchés par ce lot. `FusionStateManager` est protégé (§30) ; non corrigé ici, signalé comme candidat P2 pour un futur lot dédié à cette réplique de test (probablement une dérive de la réplique elle-même, pas de la production — hors scope de vérifier lequel des deux ici).

**Ignoré, préexistant, sans rapport** : `Sprint1515HistoricalReconciliationXunitTests.VerifySprint1514Unaffected` (`[Fact(Skip=...)]` depuis le Sprint 15.22).

---

# 17. REGRESSION

**PASS**, preuve double :

- **Structurelle** : `noActionReason=UNSUPPORTED_REGIME` n'est assignable que dans la branche `Winner != MeanReverting` — logiquement impossible pour un bar MeanReverting, indépendamment de toute exécution.
- **Empirique** : `UNSUPPORTED_REGIME=0/6511 (0%)` pour MeanReverting ; BUY/SELL/NO_ACTION dans les mêmes proportions qu'au Lot 15.0 (écart de dataset Yahoo glissant, documenté, non une régression — confirmé par un second run indépendant du même jour montrant la même dérive résiduelle sur `RegimeCoverageMaturityAuditLot150Tests`, fichier non modifié par ce lot).

Toute la suite de tests préexistante touchant MeanReverting (`Tests/Fusion/`, `Tests/Decision/`, `Tests/Signal/`, `Tests/TradePlan/`, `Tests/Backtest/`, `DecisionDirectionCoherenceTests.cs` en particulier — y compris son test `AssertSuppressionReasonIsRecordedForUnsupportedRegime` déjà existant, qui vérifie la présence de la sous-chaîne du nom de régime dans le diagnostic) passe sans modification.

---

# 18. DETERMINISM

**PASS**. Testé explicitement : deux constructions indépendantes de `EntryTriggerBuilder`/`Build` sur le même `EntryTriggerContext` produisent `Direction`/`Reason`/`Diagnostics` bit-identiques. Le nouveau code ne lit aucune horloge, aucun générateur aléatoire, aucun état partagé — uniquement `context.ScientificAssessment.CoverageStatus` (une valeur déjà déterministe, calculée en amont) et `decision.Winner`.

Note méthodologique (brief §27, "même dataset+config → même résultat") : les chiffres absolus du dataset réel dérivent légèrement d'un run à l'autre à cause de la fenêtre Yahoo glissante (`to=DateTime.UtcNow`) — un fait du fournisseur de données, documenté depuis Lot 14.12, jamais une violation du déterminisme du CODE lui-même. C'est pourquoi les nouveaux tests §16 comparent des proportions/invariants structurels plutôt que des valeurs absolues codées en dur.

---

# 19. LOOK-AHEAD

**PASS**. `EntryTriggerContext.ScientificAssessment` est un `record` immuable, sans index de barre ni référence à une série temporelle — `CoverageStatus` est calculé dans `ScientificAssessmentBuilder.Populate` à partir du seul `scientificResults` de la barre COURANTE (passé par `SignalEngine.Process` au même bar). Aucun champ nouvellement lu par ce lot (`context.ScientificAssessment.CoverageStatus`) ne provient d'un calcul impliquant une barre future — c'est une propriété déjà présente sur l'objet `ScientificAssessment` que `DetermineDirection` recevait déjà intégralement avant ce lot (seul `.OverallConfidence`/`.ScientificResults` en étaient lus jusqu'ici) ; aucun nouvel accès à `series`/`bars`/`context.Clock` n'a été introduit.

---

# 20. RUN ISOLATION

**PASS**. `EntryTriggerEngine.Process` instancie un `EntryTriggerBuilder` local à chaque appel (`var builder = new EntryTriggerBuilder(_ambiguityGateThreshold);`, inchangé par ce lot) — aucun champ statique, aucun état partagé entre bars ou entre runs n'a été introduit (`_noActionReason` reste un champ d'instance déjà existant, réinitialisé à `null` au début de chaque `Populate`, comme avant ce lot). Testé explicitement en enchaînant `RandomWalk → MeanReverting → RandomWalk` sur un même `SignalEngine` réutilisé : aucune fuite d'état, chaque bar produit le `Reason` attendu pour SON PROPRE régime.

---

# 21. PRODUCTION SAFETY

Aucun `SubmitOrder`/`Buy()`/`Sell()` dans le diff. Aucune position réelle. Aucune connexion ATAS — dataset Yahoo uniquement, comme tous les lots précédents. Confirmé par lecture directe du diff (§ci-dessous) : uniquement de la logique d'assignation de raison/enum, aucun appel d'exécution.

---

# 22. REMAINING GAPS

- `EntryTriggerContext, decision is null` (branche 2, §12) reste une "silent rejection" au sens strict (pas de `noActionReason` assigné) — jamais observée en pratique (n'apparaît que si `MethodologySelection` est null, un cas de construction manuelle en test, jamais produit par le pipeline réel) ; non corrigée, hors du périmètre explicite de ce lot (le brief cible spécifiquement le blocage identifié au Lot 15.0, pas cette branche distincte).
- `ScientificAssessmentBuilder.BuildDiagnostics`'s nommage trompeur pour les régimes sans modèle (§14) — non corrigé, fichier hors périmètre licencié.
- Test `HysteresisThresholdSensitivityLot1418Tests` en échec, préexistant, `FusionStateManager`-scope, hors périmètre protégé de ce lot.
- Aucun modèle scientifique réel n'existe encore pour `Trending`/`RandomWalk`/`StructuralBreak`/`StableRange` — ce lot rend cette absence explicite, il ne la comble pas (hors mandat, brief §5/§6/§7/§8).
- Le Stop Loss (Lot 15.0 P0) et le Risk Engine restent non traités — indépendants, brief §19/§20.
- L'evidence de rupture structurelle (CUSUM/Bai-Perron non consommées, Lot 15.0 P0) reste non traitée — brief §6/§21, lot dédié explicitement prévu.

---

# 23. RECOMMENDED NEXT LOT

Deux lots indépendants, tous deux P0 hérités du Lot 15.0, aucun n'est un prérequis de l'autre :

1. **Lot Stop Loss** : implémenter une méthodologie de Stop Loss de production (recherche déjà entamée, `Tests/Research/StopLossCalibration/`), seule voie pour que `TradePlanStatus.PLAN_READY` devienne jamais atteignable, y compris pour MeanReverting.
2. **Lot Evidence StructuralBreak** : décider si CUSUM/Bai-Perron doivent être câblées en Fusion (gradient monotone déjà démontré au Lot 15.0 §10) — préalable scientifique avant d'envisager un jour un modèle de signal StructuralBreak réel.

Une troisième option plus lourde, à ne considérer qu'après une décision explicite de l'utilisateur : un lot de recherche/implémentation d'un modèle Trend réel (remplaçant le stub `TimeSeriesMomentumModel`) — le brief de ce lot interdit explicitement de l'entreprendre sans cette décision préalable.

---

# 24. PRODUCTION SAFETY (git proof)

```
git diff --stat -- IQIAIndicator/Engine/EntryTrigger/EntryTriggerAssessment.cs IQIAIndicator/Engine/EntryTrigger/EntryTriggerBuilder.cs
 .../Engine/EntryTrigger/EntryTriggerAssessment.cs | 13 +++-
 .../Engine/EntryTrigger/EntryTriggerBuilder.cs    | 84 +++++++++++++++++++---
 2 files changed, 87 insertions(+), 10 deletions(-)
```

Aucun fichier protégé (`RiskEngine.cs`, `RiskPolicy.cs`, `InstrumentRiskSpecification.cs`, `RiskEngineRequest.cs`, `RiskAssessment.cs`, `TradePlanBuilder.cs`, `RegimeEngine.cs`, `EvidenceFusionEngine.cs` (les deux), `FusionStateManager.cs`, `DecisionArbitrator.cs`) n'apparaît dans le diff de ce lot. 3 nouveaux fichiers de test, purement additifs, tombant dans des répertoires déjà non suivis par git. Aucun commit. Aucun ordre. Aucune DLL déployée. ATAS non utilisé.

---

# 25. HANDOFF CONTEXT

Voir bloc final ci-dessous.

---

# HANDOFF CONTEXT — NEXT SESSION

```
PROJECT:
IQIA

CURRENT LOT:
15.1

LAST COMPLETED:
15.0

CALIBRATION:
PAUSED

CURRENT DATASET:
MES=F
M5
~59 days

REGIMES:
5 live (MeanReverting 59.5%, StructuralBreak 30.9%, RandomWalk 4.4%, Trending 4.1%, StableRange 1.1%)
+ Transitional/Unknown (0%, architecturally unreachable, unchanged from Lot 15.0)

SIGNAL MODELS:
7 IScientificModel classes exist repo-wide, no others. 5 real/tested (MeanReversion). 2 literal stubs
(TimeSeriesMomentumModel, BOCPDModel, both Success=false always). No RandomWalk/Breakout/Range model
exists at all, not even a stub.

SUPPORTED REGIMES:
MeanReverting only (5/5 real models, unchanged by this lot).

UNSUPPORTED REGIMES:
Trending, RandomWalk, StructuralBreak, StableRange - now explicitly reported as
EntryTriggerReason.UNSUPPORTED_REGIME (100% of Ready bars each, confirmed empirically), never a
fabricated fallback, never silent BLOCKED/NOT_READY.

NO_ACTION REGIMES:
Same 4 as UNSUPPORTED_REGIME - RandomWalk was considered for a scientifically-distinct "expected null
result" treatment (brief §7) but treated identically to Trending/StructuralBreak/StableRange since no
model actually runs to demonstrate the absence of edge - documented as a narrative nuance in the report
(§8), not a separate code path, to avoid fabricating an unverified scientific claim.

MEAN REVERSION:
Unchanged. BUY=1154, SELL=1145, NO_ACTION=4212, UNSUPPORTED_REGIME=0, out of 6511 Ready bars (today's
run) - proportions match Lot 15.0 within expected Yahoo rolling-window drift.

TREND:
UNSUPPORTED_REGIME, 448/448 (100%). No real model exists (TimeSeriesMomentumModel/BOCPDModel both
stubs). Not implemented in this lot per brief §5 (forbidden without a pre-existing complete
implementation, none found).

RANDOM WALK:
UNSUPPORTED_REGIME, 487/487 (100%). No model at all exists (not even a stub) - "Random Walk Null Model"
is a name in MethodologyRegistry's declaration only.

STABLE RANGE:
UNSUPPORTED_REGIME, 118/118 (100%). StableRangeRule (Decision layer) confirmed to be classification-only
- never produces or references a signal/EntryCandidate.

STRUCTURAL BREAK:
UNSUPPORTED_REGIME, 3367/3367 (100%). BOCPDModel stub is its only declared model.

ENTRY:
EntryCandidate structurally cannot reach a directional OpportunityStatus for any UNSUPPORTED_REGIME bar
- ScientificAssessmentBuilder.Populate adds MissingEvidence (non-empty) whenever CoverageStatus is
NoModelCoverage, which EntryAssessmentBuilder.Build turns into OpportunityStatus.NOT_QUALIFIED via its
blocking-issues branch, before EntryTriggerBuilder is even reached - confirmed by tracing the code and by
running the real MethodologyEngine->ScientificModelRegistry->SignalEngine chain end to end per regime in
the new tests (brief §31 acceptance criterion verified, not merely asserted).

TRADE PLAN:
Unchanged (TradePlanBuilder not modified, protected). NO_TRADE for every non-MeanReverting regime, as
before this lot - only the upstream Reason naming changed, never TradePlanStatus itself.

STOP LOSS:
NOT TOUCHED

RISK:
NOT TOUCHED

FUSION:
NOT TOUCHED

DECISION:
NOT TOUCHED (no structural correction was needed - MarketState.Winner already propagates correctly
through MethodologyEngine/MethodologyRegistry/ScientificModelRegistry to ScientificAssessment.CoverageStatus,
confirmed to be a deterministic, unconditional, Winner-keyed chain with no other branch).

P0 REMAINING:
(1) Stop Loss globally absent (TradePlanContext.RiskParameters always null) - unchanged by this lot,
independent correction lot needed. (2) StructuralBreak (30.9% of dataset) still detected without any
structural-break evidence (Cusum/BaiPerron still never consumed by Fusion) - unchanged by this lot,
independent correction lot needed, explicitly deferred by this lot's own brief (§6/§21).

P1:
ScientificAssessmentBuilder.BuildDiagnostics still names the 5 MeanReversion-specific model names as
"missing evidence" even for regimes that were never expected to run them - a preexisting misleading
diagnostic, not corrected here (file outside this lot's licensed scope). One preexisting, unrelated test
failure in HysteresisThresholdSensitivityLot1418Tests (FusionStateManager-scope replica fidelity, proven
structurally independent of this lot's change) - candidate for a future dedicated lot to fix the test
replica or investigate the divergence.

CALIBRATION READINESS:
NOT READY (unchanged from Lot 15.0 - this lot improved observability, not capability; the same P0s that
blocked calibration readiness at Lot 15.0 still block it)

NEXT RECOMMENDED LOT:
A dedicated Stop Loss lot OR a dedicated StructuralBreak Evidence-wiring lot - both P0, independent,
either can go first. A Trend-model-implementation lot is a heavier third option requiring explicit user
approval before starting (brief forbids inventing one silently).

WHY:
This lot answered Lot 15.0's open question precisely: no regime other than MeanReverting has ANY
scientific signal model, real or stub-but-functional, anywhere in the repository - confirmed by an
exhaustive repo-wide inventory (7 IScientificModel classes total, no others). Given that, the only
scientifically honest correction available within this lot's mandate was observability, not capability -
fabricating a signal model to "solve" the gap would have violated the brief's absolute rule. The fix
reused an existing, already-designed-for-this-purpose mechanism (ScientificCoverageStatus, from a prior
sprint's "ARC-003" finding) rather than inventing a new one, keeping the change to 87 lines across 2
files with a provable, not just tested, non-regression guarantee for MeanReverting.

DO NOT DO:
Do NOT modify RiskEngine, RiskPolicy, InstrumentRiskSpecification, RiskEngineRequest, RiskAssessment,
TradePlanBuilder, RegimeEngine, EvidenceFusionEngine (either), FusionStateManager, DecisionArbitrator -
none were touched, none should be for an observability-only fix. Do NOT invent a Trend/Breakout/RandomWalk
signal model to "complete" the Regime -> Signal Matrix without an explicit, separate user decision - the
brief's absolute rule forbids it and this lot did not do it. Do NOT treat RandomWalk's NO_ACTION as
scientifically proven absence-of-edge - no model actually runs to demonstrate that; it is currently
UNSUPPORTED_REGIME for the same reason as the other three, not a distinct verified conclusion. Do NOT
change EntryTriggerBuilder's gating CONDITION (decision.Winner != MarketState.MeanReverting) to read
ScientificAssessment.CoverageStatus directly without first fixing every hand-built EntryTriggerContext
test fixture across the suite that currently relies on ScientificAssessment's CoverageStatus default -
this was evaluated and deliberately rejected in this lot for exactly that reason (see EntryTriggerBuilder's
own updated doc comment).
```

**STOP.**
