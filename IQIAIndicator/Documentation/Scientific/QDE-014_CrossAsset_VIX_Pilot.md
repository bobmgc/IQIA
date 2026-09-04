# QDE-014 — Pilote cross-asset : le régime de volatilité implicite (VIX) conditionne-t-il le rendement forward de MES ?

**Date :** 2026-09-01
**Mode :** lecture seule. **Production modifiée : NON.**
**Sonde :** `IQIAIndicator/Tests/Research/NewDataSourceFeasibility/VixRegimeConditioningPilotTests.cs` (read-only, contourne `YahooSymbolMap` via `HttpYahooChartClient.FetchChartJson` direct, comme QDE-012/013). Sortie : `Output/vix_regime_pilot.txt`.
**Réutilise :** le pipeline MeanReverting H1 validé en QDE-012 (`RunFullBacktest` sur MES=F 1h natif, warmup 128, horizon 10, coût round-trip base ~3,855 $ ≈ 0,77 pt, appliqué en post-traitement).

---

## 1. Contexte et hypothèses (pré-enregistrées)

QDE-012 a établi que le book MeanReverting n'a pas d'edge net de coûts, à aucune cadence (6 vérifications convergentes), la cause de fond étant l'absence de dépendance sérielle exploitable dans le prix seul. QDE-013 a confirmé un accès gratuit à ~700 jours d'historique horaire pour VIX/VIX9D/VIX3M/TNX/ZN=F/DX-Y.NYB/NQ=F. Ce lot teste la première piste candidate : **une variable d'état hors-prix conditionne-t-elle l'expectancy forward de MES ?**

- **Hypothèse primaire (seule habilitée à conclure « edge confirmé ») :** le régime de VIX (niveau et/ou variation récente) conditionne l'expectancy forward de MES sur H1 — typiquement : réversion plus profitable en VIX bas/stable, dégradation ou inversion en VIX haut/en hausse rapide.
- **Hypothèses secondaires (exploratoires, jamais concluantes seules) :** pente `^VIX3M/^VIX` ; variation `^TNX` / `ZN=F` ; variation `DX-Y.NYB` ; écart de rendement 24 h normalisé MES↔`NQ=F`.

---

## 2. Méthodologie

- **Données :** MES=F 1h + 7 séries cross-asset 1h, fenêtre 720 jours (2024-09-11 → 2026-09-01), via `HttpYahooChartClient.FetchChartJson` + `YahooChartParser.Parse` (inchangés).
- **Trades :** les 2 549 trades MeanReverting `Closed` produits par `RunFullBacktest` sur MES=F 1h (mêmes signaux, même exécution, même coût que QDE-012).
- **Métrique :** expectancy nette de coût par trade en **R** = `(GrossPnL − 3,855) / (stopPts × 5)`.
- **Segmentation :** terciles du niveau de VIX au signal ; signe de la variation VIX 24 h ; grille 2×3 ; terciles pour chaque variable secondaire.
- **IC :** 95 % (± 1,96 · écart-type / √n).
- **Split :** train 60 % / OOS 40 % par index de barre, purge 20 barres — **identique à QDE-012**. Rapporté seulement si train ≥ 30 et OOS ≥ 30.

---

## 3. Phase 0 — Qualité des données (bloquante)

**MES=F 1h : 11 331 barres, 2024-09-11 → 2026-09-01.**

| Type de gap | count | slots manquants | interprétation |
|---|---|---|---|
| Week-end | 91 | 4 465 | fermeture CME vendredi ~21:00 UTC → dimanche ~22:00 UTC |
| Pause quotidienne CME | 387 | 387 | 21:00–22:00 UTC (16:00–17:00 CT), exactement 1/jour de bourse |
| Jours fériés / autres | 31 | 1 099 | échantillons vérifiés : 2024-11-28 Thanksgiving, 2024-12-24/25 Noël, 2024-12-31/01-01 Nouvel An, 2025-01-09 (deuil national Carter), 2025-01-20 MLK, 2025-02-17 Presidents Day, 2025-04-18 Good Friday, 2025-05-26 Memorial Day, 2025-06-19 Juneteenth, 2025-07-03/04 Independence Day |
| **En session active** | **0** | **0** | — |

