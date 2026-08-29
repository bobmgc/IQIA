# QDE-012 — Sprint 15.25 — Lot 16.1 — StructuralBreak Confidence Decoupling Audit

**Date** : 2026-08-27
**Branche** : `feature/structural-stability-v2`
**Lot précédent** : 16 (StructuralBreak Strength Contract Repair)
**Type** : Audit + tentative de réparation du canal `Confidence` (distinct de `Strength`)
**Statut** : **CONFIDENCE CONTRACT — UNRESOLVED (documenté, non corrigé)** — production **non modifiée**

---

## 1. Objective

Déterminer scientifiquement si `FusionDimension.StructuralBreak.Confidence` peut/doit être dérivée
d'une source **distincte** de `Strength`, et si oui l'implémenter avec la rigueur du Lot 16 ; sinon,
documenter pourquoi et clore proprement.

Le but **n'est pas** de recalibrer `Strength` (traité au Lot 16), ni de toucher au reweighting, aux
Decision Rules, au Risk Engine, ni à aucune autre dimension.

---

## 2. Scope

**Ajouté (observationnel, ne modifie aucune production) :**

- `Tests/Research/Lot16ContractNormalization/Lot161ConfidenceSourceComparisonTests.cs` — Phase 0 (mesure
  `corr(Value, Confidence)` post-Lot 16, RAW + STABLE, déterminisme double-hash) + Phase 1 (évaluation
  des sources candidates).
- ce rapport.

**Production modifiée :** **AUCUNE.** `StructuralBreakEvidenceRule.cs` (et *a fortiori* `Strength`)
inchangé par ce lot ; `CusumStatistics.Compute` non touché ; aucune autre dimension.

**Fichiers protégés (identiques Lot 16) + `Strength` lui-même :** non rouverts.

**Interdictions respectées :** NO CALIBRATION, NO REWEIGHTING, NO OPTIMIZATION, NO DECISION RULE
CHANGE, NO RISK ENGINE CHANGE, NO ENTRY CHANGE, NO ATAS, NO ORDERS, NO DLL DEPLOYMENT, NO COMMIT.

---

## 3. Phase 0 — Verification of Lot 16 Outcome

### 3.1 Vérification du code (git + lecture)

- `git ls-files` : `Engine/Fusion/Rules/StructuralBreakEvidenceRule.cs` est **non suivi** (`??`).
  L'ensemble de la fonctionnalité StructuralBreak (Lots 15.6→16) est du travail **non commité** sur la
  branche `feature/structural-stability-v2` (dernier commit `b75ffa8`, antérieur). `git diff` ne montre
  donc rien pour ce fichier — mais les modifications du Lot 16 y sont **physiquement présentes** :
  - `BuildContract` : `double strength = NormalizeStrength(cusum);`
  - `NormalizeStrength(CusumResult)` : `return rawRatio / (1.0 + rawRatio);` avec
    `rawRatio = max(PositiveCusum, |NegativeCusum|) / Threshold`, garde `Threshold ≤ 0` ⇒ `0.0`.
  - `CusumStatistics.Compute` : **non modifié** (le `Math.Clamp` saturant y subsiste).
- Le FINAL OUTPUT du Lot 16 revendiquait `PRODUCTION MODIFIED: YES (StructuralBreakEvidenceRule.cs only)`
  et `COMMIT: NO`. **Conforme à la réalité observée** : le fichier de travail est modifié, rien n'est
  commité.

### 3.2 Contrat `Confidence` actuel (post-Lot 16)

`StructuralBreakEvidenceRule.Evaluate` (inchangé par le Lot 16) :

```csharp
FusionConfidence confidence = contract.IsAvailable
    ? new FusionConfidence
      {
          Value      = contract.Strength,
          Confidence = contract.Strength,     // ← recopie EXACTE de Value
          Explanation = contract.Explanation
      }
    : new FusionConfidence { Value = 0.0, Confidence = 0.0, IsAvailable = false, ... };
```

