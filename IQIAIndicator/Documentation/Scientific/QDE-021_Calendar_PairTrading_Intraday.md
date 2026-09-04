# QDE-021 — Effets calendaires/horaires et pair-trading MES↔NQ, contraints intraday

**Date :** 2026-09-01
**Mode :** lecture seule. **Production modifiée : NON.** Aucune entrée ajoutée à `YahooSymbolMap` (sonde en contournement, comme QDE-013/014/015/020). Aucun seuil calibré sur l'échantillon complet (θ calibré sur TRAIN uniquement). **Aucune position simulée ne franchit la clôture de session** dans l'un ou l'autre axe.
**Sonde :** `IQIAIndicator/Tests/Research/CalendarPairTrading/CalendarAndPairTradingTests.cs` (read-only ; réutilise `HttpYahooChartClient`, `YahooChartParser` et `OrderFlowCsvBarSource` **sans les modifier**). Sorties : `Output/qde021_axisA_calendar.txt`, `Output/qde021_axisB_pairtrading.txt`.

---

## 1. Contexte et question

QDE-016 → QDE-020 ont fermé l'axe order flow et le changement de sous-jacent. Deux pistes restaient non testées, toutes deux jouables **sans jamais tenir de position overnight** (contrainte propfirm — flat en soirée) et **sans nouvelle source de données** :

- **Axe A** — les effets **calendaires / horaires** intraday sur MES seul (jour de la semaine, bloc horaire dans la session, position dans le mois — tout dérivable de la date seule).
- **Axe B** — le spread **MES↔NQ** comme **stratégie de paire autonome**, c'est-à-dire tester si le spread lui-même revient à sa moyenne (distinct de QDE-014, qui testait si le spread prédisait le rendement de MES seul — refusé).

---

## 2. Données

| Axe | Série | Source | Fréq. | Fenêtre | n |
|---|---|---|---|---|---|
| A | MES=F | Yahoo (`FetchChartJson` direct) | H1 natif | 2024-09-11 → 2026-09-01 (~720 j) | 10 821 rendements contigus (dont 2 960 RTH) |
| A | MES M5 natif | capture order flow QDE-017 (`orderflow_export_*.csv`) | M5 natif | 2025-09-18 → 2026-09-01 (~349 j) | 67 002 rendements contigus (dont 18 790 RTH) |
| B | MES=F + NQ=F | Yahoo (`FetchChartJson` direct) | H1 natif | 2024-09-11 → 2026-09-01 (~720 j) | 11 302 barres alignées à l'horodatage |

**Limitation de couverture (documentée, non contournée) :** Yahoo plafonne le M5 à ~58 jours (vérifié QDE-013). Le M5 « ~700 j » du brief est donc impossible via Yahoo ; l'axe A utilise à la place la capture M5 réelle de QDE-017 (~349 j, ≈ 11 mois — suffisant pour jour-de-semaine, bloc horaire et position-dans-le-mois). L'axe B reste en H1 : NQ=F en M5 au-delà de ~58 j n'existe pas dans la source.

**Fuseau :** tous les horodatages (Yahoo Unix→UTC ; capture ATAS = UTC, vérifié — première barre 22:00 UTC = réouverture Globex 18:00 ET en EDT) sont convertis en **America/New_York** (`TimeZoneInfo`, DST géré) pour le partitionnement en heure de session.

---

## 3. Axe A — Méthodologie

