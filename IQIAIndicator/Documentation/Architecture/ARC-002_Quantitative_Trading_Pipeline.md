# ARC-002 — Quantitative Trading Pipeline

**Projet :** IQIA (Intelligent Quantitative Intelligence Analyzer)

**Statut :** Architecture Officielle

**Version :** 1.0

**Auteur :** BobLabs Research

---

# 1. Objectif

Ce document définit l'architecture complète du pipeline de décision quantitative d'IQIA.

Il décrit les responsabilités de chaque moteur, les échanges de données entre eux et la philosophie générale du système.

Ce document constitue la référence officielle de l'architecture d'IQIA.

---

# 2. Philosophie

IQIA n'est pas un indicateur technique.

IQIA est un système de décision quantitative.

Le système ne cherche jamais à produire directement un signal d'achat ou de vente à partir d'un indicateur.

Au contraire, IQIA applique une succession de modèles scientifiques indépendants afin de transformer des observations de marché en une décision probabiliste.

Le pipeline est construit autour de cinq principes fondamentaux :

- séparation stricte des responsabilités ;
- explicabilité de chaque décision ;
- indépendance des modèles scientifiques ;
- fusion probabiliste des évidences ;
- modularité de l'architecture.

---

# 3. Pipeline Global

```

Market Data

↓

Evidence Models

↓

Fusion Engine

↓

Decision Engine

↓

Methodology Engine

↓

Signal Engine

↓

Entry Engine

↓

Risk Engine

↓

Execution

```

Chaque moteur possède une responsabilité unique.

Aucun moteur ne remplit les responsabilités d'un autre.

---

# 4. Evidence Models

## Mission

Produire des mesures scientifiques indépendantes décrivant le comportement du marché.

## Entrées

Market Data

## Sorties

Evidence Models

Exemples :

- Stationarity
- Persistence
- Mean Reversion
- Structural Stability
- Random Walk

## Interdictions

Ne jamais produire :

- BUY
- SELL
- Strategy
- Risk

---

# 5. Fusion Engine

## Mission

Fusionner les différents modèles scientifiques afin d'obtenir une représentation cohérente du comportement du marché.

## Entrées

Evidence Models

## Sorties

FusionResult

Le Fusion Engine ne prend jamais de décision de trading.

---

# 6. Decision Engine

## Mission

Identifier le comportement dominant du marché.

Le moteur répond exclusivement à la question :

> Quel comportement de marché est actuellement le plus probable ?

## Sorties possibles

- Stable Range
- Mean Reverting
- Trending
- Structural Break
- Random Walk

Le Decision Engine ne produit jamais :

- BUY
- SELL
- Entry
- Risk

---

# 7. Methodology Engine

## Mission

Sélectionner la méthodologie quantitative adaptée au comportement détecté.

Le moteur ne choisit jamais une stratégie commerciale.

Il sélectionne un ensemble cohérent de modèles mathématiques.

Exemples :

Mean Reverting

↓

Ornstein-Uhlenbeck Stack

Trending

↓

Time Series Momentum Stack

Le Methodology Engine ne génère jamais un signal.

---

# 8. Signal Engine

## Mission

Déterminer si une opportunité statistique existe.

Le Signal Engine exécute uniquement les modèles correspondant à la méthodologie sélectionnée.

Il fusionne plusieurs évidences quantitatives.

Exemples :

- Ornstein-Uhlenbeck
- Kalman Filter
- Dynamic Z-Score
- Time Series Momentum
- Volatility Models
- BOCPD
- SPRT

Le résultat est un :

Signal Candidate

Le Signal Engine ne décide jamais de l'exécution.

---

# 9. Entry Engine

## Mission

Déterminer le meilleur instant pour entrer sur le marché.

Le Signal Engine indique :

"Une opportunité existe."

L'Entry Engine répond :

"Est-il optimal d'entrer maintenant ?"

Sorties possibles :

- BUY
- SELL
- WAIT

