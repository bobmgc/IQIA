# QDE-012 — Sprint 15.25 — Lot 15.2 — Structural Break Evidence Integration Audit & Correction

**Date** : 2026-08-24
**Branche** : `feature/structural-stability-v2`
**Lot précédent** : 15.1
**Statut** : **IMPLEMENTED**
**Type** : **AUDIT + CORRECTION STRUCTURELLE CONDITIONNELLE — conclusion : aucune correction de production justifiée ce lot**

---

# 1. EXECUTIVE SUMMARY

Le Lot 15.0 a découvert que `Cusum`/`BaiPerron` sont calculées à chaque barre mais jamais consommées par `EvidenceFusionEngine`. Ce lot avait pour mandat d'auditer précisément cette chaîne et de la corriger **si et seulement si** un défaut de consommation/propagation était démontré — jamais de calibrer, jamais de fabriquer une corrélation, jamais d'inventer une relation que le système ne définit pas déjà.

**Conclusion, établie par audit de code (algorithmes tracés ligne à ligne) puis vérifiée empiriquement (tests synthétiques + réels)** : **aucune modification de production n'est justifiée dans ce lot.** Deux raisons indépendantes, chacune suffisante seule :

**1. Aucun contrat scientifique n'existe pour combiner ces evidences.** Il n'existe nulle part dans le dépôt de formule, de poids ni de convention pour transformer `CusumResult`/`BaiPerronResult` en une valeur `FusionDimension` — contrairement à `Stationarity` (Adf+Kpss via une transformée logistique documentée) ou `Persistence` (Dfa+VarianceRatio via une formule explicite, Lot 14.17). En inventer une maintenant serait exactement la "nouvelle relation monotone" que le brief §12 interdit d'inventer sans contrat préexistant — **DOCUMENTÉ, pas fabriqué.**

**2. Empiriquement, `BaiPerron.Confidence` — le champ `[0,1]` qui serait le candidat naturel pour un score Fusion — est quasi-constant en production, donc quasi-inutile comme discriminant.** Mesuré sur 10 931 barres réelles : `StdDev(BaiPerron.Confidence)` = **0.0000 exactement** dans `RandomWalk`/`Trending`/`StableRange`, 0.000014 dans `StructuralBreak`, 0.0176 dans `MeanReverting` — la moyenne est **1.0000** dans 4 des 5 régimes. `Cusum.Confidence` varie davantage (StdDev 0.017-0.107) mais reste concentré près de 1.0 (moyennes 0.981-0.999). Le score hypothétique d'ablation (illustratif, jamais optimisé — `0.5×Cusum.Confidence + 0.5×BaiPerron.Confidence`) corrèle **faiblement** avec le score `StructuralBreak` réel (Pearson=0.111, Spearman=0.157, point-bisérial avec le statut gagnant=0.145) — une information positive mais ténue, dominée par la saturation de `BaiPerron.Confidence`.