Le Lot 16 a changé **ce qu'est** `contract.Strength` (`r/(1+r)` au lieu de `Clamp`), mais
`.Value` **et** `.Confidence` reçoivent toujours le **même double** `contract.Strength`. La
duplication est **exacte, par construction**, et n'a pas été touchée.

### 3.3 Mesure empirique — `corr(Value, Confidence)` post-Lot 16 (dataset Yahoo réel)

`Lot161ConfidenceSourceComparisonTests` — même méthodologie que l'audit indépendant (5 règles de
production, même ordre, run isolé, déterminisme vérifié).

| Stage | Pearson(Value, Confidence) | Paires bit-identiques |
|---|---|---|
| **RAW** (post-`EvidenceFusionEngine`) | **1.000000000000000** | **11 428 / 11 428** |
| **STABLE** (post-`FusionStateManager`) | **1.000000000000000** | **11 428 / 11 428** |

Déterminisme : `Hash1 == Hash2 == 91b27313ff84a9bdcb5238674e1dafd5a72309da1316f3889d23edff04559f38`.

### 3.4 Décision de scope

```
corr(Value, Confidence) = 1.000  ≥ 0.98
⇒ PHASE 0 RESULT: CONFIRMED PERSISTENT
```

Le défaut `CONFIDENCE_DUPLICATES_VALUE` identifié §3.D / §7.1 de l'audit indépendant **persiste
intégralement** après le Lot 16, sur RAW comme sur STABLE. On continue en Phase 1.

---

## 4. Current Confidence Contract (post-Lot 16) — résumé

| Attribut | État |
|---|---|
| Formule | `Confidence = contract.Strength = r/(1+r)` (identique à `Value`) |
| corr(Value, Confidence) | 1.000 (exact, par construction) sur RAW et STABLE |
| Information transportée | une seule : la magnitude normalisée de la rupture CUSUM |
| Information **absente** | la fiabilité / qualité de la mesure (aucun second canal) |
| Cause structurelle | CUSUM n'expose pas de métrique de qualité indépendante (déjà noté Lot 15.7/15.8 : « CUSUM does not expose a second, separate quality metric the way ADF/KPSS/DFA do ») |

---

## 5. Correlation Measurement (before this lot)

Voir §3.3. **RAW = 1.000, STABLE = 1.000**, 11 428/11 428 paires bit-identiques dans les deux cas.
Dataset : `MES=F` M5, `BarCount=11556`, `Range 2026-06-29T17:20:00Z .. 2026-08-27T17:14:00Z`,
`Fingerprint=89F545C4119DE7D57D62D124E9FDA2A8C36771096588B8675988F27793E3E7D1`, `ReadyBars=11428`,
`Detected=10670`.

---

## 6. Candidate Sources Evaluated (Phase 1)

Grandeurs de `Evidence.Cusum` / `Evidence.BaiPerron` / du contrat qui **ne sont pas** le
ratio/magnitude de la rupture, et pourraient représenter une **qualité de mesure**. Mesurées sur les
11 428 barres réelles.

| Candidat | Définition (paramétrique-libre sauf mention) | Distinct | Sat % (≥0.9999) | Zéro % | Variance | **Pearson vs Strength** | Spearman vs `r` brut | % transitions |
|---|---|---|---|---|---|---|---|---|
| **A** `agreement` | Both→1.0 ; CusumOnly/BaiPerronOnly→0.5 ; Neither/Unavailable→0.0 | 2 | **93.4 %** | 0 | 0.0155 | **0.709** | 0.431 | 5.8 % |
| **B** `oldRampClamp` | `Math.Clamp(cusum.Confidence, 0, 1)` = `min(r, 1)` (valeur libérée par le Lot 16) | 749 | **93.4 %** | 0 | 0.0075 | **0.652** | 0.431 | 9.5 % |
| **C** `breakCountNorm` | `Clamp(BreakCountMagnitude / 8, 0, 1)` (Bai-Perron BreakCount ∈ 1..8) | 8 | 1.3 % | 0 | 0.0241 | **−0.023** | −0.023 | 18.1 % |
| **D** `sampleAdequacy` | `min(1, Cusum.SampleSize / 30)` (fenêtre CUSUM `RegimeEngine`) | **1** | 100 % | 0 | **0** | 0 | 0 | 0 % |
| **E** `locStableInstant` | `1.0` si `EstimatedBreakIndex == EstimatedBreakIndex(barre i−1)`, sinon `0.0` | 2 | 32.3 % | **67.7 %** | 0.219 | **−0.102** | −0.065 | 27.8 % |

