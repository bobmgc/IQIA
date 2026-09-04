# QDE-015 — Validation isolée du signal Trending H1 en walk-forward

**Date :** 2026-09-01
**Mode :** lecture seule. **Production modifiée : NON.**
**Sonde :** `IQIAIndicator/Tests/Research/NewDataSourceFeasibility/TrendingWalkForwardValidationTests.cs` (read-only ; aucun paramètre de `TimeSeriesMomentumModel` touché ; `YahooSymbolMap` intouché ; `HttpYahooChartClient.FetchChartJson` appelé en direct comme QDE-012/013/014). Sortie : `Output/trending_walkforward.txt`.
**Réutilise :** le pipeline Trending H1 de QDE-012 (`RunFullBacktest` sur MES=F 1h natif, warmup 128, horizon 10, R:R fixe 2,0, coût round-trip base ~3,855 $ ≈ 0,77 pt appliqué en post-traitement).

---

## 1. Contexte et objet

QDE-012 avait laissé le signal Trending H1 dans un état incertain : split unique train négatif (−0,10 R) / OOS positif (+0,42 R net, IC excluant 0), échantillon petit (~209 trades) et groupé (23 jours de signal), modèle jamais calibré. QDE-014 a montré que conditionner un signal sur l'état cross-asset sans valider sa robustesse de base est un dead-end méthodologique. **Ce lot ne conditionne rien : il détermine si l'OOS-positif de Trending est un signal stable ou l'artefact d'une seule période favorable.**

Méthode : un seul run du pipeline complet sur toute la fenêtre H1 (warmup / buffer de régime ne redémarrent jamais en cours d'historique, paramètres identiques par construction sur tous les segments), puis partition des trades Trending résultants par timestamp de signal en segments contigus non chevauchants.

---

## 2. Phase 0 — Vérification anti-data-snooping (bloquante)

`git log --follow` + `git blame` sur `IQIAIndicator/Engine/ScientificModels/Trend/TimeSeriesMomentumModel.cs` :

- Le vrai modèle a été introduit dans **un unique commit, `3ca0418` (2026-08-30)**, « feat(trending): real TimeSeriesMomentumModel + calibration (P0-2) ». Avant : stub vide (`51b67f1`, 2026-08-08).
- Les quatre constantes datent toutes de ce commit, chacune commentée `NON CALIBRATED` :
  `DefaultLookbacks = {12, 36, 72, 144}` (ligne 47), `TStatScale = 2.0` (72), `AutocorrelationWindow = 100` (75), `VolatilityWindow = 60` (79).
- La tentative de calibration P0-2 (`Tests/Research/TrendingCalibration/`, documentée QDE-012 §P0-2) a tourné sur **6 marchés M5, ~59 jours**, grille de 81 cellules, split cross-market {NQ,RTY,GC}/{ES,YM,CL} + split temporel purgé 70/30. **Résultat : les 81 cellules ont une espérance-R médiane cross-market négative → aucune calibration livrée, défauts inchangés.**

**Conclusion Phase 0 : les paramètres sont fixés a priori et n'ont JAMAIS été ajustés sur une fenêtre H1** (la seule tentative de calibration était sur M5 et n'a rien produit). Pas de data-snooping sur la fenêtre MES H1 720 jours testée ici.

**Caveat documenté (intention, pas snooping) :** les lookbacks {12,36,72,144} sont documentés comme « 1h/3h/6h/12h **sur un graphique M5** ». Exécutés sur des barres **H1**, ils valent 12h/36h/72h/144h (0,5 à 6 jours) — des horizons choisis pour un autre timeframe, réutilisés tels quels (l'interdiction du lot m'empêche de les changer). Cela ne crée pas de biais de sélection (jamais fittés), mais le signal Trending H1 est testé avec des horizons non pensés pour H1 — même nature de mismatch que les fenêtres de régime signalées en QDE-012 §Phase 0.

---

## 3. Phase 1 — Découpage walk-forward

**Total : 229 trades Trending `Closed`, 2024-09-12 → 2026-04-20. 27 jours de signal distincts** sur 720 jours calendaires (régime rare et groupé). R:R = 2,00 partout.

### 3.1 — 6 segments contigus calendaires-égaux

| seg | plage | n | win rate | grossR | **netR (IC 95 %)** | verdict |
|---|---|---|---|---|---|---|
| S1 | 2024-09-11 → 2025-01-09 | 42 | 0,643 | +0,323 | **+0,293 ± 0,286** | robustement positif |
| S2 | 2025-01-09 → 2025-05-09 | 50 | 0,440 | +0,026 | −0,014 ± 0,312 | non concluant |
| S3 | 2025-05-09 → 2025-09-06 | 34 | 0,206 | −0,553 | **−0,600 ± 0,224** | **robustement négatif** |
| S4 | 2025-09-06 → 2026-01-04 | 54 | 0,741 | +0,986 | **+0,951 ± 0,324** | robustement positif |
| S5 | 2026-01-04 → 2026-05-04 | 49 | 0,408 | −0,093 | −0,134 ± 0,285 | non concluant |
| S6 | 2026-05-04 → 2026-09-01 | 0 | — | — | — | aucun trade (régime jamais déclenché) |

