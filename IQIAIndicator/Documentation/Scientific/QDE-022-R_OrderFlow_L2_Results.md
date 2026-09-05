**Hash du pré-enregistrement en vigueur : `eaba9be` (tag `qde-022-prereg-v4`). Chaîne complète : v1 `2d21ae5` → v2 `2e8d7c9` → v3 `2dd383a` → v4 `eaba9be`.**

# QDE-022-R — Résultats : contenu informationnel du carnet d'ordres L2 (ES, Databento GLBX.MDP3)

**Date :** 2026-09-05
**Mode :** lecture seule sur les résultats. **Production modifiée : NON.** Aucune entrée `YahooSymbolMap`. Aucun code de stratégie, aucun backtest, aucun P&L, aucun Sharpe.
**Pré-enregistrement gelé :** `IQIAIndicator/Tests/Research/OrderFlow/QDE-022_OrderFlow_L2_PreRegistration.md`, tag `qde-022-prereg-v4`, commit `eaba9be`.
**Sonde :** `qde022_build_grid.py` (grille 1 s) + `qde022_ic_curve.py` (courbe d'IC), toutes deux sous `IQIAIndicator/Tests/Research/OrderFlow/`. Résultats : `qde022_ic_curve_results.csv` (même dossier, 9 288 octets) ; grilles Parquet hors dépôt (`QDE-022_Data/grid_1s`, volumineuses, régénérables).
**Vérification d'intégrité des données :** `QDE-022_DataIntegrity_Check.md`, verdict **CONFORME** (mis à jour le 2026-09-05, historique complet du verdict conservé dans ce document).

---

## 1. Contexte et question

QDE-012 → QDE-021 ont fermé, négativement, toutes les pistes d'edge directionnel accessibles sans nouvelle source de données : prix seul, order flow *par barre*, changement de sous-jacent, effets calendaires/horaires, pair-trading MES↔NQ (voir QDE-022 §1.1 pour le détail). QDE-022 teste la dernière information formellement absente de ces axes : la dynamique du **carnet d'ordres niveau 2** sur ES, à haute résolution (`mbp-10`).

**H1** (v4 §1.2, inchangée depuis v1) : au moins un des 4 indicateurs de carnet présente, à au moins un des 6 horizons, une corrélation avec le rendement futur qui est (i) significative après Holm sur 24 tests, (ii) stable en signe TRAIN/OOS, et (iii) économiquement matérielle au gate de §7.3.
**H0** : aucune des 24 cellules n'est porteuse à horizon ≥ 60 s.

Ce rapport applique cette règle, gelée depuis v1/v2, aux résultats mesurés sur la portée v4 (août 2026, `ESU6`).

---

## 2. Portée et données

| Paramètre | Valeur |
|---|---|
| Instrument | `ESU6`, `stype_in=raw_symbol` |
| Dataset / schéma | `GLBX.MDP3` / `mbp-10` |
| Période | 2026-08-02 → 2026-09-02 |
| Job Databento | `GLBX-20260904-93PSB55DT3` |
| Points de grille RTH (1 s) | **514 786** |
| Jours ouvrés couverts | **22** (5 dimanches à 0 point, 4 samedis absents du job — cf. contrôle 3 de `QDE-022_DataIntegrity_Check.md`) |
| Mises à jour de carnet retenues (RTH) / facturées | 130 991 806 / 175 348 820 = **ratio 0,7470** (le filtre RTH écarte la session de nuit — baisse attendue, non interprétée davantage ici) |
| Split | **TRAIN** 13 j / 304 192 pts — **OOS** 8 j / 187 194 pts — **purge** 1 j |

**Coût :**

| Poste | Montant |
|---|---|
| `metadata.get_cost` estimé (portée v3/v4) | 30,048361867666 USD |
| Facturé réellement (job `GLBX-20260904-93PSB55DT3`) | 30,048361867666 USD — identique au centime |
| Job `parent` non conforme antérieur (`GLBX-20260903-7XE859LGPE`), engagé hors portée | 33,13424949347973 USD |
| **Total Databento `GLBX.MDP3` à ce jour** | **63,18261136114597 USD** |
| Licence CME | **TODO, non résolu** (§2.3 du pré-enregistrement, inchangé) |

Le job `parent` est la source du risque résiduel documenté et assumé en v4 §8.7 (téléchargement de vraies données ESU6/août 2026 antérieur de ~34 h 28 au tag `qde-022-prereg-v3`) — rappelé en §7g ci-dessous, non ré-instruit ici.

---

## 3. Résultats bruts — les 48 lignes, sans sélection

### 3.1 TRAIN (24 lignes)

| horizon_s | indicator | ic | se_nw | ci95_lo | ci95_hi | p_value | sd_ret_ticks_es | move_ticks_es | move_ticks_mes | gate_es_pass | holm_reject |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 1 | ofi_l1 | 0.013945 | 0.003347 | 0.007386 | 0.020505 | 3.088539e-05 | 0.902295 | 0.010040 | 0.100397 | False | True |
| 1 | ofi_l10 | -0.035566 | 0.002984 | -0.041415 | -0.029717 | 9.489939e-33 | 0.902295 | 0.025605 | 0.256052 | False | True |
| 1 | book_imb | 0.060412 | 0.002127 | 0.056242 | 0.064582 | 2.283032e-177 | 0.902295 | 0.043492 | 0.434921 | False | True |
| 1 | micro_dev | 0.202887 | 0.001877 | 0.199208 | 0.206566 | 0.000000e+00 | 0.902295 | 0.146064 | 1.460637 | False | True |
| 5 | ofi_l1 | 0.007851 | 0.003341 | 0.001302 | 0.014400 | 1.878665e-02 | 1.959038 | 0.012272 | 0.122723 | False | False |
| 5 | ofi_l10 | -0.016987 | 0.002929 | -0.022728 | -0.011246 | 6.649540e-09 | 1.959038 | 0.026552 | 0.265524 | False | True |
| 5 | book_imb | 0.032397 | 0.003580 | 0.025380 | 0.039415 | 1.450239e-19 | 1.959038 | 0.050640 | 0.506400 | False | True |
| 5 | micro_dev | 0.101431 | 0.001970 | 0.097569 | 0.105292 | 0.000000e+00 | 1.959038 | 0.158545 | 1.585448 | False | True |
| 30 | ofi_l1 | -0.004592 | 0.002392 | -0.009281 | 0.000096 | 5.489044e-02 | 4.632569 | 0.016974 | 0.169742 | False | False |
| 30 | ofi_l10 | -0.015694 | 0.002177 | -0.019960 | -0.011428 | 5.598590e-13 | 4.632569 | 0.058009 | 0.580088 | False | True |
| 30 | book_imb | 0.016848 | 0.006851 | 0.003420 | 0.030277 | 1.392717e-02 | 4.632569 | 0.062276 | 0.622763 | False | False |
| 30 | micro_dev | 0.045897 | 0.002222 | 0.041543 | 0.050252 | 8.353956e-95 | 4.632569 | 0.169648 | 1.696480 | False | True |
| 60 | ofi_l1 | -0.001143 | 0.002361 | -0.005770 | 0.003484 | 6.282898e-01 | 6.408883 | 0.005844 | 0.058444 | False | False |
| 60 | ofi_l10 | -0.009464 | 0.002018 | -0.013419 | -0.005510 | 2.719118e-06 | 6.408883 | 0.048397 | 0.483967 | False | True |
| 60 | book_imb | 0.008647 | 0.008992 | -0.008977 | 0.026272 | 3.362302e-01 | 6.408883 | 0.044218 | 0.442178 | False | False |
| 60 | micro_dev | 0.031329 | 0.002342 | 0.026739 | 0.035919 | 8.079819e-41 | 6.408883 | 0.160203 | 1.602034 | False | True |
| 300 | ofi_l1 | -0.004092 | 0.002275 | -0.008552 | 0.000368 | 7.210568e-02 | 13.548605 | 0.044235 | 0.442354 | False | False |
| 300 | ofi_l10 | -0.007451 | 0.001841 | -0.011060 | -0.003843 | 5.187570e-05 | 13.548605 | 0.080550 | 0.805501 | False | True |
| 300 | book_imb | 0.019444 | 0.016376 | -0.012654 | 0.051542 | 2.350980e-01 | 13.548605 | 0.210195 | 2.101947 | False | False |
| 300 | micro_dev | 0.013487 | 0.002949 | 0.007707 | 0.019267 | 4.804987e-06 | 13.548605 | 0.145797 | 1.457966 | False | True |
| 900 | ofi_l1 | -0.000581 | 0.002184 | -0.004862 | 0.003701 | 7.903727e-01 | 22.315828 | 0.010339 | 0.103391 | False | False |
| 900 | ofi_l10 | -0.003136 | 0.001826 | -0.006716 | 0.000443 | 8.592395e-02 | 22.315828 | 0.055841 | 0.558407 | False | False |
| 900 | book_imb | 0.016923 | 0.022191 | -0.026571 | 0.060418 | 4.456914e-01 | 22.315828 | 0.301326 | 3.013260 | False | False |
| 900 | micro_dev | 0.011863 | 0.003472 | 0.005057 | 0.018668 | 6.341904e-04 | 22.315828 | 0.211223 | 2.112230 | False | True |

### 3.2 OOS (24 lignes)

| horizon_s | indicator | ic | se_nw | ci95_lo | ci95_hi | p_value | sd_ret_ticks_es | move_ticks_es | move_ticks_mes | gate_es_pass | holm_reject |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 1 | ofi_l1 | 0.019944 | 0.004726 | 0.010681 | 0.029207 | 2.442983e-05 | 0.888108 | 0.014132 | 0.141323 | False | False |
| 1 | ofi_l10 | -0.027952 | 0.004139 | -0.036065 | -0.019839 | 1.452204e-11 | 0.888108 | 0.019807 | 0.198070 | False | False |
| 1 | book_imb | 0.063753 | 0.002506 | 0.058840 | 0.068666 | 1.018851e-142 | 0.888108 | 0.045176 | 0.451758 | False | False |
| 1 | micro_dev | 0.203915 | 0.002432 | 0.199148 | 0.208681 | 0.000000e+00 | 0.888108 | 0.144496 | 1.444955 | False | False |
| 5 | ofi_l1 | 0.011125 | 0.004033 | 0.003222 | 0.019029 | 5.799056e-03 | 1.930420 | 0.017136 | 0.171360 | False | False |
| 5 | ofi_l10 | -0.013149 | 0.003748 | -0.020494 | -0.005803 | 4.505320e-04 | 1.930420 | 0.020252 | 0.202521 | False | False |
| 5 | book_imb | 0.029398 | 0.004483 | 0.020612 | 0.038183 | 5.447301e-11 | 1.930420 | 0.045280 | 0.452796 | False | False |
| 5 | micro_dev | 0.096548 | 0.002511 | 0.091627 | 0.101469 | 1.482197e-323 | 1.930420 | 0.148708 | 1.487082 | False | False |
| 30 | ofi_l1 | -0.000072 | 0.003351 | -0.006640 | 0.006495 | 9.827755e-01 | 4.592583 | 0.000265 | 0.002651 | False | False |
| 30 | ofi_l10 | -0.010781 | 0.002968 | -0.016599 | -0.004963 | 2.812790e-04 | 4.592583 | 0.039506 | 0.395060 | False | False |
| 30 | book_imb | -0.006444 | 0.009034 | -0.024150 | 0.011262 | 4.756334e-01 | 4.592583 | 0.023613 | 0.236133 | False | False |
| 30 | micro_dev | 0.037084 | 0.002853 | 0.031493 | 0.042675 | 1.218743e-38 | 4.592583 | 0.135889 | 1.358895 | False | False |
| 60 | ofi_l1 | 0.000736 | 0.002996 | -0.005136 | 0.006607 | 8.060068e-01 | 6.404733 | 0.003760 | 0.037596 | False | False |
| 60 | ofi_l10 | -0.006699 | 0.002593 | -0.011782 | -0.001616 | 9.792874e-03 | 6.404733 | 0.034233 | 0.342327 | False | False |
| 60 | book_imb | -0.005003 | 0.011471 | -0.027486 | 0.017481 | 6.627637e-01 | 6.404733 | 0.025564 | 0.255644 | False | False |
| 60 | micro_dev | 0.032303 | 0.003017 | 0.026390 | 0.038217 | 9.488403e-27 | 6.404733 | 0.165078 | 1.650778 | False | False |
| 300 | ofi_l1 | -0.000828 | 0.002946 | -0.006603 | 0.004946 | 7.786124e-01 | 13.639611 | 0.009014 | 0.090138 | False | False |
| 300 | ofi_l10 | -0.003829 | 0.002430 | -0.008593 | 0.000934 | 1.151015e-01 | 13.639611 | 0.041675 | 0.416755 | False | False |
| 300 | book_imb | -0.038125 | 0.019135 | -0.075629 | -0.000621 | 4.632391e-02 | 13.639611 | 0.414904 | 4.149038 | False | False |
| 300 | micro_dev | 0.012999 | 0.003365 | 0.006403 | 0.019594 | 1.120308e-04 | 13.639611 | 0.141461 | 1.414615 | False | False |
| 900 | ofi_l1 | 0.002879 | 0.003892 | -0.004749 | 0.010508 | 4.594180e-01 | 23.728912 | 0.054516 | 0.545162 | False | False |
| 900 | ofi_l10 | 0.000644 | 0.003193 | -0.005615 | 0.006902 | 8.402216e-01 | 23.728912 | 0.012188 | 0.121885 | False | False |
| 900 | book_imb | -0.075959 | 0.026814 | -0.128515 | -0.023403 | 4.614696e-03 | 23.728912 | **1.438127** | 14.381270 | **True** | False |
| 900 | micro_dev | 0.008822 | 0.003726 | 0.001518 | 0.016125 | 1.790879e-02 | 23.728912 | 0.167023 | 1.670227 | False | False |

`holm_reject` sur OOS vaut `False` par construction du script (Holm §5 n'est calculé que sur les 24 tests TRAIN, §7.1 condition 2 exige la stabilité de signe TRAIN→OOS, pas un second test Holm sur OOS) — ce n'est pas un résultat de test, juste une valeur par défaut.

---

## 4. Application de la règle de décision §7 (gelée depuis v1/v2, non modifiée)

Trois conditions cumulatives (§7.1), évaluées ici pour les horizons ≥ 60 s conformément au champ de H0 (§1.2) : `h ∈ {60, 300, 900}` s.

### 4.1 Condition 1 — Holm α = 0,05 sur les 24 tests TRAIN

Survivent (voir §3.1) : **`ofi_l10`@60s, `ofi_l10`@300s, `micro_dev`@60s, `micro_dev`@300s, `micro_dev`@900s**. Aucune autre cellule à h ≥ 60 s ne survit (`ofi_l1` et `book_imb` échouent Holm à tous les horizons ≥ 60 s ; `ofi_l10`@900s échoue aussi, p = 0,0859).

### 4.2 Condition 2 — stabilité de signe TRAIN/OOS et IC95(OOS) excluant 0

| Cellule | Signe TRAIN | Signe OOS | IC95(OOS) exclut 0 ? | Condition 2 |
|---|---|---|---|---|
| `ofi_l10`@60s | − (−0,00946) | − (−0,00670) | oui : [−0,01178 ; −0,00162] | **passe** |
| `ofi_l10`@300s | − (−0,00745) | − (−0,00383) | **non** : [−0,00859 ; +0,00093] traverse 0 | **échoue** |
| `micro_dev`@60s | + (0,03133) | + (0,03230) | oui : [0,02639 ; 0,03822] | **passe** |
| `micro_dev`@300s | + (0,01349) | + (0,01300) | oui : [0,00640 ; 0,01959] | **passe** |
| `micro_dev`@900s | + (0,01186) | + (0,00882) | oui : [0,00152 ; 0,01613] | **passe** |

`ofi_l10`@300s survit Holm en TRAIN mais **échoue la condition 2** : le signe est stable mais l'intervalle de confiance OOS traverse zéro. Cellule non porteuse, à ce titre seul.

### 4.3 Condition 3 — gate économique `M(X,h) ≥ 0,80` tick ES

Pour les 4 cellules restantes après condition 2 (`ofi_l10`@60s, `micro_dev`@60s/300s/900s), `move_ticks_es` (TRAIN et OOS) reste très en-deçà de 0,80 :

| Cellule | `move_ticks_es` TRAIN | `move_ticks_es` OOS | Gate 0,80 franchi ? |
|---|---|---|---|
| `ofi_l10`@60s | 0,0484 | 0,0342 | non |
| `micro_dev`@60s | 0,1602 | 0,1651 | non |
| `micro_dev`@300s | 0,1458 | 0,1415 | non |
| `micro_dev`@900s | **0,2112** | **0,1670** | non |

**Aucune des 4 ne franchit le gate.** L'écart est large (facteur ~3,8 en TRAIN, ~4,8 en OOS pour la meilleure, `micro_dev`@900s) — condition 3 échoue sans ambiguïté, pas au bord.

### 4.4 Cas particulier — `book_imb`@900s en OOS, seule ligne `gate_es_pass=True`

C'est la **seule** des 48 lignes où `gate_es_pass` vaut `True` (`move_ticks_es` = 1,438, largement au-dessus de 0,80). Elle échoue néanmoins les deux autres conditions :
- **Condition 1 :** non retenue par Holm en TRAIN (`p_value` = 0,4457, `holm_reject` = False).
- **Condition 2 :** signe TRAIN **positif** (+0,01692) vs signe OOS **négatif** (−0,07596) — **inversion de signe**, condition automatiquement violée indépendamment de tout calcul d'IC95.

C'est exactement le rôle que la condition de stabilité de signe (§7.1.2) est censée jouer : un gate économique franchi ne suffit pas si le signe ne survit pas au split TRAIN/OOS. Cette ligne ne fait basculer aucun verdict.

### 4.5 Verdict

**Aucune cellule ne satisfait les trois conditions cumulatives à un horizon ≥ 60 s.**

# **H0 NON REJETÉE. STOP** (au sens de §7.4 — pas de GO, pas de GO conditionnel).

---

## 5. Résultat principal — invariance du gain économique selon l'horizon

Le point le plus notable de ce rapport n'est pas qu'aucune cellule ne passe le gate, mais **la façon dont elle en reste systématiquement loin**, à tous les horizons, pour l'indicateur le plus informatif.

`move_ticks_es`, `micro_dev`, TRAIN, par horizon (1/5/30/60/300/900 s) :

**0,1461 / 0,1585 / 0,1696 / 0,1602 / 0,1458 / 0,2112**

Sur la même série : `ic` chute de 0,2029 (1 s) à 0,0119 (900 s), soit un **facteur ~17,1**. Dans le même temps, `sd_ret_ticks_es` (l'écart-type du rendement à horizon `h`) croît de 0,902 à 22,32, soit un **facteur ~24,7** — la volatilité du rendement croît presque au même rythme que l'IC décroît (`move = |ic| × K × sd_ret_ticks`). Le produit des deux ne s'effondre donc pas : il reste confiné entre **0,146 et 0,211 tick ES** sur les six horizons, un facteur ~1,45 de bout en bout — contre un facteur 17 pour l'IC seul.

**Conséquence opérationnelle, non une conclusion statistique en soi :** changer d'horizon ne peut pas fermer l'écart avec le gate (0,80 tick). L'horizon **n'est pas** le paramètre limitant de ce résultat — le déclin classique de l'IC avec l'horizon est presque exactement compensé par la hausse de la volatilité du rendement, si bien qu'aucune fenêtre temporelle testée n'approche le seuil économique.

---

## 6. Nature du négatif — à distinguer explicitement de QDE-021

**QDE-021 :** la distribution des p-values sur les 30 partitions calendaires/pair-trading était indiscernable du bruit — la plus petite p de tout l'axe (0,0385) échouait déjà un Holm par résolution, sans réplication H1↔M5.

**QDE-022 : ce n'est pas le même type de négatif.** Le signal est **réel**, **très significatif** (`micro_dev`@1s : `p_value` = 0,0 en TRAIN, 0,0 en OOS — en dessous de la précision flottante), et **se réplique hors échantillon avec une magnitude quasi identique** : `ic` = 0,2029 en TRAIN contre 0,2039 en OOS pour `micro_dev`@1s (accord à la 2ᵉ décimale, sens et ordre de grandeur identiques). Il échoue uniquement — et systématiquement, à tous les horizons ≥ 60 s — sur le **seuil économique**.

**Formulation à retenir :** ce n'est pas « il n'y a rien », c'est **« il y a quelque chose, et c'est trop petit »**.

---

## 7. Limites et menaces à la validité

**a) Le gate de 0,80 tick était généreux, pas strict — et H1 échoue quand même.** Le gate gelé (§7.3) correspond à `2 × 0,40 tick` de **commissions seules** (coût AR ES ≈ 0,36–0,40 tick, borne haute). Un signal directionnel réel implique de prendre la liquidité au marché plutôt que de la poser, donc de payer le spread en plus des commissions — environ 1 tick de plus par aller-retour sur ES. Un gate réaliste incluant le spread serait de l'ordre de `2 × (0,40 + 1,0) = 2,8 ticks` — **3,5× le gate gelé**, ce qui rendrait l'écart avec `micro_dev`@900s (0,21/0,17 tick) encore plus large (facteur ~13 à 17 au lieu de ~4 à 5). **Le gate n'est pas amendé** — on ne déplace pas un seuil après avoir vu les résultats, même dans le sens défavorable au signal. Ce point renforce le STOP, il ne le remet pas en cause.

**b) Les 4 indicateurs ne sont pas indépendants.** `micro_dev` et `book_imb` mesurent tous deux, sous des formes différentes, le déséquilibre de la file d'attente au sommet du carnet. La correction de Holm sur 24 tests est donc conservatrice d'un côté (elle traite ces tests comme indépendants alors qu'ils sont corrélés) et optimiste de l'autre (une vraie découverte sur l'un des deux indicateurs corrélés « coûte » une pénalité Holm complète). Aucun ajustement n'est appliqué ici — signalé pour QDE-023 ou une réplication future.

**c) L'IC élevé de `micro_dev` à 1 s est en partie mécanique.** Le micro-prix est, par construction, une estimation du prix d'équilibre pondérée par la liquidité aux deux extrémités du carnet — il est donc structurellement un meilleur prédicteur du prochain `mid` que le `mid` courant lui-même, à très court horizon. Une partie de l'IC = 0,20 à 1 s reflète cette propriété arithmétique de la définition, pas nécessairement une information nouvelle sur le flux d'ordres. Ce n'est pas nécessairement de l'alpha.

**d) `ofi_l10` est négatif à tous les horizons, dans les deux échantillons (TRAIN et OOS).** Explication plausible, **non vérifiée dans ce rapport** : la somme à poids égaux sur 10 niveaux est dominée par les niveaux profonds (indices 2 à 9), qui portent des tailles bien supérieures au niveau 0 ; le flux profond est connu dans la littérature (Cont–Kukanov–Stoikov et suites) pour être davantage contrarien que le flux au sommet. Consigné comme observation, pas comme correction — aucune repondération n'est appliquée (interdit par §9, variante paramétrique hors gel).

**e) Latence supposée nulle (menace déjà déclarée en v1, §8.2), rappelée ici avec un chiffre concret.** Un acteur retail opérant depuis l'Europe subit de l'ordre de ~100 ms de latence aller simple vers Aurora (Databento/CME). Ce délai est très supérieur au plus petit horizon testé (1 s) et non négligeable même à 5-30 s. **Ce rapport conclut donc sur une borne supérieure de l'exploitabilité théorique** — toute latence réelle non nulle ne peut que réduire, jamais augmenter, l'IC exploitable rapporté ici.

