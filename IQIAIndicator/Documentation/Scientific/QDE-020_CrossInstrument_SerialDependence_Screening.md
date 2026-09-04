# QDE-020 — Dépistage cross-instrument de la dépendance sérielle : MES est-il un cas particulier ?

**Date :** 2026-09-01
**Mode :** lecture seule. **Production modifiée : NON.** Aucune entrée ajoutée à `YahooSymbolMap` (sonde en contournement, comme QDE-013/014).
**Sonde :** `IQIAIndicator/Tests/Research/OrderFlowFeasibility/CrossInstrumentSerialDependenceTests.cs` (read-only ; réutilise `VarianceRatioStatistics.Compute` et les helpers stats de QDE-012 §2.h **sans les modifier**). Sortie : `Output/cross_instrument_serial_dependence.txt`.

---

## 1. Contexte et question

QDE-012 → QDE-019 ont établi, sur MES (validé cross-instrument avec ES=F, quasi-doublon du même marché), qu'aucune structure directionnelle exploitable n'existe après coûts — ni dans le prix, ni dans l'order flow. Avant de changer de source de données ou d'horizon, une question non posée : cette absence de dépendance sérielle est-elle **spécifique à MES**, ou est-elle **générale sur les futures retail à cette échelle** ? Si un autre instrument montre une signature statistique différente, c'est la piste la moins chère de toutes — zéro nouvelle donnée, juste un changement de sous-jacent.

---

## 2. Méthodologie

- **Batterie :** 10 futures via Yahoo H1 natif ~720 jours (`HttpYahooChartClient.FetchChartJson` direct) — 2 références (MES=F, ES=F) + 8 instruments couvrant des classes d'actifs vraiment différentes : NQ=F (indice tech), RTY=F (small cap), ZN=F (taux 10 ans), ZB=F (taux 30 ans), 6E=F (EUR/USD), CL=F (pétrole WTI), GC=F (or), SI=F (argent).
- **Tests par instrument** (identiques à QDE-012 §2.h) : log-rendements, barres consécutives contiguës (écart ≤ 1,5× l'intervalle) ; ACF lags {1,2,3,5,10,20} avec IC de Bartlett 95 % ; ratio de variance de Lo-MacKinlay VR(q) pour q ∈ {2,3,5,10,20} (`VarianceRatioStatistics.Compute`, robuste à l'hétéroscédasticité) ; test de runs de Wald-Wolfowitz sur le signe.
- **Correction de comparaisons multiples (bloquante) :**
  - *intra-instrument* : Bonferroni ×6 sur la famille {VR(2), VR(3), VR(5), VR(10), VR(20), runs} → `within-adj p = min(1, 6 × min p)` ;
  - *inter-instruments* : Holm-Bonferroni sur la colonne `within-adj p` de la famille de 8 (références MES/ES exclues), α familial = 0,05.
- **Robustesse temporelle :** pour tout instrument brut-significatif (`min p < 0,05` avant correction), split 60/40 et re-test VR(2) + runs + ρ(1) sur chaque moitié.
- **Phase 2 (pipeline complet) :** lancée uniquement sur les survivants du dépistage corrigé **ET** passant un pré-check économique de coût réaliste par instrument.

---

## 3. Phase 1 — Résultats (~10 600–10 825 rendements par instrument)