### 3.2 — 5 segments

| seg | plage | n | netR (IC) | verdict |
|---|---|---|---|---|
| S1 | 2024-09 → 2025-02 | 42 | +0,293 ± 0,286 | robustement positif |
| S2 | 2025-02 → 2025-06 | 84 | −0,251 ± 0,215 | robustement négatif |
| S3 | 2025-06 → 2025-11 | 42 | +0,932 ± 0,384 | robustement positif |
| S4 | 2025-11 → 2026-04 | 47 | −0,111 ± 0,318 | non concluant |
| S5 | 2026-04 → 2026-09 | 14 | +0,775 ± 0,434 | n<20 (faible) |

### 3.3 — 4 segments

| seg | plage | n | netR (IC) | verdict |
|---|---|---|---|---|
| S1 | 2024-09 → 2025-03 | 92 | +0,126 ± 0,215 | non concluant |
| S2 | 2025-03 → 2025-09 | 34 | −0,600 ± 0,224 | robustement négatif |
| S3 | 2025-09 → 2026-03 | 89 | +0,381 ± 0,269 | robustement positif |
| S4 | 2026-03 → 2026-09 | 14 | +0,775 ± 0,434 | n<20 (faible) |

### 3.4 — 6 segments trade-count-égaux (n ≈ 38, expose le groupement temporel)

| seg | plage | n | win rate | netR (IC) | verdict |
|---|---|---|---|---|---|
| Q1 | 2024-09-12 → 2024-12-27 | 38 | 0,711 | +0,413 ± 0,289 | robustement positif |
| Q2 | 2024-12-27 → 2025-02-24 | 38 | 0,263 | −0,585 ± 0,176 | robustement négatif |
| Q3 | 2025-02-24 → 2025-06-04 | 38 | 0,395 | +0,096 ± 0,402 | non concluant |
| Q4 | 2025-06-04 → 2025-11-14 | 38 | 0,605 | +0,426 ± 0,397 | robustement positif |
| Q5 | 2025-11-14 → 2026-01-13 | 38 | 0,632 | +0,699 ± 0,391 | robustement positif |
| Q6 | 2026-01-13 → 2026-04-20 | 39 | 0,436 | −0,100 ± 0,347 | non concluant |

Note : Q4 couvre **5 mois** et Q5 **2 mois** pour le même nombre de trades — la densité de signaux Trending varie d'un facteur ~2,5 selon la période.

---

## 4. Phase 2 — Cohérence inter-segments

**Le signe de l'expectancy n'est pas stable.** Il oscille : positif → plat → **robustement négatif** → robustement positif → plat.

| découpage | segments exploitables (n ≥ 20) | robustement positifs | robustement négatifs | non concluants |
|---|---|---|---|---|
| k = 6 | 5 | 2 (40 %) | 1 (20 %) | 2 (40 %) |
| k = 5 | 4 | 2 | 1 | 1 |
| k = 4 | 3 | 1 | 1 | 1 |
| trade-count (6) | 6 | 3 | 1 | 2 |

**Jamais une majorité robustement positive.** Sous les quatre partitionnements, il existe systématiquement au moins un segment **robustement négatif** (S3 en k=6/k=4 : −0,60 R sur 34 trades ; S2 en k=5 : −0,251 ; Q2 : −0,585). Une stratégie live aurait subi un tronçon de 4 mois robustement perdant.

