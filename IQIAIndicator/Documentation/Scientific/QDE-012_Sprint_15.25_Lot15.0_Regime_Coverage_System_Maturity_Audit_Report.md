# QDE-012 — Sprint 15.25 — Lot 15.0 — Regime Coverage & System Maturity Audit

**Date** : 2026-08-24
**Branche** : `feature/structural-stability-v2`
**Lots précédents** : 14.9-14.17
**Statut** : **IMPLEMENTED**
**Type** : **AUDIT ONLY — AUCUNE CALIBRATION, AUCUN TUNING, AUCUNE MODIFICATION DE PRODUCTION**

---

# 1. EXECUTIVE SUMMARY

Ce lot répond à une seule question, posée explicitement par le brief : **le système IQIA est-il réellement construit pour fonctionner dans tous les régimes qu'il prétend supporter ?**

**Réponse, établie par audit de code direct (fichiers protégés lus, jamais modifiés) puis confirmée empiriquement sur 11 059 barres fraîches (MES=F, M5, ~59 jours, 2026-06-26 → 2026-08-24)** :

**Non.** Le système ne produit un signal directionnel, une position, une mesure de performance ou un coût que pour **un seul régime sur cinq réellement atteignables : `MeanReverting`**. C'est un fait architectural auto-documenté dans le code lui-même (`EntryTriggerBuilder.DetermineDirection`, commentaire de la méthode : *"the ONLY methodology backed by real, non-placeholder scientific models capable of a directional read is MeanReversionMethodology"*), corroboré indépendamment par `MethodologyRegistry`/`ScientificModelRegistry` (les modèles scientifiques des 4 autres méthodologies — `TimeSeriesMomentumModel`, `BOCPDModel` — sont des stubs qui renvoient toujours `Success=false`), et confirmé bit pour bit par la mesure directe de ce lot :

```
EntryCandidate produit :      MeanReverting 6507/6507 (100%)  |  StructuralBreak/RandomWalk/Trending/StableRange 0/N (0%)
Signal directionnel BUY/SELL: MeanReverting 2315/6507 (35.6%) |  tous les autres régimes           0/N (0%)
TradePlan produit :           MeanReverting 2315 SIGNAL_ONLY  |  tous les autres régimes           0
Position ouverte :            MeanReverting 2315/6507 (35.6%) |  tous les autres régimes           0/N (0%)
```

`StructuralBreak` (30.9% du dataset — le **deuxième régime le plus fréquent**) et `Trending`/`RandomWalk`/`StableRange` (ensemble 9.6% du dataset) sont **entièrement inertes en aval de Decision** : Signal est produit (100%, un artefact de câblage — voir §16), mais aucune méthodologie scientifique réelle n'existe pour eux, donc `NO_ACTION` est garanti par construction, indépendamment de la qualité de la Evidence/Fusion/Decision en amont.

Deuxième découverte structurelle majeure : `StructuralBreak`, alors qu'il représente près d'un tiers du dataset, est détecté **sans aucune evidence de rupture structurelle**. Le moteur calcule bien CUSUM et Bai-Perron à chaque barre (`RegimeEngine.Collect`), mais **aucune `IFusionRule` ne les consomme jamais** — la dimension `FusionDimension.StructuralStability` que `StructuralBreakRule`/`TrendingRule`/`MeanRevertingRule`/`StableRangeRule` lisent toutes n'est pas dérivée de CUSUM/Bai-Perron : c'est un signal auto-référentiel calculé par `StructuralStabilityRule.EvaluateAnalysis` à partir de la variabilité *récente des 4 autres dimensions Fusion elles-mêmes* (`FusionProfileAnalyzer`), injecté directement dans `FusionStateManager`, en dehors des 4 `IFusionRule` que `BacktestEngine`/`IQIAIndicator.cs` cablent explicitement.

Troisième découverte : le Stop Loss est **globalement absent**, pas seulement pour les régimes non couverts — `TradePlanContext.RiskParameters` est toujours `null`, aussi bien dans le chemin live (`IQIAIndicator.cs:602`) que dans le Backtest (`BacktestEngine.cs:345`). `TradePlanStatus.PLAN_READY` est donc **structurellement inatteignable pour tous les régimes, y compris MeanReverting** — confirmé empiriquement : **0 `PLAN_READY` sur 2315 TradePlans produits** (100% `SIGNAL_ONLY`). Les positions du Backtest s'ouvrent quand même (2315), mais via une sortie à horizon temporel fixe (`ExecutionSimulator`), totalement découplée du concept de TradePlan/StopLoss — un choix de conception documenté depuis le Lot 14.10, pas un bug caché, mais qui doit être compris avant d'interpréter toute mesure de performance.

**Verdict global (§26)** : **GLOBAL CALIBRATION = NOT READY.** Le projet n'est prêt à calibrer ni Fusion, ni Decision, ni Risk, ni Stop Loss tant que (a) 4 des 5 régimes réellement atteignables n'ont aucune capacité de trading, et (b) le régime `StructuralBreak` n'a aucune evidence de rupture structurelle qui le sous-tend. Ce n'est pas un problème de données — le dataset couvre les 5 régimes avec des volumes exploitables pour `MeanReverting`/`StructuralBreak`, et des volumes plus fragiles mais réels pour `RandomWalk`/`Trending`/`StableRange` (§27). C'est un problème d'architecture, documenté ici avec preuve directe de code et preuve empirique concordante.

**Aucune modification de production. Aucun fichier protégé touché. Aucune calibration exécutée.**

---

# 2. WHY CALIBRATION IS PAUSED

Le brief de ce lot l'impose explicitement (§0, §26, §33) : calibrer un seuil, un poids ou une fenêtre avant de savoir si l'architecture qui les consomme fonctionne dans l'espace des régimes visé reviendrait à optimiser un système dont on ignore s'il est structurellement capable de faire ce qu'on lui demande. Les Lots 14.13-14.17 ont déjà exploré `AmbiguityGateThreshold`, `Persistence`, `HysteresisThreshold` — tous dans le contexte implicite (non vérifié à l'époque) que ces paramètres s'appliquent uniformément à tous les régimes. Ce lot démontre que ce n'est pas le cas : `AmbiguityGateThreshold` par exemple n'a **jamais** d'effet en dehors de `MeanReverting` (§15), puisque les 4 autres régimes sont déjà bloqués en amont de ce test. Toute calibration antérieure ou future de ce paramètre doit donc être comprise comme une calibration **de `MeanReverting` uniquement**, jamais du système dans son ensemble — un fait qui change la portée de toute conclusion déjà tirée des Lots 14.13/14.14.

---

# 3. CURRENT DATASET

```
Source          : Yahoo Finance (YahooHistoricalBarSource), ATAS NON UTILISÉ
Symbole         : MES=F
Timeframe       : M5
Fenêtre         : 2026-06-26T07:35Z → 2026-08-24T07:29Z (~59 jours, fenêtre glissante — non identique
                  jour pour jour aux téléchargements des Lots 14.9-14.17, cf. mémoire projet sur la
                  non-déterminisme Yahoo)
BarCount total  : 11 059
Fingerprint     : 1617CBAA…31FE8 (HistoricalSeriesFingerprint.Compute)
WarmupBars      : 128 (fenêtre DFA, la plus longue de toutes les evidences)
```

Statuts pipeline (`BacktestSignalStatus`) sur les 11 059 barres :

| Statut | N | % |
|---|---:|---:|
| Rejected | 0 | 0% |
| Warmup | 128 | 1.16% |
| Ready | 10 931 | 98.84% |
| Exception | 0 | 0% |

Aucune barre rejetée, aucune exception sur l'ensemble du run (pipeline complet Signal→Measurement→Execution→Risk/Cost) — confirme la robustesse mécanique déjà établie par les Lots 14.1-14.10.

---

# 4. REGIME INVENTORY

**Deux taxonomies de "régime" coexistent dans le code — ce n'était pas supposé, c'est un résultat de cet audit (brief §1, "ne pas supposer que la liste historique est correcte").**

### 4.1 `Engine/Regime/RegimeType.cs` — MORTE, JAMAIS CÂBLÉE

```csharp
public enum RegimeType { Unknown, TrendBull, TrendBear, MeanReversion, Range, Compression, Expansion, Transition }
```

Consommée uniquement par `Engine/Regime/Core/EvidenceFusionEngine.cs`, dont le corps entier est :

```csharp
public RegimeResult Fuse(EvidenceSet evidence) => new() { Regime = RegimeType.Unknown, ... "Fusion non implémentée — Sprint 2.4." };
```

Un stub du Sprint 2.4, jamais complété. Recherche exhaustive (`grep -r "new EvidenceFusionEngine()"` restreinte au namespace `Engine.Regime.Core`) : **aucun appelant, nulle part dans le dépôt** (ni `BacktestEngine.cs`, ni `IQIAIndicator.cs`). Cette classe et son enum ne participent à AUCUN pipeline exécuté — ni live, ni backtest. Ils sont **écartés de tout le reste de cet audit**.

### 4.2 `Engine/Decision/States/MarketState.cs` — VIVANTE, C'EST LE VRAI RÉGIME

```csharp
public enum MarketState { Unknown, StableRange, MeanReverting, Trending, Transitional, StructuralBreak, RandomWalk }
```

Produite par `DecisionArbitrator.Arbitrate(candidates).Winner` — c'est la valeur utilisée dans tout le reste de ce rapport comme "le régime". 7 valeurs déclarées, mais :

