# Architecture Evidence-Based — IQIA Regime Engine

## Principe fondamental

Les modèles scientifiques sont des **producteurs d'observations**.
Ils ne prennent aucune décision.

Toute décision de régime appartient exclusivement au `EvidenceFusionEngine`.

---

## Flux de données

```
MarketContext
      │
      ▼
RegimeEngine.Collect()          ← orchestrateur pur, sans logique métier
      │
      ├── AdfEvidence.Compute()         → AdfResult
      ├── KpssEvidence.Compute()        → KpssResult
      ├── HurstEvidence.Compute()       → HurstResult
      ├── HalfLifeEvidence.Compute()    → HalfLifeResult
      ├── VarianceRatioEvidence.Compute()→ VarianceRatioResult
      ├── CusumEvidence.Compute()       → CusumResult
      └── VolatilityEvidence.Compute()  → VolatilityResult
      │
      ▼
EvidenceSet                     ← conteneur immuable de toutes les observations
      │
      ▼
EvidenceFusionEngine.Fuse()     ← SEUL lieu de décision de régime
      │
      ▼
RegimeResult
```

---

## Règles architecturales

### Ce que les modèles peuvent faire

- Lire `MarketContext` (données de marché brutes)
- Calculer des statistiques scientifiques (t-stat, p-value, VR, β, …)
- Retourner un résultat typé (AdfResult, KpssResult, …)

### Ce qui est interdit dans les modèles

Les notions suivantes sont interdites dans tout code d'evidence :

- `Trend`, `TrendBull`, `TrendBear`
- `Range`, `MeanReversion`
- `Compression`, `Expansion`
- `Transition`, `Unknown` (sauf pour signaler un warmup)
- Toute référence à `RegimeType`

### Ce que fait l'EvidenceFusionEngine

- Reçoit un `EvidenceSet` complet
- Interprète les observations statistiques
- Produit un `RegimeResult` avec un `RegimeType` unique

---

## Résultats typés par modèle

| Modèle | Résultat | Champs clés |
|--------|----------|-------------|
| ADF | `AdfResult` | Statistic, PValue, CriticalValues, IsStationary, LagUsed |
| KPSS | `KpssResult` | Statistic, PValue, CriticalValues, IsStationary, Bandwidth |
| Hurst | `HurstResult` | VarianceRatio, HurstProxy |
| HalfLife | `HalfLifeResult` | Beta, HalfLifeBars, IsMeanReverting |
| VarianceRatio | `VarianceRatioResult` | VR5 |
| CUSUM | `CusumResult` | SPlus, SMinus, HasBreak |
| Volatility | `VolatilityResult` | AcfAbsReturns, IsClustering |
| BaiPerron | `BaiPerronResult` | (Sprint 2.4 — non implémenté) |

---

## Dépendances

```
Core/MarketContext
    ↓
Evidence/* (ADF, KPSS, Hurst, HalfLife, VR, CUSUM, Volatility)
    ↓
Core/EvidenceSet
    ↓
Core/EvidenceFusionEngine
    ↓
Engine/Regime/RegimeResult
```

Aucune dépendance circulaire.
Aucun modèle ne connaît un autre modèle.

---

## Plan Sprint 2.4

- Implémenter `EvidenceFusionEngine.Fuse()` avec la logique de vote pondéré
- Ajouter le test DFA (Detrended Fluctuation Analysis) → `HurstResult` amélioré
- Ajouter Bai-Perron → `BaiPerronResult`