L'Entry Engine pourra évoluer dans les futures versions afin d'intégrer :

- Order Flow
- Auction Market Theory
- Market Microstructure
- Liquidité
- Price Action objectivable

Cette évolution ne modifiera jamais le Signal Engine.

---

# 10. Risk Engine

## Mission

Optimiser l'exposition au risque.

Le Risk Engine répond notamment aux questions suivantes :

- taille de position ;
- niveau du Stop Loss ;
- niveau du Take Profit ;
- Risk / Reward ;
- validation finale du trade.

Le Risk Engine ne décide jamais du sens du trade.

---

# 11. Responsabilités

| Engine | Responsabilité |
|----------|---------------|
| Evidence Models | Observer le marché |
| Fusion Engine | Fusionner les observations |
| Decision Engine | Comprendre le comportement |
| Methodology Engine | Choisir les modèles quantitatifs |
| Signal Engine | Déterminer si une opportunité existe |
| Entry Engine | Choisir le meilleur timing |
| Risk Engine | Optimiser le risque |
| Execution | Exécuter l'ordre |

---

# 12. Modèles Quantitatifs

## Mean Reversion Stack

- Ornstein-Uhlenbeck
- Dynamic Z-Score
- Kalman Filter
- Volatility Models
- SPRT

---

## Trend Following Stack

- Time Series Momentum
- Volatility Models
- SPRT

---

## Structural Break Stack

- BOCPD
- Volatility Models
- SPRT

---

## Random Walk Stack

Aucune méthodologie de trading.

Le système privilégie :

WAIT

---

# 13. Pipeline Détaillé

```

Market Data

↓

Evidence Models

↓

Fusion Engine

↓

Decision Engine

↓

Methodology Engine

↓

Quantitative Signal Engine

├── Ornstein-Uhlenbeck

├── Kalman Filter

├── Dynamic Z-Score

├── Time Series Momentum

├── Volatility Models

├── BOCPD

└── Sequential Probability Ratio Test

↓

Signal Candidate

↓

Entry Engine

├── Execution Timing

├── Entry Confirmation

└── BUY / SELL / WAIT

↓

Risk Engine

├── Position Sizing

├── Stop Loss

├── Take Profit

├── Risk / Reward

└── Trade Validation

↓

Execution

```

---

# 14. Architecture Modulaire

Chaque moteur peut évoluer indépendamment.

Exemples :

- amélioration du Signal Engine ;
- ajout de l'Order Flow ;
- ajout du Market Profile ;
- ajout du Machine Learning.

Ces évolutions ne nécessitent pas de modifier le Decision Engine.

---

# 15. Évolutions Futures

Les modules suivants pourront être ajoutés sans modifier l'architecture principale :

## Validation

- Hidden Markov Models

## Probabilités

- Bayesian Decision Theory

## Multi-Assets

- Cointegration

## Information

- Information Theory

## Confirmation

- Order Flow

## Microstructure

- Auction Market Theory

- Market Microstructure

- Liquidity Models

---

# 16. Principes Fondamentaux

IQIA applique les règles suivantes :

- un moteur possède une responsabilité unique ;
- aucun moteur ne produit une information appartenant à un autre moteur ;
- tous les modèles scientifiques sont indépendants ;
- les décisions sont explicables ;
- les modèles statistiques sont privilégiés aux indicateurs techniques ;
- la modularité prime sur la complexité.

---

# 17. Conclusion

IQIA n'est pas construit autour d'indicateurs techniques.

IQIA est un pipeline de décision quantitative.

Chaque moteur répond à une question spécifique :

Le marché est-il compréhensible ?

↓

Quel comportement domine ?

↓

Quelle méthodologie est adaptée ?

↓

Existe-t-il une opportunité statistique ?

↓

Le moment est-il optimal ?

↓

Quel risque faut-il accepter ?

↓

Exécuter ou attendre.

Cette séparation garantit la robustesse, l'explicabilité et l'évolutivité de l'ensemble de l'architecture.