**Aucun gap ne tombe en session active.** Tous coïncident avec un week-end, la pause quotidienne CME (21:00–22:00 UTC) ou un jour férié connu. Un seul échantillon mérite une note (2025-07-11, vendredi 20:00 UTC + 56 h) : gap légèrement plus long qu'un week-end standard, probablement un trou côté Yahoo à la réouverture dominicale — mais il commence un vendredi soir marché fermé, donc aucun signal MES n'y est généré et le forward-fill causal atterrit sur la clôture du vendredi, ce qui reste correct.

**Traitement retenu pour les gaps résiduels :** forward-fill causal strict. Pour un signal dont la barre clôture à l'instant `T`, la valeur cross-asset utilisée est celle de la **dernière barre cross-asset entièrement close à `T` ou avant** ; jamais une barre en cours de formation, jamais une barre future. Une valeur plus ancienne que 120 h est traitée comme absente (n'est jamais arrivé : 0 trade rejeté).

---

## 4. Phase 1 — Alignement causal

- **2 549 trades MeanReverting** exploités ; **0 rejeté** faute de valeur VIX causale.
- **Règle de jointure :** valeur cross-asset = dernière barre avec `(Timestamp + 1h) ≤ (barre de signal MES.Timestamp + 1h)`.
- **Point de code garantissant la causalité :** `VixRegimeConditioningPilotTests.Series.AsOf(DateTime instant)` — recherche dichotomique dans `_closeInstants` de la dernière entrée `≤ instant`, où `_closeInstants[i] = bars[i].Timestamp + 1h` (l'instant où la barre `i` est entièrement connue). Aucune barre future ne peut être sélectionnée. Même discipline que l'anti-look-ahead vérifié en QDE-012 (MTF equilibrium) et QDE-013.
- **Distribution du niveau de VIX au signal :** min 12,72 / p33 16,50 / médiane 17,71 / p67 19,07 / p90 23,75 / **max 31,15**.
  → **La fenêtre de 2 ans est un régime de VIX bas-à-modéré.** Aucun pic de volatilité / régime de crise (VIX > 35) n'est présent dans l'échantillon. Limitation majeure pour l'interprétation de l'hypothèse primaire.

---

## 5. Phase 2 — Test de l'hypothèse primaire (régime de VIX)

**Baseline non conditionnée (2 549 trades) :** netR **−0,074 ± 0,024** [net− robuste]. TRAIN −0,099 ± 0,032 / OOS −0,038 ± 0,036.

### 5.1 Par tercile de NIVEAU de VIX

| segment | n | win rate | grossR | **netR (IC 95 %)** | verdict | TRAIN / OOS (netR) |
|---|---|---|---|---|---|---|
| VIX bas (≤ p33 = 16,50) | 853 | 0,710 | −0,038 | **−0,097 ± 0,042** | net− robuste | −0,142 / −0,013 |
| VIX moyen (16,50–19,07) | 855 | 0,715 | −0,038 | **−0,083 ± 0,043** | net− robuste | −0,090 / −0,076 |
| VIX haut (> p67 = 19,07) | 841 | 0,743 | −0,013 | **−0,043 ± 0,040** | net− robuste | −0,062 / −0,011 |

**Les trois terciles sont robustement net-négatifs** (IC entièrement < 0). Il existe un gradient monotone : le VIX **haut** est le *moins* mauvais (−0,043) et le VIX **bas** le *plus* mauvais (−0,097) — c'est **l'inverse** de la direction pré-enregistrée (« réversion plus profitable en VIX bas/stable »).

### 5.2 Par signe de la VARIATION de VIX sur 24 h

| segment | n | win rate | grossR | **netR (IC 95 %)** | verdict | TRAIN / OOS (netR) |
|---|---|---|---|---|---|---|
| VIX en baisse/plat (chg ≤ 0) | 1 480 | 0,715 | −0,049 | **−0,094 ± 0,031** | net− robuste | −0,125 / −0,048 |
| VIX en hausse (chg > 0) | 1 069 | 0,733 | −0,002 | **−0,047 ± 0,038** | net− robuste | −0,063 / −0,024 |
| VIX en hausse rapide (chg > +1,0) | 242 | 0,769 | +0,026 | **−0,008 ± 0,079** | chevauche 0 | −0,029 / +0,018 |

Là encore, **inverse de l'hypothèse** : la réversion est *moins* mauvaise quand le VIX monte. Le segment « VIX en hausse rapide » atteint la **neutralité statistique** (netR −0,008, IC chevauche 0) — mais pas la positivité robuste, sur n=242 seulement, avec TRAIN (−0,029) et OOS (+0,018) qui n'ont même pas le même signe.

### 5.3 Grille 2×3 (signe variation × tercile niveau)

| cellule | n | grossR | **netR (IC 95 %)** | verdict | TRAIN / OOS |
|---|---|---|---|---|---|
| chg ≤ 0 & bas | 547 | −0,042 | −0,101 ± 0,052 | net− robuste | −0,150 / −0,012 |
| chg ≤ 0 & moyen | 471 | −0,062 | −0,106 ± 0,056 | net− robuste | −0,153 / −0,061 |
| chg ≤ 0 & haut | 462 | −0,046 | −0,075 ± 0,052 | net− robuste | −0,075 / −0,075 |
| chg > 0 & bas | 306 | −0,031 | −0,091 ± 0,071 | net− robuste | −0,127 / −0,014 |
| chg > 0 & moyen | 384 | −0,008 | −0,055 ± 0,067 | chevauche 0 | −0,019 / −0,095 |
| **chg > 0 & haut** | 379 | +0,026 | **−0,004 ± 0,062** | chevauche 0 | −0,044 / +0,055 |

La cellule la moins mauvaise (« VIX en hausse & niveau haut ») atteint la neutralité (netR −0,004) mais **pas** la positivité robuste, et TRAIN (−0,044) / OOS (+0,055) sont de signes opposés.

### 5.4 Verdict de l'hypothèse primaire

**L'hypothèse primaire pré-enregistrée n'est PAS confirmée. Elle est contredite en direction :**
- le VIX **bas** est le pire segment (netR −0,097), pas le meilleur ;
- la réversion est **moins** mauvaise quand le VIX monte, pas plus ;
- **aucun segment conditionné par le VIX** (niveau, variation, ou grille croisée) n'atteint une expectancy nette robustement positive ;
- le mieux obtenu est la **neutralité statistique** (« VIX en hausse rapide », « VIX en hausse & haut ») sur des sous-échantillons de 242–379 trades, sans accord TRAIN/OOS.

Le book MeanReverting reste robustement net-négatif globalement (−0,074 R) et dans **chaque** tercile de niveau de VIX. **Le résultat « pas d'edge » de QDE-012 s'étend aux sous-ensembles conditionnés par le VIX** — septième résultat négatif convergent pour MeanReverting.

---

## 6. Phase 3 — Hypothèses secondaires (EXPLORATOIRE — jamais concluant seul)

| variable (terciles) | tercile bas | tercile moyen | tercile haut | note |
|---|---|---|---|---|
| **Pente `VIX3M/VIX`** | netR −0,048 [net− rob.] (OOS +0,008) | −0,101 [net− rob.] | −0,075 [net− rob.] | non monotone, tout négatif |
| **Variation `TNX` 24 h** (rendement) | −0,023 [**chevauche 0**] (TRAIN −0,041 / OOS +0,010) | −0,120 [net− rob.] | −0,080 [net− rob.] | tercile « rendements en baisse » atteint la neutralité |
| **Variation `ZN=F` 24 h** (prix obligataire) | −0,096 [net− rob.] | −0,087 [net− rob.] | −0,038 [**chevauche 0**] (OOS +0,039) | tercile « obligations en hausse » = rendements en baisse — **cohérent avec TNX** |
| **Variation `DXY` 24 h %** | −0,076 [net− rob.] | −0,050 [net− rob.] | −0,097 [net− rob.] | non monotone, tout négatif |
| **Écart `MES − NQ` 24 h** (log-retour) | −0,118 [net− rob.] | −0,057 [net− rob.] | −0,048 [net− rob.] | gradient faible (force relative MES → moins mauvais), tout négatif |

**Seul signal exploratoire atteignant la neutralité :** « rendements en baisse » — vu deux fois de façon **cohérente** (`TNX` tercile bas ET `ZN=F` tercile haut, qui mesurent la même chose sur deux instruments). La réversion MeanReverting est *la moins mauvaise* quand les taux baissent (bid sur la duration / risk-on). Mais : chevauche 0, TRAIN négatif, non confirmé en OOS. **Génératrice d'hypothèse pour un lot dédié ultérieur, jamais un résultat validé ici.**

Aucune autre variable secondaire ne dépasse « robustement négatif » ou « chevauche 0 dans un seul tercile ».

---

## 7. Établi avec confiance (limité à l'hypothèse primaire)

1. Les gaps de la série MES=F 1h (720 j) coïncident **tous** avec des week-ends, la pause quotidienne CME (21:00–22:00 UTC) ou des jours fériés connus — **zéro gap en session active**. Le forward-fill causal strict est méthodologiquement sûr.
2. **L'hypothèse primaire est réfutée :** le régime de VIX (niveau et variation) **ne** conditionne **pas** l'expectancy MeanReverting H1 dans le sens attendu. Le VIX bas est le pire segment ; la réversion est moins mauvaise quand le VIX est élevé/en hausse — l'inverse de la direction pré-enregistrée.
3. **Aucun segment conditionné par le VIX n'a une expectancy nette robustement positive.** Les trois terciles de niveau sont robustement net-négatifs (netR −0,043 à −0,097, IC excluant 0). Le mieux atteint est la neutralité statistique sur des sous-échantillons de 242–379 trades sans accord TRAIN/OOS.
4. Le book MeanReverting H1 reste robustement net-négatif globalement (−0,074 R, IC excluant 0) — le résultat de QDE-012 est reconfirmé sur ce pull de données (7ᵉ vérification convergente).

**QDE-014 ne confirme aucun edge.**

---

## 8. Exploratoire / non concluant

Deux gradients faibles, cohérents en direction, **générateurs d'hypothèse pour des lots dédiés ultérieurs, jamais des conclusions ici** :

- **A. VIX élevé / en hausse → book moins non-rentable.** grossR passe de −0,038 (VIX bas) / −0,049 (VIX en baisse) à −0,013 (VIX haut) / +0,026 (VIX en hausse rapide). Direction opposée à l'hypothèse pré-enregistrée. Ne franchit jamais zéro robustement. L'échantillon ne contient aucun régime de VIX > 35, donc l'extrapolation vers un vrai pic de volatilité est impossible.
- **B. Taux en baisse (`TNX`↓ / `ZN=F`↑) → book moins non-rentable.** Vu de façon interne-cohérente sur deux instruments. Chevauche 0 dans le tercile concerné, TRAIN négatif, non confirmé OOS.

Chaque gradient exigerait, pour être validé : (a) un historique plus long incluant un régime de pic de volatilité et un cycle de taux complet, (b) un design OOS indépendant dédié, (c) un modèle directionnel plutôt qu'un filtre de conditionnement appliqué à un book sans edge de base.

---

## 9. Recommandation

**Ne pas poursuivre une stratégie MeanReverting conditionnée par le VIX.** L'hypothèse primaire pré-enregistrée a échoué et est contredite en direction ; aucun segment n'est net-positif ; le book reste robustement perdant dans chaque tercile.

Compte tenu de **sept résultats négatifs convergents** sur MeanReverting (QDE-012 ×6 + QDE-014), continuer à conditionner ce book précis a un rendement décroissant. Options, par valeur décroissante :

1. **Tester si l'état cross-asset conditionne un signal DIFFÉRENT** — soit le book Trending (dont QDE-012 laissait un résultat OOS fragile non tranché), soit un nouveau modèle directionnel — plutôt que de continuer à conditionner MeanReverting. C'est un nouveau lot (QDE-015 candidat).
2. **Lot dédié sur le gradient A (VIX élevé)** avec un historique incluant un régime de crise — mais uniquement si une source d'historique horaire > 2 ans (ou daily) est acquise, et avec un modèle directionnel, pas un filtre.
3. **Lot dédié sur le gradient B (taux en baisse)** — priorité plus faible (signal plus faible que A, même problème d'échantillon).

**Ne PAS :** tirer une conclusion « edge » d'un des gradients exploratoires sans confirmation OOS indépendante dans un lot dédié ; ajuster un paramètre de production ; ajouter une entrée à `YahooSymbolMap` ; modifier le Risk Engine, la Decision Rule ou le statut SIGNAL_ONLY.

---

## 10. Interdictions respectées

- **Aucune modification de code de production.** Un seul fichier ajouté, `Tests/Research/NewDataSourceFeasibility/VixRegimeConditioningPilotTests.cs`, isolé, réutilisant `HttpYahooChartClient` / `YahooChartParser` / `BacktestEngine` / `PositionCostCalculator` **sans les modifier**. `YahooSymbolMap` intouché (la sonde passe les tickers en direct).
- **Aucune calibration, aucun ajustement de paramètre de production.**
- **Aucune conclusion « edge confirmé »** tirée d'une hypothèse secondaire. Les deux gradients sont explicitement qualifiés d'exploratoires et renvoyés à un lot dédié avec confirmation OOS indépendante.
- **Risk Engine, Decision Rule, statut SIGNAL_ONLY : inchangés.**

---

## 11. Index des fichiers produits

| Fichier | Rôle | Committable en production ? |
|---|---|---|
| `IQIAIndicator/Documentation/Scientific/QDE-014_CrossAsset_VIX_Pilot.md` | Ce rapport | Oui (documentation) |
| `IQIAIndicator/Tests/Research/NewDataSourceFeasibility/VixRegimeConditioningPilotTests.cs` | Sonde du pilote, read-only | Recherche uniquement |
| `IQIAIndicator/Tests/Research/NewDataSourceFeasibility/Output/vix_regime_pilot.txt` | Sortie mesurée (preuve des chiffres §3–§6) | Recherche uniquement |

Suppression éventuelle : `git clean -fd IQIAIndicator/Tests/Research/NewDataSourceFeasibility/`.

---

## 12. FINAL OUTPUT

**STATUS :** Pilote terminé. Hypothèse primaire (VIX conditionne l'expectancy MeanReverting H1) **réfutée** — contredite en direction, aucun segment net-positif, book robustement perdant dans chaque tercile de VIX (netR −0,043 à −0,097, IC excluant 0). 7ᵉ résultat négatif convergent pour MeanReverting. Deux gradients faibles exploratoires (VIX élevé, taux en baisse) → générateurs d'hypothèse, non concluants.

**PRODUCTION MODIFIED :** NO. 1 sonde de recherche ajoutée sous `Tests/Research/NewDataSourceFeasibility/`. `YahooSymbolMap` intouché.

**TESTS :** `VixRegimeConditioningPilotTests` — Passed (2 549 trades conditionnés, 0 rejeté, Phase 0 : 0 gap en session). Skipped si Yahoo indisponible.

**DOCUMENTATION :** Ce rapport — `IQIAIndicator/Documentation/Scientific/QDE-014_CrossAsset_VIX_Pilot.md`.

**NEXT ACTION :** Décision de priorisation. Recommandé : **QDE-015 — tester si l'état cross-asset (VIX niveau/variation, pente term-structure, variation de taux) conditionne le book Trending H1** (résultat OOS fragile laissé ouvert par QDE-012) ou un nouveau modèle directionnel — plutôt que de continuer à conditionner MeanReverting. Ne pas engager de lot sur les gradients exploratoires A/B sans acquisition préalable d'un historique incluant un régime de pic de volatilité.