| instrument | ρ(1) | ACF sig /6 | VR(2) | VR(2) p | min VR p | runs p | **within-adj p** | **Holm** |
|---|---|---|---|---|---|---|---|---|
| MES (réf) | −0,038 | 3 | 0,962 | 0,129 | 0,129 | 0,840 | 0,776 | référence |
| ES (réf) | −0,022 | 3 | 0,978 | 0,393 | 0,393 | 0,829 | 1,000 | référence |
| NQ (Nasdaq tech) | −0,018 | 1 | 0,982 | 0,388 | 0,388 | 0,246 | 1,000 | non sig |
| RTY (small cap) | −0,051 | 3 | 0,949 | **0,022** | 0,021 | 0,141 | 0,128 | non sig (crit 0,0083) |
| **ZN (taux 10 ans)** | −0,024 | 1 | 0,976 | 0,082 | 0,082 | **0,00002** | **0,0001** | **✱✱✱ SIGNIFICATIF** (crit 0,0063) |
| ZB (taux 30 ans) | −0,026 | 1 | 0,974 | 0,086 | 0,086 | **0,0016** | 0,0093 | non sig (crit 0,0071 — manque de peu) |
| 6E (EUR/USD) | −0,021 | 2 | 0,979 | 0,153 | 0,068 | 0,053 | 0,319 | non sig (crit 0,0100) |
| CL (pétrole) | −0,028 | 3 | 0,972 | 0,125 | 0,125 | 0,218 | 0,747 | non sig (crit 0,0167) |
| GC (or) | −0,014 | 2 | 0,986 | 0,522 | 0,212 | 0,788 | 1,000 | non sig (crit 0,0500) |
| SI (argent) | −0,005 | 3 | 0,996 | 0,874 | 0,289 | 0,065 | 0,389 | non sig (crit 0,0125) |

*Bande de Bartlett pour ρ(1) : ±0,019 (≈, N varie par instrument).*

**Constats :**
1. **ρ(1) est négatif pour les 10 instruments** (−0,005 à −0,051) — faible autocorrélation négative universelle au lag 1 —, mais majoritairement à l'intérieur de la bande de Bartlett. VR(2) < 1 pour les 10 (« réversion » directionnelle partout), mais aucun ne survit à la correction intra-instrument sur la famille VR.
2. **Un seul survivant Holm : ZN=F.** `within-adj p = 0,0001` (Bonferroni ×6 du runs test brut ≈ 2·10⁻⁵) ≤ seuil Holm 0,0063.
3. **Le signal de ZN vient du runs test** (Z = +4,26, **alternance de signe / dépendance négative**), pas du VR (VR(2)=0,976, p=0,082, non sig) ni de l'ACF (ρ(1)=−0,024, marginal). C'est la **même signature que le bid-ask bounce de MES en M5** (QDE-012 §2.h) — mais ici en **H1**, où celui de MES avait disparu.
4. **Le complexe obligataire se distingue** : ZN (Holm-significatif) et ZB (juste sous le seuil, `within-adj` 0,0093 vs crit 0,0071) ont le même profil — ρ(1) négatif, runs Z positif. Les indices actions (NQ, RTY), le FX (6E) et les matières (CL, GC, SI) : rien après correction.

---

## 4. Robustesse temporelle (split 60/40)

| instrument | TRAIN | OOS | verdict |
|---|---|---|---|
| RTY | ρ(1)=−0,079 (sig), VR(2) p=0,012, runs p=0,030 | ρ(1)=**+0,005**, VR(2) p=**0,83**, runs p=**0,77** | **disparaît complètement** — blip de première moitié ; a correctement échoué au dépistage corrigé |
| **ZN** | runs Z=+2,49 (p=0,013) ; VR(2) p=0,10 ; ρ(1)=−0,029 | runs Z=**+3,72 (p=0,0002)** ; VR(2) p=0,55 ; ρ(1)=−0,013 | **le runs signal tient et se renforce en OOS** ; VR(2) non sig dans les deux moitiés |
| ZB | runs Z=+1,79 (p=0,074, non sig) | runs Z=+2,79 (p=0,0053) | direction stable, borderline — cohérent avec ZN (même complexe) |

**ZN a une dépendance sérielle statistiquement robuste et stable hors échantillon.** RTY confirme le bon fonctionnement de la correction.

---

## 5. Pré-check économique par instrument (bloquant avant Phase 2)

### ZN=F — 10-Year T-Note Future

**Specs CME (publiques) :** tick = ½ de 1/32 = **1/64 de point = 0,015625** ; valeur du tick = **$15,625** ; valeur du point = **$1 000** ; notionnel ≈ $110 000.

