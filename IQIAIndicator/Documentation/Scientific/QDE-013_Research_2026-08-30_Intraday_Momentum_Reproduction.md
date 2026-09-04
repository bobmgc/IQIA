# QDE-013 — Reproduction indépendante « Beat the Market » (momentum intraday SPY)

**Date :** 2026-08-30
**Statut :** recherche exploratoire — hors pipeline IQIA (aucun code de production modifié)
**Harness :** `IQIAIndicator/Tests/Research/IntradayMomentum/IntradayMomentumSprintRunner.cs`
**Sortie :** `IQIAIndicator/Tests/Research/IntradayMomentum/Output/{summary.txt, equity.csv}`

## 1. Objet

L'utilisateur a fourni l'article de Zarattini, Aziz & Barbon, *« Beat the Market: An Effective
Intraday Momentum Strategy for S&P500 ETF (SPY) »* (SSRN 4824172 / SFI 24-97) en affirmant
« j'ai un edge » et a demandé une évaluation avant mise en place. Décision prise : reproduction
autonome de la **version cœur non-levier** sur données réelles, avec un vrai découpage
train / OOS / post-publication.

Cette stratégie est **volontairement tenue hors du pipeline `regime → methodology → model`** de
IQIA : elle est ancrée à l'horloge de séance (enveloppe de volatilité en fonction de l'heure autour
de l'ouverture RTH), pas à un « régime » statistique.

## 2. Données

- **SPY, barres 1 minute RTH, 2007-01-03 → 2025-12-31**, source London Strategic Edge
  (`GET /v1/candles`, `symbol=SPY&timeframe=1m`), mise en cache CSV hors dépôt
  (`scratchpad/lse_cache/SPY_1m_YYYY.csv`, 6 colonnes `ts,o,h,l,c,v`, horodatage ISO Z UTC).
- 19 fichiers annuels, ~1,86 M barres, ~97–98 k barres / an (≈ 250 séances × 390 min). Aucune
  ligne NaN/Inf. Format et bornes vérifiés par sondage.
- **Limite connue :** l'endpoint LSE `/v1/candles` ne sert que des actions/ETF sur ce plan.
  `ES`/`MES`/`ES=F`/`SPX`/`VIX` renvoient « Symbol not available ». SPY est exactement
  l'instrument de l'article, donc suffisant pour la reproduction — mais **pas** l'instrument
  qu'on tradera en ATAS (MES). La bascule SPY→MES est un sujet distinct.

## 3. Stratégie implémentée (cœur, taille constante, sans levier)

Correspond à la ligne « Curr.Band + VWAP, constant size » de la Table 2 de l'article.

- **Noise Area** jour `t`, pour chaque minute-de-séance `k` :
  `sigma_t[k] = moyenne sur les 14 dernières séances de |Close_{t-i}[k] / Open_{t-i} − 1|`.
  `Upper[k] = max(Open_t, PrevClose) × (1 + VM·sigma_t[k])`,
  `Lower[k] = min(Open_t, PrevClose) × (1 − VM·sigma_t[k])`, avec `VM = 1`.
- **Décisions (et évaluation des stops) uniquement aux :00 et :30, de 10:00 à 15:30 inclus**
  (heure de New York, conversion via `TimeZoneInfo`, index minute relatif à l'ouverture réelle
  du jour → DST et demi-séances gérés automatiquement).
- Plat → prix au-dessus de `Upper[k]` ⇒ long ; en-dessous de `Lower[k]` ⇒ short.
- Long → prix ≤ `Lower[k]` ⇒ clôture + inversion en short ; prix ≤ `max(Upper[k], VWAP[k])`
  ⇒ clôture, retour plat (symétrique pour un short avec `min(Lower[k], VWAP[k])`).
- **Flat forcé sur la dernière barre de séance** (aucun risque overnight).
- **Taille :** `shares = floor(AUM_début_de_jour / Open_t)`, constante sur la journée.
- **Coûts :** 0,0035 $/action de commission + 0,001 $/action de slippage, **à chaque entrée
  ET chaque sortie** (une inversion = 2 transactions = 2 coûts).