Vérification « ne redérive pas Strength » (Phase 1) :

- **B** est littéralement `min(r, 1)` ; `Strength = r/(1+r)`. Même grandeur sous-jacente `r`, autre
  transformation bornée. `Pearson vs Strength = 0.652` ⇒ **redérive Strength**.
- **A** : `Both` ≈ « CUSUM a détecté » ≈ `Strength` élevé ; les 6.6 % `BaiPerronOnly` sont exactement
  les barres non détectées à `Strength` bas. `Pearson vs Strength = 0.709` ⇒ **suit l'état de
  détection**, donc `Strength`.
- **C**, **E** : `Pearson vs Strength` ≈ 0 ⇒ réellement découplés de `Strength`.
- **D** : constant ⇒ non applicable.

---

## 7. Constraints Check per Candidate (Phase 2)

Contraintes : (1) mesurablement distinct de `Value` (cible indicative `corr < 0.90` sur RAW) ;
(2) borné [0,1] ; (3) représente une **fiabilité de la mesure**, pas la magnitude de la rupture ;
(4) sans paramètre arbitraire ; (5) déterministe ; (6) sans look-ahead ; plus une exigence implicite
de **granularité adéquate / non dégénérescence**.

| Contrainte | A `agreement` | B `oldRamp` | C `breakCountNorm` | D `sampleAdequacy` | E `locStableInstant` |
|---|---|---|---|---|---|
| 1. Distinct de `Value` (`corr < 0.90` RAW) | ✗ **0.71** | ✗ **0.65** | ✓ (−0.02) | — (constant) | ✓ (−0.10) |
| 2. Borné [0,1] | ✓ | ✓ | ✓ | ✓ | ✓ |
| 3. Fiabilité, **pas** magnitude | ~ (corroboration, faible) | ✗ (= `min(r,1)`) | ✗ **magnitude** (nb de ruptures) | ✓ mais dégénéré | ✓ (stabilité de localisation) |
| 4. Sans paramètre arbitraire | ✗ (`0.5` pour « un seul test » = valeur non justifiée) | ✓ | ~ (`/8` = max Bai-Perron) | ✓ | ✓ |
| 5. Déterministe | ✓ | ✓ | ✓ | ✓ | ✓ |
| 6. Sans look-ahead | ✓ | ✓ | ✓ | ✓ | ✓ |
| **Granularité / non dégénérescence** | ✗ (2 niveaux, 93 % à 1.0) | ✗ (93 % à 1.0) | ✓ (8 niveaux, dynamique réelle) | ✗ (1 niveau, Var=0) | ✗ (binaire, 68 % à 0) |

**Aucun candidat ne satisfait simultanément toutes les contraintes.**

- **A `agreement`** : non découplé (`corr 0.71`), lui-même **93 % saturé** à 1.0, 2 niveaux. Remplacer
  une duplication exacte par un signal à 0.71 de corrélation, 93 % saturé et binaire n'est **pas** une
  amélioration scientifique. Le `0.5` pour « un seul test a déclenché » est arbitraire (pourquoi pas
  0.4 ou 0.6 ?).
