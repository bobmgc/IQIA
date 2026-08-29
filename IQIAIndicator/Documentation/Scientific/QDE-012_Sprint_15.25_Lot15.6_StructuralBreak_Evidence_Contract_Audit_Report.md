# QDE-012 — Sprint 15.25 — Lot 15.6 — Structural Break Evidence: Scientific Contract & Integration Design Audit

**Date** : 2026-08-26
**Branche** : `feature/structural-stability-v2`
**Lot précédent** : 15.5
**Statut** : **AUDIT COMPLETE**
**Type** : **AUDIT + ARCHITECTURE + DESIGN SCIENTIFIQUE — AUCUNE MODIFICATION DE PRODUCTION**

---

# 1. EXECUTIVE SUMMARY

Les Lots 15.3-15.5 ont fermé les P0 Stop Loss/Execution/Fill-Price. Seul reste ouvert : **StructuralBreak Evidence** (Lot 15.2). Ce lot répond à la question posée : **quel contrat scientifique formaliser avant tout câblage de CUSUM/Bai-Perron dans la Fusion ?**

**Confirmé** : aucun fichier concerné (`CUSUM/`, `BaiPerron/`, `Engine/Fusion/`, `Engine/Decision/`, `RegimeEngine.cs`) n'a changé depuis le Lot 15.2 (`git status` vide sur ces chemins) — ses conclusions restent intégralement valides.

**Découverte centrale, nouvelle à ce lot** : l'opérationnalisation la plus naturelle de "Bai-Perron a détecté une rupture" (`BreakCount > 0`) est **presque toujours vraie** — 99.98% des 11 421 barres du dataset réel (`Both`+`BaiPerronOnly` = 93.24%+6.74%). Bai-Perron ne produit que **2 séquences ("runs") sur tout le dataset**, l'une de 11 357 barres consécutives. Ce n'est pas un signal de rupture — sur une fenêtre M5 de 128 barres, une segmentation BIC-optimale trouve presque toujours qu'au moins un point de coupure améliore l'ajustement par rapport à l'hypothèse "aucune rupture" (1 segment). **`BreakCount > 0` n'est donc PAS un événement rare ni discriminant — c'est quasiment une constante.**

**Deuxième découverte, réfutant partiellement l'hypothèse "fraîcheur" du Lot 15.2 sur données réelles** : la corrélation `Pearson(BreakAgeBars, BaiPerron.Confidence) = 0.0033` (quasi nulle) — parce que `Confidence` est saturée à ~0.9997-1.0 dans **toutes** les tranches d'âge. L'effet de fraîcheur démontré au Lot 15.2 (synthétique) est réel mécaniquement mais **totalement masqué en pratique** par la saturation déjà documentée — confirmant, avec une preuve supplémentaire indépendante, que `BaiPerron.Confidence` est scientifiquement inutilisable comme entrée Fusion telle quelle, sur ce dataset.

**Conséquence directe pour le contrat proposé (§25)** : ni `BaiPerron.BreakCount>0` (quasi-constant), ni `BaiPerron.Confidence` (saturé) ne peuvent servir seuls de signal StructuralBreak discriminant. Seuls trois éléments montrent une variation réellement exploitable : `Cusum.Confidence`/`ChangeDetected` (dynamique, 340 runs, longueur médiane 21 barres), `BaiPerron.BreakCount` (magnitude continue, gradient de régime déjà noté au Lot 15.2), et `BreakAgeBars` dérivé (une mesure d'ancienneté brute jamais normalisée aujourd'hui).

**Décision Replace vs Augment (§20)** : **AUGMENT.** `StructuralStability` est partagée par 4 des 5 règles de Decision (`MeanRevertingRule` 0.10, `TrendingRule` 0.30, `StableRangeRule` 0.20, `StructuralBreakRule` 0.40) — la remplacer changerait le comportement des quatre régimes simultanément, hors de portée d'un correctif ciblé sur StructuralBreak.

**Aucune modification de production. Aucun seuil calibré. Aucune optimisation.**

---

# 2. LOT 15.0 CONTEXT

`StructuralBreak` ≈ 30.9% du dataset ; `Cusum`/`BaiPerron` calculées mais jamais consommées par Fusion.

# 3. LOT 15.1 CONTEXT

`StructuralBreak` reste `UNSUPPORTED_REGIME` — aucun modèle de signal réel. Ce lot ne change rien à cela (brief §19, confirmé).

# 4. LOT 15.2 CONTEXT

Audit complet de CUSUM (causal, séquentiel) et Bai-Perron (causal au sens strict mais avec la propriété de "fraîcheur" — confiance plus faible pour une rupture récente que pour une rupture centrale, prouvé synthétiquement). Conclusion : aucune correction de production justifiée, contrat scientifique manquant.

# 5. LOT 15.5 CONTEXT

Aucun lien direct — Fill-Price Reconciliation concerne Execution, pas Regime/Fusion. Mentionné pour mémoire de continuité de la lignée P0.

---

# 6. CURRENT STRUCTURALBREAK PIPELINE