- **5 sont atteignables comme Winner** : chacune assignée par exactement une des 5 `Engine/Decision/Rules/*.cs` (`MeanRevertingRule`, `TrendingRule`, `RandomWalkRule`, `StableRangeRule`, `StructuralBreakRule`).
- **`Unknown`** n'est retourné que si `candidates.Count == 0` (`DecisionArbitrator.Arbitrate`) — jamais observé (0/10 931 barres Ready, confirmé §6).
- **`Transitional`** n'est **jamais** assigné par aucune des 5 règles (`grep "MarketState.Transitional ="` dans `Engine/Decision/Rules/` : 0 résultat) — **structurellement inatteignable comme Winner**, confirmé empiriquement (0/10 931). C'est un CASE B-adjacent : le régime existe dans l'enum et a même une méthodologie dédiée en aval (`MethodologyRegistry.Resolve(MarketState.Transitional)` → `"TransitionalMethodology"`, explicitement documentée "No scientific methodology is implemented yet... Explicitly unsupported"), mais rien en amont ne peut jamais le sélectionner. Ni détection manquante, ni donnée absente : un **maillon mort par construction**.

**Inventaire final retenu pour ce rapport : 5 régimes vivants (`MeanReverting`, `Trending`, `RandomWalk`, `StableRange`, `StructuralBreak`) + 1 fallback jamais observé (`Unknown`) + 1 état mort (`Transitional`).**

---

# 5. REGIME DEFINITIONS