Le **win rate** passe de **0,21 (S3) à 0,74 (S4)** entre deux blocs de 4 mois adjacents (seuil d'équilibre pour un signal 2R = 0,333). La précision directionnelle du signal n'est pas stable — elle dépend du régime d'une façon que le modèle ne détecte pas.

---

## 5. Phase 3 — Diagnostic de concentration

### 5.1 — Book entier (229 trades)

- Somme netR = **35,98** ; mean netR = **+0,157 ± 0,149** (borne basse +0,008 → à peine > 0).
- **Top 10 % des trades (23) = 45,14 R = 125 % du total** → les 90 % restants perdent collectivement ~9 R.
- **Plus grosse fenêtre gagnante de 15 jours calendaires : 2025-11-13 → 2025-11-28, 31 trades, +34,56 R = 96 % du R net total du book sur 2 ans.**
- **Retrait de cette seule fenêtre :** mean netR passe de **+0,157 ± 0,149 à +0,007 ± 0,149** → à plat, indistinguable de zéro. **Le signe positif NE survit PAS.**

### 5.2 — Meilleur segment k=6 (S4, +0,951)

- Somme netR = 51,37 sur 54 trades.
- Sa plus grosse fenêtre de 15 jours est **le même épisode** 2025-11-13 → 11-28 : **31 des 54 trades de S4** (plus de la moitié), +34,56 R.
- Retrait : S4 → +0,731 mais sur **n = 23, IC ± 0,510** → non significatif.

**Tout le résultat positif du signal Trending H1 sur 2 ans se réduit à un unique épisode de ~15 jours (mi-novembre 2025).**

---

## 6. Phase 4 — Réconciliation avec le split unique de QDE-012

Le split 60/40 de QDE-012 tombe à la barre 5529 (purge 20) ≈ **2025-08-27**.

| bloc | plage calendaire | n | netR (IC 95 %) |
|---|---|---|---|
| TRAIN | 2024-09-12 → 2025-06-04 | 126 | **−0,070 ± 0,177** |
| OOS | 2025-10-13 → 2026-04-20 | 103 | **+0,435 ± 0,240** |

Rapproché du découpage k=6 :
- **TRAIN** englobe S1 (robustement positif) + S2 (plat) + l'essentiel de S3 (**robustement négatif**, jusqu'à ~sept 2025). Somme : légèrement négative.
- **OOS** englobe **exactement S4** (le segment +0,951, qui contient l'épisode de novembre 2025) + S5 (plat).

**Le split unique de QDE-012 « est tombé par chance » à la frontière de régime S3/S4.** La coupure + la purge de ~6 semaines ont atterri pile sur le passage du bloc robustement négatif (S3, fin ~sept 2025) au bloc robustement positif (S4). TRAIN a avalé le mauvais tronçon et l'a dilué avec S1 ; OOS a hérité de tout le bon tronçon S4 + l'épisode de novembre. Avec seulement ~5 blocs de régime de signe alterné en 2 ans, une coupure arbitraire unique a une probabilité réelle de placer l'OOS sur un bloc favorable. Le walk-forward — non fait en QDE-012 — montre que le +0,42 OOS **n'est pas une propriété forward stable** : c'est S4, et S4 est à 96 % porté par 15 jours.

Il n'y a pas de contradiction de fond entre les deux vues : le split unique n'était pas *faux*, il était *non informatif* sur la stabilité forward, parce qu'un seul point de coupure ne peut pas révéler l'alternance de régime que le walk-forward expose.

---

## 7. Établi avec confiance

Les deux conditions de cette section (majorité de segments robustement positifs **ET** survie au retrait du plus gros cluster gagnant) devaient toutes deux être remplies pour conclure à un signal robuste. **Les deux échouent :**

1. **Majorité de segments robustement positifs : NON.** Sous les quatre partitionnements, la fraction robustement positive est 2/5, 2/4, 1/3, 3/6 — jamais une majorité stricte, et il y a toujours au moins un segment robustement négatif.
2. **Survie au retrait du plus gros cluster : NON.** Le mean netR du book entier passe de +0,157 à **+0,007** après retrait d'une seule fenêtre de 15 jours (mi-novembre 2025) qui porte **96 %** du R net total accumulé sur 720 jours.

**Classification : NON-ROBUSTE / ARTEFACT DE RÉGIME PROBABLE.** Le signal Trending H1 n'a **pas d'edge forward stable**. L'OOS-positif observé en QDE-012 était un artefact de l'emplacement de la coupure unique par rapport à la frontière de régime S3/S4.

Faits additionnels convergents :
- 27 jours de signal distincts en 720 jours → les 229 trades sont ~8,5/jour actif, groupés en une poignée d'épisodes.
- Un tronçon de 4 mois (S3) robustement négatif (−0,60 R).
- Win rate instable en régime (0,21 à 0,74 entre blocs adjacents).
- Segment S6 (2026-05 → 2026-09) : zéro trade — le régime Trending n'a plus été détecté sur les 4 derniers mois de données.

---

## 8. Exploratoire / non concluant

Aucun élément de ce lot n'atteint le seuil « exploratoire prometteur ». Le seul fait à noter pour mémoire :

- Les épisodes gagnants (S1 fin 2024, S4 nov-2025) correspondent à des phases de tendance marquée de l'indice ; les épisodes perdants (S3 mi-2025, Q2 début 2025) à des phases de range/retournement. Le modèle capte la tendance *quand elle est là* mais n'a aucun mécanisme pour éviter de trader quand elle ne l'est pas — d'où l'alternance. Ceci n'est pas exploitable en l'état (il faudrait un détecteur de « tendance en cours » fiable, qui est précisément ce que la couche Régime/Fusion est censée fournir et ne fournit pas de façon calibrée — cf. QDE-012 §P0-2b).

---

## 9. Recommandation

**Ne rien construire sur le signal Trending H1** — ni conditionnement cross-asset (QDE-014 aurait été le même dead-end), ni calibration de `TimeSeriesMomentumModel` sur cette fenêtre. Le signal n'a pas d'edge forward stable ; son unique résultat positif se réduit à un épisode de 2 semaines.

**Cela clôt négativement le dernier fil ouvert MeanReverting/Trending de QDE-012.** L'ensemble des signaux directionnels du système actuel, fondés sur le seul historique de prix, est maintenant établi comme sans edge net de coûts (MeanReverting : 7 vérifications convergentes ; Trending : artefact de régime).

Suite possible, par valeur décroissante :
1. **Nouvelle source de données** (axe order flow de QDE-013, coût moyen-élevé) — la seule piste non encore éliminée qui apporte de l'information hors de la série de prix.
2. **Si Trending est un jour revisité** : lot de calibration dédié sur un historique multi-instruments **beaucoup plus long** (plusieurs années, plusieurs marchés) avec validation croisée explicitement consciente des épisodes de régime (blocked/purged CV, pas un split unique). Mais le résultat de concentration de ce lot (un épisode de 2 semaines = 96 % du R net sur 2 ans) est un prior fort que même cela ne trouverait rien de durable.

**Ne PAS :** conclure à un edge Trending à partir d'un seul segment positif ; ajuster un paramètre de `TimeSeriesMomentumModel` ou le multiple R ; introduire une variable de conditionnement ; modifier la production.

---

## 10. Interdictions respectées

- **Aucune calibration ni ajustement de paramètre de `TimeSeriesMomentumModel`.** Les 4 constantes n'ont pas été touchées ; leur provenance a seulement été vérifiée (Phase 0).
- **Aucune modification du multiple R ni du calcul de TP.**
- **Aucune variable de conditionnement cross-asset** introduite (c'est précisément ce que le lot évite tant que la base n'est pas validée).
- **Aucune modification de production.** Un fichier ajouté, `Tests/Research/NewDataSourceFeasibility/TrendingWalkForwardValidationTests.cs`, read-only, réutilisant `HttpYahooChartClient` / `YahooChartParser` / `BacktestEngine` / `PositionCostCalculator` sans les modifier. `YahooSymbolMap` intouché.

---

## 11. Index des fichiers produits

| Fichier | Rôle | Committable en production ? |
|---|---|---|
| `IQIAIndicator/Documentation/Scientific/QDE-015_Trending_WalkForward_Validation.md` | Ce rapport | Oui (documentation) |
| `IQIAIndicator/Tests/Research/NewDataSourceFeasibility/TrendingWalkForwardValidationTests.cs` | Sonde walk-forward, read-only | Recherche uniquement |
| `IQIAIndicator/Tests/Research/NewDataSourceFeasibility/Output/trending_walkforward.txt` | Sortie mesurée (preuve des chiffres §3–§6) | Recherche uniquement |

Suppression éventuelle : `git clean -fd IQIAIndicator/Tests/Research/NewDataSourceFeasibility/`.

---

## 12. FINAL OUTPUT

**STATUS :** Walk-forward terminé. Signal Trending H1 classé **NON-ROBUSTE / ARTEFACT DE RÉGIME** sans ambiguïté. Aucune majorité de segments robustement positive (2/5, 1/3 selon le découpage ; toujours ≥ 1 segment robustement négatif). Le résultat positif du book entier (+0,157 R) s'effondre à +0,007 R après retrait d'une unique fenêtre de 15 jours (mi-nov 2025) portant 96 % du R net sur 2 ans. L'OOS-positif de QDE-012 était un artefact de l'emplacement de la coupure unique à la frontière de régime S3/S4.

**PRODUCTION MODIFIED :** NO. 1 sonde de recherche ajoutée. Aucun paramètre de `TimeSeriesMomentumModel` touché. `YahooSymbolMap` intouché.

**TESTS :** `TrendingWalkForwardValidationTests` — Passed (229 trades Trending, 4 partitionnements, diagnostic de concentration, réconciliation split QDE-012). Skipped si Yahoo indisponible.

**DOCUMENTATION :** Ce rapport — `IQIAIndicator/Documentation/Scientific/QDE-015_Trending_WalkForward_Validation.md`.

**NEXT ACTION :** Dernier signal directionnel prix-seul du système écarté. Décision stratégique : (1) engager l'axe order flow de QDE-013 (nouvelle source de données, coût moyen-élevé, seule piste non éliminée), ou (2) mettre l'axe recherche-d'edge en pause. Ne PAS relancer de lot MeanReverting/Trending fondé sur l'historique de prix.
