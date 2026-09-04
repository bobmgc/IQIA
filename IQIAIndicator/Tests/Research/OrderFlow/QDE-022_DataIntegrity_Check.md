# QDE-022 — Vérification d'intégrité des données livrées (v3)

**Date :** 2026-09-04
**Portée vérifiée :** job Databento `GLBX-20260904-93PSB55DT3` (`GLBX.MDP3` / `mbp-10` / `ESU6` / `stype_in=raw_symbol` / 2026-08-02 → 2026-09-02), livré dans `c:\Users\rnbch\OneDrive\Bureau\QDE-022_Data\GLBX-20260904-93PSB55DT3\` (hors dépôt).
**Document de référence :** `QDE-022_OrderFlow_L2_PreRegistration.md`, tag `qde-022-prereg-v3`, commit `2dd383a013e0f2c2a4f493703a73fbb9f45c90e1`.
**Type :** vérification de conformité uniquement. **Aucun calcul d'OFI, d'IC, de rendement ou de statistique de prix n'a été effectué.**

---

## Verdict global

# **NON CONFORME**

Ce verdict porte sur le **processus de pré-enregistrement dans son ensemble**, pas sur la structure du jeu de données livré. À noter explicitement :

- Le **jeu de données lui-même** (job `GLBX-20260904-93PSB55DT3`) est **structurellement conforme** à la portée gelée v3 sur tous les champs vérifiés (contrôles 1, 2, 3, 5, 6 : CONFORME ; contrôle 4 : ÉCART MINEUR non bloquant).
- Le motif du NON CONFORME est un **constat découvert pendant ce contrôle, au §8 ci-dessous** : un job antérieur non conforme (`stype_in=parent`) a téléchargé de **vraies données de marché ESU6/août 2026** le **2026-09-03**, soit **avant** l'existence de tout tag couvrant cette portée (`qde-022-prereg-v3`, créé le 2026-09-04). Cela contredit factuellement l'affirmation « choix a priori » de v3 §8.7. Voir §8 et §9 pour le détail et la correction proposée.

**Aucune analyse (OFI, IC, gate économique) n'est lancée à la suite de ce rapport**, conformément à la consigne, quel que soit le verdict.

---

## Tâche annexe — commit QDE-013 à QDE-021

Vérifié avant les contrôles : le commit demandé existait déjà dans l'historique (`git log` / `git fetch origin` concordants), poussé sur `origin/main`.

- **Commit :** `bbe3266599f41f212f31d6324ed11c543201c82e` — *"research: commit QDE-012 to QDE-021 documents and test probes"*
- **Contenu :** 33 fichiers, 8032 insertions (documents `QDE-012` à `QDE-021` + sondes de test associées).
- **Fichiers volumineux (>10 Mo) dans ce commit :** `git ls-tree -r -l bbe3266` filtré `>10485760` octets → **aucun**.
- **État :** déjà présent sur `origin/main` (`git log origin/main --oneline` le confirme), aucune action de commit/push supplémentaire nécessaire de ma part.

---

## Contrôle 1 — Conformité au gel (`metadata.json`)

| Champ | `metadata.json` | Portée gelée v3 (§2.1/§2.2) | Verdict |
|---|---|---|---|
| `dataset` | `GLBX.MDP3` | `GLBX.MDP3` | conforme |
| `schema` | `mbp-10` | `mbp-10` | conforme |
| `symbols` | `["ESU6"]` | `["ESU6"]` | conforme |
| `stype_in` | `raw_symbol` | `raw_symbol` | conforme |
| `stype_out` | `instrument_id` | (non spécifié explicitement en v3, valeur par défaut du SDK) | conforme |
| `start` | `1785628800000000000` ns = **2026-08-02T00:00:00Z** | 2026-08-02T00:00:00Z | conforme |
| `end` | `1788307200000000000` ns = **2026-09-02T00:00:00Z** | 2026-09-02T00:00:00Z | conforme |
| `encoding` | `dbn` | `dbn` | conforme |
| `compression` | `zstd` | `zstd` | conforme |
| `split_duration` | `day` | (non figé explicitement — valeur par défaut du SDK, cohérente avec la livraison en fichiers journaliers attendue) | conforme |
| `map_symbols` | `false` | (non figé explicitement — cohérent avec `stype_out=instrument_id`, aucune résolution de symbole appliquée en amont) | conforme |

**Aucun champ ne diverge.** Contrôles suivants poursuivis.

**Verdict contrôle 1 : CONFORME.**

---

## Contrôle 2 — Unicité du contrat

`symbology.json` **n'a pas été produit** par ce job (delivery standard sans requête symbology séparée). Le mapping symbole → `instrument_id` est cependant présent en clair dans l'en-tête de **chaque** fichier `.dbn.zst` (champ `metadata.mappings`, lu via `databento.DBNStore`). Les 27 fichiers ont été inspectés individuellement :

- **`symbols` déclaré par fichier :** `['ESU6']` — identique sur les 27 fichiers.
- **`instrument_id` mappé par fichier :** `{'42140870'}` — **un seul et même `instrument_id`, identique sur les 27 fichiers**, aucune variation sur le mois (cohérent avec un contrat unique, sans roulement, sur toute la fenêtre).
- **`partial` / `not_found` (SDK) :** vides sur les 27 fichiers — aucun symbole partiellement résolu ou introuvable.
- **Aucun symbole de type spread calendaire** (`ESU6-ESZ6` ou similaire) : absent, seul `ESU6` apparaît sur l'ensemble des 27 fichiers.

C'est exactement le contrôle qui garantit que le problème du job `parent` précédent (`GLBX-20260903-7XE859LGPE`, `symbols=ES.FUT`, qui aurait pu renvoyer plusieurs contrats ES simultanément) **ne se reproduit pas** dans cette livraison.

**Verdict contrôle 2 : CONFORME.**

---

## Contrôle 3 — Inventaire des jours

Fenêtre gelée : 2026-08-02 → 2026-09-01 inclus (32 dates calendaires ; `end` exclusif au 2026-09-02).

**27 fichiers journaliers livrés :**
2026-08-02, 03, 04, 05, 06, 07, 09, 10, 11, 12, 13, 14, 16, 17, 18, 19, 20, 21, 23, 24, 25, 26, 27, 28, 30, 31, 2026-09-01.

**Dates absentes (5 sur 32) — toutes des samedis, jour de semaine indiqué :**

| Date | Jour |
|---|---|
| 2026-08-01 | Samedi *(hors fenêtre effective — avant le premier fichier livré, la requête démarre au 2026-08-02)* |
| 2026-08-08 | Samedi |
| 2026-08-15 | Samedi |
| 2026-08-22 | Samedi |
| 2026-08-29 | Samedi |

**Aucun jour de semaine (lundi–vendredi) ne manque.** Les 22 jours ouvrés de la fenêtre (03, 04, 05, 06, 07, 10, 11, 12, 13, 14, 17, 18, 19, 20, 21, 24, 25, 26, 27, 28, 31 août + 1er septembre) sont tous présents.

**Dates présentes mais de taille anormalement faible (< 20 % de la médiane des tailles de fichier, jours ouvrés) :** médiane (22 fichiers ouvrés) = 188 103 686 octets compressés ; seuil 20 % = 37 620 737 octets. **Aucun fichier de jour ouvré sous ce seuil** (plage observée : 64,3 % à 140,1 % de la médiane).

**Dimanches présents (5) mais de taille très faible — attendu, non anormal :** 2026-08-02, 09, 16, 23, 30 (1,3 à 5,6 Mo compressés, 0,7 % à 3,0 % de la médiane des jours ouvrés). Cohérent avec une session Globex du dimanche soir entièrement hors de la fenêtre RTH gelée (13:30–20:00 UTC, §2.1) — aucune intervention manuelle, aucun filtrage appliqué ici, simple constat.

**Verdict contrôle 3 : CONFORME.** Aucun jour ouvré manquant ni anormalement petit.

---

## Contrôle 4 — Qualité déclarée (`condition.json`)

31 dates couvertes (2026-08-02 → 2026-09-01), toutes `available` **sauf une** :

| Date | Condition |
|---|---|
| 2026-08-29 | **`degraded`** |
| (30 autres dates de la fenêtre) | `available` |

**Aucun jour ouvré n'est marqué `degraded` ou `missing`.** Le seul statut non-`available` (2026-08-29) tombe sur un **samedi**, absent des fichiers livrés (contrôle 3) — cohérent avec une anomalie ponctuelle sur un jour sans session RTH exploitable.

**Recoupement avec le job `parent` précédent :** conforme à ce qu'indiquait l'utilisateur, **le même statut `degraded` sur 2026-08-29 se retrouve ici**, sur une livraison `raw_symbol` indépendante. Le signal semble donc attaché à la donnée d'échange sous-jacente elle-même (assessment Databento de la qualité de la journée CME), pas à la voie d'acquisition (`parent` vs `raw_symbol`) — constat, pas d'interprétation plus poussée demandée ici.

**Verdict contrôle 4 : ÉCART MINEUR** — constaté, sans impact sur un jour ouvré, donc sans impact sur H0/H1 tel que testé (aucun horizon ni indicateur n'est mesuré sur un samedi).

---

## Contrôle 5 — Comptage des enregistrements

Comptage exact par fichier (itération complète des 27 `.dbn.zst`, ~194 s cumulées) :

| Date | Jour | `record_count` |
|---|---|---|
| 2026-08-02 | Dim | 220 713 |
| 2026-08-03 | Lun | 8 257 020 |
| 2026-08-04 | Mar | 8 960 902 |
| 2026-08-05 | Mer | 10 454 003 |
| 2026-08-06 | Jeu | 10 910 265 |
| 2026-08-07 | Ven | 9 277 024 |
| 2026-08-09 | Dim | 117 635 |
| 2026-08-10 | Lun | 6 752 834 |
| 2026-08-11 | Mar | 6 670 883 |
| 2026-08-12 | Mer | 6 219 159 |
| 2026-08-13 | Jeu | 6 079 639 |
| 2026-08-14 | Ven | 5 067 446 |
| 2026-08-16 | Dim | 55 132 |
| 2026-08-17 | Lun | 5 162 090 |
| 2026-08-18 | Mar | 7 987 830 |
| 2026-08-19 | Mer | 9 456 379 |
| 2026-08-20 | Jeu | 9 667 659 |
| 2026-08-21 | Ven | 6 680 759 |
| 2026-08-23 | Dim | 115 176 |
| 2026-08-24 | Lun | 7 717 165 |
| 2026-08-25 | Mar | 6 770 221 |
| 2026-08-26 | Mer | 6 491 007 |
| 2026-08-27 | Jeu | 8 512 037 |
| 2026-08-28 | Ven | 10 717 460 |
| 2026-08-30 | Dim | 212 891 |
| 2026-08-31 | Lun | 7 284 718 |
| 2026-09-01 | Mar | 9 530 773 |

**Somme totale : 175 348 820 = `record_count` facturé par le job (`get_job_details`), exactement.** Aucune perte, troncature ni double-comptage entre la facturation Databento et le contenu réel des fichiers.

**Statistique sur les 22 jours ouvrés uniquement** (les 5 dimanches, population non comparable — session nocturne partielle — sont exclus de ce calcul, reportés à titre de contexte) :
- Médiane : 7 852 498 — Moyenne : 7 937 603 — Écart-type (population) : 1 717 223.
- **Écart-type maximal observé sur un jour ouvré : 1,73σ** (2026-08-06, 10 910 265 enregistrements). **Aucun jour ouvré au-delà de 3σ.**

**Verdict contrôle 5 : CONFORME.** Aucune anomalie de comptage signalée, aucune exclusion appliquée.

---

## Contrôle 6 — Coût et licence

| Poste | Montant | Détail |
|---|---|---|
| `get_cost` estimé (portée v3) | **30,048361867666 USD** | appel du 2026-09-04 (avant tag v3 — déviation déjà consignée dans le document gelé, §2.2) |
| Coût réellement facturé (job `GLBX-20260904-93PSB55DT3`) | **30,048361867666 USD** | identique à l'estimation au centime près (`cost_usd` du job, `state=done`) |
| Job précédent non conforme (`GLBX-20260903-7XE859LGPE`, `stype_in=parent`, `symbols=ES.FUT`) | **33,13424949347973 USD** | terminé 2026-09-03T08:11:37Z — **hors portée v3**, consigné comme dépense engagée, voir §8 pour l'implication méthodologique (pas seulement comptable) |
| **Total Databento à ce jour** | **63,18261136114597 USD** | somme des deux jobs GLBX.MDP3 ci-dessus (+ 3 jobs d'exemple MSFT à 0 USD, hors périmètre IQIA, non comptés) |
| Statut de licence CME | **TODO, non résolu** | inchangé depuis v3 §2.3 — questionnaire Databento non complété à ce jour |

**Verdict contrôle 6 : CONFORME** sur la structure comptable du job vérifié (coût exact, cohérent avec le gel) ; **voir §8** pour ce que la simple présence du job `parent` révèle sur l'antériorité par rapport au tag v3.

---

## Contrôle 7 — MDE (effet minimal détectable)

Calculé et **inséré dans `QDE-022_OrderFlow_L2_PreRegistration.md` §6.4** (seul TODO du document modifié, comme demandé — la section porte le numéro §6.4 dans le document réel ; le message de tâche la référençait comme « §9 », correction de renvoi apportée dans ce rapport).

Méthode : `n_eff(h)` = borne supérieure théorique = (23 400 s RTH / `h`) × 22 jours ouvrés, sans rejet de carnet croisé/bord de session (donnée non disponible sans rejouer l'état du carnet — hors périmètre de ce contrôle). MDE via approximation Fisher-z, puissance 0,80, deux bornes d'`alpha` : 0,05 non corrigé (indicatif) et 0,05/24 (borne Holm la plus stricte possible sur la famille de 24 tests).

| `h` (s) | `n_eff` (majorant) | MDE (α=0,05) | MDE (α=0,05/24) |
|---|---|---|---|
| 1 | 514 800 | 0,0039 | 0,0055 |
| 5 | 102 960 | 0,0087 | 0,0122 |
| 30 | 17 160 | 0,0214 | 0,0299 |
| 60 | 8 580 | 0,0302 | 0,0423 |
| 300 | 1 716 | 0,0676 | 0,0944 |
| 900 | 572 | 0,1169 | 0,1629 |

**Rappel :** ce tableau documente la puissance statistique de l'échantillon ; il n'entre dans **aucun** critère de décision de §7 (le seuil reste économique, `M(X,h) ≥ 0,80` tick ES, §7.3).

---

## §8 — Constat critique : antériorité du job `parent` par rapport au tag v3

**Chronologie établie via `batch.list_jobs` et `git log` :**

| Horodatage (UTC) | Événement |
|---|---|
| 2026-09-01 | v1/v2 gelés et taggés (portée : avril 2026, `ESM26`) |
| **2026-09-03T07:43:23Z** | **Job `GLBX-20260903-7XE859LGPE` soumis** — `stype_in=parent`, `symbols=ES.FUT`, `mbp-10`, **2026-08-02 → 2026-09-02** (déjà la fenêtre août/ESU6) |
| 2026-09-03T08:11:37Z | Job `parent` terminé — **vraies données de marché ESU6 (parmi d'autres contrats ES) téléchargées**, 193 356 682 enregistrements, 33,13 USD facturés |
| 2026-09-04T18:39:53Z | **Commit `2dd383a` (v3) créé** — première fois qu'un tag/document couvre explicitement la portée août 2026 / `ESU6` |

**Le job `parent` a livré de vraies données de marché sur la portée exacte (`ESU6`, `mbp-10`, 2026-08-02 → 2026-09-02) environ 35 heures avant que cette portée n'existe sous forme de pré-enregistrement gelé.**

Cela contredit factuellement l'affirmation de v3 §8.7 : *« Août 2026 est choisi a priori […] aucune donnée n'a été acquise pour avril ni pour août avant ce changement, donc ce n'est pas un re-choix après résultat. »* Cette phrase, écrite lors de la rédaction de v3 (2026-09-04), **était incorrecte** au moment où elle a été écrite : des données réelles sur cette portée existaient déjà depuis la veille.

**Portée exacte du risque, sans l'exagérer :**
- Les **4 indicateurs (§3), les 6 horizons (§4), la comptabilité Holm (§5) et la règle de décision incluant le gate économique (§7)** sont gelés **depuis v1 (2026-09-01)** — **avant** l'existence même du job `parent` (2026-09-03). Aucun de ces paramètres n'a donc pu être choisi ou ajusté après exposition à de vraies données.
- Le risque est **circonscrit au choix de la période et du contrat** (août 2026 / `ESU6`) — exactement le paramètre que v3 a changé, et exactement celui pour lequel l'affirmation « a priori » est maintenant invérifiable : il n'est pas possible d'exclure, sur la seule base des logs disponibles, qu'une personne ait consulté un aperçu des données `parent` avant de proposer ce changement de portée à v3.
- Ce contrôle d'intégrité **ne peut pas trancher** cette question (elle porte sur l'intégrité du processus de décision humain/agent, pas sur les fichiers de données) — elle est donc **rapportée, pas résolue**, conformément au mandat de ce document (vérification de conformité, aucune modification du pré-enregistrement au-delà du seul TODO §6.4).

**Ce constat n'a pas été corrigé dans `QDE-022_OrderFlow_L2_PreRegistration.md`** au-delà du TODO §6.4 explicitement autorisé — corriger l'affirmation erronée de §8.7 constituerait une modification de fond d'un document gelé, hors du mandat de cette vérification, et doit faire l'objet d'un amendement explicite et tagué (v4), pas d'une correction silencieuse.

---

## §9 — Recommandation

Un amendement **v4** est recommandé avant toute exécution de la sonde d'analyse (`qde022_l2_ofi.py`), avec deux volets :

1. **Correction factuelle obligatoire** de v3 §8.7 : remplacer l'affirmation « choix a priori, aucune donnée acquise » par la chronologie exacte ci-dessus (§8 de ce rapport).
2. **Décision explicite de l'utilisateur, à trancher avant analyse**, entre :
   - **(a) Accepter le risque résiduel**, documenté comme menace à la validité dans QDE-022-R, au motif que seul le choix période/contrat est exposé (indicateurs, horizons, gate figés depuis v1, avant le job `parent`) — et poursuivre l'analyse sur le jeu de données déjà conforme (`GLBX-20260904-93PSB55DT3`) ;
   - **(b) Écarter le risque entièrement** en basculant vers la période de repli déjà prévue en v3 (**juillet 2026, `ESU6`**), qui n'a **jamais** fait l'objet du job `parent` ni d'aucun autre téléchargement à ce jour — nécessite un nouveau `get_cost` + `batch.submit_job` sous un nouveau tag `qde-022-prereg-v4`.

Ce choix relève d'une décision de portefeuille de recherche, pas d'un contrôle technique — il n'est pas tranché par ce rapport.

---

## Tableau récapitulatif des verdicts

| Contrôle | Verdict |
|---|---|
| 1. Conformité au gel (`metadata.json`) | CONFORME |
| 2. Unicité du contrat | CONFORME |
| 3. Inventaire des jours | CONFORME |
| 4. Qualité déclarée (`condition.json`) | ÉCART MINEUR (non bloquant) |
| 5. Comptage des enregistrements | CONFORME |
| 6. Coût et licence | CONFORME (structure), voir §8 |
| 7. MDE | Renseigné (§6.4 du pré-enregistrement) |
| **Constat §8 (antériorité job `parent`)** | **NON CONFORME (processus)** |

**Verdict global : NON CONFORME**, du seul fait du constat §8 — au niveau du **processus** de pré-enregistrement, pas de la structure du jeu de données lui-même. **Aucune analyse (OFI/IC/gate) n'est autorisée par ce rapport** ; elle reste subordonnée à la décision utilisateur demandée en §9 et, le cas échéant, à un amendement v4 taggé.