- **B `oldRamp`** : redérive `Strength` (= `min(r,1)`), 93 % saturé. Rejeté par la règle Phase 1.
- **C `breakCountNorm`** : le **seul** candidat à la fois découplé (`corr −0.02`) et non dégénéré
  (8 niveaux, 18 % de transitions). **Mais** sa sémantique viole la contrainte 3 : `BreakCount` est
  le **nombre de points de rupture** trouvés par Bai-Perron — une *magnitude/quantité de changement
  structurel*, pas une *qualité de la mesure CUSUM*. Le Lot 15.9 a explicitement mis en garde :
  « Do NOT interpret a high BaiPerron.BreakCount as 'a stronger break' — not established by any data ».
  En faire la `Confidence` encoderait précisément cette interprétation non établie.
- **D `sampleAdequacy`** : **dégénéré** — `SampleSize` atteint le plafond de fenêtre (30) après le
  warm-up ⇒ constant 1.0 (même mode d'échec que la confiance `n/Window` d'ADF/KPSS, déjà signalée
  §3.D de l'audit indépendant).
- **E `locStableInstant`** : bonne **sémantique** (un point de rupture qui saute d'une barre à l'autre
  est une localisation moins fiable), découplé, sans paramètre, sans look-ahead. **Mais binaire**
  (2 niveaux), et il fonde un contrat de production sur l'égalité image-à-image de
  `EstimatedBreakIndex` — un champ que le Lot 15.7 a explicitement figé comme « diagnostic only, never
  used as a Freshness score », qui peut même valoir `SampleSize` en cas limite (Lot 15.6). Lui donner
  un rôle sémantique en production dépasse « extraire un champ déjà présent ».

---

## 8. Ablation

**Sans objet.** Aucune transformation retenue ⇒ aucune comparaison avant/après d'une nouvelle
`Confidence`. L'état reste : `corr(Value, Confidence) = 1.000` (RAW et STABLE).

---

## 9. Regression Analysis on Other 5 Dimensions

**Sans objet — production non modifiée.** Par construction, `Stationarity`, `Persistence`,
`MeanReversion`, `StructuralStability`, `RandomWalk` sont strictement inchangées (aucune ligne de
production touchée par ce lot). Le seul fichier de production concerné,
`StructuralBreakEvidenceRule.cs`, n'est pas modifié par le Lot 16.1.

---

## 10. Strength Unchanged Verification

**Sans objet — production non modifiée.** `contract.Strength` reste `NormalizeStrength(cusum) = r/(1+r)`
tel que posé par le Lot 16 ; ce lot ne touche pas `NormalizeStrength`, `BuildContract`, ni `Evaluate`.
La mesure Phase 0 confirme `Strength` (= `Value`) : parmi les barres détectées, min 0.500, médiane
0.937, max 0.9998, variance 0.0149 — identique aux mesures Lot 16.

---

## 11. Determinism

- Phase 0/1 : double agrégation ⇒ `Hash1 == Hash2 == 91b27313ff84a9bdcb5238674e1dafd5a72309da1316f3889d23edff04559f38`.
- `corr(Value, Confidence)` mesuré deux fois (build initial + rebuild), identique.
- Aucun RNG, aucun parallélisme, aucune dépendance à l'horloge dans le chemin mesuré.

---

## 12. Look-Ahead

- Toutes les grandeurs candidates sont des fonctions de la barre courante (`Cusum` / `BaiPerron` de la
  barre `i`), sauf `E` qui compare `EstimatedBreakIndex(i)` à `EstimatedBreakIndex(i−1)` — **strictement
  passé**, aucune barre future.
- Le harnais alimente `EvidenceFusionEngine` + `FusionStateManager` dans l'ordre chronologique, une
  barre à la fois, exactement comme la production.
- **Conclusion : aucune contamination temporelle**, ni dans la mesure Phase 0, ni dans l'évaluation
  des candidats.

---

## 13. Full Test Suite Result

`dotnet test` (aucun filtre) sur `IQIAIndicator.Tests.dll`, `--blame-hang-timeout 15m`. Durée : **1 h 03 m**.

```
Échoué!  - échec : 1, réussite : 971, ignorée(s) : 1, total : 973
```

- **971 réussis.**
- **1 échec : `HysteresisThresholdSensitivityLot1418Tests.Integration_Network_HysteresisThresholdSensitivity_Lot1418`** —
  l'**échec pré-existant connu**, explicitement nommé dans le brief. C'est un contrôle de fidélité
  d'une **réplique EMA/hystérésis côté test** (`PersistenceHysteresisReplica`) contre le vrai
  `FusionStateManager`, sur la dimension **`Persistence`** :
  `FidelityPass(BitIdentical)=False, 3420/11428 mismatches` (`replica≈0.0142`, `real=0` sur les
  premières barres). **Causalement disjoint de `StructuralBreak`** : (a) ce lot ne modifie aucune
  production ; (b) le Lot 16 n'a touché que `StructuralBreakEvidenceRule.NormalizeStrength`, et
  `FusionStateManager` lisse chaque dimension indépendamment — `StructuralBreak.Strength` ne peut pas
  influencer la séquence stabilisée de `Persistence`. Vérifié en isolation avant/après : même mode
  d'échec (~3420–3433 mismatches selon le tirage Yahoo).
- **1 ignoré : `Sprint1515HistoricalReconciliationXunitTests.VerifySprint1514Unaffected`** — `[Skip]`
  délibéré préexistant.
- Le run a été **abandonné à 1 h 03 m** par le `--blame-hang-timeout 15m` pendant
  `AmbiguityGateThresholdRevalidationLot1413Tests.Integration_Network_..._SensitivityGrid`, un test
  **réseau** qui exécute plusieurs backtests de production complets sur une grille de seuils.
  **Cause confirmée : épuisement du quota Yahoo Finance.** Après ~1 h de tests d'intégration réseau
  (la suite complète elle-même) plus l'ensemble des runs réseau des Lots 16 / 16.1 de cette session,
  `https://query1.finance.yahoo.com` renvoie **HTTP 429 (Too Many Requests)** sur chaque appel
  (vérifié : 3 appels consécutifs, tous 429). `YahooHistoricalBarSource.Load` sous rafale de 429 reste
  bloqué en boucle de retry/backoff au lieu de lever une `HttpRequestException` que le
  `catch (IsConnectivityOrProviderIssue)` du test convertirait en `SKIPPED`. Une **relance isolée** de
  ce test a de nouveau bloqué (> 40 min, `--blame-hang-timeout 40m`).
- Ce blocage est un **stall du fournisseur de données (rate-limit)**, pas une régression : (a) le
  Lot 16.1 ne modifie aucune production ; (b) le Lot 16 n'ajoute qu'une transformation arithmétique
  pure bornée d'une ligne dans `StructuralBreakEvidenceRule` — incapable de provoquer un hang ; (c) le
  test ne lit que `Decision.Winner` / mesures / risque, aucun de ces champs n'étant influencé par
  `StructuralBreak` (zéro consommateur de décision — cf. `StructuralBreakRegressionTests`, 0 mismatch).

**Aucune régression** : sur les 973 tests exécutés, le seul échec est l'échec pré-existant connu, sans
rapport avec ce lot ni avec le Lot 16 ; le seul non-achèvement est un stall de rate-limit Yahoo
externe à la base de code. Les fichiers de test ajoutés (`Lot161ConfidenceSourceComparisonTests`,
Lot 16) compilent et sont découverts sans erreur (assemblage de test intact — condition nécessaire de
tous les runs `dotnet test` de cette session).

> Reproductibilité : une re-vérification propre de `AmbiguityGateThresholdRevalidationLot1413Tests`
> nécessite d'attendre la levée du rate-limit Yahoo (typiquement quelques heures) ; elle est
> recommandée hors session mais n'affecte pas la conclusion de ce lot (production non modifiée).

---

## 14. Production Decision (Phase 2 → clôture)

```
CONFIDENCE CONTRACT: UNRESOLVED (documented, not fixed)
PRODUCTION MODIFIED: NO
```

Justification : aucune source candidate ne satisfait **simultanément** les 6 contraintes Phase 2 plus
l'exigence de granularité. Les candidats **découplés** de `Value` sont soit sémantiquement faux
(`C` = magnitude de rupture, pas qualité de mesure), soit dégénérés (`D` = constant), soit trop
grossiers (`E` = binaire, et repositionne un champ « diagnostic only »). Les candidats à la
**sémantique acceptable** (`A` corroboration) restent corrélés à `Strength` (0.71) et 93 % saturés.

La cause est **structurelle** : le contrat CUSUM tel qu'exposé (`Evidence.Cusum`) ne transporte
**pas** de métrique de qualité de mesure indépendante de la magnitude. C'est un fait de conception
déjà établi (Lot 15.7/15.8), pas une lacune que l'on peut combler en aval sans soit :

1. exposer une métrique de qualité réelle depuis `CusumStatistics.Compute` (résidu de calibration
   pré-échantillon, stabilité de la variance de référence) — **code protégé, hors périmètre**,
   nécessite un audit séparé et autorisé de ses autres consommateurs ; soit
2. construire un accumulateur de stabilité temporelle dédié (état inter-barres pour
   `EstimatedBreakIndex` ou pour la valeur stabilisée) — ajout architectural dépassant « extraire un
   champ déjà présent », et décision de conception à ne pas précipiter.

Conformément à la clause explicite du brief (« C'est un résultat de clôture acceptable. Ne pas forcer
une transformation juste pour produire du code »), le lot **se clôt sans modification de production**.

---

## 15. Limitations

1. Dataset unique de ~59 jours (rappel constant depuis le Lot 15.0). La distribution de `Agreement`
   (Both 93 %) et la fréquence de stabilité de `EstimatedBreakIndex` (32 %) pourraient différer sur un
   dataset plus long — mais la conclusion (aucun canal de qualité indépendant dans le contrat CUSUM)
   est un fait de **code**, indépendant du tirage.
2. Corrélations Pearson/Spearman uniquement — une dépendance non linéaire plus riche entre un candidat
   et `Strength` n'est pas exclue (hors périmètre).
3. Le candidat `E` (stabilité de localisation) n'a été testé que dans sa forme **instantanée** (égalité
   barre-à-barre). Une forme lissée (fraction des K dernières barres où l'indice n'a pas bougé)
   pourrait avoir une meilleure granularité — mais `K` serait un paramètre, et le champ sous-jacent
   reste « diagnostic only ». Non poursuivi ici.
