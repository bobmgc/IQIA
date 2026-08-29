# QDE-012 — Sprint 15.25 — Lot 17 — Audit end-to-end de la contribution réelle de StructuralBreak jusqu'au signal

> **Type :** Independent Scientific Audit — **Mode :** READ ONLY
> **Production modifiée :** NON — **Calibration / Optimisation / Repondération :** NON
> **ATAS :** non utilisé — **Ordres :** aucun — **DLL déployée :** non — **Commit :** non
>
> Trace de bout en bout : depuis les statistiques CUSUM / Bai-Perron **et** depuis la règle de régime
> `StructuralBreakRule`, jusqu'à `DirectionCandidate` (BUY / SELL / NO_ACTION) et l'ouverture de
> position. Aucune conclusion n'engage la roadmap sans décision explicite de l'utilisateur.

---

## 0. Méthode

1. **Lecture du code de production** (aucune modification) : `Engine/Fusion/Rules/StructuralBreakEvidenceRule.cs`,
   `Engine/Decision/Rules/StructuralBreakRule.cs` + les 4 autres règles, `Engine/Decision/Core/DecisionEngine.cs`,
   `Engine/Decision/Arbitration/DecisionArbitrator.cs`, `Engine/EntryTrigger/EntryTriggerBuilder.cs`,
   `Engine/Methodology/Core/MethodologyRegistry.cs`, le câblage `IQIAIndicator.cs`, `Engine/Regime/Evidence/CUSUM/**`,
   `Engine/Regime/Evidence/BaiPerron/**`.
2. **Données empiriques déjà au dépôt** (pipeline de production non modifié, Yahoo `MES=F` M5, ~59 j) :
   `regime_status_counts.csv`, `regime_signal_funnel.csv`, `regime_entrytrigger_reason_lot151.csv`,
   `structuralbreak_evidence_ablation_lot152.csv`, `StructuralBreakInformationAudit/Output/PipelineTraceStages.csv`.
3. **Test additif isolé** (observation only) :
   `Tests/Research/StructuralBreakSignalContribution/StructuralBreakSignalContributionAuditTests.cs` — rejoue le
   pipeline de signal non modifié, reconstruit la fusion stabilisée barre par barre, puis réévalue la décision
   avec **trois** moteurs sur le même `FusionResult` :
   - `engine5` : liste de production `{StableRange, Trending, MeanReverting, StructuralBreak, RandomWalk}` ;
   - `engine4` : `StructuralBreakRule` **retirée** ;
   - `engine5` avec `FusionDimension.StructuralBreak` **forcée à 0.0 puis à 1.0** (test de perturbation).
   Sorties : `Tests/Research/StructuralBreakSignalContribution/Output/`.

---

## 1. Les DEUX entités « StructuralBreak » — état réel du code

Le nom « StructuralBreak » recouvre **deux objets disjoints** dans le pipeline :