**Causalité** : `CUSUM` (test séquentiel de Page, calibré sur le premier quart de sa fenêtre puis balayé en avant uniquement) est **causal sans réserve**. `Bai-Perron` (segmentation multiple par programmation dynamique, optimisation globale sur toute sa fenêtre de 128 barres) est **causal au sens strict** (`output[t]` ne lit jamais une barre `>t`, prouvé à 3 niveaux : unité, reconstruction de fenêtre, pipeline complet tronqué vs étendu) — **mais** possède une propriété statistique réelle et démontrée : sa confiance en une rupture proche du bord de fenêtre (rupture "récente") est structurellement plus faible que pour une rupture équivalente au milieu de la fenêtre (Confidence=0.759 vs 0.9999986 pour un même saut d'amplitude, démontré §6/§13 Cas B/C). Ce n'est pas un bug — c'est une propriété inhérente à tout détecteur de rupture rétrospectif — mais cela renforce la prudence avant de le traiter comme une evidence "temps réel" fiable.

**Aucun bug trouvé** dans `CusumMath`/`CusumStatistics`/`BaiPerronRegression`/`BaiPerronStatistics` — inspection ciblée, aucune anomalie.

**Résultat** : ce lot est un audit complet, avec cartographie exhaustive, contrat Evidence→Fusion documenté précisément, ablation descriptive (test uniquement), tests synthétiques (Cas A-F), preuves de look-ahead/déterminisme/isolation — mais **zéro ligne de production modifiée**. `EvidenceFusionEngine`, `FusionStateManager`, `RegimeEngine`, l'algorithme CUSUM et l'algorithme Bai-Perron restent strictement inchangés.

---

# 2. LOT 15.0 FINDINGS (repris, contexte)

- `StructuralBreak` ≈ 30.9% du dataset, deuxième régime le plus fréquent.
- `Cusum`/`BaiPerron` calculées chaque barre par `RegimeEngine.Collect`, jamais consommées par les 4 `IFusionRule` câblées.
- `FusionDimension.StructuralStability` n'est pas dérivée d'evidence — auto-référentielle, calculée par `StructuralStabilityRule.EvaluateAnalysis` à partir de la variabilité récente des 4 AUTRES dimensions Fusion elles-mêmes, injectée directement dans `FusionStateManager`.
- `Dfa(Hurst)` et `BaiPerron(BreakCount)` montrent un gradient monotone par régime (BreakCount : MeanReverting 4.98 → StableRange 6.04) — un signal discriminant potentiellement jeté.

# 3. LOT 15.1 FINDINGS (repris, contexte)

- Seul `MeanReverting` produit un signal directionnel — confirmé, non affecté par ce lot.
- `EntryTriggerReason.UNSUPPORTED_REGIME` rend le blocage explicite pour les 4 autres régimes.
- Aucune modification de Fusion/Decision au Lot 15.1 — ce lot 15.2 est le premier à toucher potentiellement `EvidenceFusionEngine`/`FusionStateManager`, et conclut à ne pas le faire.

---

# 4. EVIDENCE INVENTORY

Recherche exhaustive (`RegimeEngine.Collect`, `EvidenceSet.cs`) — 9 evidences produites, aucune `ADX` (recherché explicitement, 0 résultat) :

| Evidence | Producteur | Entrée | Fenêtre/Min | Sortie clé | Classification |
|---|---|---|---|---|---|
| Adf | `AdfEvidence` | `EvidenceContext.Series` | 60/30 | `PValue`, `IsStationary` | **PRODUCED → NORMALIZED → FUSED → CONSUMED** (Stationarity) |
| Kpss | `KpssEvidence` | `EvidenceContext.Series` | 60/30 | `PValue`, `IsStationary` | **CONSUMED** (Stationarity) |
| Dfa | `DfaEvidence` | `EvidenceContext.Series` | 128/80 | `Hurst` | **CONSUMED** (Persistence) |
| HalfLife | `HalfLifeEvidence` | `EvidenceContext.Series` | 30/20 | `HalfLife`, `RSquared` | **CONSUMED** (MeanReversion) |
| VarianceRatio | `VarianceRatioEvidence` | `EvidenceContext.Series` | 30/20 | `VarianceRatio`, `ZStatistic`, `PValue` | **CONSUMED** (Persistence ET RandomWalk) |
| Hurst (brut) | `HurstEvidence` | `MarketContext` (buffer interne propre, 30/20) | 30/20 | `HurstProxy` (VR-lag-2) | **PRODUCED → UNUSED** (aucun consommateur Fusion ; seul un compteur de validité dashboard le lit) |
| Volatility | `VolatilityEvidence` | `MarketContext` (buffer interne propre, 30/20) | 30/20 | `AcfAbsReturns`, `IsClustering` | **PRODUCED → UNUSED** (idem) |
| **Cusum** | `CusumEvidence` | `EvidenceContext.Series` | **30/20** | `Confidence`, `ChangeDetected`, `EstimatedBreakIndex` | **PRODUCED → NORMALIZED (Confidence∈[0,1]) → UNUSED** (aucune `IFusionRule` ne le lit) |
| **BaiPerron** | `BaiPerronEvidence` | `EvidenceContext.Series` | **128/64** | `Confidence`, `BreakCount`, `Breakpoints` | **PRODUCED → NORMALIZED (Confidence∈[0,1]) → UNUSED** (idem) |

Note architecturale mineure : `Hurst`/`Volatility` utilisent un buffer circulaire interne à la classe elle-même (champ `_buf`, reset sur `IsFirstBar`), tandis que les 7 autres evidences reçoivent une tranche déjà découpée via `EvidenceContext.Series` (construite par `RegimeEngine.BuildEvidenceContext` à partir du buffer partagé `_priceBuffer`) — deux conventions de calcul coexistent, sans incohérence fonctionnelle démontrée, mais un détail à connaître pour tout futur audit.

`FusionDimension.StructuralStability` = **EXPERIMENTAL** (au sens brief §2) : ni PRODUCED comme les autres (pas de classe Evidence dédiée), ni directement CONSOMMÉE d'une evidence — calculée à partir de l'HISTORIQUE des 4 autres dimensions FUSED elles-mêmes (`FusionProfileAnalyzer`).

`FusionConfiguration.cs` (`Weights`/`Thresholds`/`EnableCalibration`) : **DEAD** — recherche exhaustive, 0 référence ailleurs dans le dépôt ; `EvidenceFusionEngine` prend une liste de règles directement, jamais cette classe. Non corrigé (hors mandat, aucun défaut de propagation démontré, juste du code mort inerte).

---

# 5. CUSUM AUDIT

**Calcul** (`CusumStatistics.Compute`, `Engine/Regime/Evidence/CUSUM/`) : test CUSUM de Page à deux composantes (niveau + variance), calibré sur le premier quart de la fenêtre (`calibrationSize = max(4, N/4)`), puis balayage **strictement en avant** (`for i = calibrationSize; i < N`), chaque pas ne lisant que `series[0..i]`. Sélectionne la composante (niveau ou variance) la plus significative (`SelectMostSignificantRun`).

**Fenêtre** : 30 barres, minimum 20 (`RegimeEngine.CusumWindowSize`/`CusumMinimumSampleSize`).

**Paramètres** : `referenceValue = σ√(2·ln(N)/N)/2` (référence de dérive), `threshold = σ√(2N·ln(N))` (seuil de détection) — dérivés analytiquement de `N` et de l'écart-type de référence, jamais ajustés empiriquement dans le code.

**Normalisation** : `Confidence = clamp(peakMagnitude/threshold, 0, 1)` — un score continu, EN PLUS du booléen `ChangeDetected`. Sortie stockée sur `CusumResult` (record immuable), consommée nulle part hors du compteur de validité dashboard.

**Causalité** : PASS sans réserve — structure séquentielle par construction, aucune relecture arrière.

**Représentation produite** : à la fois `binary detection` (`ChangeDetected`) ET `continuous evidence score` (`Confidence`) — les deux existent simultanément, contrairement à ce que le brief §3 semblait envisager comme un choix exclusif.

---

# 6. BAI-PERRON AUDIT

**Calcul** (`BaiPerronStatistics.Compute`) : segmentation multiple par programmation dynamique — ajuste une régression OLS pour CHAQUE segment `[start,end)` possible dans la fenêtre (`BaiPerronRegression.TryFit`), puis sélectionne, par DP, la segmentation en `k` segments minimisant la RSS totale pour chaque `k`, puis choisit le `k` optimal au sens BIC (`bic = N·ln(RSS/N) + (3k-1)·ln(N)`).

**Fenêtre** : 128 barres, minimum 64 (`RegimeEngine.BaiPerronWindowSize`/`BaiPerronMinimumSampleSize`) — la plus longue de toutes les evidences avec DFA.

**Paramètres** : `minimumSegmentSize = max(3, ⌈√N⌉)` (≈12 pour N=128) — dérivé de `N`, jamais ajusté empiriquement.

**Sortie** : `BreakCount` (entier), `Breakpoints` (indices locaux à la fenêtre), `Confidence = clamp(1-exp(-0.5·bicImprovement), 0, 1)`.

**Localisation** : les `Breakpoints` sont des indices RELATIFS à la fenêtre courante (0 à SampleSize-1), reconstruits par rétro-propagation (`ReconstructBreakpoints`) depuis la table `previousBreak`.

**Réellement utilisable en temps réel, OU uniquement expérimental/offline ?** — Réponse nuancée, établie empiriquement (Cas B/C, §13) :
- **Causalement PASS** : `output[t]` ne lit jamais `bars > t` — l'optimisation, bien que globale, ne porte que sur la fenêtre `[t-127, t]`, entièrement passée. Prouvé à 3 niveaux (unité, reconstruction manuelle de la convention de découpe de `RegimeEngine`, pipeline complet tronqué vs étendu).
- **Statistiquement, une réserve réelle et démontrée** : la confiance en une rupture dépend de la quantité de données de confirmation "post-rupture" DISPONIBLES DANS LA FENÊTRE. Une rupture proche du bord (récente) a mécaniquement moins de barres pour la confirmer statistiquement qu'une rupture centrale — démontré : à amplitude de saut identique (shift=2.0σ), une rupture tardive donne `Confidence=0.759` contre `Confidence=0.9999986` pour une rupture médiane (Cas B vs C, §13). Ce n'est pas un artefact d'implémentation — c'est une propriété générale des détecteurs de rupture rétrospectifs (offline par nature, même appliqués dans une fenêtre glissante causale) — mais elle signifie concrètement que **Bai-Perron est structurellement meilleur pour confirmer "il y a eu une rupture il y a quelque temps dans cette fenêtre" que pour détecter "une rupture est en train de se produire maintenant"**, ce qui est précisément le cas d'usage qu'un moteur de régime temps réel voudrait.
- **Conclusion** : ni "purement offline/inutilisable", ni "prêt à l'emploi sans réserve" — **utilisable causalement, mais avec un décalage de confiance inhérent qui doit être compris avant toute intégration en Fusion**, documenté ici pour la première fois avec preuve chiffrée.

---

# 7. CAUSALITY

**PASS pour les deux evidences**, prouvé à 3 niveaux méthodologiques (patronnés sur `Tests/Backtest/BacktestFoundationLookAheadTests.cs`/`Tests/Backtest/Pipeline/BacktestSignalPipelineLookAheadTests.cs`, la méthodologie de preuve déjà établie et acceptée par le projet, jamais réinventée) :

1. **Niveau unité** : `CusumEvidence.Compute`/`BaiPerronEvidence.Compute` ne lisent jamais `context.Series` au-delà de `context.SampleSize`.
2. **Reconstruction de la convention de découpe** : réplique exacte de `RegimeEngine.BuildEvidenceContext`'s slicing, confirmant que la fenêtre transmise ne contient jamais de barre postérieure à la barre courante.
3. **Pipeline complet, tronqué vs étendu** : `BacktestEngine.Run()` sur une série synthétique, une fois tronquée à la barre N, une fois avec des barres supplémentaires ajoutées après N — `EvidenceSet.Cusum`/`.BaiPerron` de la barre N sont bit-identiques dans les deux cas.

Aucune violation détectée. Cette preuve n'existait pour aucune des deux evidences avant ce lot — **nouvelle, additive**, jamais assertée par un test antérieur.

---

# 8. NORMALIZATION

Toutes deux exposent un `Confidence ∈ [0,1]` — même échelle nominale que les 5 `FusionDimension` existantes. Mais l'audit empirique (§13, dataset réel 10 931 barres) révèle une **différence de comportement radicale entre les deux** :

| Evidence | Régime | N | Mean(Confidence) | StdDev(Confidence) |
|---|---|---:|---:|---:|
| Cusum | MeanReverting | 6497 | 0.9694 | **0.1068** |
| Cusum | StructuralBreak | 3383 | 0.9991 | 0.0172 |
| Cusum | RandomWalk | 485 | 0.9915 | 0.0550 |
| Cusum | Trending | 450 | 0.9863 | 0.0696 |
| Cusum | StableRange | 116 | 0.9808 | 0.0841 |
| BaiPerron | MeanReverting | 6497 | 0.99968 | **0.0176** |
| BaiPerron | StructuralBreak | 3383 | 1.00000 | 0.000014 |
| BaiPerron | RandomWalk | 485 | **1.00000** | **0.0000 exactement** |
| BaiPerron | Trending | 450 | **1.00000** | **0.0000 exactement** |
| BaiPerron | StableRange | 116 | **1.00000** | **0.0000 exactement** |

**Saturation confirmée, sans ambiguïté, pour `BaiPerron.Confidence`** : constante à `1.0` dans 3 des 5 régimes (variance NULLE), quasi-constante dans les 2 autres. Sur ce dataset (M5, fenêtre 128 barres), le marché produit systématiquement assez d'amélioration BIC pour saturer la transformée `1-exp(-0.5·x)` au-delà de la précision de `double` — la formule de normalisation elle-même n'est pas cassée (elle réagit bien en régime synthétique contrôlé, Cas B/C), mais sur des données de marché réelles à cette échelle temporelle, elle ne discrimine plus rien. `Cusum.Confidence` reste dispersée (StdDev 0.017-0.107) — un candidat structurellement plus sain si une intégration future était envisagée, MAIS `BreakCount` de BaiPerron (jamais borné à [0,1], donc jamais saturé de la même manière) reste, lui, le signal qui montrait le gradient monotone intéressant au Lot 15.0 (§2) — suggérant que si une intégration était un jour tentée, ce serait `BreakCount` (normalisé différemment) plutôt que `Confidence` tel quel qui mériterait d'être exploré, une observation qui reste hors du mandat de correction de ce lot.

Aucun `fallback`/`missing value` observé sur ce dataset (100% `IsValid` pour les deux evidences sur tous les régimes, `%Valid=100` dans le CSV) — le mécanisme "Missing Evidence" n'a pas été exercé ici (cohérent avec Lot 15.0/14.17).

---

# 9. EVIDENCE → FUSION CONTRACT

| Evidence | Calculée | Causale | Normalisée | Fusionnée | StructuralBreak |
|---|---|---|---|---|---|
| CUSUM | OUI (fenêtre 30) | **OUI (prouvé §7)** | OUI (`Confidence∈[0,1]`, non saturée) | **NON** | **NON — aucun contrat n'existe** |
| Bai-Perron | OUI (fenêtre 128) | **OUI (prouvé §7), avec réserve de fraîcheur documentée §6** | OUI (`Confidence∈[0,1]`, **saturée en production**) | **NON** | **NON — aucun contrat n'existe** |
| Persistence | OUI (Dfa+VarianceRatio) | OUI | OUI (formule documentée, Lot 14.17) | **OUI** | Indirect (poids négatif, 0.30 dans StructuralBreakRule) |
| Hurst (brut) | OUI | OUI | OUI | **NON** | NON |
| Volatility | OUI | OUI | OUI | **NON** | NON |
| StructuralStability | **NON (auto-référentielle)** | N/A | OUI | **OUI** | Directe (poids 0.40, la plus lourde de `StructuralBreakRule`) |

**Constat central** : `StructuralBreakRule` (Decision) donne le poids le PLUS ÉLEVÉ (0.40, via `(1-StructuralStability.Value)`) à la SEULE dimension qui n'est PAS dérivée d'une evidence de marché indépendante — elle mesure la stabilité récente du pipeline Fusion lui-même. Les deux evidences explicitement conçues pour détecter une rupture structurelle (`Cusum`, `BaiPerron`) contribuent 0.00 au score qui porte leur nom.

---

# 10. EVIDENCEFUSIONENGINE

Non modifié. Audité (déjà lu intégralement aux Lots 15.0/15.1, reconfirmé ici) : construit avec exactement `[StationarityRule, PersistenceRule, MeanReversionRule, RandomWalkRule]` (`BacktestEngine.RunSignalPipeline`/`IQIAIndicator.cs`, identiques) — 4 règles, jamais 5. Aucune règle `StructuralBreakEvidenceRule`/`CusumRule`/`BaiPerronRule` n'existe dans `Engine/Fusion/Rules/`. Poids : chaque `IFusionRule` calcule sa propre dimension indépendamment (pas de pondération croisée au niveau du moteur de fusion lui-même — la pondération n'intervient qu'en aval, dans les `Decision.Rules`). Fallback : valeur neutre 0.5 si `IsAvailable=false` (jamais observé ici, evidences 100% valides). Consensus/conflit : non géré au niveau Fusion — chaque dimension est indépendante, le "conflit" éventuel n'apparaît qu'en aval dans `Decision`.

---

# 11. FUSIONSTATEMANAGER

Non modifié. `StructuralStability` suit une trajectoire différente des 4 autres dimensions dans `BuildStableResult` (`Engine/Fusion/State/FusionStateManager.cs`, déjà lu intégralement au Lot 15.0) : `if (dimension == StructuralStability) newConfidence = previousConfidence;` — elle n'est PAS smoothée par EMA/hystérésis comme les autres ; sa valeur "stable" est écrasée après coup par `ReplaceStructuralStability(stableResult, stabilityConfidence)`, où `stabilityConfidence` vient de `StructuralStabilityRule.EvaluateAnalysis(FusionProfileAnalyzer.Analyze(snapshot))` — c'est-à-dire de l'HISTORIQUE des snapshots STABLES déjà produits (incluant les 4 autres dimensions déjà lissées). Aucune donnée brute Cusum/BaiPerron n'entre jamais dans ce calcul, à aucune étape.

**L'hystérésis (seuil 0.03, Lot 14.17) influence-t-elle `StructuralBreak` de manière excessive ?** N'a pas pu être mesurée directement sur `StructuralStability` elle-même (pas de valeur RAW comparable — elle n'est jamais lissée par EMA/hystérésis, contrairement aux 4 autres dimensions, elle est directement remplacée). Ce n'est donc PAS un cas d'hystérésis excessive au sens du Lot 14.17 — c'est un mécanisme structurellement différent (remplacement, pas lissage). Non recalibré, non modifié.

---

# 12. STRUCTURALBREAK COVERAGE

Rejoué sur le dataset réel (observation uniquement, aucun paramètre choisi à partir des résultats) :

| | Lot 15.0 | Lot 15.1 | Lot 15.2 (ce jour) |
|---|---:|---:|---:|
| N barres | 3375 | 3367 | 3383 |
| % | 30.88% | 30.80% | 30.95% |
| Runs | 263 | — | 264 |
| AvgRun | 12.83 | — | 12.81 |
| MedianRun | 11 | — | 11 |
| LongestRun | 79 | — | 79 |

Écarts cohérents avec la dérive de fenêtre Yahoo glissante déjà documentée (mémoire projet) — jamais une régression, la stabilité du `LongestRun` (79 dans les 2 mesures disponibles) et du profil de distribution des runs le confirme.

---

# 13. ABLATION

**Test uniquement, jamais de production** — `StructuralBreakEvidenceAblationLot152Tests.cs`, réplique le pattern Lot 15.0/14.17 (double marche `EvidenceFusionEngine`+`FusionStateManager` parallèle, jamais la production réelle modifiée).

**Cas synthétiques A-F** (`StructuralBreakEvidenceSyntheticTests.cs`, 7 tests, valeurs réelles) :

| Cas | Série | Cusum.Confidence | Cusum.ChangeDetected | BaiPerron.Confidence | BaiPerron.BreakCount |
|---|---|---:|---|---:|---:|
| A — pas de rupture (bruit blanc) | flat/bruit | 0.575 | false | 0.000 | 0 |
| B — rupture tardive, shift=3.0 | saut proche du bord | 1.000 | **true** | (voir balayage) | — |
| B — rupture tardive, shift=2.0 | idem, amplitude réduite | — | — | **0.759** | 1 (breakpoint estimé 112, vrai 120 — `minimumSegmentSize=12` empêche une localisation plus précise près du bord) |
| C — rupture médiane, shift=2.0 | saut au milieu de fenêtre | 1.000 | true | **0.9999986** | 1 (breakpoint exact = 64) |
| D — rupture large, multi-segments | plusieurs sauts nets | 1.000 | true | 1.000 | >1 |
| E — sous le minimum d'échantillon | trop courte | `IsValid=false` | — | `IsValid=false` | — |
| F — dérive lente, non abrupte | tendance lisse continue | 1.000 (détecte) | true | — | **8** (sur-segmentation) |

**Propriété de fraîcheur confirmée numériquement** (Cas B vs C, même amplitude 2.0σ) : `Confidence` BaiPerron **0.759 (rupture tardive) vs 0.9999986 (rupture médiane)** — un écart de 24 points de confiance pour un signal de marché identique, uniquement dû à la position dans la fenêtre. Un balayage complémentaire (amplitude 0.5 à 3.0, documenté en commentaire de test) montre une saturation de `Confidence→1.0` dès `shift≥2.5`, et une absence totale de détection pour un saut tardif `<2.0σ`.

**Cas F, désaccord non forcé, honnêtement rapporté** : sur une dérive lente et continue (pas de rupture nette), CUSUM la détecte comme une rupture de niveau (`Confidence=1.0`) tandis que Bai-Perron sur-segmente (`BreakCount=8`, essayant de découper une tendance continue en multiples segments linéaires) — ni l'un ni l'autre n'est "faux" au sens strict (une dérive continue N'EST PAS un régime stationnaire), mais leurs représentations divergent fortement — une limitation documentée, pas un bug.

**Ablation descriptive sur données réelles** (10 931 barres, score hypothétique `0.5×Cusum.Confidence + 0.5×BaiPerron.Confidence` — **split arbitraire, illustratif, jamais recherché ni optimisé**) :

```
Pearson(HypotheticalScore, StructuralBreak.FinalScore)      = 0.111207
Spearman(HypotheticalScore, StructuralBreak.FinalScore)     = 0.156782
PointBiserial(HypotheticalScore, Winner==StructuralBreak)   = 0.144535
```

Moyenne du score hypothétique par régime : MeanReverting 0.9845, StructuralBreak **0.9995** (la plus haute), RandomWalk 0.9958, Trending 0.9932, StableRange 0.9904 — un ordre cohérent avec l'intuition (StructuralBreak en tête) mais un écart TRÈS faible entre régimes (0.986 à 0.9995, une bande de 1.3 point de pourcentage) — largement expliqué par la saturation de `BaiPerron.Confidence` (§8) qui écrase la moitié du score hypothétique vers 1.0 partout.

**Réponse à la question du brief** ("la présence de cette evidence change-t-elle réellement le résultat de Fusion ?") : **l'information existe et pointe dans la bonne direction (corrélation positive, ordre par régime cohérent), mais son amplitude actuelle — telle que `Confidence` est normalisée aujourd'hui — est trop faible et trop saturée pour changer significativement un résultat de Fusion sur ce dataset.** Ce n'est pas une réponse "oui" ni "non" simple — c'est exactement la nuance que ce lot devait établir sans la forcer.

---

# 14. SYNTHETIC TESTS

Voir §13 pour le détail des Cas A-F. Fichier : `Tests/GoldenDatasets/StructuralBreakEvidenceSyntheticTests.cs`, 7 tests, tous PASS, utilisant exclusivement les vraies classes `CusumEvidence`/`BaiPerronEvidence` (jamais de réimplémentation). Aucun test ne passe par ATAS.

---

# 15. LOOK-AHEAD

**PASS**, prouvé à 3 niveaux (§7). Fichier : `Tests/GoldenDatasets/StructuralBreakEvidenceLookAheadTests.cs`, 7 tests. Les deux tests de référence préexistants (`BacktestFoundationLookAheadTests`, `BacktestSignalPipelineLookAheadTests`) ainsi que la suite complète `CusumValidationTests`/`BaiPerronValidationTests` (golden datasets, préexistants) ont été ré-exécutés avec succès, confirmant l'absence de régression sur les evidences elles-mêmes.

---

# 16. DETERMINISM

**PASS**. `CusumEvidence`/`BaiPerronEvidence` sont des fonctions pures de leur `EvidenceContext.Series` — aucun champ d'instance mutable (confirmé par inspection). Testé explicitement : même fenêtre → résultats bit-identiques.

---

# 17. RUN ISOLATION

**PASS**. Aucun état statique/partagé. Testé explicitement : séquences A/B/A entrelacées, confirmant qu'aucune fuite d'état n'affecte le second run de A.

---

# 18. BEFORE/AFTER OBSERVATION

Il n'y a pas de "après" au sens d'un changement de comportement — **aucune modification de production n'a été appliquée**. La seule différence observable entre "avant" et "après" ce lot est l'existence de nouvelles preuves (causalité, ablation, synthétique) et de nouveaux chiffres de couverture (§12, dérive de dataset normale). La distribution de régime n'a **pas** été et ne devait **pas** être une cible — conformément au brief §17, ce constat est une **observation**, ni un succès ni un échec.

---

# 19. REMAINING GAPS

- Aucun contrat scientifique Evidence→Fusion n'existe pour `Cusum`/`BaiPerron`/`Hurst`(brut)/`Volatility` — 4 evidences validées (golden datasets) et causales restent inertes en production.
- `BaiPerron.Confidence` est saturée en production à cette échelle temporelle (M5) — toute intégration future devrait explorer `BreakCount` (non borné, montre un gradient réel au Lot 15.0) plutôt que `Confidence` tel que normalisé aujourd'hui, ou une renormalisation différente de `Confidence` lui-même — une question de conception, pas de calibration, à trancher explicitement par un futur lot.
- La réserve de fraîcheur de Bai-Perron (§6) doit être prise en compte dans la conception de tout futur contrat — un score Fusion `StructuralBreak` basé naïvement sur `BaiPerron.Confidence` sous-détecterait systématiquement les ruptures récentes au profit des ruptures anciennes dans la fenêtre.
- `StructuralStability` reste auto-référentielle — aucune correction proposée ici (aucun défaut de propagation démontré dans SON FONCTIONNEMENT PROPRE, seulement l'absence d'un lien à `Cusum`/`BaiPerron` qui n'a jamais existé).
- `FusionConfiguration.cs` reste du code mort inerte — non corrigé (hors mandat, aucun impact comportemental).

---

# 20. PRODUCTION SAFETY

Aucune modification de fichier de production. `git diff --stat` confirme : aucun des fichiers protégés (`RiskEngine.cs`, `RiskPolicy.cs`, `InstrumentRiskSpecification.cs`, `RiskEngineRequest.cs`, `RiskAssessment.cs`, `TradePlanBuilder.cs`, `DecisionArbitrator.cs`, `RegimeEngine.cs`, `EvidenceFusionEngine.cs`, `FusionStateManager.cs`, `DecisionEngine.cs`, `SignalEngine.cs`, `EntryTriggerBuilder.cs`/`EntryTriggerAssessment.cs`, `CusumEvidence.cs`/`CusumStatistics.cs`/`CusumMath.cs`, `BaiPerronEvidence.cs`/`BaiPerronStatistics.cs`/`BaiPerronRegression.cs`) n'a été touché. 4 nouveaux fichiers, purement additifs :

- `Tests/GoldenDatasets/StructuralBreakEvidenceSyntheticTests.cs`
- `Tests/GoldenDatasets/StructuralBreakEvidenceLookAheadTests.cs`
- `Tests/Backtest/Calibration/StructuralBreakEvidenceAblationLot152Tests.cs`
- `Tests/Research/RegimeCoverageAudit/Output/structuralbreak_evidence_ablation_lot152.csv`

Aucun ordre. Aucune DLL. ATAS non utilisé. Aucun commit.

---

# 21. RECOMMENDED NEXT LOT

**Une décision explicite de l'utilisateur est nécessaire avant tout futur lot d'intégration** — ce lot ne recommande pas de configuration, seulement les questions à trancher :

1. Le futur contrat Fusion pour `StructuralBreak` doit-il **remplacer** `StructuralStability` (auto-référentielle) par une mesure dérivée de `Cusum`/`BaiPerron`, ou **l'augmenter** (une 6ᵉ dimension, ou une combinaison au niveau Decision) ?
2. Si `BaiPerron` est utilisé, faut-il transformer `BreakCount` plutôt que `Confidence` (saturée) — et avec quelle normalisation, documentée et non ajustée sur ce dataset ?
3. Comment traiter la réserve de fraîcheur (§6) — un décalage temporel volontaire, une pondération dégressive, ou l'acceptation explicite que l'evidence est plus fiable en rétrospective qu'en temps réel ?

Ce lot fournit toutes les données nécessaires (causalité prouvée, saturation mesurée, corrélation descriptive) pour que cette décision soit prise en connaissance de cause — mais elle reste hors du mandat de ce lot, qui interdit explicitement de fabriquer une relation non préexistante.

Rappel : les deux autres P0 hérités du Lot 15.0 (Stop Loss, régimes non supportés) restent indépendants et non traités ici.

---

# 22. HANDOFF CONTEXT

Voir bloc final ci-dessous.

---

# HANDOFF CONTEXT — NEXT SESSION

```
PROJECT:
IQIA

CURRENT LOT:
15.2

LAST COMPLETED:
15.1

CALIBRATION:
PAUSED

DATASET:
MES=F
M5
~59 days

STRUCTURAL BREAK:
3383/10931 Ready bars (30.95%), 264 runs, avg 12.81, median 11, longest 79 - consistent with Lot
15.0/15.1 within expected Yahoo rolling-window drift. StructuralStability (its Fusion dimension) remains
self-referential (derived from the OTHER 4 dimensions' own recent stability, via
StructuralStabilityRule+FusionProfileAnalyzer inside FusionStateManager) - unchanged, no evidence-based
alternative was wired in this lot.

CUSUM:
Fully causal (Page's sequential test, calibrated on window's first quarter, scans forward only - proven
at 3 levels: unit, window-reconstruction, full-pipeline truncated-vs-extended). Produces BOTH a boolean
ChangeDetected AND a continuous Confidence in [0,1] - the latter NOT saturated in production
(StdDev 0.017-0.107 across regimes, mean 0.981-0.999). Never consumed by any IFusionRule - confirmed
again this lot, no change. No bug found (CusumMath/CusumStatistics inspected, none found).

BAI-PERRON:
Causal in the strict sense (output[t] never reads bars>t, proven identically to CUSUM) but has a
demonstrated "freshness" property: identical-magnitude breaks near the trailing edge of its 128-bar
window get materially lower Confidence than breaks mid-window (0.759 vs 0.9999986 for a 2.0-sigma shift,
synthetic Case B vs C) - an inherent property of retrospective multi-breakpoint detectors, not a bug.
CRITICALLY: Confidence is SATURATED in production - StdDev=0.0000 EXACTLY in RandomWalk/Trending/
StableRange, 0.000014 in StructuralBreak, 0.0176 in MeanReverting; mean=1.0000 in 4/5 regimes. BreakCount
(unbounded integer) is NOT saturated the same way and showed a real cross-regime gradient in Lot 15.0
(4.98->6.04) - a future integration attempt should likely explore BreakCount over Confidence-as-is. Never
consumed by any IFusionRule - confirmed again, no change. No bug found (BaiPerronRegression/
BaiPerronStatistics inspected, none found).

EVIDENCE FUSION:
EvidenceFusionEngine unchanged - still exactly 4 IFusionRule (Stationarity/Persistence/MeanReversion/
RandomWalk), never a 5th for structural-break evidence. No FusionConfiguration.cs usage anywhere (0
references repo-wide) - confirmed dead scaffold, not touched (no behavioral impact, out of this lot's
mandate to clean up).

FUSION STATE:
FusionStateManager unchanged. StructuralStability is NOT smoothed by the same EMA+hysteresis as the other
4 dimensions - it's directly overwritten each bar via ReplaceStructuralStability from
StructuralStabilityRule.EvaluateAnalysis(FusionProfileAnalyzer.Analyze(snapshot)), which reads only the
already-smoothed history of the OTHER 4 dimensions, never Cusum/BaiPerron. Confirmed, unchanged.

HYSTERESIS:
Not applicable to StructuralStability directly (it's replaced, not EMA-smoothed) so Lot 14.17's
HysteresisThreshold=0.03 finding doesn't directly govern it - documented as a structural difference, not
recalibrated, not modified.

CAUSALITY:
PASS for both Cusum and BaiPerron, proven at 3 levels (unit / window-reconstruction / full-pipeline
truncated-vs-extended, patterned on BacktestFoundationLookAheadTests.cs and
BacktestSignalPipelineLookAheadTests.cs, both re-run successfully). New proof, did not exist before this
lot for either evidence type.

ABLATION:
Test-only, descriptive, 10931 real bars. Hypothetical score = 0.5*Cusum.Confidence + 0.5*BaiPerron.Confidence
(explicitly arbitrary/illustrative, never searched or optimized). Pearson=0.111, Spearman=0.157,
PointBiserial(StructuralBreak winner)=0.145 vs the real StructuralBreak FinalScore - weak but positive,
directionally consistent (StructuralBreak regime has the highest mean hypothetical score, 0.9995), but the
spread across regimes (0.9845-0.9995) is narrow, dominated by BaiPerron.Confidence's saturation. Full CSV:
Tests/Research/RegimeCoverageAudit/Output/structuralbreak_evidence_ablation_lot152.csv.

REGIME:
Unchanged from Lot 15.1 - 5 live regimes, MeanReverting still the only one with any Signal/Entry capability
(Lot 15.1, untouched by this lot).

SIGNAL:
NOT MODIFIED - this lot never touches Signal/Entry, confirmed by brief and honored.

ENTRY:
NOT MODIFIED.

STOP LOSS:
NOT TOUCHED

RISK:
NOT TOUCHED

CALIBRATION:
NOT EXECUTED

P0 REMAINING:
(1) Stop Loss globally absent (Lot 15.0, untouched by 15.1/15.2). (2) StructuralBreak still detected
without any structural-break evidence contributing to its Fusion score - CONFIRMED AGAIN, still true,
this lot did NOT fix it because no scientific contract exists to fix it without fabricating one (brief's
explicit prohibition) - the fix now requires an EXPLICIT USER DECISION on the 3 open design questions in
§21 of the report, not further unilateral audit work.

P1:
BaiPerron.Confidence's saturation in production (near-zero variance in 4/5 regimes) means it is currently
unusable as a Fusion input in its present normalization even if a contract existed - BreakCount is the
more promising candidate, unexplored. BaiPerron's freshness property (weaker confidence for recent breaks
vs older-in-window breaks) must be accounted for in any future design, not ignored. FusionConfiguration.cs
remains dead code (0 references repo-wide), not cleaned up (out of mandate).

NEXT LOT:
Requires an explicit user decision on the 3 design questions (§21: replace-vs-augment StructuralStability,
BreakCount-vs-Confidence, freshness handling) BEFORE any Evidence-wiring lot can begin - this lot
deliberately stops short of proposing a specific answer. Independently, either a dedicated Stop Loss lot
or a dedicated Signal-model-research lot (Trending/RandomWalk/StructuralBreak/StableRange) remain open
P0/P1 items from Lots 15.0/15.1, unaffected by this lot's findings.

DO NOT DO:
Do NOT wire Cusum/BaiPerron into EvidenceFusionEngine/FusionStateManager without an explicit user decision
on the 3 open design questions - doing so unilaterally, even with a "principled-looking" formula, would be
exactly the fabricated relation this lot's brief prohibited inventing. Do NOT treat BaiPerron.Confidence as
a usable Fusion input as currently normalized - it is empirically saturated in production. Do NOT treat the
weak positive ablation correlation (Pearson=0.111) as justification for a specific weight - it was computed
from an explicitly arbitrary 0.5/0.5 illustrative split, never searched, never intended as a proposal. Do
NOT modify CusumStatistics/BaiPerronStatistics/BaiPerronRegression algorithms - no bug was found, and Lot
15.0's structural-break evidence quality relies on them being unchanged. Do NOT reinterpret this lot's
"no production change" outcome as a failure - the brief explicitly classifies a well-explained non-change
as a valid, expected possible outcome (§17: "la question est 'est-ce scientifiquement explicable ?'").
```

**STOP.**