| Étape | Input | Output | Fenêtre | Causalité | Fallback | Consommation aval |
|---|---|---|---|---|---|---|
| `RegimeEngine.Collect` | Prix bruts (buffer circulaire 128) | `EvidenceSet` (9 evidences) | par evidence | causal (prouvé) | N/A | `EvidenceFusionEngine` (4 evidences seulement) |
| `CusumEvidence` | Série de prix, 30 barres | `CusumResult` | 30, min 20 | causal (séquentiel) | `IsValid=false` si <20 | **Aucun** (Fusion), compteur dashboard uniquement |
| `BaiPerronEvidence` | Série de prix, 128 barres | `BaiPerronResult` | 128, min 64 | causal (strict), fraîcheur limitée | `IsValid=false` si <64 | **Aucun** (Fusion), idem |
| `StructuralStabilityRule` | `FusionProfileAnalysis` (auto-référentiel) | `FusionConfidence` (StructuralStability) | historique des 4 autres dimensions | causal | `Value=1.0` en warmup (<2 snapshots) | `FusionStateManager.ReplaceStructuralStability` |
| `FusionStateManager` | `FusionResult` brut + historique | `FusionResult` stable (EMA+hystérésis) | 1 barre (état persistant) | causal | valeur précédente gelée | `DecisionEngine` |
| `DecisionArbitrator` | 5 candidats de `IDecisionRule` | `MarketState` (Winner) | 1 barre | causal | `Unknown` si 0 candidat (jamais observé) | `MethodologyEngine`/`SignalEngine` |
| Signal/Entry | `MarketState.StructuralBreak` | `UNSUPPORTED_REGIME` (Lot 15.1) | — | causal | — | Aucune (bloqué en amont) |

---

# 7. CUSUM CONTRACT

**Sortie réelle** (`CusumResult`) : `ChangeDetected` (bool), `EstimatedBreakIndex` (int, **local à la fenêtre**, -1 si non détecté), `PositiveCusum`/`NegativeCusum` (double, magnitude brute), `Threshold` (double), `Confidence` (double [0,1] = `peakMagnitude/threshold`), `SampleSize`, `IsValid`.

**Réponse explicite (brief §5)** : CUSUM produit **simultanément** une détection binaire (`ChangeDetected`) ET un score continu (`Confidence`) ET une localisation (`EstimatedBreakIndex`) — ce ne sont pas trois représentations concurrentes du même fait, ce sont trois CHAMPS DISTINCTS avec des sémantiques distinctes, jamais fusionnés dans le code source lui-même. `Confidence` **n'est jamais** une conversion artificielle du booléen — elle est calculée indépendamment (`peakMagnitude/threshold`), le booléen (`peakMagnitude > threshold`) n'étant qu'un seuillage de la même quantité sous-jacente.

**Mesuré sur données réelles** : dynamique et discriminant — 340 "runs" de détection, longueur médiane 21 barres, jamais figé sur tout le dataset (contraste direct avec Bai-Perron, §8).

---

# 8. BAI-PERRON CONTRACT

**Sortie réelle** (`BaiPerronResult`) : `Breakpoints` (liste d'index **locaux à la fenêtre**, ordre chronologique croissant, vérifié par lecture de `ReconstructBreakpoints`), `BreakCount` (int, non borné), `Confidence` (double [0,1] = `1-exp(-0.5×bicImprovement)`), `GlobalRSS`, `BicScore`, `SampleSize`, `IsValid`.

**Réponse explicite (brief §6)** : "break detected" (`BreakCount>0`) n'est **absolument pas** équivalent à "break currently active" — démontré empiriquement et sans ambiguïté (§9) : sous cette opérationnalisation, `BreakCount>0` est vrai sur **99.98%** du dataset (2 "runs" seulement, l'un de 11 357 barres). Une segmentation BIC-optimale sur 128 barres de marché réel trouve presque toujours qu'au moins une coupure améliore l'ajustement — c'est une propriété STATISTIQUE du modèle de segmentation appliqué à des données de marché intrinsèquement non stationnaires à cette échelle, pas la détection d'un événement rare.

---

# 9. DETECTION VS ACTIVE STATE

**C'est le point central du lot, formalisé ici comme un modèle conceptuel EN 5 ÉTAPES — jamais implémenté :**

```
Detection        : sortie brute de l'algorithme sur la fenêtre courante
                    (Cusum.ChangeDetected ; BaiPerron.BreakCount>0)
    ↓
Confirmation     : le signal est-il corroboré par une SECONDE source indépendante ?
                    (accord Cusum/BaiPerron, §17 ; jamais un vote arbitraire)
    ↓
Freshness        : la rupture détectée est-elle RÉCENTE (proche de "maintenant",
                    la fin de la fenêtre) ou ANCIENNE (proche du début de la fenêtre) ?
    ↓
Persistence      : le signal (une fois Freshness prise en compte) reste-t-il stable
                    d'une barre à l'autre, ou oscille-t-il ?
    ↓
Active Structural State : la conclusion finale, seule susceptible d'alimenter Fusion
```

**Constat empirique, sans ambiguïté** : l'architecture actuelle ne fournit QUE l'étape 1 (Detection). Aucune Confirmation formelle, aucune Freshness normalisée, aucune Persistence n'existe nulle part — `CusumEvidence`/`BaiPerronEvidence` sont des classes **sans état**, recalculées à neuf chaque barre, sans mémoire d'une barre à l'autre. Confondre "Detection" (99.98% du temps pour Bai-Perron) avec "Active State" produirait un signal presque toujours au maximum — scientifiquement indéfendable.

---

# 10. BREAKCOUNT VS CONFIDENCE

Les 4 concepts demandés (brief §7), formellement distingués :

| Concept | Nature | Mesuré | Variation réelle |
|---|---|---|---|
| **BreakCount** | Nombre de segments choisis par sélection BIC | Bai-Perron uniquement | Gradient de régime réel (Lot 15.2 : 4.98→6.04) — informatif |
| **Confidence** (BaiPerron) | Amélioration BIC globale vs hypothèse nulle (1 segment) | Bai-Perron | **Saturée** (0.9997-1.0 partout, §9) — non informatif en production |
| **EvidenceStrength** | Magnitude d'UNE rupture spécifique (non un champ existant — concept à définir) | Ni Cusum ni BaiPerron ne l'exposent directement aujourd'hui | N/A — GAP |
| **Freshness** | Ancienneté relative de la rupture (non un champ existant) | Dérivable de `EstimatedBreakIndex`/`Breakpoints`, jamais calculé aujourd'hui | Voir §11 — GAP |

