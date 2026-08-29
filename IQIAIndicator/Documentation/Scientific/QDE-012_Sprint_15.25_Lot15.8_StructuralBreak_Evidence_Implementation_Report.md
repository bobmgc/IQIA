# QDE-012 — Sprint 15.25 — Lot 15.8 — StructuralBreak Evidence Implementation

**Date** : 2026-08-27
**Branche** : `feature/structural-stability-v2`
**Lot précédent** : 15.7 (audit sémantique, aucune implémentation)
**Statut** : **IMPLEMENTED**
**Type** : **LOT D'INTÉGRATION — AUCUNE CALIBRATION, AUCUN REWEIGHTING, AUCUNE OPTIMISATION**

---

# 1. EXECUTIVE SUMMARY

L'evidence StructuralBreak (CUSUM + Bai-Perron) est maintenant réellement intégrée dans la Fusion, sous la forme d'une nouvelle dimension `FusionDimension.StructuralBreak`, produite par une nouvelle règle `StructuralBreakEvidenceRule`, stabilisée par le mécanisme EMA+hystérésis **déjà existant** de `FusionStateManager` (Alpha=0.20, seuil=0.03, tous deux inchangés). Architecture State-based, sans Freshness ni Persistence dédiées, exactement comme décidé au Lot 15.7.

**Cinq fichiers touchés, tous minimaux** : une valeur d'enum additive (`FusionDimension.cs`), une ligne dans le tableau `Dimensions` de `FusionStateManager.cs`, un nouveau fichier de règle (`StructuralBreakEvidenceRule.cs`), et une ligne de câblage dans chacun des deux points d'entrée production (`BacktestEngine.cs`, `IQIAIndicator.cs`).

**Aucune `IDecisionRule` ne lit cette nouvelle dimension** — l'evidence est produite et stabilisée, mais n'influence encore aucune décision de régime, aucun poids, aucune stratégie. C'est un choix délibéré, conforme à l'interdiction explicite de reweighting de ce lot.

**Régression empiriquement nulle** : `MarketState` (Decision.Winner) est **bit-identique, barre par barre, sur les 11555 barres du dataset Yahoo MES M5 réel**, avant et après l'ajout — 0 différence.

**Suite de tests complète** : 936 tests, 934 réussis, 1 échec (pré-existant, confirmé structurellement indépendant de ce lot), 1 ignoré (pré-existant, sans rapport).

**Découverte empirique notable, documentée mais non corrigée (calibration hors mandat)** : `Strength` (=`Cusum.Confidence`) **sature exactement à 1.0 pour la totalité des 10758 barres où `Detected=true`** — moyenne=médiane=min=max=1.0. Le champ `Strength`, dans sa définition actuelle, n'apporte donc **aucune information discriminante une fois la détection déclenchée** — voir Section 23.

**Aucune modification de** `FusionConfidence`, `FusionResult`, `FusionContext`, `EvidenceSet`, `CusumResult`, `BaiPerronResult`, `FusionProfileAnalyzer`, `StructuralStabilityRule`, `DecisionArbitrator`, ou d'aucune `IDecisionRule`.

---

# 2. LOT 15.7 CONTEXT

Le Lot 15.7 a tranché : architecture State-based, sans Freshness ni Persistence explicites (le mécanisme EMA+hystérésis existant de `FusionStateManager` suffit). Contrat minimal retenu : `Detected` (=`Cusum.ChangeDetected`), `Strength` (=`Cusum.Confidence`), `BreakCountMagnitude` (=`BaiPerron.BreakCount`), `BreakLocationBarIndex` (âge en barres, diagnostic), `Agreement` (catégoriel, gratuit). `DetectionTimestamp` explicitement écarté (trivial). Ce lot implémente ce contrat tel quel, sans le modifier.

---

# 3. EXISTING EVIDENCE ARCHITECTURE (audit préalable, avant toute modification)