**f) Un seul mois, un seul instrument, un seul schéma.** Août 2026 est un mois de volume estival réduit pour les indices actions US (constaté, non quantifié plus avant ici). Aucune généralité saisonnière ou de régime n'est établie par ce rapport (menace déjà déclarée en v1, §8.1).

**g) Risque résiduel documenté en v4 §8.7, rappelé sans être ré-instruit ici.** Le job Databento `parent` non conforme (`GLBX-20260903-7XE859LGPE`) a téléchargé de vraies données ESU6/août 2026 environ 34 h 28 avant l'existence du tag `qde-022-prereg-v3` couvrant cette portée — ce qui expose potentiellement le **choix de la période/du contrat** (pas les indicateurs, horizons, Holm ou gate, gelés depuis v1, avant ce job). Risque assumé et documenté en v4 §8.7, pas neutralisé par ce rapport.

**h) Observation additionnelle sur la colonne `move_ticks_mes` du CSV (non un résultat, une note de lecture) :** le pré-enregistrement (§7.3) établit qu'un même mouvement d'indice vaut le **même nombre de ticks** sur ES et sur MES (seule la valeur en dollars diffère, ratio 10×, puisque les deux partagent un tick de 0,25 point). Le script calcule `move_ticks_mes = move_ticks_es × 10`, ce qui correspond au ratio **dollar**, pas au ratio de **nombre de ticks** tel qu'établi en §7.3. Sans incidence sur le verdict de ce rapport (le gate primaire est en ticks ES, §7.3, et aucune cellule n'en est proche) — signalé pour information, non corrigé ici (script non modifié, conformément à la consigne).

