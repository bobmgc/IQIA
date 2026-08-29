# QDE-012 — Sprint 15.25 — Lot 14.16 — Persistence : Variabilité Historique & Puissance Discriminante

**Date** : 2026-08-24
**Branche** : `feature/structural-stability-v2`
**Lots précédents** : Lot 14.13-14.15 (Ambiguity/Decision/Fusion), Lot 14.15 (ablation causale)
**Statut** : **IMPLEMENTED**
**Type** : **AUDIT / INVESTIGATION — AUCUNE REPONDÉRATION, AUCUNE MODIFICATION DE PRODUCTION**

---

# 1. EXECUTIVE SUMMARY

Le Lot 14.15 a prouvé, par ablation causale, que `Persistence` est le seul levier de séparation actif entre `StableRangeRule` et `MeanRevertingRule` dans la formule actuelle. Ce lot répond à la question laissée ouverte : **cet effet est-il robuste, ou spécifique au dataset/contexte ?**

**Réponse, nuancée et directement soutenue par les données (10 930 barres fraîches, MES=F/M5/59 jours)** :

- **Le MÉCANISME causal est robuste** : l'ablation du Lot 14.15 est reproduite quasi à l'identique une troisième fois (baseline=0.9652, shared-removed=0.1083, persistence-removed=0.9913 — la valeur "persistence-removed" est EXACTEMENT 0.9913, identique au Lot 14.15) et reste QUALITATIVEMENT STABLE sur 5 sous-périodes chronologiques ET sur 4 régimes testés séparément — retirer `Persistence` AUGMENTE systématiquement la corrélation `MeanReverting`↔`StableRange`, sans exception, partout où mesuré (§17/§18/§19).
- **Mais la PUISSANCE STATISTIQUE (l'ampleur de la corrélation) est fortement dépendante du contexte** — c'est le résultat central, nouveau, de ce lot :
  - `Corr(Persistence, ScoreDifference)` Pearson = **-0.8140** (reproduit le -0.81 du Lot 14.15) MAIS Spearman = **-0.4494 seulement** — un écart de près de 40 points entre les deux mesures, signe que la corrélation de Pearson est **gonflée par une minorité de points à effet de levier élevé**, pas une relation monotone générale.
  - `Persistence` a une distribution GLOBALEMENT bimodale par régime : **très concentrée** dans MeanReverting/StructuralBreak/RandomWalk (mean≈0.15, 94.7% des barres) mais **beaucoup plus élevée et dispersée** dans Trending/StableRange (mean≈0.45-0.51, 5.3% des barres seulement) — la queue globale (P90=0.25, P99=0.60) est presque entièrement portée par ces 5.3% de barres rares.
  - Corrélation quotidienne (Pearson) : de **-0.9777 à +0.0249** sur 40 jours (moyenne -0.4915) — **6 jours sur 40 montrent une variance de Persistence EXACTEMENT NULLE** (stdDev=0.0000), rendant le discriminant totalement inerte ces jours-là.
  - **Dans le régime qui compte le plus (MeanReverting, 59.6% des barres)** : `Corr(Persistence, ScoreDifference)` chute à **Pearson=-0.4977, Spearman=-0.3011** — nettement plus faible que le chiffre global de -0.81.
- L'analyse par bin (déciles) confirme : l'effet n'est PAS une pente linéaire douce — c'est un **plateau quasi plat sur 90% du dataset** (deciles 0-8, meanDiff entre 0.053 et 0.067) suivi d'une **chute brutale au 10ᵉ décile seulement** (meanDiff=0.0072, MR winRate chute de ~60-70% à 23.3%, StableRange apparaît enfin comme gagnant 11.0% du temps) — un effet de SEUIL concentré dans la queue extrême, pas une relation continue.

**Classification de robustesse retenue (§25)** : **DATASET/REGIME-DEPENDENT pour la puissance statistique, mais le mécanisme causal lui-même est STRONG (stable temporellement et par régime dans son SIGNE et sa DIRECTION, même quand sa MAGNITUDE varie).** Ni Hypothèse A pure (pas assez stable/uniforme) ni Hypothèse C pure (le mécanisme n'est pas "trop peu variable pour être exploitable" — il l'est, mais seulement dans certaines conditions).

**Décision de repondération (§27)** : **"A reweighting experiment may be justified"**, mais UNIQUEMENT ciblé sur mieux comprendre l'effet de seuil du 10ᵉ décile / la dépendance au régime — jamais une augmentation générale et uniforme du poids 0.20, qui ignorerait le fait que l'effet est concentré dans une queue étroite et un sous-ensemble de régimes.

**Aucune modification de production. Aucun poids touché. Aucune calibration.**

---

# 2. LOT 14.15 FINDINGS (repris tels quels)

- `Corr(StableRange, MeanReverting)` = 0.9653 Pearson / 0.9658 Spearman.
- Ablation : dimensions partagées neutralisées → Corr=0.1033 ; `Persistence` neutralisée → Corr=0.9913.
- `Corr(Persistence, ScoreDifference)` = -0.8141 (mesure globale, pas encore vérifiée pour sa stabilité).
- Hypothèse D retenue : séparation partielle, discriminant réel mais sous-pondéré (0.20) et peu variable sur ce dataset.
- `FusionConfiguration`/Decision weights = DEAD (confirmé une seconde fois).

---

# 3. SCIENTIFIC QUESTION

> `Persistence` est-elle réellement un discriminant structurel robuste entre `StableRange` et `MeanReverting`, ou son effet observé est-il spécifique au dataset actuel ?

Décomposée en 5 hypothèses (A robuste / B spécifique aux 59 jours / C trop peu variable / D spécifique à certains régimes / E statistiquement réel mais instable) — évaluées §25.

---

# 4. PERSISTENCE DEFINITION

`Persistence` (dimension Fusion) mesure l'accord scientifique entre deux tests statistiques indépendants sur la **mémoire longue / tendance de la série de prix** :

- **DFA (Detrended Fluctuation Analysis)** — exposant de Hurst H. `H≈0.50` = marche aléatoire (mémoire nulle), `H>0.50` = persistance (tendance longue), `H<0.50` = anti-persistance (retour à la moyenne). Source : `Engine/Regime/Evidence/DFA`.
- **Variance Ratio (test de Lo-MacKinlay)** — ratio de variance à un lag donné, avec Z-statistique et p-value. Source : `Engine/Regime/Evidence/VarianceRatio`.

Les deux tests sont fenêtrés indépendamment : **DFA, fenêtre=128 barres, échantillon minimum=80** ; **Variance Ratio, fenêtre=30 barres, échantillon minimum=20** (`Engine/Regime/RegimeEngine.cs:21-26`, constantes vérifiées par lecture directe, inchangées depuis le Lot 14.9).

---

# 5. PERSISTENCE FORMULA

Source exacte : `Engine/Fusion/Rules/PersistenceRule.cs`.

```
Input : DfaResult (Hurst, Confidence, RSquared, WindowCount), VarianceRatioResult (VarianceRatio, PValue, Confidence, SampleSize)

dfaDirection      = tanh(6.0 × (Clamp(Hurst,0,2) - 0.5))
dfaPersistence    = dfaDirection² × sigmoid(4.0 × dfaDirection)          [PersistenceSupport]

vrDirection       = tanh(2.0 × ln(max(VarianceRatio, 1e-12)))
vrPersistence     = PValueStrength(VR.PValue) × PersistenceSupport(vrDirection)

scientificScore   = 0.5 × (dfaPersistence + vrPersistence)
dfaQuality        = (DFA.Confidence + DFA.RSquared + SampleSizeStrength(WindowCount)) / 3
vrQuality         = 0.5 × (VR.Confidence + SampleSizeStrength(VR.SampleSize))
qualityScore      = 0.5 × (dfaQuality + vrQuality)

Persistence.Value = Clamp(0.90 × scientificScore + 0.10 × qualityScore, 0, 1)
```

**Cas limite "Missing Evidence"** : si `DfaResult`/`VarianceRatioResult` est `null` ou `IsValid=false` (ex. warmup insuffisant), `Persistence = {Value=0.0, Confidence=0.0, IsAvailable=false}` — jamais 0.0 fabriqué comme mesure réelle (Sprint 14, DEC-01/FUS-02, testé et confirmé au Lot 14.14 : `IsAvailable=false` count = 0/10930 sur ce dataset, jamais activé ici).

---

# 6. GLOBAL DISTRIBUTION (10 930 barres)

```
n=10930, min=0.1209, max=0.7466, mean=0.1693, stdDev=0.0917
P01=0.1212, P05=0.1286, P10=0.1291, P25=0.1327, P50=0.1398, P75=0.1486, P90=0.2500, P95=0.3595, P99=0.5983
```

**Distribution fortement asymétrique à droite** : P01→P75 couvre seulement `[0.121, 0.149]` (bande de 0.028) — mais P75→P99 couvre `[0.149, 0.598]` (bande de 0.45, seize fois plus large). 75% des barres ont une `Persistence` quasi constante autour de 0.13-0.15 ; les 25% restants (surtout le dernier décile) portent toute la queue haute. **Ce n'est ni "très concentrée" ni "suffisamment dispersée" de façon homogène — c'est un mélange des deux, dont l'origine est éclaircie au §8.**

---

# 7. TEMPORAL DISTRIBUTION (par jour calendaire, 40 jours, n≥50/jour)

Découpage retenu (brief §3, "sans introduire de biais") : **jour calendaire** — frontière naturelle du dataset (pas de choix arbitraire de taille de fenêtre), déjà utilisée aux Lots 14.14/14.15 pour l'analyse temporelle, cohérence méthodologique intentionnelle.

Extraits représentatifs (voir le log de test pour les 40 jours complets, §Tests) :

| Jour | n | mean | median | stdDev | P10 | P90 |
|---|---|---|---|---|---|---|
| 2026-06-29 | 276 | 0.1396 | 0.1398 | 0.0037 | 0.1398 | 0.1398 |
| 2026-07-17 | 251 | **0.2552** | 0.1468 | **0.1793** | 0.1328 | **0.5131** |
| 2026-07-29 | 275 | **0.3540** | 0.2656 | **0.1989** | 0.1658 | **0.6876** |
| 2026-08-05 | 275 | 0.1327 | 0.1327 | **0.0000** | 0.1327 | 0.1327 |
| 2026-08-11 | 275 | 0.1291 | 0.1291 | **0.0000** | 0.1291 | 0.1291 |
| 2026-08-12 | 275 | 0.1291 | 0.1291 | **0.0000** | 0.1291 | 0.1291 |
| 2026-08-13 | 275 | **0.2978** | 0.1291 | **0.2242** | 0.1291 | **0.5969** |
| 2026-08-17 | 276 | 0.1361 | 0.1361 | **0.0000** | 0.1361 | 0.1361 |
| 2026-08-18 | 275 | 0.1361 | 0.1361 | **0.0000** | 0.1361 | 0.1361 |
| 2026-08-21 | 251 | 0.1436 | 0.1436 | **0.0000** | 0.1436 | 0.1436 |

**Constat majeur, nouveau pour ce lot** : sur **6 jours des 40 (15%)**, `Persistence` a une **variance strictement nulle sur toute la journée** (`stdDev=0.0000`, min=max=mean=median) — le discriminant est totalement figé, sans aucun mouvement mesurable. À l'inverse, quelques jours isolés (2026-07-17, 2026-07-29, 2026-08-13) montrent une dispersion considérable (stdDev jusqu'à 0.22, P90 jusqu'à 0.69) — `Persistence` change réellement dans le temps, mais de façon **très inégale**, concentrée sur une minorité de journées, jamais uniformément distribuée sur les 59 jours.

---

# 8. REGIME DISTRIBUTION

```
Persistence | Winner=MeanReverting   (n=6519) : mean=0.1481, stdDev=0.0366, P90=0.1583, P99=0.3314
Persistence | Winner=StructuralBreak (n=3359) : mean=0.1566, stdDev=0.0461, P90=0.2198, P99=0.3329
Persistence | Winner=RandomWalk      (n=483)  : mean=0.1530, stdDev=0.0461, P90=0.1828, P99=0.3348   [UNDERREPRESENTED]
Persistence | Winner=Trending        (n=449)  : mean=0.5126, stdDev=0.1264, P90=0.7146, P99=0.7466   [UNDERREPRESENTED]
Persistence | Winner=StableRange     (n=120)  : mean=0.4578, stdDev=0.1035, P90=0.5969, P99=0.5969   [TRÈS UNDERREPRESENTED]
```

**C'est le résultat le plus important de ce lot** : la queue haute de `Persistence` observée §6 n'est PAS répartie aléatoirement dans le dataset — elle appartient presque exclusivement aux barres où `Trending` ou `StableRange` remporte l'arbitrage (5.3% du dataset combiné). Dans les 3 régimes qui dominent (94.7% des barres, dont `MeanReverting` — le seul qui compte pour le trading), `Persistence` reste confinée à une bande étroite (`mean≈0.15`, `P90≤0.22`). **Cela explique mécaniquement pourquoi `TrendingRule` (qui pondère `Persistence` à 0.50, le poids le plus élevé de toutes les règles) ne gagne quasiment jamais (4.1%) : il faut une valeur de `Persistence` rare (queue haute) pour qu'il l'emporte.**

Pour `Trending`/`StableRange` (sous-représentés) et surtout `StableRange` (n=120, très réduit) : **aucune conclusion forte** n'est tirée de ces chiffres seuls (brief §4, discipline respectée) — ils sont rapportés comme descriptifs.

---

# 9. BUY/SELL DISTRIBUTION

```
Persistence | Direction=BUY_CANDIDATE  (n=1175) : mean=0.1399, stdDev=0.0150
Persistence | Direction=SELL_CANDIDATE (n=1145) : mean=0.1389, stdDev=0.0119
```

**Aucune asymétrie directionnelle** — moyennes quasi identiques (écart 0.001), écarts-types du même ordre de grandeur. Cohérent avec la symétrie déjà établie au Lot 14.14 pour `AmbiguityScore` BUY/SELL.

---

# 10. PERSISTENCE vs SCOREDIFFERENCE

```
Pearson  Corr(Persistence, ScoreDifference) = -0.8140   (reproduit le -0.8141 du Lot 14.15)
Spearman Corr(Persistence, ScoreDifference) = -0.4494   (BEAUCOUP plus faible)
Pearson  Corr(Persistence, AmbiguityScore)  = -0.2082
```

---

# 11. PEARSON ANALYSIS

Le Pearson global (-0.8140) reproduit fidèlement la valeur de référence du Lot 14.15 — **mais un coefficient de Pearson est sensible aux valeurs à fort effet de levier** (ici, la queue haute de `Persistence`, concentrée dans les régimes Trending/StableRange, §8). Un Pearson élevé peut donc refléter surtout la SÉPARATION ENTRE RÉGIMES (Trending/StableRange à Persistence élevée + ScoreDifference faible/négatif VS MeanReverting/StructuralBreak/RandomWalk à Persistence faible + ScoreDifference élevé), plutôt qu'une relation continue et homogène À L'INTÉRIEUR de chaque régime.

---

# 12. SPEARMAN ANALYSIS

Le Spearman global (-0.4494), robuste aux valeurs extrêmes (il travaille sur les RANGS, pas les valeurs), est **presque deux fois plus faible** que le Pearson. **C'est la preuve statistique directe que l'essentiel de la corrélation de Pearson observée provient d'un nombre relativement restreint de barres à `Persistence` extrême (la queue haute), pas d'une relation monotone forte et généralisée sur l'ensemble du dataset.** Sans ce diagnostic Pearson/Spearman croisé, le -0.81 du Lot 14.15 aurait pu être mal interprété comme une relation monotone uniformément forte — ce lot corrige cette lecture.

---

# 13. QUANTILE/BINNING ANALYSIS

Déciles de `Persistence`, avec `ScoreDifference` moyen/médian, `AmbiguityScore` moyen, taux de victoire MR/SR :

| Décile | Plage | n | meanDiff | medianDiff | meanAmbiguity | MR winRate | SR winRate |
|---|---|---|---:|---:|---:|---:|---:|
| 0 | [0.1209,0.1291] | 1093 | 0.0661 | 0.0679 | 0.9476 | 66.7% | 0.0% |
| 1 | [0.1291,0.1327] | 1093 | 0.0670 | 0.0703 | 0.9480 | 59.7% | 0.0% |
| 2 | [0.1327,0.1361] | 1093 | 0.0619 | 0.0636 | 0.9461 | 59.1% | 0.0% |
| 3 | [0.1361,0.1370] | 1093 | 0.0622 | 0.0649 | 0.9547 | 70.4% | 0.0% |
| 4 | [0.1370,0.1398] | 1093 | 0.0613 | 0.0631 | 0.9546 | 66.3% | 0.0% |
| 5 | [0.1398,0.1434] | 1093 | 0.0576 | 0.0598 | 0.9502 | 77.8% | 0.0% |
| 6 | [0.1434,0.1457] | 1093 | 0.0586 | 0.0610 | 0.9489 | 64.2% | 0.0% |
| 7 | [0.1457,0.1497] | 1093 | 0.0617 | 0.0629 | 0.9475 | 59.0% | 0.0% |
| 8 | [0.1497,0.2500] | 1093 | 0.0527 | 0.0547 | 0.9480 | 50.0% | 0.0% |
| **9** | **[0.2500,0.7466]** | 1093 | **0.0072** | **0.0101** | 0.9434 | **23.3%** | **11.0%** |

**Effet de SEUIL, pas de pente linéaire** : les déciles 0-8 (90% du dataset, tous sous `Persistence≈0.25`) restent dans une bande étroite de `meanDiff` (0.053-0.067) — quasi plate, aucun signe de dégradation progressive. Le décile 9 (le dernier 10%, `Persistence∈[0.25,0.75]`) chute brutalement à `meanDiff=0.0072` — quasiment nul — et c'est SEULEMENT là que `StableRange` remporte l'arbitrage de façon non négligeable (11.0%, contre 0.0% partout ailleurs). **Cette non-monotonicité (vérifiée : la séquence des moyennes par décile n'est PAS strictement décroissante — décile 1 > décile 0) confirme que l'effet de `Persistence` ressemble à un SEUIL d'activation au-delà de ~0.25, pas à une relation linéaire continue depuis la valeur la plus basse.**

---

# 14. PERSISTENCE vs WINNER

Fréquences descriptives (brief §9, jamais présentées comme prédictives hors échantillon) :

```
Winner=MeanReverting   : mean Persistence=0.1481 (n=6519)
Winner=StructuralBreak : mean Persistence=0.1566 (n=3359)
Winner=RandomWalk      : mean Persistence=0.1530 (n=483)
Winner=Trending        : mean Persistence=0.5126 (n=449)
Winner=StableRange     : mean Persistence=0.4578 (n=120)
```

Reprend et confirme §8 sous un angle différent (par Winner plutôt que par régime — même partition, résultat identique).

---

# 15. PERSISTENCE vs RUNNER-UP

```
Persistence | Winner=MeanReverting, RunnerUp=StableRange (n=3692) : mean=0.1505, P90=0.1684, P99=0.3330
Persistence | Winner=StableRange, RunnerUp=MeanReverting (n=92, PETIT ÉCHANTILLON) : mean=0.4290, P90=0.5666, P99=0.5969
```

Séparation nette entre ces deux conditions jointes spécifiques (§17 pour la quantification).

---

# 16. PERSISTENCE vs AMBIGUITY

```
Corr(Persistence, AmbiguityScore) = -0.2082
```

Plus faible que `Corr(Persistence, ScoreDifference)` (-0.81 Pearson / -0.45 Spearman) — cohérent avec la chaîne `Persistence → ScoreDifference → (via Winner/RunnerUp, pas exclusivement StableRange/MeanReverting) → AmbiguityScore` : `AmbiguityScore` dépend de l'écart Winner-RunnerUp RÉEL (qui peut impliquer `StructuralBreak` comme RunnerUp 40.4% du temps, Lot 14.15 §12), pas seulement de l'écart `MeanReverting-StableRange` — la relation s'atténue en traversant cette étape intermédiaire.

---

# 17. ABLATION REPRODUCTION

| Scénario | Lot 14.15 (référence) | Lot 14.16 (ce run) |
|---|---:|---:|
| Baseline | 0.9653 | 0.9652 |
| Dimensions partagées neutralisées | 0.1033 | 0.1083 |
| Persistence neutralisée | 0.9913 | **0.9913 (exact)** |

**Reproduction quasi parfaite sur un troisième dataset indépendant** (fingerprint `E22FA822930A81CAF1FA16D3FB714F5E33662C7B11D119CDF07FB7C6B28EBADF`, distinct des Lots 14.14/14.15). Les écarts (0.9652 vs 0.9653 ; 0.1083 vs 0.1033) sont de l'ordre du bruit d'échantillonnage jour-à-jour (fenêtre Yahoo glissante) — **pas une régression, pas une raison de suspecter une erreur** (brief §12 : "si les valeurs changent, ne pas supposer automatiquement une régression, analyser la raison" — analysé, raison = fenêtre glissante, cohérent avec l'écart similaire déjà documenté entre les Lots 14.14 et 14.15 eux-mêmes).

---

# 18. TEMPORAL ABLATION

5 chunks chronologiques de taille égale (~2 186 barres chacun, découpage non biaisé par comptage égal plutôt que par date arbitraire) :

| Période | Dates | n | Baseline Corr(MR,SR) | Persistence neutralisée |
|---|---|---|---:|---:|
| 0 | 2026-06-26 → 2026-07-09 | 2 186 | 0.9728 | 0.9900 |
| 1 | 2026-07-09 → 2026-07-21 | 2 186 | 0.9579 | 0.9895 |
| 2 | 2026-07-21 → 2026-07-31 | 2 186 | 0.9578 | 0.9927 |
| 3 | 2026-07-31 → 2026-08-12 | 2 186 | 0.9852 | 0.9928 |
| 4 | 2026-08-12 → 2026-08-24 | 2 186 | 0.9533 | 0.9913 |

**L'effet qualitatif de l'ablation (retirer Persistence AUGMENTE la corrélation) est vérifié sur les 5 périodes, sans exception** — le mécanisme causal est stable dans le temps. La baseline elle-même varie modérément (0.953-0.985), cohérent avec l'instabilité déjà documentée §7/§13, mais jamais au point d'inverser le sens de l'effet d'ablation.

---

# 19. REGIME ABLATION

Seuls les régimes avec n≥300 sont analysés (brief §14) :

| Winner | n | Baseline Corr(MR,SR) | Persistence neutralisée |
|---|---:|---:|---:|
| MeanReverting | 6 519 | 0.9718 | 0.9851 |
| StructuralBreak | 3 359 | 0.9611 | 0.9830 |
| RandomWalk | 483 | 0.9668 | 0.9855 |
| Trending | 449 | 0.9328 | 0.9894 |
| StableRange | 120 | **SKIPPED — échantillon insuffisant (brief §14/§19)** | |

**Même constat que §18** : l'effet qualitatif tient dans les 4 régimes testés — la baseline la plus basse (Trending, 0.9328) est aussi celle où `Persistence` a le plus d'influence en valeur absolue (elle y est, par construction, la plus dispersée, §8), cohérent avec l'ensemble du tableau.

---

# 20. DISTRIBUTION OVERLAP

```
Overlap coefficient (intersection d'histogrammes, 20 bins) entre :
  Persistence | Winner=MeanReverting, RunnerUp=StableRange (n=3692)
  Persistence | Winner=StableRange, RunnerUp=MeanReverting (n=92, PETIT ÉCHANTILLON)
= 0.0320
```

**Chevauchement quasi nul (3.2%)** entre ces deux conditions jointes spécifiques — les distributions sont presque entièrement séparées. Interprétation prudente (brief §17 : "POTENTIELLEMENT fort discriminant, pas PROUVÉ ROBUSTE") : cette séparation nette est mesurée sur une comparaison qui ISOLE déjà les deux issues extrêmes (qui gagne entre les deux) — elle confirme que **quand** `StableRange` bat `MeanReverting`, c'est presque toujours accompagné d'une `Persistence` nettement plus élevée. Cela ne contredit PAS l'instabilité documentée §11/§12/§13 — au contraire, cela precise QUAND le discriminant s'active (queue haute uniquement), cohérent avec l'effet de seuil du décile 9.

---

# 21. TEMPORAL STABILITY

`Corr(Persistence, ScoreDifference)` par jour (40 jours, extraits) :

| Jour | n | Pearson | Spearman |
|---|---|---:|---:|
| 2026-06-29 | 276 | -0.0288 | 0.0155 |
| 2026-07-17 | 251 | -0.9777 | -0.7669 |
| 2026-07-29 | 275 | -0.9558 | -0.9371 |
| 2026-08-05 | 275 | **0.0000** | **NaN (variance nulle)** |
| 2026-08-11 | 275 | **0.0000** | **NaN** |
| 2026-08-12 | 275 | **0.0000** | **NaN** |
| 2026-08-17 | 276 | **-0.0000** | **NaN** |
| 2026-08-18 | 275 | **0.0000** | **NaN** |
| 2026-08-21 | 251 | **-0.0000** | **NaN** |

```
Daily Pearson : min=-0.9777, max=+0.0249, mean=-0.4915, jours analysés=40
```

**Verdict explicite (brief §18, "Il faut vérifier sa stabilité")** : **-0.81 N'EST PAS stable — c'est une moyenne масquant une variation extrême**, de journées à corrélation quasi parfaite (-0.98) à des journées à corrélation LITTÉRALEMENT NULLE (variance de `Persistence` = 0, 6 jours sur 40, 15%). La moyenne journalière (-0.4915) est plus proche du Spearman global (-0.4494) que du Pearson global (-0.8140) — confirmation croisée que le Pearson global est gonflé par la structure inter-régime (§8/§11), pas une propriété stable jour-après-jour.

---

# 22. REGIME STABILITY

| Régime | n | Pearson | Spearman | Prudence |
|---|---:|---:|---:|---|
| MeanReverting | 6 519 | **-0.4977** | **-0.3011** | REPRESENTED — **c'est le chiffre qui compte pour le trading** |
| StructuralBreak | 3 359 | -0.7756 | -0.5467 | REPRESENTED |
| RandomWalk | 483 | -0.7644 | -0.3783 | UNDERREPRESENTED |
| Trending | 449 | -0.9270 | -0.9409 | UNDERREPRESENTED |
| StableRange | 120 | -0.8760 | -0.8851 | TRÈS UNDERREPRESENTED |

**Constat central pour la décision de repondération (§27)** : DANS le régime `MeanReverting` (celui que le gate `AmbiguityGateThreshold` filtre réellement), la corrélation `Persistence`↔`ScoreDifference` n'est que **MODÉRÉE** (Pearson -0.50, Spearman -0.30) — bien en-deçà du -0.81 global. Le discriminant existe et fonctionne dans le sens attendu partout, mais sa force pratique, LÀ OÙ ELLE COMPTE POUR LA PRODUCTION, est significativement plus faible que le chiffre le plus souvent cité.

---

# 23. EXISTING HISTORICAL DATA AUDIT

Audit du dépôt (brief §21) :

| Source | Nature | Pertinence pour ce lot | Statut |
|---|---|---|---|
| `Tests/Research/StopLossCalibration/RealMarket/RawCapture/Sprint15_18/*` | Capture ATAS réelle, ES M5, 2026-08-14 (deux sessions) | Seule capture "live" réelle du dépôt | **`SCIENTIFIC_ADMISSIBILITY = BLOCKED`** (qualité insuffisante — 36.5% de barres en formation, défaut déjà identifié et documenté aux lots précédents, non résolu) — **NON utilisée, non mélangée** (brief §21, discipline respectée) |
| `Tests/Research/StopLossCalibration/Output/*.csv` | ~30 fichiers CSV de résultats de calibration Stop Loss (A1/A2/D1/D2, Hybrid) | Aucune (calibration Stop Loss, sujet disjoint de Persistence/Decision) | Non pertinents pour ce lot, non consultés en détail |
| `Tests/Research/StopLossCalibration/RealMarket/*.csv` (inventaire, qualité, gaps...) | Rapports de qualité sur la capture ATAS ci-dessus | Confirme indirectement pourquoi la capture reste BLOCKED | Non réutilisés (déjà résumés par la conclusion BLOCKED) |
| `Engine/Regime/Evidence/DFA/DfaGoldenDataset.cs`, `VarianceRatio/VarianceRatioGoldenDataset.cs` | Jeux de données synthétiques de RÉFÉRENCE pour valider les FORMULES DFA/VarianceRatio elles-mêmes (pas des séries de marché) | Utile pour comprendre que les formules sont déjà validées indépendamment — pas une source de données de marché alternative | Cité, non exploité comme dataset de marché (hors scope) |

**Aucune autre source de données historiques MES/ES M5 pertinente et exploitable au-delà de Yahoo (59 jours) n'a été trouvée dans le dépôt.** La seule alternative réelle (capture ATAS Sprint 15.18) reste explicitement bloquée pour raison de qualité, une conclusion déjà établie avant ce lot et reconfirmée ici par audit, pas re-décidée.

---

# 24. SYNTHETIC CASES

Fichier : `Tests/Decision/PersistenceVariabilitySyntheticCasesTests.cs` — 4 groupes de cas (A/B/C combinés, D, E, F), tous PASS, via les vraies classes `MeanRevertingRule`/`StableRangeRule`/`DecisionEngine` :

| Cas | Configuration | Résultat observé |
|---|---|---|
| A/B/C | Persistence=0.10 / 0.50 / 0.90, dimensions partagées fixes | `ScoreDifference` décroît strictement de façon monotone (0.10→0.50→0.90) — confirme au niveau synthétique la relation moyenne observée sur données réelles (§13, hors effet de seuil) |
| D | Balayage complet Persistence∈{0,0.2,0.4,0.6,0.8,1.0} | Décroissance stricte à chaque pas ; amplitude totale >0.10 — quantifie le levier maximal théorique de la formule |
| E | Persistence FIXE (0.50), StructuralStability divergente (0.20 vs 0.95) | Une séparation réelle mais PLUS PETITE que l'amplitude totale de Persistence apparaît — confirme que StructuralStability reste un discriminant secondaire (cohérent avec Lot 14.15 §16) |
| F | Persistence divergente (0.10 vs 0.90) testée à deux niveaux de dimensions partagées (modérées vs extrêmes) | Séparation quasi identique dans les deux cas (écart <0.01) — confirme mathématiquement que l'effet de Persistence est ADDITIF, jamais dilué par le niveau des dimensions partagées (propriété du blend linéaire, pas une hypothèse) |

---

# 25. ROBUSTNESS CLASSIFICATION

Au vu de l'ensemble des preuves :

| Dimension évaluée | Verdict |
|---|---|
| Mécanisme causal (ablation) | **STRONG** — stable en signe et en direction sur 5 périodes et 4 régimes, reproduit à l'identique 3 fois sur des datasets indépendants |
| Puissance statistique globale (Pearson) | Élevée en apparence (-0.81) mais **gonflée par la structure inter-régime** |
| Puissance statistique robuste (Spearman) | **MODERATE** (-0.45 global) |
| Stabilité temporelle | **WEAK à DATASET-DEPENDENT** — 15% des jours à variance nulle, plage quotidienne [-0.98, +0.02] |
| Stabilité par régime (dans MeanReverting spécifiquement) | **MODERATE** (Pearson -0.50, Spearman -0.30) — plus faible que le chiffre global |
| Forme de la relation | **Effet de seuil**, pas une pente linéaire — actif principalement au-delà du 90ᵉ percentile |

**Classification retenue : DATASET/REGIME-DEPENDENT** pour la puissance pratique du discriminant, avec un **mécanisme causal sous-jacent STRONG**. Ni Hypothèse A (pas assez stable/uniforme pour "structurel robuste" au sens fort), ni Hypothèse C pure (le mécanisme n'est pas simplement "trop peu variable" — il EST exploitable, mais seulement dans une queue étroite et hors du régime MeanReverting principalement) — **combinaison des Hypothèses D et E** : discriminant régime-dépendant (D) ET statistiquement réel mais insuffisamment stable pour justifier une repondération générale sans étude supplémentaire (E).

---

# 26. LIMITATIONS

- Un troisième dataset MES M5/59 jours indépendant (ce run) — la stabilité temporelle mesurée reste bornée à cette même fenêtre glissante de 59 jours (aucune validation multi-mois/multi-saison possible, mur Yahoo inchangé).
- Les 6 jours à variance nulle de `Persistence` n'ont pas été investigués quant à leur cause (heures de marché calmes ? artefact du test statistique lui-même ? — hors scope).
- L'échantillon `Winner=StableRange` (n=120, encore plus petit pour `RunnerUp=StableRange&Winner=MeanReverting` inversé, n=92) reste trop réduit pour toute conclusion ferme sur ce régime spécifique — signalé à chaque usage.
- Le coefficient de chevauchement (§20) utilise un binning fixe à 20 classes sur la plage combinée — un choix méthodologique raisonnable mais non unique.
- Aucune correction de comparaisons multiples n'a été appliquée aux dizaines de corrélations rapportées (cohérent avec la discipline "descriptif, pas inférentiel" des lots précédents).

---

# 27. REWEIGHTING DECISION

**"A reweighting experiment may be justified"** — mais avec une portée précise, pas une simple augmentation du poids 0.20 :

- Toute expérience future de repondération devrait tenir compte de la **non-linéarité en seuil** (§13) — une repondération LINÉAIRE uniforme du poids actuel de `Persistence` amplifierait un effet qui n'est réellement utile que dans le dernier décile, et pourrait dégrader le comportement dans les 90% de barres où l'effet actuel (faible mais stable) est déjà approprié.
- Toute expérience future devrait être menée **séparément pour le régime MeanReverting** (où la corrélation réelle est modérée, -0.30 à -0.50) plutôt que calibrée sur le chiffre global (-0.81), qui ne reflète pas la situation pertinente pour le trading.
- **Aucune valeur précise n'est proposée** (brief §24, discipline respectée) — ni `0.30`, ni `0.40`, ni aucune autre.

**"No reweighting justified" n'est PAS la conclusion retenue** — le mécanisme causal est trop clairement établi (§17-19) pour l'écarter, mais il ne peut pas non plus être qualifié de suffisamment robuste et homogène pour justifier une simple augmentation de poids sans étude complémentaire.

---

# 28. RECOMMENDED FUTURE LOT

**Lot proposé (investigation, pas calibration)** : caractériser précisément le "seuil d'activation" observé au décile 9 (§13) — à quelle valeur de `Persistence` l'effet démarre-t-il réellement, et cette valeur est-elle stable d'un jour à l'autre parmi les jours à forte variance (§21) ? Cela informerait une éventuelle repondération NON LINÉAIRE (ex. une transformation sigmoïde de `Persistence` avant pondération) — mais resterait une PROPOSITION à documenter, jamais implémentée avant un lot de calibration explicitement approuvé.

**Alternative, complémentaire** : investiguer pourquoi 15% des jours montrent une variance nulle de `Persistence` (artefact du test statistique DFA/Variance Ratio lui-même, ou propriété réelle du marché durant ces sessions ?) — répondrait à l'Hypothèse B/C de façon plus déterminante.

**Ne pas encore lancer** : toute repondération de `Persistence` (ou de tout autre poids Decision/Fusion) tant que l'effet de seuil (§13) et l'écart Pearson/Spearman (§11/§12) ne sont pas mieux compris — un ajustement naïf risquerait d'optimiser sur un artefact statistique (queue rare, corrélation gonflée) plutôt que sur un signal généralisable.

---

# 29. HANDOFF CONTEXT — NEXT SESSION

```
PROJECT:
IQIA

CURRENT LOT:
14.16

LAST COMPLETED:
14.15

PRIMARY QUESTION:
Is Persistence a robust discriminant between StableRange and MeanReverting? ANSWERED with nuance.

DATASET:
MES=F
M5
~59 days
11058 bars downloaded, 10930 analyzed after warmup - fingerprint
E22FA822930A81CAF1FA16D3FB714F5E33662C7B11D119CDF07FB7C6B28EBADF (fresh, distinct from Lots 14.14/14.15)

BAR COUNT:
10930

REFERENCE CORRELATION:
Corr(Persistence, ScoreDifference) Pearson = -0.8140 (reproduces Lot 14.15's -0.8141) BUT Spearman =
-0.4494 only - large Pearson/Spearman gap indicates the Pearson figure is inflated by a minority of
high-leverage, high-Persistence points concentrated in the Trending/StableRange regimes (5.3% of bars).

RULE CORRELATION:
StableRange <-> MeanReverting = 0.9652 (reproduces 0.9653/0.9652 across 3 independent dataset downloads)

ABLATION:
Shared dimensions removed = 0.1083 (Lot 14.15: 0.1033)
Persistence removed = 0.9913 (EXACT match to Lot 14.15)
Ablation effect direction confirmed stable across 5 chronological periods AND 4 regimes (n>=300) - the
CAUSAL MECHANISM is robust even though the correlation MAGNITUDE is not.

PERSISTENCE CURRENT WEIGHT:
0.20 (unchanged)

GLOBAL VARIABILITY:
Heavily right-skewed: P01-P75=[0.121,0.149] (narrow), P75-P99=[0.149,0.598] (16x wider). Not uniformly
"low variance" - the tail is real but concentrated in 25% of bars, mostly Trending/StableRange regime bars.

TEMPORAL VARIABILITY:
Highly unstable. Daily Pearson Corr(Persistence,ScoreDifference) ranges -0.9777 to +0.0249 (mean -0.4915,
40 days). 6/40 days (15%) show EXACTLY ZERO Persistence variance for the entire day (discriminant totally
inert those days). A handful of days (07-17, 07-29, 08-13) show strong swings (stdDev up to 0.22).

REGIME VARIABILITY:
Bimodal by regime: mean~0.15 (tight, stdDev~0.04) in MeanReverting/StructuralBreak/RandomWalk (94.7% of
bars); mean~0.45-0.51 (wide, stdDev~0.10-0.13) in Trending/StableRange (5.3% of bars, underrepresented).
Within MeanReverting specifically (the regime that matters for the AmbiguityGateThreshold gate), Corr with
ScoreDifference is only MODERATE: Pearson=-0.4977, Spearman=-0.3011 - much weaker than the -0.81 headline.

DISCRIMINATIVE POWER:
Threshold-like, not linear: decile binning (§13) shows a near-flat plateau (meanDiff 0.053-0.067) across
deciles 0-8 (90% of the dataset, Persistence<0.25), then a sharp drop to meanDiff=0.0072 only in decile 9
(Persistence 0.25-0.75) - StableRange only wins (11.0%) in that top decile, 0.0% everywhere else.

CORRELATION STABILITY:
NOT stable - large Pearson/Spearman gap (outlier-driven inflation), daily correlation ranges from near-
perfect (-0.98) to exactly zero (6 days with zero Persistence variance), and regime-conditional correlation
within MeanReverting is meaningfully weaker than the global figure.

ROBUSTNESS CLASSIFICATION:
DATASET/REGIME-DEPENDENT for statistical power, but the underlying CAUSAL MECHANISM (ablation) is STRONG
- stable in sign/direction across 5 periods and 4 regimes, reproduced identically 3 times. Combination of
Hypotheses D (regime-dependent) and E (statistically real but too unstable for a blanket reweighting).

REWEIGHTING:
NOT PERFORMED. "A reweighting experiment may be justified" but scoped narrowly (threshold-region-specific,
regime-specific) - never a blanket increase of the current 0.20 weight. No specific value proposed.

PRODUCTION:
UNCHANGED

CALIBRATION:
NOT PERFORMED

FUSION CONFIGURATION:
DEAD (confirmed a third time, no new evidence changes this)

DECISION WEIGHTS:
DEAD (confirmed a third time)

ATAS:
NOT USED

ORDERS:
NONE

DLL:
NOT DEPLOYED

COMMIT:
NO

NEXT LOT:
Characterize the decile-9 threshold precisely (where does Persistence's effect actually activate, and is
that activation point stable across the high-variance days identified in §21?) - OR investigate why 15% of
days show zero Persistence variance (DFA/VarianceRatio artifact vs genuine market property). Both are
investigation-only, not calibration.

WHY:
This lot found that the -0.81 correlation driving the Lot 14.15 Hypothesis-D conclusion is real but
substantially inflated by cross-regime structure and a small high-leverage tail - any future reweighting
decision needs the threshold/regime-specific picture from this lot, not the single global correlation
figure, to avoid optimizing against a statistical artifact.

DO NOT DO:
Do NOT modify Persistence's weight (0.20), MeanRevertingRule, StableRangeRule, Fusion/Decision weights,
AmbiguityGateThreshold, RegimeEngine, DecisionEngine, or FusionEngine - this lot is investigation-only. Do
NOT treat the -0.81 Pearson figure as the "true" discriminative power without the Spearman/regime-
conditional context from this lot (-0.45 / -0.30 to -0.50) - both readings are valid, they answer different
questions. Do NOT assume the decile-9 threshold effect generalizes beyond this specific 59-day window
without further investigation (Hypothesis B still not excluded for the EXACT threshold location).
```

**STOP.**