Cartographie effectuée avant tout changement (`Engine/Fusion/Core/*`, `Engine/Fusion/State/FusionStateManager.cs`, `Engine/Fusion/Profile/*`, `Engine/Regime/Core/EvidenceSet.cs`, `Engine/Decision/*`).

- `EvidenceFusionEngine.Fuse(FusionContext)` itère une liste `IReadOnlyList<IFusionRule>` injectée au constructeur — générique, aucune connaissance codée en dur des dimensions. Aucune modification nécessaire.
- Chaque `IFusionRule` (`StationarityRule`, `PersistenceRule`, `MeanReversionRule`, `RandomWalkRule`) lit 1-2 champs de `EvidenceSet`, produit un `FusionConfidence { Value, Confidence, Explanation, IsAvailable }` sur SA propre clé `FusionDimension`, avec un sentinel "Missing Evidence" (`Value=0, Confidence=0, IsAvailable=false`) commun à toutes quand l'evidence source est nulle/invalide.
- `FusionStateManager.Update` : deux tableaux `FusionDimension[]` **séparés** existent dans le pipeline :
  - `FusionStateManager.Dimensions` (5 valeurs avant ce lot) — pilote l'EMA(0.20)+hystérésis(0.03) générique, avec un cas spécial pour `StructuralStability` (recalculée séparément par `StructuralStabilityRule.EvaluateAnalysis`, jamais lissée par la même récursion).
  - `FusionProfileAnalyzer.Dimensions` (5 valeurs, **inchangé**, toujours 5 après ce lot) — alimente `ProfileVelocity`/`ProfileStability`/`BehaviourConsistency`, qui alimentent à leur tour `StructuralStabilityRule`.

  **Ces deux tableaux sont indépendants dans le code source** — ce n'est PAS une invention de ce lot, c'était déjà le cas avant toute modification. Conséquence exploitée (Section 15) : ajouter une 6ᵉ dimension au premier tableau sans toucher au second isole automatiquement StructuralBreak de StructuralStability.
- `Engine/Decision/Rules/*` (`StableRangeRule`, `TrendingRule`, `MeanRevertingRule`, `StructuralBreakRule`, `RandomWalkRule`) : chacune lit des clés `FusionDimension` spécifiques et connues à l'avance dans `fusionResult.Dimensions` (un dictionnaire `TryGetValue`). Aucune n'itère "toutes les dimensions disponibles" — une nouvelle clé absente de leur liste de lectures explicites est invisible pour elles par construction, sans qu'il soit nécessaire de les modifier.
- Câblage production : `BacktestEngine.RunSignalPipeline` (ligne ~212) et `IQIAIndicator.cs` (champ `_fusion`, ligne ~64) sont les DEUX SEULS points où `new EvidenceFusionEngine([...])` est construit avec la liste réelle de règles utilisées en live/backtest (confirmé par recherche exhaustive — tous les autres sites `new EvidenceFusionEngine(...)` trouvés sont dans des fichiers de test).

---

# 4. CUSUM

`Engine/Regime/Evidence/CUSUM/CusumResult.cs` — champs réellement disponibles (aucune supposition) : `ChangeDetected(bool)`, `EstimatedBreakIndex(int, local, -1 si aucun)`, `PositiveCusum`, `NegativeCusum`, `Threshold`, `Confidence(double, =Clamp(peakMagnitude/threshold,0,1))`, `SampleSize`, `IsValid`, `Explanation`. `IsValid=false` (sentinel `CusumResult.Invalid`) en cas de warmup, série trop courte, série non finie, ou variance de référence nulle — jamais `ChangeDetected=true` dans ces cas (vérifié par lecture de `CusumStatistics.Compute`/`CusumResult.Invalid`).

---

# 5. BAI-PERRON