---

## 8. Ce que ce résultat exclut, et ce qu'il n'exclut pas

**Exclu par ce résultat :** acheter un mois supplémentaire de données L2 sur ES/`ESU6` ; tester d'autres horizons dans la même bande temporelle ; chercher une variante paramétrique de déséquilibre de carnet (profondeur, fenêtre OFI). **Le facteur limitant démontré ici est le coût de transaction (le gate), pas la taille de l'échantillon ni le choix d'horizon** — la §5 montre qu'aucun horizon ne s'en approche, et l'échantillon (514 786 points, réplication OOS à la 2ᵉ décimale) est déjà assez grand pour détecter un IC bien plus petit que ceux mesurés ici (cf. MDE, `QDE-022_DataIntegrity_Check.md` §7 / pré-enregistrement §6.4).

**Non exclu par ce résultat :** d'autres classes de signaux (options, cross-market profond, alternative data) ; d'autres marchés ou horizons fondamentalement différents ; une structure de coût différente (market maker, colocalisation). Ce rapport **ne conclut pas** sur la ligne recherche d'edge dans son ensemble — même correction de portée que celle déjà appliquée à la conclusion de QDE-021.

---

## 9. Conclusion en portée étroite

> Le carnet d'ordres de l'ES contient une information statistiquement robuste et reproductible hors échantillon sur le prix futur. Son contenu économique est d'environ 0,15 à 0,21 tick ES, invariant selon l'horizon, soit quatre à cinq fois moins que le seuil d'exploitabilité retenu, avant même de tenir compte de la latence.

