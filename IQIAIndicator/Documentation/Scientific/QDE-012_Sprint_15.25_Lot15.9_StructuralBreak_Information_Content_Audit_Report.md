# QDE-012 — Sprint 15.25 — Lot 15.9 — StructuralBreak Evidence Quality & Information Content Audit

**Date** : 2026-08-27
**Branche** : `feature/structural-stability-v2`
**Lot précédent** : 15.8 (implémentation)
**Statut** : **AUDIT COMPLETE**
**Type** : **AUDIT UNIQUEMENT — AUCUNE MODIFICATION DE PRODUCTION**

---

# 1. EXECUTIVE SUMMARY

Ce lot répond à la question centrale héritée du Lot 15.8 : `Strength` (=`Cusum.Confidence`) sature à 1.0 — **pourquoi, où exactement, et cette evidence apporte-t-elle malgré tout une information marginale réelle ?**

**Localisation exacte de la saturation (Section 8/9)** : la table de trace du pipeline en 7 étapes montre que la variance s'effondre **entre l'étape 3 (ratio brut CUSUM, avant clamp — variance=20239, plage 1.0 à 5982.8) et l'étape 4 (`Cusum.Confidence`, après `Math.Clamp(ratio,0,1)` — variance=0, une seule valeur distincte : 1.0, sur les 10764 barres détectées)**. La saturation existe **déjà dans CUSUM lui-même** (`CusumStatistics.Compute`, code protégé, Sprint 2.x), **avant** que `StructuralBreakEvidenceRule` n'intervienne — celle-ci se contente de transmettre `Confidence` telle quelle (étapes 4→5→6 rigoureusement identiques). Le clamp lui-même n'est pas fautif intrinsèquement — c'est la conséquence attendue d'un test CUSUM qui, une fois le seuil franchi, continue d'accumuler sans borne tant que le régime décalé persiste dans la fenêtre.

**BaiPerron.BreakCount** n'est PAS constant (contrairement à une lecture rapide des lots précédents) : plage 1-8, moyenne 5.10, variance 1.59, distribution en cloche centrée sur 4-6, change de valeur sur 17.9% des barres consécutives — un signal réellement dynamique, mais à plage étroite et faible amplitude.

**Redondance** : corrélations (Pearson/Spearman, brut et stabilisé) entre `StructuralBreak` et les 5 autres dimensions sont **globalement faibles** (|r|<0.16 partout, sauf un cas structurellement dégénéré expliqué en Section 13). **StructuralBreak n'est PAS redondant** avec les dimensions existantes au sens de la corrélation linéaire/de rang.

**Information unique mesurée** : 88 barres (11.06% des propres transitions stabilisées de StructuralBreak, 0.76% de toutes les paires de barres) où StructuralBreak change **seul**, sans qu'aucune des 5 autres dimensions ne change — un signal réel, modeste, réparti sur les 9 semaines du dataset (pas un artefact ponctuel).

**Autour des transitions réelles de `MarketState`** : `StructuralBreak` est **plat** (§16) — il ne présente aucun pic distinctif au moment `t=0` d'un changement de régime ; il est déjà élevé (~0.86) dix barres avant et le reste dix barres après. Ce n'est pas un marqueur d'événement de transition, c'est un état de fond persistant.

**Stabilisation** : réduit la variance de ~78% (0.00905→0.00198) mais les transitions de seulement ~28.3% (ratio stabilisé/brut = 0.717) — **beaucoup moins agressif que le gel de 95%+ observé pour Persistence au Lot 14.17.**

**Verdict final (Section 26)** : **REWEIGHTING NOT READY.** Non par manque d'information — une information réelle, non redondante existe — mais parce que le champ `Strength` actuel (le seul champ continu du contrat) est prouvé non fonctionnel comme signal continu une fois la détection déclenchée. Toute repondération construite directement sur `Strength` aujourd'hui encoderait silencieusement un défaut de contrat. Une redéfinition explicite de `Strength` (hors mandat de ce lot) devrait précéder toute repondération.

---

# 2. SCOPE

Audit statistique pur. Aucun fichier de production modifié — confirmé par `git status` (seul `Tests/Research/StructuralBreakInformationAudit/` est nouveau, untracked). Aucune calibration, aucun reweighting, aucune comparaison P&L/Sharpe/win-rate.

---

# 3. LOT 15.7 CONTEXT

Architecture State-based sans Freshness/Persistence dédiées, retenue et implémentée telle quelle au Lot 15.8. Ce lot ne remet rien en cause de cette architecture — il en audite la qualité informationnelle a posteriori.