| | `FusionDimension.StructuralBreak` (D6) | `MarketState.StructuralBreak` (régime) |
|---|---|---|
| Producteur | `StructuralBreakEvidenceRule` (règle de fusion) | `StructuralBreakRule` (règle de décision) |
| Entrées brutes | `Evidence.Cusum` (+ `Evidence.BaiPerron` diagnostique) | `1 − {StructuralStability, Persistence, MeanReversion, Stationarity}` (dimensions stabilisées) |
| Utilise les évidences CUSUM / Bai-Perron ? | **oui** | **NON** — aucune |
| Utilise `FusionDimension.StructuralBreak` ? | (c'est elle) | **NON** |
| Câblé en production ? | oui (`EvidenceFusionEngine`, `IQIAIndicator.cs:72`) | oui (`DecisionEngine`, `IQIAIndicator.cs:61`) |
| Consommé en aval ? | **par personne** (aucune `IDecisionRule`, aucun dashboard) | par l'arbitrage → `Decision.Winner` |

### 1.1 `StructuralBreakRule` — formule exacte

```
scientificScore = 0.40·(1−StructuralStability.Value) + 0.30·(1−Persistence.Value)
               + 0.20·(1−MeanReversion.Value) + 0.10·(1−Stationarity.Value)
qualityScore   = 0.40·StructuralStability.Conf + 0.30·Persistence.Conf
               + 0.20·MeanReversion.Conf + 0.10·Stationarity.Conf
finalScore     = clamp(0.90·scientificScore + 0.10·qualityScore, 0, 1)
```

La règle **n'ouvre `FusionResult.Dimensions[FusionDimension.StructuralBreak]` à aucun moment**, ni
`Evidence.Cusum`, ni `Evidence.BaiPerron`. C'est une **anti-combinaison linéaire des 4 autres
dimensions** — le mot « structural break » ne renvoie ici qu'à une étiquette de régime.

### 1.2 `StructuralBreakEvidenceRule` — doc-comment de production

> « Deliberately NOT consumed by any Decision.Rules.IDecisionRule in this lot (brief §9/§25 …) - it is
> produced and stabilized, available for a future, explicitly-scoped reweighting lot. »

`grep FusionDimension.StructuralBreak` hors tests → seulement `FusionStateManager` (liste) et
`StructuralBreakEvidenceRule` (production). Confirme QDE-012.

---

## 2. Chaîne complète (SOURCE → EFFET OBSERVABLE)

### Chemin A — `FusionDimension.StructuralBreak` (évidence CUSUM/Bai-Perron)

```
Evidence.Cusum ──► StructuralBreakEvidenceRule.BuildContract
   Strength = r/(1+r),  r = peakCusum/threshold           (Lot 16, granularité restaurée)
        │
        ▼
FusionResult.Dimensions[StructuralBreak]  (Value = Confidence = Strength)
        │  EMA + hystérésis (FusionStateManager, même chemin que D1/D2/D3/D5)
        ▼
FusionSnapshot.StableResult[StructuralBreak]           ◄── PipelineTraceStages.csv s'arrête ICI
        │
        ▼
   (aucun lecteur)                                       EFFET : NO_OBSERVED_DOWNSTREAM_EFFECT
```

`PipelineTraceStages.csv` (Lot 15.9) liste 7 étages : `1_PeakMagnitude → … → 7_StableValue_…_PostFusionState`.
**Il n'y a pas d'étage 8** : rien ne lit la dimension après stabilisation.

### Chemin B — `MarketState.StructuralBreak` (régime)

```
{StructuralStability, Persistence, MeanReversion, Stationarity}  (stabilisées)
        │  StructuralBreakRule : 0.90·Σ wᵢ·(1−Valueᵢ) + 0.10·qualité
        ▼
DecisionCandidate(FinalScore)
        │  DecisionArbitrator : argmax FinalScore ; AmbiguityScore = clamp(1−(winner−runnerUp),0,1)
        ▼
Decision.Winner == MarketState.StructuralBreak
        │
        ├─► MethodologyRegistry.Resolve → « StructuralBreakMethodology » (fiche descriptive : libellés
        │     « BOCPD », « Sequential Detection » — AUCUN modèle exécuté)
        │
        ▼
EntryTriggerBuilder.DetermineDirection :
        if (decision.Winner != MarketState.MeanReverting)
            return DirectionCandidate.NO_ACTION;  reason = UNSUPPORTED_REGIME
        │
        ▼
   NO_ACTION  ──►  TradePlan NO_TRADE  ──►  0 position               EFFET : NO_ACTION_SINK
```

### Chemin C — statistiques CUSUM / Bai-Perron ailleurs

`grep .Cusum / .BaiPerron` hors `Engine/Regime/Evidence/` et hors `StructuralBreakEvidenceRule` :
- `EvidenceSet.cs` (déclaration du conteneur) ;
- `RegimeEngine.cs` (producteur) ;
- **`IQIAIndicator.cs:1172-1174`** : `CountAvailableEvidence` — `Cusum.IsValid`/`BaiPerron.IsValid`
  comptent pour un compteur affiché « N/9 évidences disponibles » (dashboard). Observabilité pure.
- **Rien** dans `RiskEngine`, `RiskPolicy`, la logique Stop-Loss, `TradePlanBuilder`, `EntryTriggerEngine`.

---

## 3. Mesures empiriques (données déjà au dépôt)

### 3.1 Fréquence du régime `StructuralBreak` — `regime_status_counts.csv`

| Régime (barres `Ready`) | Barres | % des `Ready` |
|---|---|---|
| MeanReverting | 6792 | 59.43 % |
| **StructuralBreak** | **3562** | **31.17 %** |
| RandomWalk | 502 | 4.39 % |
| Trending | 459 | 4.02 % |
| StableRange | 113 | 0.99 % |

`MarketState.StructuralBreak` est le **2ᵉ régime le plus fréquent** — près d'une barre décidée sur trois.

### 3.2 Entonnoir de signal — `regime_signal_funnel.csv`

| Régime | ReadyBars | SignalProduced | EntryCandidate | BUY | SELL | NO_ACTION | SIGNAL_ONLY | PositionOpened |
|---|---|---|---|---|---|---|---|---|
| MeanReverting | 6792 | 6792 | 6792 | 1222 | 1203 | 4367 | 0 | 2425 |
| **StructuralBreak** | **3562** | **3562** | **0** | **0** | **0** | **3562** | **0** | **0** |
| RandomWalk | 502 | 502 | 0 | 0 | 0 | 502 | 0 | 0 |
| Trending | 459 | 459 | 0 | 0 | 0 | 459 | 0 | 0 |
| StableRange | 113 | 113 | 0 | 0 | 0 | 113 | 0 | 0 |

⇒ **`MarketState.StructuralBreak` : 3562 barres gagnées, 0 BUY, 0 SELL, 0 position. 100 % NO_ACTION.**

### 3.3 Raison du NO_ACTION — `regime_entrytrigger_reason_lot151.csv`

| Régime | NO_ACTION | `UNSUPPORTED_REGIME` | % `UNSUPPORTED_REGIME` | Ambiguïté < 0.95 |
|---|---|---|---|---|
| **StructuralBreak** | 3557 | **3557** | **100 %** | 1804 (50.7 %) |

Chaque barre `StructuralBreak` meurt exactement au gate `decision.Winner != MeanReverting`. Même les
**50.7 %** de barres dont l'ambiguïté passerait le seuil sont bloquées uniquement par le gate de régime.

### 3.4 La règle de régime n'est pas un proxy de l'évidence — `structuralbreak_evidence_ablation_lot152.csv`

| Mesure (N = 11428) | Valeur |
|---|---|
| `Pearson(HypotheticalEvidenceScore, StructuralBreakFinalScore)` | **0.113** |
| `Spearman(…)` | 0.171 |
| `PointBiserial(HypotheticalEvidenceScore, IsStructuralBreakWinner)` | **0.146** |

Un score qui utiliserait **réellement** l'évidence CUSUM/Bai-Perron n'est corrélé qu'à ~0.11–0.17 au
score effectif de `StructuralBreakRule`. Les deux mesurent des choses quasi indépendantes.

### 3.5 Lot 16 : granularité restaurée pour une dimension non lue — `PipelineTraceStages.csv`

| Étage | Min | Max | Mean | DistinctValues | Saturation % |
|---|---|---|---|---|---|
| 4_Cusum_Confidence (pré-Lot 16) | 1 | 1 | 1 | **1** | **100** |
| 5_Contract_Strength (Lot 16, r/(1+r)) | 0.500 | 0.9998 | 0.886 | 10732 | 0 |
| 7_StableValue_StructuralBreak_PostFusionState | 0.182 | 0.879 | 0.811 | 2375 | 0 |

Le Lot 16 a rendu à D6 une `Value` variée (0.5–1.0)… toujours consommée par rien.

---

## 4. Mesures du test additif `StructuralBreakSignalContributionAudit`

Exécution : **Réussi** (4 min 14 s). `ReadyBars = 11314` (fenêtre Yahoo glissante — écart attendu avec
les ~11428 des CSV plus anciens, cf. QDE-012). Double agrégation → hash SHA-256 identique
(déterminisme confirmé). Sorties : `Tests/Research/StructuralBreakSignalContribution/Output/`.

### 4.1 Fidélité de reconstruction — `sb_counterfactual.csv`

`engine5` (liste de production rejouée sur le `FusionResult` stabilisé reconstruit) reproduit le
`Decision.Winner` de production sur **11314 / 11314 barres** — **0 divergence**. La reconstruction est
donc fidèle et les mesures d'ablation ci-dessous portent bien sur le pipeline réel.

### 4.2 Test de perturbation D6 — preuve empirique de non-consommation

En forçant `FusionResult.Dimensions[FusionDimension.StructuralBreak]` à `Value=Confidence=0.0` **puis**
à `1.0` sur chaque barre et en réévaluant `engine5` :

> **`D6 perturbation changed a decision` = 0 / 11314.**

Ni le `Winner`, ni le `WinnerScore` (|Δ| < 1e-12) ne bougent. **Aucune règle de décision ne lit la
dimension d'évidence StructuralBreak** — preuve indépendante de la lecture de code.

### 4.3 Entonnoir end-to-end — `sb_end_to_end_funnel.csv`

| `Decision.Winner` | Barres | % Ready | BUY | SELL | NO_ACTION | `UNSUPPORTED_REGIME` | Ambiguïté moy. | Ambiguïté < 0.95 |
|---|---|---|---|---|---|---|---|---|
| MeanReverting | 6687 | 59.10 % | 1170 | 1157 | 4360 | 0 | 0.958 | 2327 |
| **StructuralBreak** | **3494** | **30.88 %** | **0** | **0** | **3494** | **3494 (100 %)** | 0.935 | 1779 (50.9 %) |
| Trending | 510 | 4.51 % | 0 | 0 | 510 | 510 | 0.904 | 334 |
| RandomWalk | 509 | 4.50 % | 0 | 0 | 509 | 509 | 0.958 | 178 |
| StableRange | 114 | 1.01 % | 0 | 0 | 114 | 114 | 0.974 | 17 |

Confirme §3.2 sur ce run : `StructuralBreak` = 2ᵉ régime (30.9 %), **0 signal directionnel, 100 %
`UNSUPPORTED_REGIME`**.

### 4.4 Matrice d'ablation — `sb_ablation_transition.csv`

`engine4` = `engine5` **sans `StructuralBreakRule`**. Tous les autres régimes : gagnant **inchangé à
100 %** (StructuralBreak n'était jamais assez haut pour être leur gagnant). Les 3494 barres
`StructuralBreak` se réassignent au *runner-up* :

| `StructuralBreak` (5 règles) → | Barres | % du groupe |
|---|---|---|
| **MeanReverting** | **2456** | **70.29 %** |
| RandomWalk | 599 | 17.14 % |
| Trending | 439 | 12.56 % |

### 4.5 Contrefactuel — signaux que `StructuralBreakRule` supprime aujourd'hui — `sb_counterfactual.csv`

| Métrique | Barres | % des barres SB |
|---|---|---|
| Barres gagnées par `StructuralBreak` | 3494 | 30.88 % des Ready |
| → deviennent `MeanReverting` sans la règle | 2456 | 70.29 % |
| → **…dont ambiguïté < 0.95 (atteignent la logique de direction z-score)** | **1677** | **48.00 %** (≈ 14.8 % des Ready) |
| → deviennent un régime non-négociable (Trending / RandomWalk) même sans la règle | 1038 | 29.71 % |

⇒ **Borne haute de l'effet suppressif direct : ~1677 barres** (≈ 15 % de toutes les barres `Ready`)
sont des lectures directionnelles potentielles que `StructuralBreakRule`, en gagnant l'arbitrage,
transforme en `NO_ACTION`. (Borne *haute* : atteindre la logique z-score ne garantit pas un
BUY/SELL — il faut encore `DynamicZScore ≠ 0` et disponible ; ces conditions sont en aval et non
rejouées ici.)

### 4.6 Contamination croisée du gate d'ambiguïté — `sb_counterfactual.csv`

| Métrique | Barres | % des Ready |
|---|---|---|
| Barres à **gagnant inchangé** par l'ablation, mais dont l'ambiguïté **franchit le seuil 0.95** (dans un sens ou l'autre) | 1920 | 16.97 % |
| …dont sur des barres gagnées par `MeanReverting` | 1736 | 15.34 % |

`StructuralBreak` est fréquemment le **runner-up** ; le retirer change le 2ᵉ score, donc
`AmbiguityScore = 1 − (winner − runnerUp)`, donc le verdict du gate 0.95 — pour **1736 barres
MeanReverting** (le seul régime négociable). L'effet net (signaux gagnés vs perdus) n'est pas tranché
ici, mais l'amplitude (~15 % des barres) montre que `StructuralBreakRule` **perturbe indirectement le
gate d'ambiguïté du seul régime qui trade**, en plus de son effet direct §4.5.

### 4.7 Corrélation évidence D6 ↔ étiquette de régime — `sb_evidence_vs_regime_correlation.csv`

| Métrique (N = 11314) | Valeur |
|---|---|
| `PointBiserial(D6.Value_stabilisée, IsStructuralBreakWinner)` | **0.395** |
| Moyenne `D6.Value` \| gagnant `StructuralBreak` | 0.849 |
| Moyenne `D6.Value` \| autre gagnant | 0.777 |
| Taux `D6.IsAvailable` | 100 % |

Couplage **modéré** : les barres où le régime `StructuralBreak` gagne ont une évidence CUSUM un peu
plus forte (0.849 vs 0.777). Mais ce couplage est **indirect** — `StructuralBreakRule` ne lit pas D6
(§4.2) ; il vient de ce que « faible stabilité + faible persistance » (ce que la règle mesure) coïncide
partiellement avec une détection CUSUM. La règle **n'est pas** un estimateur de rupture structurelle,
elle en attrape le reflet par corrélation.

---

## 5. Classification finale

| Entité | Producer | Câblé ? | Consommé aval | Effet sur le signal | Verdict |
|---|---|---|---|---|---|
| `FusionDimension.StructuralBreak` (D6) | `StructuralBreakEvidenceRule` (CUSUM) | oui | **aucun** | **aucun** (prouvé par perturbation) | `NO_CONTRIBUTION` / `DEAD_END` |
| `MarketState.StructuralBreak` (régime) | `StructuralBreakRule` (anti-combi 4 dims) | oui | arbitrage → `Decision.Winner` | **0 signal directionnel, 0 position** ; route ~31 % des barres vers `UNSUPPORTED_REGIME` NO_ACTION | `NO_ACTION_SINK` / `NEGATIVE_CONTRIBUTION_ONLY` |
| CUSUM / Bai-Perron (stats brutes) | `RegimeEngine` | oui | compteur d'observabilité `N/9` | **aucun** effet comportemental | `OBSERVABILITY_ONLY` |

### Réponse à la question posée

**La contribution positive de « StructuralBreak » au signal est nulle**, quel que soit le sens du terme :

- l'**évidence** structurelle (CUSUM → D6) se termine en cul-de-sac : produite, stabilisée (granularité
  restaurée au Lot 16), lue par personne — **0 / 11314** décisions modifiées en forçant la dimension à
  0.0 puis 1.0 (§4.2) ;
- le **régime** `MarketState.StructuralBreak`, lui, a un effet end-to-end réel mais **exclusivement
  négatif** : il gagne l'arbitrage sur **30.9 %** des barres et les envoie **toutes** en `NO_ACTION /
  UNSUPPORTED_REGIME` (0 BUY, 0 SELL, 0 position). Il ne peut que *supprimer* un signal, jamais en
  produire. Effet suppressif quantifié :
  - **effet direct** : ~**1677 barres** (≈ 15 % des `Ready`) deviendraient `MeanReverting` avec
    ambiguïté < 0.95 — donc atteindraient la logique de direction — si la règle était retirée (§4.5) ;
  - **effet indirect** : `StructuralBreak` comme runner-up fait franchir le gate 0.95 à **1736 barres
    MeanReverting** (§4.6) — il perturbe le gate d'ambiguïté du seul régime négociable.
  Et il fait tout cela **sans consommer la moindre évidence de rupture structurelle** — sa formule est
  une anti-combinaison de `{StructuralStability, Persistence, MeanReversion, Stationarity}` (couplage
  indirect à D6 : point-biserial 0.395).

---

## 6. OBSERVED FACTS / INTERPRETATIONS / POTENTIAL ACTIONS

### OBSERVED FACTS

1. `FusionDimension.StructuralBreak` : 0 consommateur en aval (code + `PipelineTraceStages.csv` sans
   étage 8 + test de perturbation §4.2).
2. `StructuralBreakRule` lit `{StructuralStability, Persistence, MeanReversion, Stationarity}` — pas
   D6, pas CUSUM, pas Bai-Perron.
3. `MarketState.StructuralBreak` gagne 31.17 % des barres `Ready` (3562), produit 0 BUY / 0 SELL / 0
   position, 100 % `UNSUPPORTED_REGIME`.
4. Le gate `EntryTriggerBuilder.DetermineDirection` renvoie `NO_ACTION` pour **tout** régime ≠
   `MeanReverting` (pas spécifique à StructuralBreak).
5. `structuralbreak_evidence_ablation_lot152.csv` : corrélation score-évidence ↔ score-règle = 0.113.
6. CUSUM/Bai-Perron alimentent seulement `CountAvailableEvidence` (compteur affiché) hors de la règle
   de fusion.
7. `MethodologyRegistry` mappe `StructuralBreak` vers une fiche descriptive (aucun modèle exécuté).

### SCIENTIFIC INTERPRETATIONS (hypothèses)

- Le pipeline contient **deux notions homonymes non connectées** : une évidence de rupture (inerte) et
  un régime de rupture (actif mais dérivé d'autre chose). L'audité ne peut pas dire que le régime
  « détecte des ruptures » — il détecte surtout « faible stabilité + faible persistance ».
- `StructuralBreakRule` fonctionne comme un **filtre NO_ACTION de grande ampleur** : ~31 % des barres.
  Une partie de ces barres (celles où MeanReverting serait 2ᵉ avec ambiguïté < 0.95) sont des signaux
  directionnels potentiels aujourd'hui bloqués — le §4.4 en donne la borne haute.
- Câbler D6 dans `StructuralBreakRule` (ou ailleurs) apporterait de l'information **nouvelle** (corr
  0.11) — mais ce serait une décision de repondération, hors périmètre d'un audit.

### POTENTIAL FUTURE ACTIONS (pistes — NE PAS exécuter)

- Décider explicitement du statut de D6 : la câbler (lot de repondération dédié), la retirer, ou la
  garder comme observabilité assumée.
- Réexaminer si `StructuralBreakRule` doit rester dans la liste de production tant que son unique effet
  est de router 31 % des barres en NO_ACTION, ou si son seuil / ses poids doivent être revus (lot de
  calibration dédié).
- Envisager un chemin d'entrée pour `MarketState.StructuralBreak` (au minimum SIGNAL_ONLY explicite
  plutôt que NO_ACTION générique) si le régime doit avoir une valeur informative pour l'utilisateur.
- Aucune de ces actions n'est engagée par le présent document.

---

## 7. Fichiers de production — non modifiés

`StructuralBreakEvidenceRule.cs`, `StructuralBreakRule.cs`, `DecisionEngine.cs`, `DecisionArbitrator.cs`,
`EntryTriggerBuilder.cs`, `MethodologyRegistry.cs`, `RegimeEngine.cs`, `FusionStateManager.cs`,
`CusumStatistics.cs`, `BaiPerronStatistics.cs`, `IQIAIndicator.cs` : **aucune modification**. Seuls
ajouts : ce rapport + `Tests/Research/StructuralBreakSignalContribution/…` + ses sorties `Output/`.

---

## 8. FINAL OUTPUT

```
STATUS: INDEPENDENT AUDIT COMPLETE
SCOPE: end-to-end contribution of StructuralBreak (evidence dimension + regime) to the trading signal
PRODUCTION MODIFIED: NO
CALIBRATION / OPTIMIZATION / REWEIGHTING / PARAMETERS CHANGED: NO
ATAS: NOT USED   ORDERS: NONE   DLL DEPLOYED: NO   COMMIT: NO

KEY FINDINGS:
1. FusionDimension.StructuralBreak (CUSUM/Bai-Perron evidence): produced + stabilized, consumed by
   NOTHING. Forcing it to 0.0 and 1.0 changed 0 / 11314 decisions (winner AND score). NO_CONTRIBUTION.
2. MarketState.StructuralBreak (regime, via StructuralBreakRule): wins 30.9% of Ready bars (3494),
   produces 0 BUY / 0 SELL / 0 position, 100% UNSUPPORTED_REGIME. NO_ACTION_SINK. Computed from
   1-{StructuralStability, Persistence, MeanReversion, Stationarity} - uses NO structural-break
   evidence (indirect coupling to D6: point-biserial 0.395; hypothetical-evidence-score corr 0.113/0.146).
3. Suppression, quantified (ablate StructuralBreakRule): 70.3% of its bars -> MeanReverting, of which
   ~1677 (=15% of all Ready bars) with ambiguity < 0.95 (would reach the z-score direction logic).
   Plus 1736 MeanReverting bars whose 0.95 ambiguity-gate verdict flips (StructuralBreak was runner-up).
4. CUSUM/Bai-Perron statistics feed only an on-screen "N/9 evidence available" counter outside the
   fusion rule. OBSERVABILITY_ONLY.

CONCLUSION: StructuralBreak makes ZERO positive contribution to any signal. The only end-to-end effect
of anything named "structural break" is StructuralBreakRule routing ~31% of bars to NO_ACTION and
perturbing MeanReverting's ambiguity gate - a purely suppressive effect, from a rule that ignores
structural-break evidence entirely; the evidence dimension itself is inert.

CONCLUSIONS: OBSERVATIONAL ONLY
ROADMAP IMPACT: NONE UNLESS EXPLICITLY APPROVED BY USER
NEXT ACTION: STOP - WAIT FOR USER DECISION

DOCUMENTATION:
Documentation/Scientific/QDE-012_Sprint_15.25_Lot17_StructuralBreak_EndToEnd_Signal_Contribution_Audit_Report.md
TEST: Tests/Research/StructuralBreakSignalContribution/StructuralBreakSignalContributionAuditTests.cs
```