4. La duplication `Value == Confidence` **persiste** en production après ce lot. Elle est documentée,
   non corrigée. Elle n'a **aucun effet aval** tant qu'aucune `IDecisionRule` ne consomme
   `FusionDimension.StructuralBreak` (état actuel : zéro consommateur).

---

## 16. Recommended Next Lot

**« CUSUM Measurement-Quality Metric Exposure »** — lot d'audit/design (pas d'implémentation de
stratégie), scindé en deux :

1. Auditer `CusumStatistics.Compute` **et tous ses consommateurs** (`StructuralBreakEvidenceRule`,
   `ScientificDashboard`, `BacktestFingerprint`, tests golden) pour déterminer si une métrique de
   qualité réelle peut être ajoutée à `CusumResult` sans casser le fingerprint ni les autres
   consommateurs : candidats — résidu de calibration pré-échantillon, stabilité de la variance de
   référence, ou accumulateur de stabilité de `EstimatedBreakIndex`.
2. Seulement si (1) aboutit à une métrique défendable : un lot d'implémentation minimal découplant
   `StructuralBreak.Confidence` de `Strength`, avec la même rigueur que le Lot 16.

Tant que ce lot n'est pas fait : **ne pas** repondérer sur `StructuralBreak` (la `Confidence` reste une
copie de `Strength`) ; **ne pas** forcer un canal `Confidence` à partir d'`Agreement` ou de `BreakCount`
(sémantiquement faux ou non discriminant) ; **ne pas** toucher `CusumStatistics.Compute` sans l'audit
de ses autres appelants.