**Interdictions du brief respectées, justifiées empiriquement** : `BreakCount` **≠** `Confidence` — confirmé, ils varient de façon complètement indépendante (`BreakCount` a un vrai gradient, `Confidence` est plate). `Confidence` **≠** `Freshness` — confirmé, `Pearson(BreakAgeBars, Confidence)=0.0033`, quasi nul. La présence d'une rupture ancienne (jusqu'à 100 barres, §11) n'est **jamais** utilisée ici comme preuve d'un état actuel — c'est précisément ce que ce lot refuse de faire.

---

# 11. FRESHNESS

**Formule descriptive utilisée pour cet audit** (jamais un champ de contrat, jamais implémentée en production) :
```
AbsoluteBreakBar = CurrentBarIndex - (SampleSize - 1 - LocalBreakIndex)
BreakAgeBars     = CurrentBarIndex - AbsoluteBreakBar   (0 = à la fin de la fenêtre, jusqu'à ~127 = au début)
```
(Bai-Perron utilise le breakpoint le plus RÉCENT, `Breakpoints.Max()`, en cas de ruptures multiples.)

**Mesuré** : Mean=21.08, Médiane=17, P10=11, P25=12, P75=25, P90=37, Max=100 barres (11 421 barres analysées). La rupture "détectée" typique a déjà **17 barres (85 minutes en M5)** d'ancienneté au moment où elle est observée — jamais un événement instantané.

**Réponse explicite (brief §9)** : **PROBLÈME NON RÉSOLU, déclaré explicitement.** Aucune règle de décroissance (5 barres, 20 barres, 1 session...) n'a de fondement scientifique dans ce dépôt — rien n'existe qui justifierait un seuil précis sans l'inventer. `BreakAgeBars` (la donnée brute) est mesurable et causale dès aujourd'hui ; sa transformation en un score `Freshness∈[0,1]` nécessiterait soit un dataset bien plus long pour caractériser empiriquement la dynamique de décroissance réelle, soit une convention de domaine explicitement approuvée par l'utilisateur — **ni l'un ni l'autre n'existe aujourd'hui.**

---

# 12. TEMPORAL ALIGNMENT

`EstimatedBreakIndex` (Cusum) et `Breakpoints` (BaiPerron) sont des **index locaux à la fenêtre courante** — jamais des timestamps ni des index absolus. **Aucune traduction vers un index de barre absolu ou un timestamp n'existe nulle part dans le code de production aujourd'hui** — confirmé par lecture, ni `RegimeEngine`, ni `EvidenceSet`, ni aucun consommateur ne réalise cette conversion (normal, puisque rien ne consomme ces champs). La formule §11 a été construite spécifiquement pour cet audit, à l'extérieur de la production, pour rendre la mesure possible. **Tout futur contrat devra intégrer explicitement cette traduction — elle n'est pas gratuite, elle exige de connaître `SampleSize` (déjà exposé) à chaque barre.**

Cas limite découvert pendant l'audit (jamais observé sur ce dataset, mais réel dans le code) : `CusumStatistics.RunPageCusum`'s bookkeeping interne peut produire `EstimatedBreakIndex == SampleSize` exactement (pas seulement `< SampleSize`) — un cas qui, non gardé, traduirait vers une barre **future**. Documenté ici comme un piège précis pour toute future implémentation ; l'audit l'a détecté et exclu explicitement (0 occurrence mesurée, jamais garanti à 0 sur un autre dataset).

---

# 13. CAUSALITY

**PASS**, reconfirmé sans changement depuis le Lot 15.2 (code inchangé) : `Cusum`/`BaiPerron` ne lisent jamais une barre au-delà de la barre courante. La propriété "fraîcheur" (Bai-Perron, Lot 15.2) n'est PAS une violation de causalité — c'est une limite STATISTIQUE (moins de données de confirmation post-rupture disponibles pour une rupture récente dans la fenêtre), jamais un accès à une donnée future.

---

# 14. LOOK-AHEAD

