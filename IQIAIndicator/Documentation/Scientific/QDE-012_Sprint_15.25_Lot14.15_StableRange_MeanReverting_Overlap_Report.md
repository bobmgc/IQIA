# QDE-012 — Sprint 15.25 — Lot 14.15 — StableRange / MeanReverting : Investigation du Chevauchement Structurel

**Date** : 2026-08-24
**Branche** : `feature/structural-stability-v2`
**Lots précédents** : Lot 14.9-14.13 (calibration/dataset), Lot 14.14 (investigation Ambiguity)
**Statut** : **IMPLEMENTED**
**Type** : **AUDIT / INVESTIGATION — AUCUNE MODIFICATION DE PRODUCTION, AUCUN POIDS MODIFIÉ, AUCUNE CALIBRATION**

---

# 1. EXECUTIVE SUMMARY

Ce lot répond à la question laissée ouverte par le Lot 14.14 : `StableRangeRule` et `MeanRevertingRule` sont-elles deux hypothèses de marché réellement distinctes, ou deux formulations du même phénomène ?

**Réponse, établie par audit du code puis confirmée quantitativement par une expérience d'ablation sur données réelles (10 930 barres, MES=F/M5/59 jours)** :

- La corrélation `0.9652` du Lot 14.14 est **reproduite quasi à l'identique sur un dataset frais** (Pearson=0.9653, Spearman=0.9658, dataset téléchargé un jour plus tard, fingerprint différent) — ce n'est pas un artefact d'une seule fenêtre.
- **Expérience d'ablation (§17), la preuve la plus directe de ce lot** : neutraliser `Stationarity`+`MeanReversion` (poids maximal partagé) dans une copie du `FusionResult` réel, puis ré-exécuter les VRAIES classes `MeanRevertingRule`/`StableRangeRule` sur cette copie, fait **s'effondrer** la corrélation de 0.9653 à **0.1033**. À l'inverse, neutraliser `Persistence` seule fait **monter** la corrélation à 0.9913 (au-dessus de la ligne de base). Neutraliser `StructuralStability` seule ne change presque rien (0.9707). **Cela prouve, sans ambiguïté et sans modifier aucune formule de production, que `Stationarity`+`MeanReversion` sont la cause structurelle dominante du chevauchement, et que `Persistence` est le seul levier de séparation actif dans la formule actuelle.**
- Sur données réelles, `Corr(Persistence, ScoreDifference[MR-SR]) = -0.8141` — une corrélation FORTE, confirmant que le test synthétique du Lot 14.14 n'était pas une coïncidence : `Persistence` explique statistiquement l'essentiel de l'écart entre les deux règles, quand cet écart existe.
- Mais `Persistence` elle-même varie PEU sur ce dataset la plupart du temps (Lot 14.14 : P25-P75 = [0.133, 0.148], bande étroite) — le levier de séparation EXISTE dans la formule mais ne s'active fortement que rarement sur cette fenêtre de marché précise.
- `StableRange` est le RunnerUp de `MeanReverting` 56.7% du temps ; `StructuralBreak` (une règle NON étudiée en détail par ce lot mais mesurée ici) est RunnerUp 40.4% du temps — **le chevauchement dominant est bien StableRange↔MeanReverting, mais StructuralBreak n'est pas négligeable comme second concurrent proche.**
- Analyse temporelle : corrélation quotidienne stable en moyenne (0.9717 sur 40 jours) mais avec au moins 8-9 journées sous 0.95 (min 0.8703) — des épisodes de divergence RÉELS existent, minoritaires mais mesurables.

**Hypothèse retenue (§23)** : **Hypothèse D** — les deux règles sont PARTIELLEMENT distinctes ; un discriminant réel (`Persistence`) existe déjà dans la formule actuelle, mais son poids (0.20 dans `MeanRevertingRule`, absent de `StableRangeRule`) et sa faible variabilité naturelle sur ce dataset limitent son effet pratique. Ni Hypothèse B ni C (aucune des deux règles n'est un sous-cas strict de l'autre — chacune porte un terme que l'autre n'a pas).

**Aucune modification de production. Aucun poids touché. Aucune calibration.**

---

# 2. LOT 14.14 FINDINGS (repris tels quels)

- `AmbiguityScore = Clamp(1-(Winner-RunnerUp),0,1)` — formule correcte, non fautive.
- Cause primaire : C (Weighting Issue) — `StableRangeRule`↔`MeanRevertingRule` Corr=0.9652.
- Cause secondaire : B (Score Distribution Issue).
- `FusionConfiguration` = DEAD ; Decision weights = DEAD (aucun type de configuration).
- `OverallConfidence` binaire empiriquement (1.0 exact si Winner=MeanReverting, 0.0 exact sinon).
- Test synthétique : diverger sur `Persistence` sépare les candidats ; pousser conjointement `Stationarity`+`MeanReversion` ne le fait PAS.

---

# 3. SCIENTIFIC QUESTION

> `StableRangeRule` et `MeanRevertingRule` sont-elles deux hypothèses de marché distinctes qui méritent d'exister séparément, ou deux formulations du même phénomène ?

Décomposée en 7 sous-questions (brief) — traitées §4 à §21.

---

# 4. MEANREVERTINGRULE ARCHITECTURE

Source : `Engine/Decision/Rules/MeanRevertingRule.cs`.

