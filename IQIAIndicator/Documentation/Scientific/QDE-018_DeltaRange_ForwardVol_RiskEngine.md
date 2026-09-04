# QDE-018 — La trouvaille de volatilité de QDE-017 (`deltaRange`) comme estimateur forward pour le dimensionnement SL/TP

**Date :** 2026-09-01
**Mode :** lecture seule / mesure. **Production modifiée : NON.** Aucun changement de `VolatilityStopLossModel`, `RiskEngine`, Decision Rule ; statut SIGNAL_ONLY inchangé.
**Sonde :** `IQIAIndicator/Tests/Research/OrderFlowFeasibility/DeltaRangeForwardVolStopTests.cs` (read-only). Sortie : `Output/deltarange_forward_vol_stop.txt`.

---

## 1. Contexte et question

QDE-017 a réfuté l'hypothèse order-flow **directionnelle** (le signe du Delta par barre ne prédit pas la direction du rendement forward sur MES M5), mais a trouvé une relation **robuste et OOS-stable** : `(MaxDelta − MinDelta)/Volume` (« deltaRange », amplitude d'excursion intra-barre du delta, normalisée) est **inversement** corrélé à `|fwdMove|` (Pearson r ≈ −0,27). Classée « exploratoire, jamais concluante seule ».

L'utilisateur a choisi l'option 2 : tester si cette relation de volatilité peut servir d'**estimateur *forward* de la volatilité pour le Risk Engine**, là où la production dimensionne le Stop Loss sur un `2 × σ` *backward* (`VolatilityModel.CurrentVolatility` = écart-type des 20 derniers rendements M5). Le book MeanReverting n'ayant pas d'edge (QDE-012→015), l'objectif n'est **pas** de le rendre rentable, mais de savoir si un stop mieux calé sur la volatilité à venir **réduit la variance des résultats** (std du R par trade, drawdown) sans dégrader l'espérance.

**Phase 0 (bloquante) :** `deltaRange` apporte-t-il de l'information sur la volatilité future **au-delà** de ce que le `σ` backward de production capture déjà ? Si non-incrémental → l'option 2 est sans fondement (un stop `deltaRange`-informé serait juste une version bruitée du `2×σ` actuel).

---

## 2. Méthodologie

- **Données :** le corpus order-flow de QDE-017 — `orderflow_export_20260901_185621.csv`, **67 236 barres MES M5 propres** (2025-09-18 → 2026-09-01, ~349 jours). Chargé en `HistoricalSeries` (OHLCV + slots `HistoricalBar.BidVolume/AskVolume/Delta/OpenInterest` remplis) via `OrderFlowCsvBarSource` — **zéro modification de production**.
- **Book :** `RunFullBacktest` inchangé (warmup 128, horizon 10, coût round-trip base ~0,77 pt) → **13 556 trades MeanReverting** avec, par barre de signal : `σ_backward` (= `CurrentVolatility` lu depuis les résultats scientifiques), `deltaRange`, `|Delta|/Volume`, prix d'entrée (fill = open barre suivante), prix de référence (équilibre Kalman).
- **Cible de volatilité forward :** `MAE` = excursion adverse maximale sur les 10 barres de détention (points). C'est la grandeur qu'un stop doit couvrir.
- **Split :** TRAIN 60 % (n=8 132) / OOS 40 % (n=5 423), purge 40 barres.
- **Phase 0 :** OLS log-log fitté sur TRAIN, scoré sur TRAIN et OOS :
  - modèle A : `log MAE ~ log σ_backward`
  - modèle B : `log MAE ~ log σ_backward + log deltaRange`
  - métriques : R² de A et B ; **R² incrémental** (B − A) ; **corrélation partielle** de `log deltaRange` avec `log MAE` en contrôlant `log σ_backward` ; coefficient ajusté de `deltaRange`.
  - **Gate pour passer en Phase 1 :** R² incrémental OOS ≥ 0,01 **ET** |corrélation partielle OOS| ≥ 0,05.