| Information | Disponible à | Dépendance future |
|---|---|---|
| `Cusum.ChangeDetected`/`.Confidence`/`.EstimatedBreakIndex` | t (barre courante) | Aucune |
| `BaiPerron.BreakCount`/`.Confidence`/`.Breakpoints` | t | Aucune |
| `BreakAgeBars` (dérivé, hypothétique) | t (calculable dès aujourd'hui à partir des champs ci-dessus) | Aucune |
| Toute future `Freshness∈[0,1]` (non définie) | t, SI dérivée uniquement de `BreakAgeBars` | Aucune, à condition de ne jamais recalculer rétroactivement |

Aucune information StructuralBreak n'exige de donnée future — le risque n'est jamais le look-ahead, c'est l'**interprétation sémantique** (traiter une détection ancienne comme un événement actuel).

---

# 15. NORMALIZATION

`Cusum.Confidence` : sain, non saturé (Lot 15.2 : StdDev 0.017-0.107 par régime ; reconfirmé indirectement ici via les moyennes par régime : 0.9693 à 0.9992, un écart réel de 3 points de pourcentage entre régimes).
`BaiPerron.Confidence` : **saturé**, confirmé une seconde fois indépendamment (§11 : quasi-constant ≈1.0 dans toutes les tranches d'âge ; §Regime : 0.9997-1.0 dans les 5 régimes). Le phénomène de saturation du Lot 14.17 (autre contexte, Persistence) se reproduit ici pour une raison DIFFÉRENTE — pas l'hystérésis de `FusionStateManager` (ces champs ne sont jamais lissés puisqu'ils ne sont jamais consommés), mais la formule `1-exp(-0.5×x)` elle-même qui sature rapidement dès que `bicImprovement` dépasse quelques unités, ce qui semble être le cas quasi systématiquement sur 128 barres M5 réelles.

---

# 16. EVIDENCE FUSION

Cartographie inchangée depuis le Lot 15.2 (code non modifié, reconfirmé §Contexte) : `EvidenceFusionEngine` (Fusion) consomme exactement 4 `IFusionRule` (`StationarityRule`←Adf+Kpss, `PersistenceRule`←Dfa+VarianceRatio, `MeanReversionRule`←HalfLife, `RandomWalkRule`←VarianceRatio) ; `StructuralStability` (5ᵉ dimension) est injectée séparément par `FusionStateManager.ReplaceStructuralStability`, dérivée de l'HISTORIQUE des 4 autres dimensions déjà stables (auto-référentielle), jamais de `Cusum`/`BaiPerron`. **Où StructuralBreak pourrait-il entrer ?** Comme une **nouvelle** `FusionDimension` (ex. `StructuralBreakEvidence`), consommée par une nouvelle `IFusionRule`, jamais en modifiant la définition de `StructuralStability` (voir §20). **Non câblé — cette section documente l'emplacement, ne l'implémente pas.**

---

# 17. DOUBLE COUNTING

| Evidence A | Evidence B | Dépendance possible |
|---|---|---|
| Cusum (30 barres) | BaiPerron (128 barres) | **Directe et significative** — la fenêtre de 30 barres de Cusum est un SOUS-ENSEMBLE des 128 dernières barres de BaiPerron ; une même rupture de niveau/variance peut légitimement déclencher les deux simultanément (confirmé : 93.24% "Both", §Overlap) — ce n'est pas un bug, mais les traiter comme deux evidences INDÉPENDANTES dans une future Fusion risquerait de compter deux fois le même événement de marché sous-jacent |
| Cusum/BaiPerron | StructuralStability (auto-référentielle) | **Indirecte** — une vraie rupture affecterait Adf/Kpss/Dfa/VarianceRatio/HalfLife (via Stationarity/Persistence/MeanReversion), donc la variabilité RÉCENTE de ces dimensions (mesurée par `StructuralStability`) — un même événement de marché pourrait donc influencer `StructuralBreakRule`'s score par DEUX voies distinctes si une nouvelle evidence Cusum/BaiPerron était ajoutée sans retirer/pondérer `StructuralStability` en conséquence |
| Cusum "variance run" | VolatilityEvidence (ACF \|rendements\|, jamais consommée) | Conceptuellement apparentées (les deux testent une forme d'instabilité de la variance) mais statistiquement DISTINCTES (test de rupture discrète vs autocorrélation de clustering) — risque de double comptage faible mais réel si les deux étaient un jour câblées ensemble |
| Dfa/Hurst | Cusum/BaiPerron | Faible — persistance long-terme vs rupture discrète, concepts orthogonaux |

**Conclusion** : le risque de double comptage le plus sérieux est **Cusum × BaiPerron entre eux** (fenêtres imbriquées, même source de prix) — tout futur contrat doit traiter cela explicitement (jamais les sommer/moyenner naïvement sans le justifier).

---

# 18. CONFLICT HANDLING

Cadre conceptuel proposé, **jamais un vote implémenté** :

```
Agreement    : Cusum ET BaiPerron détectent (93.24% du dataset)
Disagreement : un seul des deux détecte (BaiPerronOnly 6.74%, CusumOnly 0.02% — quasi-exclusivement asymétrique en faveur de BaiPerron, cohérent avec sa quasi-omniprésence)
Uncertainty  : détection présente mais timing éloigné — mesuré : quand les deux détectent, l'écart de localisation absolue moyen est de 8.9 barres (médiane 7, max 83) — un écart NON négligeable, suggérant que même les cas "Both" ne pointent pas toujours vers LE MÊME événement précis.
```

**Aucun vote arbitraire proposé** — préserver ces 3 catégories (ou un champ `Agreement` explicite, §25) comme information distincte, jamais les collapser en un score unique sans décision explicite future.

---

# 19. EVENT VS STATE

| | Event-based | State-based (retenu par défaut, cohérence architecturale) |
|---|---|---|
| Principe | Détections modélisées comme événements timestampés, avec décroissance temporelle explicite (type processus de Hawkes) | Sortie par barre alimentant directement une `FusionDimension`, lissée par le même EMA+hystérésis que les 4 autres dimensions |
| Avantage | Séparation propre détection/pertinence temporelle ; correspond naturellement à "Freshness" | Cohérent avec l'architecture Fusion existante ; aucune nouvelle infrastructure |
| Inconvénient | Aucune infrastructure d'événements n'existe dans `Engine.Fusion` aujourd'hui — devrait être construite de zéro | Risque de COMPOSER deux effets de retard : le retard déjà inhérent à Bai-Perron (fenêtre 128 barres rétrospective) PLUS le lissage EMA+hystérésis (déjà démontré geler jusqu'à 95%+ des mouvements, Lot 14.17) — un signal déjà en retard deviendrait encore plus en retard |
| Verdict | Non retenu, infrastructure absente | **Recommandé par défaut pour la cohérence**, MAIS le risque de double-retard doit être examiné explicitement par le futur lot d'implémentation avant tout câblage — pas supposé sans risque ici |

---

# 20. REPLACE VS AUGMENT

**Décision P0. AUGMENT retenu, jamais Replace.**

`StructuralStability` est consommée par **4 des 5** règles de Decision (`MeanRevertingRule` poids 0.10, `TrendingRule` poids 0.30, `StableRangeRule` poids 0.20, `StructuralBreakRule` poids 0.40 — seule `RandomWalkRule` ne l'utilise pas). **Remplacer** sa définition changerait simultanément le comportement des QUATRE régimes qui la consomment — bien au-delà du périmètre "corriger StructuralBreak", et contredirait la discipline "modification minimale" de tous les lots précédents. `StructuralStability` mesure par ailleurs un concept LÉGITIMEMENT DIFFÉRENT pour les 3 autres règles ("la classification de régime récente est-elle stable ?" — une méta-stabilité de la Fusion elle-même) — la remplacer par une mesure de rupture de PRIX conflaterait deux concepts distincts.

**Augmenter** signifie : ajouter une **nouvelle** `FusionDimension` dédiée (ex. `StructuralBreakEvidence`), consommée **uniquement** par `StructuralBreakRule` (ou toute règle où une justification explicite serait apportée), sans toucher `StructuralStability` ni les 3 autres règles. Coût : nécessite un nouveau poids dans `StructuralBreakRule` (actuellement 0.40+0.30+0.20+0.10=1.00 exactement) — une décision de PONDÉRATION, pas une calibration de performance, mais une décision architecturale explicite qu'un futur lot devra prendre consciemment (jamais dans ce lot).

---

# 21. MARKETSTATE SEMANTICS

`MarketState.StructuralBreak` est produit par `DecisionArbitrator.Arbitrate` — l'argmax d'un score PAR BARRE parmi 5 candidats. C'est structurellement une **CLASSIFICATION** (quel régime les evidences de CETTE barre ressemblent-elles le plus), jamais un **ÉVÉNEMENT** (quelque chose qui vient de se produire) ni un **ÉTAT ontologique** (le marché "est" dans tel régime). Ambiguïté sémantique réelle et non résolue par l'architecture actuelle : rien ne distingue actuellement "le marché ressemble à une rupture structurelle CE bar" de "une rupture structurelle authentique est en cours" — la donnée de transition du Lot 15.0 (auto-persistance 92%, flux bidirectionnel avec MeanReverting 203/193) suggère empiriquement un comportement plus proche d'un ÉTAT durable que d'un événement ponctuel, mais ceci reste descriptif, jamais un design.

---

# 22. REGIME INTERACTION

`MarketState.Transitional` reste structurellement inatteignable (Lots 15.0-15.2, reconfirmé ici : N=0 dans ce dataset). Aucune distinction actuelle entre "rupture = événement isolé", "rupture = transition vers un nouveau régime durable", et "rupture = faux signal" — l'architecture actuelle n'a tout simplement pas les moyens de les distinguer (pas de mécanisme de confirmation/persistance dédié, §9). `StructuralBreak ≠ Transitional` par construction (l'un est atteignable, l'autre non) — ne jamais les assimiler.

---

# 23. DATASET ANALYSIS

Dataset Yahoo MES M5, 11 549 barres brutes, 2026-06-28→2026-08-26, `ReadyBars=11421`, `ExceptionCount=0`, aucune anomalie d'index détectée (`CusumIndexAnomalyCount=0`, `BaiPerronIndexAnomalyCount=0` — le cas limite du §12 ne s'est pas matérialisé sur ce pull).

**Overlap/accord (définition : "détecté" = `ChangeDetected` pour Cusum, `BreakCount>0` pour BaiPerron)** :

| Catégorie | N | % |
|---|---:|---:|
| Both | 10 649 | 93.241% |
| BaiPerronOnly | 770 | 6.742% |
| CusumOnly | 2 | 0.018% |
| Neither | 0 | 0% |

**Timing du désaccord** (barres où les deux détectent, écart de localisation absolue, n=10 649) : Mean=8.891 barres, Médiane=7, Max=83, StdDev=9.295.

**Distribution de `BreakAgeBars`** (Bai-Perron, n=11 419) : Mean=21.08, Médiane=17, P10=11, P25=12, P75=25, P90=37, Max=100.

**Table croisée âge × Confidence** : [0,20)→0.999993 (n=6803), [20,50)→0.999998 (n=4165), [50,90)→1.0 (n=443), [90,127]→1.0 (n=8). `Pearson(BreakAgeBars, Confidence)=0.003343`.

**Clustering (longueur de séquence du booléen de détection)** : Cusum — 340 séquences, longueur moyenne 31.3, médiane 21, max 235. BaiPerron — **2 séquences seulement**, longueur moyenne 5709.5, max 11 357.

---

# 24. REGIME CONDITIONAL ANALYSIS

| Régime | N | %CusumDét. | %BPDét. | MeanCusumConf | MeanBPConf | MeanBPBreakCount | MeanBreakAge | Fiabilité |
|---|---:|---:|---:|---:|---:|---:|---:|---|
| MeanReverting | 6824 | 89.58% | 99.97% | 0.9693 | 0.9997 | 5.023 | 21.97 | OK |
| StructuralBreak | 3532 | **99.69%** | 100% | **0.9992** | 1.0 | 5.072 | 19.99 | OK |
| RandomWalk | 499 | 96.99% | 100% | 0.9904 | 1.0 | 5.381 | 18.01 | OK |
| Trending | 453 | 94.70% | 100% | 0.9864 | 1.0 | 5.764 | 19.46 | OK |
| StableRange | 113 | 92.03% | 100% | 0.9784 | 1.0 | 6.044 | 21.01 | OK (N≥30, mais historiquement le régime le plus rare) |
| Transitional | 0 | — | — | — | — | — | — | LOW_N_UNRELIABLE (inatteignable) |
| Unknown | 0 | — | — | — | — | — | — | LOW_N_UNRELIABLE (jamais observé) |

**Observation notable, jamais interprétée causalement (dataset limité)** : `StructuralBreak` a le %CusumDétecté et la CusumConfidence les plus ÉLEVÉS de tous les régimes — la SEULE evidence qui montre une différenciation cohérente avec le nom du régime. `BaiPerron` (BreakCount/Confidence) ne différencie quasiment rien entre régimes (100% ou quasi partout).

---

# 25. PROPOSED EVIDENCE CONTRACT

Proposition, **chaque champ justifié individuellement**, aucun repris automatiquement de l'exemple du brief :

```
StructuralBreakEvidence (PROPOSÉ, NON IMPLÉMENTÉ) :

CusumDetected        : bool           — pass-through direct de Cusum.ChangeDetected. Justifié : booléen
                                         DÉJÀ discriminant (340 runs), ne jamais le reconstruire.
CusumConfidence       : double [0,1]   — pass-through direct. Justifié : sain, non saturé (§15).
BaiPerronBreakCount   : int >= 0       — pass-through direct. Justifié : seul signal BaiPerron montrant
                                         une vraie variation (gradient régime, Lot 15.2).
BreakAgeBars          : int? >= 0      — dérivé (§11 formule), NULL si aucune rupture BaiPerron. Justifié :
                                         donnée brute causale déjà calculable ; PAS de Freshness normalisée
                                         (GAP explicite, §11) tant qu'aucune règle de décroissance n'est
                                         scientifiquement fondée.
Agreement             : enum {Both, CusumOnly, BaiPerronOnly, Neither} — Justifié : préserve l'information
                                         de désaccord (§18) au lieu de la collapser arbitrairement.

CHAMPS EXPLICITEMENT NON PROPOSÉS, ET POURQUOI :
- BaiPerronConfidence : saturée (§8/§11/§24), scientifiquement non informative sur ce dataset — l'inclure
  fabriquerait une apparence de précision inexistante.
- StructuralBreakScore (scalaire composite unique) : nécessiterait une règle de pondération/agrégation
  qui n'existe pas et ne peut être justifiée sans calibration — hors mandat de ce lot (§26).
- Freshness∈[0,1] : GAP déclaré (§11), jamais fabriqué.
```

---

# 26. CONTRACT INVARIANTS

Invariants scientifiquement justifiés pour un futur lot d'implémentation :

- `CusumConfidence ∈ [0,1]` — déjà garanti par `CusumStatistics.Compute` (clamp existant).
- `BaiPerronBreakCount >= 0` — déjà garanti (jamais négatif par construction de la programmation dynamique).
- `BreakAgeBars >= 0` quand non-null — par construction (`CurrentBarIndex - AbsoluteBreakBar`, `AbsoluteBreakBar <= CurrentBarIndex` toujours, sauf le cas limite du §12 qui doit être explicitement gardé).
- `BreakAgeBars <= WindowSize - 1` (127 pour BaiPerron) — jamais observé au-delà sur ce dataset (max=100), mais devrait être un invariant TESTÉ, pas supposé.
- **PAS d'invariant sur `Agreement`** au-delà de son domaine énuméré (4 valeurs) — aucune contrainte de proportion n'est scientifiquement justifiable (dépend du marché, jamais fixe).

---

# 27. FUTURE IMPLEMENTATION BOUNDARY

**Fichiers qu'un futur lot toucherait probablement** : un NOUVEAU fichier `Engine/Fusion/Rules/StructuralBreakEvidenceRule.cs` (nouvelle `IFusionRule`, implémentant le contrat §25) ; une extension ADDITIVE de `FusionDimension` (nouvelle valeur enum) ; une extension du câblage de règles dans `BacktestEngine.RunSignalPipeline`/`IQIAIndicator.cs` (ajouter la nouvelle règle à la liste, déjà un pattern établi) ; une modification EXPLICITE et délibérée des poids de `StructuralBreakRule` (nouveau poids pour la nouvelle dimension, retrait proportionnel des poids existants — une DÉCISION, jamais une calibration).

**Fichiers qui ne devraient PAS être touchés** : `CusumEvidence.cs`/`CusumStatistics.cs`/`BaiPerronEvidence.cs`/`BaiPerronStatistics.cs` (algorithmes déjà validés, aucun défaut trouvé, Lot 15.2) ; `RegimeEngine.cs` (déjà expose ces evidences dans `EvidenceSet`, rien à ajouter) ; `StructuralStabilityRule.cs`/`FusionProfileAnalyzer.cs` (Replace rejeté, §20) ; `DecisionArbitrator.cs`/`FusionStateManager.cs`'s hystérésis (aucune preuve qu'ils doivent changer) ; `MeanRevertingRule.cs`/`TrendingRule.cs`/`StableRangeRule.cs`/`RandomWalkRule.cs` (aucun besoin identifié de leur donner accès à la nouvelle evidence, brief §19 non touché).

**La couche d'adaptation** (traduction `EstimatedBreakIndex`/`Breakpoints` locaux → `BreakAgeBars`, §12) devra vivre dans la NOUVELLE `IFusionRule`, jamais dans `RegimeEngine`/`CusumEvidence`/`BaiPerronEvidence` eux-mêmes (qui n'ont, par design, aucune notion d'index absolu — confirmé, ce sont des classes pures sur des fenêtres relatives).

---

# 28. CALIBRATION BOUNDARY

| Paramètre | Pourquoi calibrable | Ce qui doit être fixé scientifiquement AVANT |
|---|---|---|
| Fonction de décroissance Freshness (`BreakAgeBars→[0,1]`) | Nécessite un choix de forme (linéaire/exponentielle) et d'échelle | D'abord établir si Event-based ou State-based (§19) est retenu — la forme de décroissance dépend de ce choix |
| Poids de la nouvelle `FusionDimension` dans `StructuralBreakRule` | Détermine combien la nouvelle evidence pèse vs `(1-StructuralStability)` existant | D'abord confirmer Augment (§20, déjà fait) et le contrat exact (§25, déjà fait) |
| Seuil d'agrégation `Agreement` (si jamais collapsé en scalaire) | Nécessiterait une règle de vote | D'abord décider SI un collapse scalaire est même souhaitable (§18 le déconseille en l'état) |
| Paramètres EMA+hystérésis pour la nouvelle dimension | Pourrait réutiliser `StabilizationConfiguration.Default` (Alpha=0.20, Seuil=0.03) ou nécessiter ses propres valeurs | D'abord trancher le risque de double-retard identifié en §19 |
| Dataset requis | Toute calibration empirique de ce qui précède nécessiterait un historique bien plus long que 59 jours pour caractériser la dynamique réelle des ruptures | Aucune action possible avant une source de données étendue |

**Aucun de ces paramètres n'a été touché, sélectionné ou même esquissé numériquement dans ce lot.**

---

# 29. REMAINING RISKS

1. **`BaiPerron.BreakCount>0`/`.Confidence` sont quasi-inutilisables tels quels** — toute future implémentation DOIT utiliser `BreakCount` (magnitude) plutôt que la détection booléenne implicite ou `Confidence` (saturée).
2. **Risque de double comptage Cusum×BaiPerron** (§17) — fenêtres imbriquées, même source — non résolu, à traiter explicitement dans le contrat de Fusion futur.
3. **Risque de double-retard Event-vs-State** (§19) — non résolu, nécessite un examen dédié avant implémentation.
4. **Aucune Freshness défendable** (§11) — bloque toute intégration temporellement nuancée tant que non résolu.
5. **Ambiguïté sémantique MarketState** (§21) — classification vs événement vs état, non résolue par l'architecture existante, hors du périmètre de correction de ce lot.

---

# 30. RECOMMENDATION

**Ne pas câbler CUSUM/Bai-Perron dans la Fusion tant que (a) une décision Event-vs-State explicite n'a pas été prise, et (b) la question de la Freshness n'a pas été résolue ou explicitement acceptée comme absente du contrat v1.** Le contrat minimal proposé (§25 : `CusumDetected`, `CusumConfidence`, `BaiPerronBreakCount`, `BreakAgeBars` brut, `Agreement`) est suffisamment précis pour qu'un futur lot d'implémentation commence — Augment (§20), jamais Replace. Recommandé comme prochain lot si l'utilisateur approuve ce contrat.

---

# 31. HANDOFF CONTEXT

```
PROJECT:
IQIA

CURRENT LOT:
15.6

LAST COMPLETED:
15.5

CALIBRATION:
PAUSED

STRUCTURAL BREAK:
Full pipeline mapped (RegimeEngine -> Cusum/BaiPerron -> [unused] -> StructuralStability [self-referential,
unrelated] -> FusionStateManager -> MarketState.StructuralBreak -> UNSUPPORTED_REGIME). Code confirmed
UNCHANGED since Lot 15.2 (git status clean on all relevant paths).

CUSUM:
Contract fully documented. Produces ChangeDetected (bool) + Confidence (double, healthy/non-saturated,
StdDev 0.017-0.107 by regime) + EstimatedBreakIndex (LOCAL to its 30-bar window, never absolute) - three
distinct fields, never conflated. Causal (sequential Page's test). 340 detection runs on real data, median
run length 21 bars - genuinely dynamic, discriminant evidence.

BAI-PERRON:
Contract fully documented. BreakCount (int, unbounded, real cross-regime gradient) + Confidence (double,
SATURATED ~0.9997-1.0 everywhere, confirmed a second independent time this lot) + Breakpoints (list of
LOCAL indices). Causal in the strict sense but "detected" (BreakCount>0) is true on 99.98% of the real
dataset (only 2 detection runs total, one spanning 11357 consecutive bars) - NOT a rare/discriminant
event under this operational definition. Freshness property (Lot 15.2, synthetic) is real mechanically but
EMPIRICALLY INVISIBLE on real data (Pearson(BreakAgeBars,Confidence)=0.0033) because Confidence is
saturated - reinforces that Confidence must never be used as a Freshness proxy.

DETECTION:
Formally defined as the raw per-bar algorithmic output only (Cusum.ChangeDetected /
BaiPerron.BreakCount>0). Nothing more.

ACTIVE STATE:
Formally defined as the (currently unimplemented) end state of a 5-stage model: Detection -> Confirmation
-> Freshness -> Persistence -> Active Structural State. Only stage 1 exists today. Conflating Detection
with Active State would be scientifically indefensible given BaiPerron's 99.98% detection rate.

BREAK COUNT:
Confirmed informative (real cross-regime variation), never a Confidence proxy.

CONFIDENCE:
BaiPerron's Confidence confirmed saturated (this lot, independently of Lot 15.2's finding) - not usable as
a Fusion input as-is. Cusum's Confidence confirmed healthy/usable.

FRESHNESS:
NOT RESOLVED - explicitly declared a GAP, not fabricated. BreakAgeBars (raw, derived, causal) is
measurable today (mean 21.08 bars, median 17, on real data) but no decay function to a normalized
Freshness score is scientifically justified without either a much longer dataset or an explicit,
separately-approved domain convention.

TEMPORAL ALIGNMENT:
Cusum/BaiPerron report LOCAL window-relative indices, never absolute bar indices or timestamps - no
translation exists anywhere in production code (nothing consumes these fields today). A translation
formula was built for this audit only (report §11/§12) and must be re-implemented, in a NEW Fusion rule,
by any future implementation lot - never inside CusumEvidence/BaiPerronEvidence themselves (both are
pure, window-relative, stateless classes by design). A documented edge case
(EstimatedBreakIndex==SampleSize exactly, would translate to a future bar if unguarded) was found and
excluded in this audit's script (0 occurrences on this dataset, not guaranteed on another).

CAUSALITY:
PASS, reconfirmed, code unchanged since Lot 15.2.

LOOK-AHEAD:
PASS - no Structural Break information requires future data; the risk is semantic misinterpretation
(treating an old detection as current), never a causality violation.

NORMALIZATION:
Cusum.Confidence healthy. BaiPerron.Confidence saturated (formula 1-exp(-0.5*bicImprovement) saturates
fast on real 128-bar M5 windows) - confirmed via a second, independent measurement this lot.

DOUBLE COUNTING:
Cusum(30-bar window) and BaiPerron(128-bar window) windows are NESTED (same underlying price source) -
93.24% "Both" agreement confirms a single market event often triggers both; must be treated explicitly
(never summed/averaged naively) in any future Fusion contract. Indirect risk also identified between
Cusum/BaiPerron and the self-referential StructuralStability dimension (a real break would also perturb
Stationarity/Persistence/MeanReversion, which StructuralStability's variability measure would then also
pick up).

CONFLICT:
Agreement/Disagreement/Uncertainty framework proposed (Both/CusumOnly/BaiPerronOnly/Neither), NEVER an
arbitrary vote. When both detect, mean absolute location gap = 8.9 bars (median 7, max 83) - even
"agreeing" bars don't always point at the exact same event.

EVENT VS STATE:
Compared both architectures. State-based recommended by default for architectural consistency with the
existing 5-dimension Fusion model, BUT flagged with an unresolved compounded-staleness risk (Bai-Perron's
own retrospective-window lag PLUS FusionStateManager's EMA+hysteresis, which already freezes 95%+ of
movements per Lot 14.17, would stack) - a future implementation lot must examine this explicitly, not
assume it away.

REPLACE VS AUGMENT:
AUGMENT decided (not Replace) - StructuralStability is shared by 4 of 5 Decision rules
(MeanReverting 0.10, Trending 0.30, StableRange 0.20, StructuralBreak 0.40); replacing its definition
would change 4 regimes' behavior simultaneously, far beyond a StructuralBreak-scoped fix, and would
conflate two legitimately different concepts (fusion-output meta-stability vs price structural breaks).

MARKET STATE:
MarketState.StructuralBreak is a per-bar CLASSIFICATION (argmax of 5 Decision Rule scores), not an EVENT
and not an ontological STATE - a real, unresolved semantic ambiguity in the existing (unmodified)
architecture, documented but not fixed by this lot.

EVIDENCE FUSION:
Unchanged (confirmed via git diff): exactly 4 IFusionRule wired, StructuralStability self-referential and
separately injected. A new FusionDimension + IFusionRule is where StructuralBreak evidence would enter -
documented, not wired.

DECISION:
Unchanged. StructuralBreakRule's 4 weights (0.40/0.30/0.20/0.10) sum to exactly 1.00 today - adding a 5th
input requires an explicit, non-performance-driven reweighting decision by a future lot.

SIGNAL:
NOT MODIFIED - StructuralBreak remains UNSUPPORTED_REGIME (Lot 15.1), unaffected by this lot.

ENTRY:
NOT MODIFIED.

RISK:
NOT MODIFIED.

EXECUTION:
NOT MODIFIED.

CALIBRATION PARAMETERS:
None touched. Full list of FUTURE-calibratable parameters and their scientific prerequisites documented
in report §28 (Freshness decay function, new-dimension Fusion weight, Agreement collapse rule if any,
EMA/hysteresis parameters for the new dimension) - none selected, none estimated, none implied as
"probably good".

PRODUCTION CHANGES:
NONE

P0 REMAINING:
StructuralBreak evidence integration remains open, now with a fully documented scientific contract
(report §25) and an explicit Augment decision (§20) - the ONLY thing blocking implementation is (a) user
approval of the proposed contract/decision, and (b) a dedicated implementation lot resolving the
Event-vs-State compounded-staleness risk (§19) and either building or explicitly deferring Freshness
(§11). No other P0 remains from the entire Lot 15.0-15.6 audit lineage.

NEXT LOT:
A "Structural Break Evidence Implementation" lot, scoped EXACTLY to: (1) a new FusionDimension +
StructuralBreakEvidenceRule (Engine/Fusion/Rules/, new file) implementing the §25 contract
(CusumDetected/CusumConfidence/BaiPerronBreakCount/BreakAgeBars/Agreement), (2) an explicit,
user-approved reweighting of StructuralBreakRule's 4 existing weights to accommodate a 5th input,
(3) a resolved decision on Event-vs-State (default recommendation: State-based, but only after examining
the compounded-staleness risk), (4) Freshness either built on an explicit, approved convention or
formally deferred (never silently normalized without one) - all wiring changes confined to NEW files plus
the existing rule-list construction sites (BacktestEngine.cs/IQIAIndicator.cs), never touching
CusumEvidence/BaiPerronEvidence/StructuralStabilityRule/RegimeEngine/the other 4 Decision Rules.

DO NOT DO:
Do NOT wire Cusum/BaiPerron into EvidenceFusionEngine without explicit user approval of this lot's
proposed contract (§25) - a fabricated relation was exactly what Lot 15.2's brief forbade, and remains
forbidden here. Do NOT use BaiPerron.BreakCount>0 or .Confidence as a proxy for "structural break is
happening now" - both are confirmed non-discriminant/saturated on real data. Do NOT invent a Freshness
decay function to "complete" the contract - it is an explicit, documented GAP, not an oversight to patch
silently. Do NOT modify StructuralStabilityRule/FusionProfileAnalyzer to "fix" StructuralBreak - the
Replace option was rejected on architectural grounds (4-regime blast radius), not performance. Do NOT sum
or average Cusum and BaiPerron signals naively - their windows are nested (double-counting risk, §17),
never treated as independent. Do NOT assume the State-based Event-vs-State choice is risk-free - the
compounded-staleness concern (§19) must be examined by whoever implements this, not assumed away because
it matches the existing architecture's shape.
```

**STOP.**