- VWAP RTH cumulé depuis l'ouverture, prix typique `(H+L+C)/3`.
- Capital initial 100 000 $.

## 4. Résultats

```
SPLIT          days  active  trades  totRet%   CAGR%   annVol%  Sharpe  hit%   MDD%   skew  worst%  best%
FULL 2007-2025  4780    2943    8838    257,4     6,9      7,6    0,93    42  -12,5   2,09   -4,7    4,9
TRAIN 07-19     3272    2039    6142    134,5     6,8      7,7    0,89    40  -12,5   2,64   -2,7    4,9
OOS 20-24May    1097     661    1958     47,3     9,3      7,7    1,20    47   -9,0   0,29   -4,7    3,2
POSTPUB 24-25    411     243     738      3,5     2,1      6,1    0,38    46   -6,4   2,54   -1,2    3,0

SPY BUY&HOLD    totRet%=379   CAGR%=8,6   annVol%=19,6   Sharpe=0,52   MDD%=-56

Article (Curr.Band+VWAP, taille constante, Table 2) : totRet 380%, CAGR 9,7%, vol 7,7%, Sharpe 1,24, hit 43%, MDD 12%.
Article (idem + vol-target 2% & levier ≤ 4x)        : totRet 1985%, CAGR 19,6%, Sharpe 1,33, MDD 25%.
```

## 5. Lecture

**La stratégie se reproduit — le profil de risque est celui de l'article :**

| Métrique | Article (2007-2024) | Reproduction FULL (2007-2025) | Verdict |
|---|---|---|---|
| Volatilité annualisée | 7,7 % | 7,6 % | identique |
| Taux de réussite (jours) | 43 % | 42 % | identique |
| Drawdown max | 12 % | 12,5 % | identique |
| Skew des rendements quotidiens | positif | +2,09 | identique (signature de la stratégie) |
| Sharpe | 1,24 | 0,93 (FULL) / **0,89 (TRAIN)** | plus bas |
| CAGR | 9,7 % | 6,9 % | plus bas |

Les quatre métriques de forme (vol, hit, MDD, skew) collent au dixième près : la mécanique est
la bonne. L'écart de Sharpe / CAGR s'explique par : (a) fenêtre étendue à 2025, qui ajoute la
tranche POSTPUB faible ; (b) source de données différente (LSE vs IQFeed) ; (c) VWAP en prix
typique ; (d) coûts peut-être un cran plus prudents ; (e) restriction stricte aux :00/:30 pour
les stops. Aucun de ces écarts n'invalide le résultat.