- **Attribution :** chaque log-rendement est rattaché à l'**heure ET du DÉBUT** de l'intervalle sur lequel il court. Barres contiguës uniquement (écart ≤ 1,5× l'intervalle).
- **Partitions** (identiques H1 et M5) :
  1. **Jour de semaine** (lundi–vendredi) — mesuré sur les rendements **RTH-only** (09:30–16:00 ET), pour qu'un survivant soit un effet intraday-tradeable.
  2. **Bloc horaire** — mesuré sur **toutes** les barres (les blocs overnight *sont* l'hypothèse) : ouverture RTH 09:30–10:30, milieu RTH 11:00–14:00, dernière heure RTH 15:00–16:00, et overnight en tranches de 4 h : 18:00–22:00, 22:00–02:00, 02:00–06:00, 06:00–09:30 ET.
  3. **Position dans le mois** — rang du jour de bourse dans le mois calendaire ET (dérivé des dates distinctes) : 3 premiers jours de bourse / 3 derniers / milieu. Mesuré RTH-only.
- **Test par partition :** Welch deux échantillons *bucket vs complément* (même univers), p bilatérale (approx. normale, n ≥ plusieurs centaines partout). On rapporte n, moyenne du bucket, écart à la baseline, t, p.
- **Correction de comparaisons multiples (bloquante) :** Holm-Bonferroni sur **l'ensemble des partitions de l'axe** — 15 par résolution, **30 au total** (H1 + M5), α familial = 0,05. Un Holm par résolution (m = 15) est aussi rapporté à titre indicatif.
- **Étape suivante (seulement si survivant) :** net de coût (moyenne du bucket convertie en points vs coût aller-retour MES 0,77 pt) puis split TRAIN/OOS purgé.

## 3.1 Axe A — Résultats

**Aucune partition ne survit, à aucun niveau de correction.** Table complète (30 lignes) dans `qde021_axisA_calendar.txt`. Extrait des plus petites p :

| partition | n | moyenne/barre | baseline | p brute | Holm (m=30) | Holm/résolution (m=15) |
|---|---|---|---|---|---|---|
| M5 : overnight 22:00–02:00 ET | 11 760 | −4,45e-6 | +1,52e-6 | **0,0385** | non sig (crit 0,0017) | non sig (crit 0,0033) |
| M5 : PIM 3 premiers JB | 2 829 | +1,91e-5 | −6,7e-7 | 0,082 | non sig | non sig |
| M5 : dernière heure RTH 15:00–16:00 | 2 832 | −1,85e-5 | +1,52e-6 | 0,098 | non sig | non sig |
| M5 : PIM milieu | 13 224 | −5,98e-6 | −6,7e-7 | 0,109 | non sig | non sig |
| H1 : overnight 02:00–06:00 ET | 1 969 | +8,52e-5 | +2,39e-5 | 0,114 | non sig | non sig |
| H1 : jeudi (RTH) | 582 | −1,21e-4 | +1,03e-5 | 0,182 | non sig | non sig |

**Constats :**
1. **La plus petite p de tout l'axe est 0,0385** (overnight M5 22:00–02:00 ET). Elle échoue même un Holm *par résolution* (seuil 0,0033) et n'est que marginalement sous 0,05 sans aucune correction. Rien d'autre n'est sous 0,08.
2. **Aucune réplication inter-résolution.** Les deux plus gros effets bruts H1 — jeudi RTH (−1,21e-4, p 0,18) et overnight 02:00–06:00 (+8,5e-5, p 0,11) — ont en M5 des p de 0,35 et 0,48 respectivement. Le seul signe partagé (overnight tardif légèrement négatif) est non significatif des deux côtés.
3. **Pas d'effet « ouverture » ni « dernière heure ».** Ouverture RTH : H1 p 0,67, M5 p 0,70. Dernière heure RTH : H1 p 0,54, M5 p 0,10 (négatif, non robuste).
4. **Pas d'effet tournant de mois.** Les 3 buckets PIM sont non significatifs aux deux résolutions (p ≥ 0,08).
5. L'étape net-de-coût / OOS purgé **n'est pas atteinte** : aucun candidat. Pour référence, la plus grosse moyenne de bucket (H1 jeudi, −1,21e-4/barre ≈ −0,79 pt sur la journée RTH) serait de toute façon au niveau du coût aller-retour (0,77 pt) — sans marge, et non significative.

---

## 4. Axe B — Méthodologie

- **Spread :** `s_t = ln(P_MES,t) − ln(P_NQ,t)` (ratio log, β = 1 — MES et NQ sont deux indices actions US très corrélés ; variante β glissant non nécessaire vu le résultat).
- **z-score glissant causal :** moyenne et écart-type de `s` sur `[t−W, t−1]` (jamais la barre courante), déviation évaluée sur `s_t`. Fenêtres testées **W ∈ {60, 120, 240}** barres H1.
- **Règle (mean-reversion) :**
  - **Entrée** quand `|z_t| ≥ θ` **et** l'heure ET est dans **[09:30, 15:00)** RTH (≥ 1 h de marge avant la clôture forcée). `z > 0` ⇒ MES riche vs NQ ⇒ **short le spread** (short MES / long NQ) ; `z < 0` ⇒ inverse.
  - **Sortie**, au premier des trois : (a) **clôture de session obligatoire** — première barre à heure ET ≥ 16:00 **ou** changement de date ET (⇒ jamais overnight) ; (b) retour à la moyenne `|z| ≤ 0,25` ; (c) stop `|z| ≥ θ + 1,5`.
  - Pas de positions superposées.
- **Unité de risque R :** σ du spread à l'entrée (fenêtre W). `grossR = −sign(z_entrée)·(s_sortie − s_entrée) / σ_entrée`.
- **Coût net — les DEUX jambes, aller-retour chacune, en unités de spread (log), à partir des prix d'entrée réels :**
  - **MES** (micro) : 3,855 $/AR à 5 $/pt (identique QDE-012+).
  - **NQ, jambe primaire = MNQ** (Micro E-mini Nasdaq-100, specs CME) : 2 $/pt, tick 0,25 = 0,50 $ ; AR retail ≈ commission 1,04 $ + 1 tick 0,50 $ ≈ **1,54 $/AR**. C'est l'instrument réaliste pour une paire retail/propfirm face à un micro MES.
  - **NQ, jambe secondaire = NQ E-mini** (specs CME) : 20 $/pt, tick 0,25 = 5,00 $ ; AR ≈ commission 2,50 $ + 1 tick 5,00 $ ≈ **7,50 $/AR** (colonne « OOS netR(NQ) »).
- **Calibration :** split **60/40 purgé** (purge 20 barres). θ **sélectionné sur TRAIN uniquement** (max netR TRAIN, ≥ 30 trades) ; on rapporte l'OOS de ce θ. La grille TRAIN×OOS complète est imprimée pour transparence, mais la décision est le θ TRAIN.

## 4.1 Axe B — Résultats

11 302 paires H1 alignées. Split : TRAIN barre < 6 761, OOS barre ≥ 6 781 (purge 20).

| W | θ TRAIN-sélectionné | TRAIN netR (n) | **OOS netR MNQ ± IC95 (n)** | OOS netR NQ E-mini | verdict OOS |
|---|---|---|---|---|---|
| 60 | 2,0 | +0,162 (n=126) | **−0,003 ± 0,215 (n=81)** | +0,004 | straddle 0 |
| 120 | 2,5 | +0,202 (n=55) | **−0,125 ± 0,330 (n=33)** | −0,120 | straddle 0 |
| 240 | 2,5 | +0,130 (n=33) | **−0,008 ± 0,187 (n=50)** | −0,005 | straddle 0 |

**Constats :**
1. **Aucune fenêtre n'est NET+ ROBUST en OOS.** Le θ sélectionné sur TRAIN donne un netR OOS qui enjambe 0 (W=60, W=240) ou est franchement négatif (W=120). Le netR TRAIN positif (+0,13 à +0,20 R) **ne se transporte jamais** hors échantillon.
2. **W=120 est négatif sur TOUTE la grille θ en OOS** (−0,070 à −0,135 R de θ=1,0 à θ=3,0). W=60 et W=240 oscillent autour de 0 sans structure exploitable.
3. **Le coût n'est pas le tueur.** L'écart grossR→netR n'est que de ~0,05–0,10 R (les micros sont peu coûteux relativement à σ du spread). Le résultat NQ E-mini (jambe plus coûteuse) est quasi identique. La paire échoue parce qu'**il n'y a pas de réversion du spread hors échantillon**, pas à cause des frictions.
4. **Cohérent avec l'acquis.** QDE-020 a établi que MES et NQ sont tous deux **sériellement indépendants à H1** ; un spread de deux marches aléatoires très corrélées n'a pas de raison de revenir à une moyenne courte. QDE-014 avait déjà trouvé le spread MES−NQ **sans valeur de conditionnement**. L'axe B teste la version « spread autonome » de la même idée et obtient le même signe.
5. Effectifs OOS minces au θ retenu (n = 33–81), ce qui gonfle les IC — mais la direction (0 ou négatif) est stable sur les trois fenêtres et sur toute la grille de W=120.

---

## 5. Établi avec confiance

1. **Axe A — aucun effet calendaire/horaire intraday sur MES.** Sur 30 partitions (2 résolutions × {jour de semaine, 7 blocs horaires, position-dans-le-mois}), **zéro ne survit à la correction Holm** (α familial 0,05), la plus petite p de tout l'axe étant 0,0385 et ne résistant même pas à un Holm par résolution. Aucune réplication H1↔M5. L'étape net-de-coût n'est pas atteinte faute de candidat.
2. **Axe B — le pair-trading MES↔NQ n'a pas d'edge OOS robuste.** Sur 3 fenêtres de z-score, avec θ calibré en TRAIN seulement, clôture forcée en fin de session (jamais overnight) et coût réaliste des deux jambes (MNQ primaire, NQ E-mini secondaire), **aucune configuration n'est NET+ ROBUST en OOS** : le netR OOS enjambe 0 ou est négatif, tandis que W=120 est négatif sur toute la grille θ. Le netR TRAIN positif est un artefact de sélection. Le coût n'est pas la cause — le spread ne revient pas à sa moyenne hors échantillon, ce qui est la conséquence directe de l'indépendance sérielle de MES et NQ à H1 (QDE-020).

**QDE-021 ne révèle aucune piste d'edge exploitable, sur aucun des deux axes.**

---

## 6. Exploratoire / non concluant

- **H1 jeudi RTH** : moyenne la plus négative de l'axe A (−1,21e-4/barre, ≈ −0,79 pt sur la journée RTH) mais p = 0,18, non répliquée en M5 (p = 0,35), et à peine au niveau du coût aller-retour. Rien d'actionnable.
- **Overnight tardif (02:00–06:00 ET H1 / 22:00–02:00 ET M5)** : léger biais, de signes opposés selon la résolution, jamais significatif. Bruit.
- **Axe B W=60, θ=1,5** : seule cellule avec un OOS légèrement positif (+0,017 R MNQ) — non significatif (IC ±0,143), non retenue par la sélection TRAIN, non stable entre fenêtres. À ignorer.

---

## 7. Recommandation

**Les deux dernières pistes intraday sans nouvelle donnée sont épuisées, négativement.** Les effets calendaires/horaires n'existent pas sur MES à ces résolutions ; le spread MES↔NQ ne revient pas à sa moyenne hors échantillon.

**La recommandation de QDE-019/QDE-020 tient et se renforce :** arrêt de l'axe recherche d'edge, système en **SIGNAL_ONLY**. Toute reprise exige une **source de données réellement différente** ou un **marché / horizon fondamentalement autre** — pas une itération de plus sur MES intraday ou sur une paire de futures d'indices retail.

**Ne PAS :** relancer un lot fondé sur les données actuelles ; interpréter le netR TRAIN positif de l'axe B comme un edge ; refaire le pair-trading avec un θ choisi sur l'échantillon complet ou une fenêtre choisie *a posteriori*.

---

## 8. Interdictions respectées

- **Aucune modification de production.** 1 fichier ajouté : `Tests/Research/CalendarPairTrading/CalendarAndPairTradingTests.cs`. Réutilise `HttpYahooChartClient`, `YahooChartParser`, `OrderFlowCsvBarSource` **sans les modifier**.
- **Aucune entrée ajoutée à `YahooSymbolMap`** — la sonde passe `MES=F` / `NQ=F` en direct au `HttpYahooChartClient`.
- **Aucune calibration sur l'échantillon complet.** θ (axe B) sélectionné sur TRAIN seul ; fenêtres W énumérées a priori, pas choisies au vu de l'OOS.
- **Aucune position overnight.** Axe A : les partitions jour-de-semaine et position-dans-le-mois sont mesurées sur des rendements RTH-only ; les blocs overnight sont des sous-tranches intra-session. Axe B : le simulateur force le flat à la première barre ≥ 16:00 ET ou au changement de date ET, indépendamment de tout horizon — vérifié dans `Simulate()`.
- **Verdict séparé par axe fourni** (§5), section « établi avec confiance » remplie pour les deux — les deux questions sont tranchées, pas laissées ouvertes.

---

## 9. Index des fichiers produits

| Fichier | Rôle | Committable en production ? |
|---|---|---|
| `IQIAIndicator/Documentation/Scientific/QDE-021_Calendar_PairTrading_Intraday.md` | Ce rapport | Oui (documentation) |
| `IQIAIndicator/Tests/Research/CalendarPairTrading/CalendarAndPairTradingTests.cs` | Sonde deux axes, read-only | Recherche uniquement |
| `IQIAIndicator/Tests/Research/CalendarPairTrading/Output/qde021_axisA_calendar.txt` | Sortie mesurée axe A (preuve §3.1) | Recherche uniquement |
| `IQIAIndicator/Tests/Research/CalendarPairTrading/Output/qde021_axisB_pairtrading.txt` | Sortie mesurée axe B (preuve §4.1) | Recherche uniquement |

Suppression éventuelle : `git clean -fd IQIAIndicator/Tests/Research/CalendarPairTrading/`.

---

## 10. FINAL OUTPUT

**STATUS :** Terminé. **Axe A (calendaire/horaire) : NÉGATIF** — 0/30 partitions survivent à Holm (α 0,05), plus petite p 0,0385, aucune réplication H1↔M5. **Axe B (pair-trading MES↔NQ) : NÉGATIF** — aucune fenêtre de z-score n'est NET+ ROBUST en OOS avec θ calibré sur TRAIN, clôture forcée intraday et coût réaliste des deux jambes ; le netR TRAIN positif ne se transporte pas hors échantillon (W=120 négatif sur toute la grille θ). Le coût n'est pas la cause — pas de réversion du spread OOS, conséquence de l'indépendance sérielle MES/NQ à H1 (QDE-020).

**PRODUCTION MODIFIED :** NO. 1 sonde de recherche. `YahooSymbolMap` intouché. θ jamais calibré sur l'échantillon complet. Aucune position overnight simulée.

**TESTS :** `CalendarAndPairTradingTests` — 2/2 Passed (`AxisA_Calendar_Hourly_Effects_MES`, `AxisB_PairTrading_MES_NQ_Intraday`).

**DOCUMENTATION :** Ce rapport — `IQIAIndicator/Documentation/Scientific/QDE-021_Calendar_PairTrading_Intraday.md`.

**NEXT ACTION :** Aucune. La recommandation de QDE-019/QDE-020 est confirmée : arrêt de l'axe recherche d'edge, système en SIGNAL_ONLY. Les deux dernières pistes intraday sans nouvelle donnée (calendaire, pair-trading) sont fermées négativement. Toute reprise = nouvelle source de données ou marché/horizon fondamentalement différent.