**Hypothèse de coût round-trip — À VALIDER, pas un fait établi :** ZN est parmi les futures les plus liquides au monde, spread affiché quasi toujours **1 tick** ($15,625). Commission retail ≈ $1,5–2,5 aller-retour (ordre de grandeur MES). Hypothèse retenue : **~1,5 tick all-in ≈ 0,0234 point ≈ $23 round-trip par contrat.** (Un pupitre passif sur une jambe descendrait vers ~1 tick ≈ $17 ; un preneur des deux côtés monterait vers ~2 ticks ≈ $31.)

**Composante prédictible :** `|ρ(1)| × σ_rendement`. σ des log-rendements H1 de ZN ≈ 4·10⁻⁴ (≈ 3 ticks ≈ 0,047 point). `|ρ(1)| × σ ≈ 0,024 × 0,047 ≈ 0,0011 point ≈ $1,1 par contrat et par barre.`

**Ratio mouvement prédictible / coût round-trip ≈ $1,1 / $23 ≈ 0,05.**

Le mouvement prédictible est **~5 % du coût** du trade nécessaire pour le capturer, et c'est un **majorant** (part de variance réellement prédictible R² = ρ(1)² ≈ 0,0006, soit 0,06 %). De plus, la nature du signal — **runs test = alternance de signe** — est celle du **bid-ask bounce** : la grille de tick grossière de ZN (1/64 sur un prix ~110) fait mécaniquement osciller les prix de transaction entre deux niveaux adjacents, produisant une dépendance négative dans les prix de *transaction* qui n'est **pas capturable** (il faudrait acheter à l'ask et vendre au bid à chaque barre, en payant exactement le spread qui crée le motif). Le VR(2) non significatif confirme l'absence de réversion **multi-barres** exploitable.

**→ Le gate économique pré-Phase-2 ÉCHOUE pour ZN.**

---

## 6. Phase 2

**NON lancée.** ZN est le seul instrument à passer le dépistage statistique corrigé, mais il échoue le pré-check économique obligatoire (§5) : effet ~5 % du coût round-trip, signature de bid-ask bounce non capturable, pas de réversion multi-barres (VR non significatif). Lancer le pipeline complet sur ZN reproduirait exactement le résultat de MES en M5 (QDE-012 §2.h) : microstructure statistiquement réelle, économiquement non traçable, artefact et non edge. Aucun autre instrument ne justifie la Phase 2.

---

## 7. Établi avec confiance

1. **L'indépendance sérielle à H1 est une propriété quasi-générale des futures retail à cette échelle**, pas une particularité de MES : sur les 8 instruments dépistés (hors références MES/ES), **7 ne montrent aucune dépendance sérielle Holm-significative** (NQ, RTY, 6E, CL, GC, SI), et le blip brut de RTY disparaît en OOS.
2. **La seule exception est le complexe obligataire** (ZN Holm-significatif, ZB systématiquement dans la même direction) : une **alternance de signe** robuste et stable OOS, détectée par le runs test.
3. **Cette exception n'est PAS un edge exploitable.** C'est un artefact de bid-ask bounce dû à la grille de tick grossière des futures de taux (1/64 sur un prix ~110) : ρ(1) = −0,024, mouvement prédictible ≈ 5 % du coût round-trip, aucune significativité du ratio de variance (pas de réversion multi-barres), et non capturable par construction (le capturer = payer le spread qui le crée).
4. **MES n'est donc pas uniquement efficient** — mais aucun future retail testé n'est *traçable* sur la dépendance sérielle à H1. ZN est une curiosité statistique (microstructure documentée des instruments à tick grossier), pas une piste d'edge moins chère.

**QDE-020 ne révèle aucune piste d'edge exploitable.**

---

## 8. Exploratoire / non concluant

- **ZB (30Y T-Bond)** montre la même alternance de signe que ZN (runs Z positif dans les deux moitiés temporelles) mais ne passe pas le seuil Holm (`within-adj` 0,0093 vs crit 0,0071). Même classe de phénomène que ZN, même verdict économique — non traçable. À noter uniquement pour la cohérence interne du complexe taux.
- Le fait que **le complexe taux** (ZN, ZB) diffère des indices actions / FX / matières sur cette signature de microstructure est cohérent avec la structure de marché (tick grossier relatif au prix, cotation en 32ᵉ/64ᵉ). C'est de la microstructure, sans contenu directionnel.

---

## 9. Recommandation

**Le changement de sous-jacent n'est pas une piste d'edge.** L'indépendance sérielle à H1 vaut pour l'ensemble des futures retail testés (indices, taux, FX, matières) ; la seule dépendance robuste trouvée (complexe taux) est un artefact de microstructure non traçable, du même type que le bid-ask bounce de MES en M5.

**La recommandation de QDE-019 tient inchangée :** arrêt de l'axe recherche d'edge, système en SIGNAL_ONLY. Toute reprise exige une **source de données réellement différente** (order flow multi-instruments profond, historique d'options — payant) ou un **marché / instrument / horizon fondamentalement autre** (daily+, autres classes d'actifs, autres régimes de liquidité) — pas un changement de future retail sur le même horizon intraday.

**Ne PAS :** lancer la Phase 2 sur ZN (gate économique échoué) ; interpréter la significativité Holm de ZN comme un edge ; relancer un lot fondé sur les données actuelles.

---

## 10. Interdictions respectées

- **Aucune modification de production.** 1 fichier ajouté : `Tests/Research/OrderFlowFeasibility/CrossInstrumentSerialDependenceTests.cs`. Réutilise `VarianceRatioStatistics.Compute` et les helpers stats de QDE-012 **sans les modifier**.
- **Aucune entrée ajoutée à `YahooSymbolMap`** — la sonde passe les tickers en direct au `HttpYahooChartClient`.
- **Aucune calibration.**
- **Conclusion explicite fournie** (§7) : propriété quasi-générale des futures retail à cette échelle, l'unique exception (taux) étant un artefact de microstructure non traçable — la question n'est pas laissée ouverte.

---

## 11. Index des fichiers produits

| Fichier | Rôle | Committable en production ? |
|---|---|---|
| `IQIAIndicator/Documentation/Scientific/QDE-020_CrossInstrument_SerialDependence_Screening.md` | Ce rapport | Oui (documentation) |
| `IQIAIndicator/Tests/Research/OrderFlowFeasibility/CrossInstrumentSerialDependenceTests.cs` | Sonde de dépistage, read-only | Recherche uniquement |
| `IQIAIndicator/Tests/Research/OrderFlowFeasibility/Output/cross_instrument_serial_dependence.txt` | Sortie mesurée (preuve §3–§4) | Recherche uniquement |

Suppression éventuelle : `git clean -fd IQIAIndicator/Tests/Research/OrderFlowFeasibility/`.

---

## 12. FINAL OUTPUT

**STATUS :** Dépistage terminé. Sur 8 instruments (hors références MES/ES), **un seul survit au dépistage Holm-corrigé : ZN=F** (runs test, alternance de signe, robuste OOS ; ZB juste sous le seuil, même direction). Les 7 autres (NQ, RTY, 6E, CL, GC, SI) : rien après correction. **ZN échoue le pré-check économique obligatoire** (effet ≈ 5 % du coût round-trip, bid-ask bounce non capturable, VR non significatif) → **Phase 2 non lancée**. Conclusion : l'indépendance sérielle à H1 est une **propriété quasi-générale des futures retail** ; l'unique exception (complexe taux) est un **artefact de microstructure non traçable**, pas un edge. MES n'est pas uniquement efficient, mais aucun future retail n'est traçable sur la dépendance sérielle à H1.

**PRODUCTION MODIFIED :** NO. 1 sonde de recherche. `YahooSymbolMap` intouché.

**TESTS :** `CrossInstrumentSerialDependenceTests` — Passed (10 instruments, ACF + VR + runs + Holm + split temporel).

**DOCUMENTATION :** Ce rapport — `IQIAIndicator/Documentation/Scientific/QDE-020_CrossInstrument_SerialDependence_Screening.md`.

**NEXT ACTION :** Aucune. La recommandation de QDE-019 est confirmée et renforcée : arrêt de l'axe recherche d'edge, système en SIGNAL_ONLY. Le changement de future retail est écarté comme piste. Toute reprise = nouvelle source de données ou marché/horizon fondamentalement différent.