Chaque règle de décision lit exclusivement les 5 dimensions `FusionDimension` (jamais l'Evidence brute directement — voir §9) :

| Régime | Fichier | Dimensions Fusion utilisées | Poids scientifiques |
|---|---|---|---|
| MeanReverting | `MeanRevertingRule.cs` | MeanReversion(.40) + Stationarity(.30) + (1-Persistence)(.20) + StructuralStability(.10) | Provisional (commentaire source) |
| Trending | `TrendingRule.cs` | Persistence(.50) + StructuralStability(.30) + (1-Stationarity)(.20) | Provisional |
| RandomWalk | `RandomWalkRule.cs` | RandomWalk(.60) + (1-Persistence)(.20) + (1-MeanReversion)(.20) | Provisional |
| StableRange | `StableRangeRule.cs` | Stationarity(.40) + MeanReversion(.40) + StructuralStability(.20) | Provisional |
| StructuralBreak | `StructuralBreakRule.cs` | (1-StructuralStability)(.40) + (1-Persistence)(.30) + (1-MeanReversion)(.20) + (1-Stationarity)(.10) | Provisional |

Toutes les 5 règles blendent `0.90×ScientificScore + 0.10×QualityScore`, et **produisent systématiquement un candidat** (`TryReadRequiredDimensions` échoue seulement si une dimension est absente du `FusionResult` — jamais observé en pratique, les 5 dimensions étant toujours présentes après le premier `FusionStateManager.Update`). Chaque dimension manquante contribue une valeur neutre `0.5` (jamais `0.0`), une discipline "ne jamais fabriquer un signal directionnel depuis une absence" documentée dans chaque règle (Sprint 14, DEC-01/FUS-02) — un point fort architectural déjà noté par le Lot 14.9.

`StableRange` et `MeanReverting` partagent leur poids le plus lourd sur les **mêmes deux dimensions** (Stationarity + MeanReversion) — cause structurelle du chevauchement `Corr≈0.965` déjà établi par les Lots 14.14/14.15 (ablation causale directe).

---

# 6. DATA COVERAGE

Sur 10 931 barres Ready :

| Régime | N | % | Runs | AvgRun(barres) | MedianRun | LongestRun(barres) | AvgRun(min) | LongestRun(min) | Classification |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---|
| MeanReverting | 6507 | 59.53% | 267 | 24.37 | 10 | 300 | 191.7 | 4015 (≈66.9h) | REPRESENTED |
| StructuralBreak | 3375 | 30.88% | 263 | 12.83 | 11 | 79 | 71.8 | 3255 (≈54.3h) | REPRESENTED |
| RandomWalk | 483 | 4.42% | 103 | 4.69 | 4 | 19 | 19.6 | 100 (≈1.7h) | UNDERREPRESENTED |
| Trending | 450 | 4.12% | 31 | 14.52 | 7 | 56 | 71.8 | 300 (5h) | UNDERREPRESENTED |
| StableRange | 116 | 1.06% | 11 | 10.55 | 9 | 19 | 47.7 | 90 (1.5h) | UNDERREPRESENTED, fragile |
| Transitional | 0 | 0% | — | — | — | — | — | — | ABSENT (structurel, §4) |
| Unknown | 0 | 0% | — | — | — | — | — | — | ABSENT (jamais 0 candidat, §5) |

Barres Warmup (128, hors analyse) par régime déjà détecté malgré le statut Warmup (le pipeline tourne intégralement même pendant le warmup, seul le *label* diffère) : Trending 52, MeanReverting 27, StableRange 25, StructuralBreak 24, RandomWalk 0.

Distribution quasi identique à celle citée dans le brief (58.7/30.5/4.8/4.7/1.3%) — l'écart de quelques dixièmes de point est cohérent avec la fenêtre Yahoo glissante (mémoire projet : non-déterminisme jour-à-jour attendu, pas une régression).

`StableRange` reste le régime le plus fragile de tout le dataset : 116 barres, 11 runs, jamais plus de 19 barres consécutives (90 minutes) — toute conclusion statistique le concernant individuellement doit être traitée avec une extrême prudence (§19).

---

# 7. DETECTION AUDIT

Aucune modification de `RegimeEngine`/`FusionStateManager`/`DecisionEngine`/`DecisionArbitrator` (protégés). Traçage du fonctionnement réel :

- **Conditions d'activation** : chaque règle calcule un score continu ∈[0,1] à partir de 3-4 dimensions Fusion ; la règle avec le score le plus haut gagne (`OrderByDescending(FinalScore).First()`). Il n'existe **aucun seuil d'activation individuel** par règle — contrairement à ce qu'on pourrait attendre d'un "détecteur de régime", c'est une arbitrage par maximum, pas un test statistique à seuil.
- **Conditions d'exclusion** : aucune — les 5 règles s'évaluent indépendamment et en parallèle sur la même `FusionResult`, jamais l'une conditionnée sur l'échec d'une autre.
- **Fallback `Unknown`** : uniquement si 0 candidat — jamais observé (les 5 règles produisent toujours un candidat valide, confirmé Lot 14.14 et reconfirmé ici : `NWithRunnerUp` = N pour chaque régime dans `regime_decision_stats.csv`, donc au moins 2 candidats systématiquement).
- **`Transitional`** : jamais un candidat, jamais un fallback — simplement absent du répertoire de sortie possible (§4).
- **Persistence/hystérésis** : gérée en amont dans `FusionStateManager`, pas dans `DecisionEngine` lui-même — voir §11-13.

---

# 8. REGIME TRANSITIONS

Matrice complète (barres Ready consécutives, 10 930 transitions observées) :

| De \ Vers | MeanReverting | StructuralBreak | RandomWalk | Trending | StableRange |
|---|---:|---:|---:|---:|---:|
| **MeanReverting** | 6240 (95.90%) | 203 (3.12%) | 51 (0.78%) | 8 (0.12%) | 5 (0.08%) |
| **StructuralBreak** | 193 (5.72%) | 3112 (92.23%) | 50 (1.48%) | 19 (0.56%) | 1 (0.86%*) |
| **RandomWalk** | 53 (10.97%) | 49 (10.15%) | 380 (78.67%) | 2 (0.44%) | 1 (0.21%) |
| **Trending** | 14 (3.11%) | 10 (2.22%) | 2 (0.44%) | 419 (93.11%) | 5 (1.11%) |
| **StableRange** | 6 (5.17%) | 1 (0.86%) | 0 | 4 (3.45%) | 105 (90.52%) |

*(% = part des transitions SORTANTES de la ligne)*

La diagonale domine partout (78.7%-95.9%) — chaque régime a une forte auto-persistance, cohérent avec un système à hystérésis. Les flux croisés dominants : `MeanReverting↔StructuralBreak` (203/193, quasi symétrique — c'est la frontière la plus fréquentée du dataset), `RandomWalk↔StructuralBreak` (49/50), `RandomWalk→MeanReverting` (53, le flux le plus asymétrique : `RandomWalk` "se résout" 2× plus souvent vers `MeanReverting` que l'inverse ne se produit). `StableRange` et `Trending` échangent peu directement avec `RandomWalk` (0-2 transitions) — ils communiquent surtout via `MeanReverting`/`StructuralBreak`.

---

# 9. EVIDENCE COVERAGE

**Découverte centrale de cette section** : `RegimeEngine.Collect` calcule 9 evidences à chaque barre (`Adf`, `Kpss`, `Hurst`, `HalfLife`, `VarianceRatio`, `Cusum`, `Volatility`, `Dfa`, `BaiPerron`), mais **seules 4 des 9 sont consommées** par les 4 `IFusionRule` réellement câblées (`BacktestEngine.RunSignalPipeline` / `IQIAIndicator.cs`) :

| Evidence | Consommée par | Dimension Fusion produite |
|---|---|---|
| `Adf` + `Kpss` | `StationarityRule` | `Stationarity` |
| `Dfa` (Hurst via DFA) + `VarianceRatio` | `PersistenceRule` | `Persistence` |
| `HalfLife` | `MeanReversionRule` | `MeanReversion` |
| `VarianceRatio` | `RandomWalkRule` | `RandomWalk` |
| **`Hurst` (raw, classe `HurstEvidence`, distincte de `Dfa`)** | **AUCUNE** | — |
| **`Cusum`** | **AUCUNE** | — |
| **`BaiPerron`** | **AUCUNE** | — |
| **`Volatility`** | **AUCUNE** | — |

Les 4 evidences non consommées ont un seul et unique consommateur dans tout le dépôt : `IQIAIndicator.cs:1167-1173`, un compteur `count += evidence.X is { IsValid: true } ? 1 : 0` utilisé pour un indicateur de complétude dans le dashboard live — **jamais dans une décision de trading**.

La 5ᵉ dimension Fusion, `StructuralStability`, **n'a pas de source Evidence du tout** : elle est produite par `StructuralStabilityRule.EvaluateAnalysis`, câblée directement à l'intérieur de `FusionStateManager` (jamais dans la liste des `IFusionRule` passée au `EvidenceFusionEngine`), à partir de `FusionProfileAnalyzer.Analyze` — une analyse de la variabilité **temporelle des 4 autres dimensions Fusion STABLE elles-mêmes** (`BehaviourConsistency`, `ProfileStability`, `ProfileVelocity`), injectée via `FusionStateManager.ReplaceStructuralStability`. C'est un signal auto-référentiel sur le comportement du pipeline, pas une mesure statistique indépendante du marché.

Matrice Evidence × Régime (ACTIVE = consommée en Fusion pour au moins une dimension, présente pour tout régime puisque les mêmes 4 règles Fusion tournent sur chaque barre indépendamment du régime gagnant) :

| Evidence | MR | Trend | RW | StableRange | StructuralBreak | Transitional |
|---|---|---|---|---|---|---|
| Adf | ACTIVE | ACTIVE | ACTIVE | ACTIVE | ACTIVE | N/A (jamais atteint) |
| Kpss | ACTIVE | ACTIVE | ACTIVE | ACTIVE | ACTIVE | N/A |
| Dfa | ACTIVE | ACTIVE | ACTIVE | ACTIVE | ACTIVE | N/A |
| HalfLife | ACTIVE | ACTIVE | ACTIVE | ACTIVE | ACTIVE | N/A |
| VarianceRatio | ACTIVE | ACTIVE | ACTIVE | ACTIVE | ACTIVE | N/A |
| Hurst (raw) | INACTIVE | INACTIVE | INACTIVE | INACTIVE | INACTIVE | N/A |
| Cusum | INACTIVE | INACTIVE | INACTIVE | INACTIVE | **INACTIVE** | N/A |
| BaiPerron | INACTIVE | INACTIVE | INACTIVE | INACTIVE | **INACTIVE** | N/A |
| Volatility | INACTIVE | INACTIVE | INACTIVE | INACTIVE | INACTIVE | N/A |

**La colonne StructuralBreak est la plus grave** : c'est le seul régime dont le NOM promet explicitement une détection de rupture, et c'est structurellement le régime dont aucune des deux evidences de rupture disponibles dans le code (`Cusum`, `BaiPerron`) n'entre jamais dans son calcul. Classé **P0** (§35).

---

# 10. EVIDENCE QUALITY

Statistiques descriptives complètes par régime (barres Ready, `regime_evidence_stats.csv`) :

| Régime | Evidence | %Valid | Mean | StdDev | Min | Max |
|---|---|---:|---:|---:|---:|---:|
| MeanReverting | Adf | 100% | -2.030 | 0.943 | -7.368 | 4.255 |
| MeanReverting | Kpss | 100% | 0.354 | 0.149 | 0.065 | 0.645 |
| MeanReverting | HalfLife | 97.8% | 3.92 | 8.61 | 0.66 | 520.3 |
| MeanReverting | VarianceRatio | 100% | 0.807 | 0.369 | 0.125 | 2.381 |
| MeanReverting | Dfa(Hurst) | 100% | 0.449 | 0.110 | 0.144 | 0.859 |
| MeanReverting | Cusum(inutilisée) | 100% | 1016.4 | 2045.9 | 0 | 35032.0 |
| MeanReverting | BaiPerron(inutilisée) | 100% | 4.98 | 1.26 | 0 | 8 |
| StructuralBreak | Adf | 100% | -0.976 | 1.011 | -6.075 | 4.799 |
| StructuralBreak | Kpss | 100% | 0.453 | 0.150 | 0.081 | 0.645 |
| StructuralBreak | HalfLife | 93.9% | 23.26 | 166.60 | 1.97 | 5950.6 |
| StructuralBreak | VarianceRatio | 100% | 0.966 | 0.421 | 0.196 | 2.412 |
| StructuralBreak | Dfa(Hurst) | 100% | 0.470 | 0.118 | 0.154 | 0.901 |
| StructuralBreak | Cusum(inutilisée) | 100% | 5448.5 | 9184.5 | 0 | 85593.8 |
| StructuralBreak | BaiPerron(inutilisée) | 100% | 5.04 | 1.27 | 1 | 8 |
| RandomWalk | VarianceRatio | 100% | 0.993 | 0.077 | 0.585 | 1.272 |
| RandomWalk | Dfa(Hurst) | 100% | 0.486 | 0.111 | 0.225 | 0.819 |
| Trending | Dfa(Hurst) | 100% | 0.770 | 0.106 | 0.486 | 0.993 |
| Trending | HalfLife | 87.8% | 20.66 | 154.36 | 1.58 | 2897.4 |
| Trending | VarianceRatio | 100% | 1.524 | 0.643 | 0.297 | 2.827 |
| StableRange | Adf | 100% | -3.165 | 1.203 | -6.238 | -0.826 |
| StableRange | Dfa(Hurst) | 100% | 0.784 | 0.115 | 0.594 | 1.039 |
| StableRange | VarianceRatio | 100% | 0.968 | 0.419 | 0.358 | 2.384 |

*(table complète des 45 lignes × 9 evidences dans `regime_evidence_stats.csv`)*

**Observations sur la qualité** :

- **Disponibilité** : 100% valide partout sauf `HalfLife`, dont le taux chute nettement dans les régimes non-stationnaires/rares (StructuralBreak 93.9%, Trending 87.8%) — cohérent avec sa définition mathématique (l'estimateur de demi-vie d'Ornstein-Uhlenbeck diverge quand le processus ne revient pas à la moyenne, exactement les cas Trending/StructuralBreak).
- **Discrimination réelle, non exploitée** : `Dfa(Hurst)` et `BaiPerron(BreakCount)`, bien qu'utilisée seulement pour la première, montrent toutes deux un **gradient monotone net et cohérent avec l'intuition économique** à travers les régimes : Dfa(Hurst) MeanReverting 0.449 → StructuralBreak 0.470 → RandomWalk 0.486 → Trending 0.770 → StableRange 0.784 ; BaiPerron(BreakCount) MeanReverting 4.98 → StructuralBreak 5.04 → RandomWalk 5.41 → Trending 5.74 → StableRange 6.04. Le fait que `BaiPerron` (inutilisée en Fusion) montre déjà un gradient monotone exploitable renforce le constat §9 : c'est un signal discriminant **jeté**, pas un signal absent.
- **Aucune saturation détectée** dans les evidences ACTIVE : toutes ont un StdDev non nul et une plage large, y compris dans les régimes rares.
- **Cusum(MaxOfPosNeg)** a une variance extrême et croissante avec la "trendiness" du régime (MeanReverting σ≈2046 → Trending σ≈31 140) — un signal fort de rupture structurelle en dormance, jamais exploité.

---

# 11. NORMALIZATION AUDIT

Repris et étendu par régime (Lot 14.17 avait établi ce mécanisme globalement) : `FusionStateManager.BuildStableResult` applique `EMA(alpha=0.20)` puis un test d'hystérésis `|smoothed - previousStable| >= 0.03` par dimension (sauf `StructuralStability`, remplacée séparément après coup, §9). Fraction des transitions RAW→STABLE absorbées (gelées) par régime (`regime_fusion_raw_vs_stable.csv`) :

| Régime | Stationarity | Persistence | MeanReversion | RandomWalk |
|---|---:|---:|---:|---:|
| MeanReverting | 77.76% | **97.22%** | 78.94% | 68.88% |
| StructuralBreak | 85.60% | **93.75%** | 53.47% | 66.19% |
| RandomWalk | 85.71% | **98.14%** | 63.88% | 40.79% |
| Trending | 81.29% | **74.22%** | 56.42% | 77.78% |
| StableRange | 46.43% | **87.93%** | 79.13% | 58.62% |

**Persistence est SATURÉE partout** (74-98% de gel selon le régime) — confirmant et étendant par régime le résultat du Lot 14.17 (95.0% global). C'est le mécanisme le moins régime-dépendant de tous : même dans `Trending`, où `Persistence` a la variance RAW la plus élevée (StdDev=0.181 vs 0.086-0.111 ailleurs), 74.2% de ses mouvements restent absorbés.

`StableRange` a le comportement le plus atypique : `Stationarity` n'y est gelée que 46.4% du temps (contre 78-86% ailleurs) — cohérent avec un régime où, par définition, la stationnarité RAW varie peu autour d'un point déjà stable, donc chaque petit mouvement franchit plus facilement le seuil relatif. Mais l'échantillon (116 barres) reste trop petit pour trancher entre "propriété réelle du régime" et "bruit d'échantillonnage" (§19).

Classification (brief §9) : **LOW-DISPERSION pour Persistence dans tous les régimes** ; **HEALTHY pour Stationarity/MeanReversion/RandomWalk** (dispersion RAW et STABLE non nulles, gel minoritaire à majoritaire selon régime mais jamais total) ; **UNKNOWN pour StructuralStability** (pas de RAW comparable — voir §9, elle n'a pas de contrepartie RAW puisqu'elle n'est jamais calculée par une `IFusionRule`, seulement injectée après coup).

---

# 12. FUSION AUDIT

`StdDev` RAW vs STABLE par régime × dimension (`regime_fusion_raw_vs_stable.csv`, table complète) — dans tous les cas STABLE < RAW (l'hystérésis réduit toujours la dispersion, jamais ne l'amplifie, contrairement à une hypothèse "amplification" qui aurait pu être envisagée) :

| Régime | Dim | RawStdDev | StableStdDev | Réduction |
|---|---|---:|---:|---:|
| MeanReverting | Stationarity | 0.207 | 0.152 | -27% |
| MeanReverting | Persistence | 0.0856 | 0.0367 | -57% |
| MeanReverting | MeanReversion | 0.177 | 0.105 | -41% |
| MeanReverting | RandomWalk | 0.234 | 0.127 | -46% |
| StructuralBreak | MeanReversion | 0.233 | 0.146 | -37% |
| Trending | MeanReversion | 0.279 | 0.194 | -30% |
| StableRange | Stationarity | 0.209 | 0.099 | -53% |

Aucune contradiction/saturation détectée entre dimensions (poids fixes, jamais renormalisés dynamiquement). Le fallback "valeur neutre 0.5" (§5) n'a jamais été exercé sur ce dataset (`%IsValid`=100% pour toutes les evidences ACTIVE, §10) — la discipline anti-fabrication documentée dans le code n'a donc pas pu être testée en conditions réelles ici ; elle reste vérifiée uniquement par les tests synthétiques existants (Lot 14.9's "DEC-01/FUS-02").

---

# 13. PERSISTENCE AUDIT

Extension par régime du résultat déjà établi globalement au Lot 14.17 :

| Régime | RawStdDev | StableStdDev | %Gelé (RawChangé&StableFrozen) |
|---|---:|---:|---:|
| MeanReverting | 0.0856 | 0.0367 | 97.22% |
| StructuralBreak | 0.1115 | 0.0439 | 93.75% |
| RandomWalk | 0.0908 | 0.0461 | 98.14% |
| Trending | 0.1814 | 0.1260 | 74.22% |
| StableRange | 0.1086 | 0.1050 | 87.93% |

Le gel n'est **jamais** absent dans un régime — même `Trending`, le moins figé (74.2%), reste majoritairement gelé. Ceci confirme et durcit la conclusion secondaire du Lot 14.17 ("REGIME DEPENDENT comme facteur secondaire") : la dépendance au régime existe (25 points d'écart entre `Trending` et `RandomWalk`) mais n'annule jamais le phénomène — le mécanisme causal primaire reste `FusionStateManager`'s EMA+hystérésis (`HysteresisThreshold=0.03`), inchangé et non recalibré ici.

---

# 14. DECISION AUDIT

| Régime | N | MeanWinnerScore | StdDev | MeanScoreDifference | MedianScoreDifference |
|---|---:|---:|---:|---:|---:|
| MeanReverting | 6507 | 0.6316 | 0.0513 | 0.0427 | 0.0445 |
| StructuralBreak | 3375 | 0.5832 | 0.0240 | 0.0641 | 0.0502 |
| RandomWalk | 483 | 0.6238 | 0.0383 | 0.0406 | 0.0351 |
| Trending | 450 | 0.6094 | 0.0505 | 0.0935 | 0.0723 |
| StableRange | 116 | 0.6530 | 0.0374 | 0.0249 | 0.0188 |

`NWithRunnerUp` = N pour chaque régime : **au moins 2 candidats systématiquement**, cohérent avec le constat du Lot 14.14 (5 candidats sur 5 règles à chaque barre, artefact du bug vestigial `finalScore > builder.Confidence` — `DecisionResultBuilder` recréé neuf à chaque itération, donc la comparaison compare toujours à `0.0`, P3, sans impact comportemental confirmé de nouveau ici).

`StableRange` a la marge de victoire la plus faible de tous les régimes (`MeanScoreDifference=0.0249`, médiane 0.0188) — ses propres victoires sont les MOINS décisives de tout le dataset, malgré (ou à cause de) son très faible effectif. `Trending` a la marge la plus large (0.0935) — ses victoires sont les plus nettes.

---

# 15. AMBIGUITY AUDIT

Extension par régime du résultat du Lot 14.13/14.14 (`AmbiguityScore = Clamp(1 - Difference, 0, 1)`, seuil de production `AmbiguityGateThreshold = 0.95`, **inchangé**) :

| Régime | N | Mean | Median | P25 | P75 | %≥0.95 |
|---|---:|---:|---:|---:|---:|---:|
| MeanReverting | 6507 | 0.9573 | 0.9555 | 0.9445 | 0.9666 | **64.42%** |
| StructuralBreak | 3375 | 0.9359 | 0.9498 | 0.9071 | 0.9779 | 49.78% |
| RandomWalk | 483 | 0.9594 | 0.9649 | 0.9369 | 0.9862 | 67.08% |
| Trending | 450 | 0.9065 | 0.9277 | 0.8511 | 0.9708 | 38.00% |
| StableRange | 116 | 0.9751 | 0.9812 | 0.9591 | 0.9924 | 85.34% |

**Confirmation directe et exacte** (brief §12, "Regime Effect") : le taux `%≥0.95` de `MeanReverting` (64.42%) est **exactement** le complément du taux de conversion directionnelle mesuré en aval (§16) : `TriggerNO_ACTION/ReadyBars = 4192/6507 = 64.42%`, et `(BUY+SELL)/ReadyBars = 2315/6507 = 35.58% = 100% - 64.42%`. C'est la preuve causale directe, sur ce dataset précis, que le gate d'ambiguïté est **le mécanisme exact et unique** qui détermine combien de barres `MeanReverting` produisent réellement un signal — cohérent avec la lecture du code (§16) mais désormais chiffré.

`StableRange`, bien qu'il ne puisse jamais atteindre ce test (§16, régime bloqué en amont), aurait — s'il l'atteignait — le taux d'ambiguïté le plus élevé de tous (85.34% ≥ 0.95), donc le moins de marge : ses propres victoires sont les plus fragiles (cohérent avec §14). `Trending` a l'ambiguïté la plus basse (38.00%) — ses victoires seraient les plus exploitables si son Signal n'était pas structurellement bloqué (§16).

---

# 16. SIGNAL COVERAGE

**Résultat central du lot, chiffré.**

| Régime | ReadyBars | SignalProduced | EntryCandidate | BUY | SELL | NO_ACTION | WATCH |
|---|---:|---:|---:|---:|---:|---:|---:|
| MeanReverting | 6507 | 6507 (100%) | 6507 (100%) | 1166 | 1149 | 4192 | 0 |
| StructuralBreak | 3375 | 3375 (100%) | **0 (0%)** | 0 | 0 | 3375 | 0 |
| RandomWalk | 483 | 483 (100%) | **0 (0%)** | 0 | 0 | 483 | 0 |
| Trending | 450 | 450 (100%) | **0 (0%)** | 0 | 0 | 450 | 0 |
| StableRange | 116 | 116 (100%) | **0 (0%)** | 0 | 0 | 116 | 0 |

`SignalProduced` (`OpportunityPresentation` non nul, ou `ExecutedModels.Count>0` selon la définition de ce lot) est 100% partout — un artefact de câblage (`SignalEngine.Process` s'exécute sans condition de régime), **pas** une preuve de couverture réelle : dès l'étape suivante (`EntryCandidate`), 4 des 5 régimes tombent à zéro net.

**Cause racine, lue directement dans `EntryTriggerBuilder.DetermineDirection` (fichier protégé, lecture seule)** :

> *"the ONLY methodology backed by real, non-placeholder scientific models capable of a directional read is MeanReversionMethodology... No other regime currently has a model that can support a reliable BUY/SELL call — producing one anyway would be exactly the fabricated signal this sprint is required to avoid, so every other regime... returns NO_ACTION regardless of any raw metric."*

```csharp
if (decision.Winner != MarketState.MeanReverting) {
    suppressionReason = $"...regime={decision.Winner} has no scientific model capable of a directional read...";
    return DirectionCandidate.NO_ACTION;
}
```

Confirmé indépendamment par `MethodologyRegistry`/`ScientificModelRegistry` (§9's sibling audit) :

| Méthodologie | Modèles déclarés | Modèles réellement fonctionnels |
|---|---|---|
| MeanReversionMethodology | Kalman Filter, Dynamic Z-Score, Volatility, SPRT, Ornstein-Uhlenbeck | **5/5, tous testés, réels** |
| TrendFollowingMethodology | Time Series Momentum, BOCPD | **0/2** (stubs : `Success=false` toujours) |
| StructuralBreakMethodology | BOCPD | **0/1** (stub) |
| RandomWalkMethodology | "Random Walk Null Model" | **0/1** (aucune classe de ce nom n'existe) |
| StableRangeMethodology | — | **0** (explicitement "no scientific methodology is implemented yet") |
| TransitionalMethodology | — | **0** (idem, et de toute façon inatteignable, §4) |

Ce n'est donc ni une "détection manquante" (CASE B) ni un "filtrage aval accidentel" (CASE D) au sens ambigu du brief — c'est une **règle métier explicite, volontaire, auto-documentée** : le système refuse consciemment de fabriquer un signal directionnel là où il n'a pas de modèle scientifique validé. Un choix de conception défendable en soi (éviter le signal fabriqué), mais qui signifie concrètement que **le système ne "trade" qu'un seul des cinq régimes qu'il classe**.

---

# 17. ENTRY COVERAGE

Conséquence directe de §16 : `EntryEngine`/`EntryCandidate` ne produisent une opportunité qualifiée que pour `MeanReverting`. Sur ce régime :

```
ReadyBars=6507 → EntryCandidate=6507 (100%, OpportunityStatus != NOT_QUALIFIED)
→ BUY=1166 (17.9%) + SELL=1149 (17.7%) + NO_ACTION=4192 (64.4%, gate d'ambiguïté, §15) + WATCH=0 (0%)
```

`WATCH=0` partout est notable : la voie `OpportunityStatus.WATCHLIST` (qui court-circuite le test d'ambiguïté dans `DetermineDirection`, §16's code) n'a jamais été empruntée sur ce dataset — soit `EntryEngine` ne produit jamais ce statut ici, soit son taux de déclenchement est nul sur cette fenêtre. Non investigué plus loin (hors scope Entry — `EntryEngine` est protégé).

Pour les 4 autres régimes : **aucune opportunité n'est jamais qualifiée** — l'audit ne peut pas distinguer "EntryEngine rejette" de "EntryTriggerBuilder bloque en aval" sans instrumenter `EntryEngine` lui-même (protégé) ; la cause structurelle identifiée en §16 (`DetermineDirection`) suffit à expliquer 100% de l'absence observée, donc ce n'est pas ambigu ici (CASE D exclu par preuve directe de code, pas par supposition).

---

# 18. EXECUTION COVERAGE

```
MeanReverting : PositionOpened=2315 (35.58% des ReadyBars), PositionClosed=2315 (100% des ouvertes)
Tous les autres : PositionOpened=0, PositionClosed=0
```

**Point d'attention architectural, pas un défaut caché** (documenté depuis le Lot 14.10, mais devant être explicité pour l'interprétation correcte des chiffres de ce lot) : `ExecutionSimulator.SimulateCore` ouvre une position dès qu'un `ExecutionCandidate` directionnel existe (`BUY_CANDIDATE`/`SELL_CANDIDATE`), **indépendamment du `TradePlan.Status`**. Sur ce dataset, `TradePlanPLAN_READY=0` partout (§19) — les 2315 positions ouvertes proviennent donc toutes de `TradePlan.Status=SIGNAL_ONLY` (StopLoss/PositionSize absents). Le modèle d'exécution du Backtest est un **exit à horizon temporel fixe** (`Fill=Open[SignalBarIndex+1]`, sortie à `Open[SignalBarIndex+1+HorizonBars]`), entièrement **découplé** du concept `TradePlan.StopLoss/TakeProfit`. Les mesures de performance de ce lot (§20) reflètent donc une stratégie "entrée directionnelle + sortie à horizon fixe", **jamais** une stratégie "entrée + Stop Loss + Take Profit" — cette dernière n'existe nulle part en production (§21).

Fill bias (Lot 14.9 P0-1, "Fill=Close[i] même barre que le signal") : **corrigé**, confirmé par lecture directe du diff non commité — `entryPrice = fillBar.Open` où `fillBar = bars[SignalBarIndex+1]`, exactement le modèle "Fill=Open[i+1]" que ce brief lui-même cite en référence (§16 du brief).

---

# 19. MEASUREMENT COVERAGE

```
MeanReverting : N=2315, RELIABLE (≥30)
  MeanReturn=2.40e-05, MedianReturn=3.20e-05, StdDevReturn=0.001286
  HitRate(seuil 0.001)=29.72%, MeanMAE=0.000862, MeanMFE=0.000907
Tous les autres régimes : N=0, LOW_N_UNRELIABLE (aucun signal mesuré, pas seulement <30)
```

**Aucune conclusion de performance n'est possible pour `StructuralBreak`/`RandomWalk`/`Trending`/`StableRange`** — ce n'est pas une limite de puissance statistique (brief §17, "les régimes sous-représentés ne doivent pas produire de conclusions fortes") mais une absence totale et structurelle de mesure, cascade directe de §16.

Pour `MeanReverting` lui-même : `HitRate=29.72%` sur un seuil de retour de `0.001` (10 ticks MES) est une mesure purement descriptive de la sortie à horizon fixe (§18) — elle ne dit rien sur la performance d'une éventuelle stratégie Stop Loss/Take Profit, qui n'existe pas en production.

---

# 20. RISK COVERAGE

```
MeanReverting : Allowed=2315 (35.58%), PositionNotExecutable=4192 (64.42%), toutes les autres raisons=0
Tous les autres régimes : PositionNotExecutable=100%, toutes les autres raisons=0
```

Fait notable : sur l'intégralité des 11 059 barres, **seules 2 des 9 valeurs de `PositionRiskReason` sont jamais observées** (`Allowed`, `PositionNotExecutable`). `InvalidConfiguration`, `InvalidInstrument`, `InvalidRiskDistance`, `RiskLimitExceeded`, `MaxQuantityExceeded`, `ExposureLimitExceeded`, `ZeroRisk` — les chemins de rejet les plus riches du Risk Engine (budget de risque, limites de drawdown/perte journalière, taille maximale) — **ne se déclenchent jamais** sur ce dataset, avec la `RiskPolicy` minimale utilisée ici (`MaxRiskPerTradePercent=0.02`, tout le reste `null`). Impossible de déterminer si ces chemins fonctionnent correctement (**CASE E**, brief §4) — ils ne sont ni prouvés fiables, ni prouvés défaillants, simplement jamais exercés par ce scénario particulier.

`Risk input available?` : oui pour `MeanReverting` (2315 positions closes évaluées). `Risk assessment produced?` : oui, binaire (Allowed/NotExecutable seulement). `Position sizing possible?` : **non observé positivement** — `PositionRiskOutcome.Allowed` ne garantit pas ici un dimensionnement réel puisque `TradePlan.PositionSize` est toujours `null` (StopLoss absent, §19/21) ; la voie `Allowed` du Backtest Risk wrapper (`BacktestRiskResultBuilder`) suit une logique de sizing propre au Lot 14.8, distincte du `TradePlanBuilder.PositionSize` (jamais peuplé) — à ne pas confondre (§24).

---

# 21. STOP LOSS COVERAGE

**Absence globale, confirmée à la source, indépendante du régime.**

`TradePlanBuilder.cs:57` : `decimal? stopLoss = context.RiskParameters?.StopLoss;` — `TradePlanContext.RiskParameters` (type `TradeRiskParameters?`) est **toujours `null`** dans les deux seuls appelants de production :

```
IQIAIndicator.cs:602  → new TradePlanContext(entryTriggerCandidate, instrumentInfo)          // RiskParameters omis
BacktestEngine.cs:345 → new TradePlanContext(entryTrigger, instrumentInfo)                     // RiskParameters omis
```

Seuls des tests unitaires (`TradePlanBuilderTests.cs`, `RiskEngineIntegrationTests.cs`) construisent jamais un `TradeRiskParameters` réel — confirmé par le propre commentaire du dépôt : *"IQIAIndicator.cs) always passes RiskParameters=null today and can only ever reach NO_TRADE or [SIGNAL_ONLY]"*. Un point d'extension existe (`IStopLossStrategy`), mais sa seule implémentation, `ProvidedStopLossStrategy`, est un pass-through qui ne dérive jamais rien elle-même — et de toute façon rien ne l'appelle depuis `TradePlanBuilder`.

**Confirmation empirique exacte** : `TradePlanPLAN_READY = 0` pour `MeanReverting` (les 2315 TradePlans produits sont tous `SIGNAL_ONLY`) et `0` partout ailleurs. `SL available? Non, nulle part. SL produced? Jamais. SL missing? Toujours` — 100% des cas, tous régimes confondus, y compris le seul régime qui a un signal directionnel.

C'est un fait déjà connu du projet (référencé dans `QDE-011_Risk_Engine_Theory.md`/`QDE-012_StopLoss_Calibration_Protocol.md`, sujet d'une recherche active dans `Tests/Research/StopLossCalibration/`) — ce lot le reconfirme et le quantifie dans le contexte précis de la maturité par régime : **c'est un blocage GLOBAL, pas un déficit spécifique aux 4 régimes sans Signal**. Même si `Trending`/`StructuralBreak`/`RandomWalk`/`StableRange` avaient chacun une méthodologie scientifique complète demain, aucun d'eux ne pourrait produire un `TradePlan` complet sans qu'un Stop Loss de production soit d'abord implémenté.

---

# 22. COST COVERAGE

```
MeanReverting : N=2315, MeanTotalCost=10.50 (fixture de test "EnabledTestCosts": slippage 0.25, spread 0.5,
                commission 2+0.5/unité, fees 0.25 — PAS un barème de courtier réel)
Tous les autres régimes : N=0
```

Confirme le Lot 14.9 : `ExecutionCostConfiguration.Disabled` reste la configuration par défaut recommandée en production ; ce lot a délibérément activé une fixture de coûts de test (reprise telle quelle depuis `BacktestFullResultWithRiskCostActivationTests.cs`, jamais inventée) uniquement pour démontrer que le mécanisme de coût est structurellement câblé et produit un chiffre — pas pour évaluer une performance nette réelle. **Aucun barème de coût réel (spread/commission MES effectifs) n'existe dans le dépôt** — Net PnL n'est donc disponible qu'au sens structurel (le calcul tourne), jamais au sens économique.

---

# 23. REGIME MATURITY MATRIX

| Régime | Data | Detection | Evidence | Fusion | Decision | Signal | Entry | Execution | Measurement | Risk | Cost |
|---|---|---|---|---|---|---|---|---|---|---|---|
| **MeanReverting** | COMPLETE | COMPLETE | PARTIAL | PARTIAL | PARTIAL | COMPLETE | PARTIAL | PARTIAL | COMPLETE | PARTIAL | EXPERIMENTAL |
| **StructuralBreak** | COMPLETE | EXPERIMENTAL | MISSING | PARTIAL | PARTIAL | MISSING | MISSING | MISSING | MISSING | MISSING | MISSING |
| **RandomWalk** | PARTIAL | PARTIAL | PARTIAL | PARTIAL | PARTIAL | MISSING | MISSING | MISSING | MISSING | MISSING | MISSING |
| **Trending** | PARTIAL | PARTIAL | PARTIAL | PARTIAL | PARTIAL | MISSING | MISSING | MISSING | MISSING | MISSING | MISSING |
| **StableRange** | PARTIAL | PARTIAL | PARTIAL | PARTIAL | PARTIAL | MISSING | MISSING | MISSING | MISSING | MISSING | MISSING |
| **Transitional** | MISSING | MISSING | N/A | N/A | MISSING | MISSING | MISSING | MISSING | MISSING | MISSING | MISSING |
| **Unknown** | MISSING(fallback jamais atteint) | COMPLETE(fallback sûr) | N/A | N/A | MISSING | MISSING | MISSING | MISSING | MISSING | MISSING | MISSING |

Notes de lecture : "PARTIAL" en Evidence/Fusion/Decision pour `MeanReverting` reflète des mécanismes réels mais non calibrés (poids "Provisional", chevauchement `StableRange`) — pas une absence. "PARTIAL" pour Data (RandomWalk/Trending/StableRange) reflète un volume exploitable mais fragile (§6), pas une absence. "EXPERIMENTAL" pour la Detection de `StructuralBreak` reflète le mécanisme auto-référentiel de `StructuralStability` (§9), scientifiquement plus faible qu'une détection directe.

---

# 24. GLOBAL VS REGIME-DEPENDENT COMPONENTS

| Composant | Classification | Justification |
|---|---|---|
| RegimeEngine (calcul Evidence) | GLOBAL | Tourne identiquement pour toute barre, indépendant du régime résultant |
| EvidenceFusionEngine (4 règles) + FusionStateManager | GLOBAL | Les 4 `IFusionRule` + hystérésis tournent identiquement quel que soit le Winner à venir |
| DecisionEngine / DecisionArbitrator | GLOBAL | Les 5 règles s'évaluent toujours toutes, sans branche conditionnelle par régime |
| `AmbiguityGateThreshold` (0.95) | **REGIME DEPENDENT dans son EFFET, GLOBAL dans sa DÉFINITION** | Un seul seuil, appliqué uniformément — mais n'a d'effet mesurable que sur `MeanReverting` (§15), les 4 autres régimes ne l'atteignant jamais |
| `EntryTriggerBuilder.DetermineDirection` | **REGIME DEPENDENT, explicitement** | Bloc `if (decision.Winner != MarketState.MeanReverting) return NO_ACTION` — la seule vraie branche régime-conditionnelle de toute la chaîne aval |
| MethodologyRegistry / ScientificModelRegistry | REGIME DEPENDENT | Table de correspondance explicite par régime, c'est leur fonction même |
| TradePlanBuilder (StopLoss) | GLOBAL (absence) | `RiskParameters=null` indépendamment du régime — le seul goulot vraiment universel |
| ExecutionSimulator (fill/horizon) | GLOBAL | Convention Open[i+1]/Horizon fixe, aucune branche par régime |
| RiskPolicy / limites | GLOBAL (mais jamais exercé au-delà de 2 issues, §20) | Une seule politique, aucune segmentation par régime dans le code |

**Conclusion** : le pipeline Evidence→Fusion→Decision est authentiquement GLOBAL et régime-agnostique (les 5 dimensions et 5 règles tournent identiquement). C'est précisément **une seule ligne** (`EntryTriggerBuilder.DetermineDirection`) qui introduit toute la dépendance au régime observée dans ce lot — une dépendance explicite, documentée, volontaire, mais qui a des conséquences en cascade sur 6 des 11 colonnes de la matrice de maturité (§23).

---

# 25. HIDDEN DEPENDENCIES

1. **`StructuralStability` dépend de sa propre histoire, pas du marché** (§9/§11) — un composant censé mesurer la stabilité structurelle du marché mesure en réalité la stabilité récente des 4 AUTRES sorties du même pipeline. Une dépendance circulaire douce (le pipeline mesure sa propre variance de sortie et l'appelle "structure").
2. **`Cusum`/`BaiPerron` : evidence calculée, jamais lue** (§9/§10) — un investissement de calcul (deux modèles complets, validés par golden datasets — `Tests/GoldenDatasets/BaiPerronValidationTests.cs`, `CusumValidationTests.cs`) totalement inerte en production. Le gradient monotone observé (§10) suggère qu'il ne s'agit pas d'un choix scientifique délibéré documenté quelque part, mais d'un oubli de câblage — aucun commentaire dans `EvidenceFusionEngine`/`BacktestEngine` n'explique pourquoi ces deux règles Fusion n'existent pas.
3. **`AmbiguityGateThreshold` calibré (Lots 4-9/14.13) sur un pipeline dont on ignorait qu'il ne s'applique qu'à 59.5% du dataset** (§2/§15) — toute conclusion antérieure sur ce seuil doit être relue comme "calibration de MeanReverting", jamais "calibration du système".
4. **`OverallConfidence`** (Lot 14.9/14.14 : quasi-binaire, 1.0 exactement si Winner=MeanReverting, 0.0 sinon) **s'explique maintenant architecturalement, pas juste empiriquement** : `ScientificModelRegistry.Resolve` renvoie `Array.Empty<IScientificModel>()` pour toute méthodologie hors `MeanReversionMethodology` — `SuccessfulModels/TotalModels` est donc `0/0` ou `0/N` par construction pour 4 régimes sur 5, jamais un vrai ratio de confiance mesuré.
5. **Le sizing "Allowed" du Backtest Risk wrapper (§20) et le `PositionSize` du `TradePlanBuilder` (§21) sont deux calculs de risque INDÉPENDANTS** (Lot 14.8 vs Lot 15.8) — l'un fonctionne (Allowed=2315), l'autre est toujours `null` (StopLoss absent). Un lecteur du seul chiffre "Allowed" pourrait croire, à tort, que le sizing de production fonctionne.
6. **L'exécution du Backtest ignore `TradePlan.Status`** (§18) — un `TradePlan=SIGNAL_ONLY` (le SEUL statut jamais observé pour un candidat directionnel) déclenche quand même une position réelle dans `ExecutionSimulator`. Le concept "TradePlan" et le concept "position simulée" ne sont, à ce jour, pas la même chose.

---

# 26. CALIBRATION READINESS

| Famille | Statut | Justification |
|---|---|---|
| REGIME ENGINE | READY | Evidence pure, déterministe, sans état caché problématique (Lot 14.17) |
| EVIDENCE | **NOT READY** | 4/9 evidences calculées jamais utilisées (§9) ; calibrer Fusion sans d'abord décider si elles doivent l'être serait prématuré |
| FUSION | NOT READY | Poids "Provisional" jamais calibrés ; StructuralStability auto-référentielle (§9) fausse la base de toute calibration |
| DECISION | NOT READY | Poids "Provisional" ; chevauchement StableRange/MeanReverting non résolu (Lots 14.14-14.16) |
| SIGNAL | **NOT READY (4/5 régimes)** | Aucune méthodologie scientifique fonctionnelle hors MeanReverting (§16) |
| ENTRY | NOT READY (4/5 régimes) | Cascade directe de Signal |
| EXECUTION | READY (mécanique) mais **DECOUPLED du TradePlan** | Fill/horizon fonctionnent et sont corrects (§18), mais ne représentent pas encore un P&L "de production" |
| MEASUREMENT | READY (mécanique), NOT READY (couverture) | Fonctionne parfaitement pour MeanReverting, aucune donnée pour les 4 autres |
| RISK | **NOT READY** | 7/9 raisons de rejet jamais exercées (CASE E, §20) ; sizing réel bloqué par l'absence de Stop Loss |
| STOP LOSS | **MISSING** | Absence globale confirmée à la source (§21) |
| COST | NOT READY | Pas de barème réel, seulement une fixture de test (§22) |
| **GLOBAL CALIBRATION** | **NOT READY** | Règle §26 du brief : un seul composant critique insuffisamment mature bloque la calibration globale — ici, au moins 3 le sont (Signal/Entry pour 4 régimes, Stop Loss, Evidence sous-exploitée) |

---

# 27. DATA LIMITATIONS

Le dataset Yahoo M5 (~59 jours) est une **limitation du fournisseur de données**, jamais un défaut du code (brief §27). Conséquences précises pour ce lot :

- `StableRange` (116 barres, 11 runs, jamais >19 barres consécutives) : tout écart observé (ex. §11, gel de Stationarity plus faible) pourrait être un vrai effet de régime OU un artefact de petit échantillon — **impossible à trancher avec ce dataset seul** (CASE E).
- `RandomWalk`/`Trending` (450-483 barres) : suffisants pour des statistiques descriptives globales mais pas pour des analyses fines (ex. sous-segmentation temporelle) sans tomber en dessous de tout seuil de fiabilité raisonnable.
- Aucune conclusion de ce lot sur `Signal`/`Entry`/`Execution`/`Measurement`/`Risk`/`Cost` pour les 4 régimes hors `MeanReverting` n'est une question de taille d'échantillon — c'est un blocage architectural à 0 par construction, qui persisterait identiquement sur un dataset de 10 ans (§16).
- La fenêtre Yahoo étant glissante, ce rapport n'est pas reproductible bit-pour-bit — les ordres de grandeur (répartition des régimes, magnitudes) sont attendus stables (cohérent avec la mémoire projet sur la non-déterminisme Yahoo), pas les chiffres exacts.

---

# 28. REQUIRED FUTURE WORK

Classé par la question que chaque futur lot répondrait, jamais par une solution imposée ici (hors scope, §28 du brief) :

- **DATA SOURCE NEEDED** : aucune — le dataset actuel suffit pour établir tous les constats de ce lot ; une source plus longue (ATAS live une fois débloqué, ou un historique M5 pluriannuel) améliorerait la fiabilité de `StableRange`/`RandomWalk`/`Trending` mais ne changerait rien au blocage architectural de §16/§21, qui n'est pas un problème de données.
- **Lot correctif dédié — Signal/Entry multi-régime** : implémenter (ou explicitement abandonner) `TimeSeriesMomentumModel`/`BOCPDModel`/un modèle Random Walk réel, seule voie pour que `Trending`/`StructuralBreak`/`RandomWalk` produisent un jour un signal.
- **Lot correctif dédié — Stop Loss** : le sujet déjà en recherche active (`Tests/Research/StopLossCalibration/`) doit aboutir à une méthodologie de production branchée dans `TradeRiskParameters`, seule voie pour que `PLAN_READY` devienne atteignable.
- **Lot correctif dédié — Evidence de rupture structurelle** : décider explicitement si `Cusum`/`BaiPerron` doivent être câblées en Fusion (le gradient §10 suggère qu'elles le devraient) ou documenter pourquoi elles resteront inertes.
- **Lot d'investigation — StructuralStability** : évaluer si un signal de rupture structurelle indépendant (basé sur Cusum/BaiPerron plutôt que sur l'auto-référence actuelle) changerait la détection de `StructuralBreak`.
- **Lot de découplage Execution/TradePlan** (§18/§25) : décider explicitement si le Backtest doit continuer à exécuter des `SIGNAL_ONLY` TradePlans, ou n'exécuter que des `PLAN_READY` une fois le Stop Loss disponible — actuellement un choix implicite, jamais statué.

---

# 29. PRIORITY CLASSIFICATION

### P0 — bloque la validité scientifique du système

1. **Signal/Entry directionnel restreint à `MeanReverting` seul** (§16/§23/§24) — auto-documenté dans le code, confirmé empiriquement à 0% ailleurs. Bloque la validité de toute affirmation "IQIA supporte N régimes" — c'est précisément la question que ce lot a été commandé pour trancher.
2. **Stop Loss globalement absent** (§21) — bloque `PLAN_READY` pour 100% des barres, tous régimes confondus, y compris `MeanReverting`. Bloque le Risk Engine de production et toute mesure de performance "réaliste" (avec gestion du risque).
3. **`StructuralBreak` (30.9% du dataset) détecté sans aucune evidence de rupture structurelle** (§9) — sa "Detection" repose sur un signal auto-référentiel (§25.1), pas sur `Cusum`/`BaiPerron`, qui existent, sont validés (golden datasets) et montrent un gradient exploitable (§10) mais ne sont jamais consommés. Compromet l'admissibilité scientifique du deuxième régime le plus fréquent.

### P1 — bloque une fonctionnalité majeure

4. Poids Fusion/Decision "Provisional", jamais calibrés empiriquement (Lot 14.9, confirmé inchangé).
5. Execution/TradePlan découplés (§18/§25.6) — toute mesure de performance actuelle reflète un horizon fixe, pas une stratégie SL/TP.
6. 7/9 `PositionRiskReason` jamais exercés sur ce dataset (§20) — capacités du Risk Engine non vérifiables en pratique (CASE E).
7. `Hurst` (raw), `Volatility` calculées, jamais consommées (§9) — moins critique que Cusum/BaiPerron (pas de gradient démontré ici) mais même schéma de gaspillage.
8. `CandidateCount=5` systématique — bug vestigial confirmé sans impact comportemental (Lot 14.14, reconfirmé §14), mais masque la vraie compétitivité de l'arbitrage.

### P2 — réduit la qualité sans tout bloquer

9. `AmbiguityGateThreshold=0.95` sur pente raide pour `MeanReverting` (Lot 14.13, reconfirmé §15) — n'affecte qu'un seul régime, jamais calibré sur cette base élargie.
10. `StableRange` extrêmement sous-représenté (1.06%, 116 barres) — toute conclusion le concernant reste fragile par construction de l'échantillon.
11. Deux enums "régime" coexistent (`RegimeType` mort vs `MarketState` vivant, §4) — risque de confusion pour un futur contributeur, sans impact runtime.

### P3 — dette technique

12. `MarketState.Transitional` : enum + méthodologie dédiée entièrement câblés en aval, structurellement inatteignable en amont (§4) — nettoyage candidat, hors scope ici.
13. `RegimeType`/`RegimeResult`/`RegimeConfidence`/`Engine.Regime.Core.EvidenceFusionEngine` : stub mort du Sprint 2.4 (§4) — candidat à suppression dans un lot dédié, jamais dans un lot d'audit.

---

# 30. HANDOFF CONTEXT

Voir bloc final ci-dessous (format exact requis par le brief §37).

---

# HANDOFF CONTEXT — NEXT SESSION

```
PROJECT:
IQIA

CURRENT LOT:
15.0

LAST COMPLETED:
14.17

CALIBRATION STATUS:
PAUSED

REASON:
REGIME SYSTEM MATURITY AUDIT

DATASET:
MES=F
M5
~59 days (2026-06-26 -> 2026-08-24, 11059 bars, fingerprint 1617CBAA...31FE8)

REGIME COVERAGE:
MeanReverting 6507/10931 Ready (59.53%) | StructuralBreak 3375 (30.88%) | RandomWalk 483 (4.42%) |
Trending 450 (4.12%) | StableRange 116 (1.06%) | Transitional 0 (structurally unreachable, confirmed
empirically) | Unknown 0 (never observed, 5/5 rules always produce a candidate)

REGIME MATURITY:
MeanReverting is the only regime with any Signal/Entry/Execution/Measurement/Risk/Cost coverage.
StructuralBreak/RandomWalk/Trending/StableRange are MISSING from Signal onward (0% EntryCandidate,
confirmed empirically down to a hard architectural wall in EntryTriggerBuilder.DetermineDirection).

REGIME MATRIX:
See report Section 23. Summary: MeanReverting=PARTIAL-to-COMPLETE across the whole chain (best-covered
but still Provisional weights and no Stop Loss). All 4 other live regimes=COMPLETE/PARTIAL only through
Decision, then MISSING from Signal onward. Transitional/Unknown=MISSING/N-A (never reached).

CRITICAL P0:
(1) Directional signal generation is hard-restricted to MarketState.MeanReverting only - self-documented
in EntryTriggerBuilder.DetermineDirection, confirmed 0% EntryCandidate on all 4 other regimes (10,924
Ready bars combined). (2) Stop Loss is globally absent (TradePlanContext.RiskParameters always null in
both IQIAIndicator.cs and BacktestEngine.cs) - PLAN_READY is unreachable everywhere, confirmed 0/2315
TradePlans reaching PLAN_READY. (3) StructuralBreak (30.9% of dataset) is detected with zero structural-
break evidence - Cusum/BaiPerron are computed every bar but never consumed by any IFusionRule;
FusionDimension.StructuralStability is instead a self-referential signal derived from the Fusion
pipeline's own recent output variability (StructuralStabilityRule + FusionProfileAnalyzer), injected
directly inside FusionStateManager, never one of the 4 IFusionRule wired into EvidenceFusionEngine.

CRITICAL P1:
Fusion/Decision weights still "Provisional" (source comments), never empirically calibrated. Backtest
execution is decoupled from TradePlan (positions open even when TradePlan.Status=SIGNAL_ONLY, the only
status ever observed - never PLAN_READY) - all Measurement numbers reflect a fixed-horizon exit, not a
Stop-Loss/Take-Profit strategy. 7 of 9 PositionRiskReason values never fire on this dataset (only
Allowed/PositionNotExecutable observed) - deeper Risk Engine paths unverified (CASE E). Raw HurstEvidence
and VolatilityEvidence are computed every bar but consumed nowhere (dashboard validity counter only).
DecisionEngine's builder.Confidence comparison is vestigial (fresh DecisionResultBuilder per rule) -
CandidateCount=5 always, confirmed again this lot, no behavioural impact, obscures true arbitration
competitiveness (Lot 14.14 finding, unchanged).

P2:
AmbiguityGateThreshold=0.95 only ever matters for MeanReverting (confirmed exactly: 64.42% of
MeanReverting Ready bars have AmbiguityScore>=0.95, which equals exactly the 64.42% NO_ACTION rate
measured downstream). StableRange is critically underrepresented (116 bars, 11 runs, longest 19 bars) -
any regime-specific conclusion about it is fragile by sample size alone. Two parallel "regime" enums
exist (RegimeType, dead; MarketState, live) - documentation/onboarding hazard, zero runtime impact.

P3:
MarketState.Transitional has a fully-wired downstream methodology (TransitionalMethodology, explicitly
"unsupported") but is structurally unreachable as a Decision winner - dead output value, confirmed 0/10931
occurrences. Engine/Regime/RegimeType.cs + Engine/Regime/Core/EvidenceFusionEngine.cs (Sprint 2.4 stub,
always returns RegimeType.Unknown) is 100% dead code, never called from Engine.Regime.Core namespace
anywhere in the repo - cleanup candidate for a dedicated lot, never in an audit lot.

NORMALIZATION:
Persistence is saturated (74-98% of raw movements frozen by FusionStateManager's EMA+hysteresis) in
EVERY regime, extending Lot 14.17's global 95.0% finding - regime-dependent in magnitude only (Trending
least frozen at 74.2%, RandomWalk most frozen at 98.1%), never absent. Stationarity/MeanReversion/
RandomWalk dimensions freeze less (40-86%, varies by regime) - STABLE tracks RAW far more often there.
StructuralStability has no comparable RAW value (not evidence-derived, see CRITICAL P0 item 3).

FUSION:
All 5 Decision rules consume only the 5 FusionDimension values, never raw Evidence directly. Only 5 of 9
Evidence types feed Fusion at all (Adf+Kpss->Stationarity, Dfa+VarianceRatio->Persistence,
HalfLife->MeanReversion, VarianceRatio->RandomWalk); Hurst(raw)/Cusum/BaiPerron/Volatility are computed
every bar and never consumed by any IFusionRule - their only consumer anywhere in the repo is a dashboard
validity counter (IQIAIndicator.cs:1167-1173). BaiPerron(BreakCount) and Dfa(Hurst) both show a clean,
economically sensible monotonic gradient across regimes even though only Dfa is actually used - a
discarded, not absent, discriminative signal.

PERSISTENCE:
Confirms and extends Lot 14.17 by regime: freeze rate never drops below 74% in any regime (MeanReverting
97.2%, StructuralBreak 93.7%, RandomWalk 98.1%, Trending 74.2%, StableRange 87.9%). Regime-dependent in
magnitude, never in kind - the causal mechanism (HysteresisThreshold=0.03 inside FusionStateManager,
unchanged) is the same everywhere.

DECISION:
MeanWinnerScore 0.583 (StructuralBreak) to 0.653 (StableRange). MeanScoreDifference (win margin) smallest
for StableRange (0.0249, its own wins are the LEAST decisive of any regime) and largest for Trending
(0.0935). NWithRunnerUp=N for every regime (at least 2 candidates always) - consistent with Lot 14.14's
CandidateCount=5-always finding, reconfirmed this lot.

AMBIGUITY:
AmbiguityGateThreshold=0.95 unchanged. Per-regime %>=0.95: MeanReverting 64.42%, StructuralBreak 49.78%,
RandomWalk 67.08%, Trending 38.00%, StableRange 85.34% (StableRange would be the MOST ambiguous regime of
all if it ever reached this test - it structurally never does). MeanReverting's 64.42% figure matches
EXACTLY the downstream NO_ACTION rate (4192/6507) - direct, exact causal confirmation that the ambiguity
gate is THE mechanism gating MeanReverting's own signal quantity.

SIGNAL:
100% "SignalProduced" in every regime (wiring artifact - SignalEngine.Process runs unconditionally) but
EntryCandidate is 100% (6507/6507) for MeanReverting and EXACTLY 0% for all 4 other regimes combined
(0/3375+483+450+116=0/4424). Root cause: EntryTriggerBuilder.DetermineDirection hard-codes
"if (decision.Winner != MarketState.MeanReverting) return NO_ACTION" with a self-documenting comment
citing MethodologyRegistry/ScientificModelRegistry - independently confirmed those registries return zero
functional scientific models for every non-MeanReverting methodology (TimeSeriesMomentumModel/BOCPDModel
are literal Success=false stubs; no RandomWalkNullModel class exists at all).

ENTRY:
Direct cascade of SIGNAL. MeanReverting: BUY=1166, SELL=1149, NO_ACTION=4192 (64.4%, ambiguity-gated),
WATCH=0 (never observed on this dataset). All other regimes: 100% NO_ACTION, 0% anything else.

EXECUTION:
Fill=Open[SignalBarIndex+1] confirmed as current production behaviour (Lot 14.10 P0-1 fix, present in the
working tree though not yet committed) - matches this brief's own Section 16 reference model exactly.
MeanReverting: PositionOpened=2315 (35.58% of Ready bars), PositionClosed=2315 (100% of opened, all via
fixed HorizonBars time exit). All other regimes: 0 positions. IMPORTANT: ExecutionSimulator opens
positions from any directional ExecutionCandidate regardless of TradePlan.Status - since TradePlan is
NEVER PLAN_READY (see STOP LOSS below), every one of these 2315 positions came from a SIGNAL_ONLY
TradePlan. Execution and TradePlan are currently two decoupled concepts.

MEASUREMENT:
MeanReverting only (N=2315, RELIABLE): MeanReturn=2.40e-05, MedianReturn=3.20e-05, StdDevReturn=0.001286,
HitRate(threshold 0.001)=29.72%, MeanMAE=0.000862, MeanMFE=0.000907. All 4 other regimes: N=0,
LOW_N_UNRELIABLE (total absence, not merely low sample size - a direct cascade of SIGNAL/ENTRY, not a
data-coverage limitation).

RISK:
MeanReverting: Allowed=2315 (35.58%), PositionNotExecutable=4192 (64.42%). All other regimes: 100%
PositionNotExecutable. Only 2 of 9 PositionRiskReason values ever observed anywhere in this dataset - the
7 richer rejection paths (budget/drawdown/exposure/quantity limits) are structurally unverified here
(CASE E), not proven broken, not proven correct.

STOP LOSS:
Confirmed globally absent, independent of regime. TradePlanContext.RiskParameters is always null from
both IQIAIndicator.cs:602 and BacktestEngine.cs:345 - only unit tests ever supply a real
TradeRiskParameters. IStopLossStrategy exists as a documented extension point but its only implementation
(ProvidedStopLossStrategy) is a pass-through with no caller. Empirically: TradePlanStatus.PLAN_READY = 0
across all 2315 TradePlans produced (100% SIGNAL_ONLY) - unreachable even for the one regime with
directional signals.

COST:
Disabled by default in production (unchanged, Lot 14.9). This lot activated a TEST FIXTURE (not a real
broker schedule, copied verbatim from BacktestFullResultWithRiskCostActivationTests.cs) purely to confirm
structural wiring: MeanReverting N=2315, MeanTotalCost=10.50. All other regimes: N=0. No real MES cost
schedule exists anywhere in the repo.

DATA LIMITATION:
Yahoo M5, ~59 days, rolling window (not bit-reproducible run to run - magnitudes stable, exact figures
are not, consistent with known project behaviour). StableRange (116 bars, 11 runs, longest run 19 bars)
and RandomWalk/Trending (450-483 bars) are statistically fragile for any fine-grained sub-analysis, though
sufficient for the descriptive coverage numbers in this report. None of the P0 findings (Signal/Entry
restriction, missing Stop Loss, StructuralBreak evidence gap) are data-limited - they are architectural
and would reproduce identically on a 10-year dataset.

MISSING REGIMES:
None absent from the data (all 5 live regimes represented, see REGIME COVERAGE). Transitional/Unknown are
architecturally unreachable, not data-absent - confirmed by code reading, not merely by their 0 count.

DATA SOURCE NEEDED:
None for the P0/P1 findings (architectural, not data-limited). A longer or independent dataset would only
improve StableRange/RandomWalk/Trending statistical reliability (secondary, P2).

CALIBRATION READINESS:
NOT READY

NEXT RECOMMENDED LOT:
A dedicated correction lot addressing EITHER (a) Signal/Entry multi-regime capability (implement or
formally abandon TimeSeriesMomentumModel/BOCPDModel/a real Random Walk model), OR (b) a production Stop
Loss methodology (the QDE-012_StopLoss_Calibration_Protocol.md research already exists) - whichever the
user prioritizes; both are P0 and independent of each other. A lighter-weight option: a dedicated
Evidence-wiring lot deciding whether Cusum/BaiPerron should feed Fusion (their per-regime gradient in this
report suggests they should).

WHY:
This lot closes the question the whole Lot 15.0 brief was commissioned to answer: the architecture is NOT
uniformly built across the 5 regimes it classifies. MeanReverting is the only regime with any trading
capability; StructuralBreak (30.9% of the dataset) lacks its own defining evidence type; Stop Loss is
globally absent. Any calibration lot attempted before one of these is addressed would calibrate a system
that structurally cannot act on 40% of the market regimes it detects, and cannot manage risk on the 60% it
can.

DO NOT DO:
Do NOT modify RegimeEngine, EvidenceFusionEngine (either), FusionStateManager, DecisionEngine,
DecisionArbitrator, SignalEngine, EntryEngine, EntryTriggerEngine, EntryTriggerBuilder, TradePlanBuilder,
RiskEngine, RiskPolicy, or ExecutionSimulator - this lot is audit-only, none were touched. Do NOT treat
this report's numbers as calibration inputs (no weight/threshold was tuned). Do NOT delete the dead
RegimeType/Engine.Regime.Core.EvidenceFusionEngine stub or the unreachable MarketState.Transitional value
without a dedicated cleanup lot - out of scope here. Do NOT assume AmbiguityGateThreshold=0.95's Lot
14.13 calibration applies system-wide - it only ever affects MeanReverting.
```

**STOP.**