**Conséquence recommandée : le système reste en SIGNAL_ONLY.**

---

## 10. Interdictions respectées

- **Aucune modification du pré-enregistrement.** `QDE-022_OrderFlow_L2_PreRegistration.md` (v4, tag `qde-022-prereg-v4`) n'est pas touché par ce rapport.
- **Aucun nouveau calcul.** Tous les chiffres de ce document proviennent de `qde022_ic_curve_results.csv`, déjà commité (commit `f8b6775`) — aucune ré-exécution des scripts.
- **Aucun paramètre gelé modifié.** Le gate (0,80 tick ES), les 4 indicateurs, les 6 horizons, Holm α = 0,05 et la règle de décision de §7 sont appliqués tels quels, y compris lorsqu'un gate plus réaliste (§7a) aurait pu être invoqué en faveur du STOP — non amendé.
- **Aucun backtest, P&L, Sharpe ou courbe d'équity.**
- **Verdict ni adouci ni dramatisé.** H0 non rejetée ; formulation « il y a quelque chose, et c'est trop petit », pas « il n'y a rien » et pas « edge caché qu'on n'a pas su capturer ».
- **La ligne recherche d'edge dans son ensemble n'est pas déclarée close** — seule la portée de QDE-022 (ES MBP-10, août 2026, ≥ 60 s) est tranchée.