```
Inputs (FusionResult.Dimensions requis) : MeanReversion, Stationarity, Persistence, StructuralStability
Rejet si l'une des 4 dimensions est absente du FusionResult (jamais observé sur ce dataset — les 4 sont
toujours présentes après FusionStateManager, Lot 14.14 §10).
EffectiveValue(dim) = dim.IsAvailable ? Clamp(dim.Value,0,1) : 0.5 (neutre, jamais 0/1 fabriqué)

scientificScore = 0.40×MeanReversion + 0.30×Stationarity + 0.20×(1-Persistence) + 0.10×StructuralStability
qualityScore    = 0.40×MR.Confidence + 0.30×Stat.Confidence + 0.20×Pers.Confidence + 0.10×SS.Confidence
finalScore      = Clamp(0.90×scientificScore + 0.10×qualityScore, 0, 1)

Condition d'activation : finalScore > builder.Confidence (TOUJOURS vrai en pratique — Lot 14.14 §12,
builder toujours frais). Condition de rejet : dimensions requises absentes.
```

---

# 5. STABLERANGERULE ARCHITECTURE

Source : `Engine/Decision/Rules/StableRangeRule.cs`.

```
Inputs (FusionResult.Dimensions requis) : Stationarity, MeanReversion, StructuralStability
(PAS de Persistence — absence structurelle, pas un oubli : la règle ne consomme jamais cette dimension)
EffectiveValue(dim) = idem MeanRevertingRule (sentinelle neutre 0.5 si IsAvailable=false)

scientificScore = 0.40×Stationarity + 0.40×MeanReversion + 0.20×StructuralStability
qualityScore    = 0.40×Stat.Confidence + 0.40×MR.Confidence + 0.20×SS.Confidence
finalScore      = Clamp(0.90×scientificScore + 0.10×qualityScore, 0, 1)

Mêmes conditions d'activation/rejet que MeanRevertingRule.
```

## Table de comparaison (brief §1)