---

## FINAL OUTPUT

```
STATUS:
PHASE 0 RESULT: CONFIRMED PERSISTENT

CORRELATION VALUE/CONFIDENCE:
  BEFORE THIS LOT (post-Lot16):  RAW 1.000000000000000  /  STABLE 1.000000000000000
                                 (11428/11428 bit-identical pairs, both stages)
  AFTER THIS LOT:                RAW 1.000000000000000  /  STABLE 1.000000000000000  (unchanged — no production change)

CONFIDENCE CONTRACT: UNRESOLVED

BOUNDED:            N/A (no new Confidence implemented; existing Confidence stays in [0,1))
DETERMINISM:        PASS  (Phase 0/1 double-hash identical: 91b27313...)
LOOK-AHEAD:         PASS  (candidates are per-bar; E uses only bar i-1; measurement walk is chronological)
RUN ISOLATION:      PASS  (fresh EvidenceFusionEngine + FusionStateManager per build; two builds identical)
STRENGTH UNCHANGED: PASS  (no production line touched; Strength = r/(1+r) as set by Lot 16; measured identical)
OTHER 5 DIMENSIONS: UNCHANGED  (no production change)

CALIBRATION:        NOT EXECUTED
REWEIGHTING:        NOT EXECUTED
PRODUCTION MODIFIED: NO
RISK ENGINE:        NOT MODIFIED
DECISION RULES:     NOT MODIFIED
ATAS:               NOT USED
ORDERS:             NONE
DLL DEPLOYED:       NO
COMMIT:             NO

TESTS DEDICATED:
  Lot161ConfidenceSourceComparisonTests.Integration_Network_Lot161_ConfidenceSource_Phase0And1 — PASS (1/1)

FULL SUITE (dotnet test, no filter, 1h03m):
  réussite: 971  |  échec: 1  |  ignorée: 1  |  total exécuté: 973
  - the single failure is HysteresisThresholdSensitivityLot1418Tests (pre-existing, known; a test-side
    Persistence EMA replica vs real FusionStateManager, causally disjoint from StructuralBreak)
  - 1 skip is a pre-existing deliberate [Skip]
  - run aborted at 1h03m by --blame-hang-timeout on AmbiguityGateThresholdRevalidationLot1413Tests
    (a NETWORK sensitivity-grid test) - CONFIRMED CAUSE: Yahoo Finance rate-limit exhaustion
    (HTTP 429 on every call after ~1h of network tests this session); YahooHistoricalBarSource.Load
    stalls in retry/backoff under a 429 storm. Environmental data-provider stall, NOT a regression -
    the test reads only Decision.Winner/measurement/risk, none influenced by StructuralBreak.
    Clean re-verification needs the Yahoo rate-limit to lift (hours); does not affect this lot's
    conclusion (no production change).
  - NO NEW REGRESSION

DOCUMENTATION:
  Documentation/Scientific/QDE-012_Sprint_15.25_Lot16.1_StructuralBreak_Confidence_Decoupling_Report.md

NEXT LOT:
  "CUSUM Measurement-Quality Metric Exposure" (audit/design): can a real quality metric be added to
  CusumResult without breaking its other consumers (dashboard, fingerprint)? Only then can
  StructuralBreak.Confidence be decoupled from Strength. Until then: no reweighting on StructuralBreak;
  no Agreement/BreakCount-based Confidence; do not touch CusumStatistics.Compute.
```

**STOP.**