---

## 11. Index des fichiers produits

| Fichier | Rôle | Committable en production ? |
|---|---|---|
| `IQIAIndicator/Documentation/Scientific/QDE-022-R_OrderFlow_L2_Results.md` | Ce rapport | Oui (documentation) |
| `IQIAIndicator/Tests/Research/OrderFlow/QDE-022_OrderFlow_L2_PreRegistration.md` | Pré-enregistrement gelé (v4), référencé, non modifié | Oui (documentation) |
| `IQIAIndicator/Tests/Research/OrderFlow/QDE-022_DataIntegrity_Check.md` | Vérification d'intégrité (CONFORME), référencée, non modifiée par ce rapport | Oui (documentation) |
| `IQIAIndicator/Tests/Research/OrderFlow/qde022_build_grid.py` | Sonde étape 1, déjà commitée | Recherche uniquement |
| `IQIAIndicator/Tests/Research/OrderFlow/qde022_ic_curve.py` | Sonde étape 2, déjà commitée | Recherche uniquement |
| `IQIAIndicator/Tests/Research/OrderFlow/qde022_ic_curve_results.csv` | Résultats mesurés (source unique des chiffres de ce rapport) | Oui (documentation) |

Grilles Parquet (`QDE-022_Data/grid_1s`, 27 fichiers) et données brutes DBN : hors dépôt, régénérables à partir des scripts et du job Databento cité en §2.