| Dimension | MeanReverting (poids) | StableRange (poids) | Shared? |
|---|---:|---:|---|
| Stationarity | 0.30 | 0.40 | **Oui** |
| MeanReversion | 0.40 | 0.40 | **Oui** |
| Persistence | 0.20 (inversé, `1-Pers`) | **0.00 (absent)** | **Non — discriminant unique de MeanReverting** |
| StructuralStability | 0.10 | 0.20 | Oui, poids asymétrique |
| RandomWalk | 0.00 (absent) | 0.00 (absent) | Non pertinent (aucune des deux règles ne l'utilise) |

**Recoupement pondéral** : `0.30+0.40=0.70` du poids scientifique de `MeanRevertingRule` porte sur des dimensions PARTAGÉES avec `StableRangeRule` (Stationarity+MeanReversion) ; `0.40+0.40=0.80` pour `StableRangeRule`. Seuls `0.20` (MeanReverting, via Persistence) et une PART du `0.20`/`0.10` de StructuralStability (poids asymétrique, pas binaire partagé/non-partagé) échappent réellement au recoupement direct.

---

# 6. FORMULA COMPARISON

```
MeanReverting : Input{MR,Stat,Pers,SS} → EffectiveValue → [0.40,0.30,0.20(inv),0.10] → Σ → Blend(0.9,0.1) → Clamp → FinalScore
StableRange   : Input{Stat,MR,SS}      → EffectiveValue →      [0.40,0.40,0.20]        → Σ → Blend(0.9,0.1) → Clamp → FinalScore
```

Différence structurelle unique : le terme `Persistence` (inversé) chez `MeanRevertingRule`, absent chez `StableRangeRule`. Toutes les autres étapes (normalisation, blend scientifique/qualité 90/10, Clamp final) sont IDENTIQUES entre les deux règles — la seule source de divergence FORMELLE possible est ce terme unique.

---

# 7. SHARED DIMENSIONS

| Dimension | MeanReverting | StableRange | Poids | Rôle | Corrélation mesurée avec l'autre dimension partagée |
|---|---|---|---|---|---|
| Stationarity | Oui | Oui | 0.30/0.40 | Confirme un régime "sans tendance" | Corr(Stationarity, MeanReversion) = 0.1831 (faible) |
| MeanReversion | Oui | Oui | 0.40/0.40 | Confirme un retour vers l'équilibre | idem |
| StructuralStability | Oui (poids faible) | Oui (poids modéré) | 0.10/0.20 | Confirme l'absence de rupture récente | Corr(StructuralStability, MeanReversion) = **-0.4815** (la plus forte corrélation de dimension à dimension mesurée) |

**Constat notable** : `Stationarity` et `MeanReversion`, bien que TOUTES DEUX pondérées fortement dans les deux règles, sont elles-mêmes assez FAIBLEMENT corrélées entre elles sur ce dataset (0.1831) — leur redondance N'EST PAS due à une corrélation intrinsèque entre elles, mais au simple fait que les DEUX règles les pondèrent presque identiquement (§17, confirmé par ablation).

---

# 8. DISCRIMINANT DIMENSIONS

**Vérifié sur données réelles, pas supposé** (brief §4, discipline respectée) :

| Dimension | Corr avec MeanRevertingScore | Corr avec StableRangeScore | Corr avec ScoreDifference(MR-SR) | Verdict |
|---|---:|---:|---:|---|
| Persistence | -0.3267 | -0.1000 | **-0.8141** | **DISCRIMINANT CONFIRMÉ — le plus fort** |
| StructuralStability | -0.4632 | -0.3571 | -0.3306 | Discriminant FAIBLE (affecte les deux scores dans le même sens, s'annule partiellement dans la différence) |

`Persistence` a une corrélation avec les scores individuels (MR: -0.33, SR: -0.10) bien plus faible qu'avec leur DIFFÉRENCE (-0.81) — signature typique d'une variable qui agit spécifiquement sur l'ÉCART entre deux quantités corrélées, plutôt que sur leur niveau commun. C'est exactement le rôle attendu d'un vrai discriminant. **L'hypothèse du Lot 14.14 (issue d'un seul test synthétique) est donc confirmée empiriquement, pas seulement supposée** (brief §4 : "cette hypothèse doit être vérifiée sur les données réelles").

---

# 9. DIMENSION CORRELATIONS (10 paires, 10 930 barres)

```
Corr(Stationarity, Persistence)        =  0.0322
Corr(Stationarity, MeanReversion)      =  0.1831
Corr(Stationarity, StructuralStability)= -0.1527
Corr(Stationarity, RandomWalk)         = -0.0879
Corr(Persistence, MeanReversion)       = -0.2252
Corr(Persistence, StructuralStability) =  0.3145
Corr(Persistence, RandomWalk)          = -0.0438
Corr(MeanReversion, StructuralStability)= -0.4815   ← la plus forte
Corr(MeanReversion, RandomWalk)        = -0.0677
Corr(StructuralStability, RandomWalk)  =  0.4811
```

**Aucune dimension "Volatility" n'existe** dans `FusionDimension` (5 valeurs exactement : Stationarity, Persistence, MeanReversion, StructuralStability, RandomWalk) — le brief §3/§5 mentionne "Volatility" comme dimension éventuelle ; ce lot confirme par lecture directe de l'enum qu'elle n'existe PAS au niveau Fusion (un `VolatilityModel` existe, mais dans `Engine/ScientificModels`, une couche différente, en aval de Decision, jamais consommée par `MeanRevertingRule`/`StableRangeRule`).

**Constat** : aucune paire de dimensions n'est fortement corrélée (toutes sous 0.50 en valeur absolue) — la redondance de score entre `MeanRevertingRule` et `StableRangeRule` n'est PAS héritée d'une redondance déjà présente entre les dimensions Fusion elles-mêmes ; elle provient du CHOIX DE PONDÉRATION des deux règles (§17, confirmé par ablation), pas d'une propriété des données amont.

---

# 10. RULE CORRELATION (reproduction)

```
Lot 14.14 (dataset du 2026-08-24, run précédent) : Corr = 0.9652
Lot 14.15 (dataset du 2026-08-24, run frais, fingerprint différent) : Pearson = 0.9653, Spearman = 0.9658
```

**Reproduite quasi exactement** (écart de 0.0001) sur un dataset Yahoo frais (fenêtre glissante, fingerprint `9AE6C21079110DE123B82F89B92BBCCF463BDEF31122DCBBC618446B72030CE7`, distinct de celui du Lot 14.14) — la corrélation n'est pas un artefact d'une capture unique, elle est stable d'un jour sur l'autre sur cette même fenêtre de 59 jours glissante.

---

# 11. SCORE DIFFERENCE

`ScoreDifference = MeanRevertingScore - StableRangeScore`, n=10 930 :

```
min=-0.0667, max=0.0886, mean=0.0558, stdDev=0.0224
P05=0.0102, P25=0.0463, P50=0.0614, P75=0.0712, P95=0.0793, P99=0.0827
```

| Seuil | Barres | % |
|---|---|---|
| \|diff\| < 0.01 | 268 | 2.45% |
| \|diff\| < 0.02 | 489 | 4.47% |
| \|diff\| < 0.05 | 3 345 | 30.60% |
| \|diff\| < 0.10 | 10 930 | 100.00% |

Histogramme (largeur 0.05) :

```
[-0.10,-0.05) :    39
[-0.05, 0.00) :   358
[ 0.00, 0.05) : 2 987
[ 0.05, 0.10) : 7 546   ← majorité absolue
```

**Constat affiné (important nuance vs la lecture superficielle du Lot 14.14)** : la différence n'est PAS centrée sur zéro — elle est majoritairement POSITIVE (MeanReverting devance StableRange dans 69% des barres par 0.05 à 0.09), avec une moyenne de 0.0558. Ce n'est donc pas un cas d'égalité quasi-parfaite (`diff≈0`) la plupart du temps, mais un ÉCART SYSTÉMATIQUE MODÉRÉ ET STABLE — MeanReverting a un avantage structurel constant d'environ 0.05-0.07, jamais nul, jamais très large. C'est justement cet écart MODÉRÉ (ni proche de 0, ni proche du maximum possible ~0.20-0.30) qui produit un `AmbiguityScore` élevé mais pas systématiquement maximal (rappel Lot 14.14 : AmbiguityScore moyen pour Winner=MeanReverting = 0.9571, pas 1.0).

---

# 12. WINNER/RUNNER-UP ANALYSIS

```
Winner=MeanReverting   : 6 519 (59.6%)
Winner=StructuralBreak : 3 359 (30.7%)
Winner=RandomWalk      :   483 (4.4%)
Winner=Trending        :   449 (4.1%)
Winner=StableRange     :   120 (1.1%)
```

**RunnerUp quand Winner=MeanReverting (n=6 519)** :

```
RunnerUp=StableRange     : 3 694 (56.7%)
RunnerUp=StructuralBreak : 2 634 (40.4%)
RunnerUp=RandomWalk      :   170 (2.6%)
RunnerUp=Trending        :    21 (0.3%)
```

**RunnerUp quand Winner=StableRange (n=120, ÉCHANTILLON TRÈS RÉDUIT — prudence)** :

```
RunnerUp=MeanReverting : 92 (76.7%)
RunnerUp=Trending      : 28 (23.3%)
```

**Constat affiné** : `StableRange` EST le RunnerUp dominant de `MeanReverting` (56.7%), confirmant le lien étudié — mais `StructuralBreak` est un second concurrent proche NON négligeable (40.4%). La relation `StableRange`↔`MeanReverting` est bien réciproque (quand StableRange gagne rarement, son concurrent le plus proche est presque toujours MeanReverting, 76.7%) — cohérent avec une paire structurellement proche dans les deux sens. **Ce lot ne conclut PAS que `StructuralBreak` partage le même défaut de conception** (hors scope, non audité en détail ici) — seulement que la paire MeanReverting/StableRange n'explique pas la totalité de la compression d'AmbiguityScore observée au Lot 14.14 pour le régime MeanReverting.

---

# 13. REGIME CONDITIONAL ANALYSIS

`ScoreDifference(MR-SR)` groupé par `Winner` réel :

| Winner | n | mean | stdDev | min | max | Prudence |
|---|---|---|---|---|---|---|
| MeanReverting | 6 519 | 0.0574 | 0.0161 | 0.0003 | 0.0886 | REPRESENTED |
| StructuralBreak | 3 359 | 0.0630 | 0.0136 | -0.0118 | 0.0860 | REPRESENTED |
| RandomWalk | 483 | 0.0623 | 0.0147 | -0.0039 | 0.0785 | **UNDERREPRESENTED** |
| Trending | 449 | -0.0063 | 0.0247 | -0.0571 | 0.0424 | **UNDERREPRESENTED** |
| StableRange | 120 | -0.0266 | 0.0213 | -0.0667 | -0.0002 | **TRÈS UNDERREPRESENTED** |

**Constat (régimes bien représentés uniquement)** : quand `StructuralBreak` gagne, `MeanReverting` reste malgré tout systématiquement AU-DESSUS de `StableRange` (mean diff=0.0630, jamais très négatif) — la relation MR>SR persiste indépendamment de qui remporte l'arbitrage final. Sur les régimes sous-représentés (Trending, StableRange), le signe s'inverse (SR>MR en moyenne) mais l'échantillon est trop petit pour une conclusion forte (brief §9, discipline respectée).

---

# 14. TEMPORAL ANALYSIS

Corrélation `Corr(MeanRevertingScore, StableRangeScore)` par jour calendaire (n≥50 barres/jour, 40 jours retenus) :

```
min=0.8703 (2026-08-13), max=0.9956 (2026-08-21), mean=0.9717, jours=40
```

**Périodes de plus forte divergence identifiées** (corrélation journalière < 0.95) : 2026-06-26 (0.9610 — proche du seuil), 2026-07-07 (0.9407), 2026-07-10 (0.9435), 2026-07-17 (0.8752), 2026-07-29 (0.9208), 2026-07-30 (0.9305), 2026-07-31 (0.9409), 2026-08-13 (0.8703, minimum), 2026-08-14 (0.9377) — soit **9 jours sur 40 (22.5%)** sous 0.95.

**Verdict** : le coefficient `0.9652`/`0.9653` N'EST PAS parfaitement stable — il existe des épisodes réels de divergence plus marquée (jusqu'à 0.87), concentrés sur quelques journées isolées plutôt que répartis uniformément. La majorité des jours (31/40, 77.5%) restent néanmoins au-dessus de 0.95, donc la MOYENNE globale (0.9653) reste représentative de la tendance dominante, pas un artefact de lissage sur des extrêmes opposés. Aucune investigation plus poussée sur la CAUSE de ces journées spécifiques de divergence (calendrier de marché, régime particulier) n'a été menée — hors scope de ce lot.

---

# 15. PERSISTENCE ANALYSIS

```
Corr(Persistence, MeanRevertingScore)      = -0.3267
Corr(Persistence, StableRangeScore)        = -0.1000
Corr(Persistence, ScoreDifference[MR-SR])  = -0.8141   ← résultat central
|ScoreDifference| mean, Persistence < médiane (0.1397) : 0.0635 (n=5 430)
|ScoreDifference| mean, Persistence ≥ médiane          : 0.0517 (n=5 500)
```

`Persistence` influence `MeanRevertingScore` bien plus que `StableRangeScore` (-0.33 vs -0.10 — cohérent avec son poids explicite de 0.20 chez MeanReverting et son absence chez StableRange) — mais c'est sur leur DIFFÉRENCE que son effet est le plus net (-0.81), confirmant qu'elle agit précisément comme un discriminant, pas comme un facteur de niveau général.

**La comparaison par médiane (|diff| moyen 0.0635 vs 0.0517) est moins parlante que la corrélation signée** : la différence signée (§11) est majoritairement positive (MR>SR) ; une `Persistence` élevée RÉDUIT cet avantage de MeanReverting (potentiellement jusqu'à l'inverser, cohérent avec `min(diff)=-0.0667`), donc `|diff|` seule peut masquer ce mécanisme directionnel (une réduction du diff positif ET une inversion vers un diff négatif produisent toutes deux un changement de `|diff|` dans des directions parfois opposées). La corrélation signée est la preuve principale ; la comparaison par médiane est secondaire.

**Réponse à la question du brief §4 ("Persistence est-elle discriminante ?")** : **OUI, confirmée empiriquement**, pas seulement supposée depuis le test synthétique.

---

# 16. STRUCTURAL STABILITY ANALYSIS

```
Corr(StructuralStability, MeanRevertingScore)     = -0.4632
Corr(StructuralStability, StableRangeScore)       = -0.3571
Corr(StructuralStability, ScoreDifference[MR-SR]) = -0.3306
```

**Utilisation confirmée** : les deux règles l'utilisent (poids 0.10 MeanReverting / 0.20 StableRange, jamais inversé). **Discriminante, mais faiblement** (-0.33, contre -0.81 pour Persistence) — son effet sur chaque score individuel (-0.46/-0.36) est PLUS fort que son effet sur leur différence, signe qu'elle déplace les deux scores dans le MÊME sens à des degrés proches, s'annulant partiellement dans l'écart. **Indépendante des dimensions partagées ?** Non — `Corr(StructuralStability, MeanReversion) = -0.4815` (la plus forte corrélation inter-dimensions mesurée, §9) : le signe négatif inattendu des corrélations StructuralStability↔Score (une dimension pondérée POSITIVEMENT dans les deux règles corrèle NÉGATIVEMENT avec leurs scores) s'explique par cette confusion indirecte via MeanReversion, dont le poids (0.40 dans les deux règles) domine largement celui de StructuralStability (0.10-0.20).

---

# 17. ABLATION ANALYSIS

**Disponible** — le framework permet cette analyse proprement : `MeanRevertingRule`/`StableRangeRule` sont des classes pures, `IDecisionRule.Evaluate(DecisionContext, DecisionResultBuilder)`, prenant un `FusionResult` en entrée. Une copie modifiée d'un `FusionResult` réel (une dimension "clampée" à la moyenne du dataset, calculée sur les 10 930 barres réelles) peut être construite en code de test SANS toucher à aucun fichier de production, puis passée aux VRAIES classes de règles. Aucun hack, aucune réflexion, aucune modification de signature.

| Scénario | Corr(MR,SR) | mean\|MR-SR\| | stdDev(MR) | stdDev(SR) |
|---|---:|---:|---:|---:|
| Baseline (aucune ablation) | 0.9653 | 0.0575 | 0.0822 | 0.0859 |
| Ablation Persistence (clampée à la moyenne) | **0.9913** ↑ | 0.0558 | 0.0784 | 0.0859 |
| Ablation dimensions PARTAGÉES (Stationarity+MeanReversion clampées) | **0.1033** ↓↓↓ | 0.0573 | 0.0169 | 0.0089 |
| Ablation StructuralStability (clampée à la moyenne) | 0.9707 | 0.0572 | 0.0841 | 0.0889 |

**Interprétation, sans ambiguïté** :
- Retirer `Persistence` (le discriminant) AUGMENTE la corrélation au-delà de la ligne de base (0.9653→0.9913) — la variation naturelle de `Persistence` est la SEULE source de désaccord mesurable entre les deux règles parmi les 3 dimensions testées ; sans elle, elles deviennent quasiment indiscernables.
- Retirer `Stationarity`+`MeanReversion` (les dimensions partagées) FAIT S'EFFONDRER la corrélation à 0.1033 — preuve directe et quantitative que ces deux dimensions sont la cause structurelle du chevauchement, pas une coïncidence de pondérations proches. Les écarts-types de chaque score chutent aussi fortement (MR : 0.0822→0.0169, -79% ; SR : 0.0859→00089, -90%) — ces deux dimensions expliquent la quasi-totalité de la variance de CHAQUE règle individuellement, pas seulement de leur covariance.
- Retirer `StructuralStability` a un effet marginal (0.9653→0.9707) — cohérent avec son rôle de discriminant faible (§16).

**C'est la preuve la plus directe de ce lot** : elle isole expérimentalement la contribution de chaque dimension à la corrélation observée, en utilisant exclusivement les vraies classes de production, sans jamais modifier un poids ni une formule.

---

# 18. SYNTHETIC CASES

Fichier : `Tests/Decision/StableRangeMeanRevertingSyntheticCasesTests.cs` — 6 cas (A-F), tous PASS, exécutés à travers les 5 VRAIES `IDecisionRule` via `DecisionEngine`/`DecisionArbitrator` réels :

| Cas | Configuration | Résultat observé |
|---|---|---|
| A | Stationarity=0.85, MeanReversion=0.85, Persistence=0.50 (neutre) | \|MR-SR\|<0.06, AmbiguityScore>0.90 — proches |
| B | Idem A mais Persistence=0.95 (divergente) | Séparation significativement plus grande qu'en A ; StableRange dépasse MeanReverting |
| C | Stationarity=0.30, MeanReversion=0.30 (faibles), Persistence=0.90 | StableRange > MeanReverting malgré une évidence partagée faible — le mécanisme de B tient même hors du régime "évident" |
| D | "StableRange évident" : Stat=0.85, Pers=0.90, MR=0.80, SS=0.75 | Winner=StableRange, marge >0.08 sur le concurrent le plus proche |
| E | "MeanReverting évident" : Stat=0.55, Pers=0.02, MR=0.90, SS=0.60 | Winner=MeanReverting, marge positive confirmée |
| F | Tout proche du neutre (Stat=0.58, Pers=0.55, MR=0.60, SS=0.65) | AmbiguityScore>0.85 ; le couple Winner/RunnerUp EST exactement {MeanReverting, StableRange} |

Le Cas C confirme un point que le brief demandait explicitement de ne pas présumer : le mécanisme de séparation par `Persistence` (Cas B) **survit** même quand l'évidence partagée est faible — ce n'est pas un artefact valable seulement dans les scénarios "décisifs".

---

# 19. SEMANTIC ANALYSIS

| | MeanReverting | StableRange |
|---|---|---|
| **Phénomène de marché prétendu** | Le prix s'écarte de son équilibre puis y REVIENT — une dynamique, un mouvement de correction | Le prix RESTE proche d'un équilibre — un état, une absence de mouvement significatif |
| **Distinction conceptuelle attendue** | Processus actif de retour (implique une trajectoire, donc une notion de PERSISTANCE du mouvement de retour) | Processus passif de stabilité (implique une absence de tendance, mais pas nécessairement un mouvement de retour actif) |
| **Inputs réellement utilisés** | MeanReversion(0.40), Stationarity(0.30), Persistence-inversée(0.20), StructuralStability(0.10) | Stationarity(0.40), MeanReversion(0.40), StructuralStability(0.20) |

**La distinction conceptuelle EXISTE en théorie** (un marché qui "revient" activement vers l'équilibre n'est pas identique à un marché qui "reste" simplement proche de l'équilibre sans mouvement notable) — la dimension censée capturer cette distinction est `Persistence` (la vitesse à laquelle un écart se corrige, DFA/Variance Ratio), utilisée UNIQUEMENT par `MeanRevertingRule`. **Le problème n'est pas conceptuel — il est de PONDÉRATION** : `Persistence` ne pèse que 0.20 sur 1.0 chez `MeanRevertingRule`, contre 0.70-0.80 partagé avec `StableRangeRule`. La distinction sémantique voulue existe dans le CODE mais est structurellement minoritaire dans le SCORE final.

**Réponse à la question centrale (§15 du brief)** : les deux règles ONT une distinction économique/statistique voulue et implémentée (via Persistence), mais celle-ci est actuellement **sous-pondérée au point d'être largement dominée par leur socle commun** — un entre-deux, ni "clairement distinctes" ni "vraiment le même phénomène", cohérent avec l'Hypothèse D (§23).

---

# 20. MARKET REGIME INTERPRETATION

Sans inventer de nouvelle théorie (brief §16, discipline respectée) :

```
Concept (MeanReversion)      → Observable (HalfLife, Engine/Regime/Evidence/HalfLife)      → FusionRule (MeanReversionRule, Engine/Fusion/Rules) → Dimension FusionDimension.MeanReversion → Consommée par MeanRevertingRule ET StableRangeRule
Concept (Stationarity)       → Observable (ADF+KPSS, Engine/Regime/Evidence/ADF,KPSS)        → FusionRule (StationarityRule)                        → Dimension FusionDimension.Stationarity   → Consommée par MeanRevertingRule ET StableRangeRule
Concept (Persistence/Trend)  → Observable (DFA Hurst + Variance Ratio, Engine/Regime/Evidence/DFA,VarianceRatio) → FusionRule (PersistenceRule) → Dimension FusionDimension.Persistence → Consommée par MeanRevertingRule SEULEMENT
Concept (Structural Change)  → Observable (FusionProfileAnalysis, historique de snapshots)  → StructuralStabilityRule (mécanisme séparé, Lot 14.14 §4) → Dimension FusionDimension.StructuralStability → Consommée par les deux, poids asymétrique
```

Ce schéma montre que le pipeline scientifique DISPOSE déjà de 4 observables distincts et documentés (ADF/KPSS, DFA/VR, HalfLife, profil temporel Fusion) — la redondance de score n'est PAS due à un manque d'observables scientifiques différenciés en amont, mais au choix de PONDÉRATION dans les 2 règles Decision qui les consomment (cohérent avec §7/§9 : les dimensions elles-mêmes sont faiblement corrélées entre elles).

---

# 21. FUSION REDUNDANCY

**Oui, un double comptage existe** (brief §17, documenté, non corrigé) : chaque fois que `Stationarity` et `MeanReversion` sont conjointement élevées, DEUX candidats parmi les 5 (`StableRangeRule` ET `MeanRevertingRule`) reçoivent un score élevé EN MÊME TEMPS, pour des raisons largement identiques (0.70-0.80 de recoupement pondéral, §5). Ce n'est pas un double comptage au sens d'une somme (`DecisionArbitrator` prend un maximum, pas une somme), mais un double comptage au sens de la STRUCTURE DE LA COMPÉTITION : l'évidence `Stationarity+MeanReversion` "vote" deux fois parmi les 5 hypothèses arbitrées, gonflant artificiellement le nombre de candidats forts sans ajouter d'information réellement nouvelle. **Risque documenté, non corrigé** (brief §17, "ne pas corriger").

---

# 22. INFORMATION LOSS

Quantification (brief §18), via l'ablation §17 et les statistiques du Lot 14.14 :

- Les dimensions partagées (`Stationarity`+`MeanReversion`) expliquent **~79-90% de la variance individuelle** de chaque score de règle (stdDev réduit de 0.0822→0.0169 pour MeanReverting, de 0.0859→0.0089 pour StableRange lors de leur ablation, §17).
- Conséquence directe sur `AmbiguityScore` (rappel Lot 14.14 §9) : pour le régime MeanReverting (celui qui compte pour le trading), `AmbiguityScore` moyen = 0.9571, `stdDev` = 0.0180 — une bande très étroite, jamais proche de 0, rarement proche de 1.0 strict.
- **Effet quantifié** : sur une plage théorique `[0,1]`, `AmbiguityScore` pour MeanReverting-won reste confiné à `[0.9308, 0.9934]` (P05-P95, Lot 14.14 §17) — soit une plage utile de seulement **0.063** sur les 1.0 possibles, à cause du chevauchement structurel StableRange/MeanReverting. C'est la mesure directe de la "perte d'information" : le mécanisme d'arbitrage POURRAIT en principe rapporter une gamme complète de confiance [0,1], mais la redondance de poids le confine à une bande de 6.3 points de pourcentage.

---

# 23. ALTERNATIVE HYPOTHESES

| Hypothèse | Évaluation | Preuve |
|---|---|---|
| A — Réellement distinctes | **Partiellement seulement** | Persistence est un vrai discriminant (§8/§15/§17) mais insuffisamment pondéré |
| B — StableRange sous-cas de MeanReverting | **Rejetée** | StableRange pondère StructuralStability à 0.20 (2× MeanReverting) et n'a AUCUN terme de Persistence — ce n'est pas un sous-ensemble de MeanReverting, c'est une formule différente sur un sous-ensemble des mêmes dimensions |
| C — MeanReverting sous-cas de StableRange | **Rejetée** | MeanReverting a un terme (Persistence) que StableRange n'a jamais — MeanReverting n'est pas réductible à StableRange |
| **D — Partiellement distinctes, discriminant nécessaire** | **RETENUE** | Le discriminant (Persistence) EXISTE déjà dans la formule actuelle (§8), fonctionne (§17 ablation, §15 corrélation -0.81), mais son poids (0.20) et sa faible variabilité naturelle sur ce dataset (Lot 14.14 : P25-P75 étroit) limitent son effet pratique |
| E — Dataset insuffisant pour conclure | **Rejetée comme conclusion principale, mais facteur reconnu** | La reproduction quasi exacte (0.9652→0.9653, §10) et la stabilité temporelle majoritaire (§14, 77.5% des jours >0.95) montrent que le résultat n'est pas un artefact d'un seul run ; MAIS la faible variabilité de Persistence pourrait être spécifique à cette fenêtre de 59 jours (non exclu, catégorie E du Lot 14.14, toujours valable) |

**Conclusion** : Hypothèse D, avec un facteur E résiduel non exclu (portée limitée à ce dataset pour l'estimation de la variabilité de Persistence elle-même).

---

# 24. DEAD CONFIGURATION ANALYSIS

Repris et approfondi du Lot 14.14 (§14 de ce rapport-ci) :

| Configuration | Pourquoi morte | Où elle devrait théoriquement se brancher | Dépendances | Modification architecturale requise ? |
|---|---|---|---|---|
| `FusionConfiguration.Weights` | Aucun constructeur de `IFusionRule` ne l'accepte — chaque règle Fusion a ses propres constantes privées, jamais paramétrées | Chaque `IFusionRule.Evaluate` devrait lire ses poids depuis un objet injecté plutôt que des `const` | Casserait la garantie actuelle "chaque règle est une fonction pure sans état externe" (documentée dans plusieurs docstrings) | **Oui** — passer un objet de config à travers `EvidenceFusionEngine.Fuse` jusqu'à chaque règle |
| Poids `Decision` (15 constantes, 5 règles) | Aucun type `DecisionConfiguration` n'existe — même pas un type mort comme `FusionConfiguration` | `DecisionEngine`/`IDecisionRule` devraient recevoir un objet de poids équivalent | Idem, plus l'absence totale de type à réutiliser (contrairement à Fusion) | **Oui, et plus lourde** — il faudrait D'ABORD créer le type, ensuite le brancher |
| `StructuralStabilityRule` | Wired mais via un mécanisme séparé (`FusionStateManager`), pas `IFusionRule` | Pourrait implémenter `IFusionRule` si son besoin d'historique (via `FusionProfileAnalyzer`) était satisfait autrement | Dépend d'un état temporel (6 snapshots) que le pattern `IFusionRule.Evaluate(FusionContext, FusionResultBuilder)` actuel (sans état) ne permet pas nativement | **Oui** — changerait la signature d'`IFusionRule` ou introduirait une interface parallèle |

**Aucun de ces branchements n'est effectué dans ce lot** (brief §21, "NE PAS les brancher").

---

# 25. ROOT CAUSE

**Cause structurelle** : recoupement de poids `Stationarity`+`MeanReversion` (0.70-0.80 du poids scientifique des deux règles), confirmé causalement par ablation (§17) — pas seulement corrélationnellement.

**Discriminant existant mais sous-exploité** : `Persistence`, poids 0.20 chez `MeanRevertingRule` uniquement, statistiquement le facteur le plus fortement lié à l'écart entre les deux scores (-0.81) mais peu variable sur ce dataset.

**Facteur secondaire** : `StructuralStability`, discriminant faible, confondu indirectement avec `MeanReversion` (corrélation dimension-à-dimension -0.4815, la plus forte mesurée).

**Ce n'est PAS** : un problème de formule d'arbitrage (Lot 14.14, confirmé), ni une redondance entre les dimensions Fusion elles-mêmes (§9, toutes faiblement corrélées entre elles), ni un problème d'implémentation (les deux règles fonctionnent exactement comme documentées).

---

# 26. RECOMMENDED FUTURE LOT

**Lot proposé (investigation, pas calibration)** : étudier la distribution DE `Persistence` elle-même sur un dataset étendu (si/quand une source de données au-delà des 59 jours Yahoo devient disponible) — déterminer si sa faible variabilité observée ici (Lot 14.14 §10, P25-P75 étroit) est une propriété structurelle du marché MES M5 ou un artefact de cette fenêtre précise. Sans cette réponse, toute future recalibration de poids resterait aveugle à la question posée par ce lot.

**Alternative, à plus faible risque** : documenter formellement (sans modifier) une proposition de re-pondération hypothétique (ex. augmenter le poids de Persistence chez MeanRevertingRule, ou l'introduire chez StableRangeRule avec un signe opposé) comme matériau de départ pour un FUTUR lot de calibration explicitement autorisé — ce lot-ci s'arrête à la documentation, conformément à la règle absolue.

**Ne pas encore lancer** : toute modification des poids Decision/Fusion tant que la variabilité réelle de `Persistence` sur un historique plus long n'est pas mieux comprise (risque de calibrer un discriminant sur un artefact d'échantillonnage).

---

# 27. LIMITATIONS

- Un seul dataset frais (MES=F/M5/59 jours, ce run) — catégorie E (Lot 14.14) toujours non exclue pour la variabilité de `Persistence` spécifiquement.
- L'analyse temporelle (§14) identifie des journées de divergence mais n'en explique pas la cause (pas de croisement avec un calendrier de marché).
- `StructuralBreak` comme second RunnerUp fréquent (§12, 40.4%) n'a pas été audité en détail dans ce lot (hors scope explicite : StableRange/MeanReverting uniquement).
- L'ablation (§17) clampe une dimension à sa MOYENNE du dataset, pas à zéro ni à une autre valeur de référence — un choix méthodologique raisonnable (préserve un niveau réaliste) mais pas le seul possible.
- Aucune p-value/test de significativité statistique formel (cohérent avec la discipline des lots précédents).

---

# 28. HANDOFF CONTEXT — NEXT SESSION

```
PROJECT:
IQIA

CURRENT LOT:
14.15

LAST COMPLETED:
14.14

PRIMARY ISSUE:
StableRange / MeanReverting structural overlap - INVESTIGATED, root cause isolated experimentally.

KNOWN CORRELATION:
0.9652 (Lot 14.14) / 0.9653 Pearson, 0.9658 Spearman (Lot 14.15, fresh dataset - reproduced almost
exactly, fingerprint 9AE6C21079110DE123B82F89B92BBCCF463BDEF31122DCBBC618446B72030CE7, 11058 bars,
2026-06-26T01:40Z..2026-08-24T01:32:15Z)

DATASET:
MES=F
M5
~59 days
10930 bars analyzed after warmup

MAIN SHARED DIMENSIONS:
Stationarity (weight 0.30 MR / 0.40 SR)
MeanReversion (weight 0.40 MR / 0.40 SR)
Together: ablating both collapses Corr(MR,SR) from 0.9653 to 0.1033 (§17) - definitive causal proof.

POTENTIAL DISCRIMINANT:
Persistence (weight 0.20 MR / absent SR) - CONFIRMED on real data: Corr(Persistence, ScoreDifference)=
-0.8141; ablating it RAISES Corr(MR,SR) to 0.9913. Real but underweighted and, on this dataset, low-
variance most of the time (Lot 14.14: P25-P75=[0.133,0.148]).

AMBIGUITY FORMULA:
Clamp(1 - (Winner - RunnerUp), 0, 1) - unchanged, still confirmed correct (Lot 14.14).

AMBIGUITY BASELINE:
0.95

FUSION CONFIGURATION:
DEAD (confirmed again, §24 - no IFusionRule constructor accepts it)

DECISION WEIGHTS:
DEAD (confirmed again, §24 - no DecisionConfiguration type exists at all, more deeply unconfigurable
than Fusion)

ROOT CAUSE CURRENTLY:
Weighting (structural, causally proven via ablation) + Score Distribution (secondary, Lot 14.14).
Semantic issue independent (Lot 14.14 D14.14-6, still valid - AmbiguityScore name vs margin-to-nearest-
competitor meaning).

RESULT:
StableRange is MeanReverting's dominant RunnerUp (56.7% of MeanReverting wins), but StructuralBreak is a
non-negligible second-closest competitor (40.4%) - not audited in depth here, flagged for a future lot.
ScoreDifference(MR-SR) is NOT centered near zero - it is systematically positive (mean=0.0558, 69% of
bars in [0.05,0.10)) - MeanReverting has a small, stable, structural edge, not a coin-flip tie. Daily
rolling correlation ranges 0.8703-0.9956 (mean 0.9717 over 40 days) - mostly stable (77.5% of days >0.95)
but with real, unexplained divergence episodes on ~9 days.

HYPOTHESIS:
D - StableRange and MeanReverting are partially distinct (each has a term the other lacks: Persistence
for MeanReverting, higher StructuralStability weight for StableRange), but the existing discriminant
(Persistence) is underweighted (0.20) and low-variance on this specific 59-day window, so in PRACTICE the
two rules behave as near-duplicates most of the time. Neither B nor C (strict subset) holds - confirmed
by architecture audit (§5) and ablation (§17).

PRODUCTION MODIFIED:
NO

CALIBRATION:
NO

NEXT LOT:
Investigate Persistence's own variability on a longer/different dataset if one becomes available (Yahoo's
60-day wall still applies, Lot 14.12) - OR document (without applying) a hypothetical re-weighting
proposal as input for a future, explicitly-approved calibration lot.

WHY:
The ablation experiment (§17) is the strongest evidence this lot produced - it isolates causation, not
just correlation, entirely through real production classes fed modified copies of real data, without
touching any production file. Any future weight change should be informed by this specific finding
(shared dimensions explain ~79-90% of each rule's own variance) rather than re-deriving it from scratch.

DO NOT DO:
Do NOT modify Engine/Decision/Rules/StableRangeRule.cs or MeanRevertingRule.cs, any Fusion/Decision
weights, AmbiguityGateThreshold, RegimeEngine, FusionConfiguration, DecisionEngine, or SignalEngine - this
lot is investigation-only. Do NOT treat StructuralBreak's 40.4% RunnerUp share (§12) as equivalent to the
StableRange/MeanReverting finding - it was observed but not investigated with the same rigor (no
architecture audit, no ablation) in this lot. Do NOT assume Persistence's measured low variance (Lot
14.14) generalizes beyond this 59-day MES M5 window - explicitly flagged as an open question (§26).
```

**STOP.**