---

# 4. LOT 15.8 CONTEXT

`StructuralBreakEvidenceRule` implémentée, 5 fichiers touchés, aucune `IDecisionRule` ne consomme encore la dimension. Découverte initiale : `Strength=1.0` pour 100% des barres `Detected=true` sur un tirage Yahoo (10758/10758). Ce lot confirme ce chiffre sur un tirage frais indépendant (10764/10764) et en détermine l'origine exacte.

---

# 5. DATASET

Dataset Yahoo MES=F M5 réel, tirage frais du 27/08/2026, profondeur maximale disponible (~60 jours). `DatasetFingerprint=DA884E942EDB61C7168F73A660BB52404E1F1C5EADC0353E1B024B78B775B933`, identique sur les 11 CSV produits (même tirage partagé par toutes les analyses de ce lot — confirmé). `ExecutedAtUtc` entre 11:36 et 11:58 UTC le 27/08/2026. `ObservedBars≈11556` (léger écart de dénominateur selon les métriques : 11537 barres avec CUSUM valide, 11493 avec Bai-Perron valide, cohérent avec un warmup différent par méthode — pas une divergence de dataset). Couverture calendaire : 60 jours, 47 jours avec au moins un changement StructuralBreak stabilisé (78.3%). Dérive jour-à-jour normale par rapport aux tirages des Lots 15.6/15.7/15.8 (fenêtre Yahoo glissante, non déterministe par construction, déjà documenté).

---

# 6. PIPELINE TRACE

```
1. PeakMagnitude = Max(PositiveCusum, |NegativeCusum|)   N=10764  [12.59 .. 264647.18]   Var=78 761 691
2. Threshold                                             N=10764  [0.31 .. 9756.52]      Var=151 767
3. RawRatio = PeakMagnitude / Threshold (AVANT clamp)     N=10764  [1.00009 .. 5982.76]   Var=20 239   Mean=46.95
4. Cusum.Confidence = Clamp(RawRatio, 0, 1)               N=10764  [1.0 .. 1.0]           Var=0        DistinctValues=1
5. Contract.Strength (= Confidence, verbatim)             N=10764  [1.0 .. 1.0]           Var=0        DistinctValues=1
6. RawValue[StructuralBreak] (pré-FusionStateManager)     N=10764  [1.0 .. 1.0]           Var=0        DistinctValues=1
7. StableValue[StructuralBreak] (post-FusionStateManager) N=10764  [0.200 .. 0.880]        Var=0.000257 DistinctValues=447
```