`Engine/Regime/Evidence/BaiPerron/BaiPerronResult.cs` — champs réellement disponibles : `Breakpoints(IReadOnlyList<int>, local, ordre chronologique)`, `BreakCount(int, non borné)`, `Confidence(double, =Clamp(1-exp(-0.5×bicImprovement),0,1))`, `GlobalRSS`, `BicScore`, `SampleSize`, `IsValid`, `Explanation`. Limite déjà documentée (Lot 15.6) : `Confidence` sature fortement sur le dataset réel — non réexploitée pour `Strength` dans ce lot, exactement comme prescrit.

---

# 6. STRUCTURALBREAK CONTRACT

Implémenté dans le nouveau fichier `Engine/Fusion/Rules/StructuralBreakEvidenceRule.cs` :

```csharp
public enum StructuralBreakAgreement { Unavailable, Neither, CusumOnly, BaiPerronOnly, Both }

public sealed record StructuralBreakContract
{
    public required bool Detected { get; init; }
    public required double Strength { get; init; }
    public required int BreakCountMagnitude { get; init; }
    public required int? BreakLocationBarIndex { get; init; }
    public required StructuralBreakAgreement Agreement { get; init; }
    public required bool IsAvailable { get; init; }
    public required string Explanation { get; init; }
}
```

`StructuralBreakEvidenceRule.BuildContract(CusumResult?, BaiPerronResult?)` est une fonction PURE, sans dépendance à `FusionContext`, testable isolément (19 tests dédiés).

---

# 7. DETECTED

`Detected = cusum.ChangeDetected`, **jamais** dérivé de Bai-Perron (Lot 15.6/15.7 : `BreakCount>0` est vrai dans 99.98% des barres — non discriminant). Si `cusum is null || !cusum.IsValid` → tout le contrat bascule en sentinel Missing Evidence, `Detected=false` (jamais fabriqué à `true` sur donnée manquante, conformément à la Section 21 du brief).

---

# 8. STRENGTH

`Strength = Clamp(cusum.Confidence, 0, 1)`. Réutilisé **sans transformation** à la fois comme score scientifique (`Value`) et comme méta-confiance (`Confidence`) de la dimension Fusion — CUSUM n'expose pas de second indicateur de qualité indépendant (contrairement à ADF/KPSS/DFA qui ont chacun un `Confidence` ET un `RSquared`/p-value distincts), donc dupliquer `Cusum.Confidence` sur les deux champs évite d'inventer une formule de blend/pondération.

**Découverte empirique (Section 23)** : sur le dataset réel, `Strength` vaut EXACTEMENT 1.0 pour la totalité des 10758 barres `Detected=true` (moyenne=médiane=min=max=1.0). Documenté, non corrigé.

---

# 9. BREAKCOUNTMAGNITUDE

`BreakCountMagnitude = baiPerron.BreakCount` si Bai-Perron valide, sinon `0` (avec `Explanation` précisant "unavailable", jamais confondu avec "zéro rupture confirmée"). Purement diagnostique — n'entre jamais dans `Value`/`Confidence`.

---

# 10. BREAKLOCATIONBARINDEX