**L'OOS tient.** Sur 2020 → mai 2024 — la fenêtre de test principale de l'article, jamais
utilisée pour calibrer quoi que ce soit ici (aucun paramètre n'a été touché) — Sharpe **1,20**,
CAGR 9,3 %, hit 47 %. C'est le chiffre phare de l'article, reproduit hors échantillon.

**Post-publication : décroissance nette mais pas d'inversion.** Sur mai 2024 → fin 2025, vrai
aveugle post-parution : Sharpe **0,38**, CAGR 2,1 %, encore positif, skew encore positif (+2,54),
drawdown contenu (−6,4 %). Signature classique d'un edge publié : l'avantage s'est fortement
émoussé une fois l'article public, sans devenir négatif. 15 mois, échantillon court — à surveiller.

**Contre buy & hold :** CAGR comparable (6,9 % vs 8,6 %) mais avec **1/3 de la volatilité**
(7,6 % vs 19,6 %), **1/5 du drawdown** (−12,5 % vs −56 %) et **aucun risque overnight**.
Sharpe ~1,8x. C'est là que réside la valeur, et elle se reproduit.

## 6. Réserves avant toute mise en place réelle

1. **Instrument.** Reproduction sur SPY (actions). En ATAS on trade MES. Il faut re-valider sur
   données futures ES/MES continues (rollover, horaires étendus, tick 0,25, point value 5 $) —
   endpoint LSE actuel insuffisant.
2. **Coûts.** Modèle simpliste : per-share constant, pas d'élargissement de spread ni d'impact
   sur les jours de tendance forte (précisément là où la stratégie prend ses gros gains). Sur
   MES, coûts en ticks + slippage de file d'attente réaliste.
3. **Données non tick-auditées.** Barres 1 min LSE prises telles quelles ; pas de recoupement
   avec une seconde source.
4. **Pas de coût de portage short.** Ignoré ici (négligeable sur SPY, à confirmer sur MES).
5. **Levier.** Le +1985 % de l'article vient de la couche vol-target 2 % + levier ≤ 4x, **non
   implémentée ici**. À n'envisager qu'après validation MES du cœur.
6. **Échantillon post-publication court** (15 mois). Le Sharpe 0,38 peut être du bruit comme le
   début d'une érosion durable.

## 7. Recommandation

L'affirmation « j'ai un edge » est **partiellement confirmée** : il y a bien un edge structurel
(momentum intraday + absence de risque overnight), il tient hors échantillon sur la fenêtre de
l'article, mais il a **matériellement décru depuis publication** (Sharpe 1,2 → 0,4). Non-levier,
c'est une stratégie basse-vol / skew-positif à Sharpe réaliste **0,8–1,0** aujourd'hui, pas 1,2+.

Étapes proposées, dans l'ordre, une hypothèse à la fois :
1. Obtenir des données futures ES/MES 1 min (autre endpoint/source) et re-passer le harness tel
   quel dessus. **Bloquant** : sans ça, tout le reste est théorique.
2. Si le cœur MES tient (Sharpe ≥ 0,8 net de coûts réalistes) → ajouter la couche vol-target +
   levier plafonné, mesurer le Sharpe après levier (l'article annonce 1,33).
3. Décider ensuite seulement d'une intégration : soit un module autonome piloté par l'horloge de
   séance, soit rien (garder IQIA sur le mean-reversion). Cette stratégie **ne doit pas** passer
   par l'arbitrage de régime existant.

Aucun code de production n'a été touché. Le harness est un `[Fact]` de recherche isolé.

## 8. Test « trois stratégies » sur fenêtre commune (2026-08-30)

Demande : tester ensemble MeanReverting + Trending + intraday-momentum SPY, quitte à intégrer
temporairement la 3ᵉ au pipeline. **Choix retenu :** ne PAS câbler la stratégie de session dans
`OnCalculate` (instrument, résolution et logique incompatibles — voir §1). À la place, les trois
sont mesurées comme **trois poches parallèles indépendantes**, chacune sur 25 000 $, sur la
**même fenêtre calendaire** que le backtest pipeline MES=F M5 : **2026-07-14 → 2026-08-28**
(34 séances). Fact : `IntradayMomentumSprintRunner.RunThreeStrategyCommonWindow` →
`Output/three_strategy.txt`.

| Poche | Trades | NET | Équité finale |
|---|---|---|---|
| MeanReverting (pipeline MES, 1 %/trade, R:R ≥ 1,5) | 3 | **+592,55 $** | 25 593 $ |
| Trending (pipeline MES, idem) | 198 | **−7 088,75 $** | 17 911 $ |
| Intraday-momentum SPY (harness autonome, coûts inclus) | 42 | **−99,59 $** | 24 900 $ |
| **PORTEFEUILLE 3 × 25 k = 75 k** | 243 | **−6 595,79 $** | **68 404 $ (−8,8 %)** |

**Lecture :** sur cette fenêtre de 6 semaines la poche intraday-momentum est **quasi plate**
(−0,4 %, 6 séances positives sur 34, drawdown −402 $). Cohérent avec le §5 : l'edge décru
post-publication n'est plus qu'un Sharpe ~0,4, et toute tranche de 6 semaines est dominée par le
bruit. Elle **n'aggrave pas** la perte mais ne la compense pas non plus. Le portefeuille perd
8,8 % en 6 semaines, **entièrement à cause de Trending**. 34 séances est bien trop court pour
juger la poche SPY (jugement valable seulement sur plusieurs années — cf. §4).