- **Phase 1 (conditionnelle) :** stop `deltaRange`-ajusté `altStop = 2 σ_backward · (médiane(deltaRange)_TRAIN / deltaRange)^p`, exposant `p ∈ [0 ; 1]` calibré sur TRAIN pour minimiser le CV de `MAE/altStop` (rendre le stop un multiple stable de l'excursion réelle). Re-simulation de la course SL/TP (cible = équilibre) sur l'OOS, baseline `2σ` vs alt : taux de stop-out, mix de sorties, espérance nette (R et $), **std du R par trade**, pire trade, drawdown $ max.

---

## 3. Résultats — Phase 0

| | corr(log MAE, log σ) | corr(log MAE, log ΔRange) | R²(σ seul) | R²(σ + ΔRange) | **R² incrémental** | **corr. partielle** (ΔRange \| σ) | coeff b₂ |
|---|---|---|---|---|---|---|---|
| **TRAIN** (n=8 132) | **+0,232** | −0,167 | 0,0540 | 0,0554 | **+0,0014** | **−0,039** | −0,212 |
| **OOS** (n=5 423) | **+0,220** | −0,166 | 0,0477 | 0,0514 | **+0,0037** | **−0,070** | −0,212 |

*(Bug cosmétique de la sonde : le format `:+0.000` affiche `-+0.XXX` pour un négatif. Valeurs réelles ci-dessus.)*

**Constats :**

1. **`deltaRange` a bien une corrélation brute négative avec la MAE** (−0,166 / −0,167) — cohérent avec QDE-017 (là r ≈ −0,27 avec `|fwdMove|` ; ici −0,17 avec la MAE, cible plus bruitée).
2. **Le `σ` backward de production est le meilleur prédicteur de volatilité forward :** corr(log MAE, log σ) = +0,22–0,23, supérieure à celle de `deltaRange`. `VolatilityModel.CurrentVolatility` prédit déjà l'excursion adverse de l'heure suivante, et mieux.
3. **`deltaRange` est quasi entièrement redondant avec `σ` :**
   - **R² incrémental** : +0,0014 (TRAIN), +0,0037 (OOS) — ajouter `deltaRange` à un modèle σ-seul de la vol forward améliore le R² de **0,14 à 0,37 point de pourcentage**. Négligeable.
   - **Corrélation partielle** : −0,039 (TRAIN), −0,070 (OOS). Les ~75–80 % de la corrélation brute de `deltaRange` avec la MAE s'expliquent par le fait que `deltaRange` est lui-même corrélé à `σ` (les deux mesurent l'intensité récente de l'activité de trading).
4. **Le modèle est faible en absolu :** R²(σ + ΔRange) ≈ 0,05 — seuls ~5 % de la log-variance de la MAE forward sont prévisibles depuis l'un ou l'autre. La volatilité forward de MES M5 est largement imprévisible au niveau de la barre individuelle.

**Gate : ÉCHEC.** R² incrémental OOS = 0,0037 (< 0,01). *(La corrélation partielle OOS = −0,070 passe de justesse son propre seuil de 0,05, mais le ET avec le R² incrémental échoue.)*

**Phase 1 non lancée.**

---

## 4. Établi avec confiance

1. **`deltaRange` n'apporte AUCUNE information de volatilité forward au-delà du `σ` backward de production.** À `σ` contrôlé, son R² incrémental est de 0,14–0,37 point de % (OOS 0,0037) et sa corrélation partielle de −0,04/−0,07. Corpus de 13 556 trades, puissance maximale.
2. **La trouvaille exploratoire de QDE-017 est réelle mais non incrémentale** — un ré-emballage d'information que `VolatilityModel.CurrentVolatility` capture déjà, et mieux (corr σ +0,22 > corr deltaRange −0,17 en valeur absolue). C'est exactement ce que « exploratoire, jamais concluante seule » signifiait : contrôlée pour l'estimateur de vol existant, la relation se dissout.
3. **Un stop `deltaRange`-informé serait une version plus bruitée du `2×σ` actuel.** Option 2 sans fondement empirique.
4. **La volatilité forward de MES M5 est ~95 % non prévisible** à l'échelle de la barre depuis σ ou l'order flow agrégé (R² ≈ 0,05).

**QDE-018 ne valide aucune amélioration du Risk Engine.**

---

## 5. Exploratoire / non concluant

Rien de nouveau. Le `σ` backward de production reste le meilleur estimateur de volatilité disponible pour le dimensionnement du stop, et sa qualité prédictive plafonne bas (corr ~0,22, R² ~0,05) — mais ce n'est pas un défaut de l'estimateur, c'est une propriété du marché à cette granularité.

---

## 6. Recommandation

L'axe **order flow agrégé par barre** est clos sur ses deux fronts :
- **directionnel** : pas d'edge (QDE-017 Test 1 — hit rate 0,47–0,49, espérance nette = −1 aller-retour) ;
- **volatilité** : signal brut réel mais non incrémental sur `σ` (QDE-018 Phase 0).

Options restantes, par valeur décroissante :

1. **QDE-019 — footprint détaillé (dernier test de l'axe order flow).** Tout ce qui précède teste des **agrégats par barre** (`Bid`, `Ask`, `Delta`, `MaxDelta`, `MinDelta`). Le footprint par niveau de prix (`IndicatorCandle.GetAllPriceLevels()` → volume/bid/ask à chaque prix) porte une structure que ces agrégats masquent : **absorption** (gros volume à un prix sans progression), **imbalances diagonales empilées**, **divergence delta/prix aux extrêmes de barre**, position du **POC** vs clôture, forme de la **Value Area**. C'est le seul contenu order-flow non encore testé. Coût : ~40 lignes de plus dans `OrderFlowExport` (sérialisation du footprint) + un re-export + une sonde d'analyse (~1 run de 20 min). **À faire une seule fois, comme test terminal.**
2. **Arrêt de l'axe recherche d'edge.** Acter que MES intraday (M5–H1, ~2 ans de prix + ~1 an d'order flow agrégé) n'a pas de structure directionnelle exploitable après coûts, et que la seule relation robuste trouvée (imbalance → volatilité) est redondante avec l'outillage existant. Le système reste en SIGNAL_ONLY.

**Ne PAS :** implémenter un stop `deltaRange` ; modifier le Risk Engine ; conclure quoi que ce soit de positif de la relation de volatilité.

---

## 7. Interdictions respectées

- **Aucune modification de production.** `VolatilityStopLossModel`, `RiskEngine`, `HistoricalBar`, `HistoricalSeries`, Decision Rule : intouchés. La sonde lit les slots order-flow **déjà présents** dans `HistoricalBar` et les API publiques `HistoricalSeries.Create` / `IHistoricalBarSource`.
- **Aucune calibration de production**, aucun paramètre modifié. Le calage de l'exposant `p` (Phase 1) n'a pas eu lieu (gate échoué) et n'aurait de toute façon vécu que dans la sonde.
- **Statut SIGNAL_ONLY inchangé.**

---

## 8. Index des fichiers produits

| Fichier | Rôle | Committable en production ? |
|---|---|---|
| `IQIAIndicator/Documentation/Scientific/QDE-018_DeltaRange_ForwardVol_RiskEngine.md` | Ce rapport | Oui (documentation) |
| `IQIAIndicator/Tests/Research/OrderFlowFeasibility/DeltaRangeForwardVolStopTests.cs` | Sonde de mesure, read-only | Recherche uniquement |
| `IQIAIndicator/Tests/Research/OrderFlowFeasibility/Output/deltarange_forward_vol_stop.txt` | Sortie mesurée (preuve §3) | Recherche uniquement |

Suppression éventuelle : `git clean -fd IQIAIndicator/Tests/Research/OrderFlowFeasibility/`.

---

## 9. FINAL OUTPUT

**STATUS :** Phase 0 **GATE ÉCHOUÉ**. `deltaRange` n'apporte pas d'information de volatilité forward au-delà du `σ` backward de production : R² incrémental OOS = 0,0037 (seuil 0,01), corrélation partielle OOS = −0,070. Sur 13 556 trades MeanReverting M5. La trouvaille de QDE-017 est réelle mais **redondante** avec `VolatilityModel.CurrentVolatility`. Phase 1 (comparaison des stops) non lancée. **Option 2 sans fondement.**

**PRODUCTION MODIFIED :** NO. 1 sonde de mesure sous `Tests/Research/OrderFlowFeasibility/`. Aucun type de production touché.

**TESTS :** `DeltaRangeForwardVolStopTests` — Passed (Phase 0 exécutée, gate évalué, Phase 1 correctement sautée). Bug cosmétique de format `-+` sur les valeurs négatives (récupérables).

**DOCUMENTATION :** Ce rapport — `IQIAIndicator/Documentation/Scientific/QDE-018_DeltaRange_ForwardVol_RiskEngine.md`.

**NEXT ACTION :** Décision. Option 1 — **QDE-019 : footprint détaillé** (`GetAllPriceLevels` : absorption / imbalances diagonales / divergence delta-prix / POC), le seul contenu order-flow non testé, comme test terminal de l'axe (~40 lignes d'export + 1 run). Option 2 — **arrêt de l'axe recherche d'edge**, système en SIGNAL_ONLY, MES intraday acté sans structure directionnelle exploitable ni dans le prix, ni dans l'order flow agrégé.