**L'effondrement de variance a lieu STRICTEMENT entre l'étape 3 et l'étape 4** — c'est-à-dire dans `Math.Clamp(ratio, 0.0, 1.0)`, une ligne de `CusumStatistics.Compute` (code protégé, non touché). Les étapes 4→5→6 sont rigoureusement identiques (`StructuralBreakEvidenceRule` n'introduit aucune perte supplémentaire — elle hérite fidèlement de ce que CUSUM lui fournit). **Fait notable et contre-intuitif** : l'étape 7 (après EMA+hystérésis) **retrouve de la variance** (447 valeurs distinctes) — parce que l'EMA lisse sur toute l'historique (y compris la phase de montée continue AVANT détection, où `Confidence` varie réellement — voir Section 7), et converge de façon asymptotique vers le plafond 1.0 sans jamais tout à fait l'atteindre avant que l'hystérésis ne gèle la valeur. La stabilisation, ici, ne détruit pas de granularité supplémentaire — elle en réintroduit, par un effet de bord de sa propre dynamique de convergence.

---

# 7. CUSUM STRENGTH DISTRIBUTION

| Sous-groupe | N | Mean | Median | StdDev | Min | Max | Distinct |
|---|---|---|---|---|---|---|---|
| Toutes barres valides | 11537 | 0.9804 | 1 | 0.0865 | 0.2179 | 1 | 767 |
| `ChangeDetected=true` | 10764 | 1 | 1 | 0 | 1 | 1 | **1** |
| `ChangeDetected=false` | 773 | 0.7069 | 0.7169 | 0.1778 | 0.2179 | 0.99995 | 766 |

**Quasi-totalité des 767 valeurs distinctes observées sur l'ensemble du dataset proviennent des 773 barres NON détectées** (766 valeurs distinctes) — la phase de montée pré-détection est le SEUL endroit où `Confidence` porte une information continue réelle. Longueur des plateaux (valeur `Confidence` constante d'une barre à l'autre) : 1110 plateaux, moyenne 10.39 barres, médiane 1, maximum 235 (= la plus longue séquence de détection continue, cohérent avec les Lots 15.6/15.8).

**Délai de saturation (lag)** : sur les 10 premiers plateaux examinés, la valeur de `Confidence` atteint son plafond **exactement à la même barre** où `ChangeDetected` devient vrai — **lag=0 dans tous les cas examinés**, jamais de montée progressive après le déclenchement. La saturation est instantanée à l'onset, pas graduelle.

---

# 8. SATURATION ANALYSIS

Réponse directe à la question du brief §3 ("la saturation existe-t-elle dans CUSUM lui-même, ou est-elle introduite par `StructuralBreakEvidenceRule` ?") : **elle existe intégralement dans CUSUM lui-même**, précisément dans le `Math.Clamp` appliqué à `peakMagnitude/threshold` (Section 6, étape 3→4). `StructuralBreakEvidenceRule` est un passe-plat fidèle, sans responsabilité dans cette perte.

---

# 9. PRE-SATURATION ANALYSIS

Voir Section 6, table complète. Le ratio brut (étape 3) est **fortement informatif** : plage 1.0 à 5982.8, avec une moyenne de 46.95 — un facteur ~47× au-dessus du seuil en moyenne une fois détecté, et jusqu'à ~5983× dans les cas extrêmes. Cette variance considérable est purement et simplement effacée par le clamp `[0,1]`. Aucune modification proposée — c'est un fait à documenter, la redéfinition éventuelle de `Strength` (ex. `log(RawRatio)` ou une transformation bornée non binaire) est une décision de contrat hors mandat de ce lot.

---

# 10. BAIPERRON INFORMATION CONTENT

| N | Min | Max | Mean | Median | Variance | Distinct |
|---|---|---|---|---|---|---|
| 11493 | 1 | 8 | 5.1015 | 5 | 1.5887 | 8 |

Histogramme : 1→0.15%, 2→2.49%, 3→7.74%, 4→19.73%, 5→30.71%, 6→26.33%, 7→11.54%, 8→1.33% — distribution en cloche, centrée sur 4-6 (76.8% des barres). **Fréquence de changement bar-à-bar : 17.93%** (2060/11492 paires) — BaiPerron.BreakCount N'EST PAS constant, il varie régulièrement, mais dans une plage étroite (1 à 8). Plateaux : 2061, moyenne 5.58 barres, médiane 4, maximum 56.

**Conclusion factuelle, sans interprétation** : BreakCount porte une information temporelle réelle (change ~1 barre sur 5.6), mais son amplitude est bornée et sa distribution centrée — ce n'est ni un signal binaire figé, ni un signal à forte variance comme le ratio CUSUM brut. Aucune conclusion tirée sur le fait qu'une valeur élevée signifie "rupture plus forte" (non prouvé, non testé ici).

---

# 11. CUSUM VS BAIPERRON

|  | Bai-Perron Yes (BreakCount>0) | Bai-Perron indisponible |
|---|---|---|
| **CUSUM Yes** (Both) | 10722 (92.783%) | — |
| **CUSUM No, BaiPerron Yes** (BaiPerronOnly) | 771 (6.672%) | — |
| **CUSUM Yes, BaiPerron No** (CusumOnly) | 0 (0%) | — |
| **Neither** | 0 (0%) | — |
| **Unavailable** (Bai-Perron invalide) | — | 63 (0.545%) |

Cohérent avec le Lot 15.8 (92.765%/6.681%/0.009%/0%/0.545% — écarts de l'ordre de la barre unité, dérive de tirage normale).

---

# 12. AGREEMENT

Transitions de la catégorie `Agreement` : 682 (5.902% des paires consécutives). Durée moyenne d'une catégorie avant changement : 16.92 barres (médiane ~3 — distribution fortement asymétrique, cohérente avec le motif observé partout dans cet audit : beaucoup de plateaux courts, quelques très longs). 683 plateaux au total, le plus long = 235 barres.

---

# 13. REDUNDANCY

| Autre dimension | PearsonRaw | PearsonStable | SpearmanRaw | SpearmanStable | SB Transitions | Autre Transitions | Co-transitions | %Co/SB | %Co/Autre |
|---|---|---|---|---|---|---|---|---|---|
| Stationarity | 0.0034 | 0.0750 | -0.0474 | -0.0784 | 796 | 2029 | 157 | 19.72% | 7.74% |
| Persistence | 0.0347 | 0.0952 | 0.0225 | 0.1574 | 796 | 584 | 25 | 3.14% | 4.28% |
| MeanReversion | -0.1504 | -0.0346 | -0.2791 | -0.2589 | 796 | 3535 | 130 | 16.33% | 3.68% |
| StructuralStability | **NaN** | 0.1376 | **NaN** | 0.1881 | 796 | 10653 | 708 | 88.94% | 6.65% |
| RandomWalk | 0.0302 | 0.0794 | 0.0289 | 0.0628 | 796 | 3813 | 303 | 38.06% | 7.95% |

**Explication du `NaN` sur StructuralStability (RawValue)** : `EvidenceFusionEngine.Fuse` (les 5 `IFusionRule` de production) **ne produit jamais d'entrée `StructuralStability` dans `FusionResult.Dimensions`** — `StructuralStabilityRule` n'implémente PAS `IFusionRule` ; elle est invoquée séparément par `FusionStateManager.Update` via `EvaluateAnalysis`, et sa valeur n'est injectée qu'APRÈS (`ReplaceStructuralStability`). La série "RawValue[StructuralStability]" utilisée ici est donc une constante (valeur de repli 0.0), à variance nulle — d'où une corrélation de Pearson/Spearman structurellement indéfinie (0/0), **pas une erreur de calcul**. C'est une confirmation supplémentaire, indépendante, de la nature auto-référentielle déjà établie de `StructuralStability` (Lots 15.2/15.6/15.8).

**Lecture du 88.94% de co-transition avec StructuralStability (stabilisé)** : à interpréter avec prudence — `StructuralStability` transitionne sur 10653/11555 barres (**92.2% de TOUTES les barres**), donc un taux de co-occurrence de 88.94% avec les transitions (bien plus rares) de StructuralBreak est **proche, voire légèrement EN DESSOUS, du taux de base attendu par pur hasard** (92.2%) — ce n'est pas une preuve de synchronisation réelle, c'est largement un artefact du fait que StructuralStability bouge presque tout le temps.

**Conclusion factuelle** : toutes les corrélations linéaires/de rang mesurables sont faibles (|r|≤0.28 partout, la plupart <0.16). **Aucune dimension existante ne prédit StructuralBreak de façon forte.** MeanReversion présente la corrélation de rang la plus marquée (Spearman≈-0.26 à -0.28, négative — cohérent avec l'intuition qu'un marché en rupture structurelle a moins de comportement de retour à la moyenne), mais reste une corrélation faible-à-modérée, pas une redondance.

---

# 14. UNIQUE INFORMATION

- **StructuralBreak transitionne SEUL** (aucune des 5 autres dimensions ne transitionne au même bar) : **88 barres** (0.762% de toutes les paires consécutives ; **11.055% des 796 transitions propres de StructuralBreak**).
- **Au moins une autre dimension transitionne, StructuralBreak reste silencieux** : 9945 barres (86.07% des paires ; 93.35% des 10653 barres où au moins une des 5 autres dimensions transitionne — dominé par StructuralStability, qui transitionne presque partout).
- Répartition hebdomadaire des 88 événements "StructuralBreak seul" : 4 à 19 par semaine ISO sur les 9 semaines du dataset (semaines 27 à 35) — **répartis sur toute la période, pas concentrés dans un sursaut isolé.**

**Conclusion factuelle** : une information réellement unique et non triviale existe (88 barres, ~11% des propres transitions), modeste en volume absolu mais réelle et distribuée dans le temps — ce n'est pas un artefact de bruit isolé.

---

# 15. REGIME CONDITIONAL ANALYSIS

| Régime | N | %Detected | Strength (moy/méd, si détecté) | BreakCount moy/méd | %Both | %BaiPerronOnly | %Unavailable |
|---|---|---|---|---|---|---|---|
| Unknown | 0 | — | — | — | — | — | — |
| StableRange | 162 | 93.21% | 1 / 1 | 4.364 / 5 | 71.61% | 5.56% | 22.84% |
| MeanReverting | 6827 | 89.58% | 1 / 1 | 5.041 / 5 | 89.57% | 10.41% | 0.015% |
| Trending | 487 | 90.97% | 1 / 1 | 5.437 / 6 | 89.94% | 5.13% | 4.93% |
| Transitional | 0 | — | — | — | — | — | — |
| StructuralBreak | 3574 | 99.69% | 1 / 1 | 5.075 / 5 | 99.66% | 0.31% | 0.03% |
| RandomWalk | 506 | 97.04% | 1 / 1 | 5.379 / 5.5 | 97.04% | 2.96% | 0% |

Cohérent avec les Lots 15.6/15.8. `Strength` reste à 1 sur TOUS les régimes une fois détecté (confirme que la saturation est un phénomène de source, indépendant du régime). `%Unavailable` élevé pour `StableRange` (22.84%, N=162 faible) — signal de fiabilité statistique limitée pour ce régime, déjà connu (rareté structurelle de StableRange, ~1% du dataset).

---

# 16. TRANSITION ANALYSIS

**POST-HOC DESCRIPTIF UNIQUEMENT — jamais présenté comme causal temps réel.** 714 transitions de `MarketState` observées. Moyenne de `StableValue[StructuralBreak]` par décalage :

| Offset | -10 | -5 | -1 | 0 | +1 | +5 | +10 |
|---|---|---|---|---|---|---|---|
| StableValue moyen | 0.8592 | 0.8610 | 0.8630 | 0.8640 | 0.8642 | 0.8643 | 0.8631 |
| RawValue moyen | 0.9838 | 0.9859 | 0.9922 | 0.9946 | 0.9913 | 0.9893 | 0.9841 |
| Moyenne des 5 autres dimensions (stabilisées) | 0.4008 | 0.4032 | 0.4058 | 0.4038 | 0.4017 | 0.3958 | 0.3923 |

**StructuralBreak est essentiellement PLAT autour des transitions de régime** — déjà élevé 10 barres avant, le reste 10 barres après, sans pic distinctif au moment `t=0`. Ce n'est PAS un marqueur d'événement de transition ; c'est un état de fond persistant, cohérent avec le taux de détection élevé et quasi-constant observé partout (89-99.7% selon le régime, Section 15).

---

# 17. RAW VS STABILIZED

| Série | Variance | %Frozen (bar-à-bar) | Plus longue série gelée | Transitions | %Transitions |
|---|---|---|---|---|---|
| Raw | 0.009052 | 90.394% | 235 | 1110 | 9.606% |
| Stable | 0.001977 | 93.111% | 260 | 796 | 6.889% |
| Ratio (Stable/Raw transitions) | | | | | **0.7171** |

**Réduction de variance : ~78.2%** (0.001977/0.009052 = 0.2185, soit une réduction de 78.15%). **Réduction du nombre de transitions : ~28.3%** (ratio 0.717, la stabilisée conserve 71.7% des transitions brutes). **Comparaison au Lot 14.17 (Persistence, gel de 95%+ des mouvements bruts)** : StructuralBreak est **beaucoup moins agressivement gelé** que Persistence ne l'était — le mécanisme EMA+hystérésis, appliqué au même Alpha/seuil, détruit nettement moins d'information temporelle pour cette dimension. Explication plausible (non prouvée formellement ici) : la dynamique de StructuralBreak (montées continues suivies de plateaux saturés) franchit le seuil d'hystérésis plus facilement que les petites oscillations fines caractéristiques de Persistence.

---

# 18. TEMPORAL DIVERSITY

796 transitions stabilisées → 797 "états". Durée moyenne d'un état : **14.50 barres** ; distribution fortement asymétrique (cohérente avec les plateaux Section 7/12 : beaucoup d'états très courts, quelques-uns très longs jusqu'à 260 barres). Couverture calendaire : 47/60 jours (78.3%) avec au moins un changement. Fréquence quotidienne de changement : de 2 (26/07) à 32 (10/08, 12/08, 17/08) — variabilité réelle jour à jour, aucune concentration anormale sur un seul jour.

**Conclusion factuelle** : StructuralBreak possède une dynamique temporelle réelle, distribuée sur l'ensemble du dataset — ce n'est ni un état figé/permanent, ni un bruit haute fréquence uniforme, mais un signal à dynamique bursty/asymétrique.

---

# 19. ABLATION OBSERVATION

Puisqu'aucune `IDecisionRule` ne consomme `FusionDimension.StructuralBreak` (Lot 15.8), la comparaison A (5 dimensions) vs B (5+StructuralBreak) est **triviale par construction sur les 5 dimensions partagées** : elles sont prouvées bit-identiques entre A et B (test de régression Lot 15.8, 0 barre différente sur `MarketState.Winner`). Le seul contenu NOUVEAU entre A et B est la distribution propre de StructuralBreak elle-même — exactement ce que caractérisent les Sections 6-18 ci-dessus. Aucune mesure d'impact économique n'est ni possible ni tentée, puisqu'aucune règle de décision n'y accède encore.

---

# 20. EVIDENCE QUALITY CLASSIFICATION

**C — PARTIALLY INFORMATIVE**, avec une réserve explicite : le sous-champ `Strength`, évalué isolément, relèverait de la catégorie **E — SATURATED / CONTRACT PROBLEM** (Sections 6-9). La classification globale C reflète que le CONTRAT dans son ensemble (Detected + BreakLocationBarIndex + BreakCountMagnitude + Agreement, au-delà du seul Strength) porte une information réelle, non redondante (Section 13), partiellement unique (Section 14) et temporellement dynamique (Section 18) — mais insuffisamment discriminante autour des événements qui compteraient le plus pour une future décision (transitions de régime, Section 16 : signal plat) et handicapée par un champ continu (`Strength`) non fonctionnel une fois la détection déclenchée.

---

# 21. DECISION TREE

```
Strength saturated at source ?
  → OUI (Section 8/9 : saturation dans CUSUM lui-même, Math.Clamp, étape 3→4)
  → inspect CUSUM contract : le clamp [0,1] du ratio brut (variance native jusqu'à 5983×
    le seuil) est la cause identifiée. StructuralBreakEvidenceRule n'ajoute aucune saturation
    supplémentaire (étapes 4→5→6 identiques).

Saturation introduced by adapter ?
  → NON (StructuralBreakEvidenceRule est un passe-plat fidèle)

StructuralBreak highly redundant ?
  → NON (Section 13 : |Pearson|/|Spearman| ≤ 0.28 partout, la plupart < 0.16 ; le seul chiffre
    élevé — 88.94% co-transition avec StructuralStability — est un artefact de base-rate,
    StructuralStability transitionnant sur 92.2% de toutes les barres)
  → reweighting n'est PAS automatiquement injustifié pour cette seule raison.

StructuralBreak contains unique information ?
  → OUI, partiellement (Section 14 : 88 barres/11.06% des transitions propres, réparties sur
    9 semaines)
  → une future expérience de repondération PEUT être scientifiquement justifiée — À CONDITION
    que le contrat soit d'abord corrigé (voir ci-dessous).

Evidence is mostly binary but useful ?
  → PARTIELLEMENT VRAI pour Strength seul (binaire de fait : constant 1.0 une fois détecté),
    FAUX pour BreakCountMagnitude (plage réelle 1-8, change 17.9% des barres) et pour
    Agreement (transitionne 5.9% des barres, plateaux réels)
  → une révision de l'architecture DecisionRule (ou, plus précisément, une révision du CONTRAT
    StructuralBreakEvidenceRule lui-même) est justifiée AVANT toute repondération basée sur
    Strength.

CONCLUSION : aucune branche unique ne suffit — la situation réelle combine "saturation à la
source confirmée" + "redondance faible" + "information unique réelle mais modeste" +
"champ continu actuellement inutilisable". Verdict : REWEIGHTING NOT READY (voir Section 26),
PAS INCONCLUSIVE (les faits sont clairs et suffisants pour trancher).
```

---

# 22. SCIENTIFIC LIMITATIONS

1. Dataset unique de ~60 jours — toute généralisation au-delà de cette fenêtre n'est pas garantie (rappel constant depuis le Lot 15.0).
2. Les corrélations de Section 13 sont linéaires/de rang uniquement — n'excluent pas une dépendance non-linéaire plus complexe, non testée ici (hors mandat, aurait nécessité une méthode plus lourde).
3. Le "unique information test" (Section 14) dépend de la définition de transition héritée de Lot 15.8 (égalité exacte bar-à-bar sur la valeur stabilisée) — une définition alternative (ex. seuil de mouvement minimal) donnerait des chiffres différents ; celle utilisée est la seule déjà existante et non inventée pour ce lot.
4. `StructuralStability` (RawValue) n'a structurellement pas de valeur "brute" pré-fusion exploitable (Section 13) — les comparaisons de redondance avec elle reposent uniquement sur la valeur stabilisée.

---

# 23. WHAT IS PROVEN

- La saturation de `Strength` existe dans `CusumStatistics.Compute` (production, protégé), pas dans `StructuralBreakEvidenceRule`.
- Le lag entre détection et saturation est nul (0 barre) sur tous les cas examinés.
- `BaiPerron.BreakCount` n'est pas constant (plage 1-8, change 17.9% des barres).
- StructuralBreak n'est pas fortement redondant avec aucune des 5 autres dimensions (corrélations faibles).
- Une information unique, réelle et distribuée dans le temps existe (88 barres, 11.06% des transitions propres).
- StructuralBreak ne marque pas les transitions de `MarketState` par un pic distinctif — il est plat autour de `t=0`.
- La stabilisation réduit la variance de ~78% mais les transitions de seulement ~28%, un gel bien moins sévère que celui mesuré pour Persistence au Lot 14.17.
- StructuralBreak possède une dynamique temporelle réelle (durées d'état très asymétriques, changements répartis sur 78% des jours du dataset).

---

# 24. WHAT IS NOT PROVEN

- Qu'une repondération future améliorerait quoi que ce soit économiquement (jamais mesuré, jamais tenté, hors mandat).
- Qu'un `BreakCount` élevé correspond à une "rupture plus forte" au sens scientifique (corrélation non établie ici).
- Qu'une dépendance non-linéaire plus riche n'existe pas entre StructuralBreak et les autres dimensions (seules Pearson/Spearman ont été mesurées).
- Que la définition actuelle de `Strength` est la SEULE cause de son manque d'utilité — une redéfinition (ex. transformation non bornée du ratio brut) n'a pas été testée ici (interdite dans ce lot).

---

# 25. REWEIGHTING READINESS

**NOT READY.** Raison précise : le seul champ continu du contrat (`Strength`) est prouvé constant (donc non informatif) une fois la détection déclenchée — pondérer dessus aujourd'hui reviendrait, de fait, à pondérer sur le booléen `Detected` tout en prétendant utiliser un score continu. Les champs réellement porteurs d'information non redondante et non triviale (`BreakCountMagnitude`, `Agreement`, et le sous-ensemble de transitions "uniques" identifié Section 14) existent mais n'ont pas de rôle défini dans le contrat actuel au-delà du diagnostic.

---

# 26. RECOMMENDED NEXT ACTION

**Ne pas enchaîner directement sur un lot de reweighting.** Décision scientifique suivante recommandée : un lot de **redéfinition du contrat `Strength`** (nom indicatif : "StructuralBreak Strength Contract Revision") qui déciderait, sans référence au P&L, comment transformer le ratio brut CUSUM (variance native prouvée ici jusqu'à ~5983×) en un score continu non saturé — par exemple une transformation logarithmique ou une normalisation par quantile empirique — AVANT tout lot de repondération. Ce futur lot devrait rester, comme celui-ci, un audit/design puis une implémentation minimale isolée à `StructuralBreakEvidenceRule`/`CusumResult`-consommation uniquement (jamais toucher `CusumStatistics.Compute` lui-même, qui reste protégé et sert d'autres consommateurs).

---

# 27. HANDOFF CONTEXT

```
PROJECT:
IQIA

CURRENT LOT:
15.9

PREVIOUS LOT:
15.8

STRUCTURAL BREAK:
IMPLEMENTED AS FUSION DIMENSION (Lot 15.8), NOT YET CONSUMED BY ANY DECISION RULE. This lot (15.9)
audited its information content - pure analysis, zero production changes.

CUSUM:
ChangeDetected/Confidence both real production fields. Confidence = Clamp(peakMagnitude/threshold,0,1)
inside CusumStatistics.Compute (protected, unmodified).

CUSUM STRENGTH:
PROVEN saturated at exactly 1.0 for 100% of ChangeDetected=true bars (10764/10764 on this lot's fresh
pull, matching Lot 15.8's 10758/10758 on a prior pull). Zero lag between detection onset and saturation
(confirmed on 10 examined transitions). All 767 distinct Confidence values across the whole dataset come
almost entirely (766/767) from the 773 NOT-detected bars - the pre-detection ramp is the only place this
field carries continuous information.

SATURATION LOCATION:
Precisely between pipeline stage 3 (RawRatio = PeakMagnitude/Threshold, BEFORE clamp - variance 20239,
range 1.0 to 5982.8) and stage 4 (Cusum.Confidence, AFTER Math.Clamp(ratio,0,1) - variance 0, 1 distinct
value). The saturation is INSIDE CUSUM's own production code (CusumStatistics.Compute), NOT introduced by
StructuralBreakEvidenceRule (stages 4→5→6 are bit-identical - a faithful pass-through). Stage 7 (post-
FusionStateManager) paradoxically regains variance (447 distinct values, range 0.20-0.88) because the EMA
tracks the full history including the informative pre-detection ramp and never perfectly reaches the
saturated ceiling before hysteresis freezes it.

BAI-PERRON:
BreakCount is NOT constant: range 1-8, mean 5.10, variance 1.59, bell-shaped distribution centered on 4-6
(76.8% of bars), changes on 17.93% of consecutive bar pairs. A real but narrow-range, low-amplitude signal.

BREAK COUNT:
See BAI-PERRON above - same field.

AGREEMENT:
Both=92.78%, BaiPerronOnly=6.67%, CusumOnly=0%, Neither=0%, Unavailable=0.55% (consistent with Lot 15.8).
Category transitions on 5.90% of bar pairs, mean duration 16.92 bars before a category change (median ~3,
heavily right-skewed).

REDUNDANCY:
LOW with all 5 existing Fusion dimensions. Pearson/Spearman (raw and stabilized) all |r|<=0.28, most
<0.16. StructuralStability's RAW series is structurally undefined (NaN correlation) because
EvidenceFusionEngine.Fuse never populates a StructuralStability entry (StructuralStabilityRule is not an
IFusionRule - it's injected separately by FusionStateManager AFTER fusion) - not a computation error, a
confirmation of its already-known self-referential nature. The 88.94% co-transition rate with
StructuralStability (stabilized) is a base-rate artifact - StructuralStability itself transitions on 92.2%
of ALL bars, so this is not evidence of real synchronization.

UNIQUE INFORMATION:
REAL but modest: 88 bars (11.06% of StructuralBreak's own 796 stabilized transitions, 0.76% of all
consecutive bar pairs) where StructuralBreak transitions with ALL 5 other dimensions staying stable that
same bar. Distributed across all 9 ISO weeks of the dataset (4 to 19 per week) - not a one-off artifact.

RAW DYNAMICS:
Variance 0.009052, 90.39% frozen bar-to-bar, longest frozen run 235 bars, 1110 transitions (9.61% of pairs).

STABILIZED DYNAMICS:
Variance 0.001977 (~78% reduction from raw), 93.11% frozen, longest frozen run 260 bars, 796 transitions
(6.89% of pairs). Transition-count ratio stabilized/raw = 0.7171 (only ~28.3% reduction in transition
count - far less aggressive than Lot 14.17's 95%+ freeze rate for Persistence).

INFORMATION LOSS:
Substantial in variance (~78%) but moderate in transition count (~28%) - FusionStateManager's EMA+
hysteresis destroys markedly less temporal information for StructuralBreak than it did for Persistence.
Around actual MarketState transitions (714 events, event-study offsets -10..+10), StructuralBreak's
stabilized value is essentially FLAT (0.859 to 0.864) - it does not spike/mark the transition moment; it
behaves as a persistent background state, not an event detector.

EVIDENCE QUALITY:
C (PARTIALLY INFORMATIVE) - with an explicit caveat that Strength alone, evaluated in isolation, would be
category E (SATURATED / CONTRACT PROBLEM).

REWEIGHTING READINESS:
NOT READY. The only continuous field in the contract (Strength) is proven non-functional once detection
triggers (constant 1.0) - weighting on it today would silently be equivalent to weighting on the Detected
boolean while appearing to use a continuous score. Genuinely informative fields (BreakCountMagnitude,
Agreement, the unique-transition subset) exist but have no defined role beyond diagnostics in the current
contract.

CALIBRATION:
PAUSED

PRODUCTION MODIFIED:
NO

ATAS:
NOT USED

NEXT SCIENTIFIC DECISION:
Whether/how to redefine Strength as a non-saturating continuous score derived from the RAW CUSUM ratio
(e.g. log-transform, empirical-quantile normalization) - a contract-design decision, never derived from
P&L, to be made BEFORE any reweighting lot. Must not touch CusumStatistics.Compute itself (protected,
serves other consumers) - any redefinition happens downstream, in StructuralBreakEvidenceRule/a successor.

NEXT LOT:
"StructuralBreak Strength Contract Revision" (indicative name) - audit/design first (which non-saturating
transform of the raw CUSUM ratio is scientifically defensible, without inventing an arbitrary parameter),
then a minimal, isolated implementation touching only StructuralBreakEvidenceRule.cs (or a successor file)
- never CusumStatistics.Compute, never FusionStateManager, never any DecisionRule.

DO NOT DO:
Do NOT proceed directly to a StructuralBreakRule reweighting lot using Strength as currently defined - it
would silently encode a saturated/degenerate signal as if it were continuous. Do NOT modify
CusumStatistics.Compute's Math.Clamp to "fix" the saturation - that is shared production code serving
other consumers beyond StructuralBreak, out of this lot's (and any StructuralBreak-scoped lot's) mandate
without a separate, explicitly-authorized audit of its other callers. Do NOT treat the 88.94% co-transition
with StructuralStability as evidence of redundancy - it is a base-rate artifact, explained in this report
§13. Do NOT interpret a high BaiPerron.BreakCount as "a stronger break" - not established by any data in
this lot.
```

**STOP.**