---

## 12. FINAL OUTPUT

**STATUS :** Terminé. **QDE-022 : NÉGATIF au sens du gate économique.** H1 (§1.2) rejetée sur aucune cellule à horizon ≥ 60 s malgré 5 cellules survivant Holm(24) en TRAIN et 4 d'entre elles stables en signe OOS (`ofi_l10`@60s, `micro_dev`@60s/300s/900s) — toutes les 4 restent 3,8 à 4,8× sous le gate de 0,80 tick ES, y compris la meilleure (`micro_dev`@900s : 0,211 TRAIN / 0,167 OOS). Signal réel, très significatif, répliqué hors échantillon — pas un résultat indiscernable du bruit (à la différence de QDE-021) — mais économiquement insuffisant, à tous les horizons testés, l'invariance de `move_ticks_es` (0,146–0,211 tick) montrant que l'horizon n'est pas le paramètre limitant.

**PRODUCTION MODIFIED :** NO. Aucun code de production touché. Aucune entrée `YahooSymbolMap`. Pré-enregistrement v4 non modifié.

**TESTS :** aucun test automatisé — rapport de résultats fondé sur `qde022_ic_curve_results.csv`, déjà produit et commité.

**DOCUMENTATION :** ce rapport — `IQIAIndicator/Documentation/Scientific/QDE-022-R_OrderFlow_L2_Results.md`.

**NEXT ACTION :** Recommandation — système en **SIGNAL_ONLY**. Aucun achat supplémentaire de données L2 sur ES ; aucun nouvel horizon ; aucune variante de déséquilibre de carnet. La question ouverte (facteur d'exploitabilité manquant × 4 à 5, avant latence) n'est pas résolue par un ajustement de paramètre gelé — un gate plus réaliste (§7a) l'aggraverait. Toute reprise de la ligne recherche d'edge sur ES intraday exige une classe de signal, un marché ou une structure de coût fondamentalement différente, pas une itération supplémentaire sur le carnet L2 d'ES.