`BreakLocationBarIndex = Math.Max(0, cusum.SampleSize - 1 - cusum.EstimatedBreakIndex)` si `Detected && EstimatedBreakIndex >= 0`, sinon `null`. **Décision volontaire** (permise explicitement par le Lot 15.7 §19, "ou AgeBars équivalent") : c'est un ÂGE en barres depuis la rupture estimée, PAS un index absolu de série — ce qui évite d'ajouter un champ `BarIndex` à `FusionContext` (qui ne porte aujourd'hui que `Timestamp`/`Symbol`/`TimeFrame`/`EvaluationId`), gardant la modification isolée au seul nouveau fichier de règle. Jamais décayé, jamais utilisé comme score de Freshness.

---

# 11. AGREEMENT

Catégoriel, gratuit (aucun seuil inventé) : `Unavailable` si Bai-Perron indisponible ; sinon `Both`/`CusumOnly`/`BaiPerronOnly`/`Neither` selon `(cusum.ChangeDetected, baiPerron.BreakCount>0)`. Jamais converti en score numérique, jamais utilisé pour un vote.

---

# 12. NORMALIZATION

Audit du contrat de normalisation existant : chaque `IFusionRule` produit un `Value`/`Confidence` dans `[0,1]` via `Math.Clamp`, sans scaler/percentile/calibration partagés au niveau de l'infrastructure (`FusionConfidence`/`FusionResultBuilder` ne normalisent rien eux-mêmes — chaque règle est responsable de son propre clamp). `StructuralBreakEvidenceRule` suit exactement ce même contrat (`Math.Clamp(cusum.Confidence, 0, 1)`), sans introduire de nouveau mécanisme. La saturation observée (Section 8/23) est un phénomène de LA MESURE elle-même (CUSUM), pas de la normalisation — non modifiable sans toucher `CusumStatistics.Compute` (protégé, hors mandat).

---

# 13. FUSIONDIMENSION

Ajout additif d'une 6ᵉ valeur : `StructuralBreak` (après `RandomWalk`). Aucune valeur existante renommée ni réordonnée. Vérifié par test (`FusionDimension_StructuralBreak_IsDefined`) et par le fait que les 5 dimensions préexistantes restent bit-identiques à l'ajout de la nouvelle règle (`AddingStructuralBreakEvidenceRule_NeverChangesTheOtherFiveDimensions`, passé).

---

# 14. FUSIONSTATEMANAGER

**Une seule ligne modifiée** : `FusionDimension.StructuralBreak` ajouté au tableau statique `Dimensions`. `Alpha=0.20`, `HysteresisThreshold=0.03`, la méthode `Smooth`, `BuildStableResult`, `BuildInitialStableResult` sont inchangés dans leur LOGIQUE — `StructuralBreak` emprunte le même chemin générique que Stationarity/Persistence/MeanReversion/RandomWalk (il n'est PAS spécial-casé comme `StructuralStability`, qui a un traitement séparé/auto-référentiel).

Démontré empiriquement (barre par barre, dataset réel) : `RawDetectedTransitions=685` (flips du booléen `Detected`, ≈ 2×340, cohérent avec le nombre de séquences CUSUM déjà mesuré au Lot 15.6) contre `StabilizedValueTransitions=799` (changements de la valeur stabilisée après hystérésis) — les deux comptes diffèrent, confirmant que le mécanisme EMA+hystérésis traite réellement `StructuralBreak` de façon indépendante du signal brut, exactement comme les autres dimensions (Section 7/14 du brief : distinction raw/stabilized prouvée par test, pas seulement affirmée).

---

# 15. STATE-BASED ARCHITECTURE

Chaîne réalisée : `Evidence (Cusum/BaiPerron) → StructuralBreakEvidenceRule → FusionDimension.StructuralBreak (raw) → FusionStateManager (EMA+hystérésis existant, inchangé) → FusionSnapshot.StableResult`. Aucun `StructuralBreakStateManager`/`PersistenceManager`/`FreshnessManager` créé.

**Isolation StructuralStability confirmée empiriquement, pas seulement architecturalement** : `FusionProfileAnalyzer.Dimensions` (qui alimente `ProfileVelocity`/`ProfileStability`/`BehaviourConsistency`/`StructuralStabilityRule`) reste à 5 valeurs, inchangé. Preuve indirecte mais rigoureuse : `StructuralBreakRule` (Decision), `MeanRevertingRule`, `TrendingRule`, `StableRangeRule` lisent toutes `FusionDimension.StructuralStability` dans leur calcul de score — si son calcul avait été altéré par l'ajout de `StructuralBreak`, au moins quelques barres du dataset réel (11555) auraient vu leur `MarketState.Winner` changer. **Zéro barre n'a changé** (Section 22) — c'est une confirmation empirique forte, pas une simple déduction de code, que l'isolation tient.

---

# 16. REGIME IMPACT

Répartition `Detected=true` (brut, avant stabilisation) par régime, dataset réel (11555 barres, N par régime dans le tableau) :

| Régime | N | % Detected (brut) | Strength moyen (si Detected) |
|---|---|---|---|
| Unknown | 0 | — (LOW_N) | — |
| StableRange | 131 | 93.13% | 1.0 |
| MeanReverting | 6841 | 89.68% | 1.0 |
| Trending | 519 | 89.21% | 1.0 |
| Transitional | 0 | — (LOW_N) | — |
| StructuralBreak | 3567 | 99.69% | 1.0 |
| RandomWalk | 497 | 96.98% | 1.0 |

Cohérent avec la mesure Lot 15.6/15.7 (99.688999% pour le régime StructuralBreak) — confirme la stabilité du signal d'un run à l'autre malgré le tirage Yahoo journalier différent. Purement descriptif — aucune tentative d'améliorer la couverture.

---

# 17. SIGNAL SAFETY

Vérifié par lecture directe des 5 fichiers `Engine/Decision/Rules/*.cs` : **aucun** ne référence `FusionDimension.StructuralBreak`. `EntryTriggerBuilder.DetermineDirection` (Lot 15.1, `UNSUPPORTED_REGIME`) reste inchangé — le blocage des régimes non supportés (Trending/RandomWalk/StructuralBreak/StableRange) n'est ni contourné ni modifié. Aucun `EntryCandidate` supplémentaire ne peut être produit par ce lot : la chaîne Signal/Entry ne lit jamais `FusionResult` directement, uniquement `DecisionResult` déjà arbitré, lui-même prouvé bit-identique (Section 22).

---

# 18. CAUSALITY

`StructuralBreakEvidenceRule.BuildContract` ne lit que `context.Evidence.Cusum`/`.BaiPerron`, tous deux déjà garantis causaux par construction (`RegimeEngine.Collect`, fenêtres glissantes strictement passées). Test dédié : `StructuralBreakLookAheadTests.AppendingFutureBars_NeverChangesAnyEarlierBarsStructuralBreakContractOrStabilizedDimension` (préfixe vs dataset étendu, valeurs bit-identiques à la frontière) — **PASS**.

---

# 19. DETERMINISM

`StructuralBreakEvidenceRule` est une classe sans état, `BuildContract` une fonction pure. 3 tests dédiés (hash du pipeline complet rejoué deux fois, deux instances indépendantes de `FusionEngine`+`FusionStateManager`, avec délai d'horloge réel entre les deux runs) — **PASS**, résultats bit-identiques.

---

# 20. RUN ISOLATION

3 tests dédiés (Run A / Run B / Run A, walk spécifique StructuralBreak, absence de contamination croisée entre deux scénarios différents) — **PASS**. Aucun état ne survit entre deux appels à `RunSignalPipeline` (la règle est instanciée fraîche à chaque appel, exactement comme les 4 règles Fusion préexistantes).

---

# 21. REAL DATASET

Dataset Yahoo MES=F M5 réel, pull frais du 27/08/2026 (~59 jours, 11555 barres — légère dérive normale par rapport au pull du Lot 15.6/15.7, cf. mémoire "Yahoo test nondeterminism" : fenêtre glissante de 45 jours, dérive jour-à-jour attendue) :

- `TotalSeriesBars=11555`, `ObservedBars=11555`, `IsAvailableCount=11536` (99.836% — 19 barres en warmup CUSUM)
- `DetectedCount(brut)=10758` (93.103% de toutes les barres ; 93.256% parmi les barres disponibles)
- `Strength` quand `Detected=true` : N=10758, moyenne=médiane=min=max=**1.0** (saturation totale — voir Section 23)
- `BreakCountMagnitude` : toutes barres N=11555, moyenne≈5.063, médiane=5, min=0 ; parmi détectées N=10758, moyenne≈5.072, médiane=5 (quasi identique — Bai-Perron peu discriminant, cohérent Lot 15.6)
- `Agreement` : Both=92.765% (10719), BaiPerronOnly=6.681% (772), CusumOnly=0.009% (1), Neither=0%, Unavailable=0.545% (63) — cohérent avec Lot 15.6 (93.24%/6.74%/0.02%/0%)
- Transitions : brutes=685 (5.929% des paires consécutives), stabilisées=799 (6.915%) — voir Section 14
- Aucune calibration effectuée sur ces chiffres.

---

# 22. REGRESSION

**Test bloquant dédié** (`StructuralBreakRegressionTests.Integration_Network_MarketStateWinner_IsBitIdentical_BeforeAndAfterStructuralBreakEvidenceRule`) : `MarketState.Winner` recalculé barre par barre AVANT (liste de règles Fusion à 4, exactement pré-Lot-15.8) et APRÈS (pipeline réel, 5 règles) sur le même `EvidenceSet` déjà calculé par barre. **Résultat : 0 différence sur toutes les barres comparées du dataset réel — PASS.** C'est la preuve empirique directe qu'aucune régression silencieuse n'a été introduite sur MeanReverting/StableRange/aucun régime.

---

# 23. PERFORMANCE (au sens : impact observé, pas P&L)

**Limitation découverte, documentée, non corrigée dans ce lot** : `Strength` (=`Cusum.Confidence`) sature à exactement 1.0 pour 100% des barres `Detected=true` sur le dataset réel (Section 8/21). Cause probable : une fois le seuil de Page's CUSUM franchi, la somme cumulative continue de croître sans borne tant que le nouveau régime persiste dans la fenêtre de 30 barres, poussant `peakMagnitude/threshold` bien au-delà de 1.0 (clampé). Conséquence : dans sa définition actuelle, `Strength` distingue bien "détecté" de "non détecté" (utile en amont du seuil), mais **n'apporte aucune granularité supplémentaire une fois détecté** — une éventuelle future règle de décision qui pondérerait par `Strength` traiterait toute barre détectée de façon identique. Ceci est un problème de DÉFINITION de la mesure, pas de poids/calibration — conformément au brief, il est documenté ici, non corrigé.

Impact de performance d'exécution : négligeable — une règle supplémentaire, calcul O(1) par barre à partir de champs déjà calculés, aucune passe supplémentaire sur les données de marché.

---

# 24. LIMITATIONS

1. Saturation de `Strength` (Section 23) — limitation de définition, pas de poids.
2. `BreakCountMagnitude` quasi constant (~5, cohérent avec Bai-Perron peu discriminant, déjà connu Lot 15.6) — la dimension `StructuralBreak` reste donc largement pilotée par `Cusum.ChangeDetected`/`Confidence` seuls en pratique.
3. Le CSV `structuralbreak_evidence_lot158.csv` tronque la colonne `Max` pour les lignes `BREAKCOUNTMAGNITUDE_DISTRIBUTION` (6 en-têtes déclarés pour des lignes à 7 valeurs dans le fichier de test) — un artefact cosmétique du fichier de recherche, sans impact sur les assertions de test (qui utilisent les données en mémoire non tronquées) ni sur aucune valeur de production.
4. Aucune `IDecisionRule` ne consomme encore cette evidence — c'est voulu (Section 25), mais signifie que ce lot, à lui seul, n'a aucun effet observable sur les décisions de régime en production.

---

# 25. NO REWEIGHTING

`StructuralBreakRule` (Decision, poids 0.40/0.30/0.20/0.10) est **inchangée, intacte** — vérifié par lecture directe, aucune modification. Aucune `IDecisionRule` ne lit `FusionDimension.StructuralBreak`. La nouvelle dimension est pondérée par... rien pour l'instant : elle est présente dans `FusionResult.Dimensions` mais n'est lue par aucun consommateur de décision. C'est la réponse exacte à la question du brief §25 ("si aucun poids explicite n'existe : documenter comment la dimension est pondérée par le mécanisme existant") — elle ne l'est pas encore, par choix, en attente d'un futur lot de reweighting explicitement scopé.

---

# 26. REMAINING P0

Le seul P0 restant sur la lignée StructuralBreak (Lots 15.0→15.8) est désormais la décision de reweighting elle-même : comment et avec quel poids `StructuralBreakRule` (ou une nouvelle règle) devrait consommer `FusionDimension.StructuralBreak`, sachant que `Strength` sature (Section 23) et devra probablement être redéfinie AVANT d'être utile à une pondération (ex. utiliser `PeakMagnitude/Threshold` non clampé, ou une autre transformation — décision hors mandat de ce lot).

---

# 27. RECOMMENDED NEXT LOT

**"StructuralBreak Reweighting Decision"** (nom indicatif). Portée suggérée, à valider explicitement avant de démarrer :
1. Décider si `Strength` doit être redéfinie (Section 23) AVANT toute pondération — sinon toute pondération traiterait "détecté" de façon binaire de fait.
2. Décider explicitement du poids/de la formule par laquelle `StructuralBreakRule` (ou une nouvelle règle Decision) consomme `FusionDimension.StructuralBreak` — décision architecturale, jamais un grid-search sur le P&L.
3. Fichiers concernés : `Engine/Decision/Rules/StructuralBreakRule.cs` (seul fichier Decision à toucher a priori), + tests associés.
4. Fichiers protégés à ne pas toucher : tout `Engine/Fusion/*` (déjà stable depuis ce lot), `FusionStateManager.cs`, `RegimeEngine.cs`, `CUSUM`/`BaiPerron`.

---

# 28. HANDOFF CONTEXT

```
PROJECT:
IQIA

CURRENT LOT:
15.8

LAST COMPLETED:
15.8

CALIBRATION:
PAUSED

STRUCTURAL BREAK:
IMPLEMENTED as FusionDimension.StructuralBreak, produced by Engine/Fusion/Rules/StructuralBreakEvidenceRule.cs.
Feeds FusionStateManager's existing EMA(0.20)+hysteresis(0.03) unmodified. NOT consumed by any
IDecisionRule yet (deliberate - reweighting forbidden this lot).

CUSUM:
Detected=cusum.ChangeDetected (primary signal). Strength=Clamp(cusum.Confidence,0,1) - SATURATES AT
EXACTLY 1.0 for 100% of the 10758 raw-Detected=true bars on the real dataset (mean=median=min=max=1.0).
Documented as a real limitation (report §23), not fixed in this lot.

BAI-PERRON:
BreakCountMagnitude=baiPerron.BreakCount when valid, else 0. Near-constant (~5) on the real dataset,
consistent with Lot 15.6's saturation finding. Never used for Detected/Strength.

DETECTED:
=Cusum.ChangeDetected only, never Bai-Perron-derived. False (never true) when Cusum missing/invalid -
whole contract becomes IsAvailable=false Missing Evidence sentinel in that case.

STRENGTH:
=Cusum.Confidence, duplicated unchanged onto both FusionConfidence.Value and .Confidence (no blend weight
invented - Cusum has no second independent quality metric the way ADF/KPSS/DFA do). SATURATED once
detected (see CUSUM above) - a real limitation for any future weighting use.

BREAK COUNT:
BreakCountMagnitude field, diagnostic only, never feeds Value/Confidence.

BREAK LOCATION:
BreakLocationBarIndex = Max(0, cusum.SampleSize-1-cusum.EstimatedBreakIndex) - an AGE in bars since the
estimated break, not an absolute series index (deliberate, avoids extending FusionContext). Null when no
break estimated. Diagnostic only, never decayed.

AGREEMENT:
Categorical (Unavailable/Neither/CusumOnly/BaiPerronOnly/Both), free (no invented threshold). Real dataset:
Both=92.765%, BaiPerronOnly=6.681%, CusumOnly=0.009%, Neither=0%, Unavailable=0.545% - consistent with
Lot 15.6 (93.24%/6.74%/0.02%/0%/n.a.).

FUSION:
FusionDimension enum extended additively (6th value, StructuralBreak, after RandomWalk). No existing value
renamed/reordered. The other 5 dimensions proven bit-identical before/after this rule's addition
(dedicated test, passing).

EMA:
UNCHANGED. Alpha=0.20, StabilizationConfiguration.Default untouched.

HYSTERESIS:
UNCHANGED. HysteresisThreshold=0.03, Smooth()/BuildStableResult()/BuildInitialStableResult() logic
untouched - only FusionStateManager.Dimensions array got one additive line
(FusionDimension.StructuralBreak). StructuralBreak takes the exact same generic EMA+hysteresis path as
Stationarity/Persistence/MeanReversion/RandomWalk (not special-cased like StructuralStability).

WEIGHTS:
UNCHANGED. StructuralBreakRule (Decision, 0.40/0.30/0.20/0.10) untouched, verified by direct reading.

FRESHNESS:
NOT IMPLEMENTED (Lot 15.7 decision confirmed and executed as-is).

PERSISTENCE:
NOT IMPLEMENTED as a separate mechanism - FusionStateManager's existing EMA+hysteresis is the sole
temporal-smoothing layer this dimension goes through.

REGIME COVERAGE:
Unchanged by construction and confirmed empirically bit-identical (MarketState.Winner regression test,
0 mismatches across 11555 real bars). Descriptive StructuralBreak-detected rate by regime: StableRange
93.13%, MeanReverting 89.68%, Trending 89.21%, StructuralBreak 99.69%, RandomWalk 96.98% (consistent with
Lot 15.6/15.7).

SIGNAL:
Unaffected - no IDecisionRule reads the new dimension, verified by direct inspection of all 5
Engine/Decision/Rules/*.cs files. EntryTriggerBuilder's UNSUPPORTED_REGIME gate (Lot 15.1) untouched.

ENTRY:
Unaffected - Signal/Entry never reads FusionResult directly, only the already-arbitrated DecisionResult,
itself proven bit-identical before/after.

STOP LOSS:
Untouched (Engine/Risk/, Backtest/Execution/, Engine/TradePlan/ not modified in this lot).

RISK:
Untouched.

EXECUTION:
Untouched.

P0 REMAINING:
The reweighting decision itself - how/whether StructuralBreakRule (or a new Decision rule) should consume
FusionDimension.StructuralBreak, and whether Strength needs redefinition first given its saturation
(report §23/§26) - explicitly out of this lot's mandate.

P1:
The CSV writer in StructuralBreakEvidenceLot158IntegrationTests.cs truncates the Max column for
BREAKCOUNTMAGNITUDE_DISTRIBUTION rows (6 declared headers vs 7 values) - cosmetic, test-file-only, no
production impact, not fixed in this lot (out of mandate, brief only asked for measurement not tooling
polish).

NEXT LOT:
"StructuralBreak Reweighting Decision" (indicative name) - scope detailed in report §27: decide whether
Strength needs redefinition before any weighting use, then decide explicitly (never via P&L grid-search)
how StructuralBreakRule or a new Decision rule consumes FusionDimension.StructuralBreak. Only
Engine/Decision/Rules/StructuralBreakRule.cs (or a new sibling file) should need touching; all
Engine/Fusion/* files from this lot are protected/stable.

DO NOT DO:
Do NOT wire FusionDimension.StructuralBreak into any IDecisionRule without an explicit, separately-scoped
reweighting lot. Do NOT redefine Strength's saturation "fix" as a side effect of some other change - it
needs its own explicit decision. Do NOT add StructuralBreak to FusionProfileAnalyzer.Dimensions - that
would create exactly the StructuralStability double-counting/circularity risk this lot's architecture
deliberately avoids (report §15). Do NOT touch FusionStateManager's Alpha/HysteresisThreshold/Smooth logic
for any reason related to StructuralBreak. Do NOT attempt to fix the pre-existing
HysteresisThresholdSensitivityLot1418Tests failure - confirmed, again, structurally unrelated to this lot
(PersistenceHysteresisReplica operates on a single scalar series, independent of FusionStateManager's
Dimensions array length).
```

**STOP.**
