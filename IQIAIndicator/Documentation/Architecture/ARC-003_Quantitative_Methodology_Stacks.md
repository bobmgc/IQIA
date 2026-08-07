# ARC-003 — Quantitative Methodology Framework

**Projet :** IQIA (Intelligent Quantitative Intelligence Analyzer)

**Statut :** Architecture Officielle

**Version :** 2.0

**Auteur :** BobLabs Research

---

# 1. Objectif

Ce document définit le rôle du Methodology Engine au sein de l'architecture IQIA.

Contrairement à une plateforme de trading traditionnelle, le Methodology Engine ne sélectionne jamais une stratégie de trading.

Il sélectionne uniquement la méthodologie quantitative la plus adaptée au comportement du marché identifié par le Decision Engine.

La génération du signal reste entièrement sous la responsabilité du Signal Engine.

---

# 2. Philosophie

IQIA applique une séparation stricte des responsabilités.

Le comportement du marché est indépendant de la méthodologie utilisée.

La méthodologie est indépendante de la génération du signal.

Le signal est indépendant du timing d'exécution.

Le timing est indépendant de la gestion du risque.

Chaque moteur possède une responsabilité unique.

---

# 3. Pipeline Général

```
Decision Engine

↓

Methodology Engine

↓

Quantitative Methodology

↓

Signal Engine

↓

Signal Candidate

↓

Entry Engine

↓

Risk Engine

↓

Execution
```

Le Methodology Engine ne produit jamais :

- BUY
- SELL
- WAIT

---

# 4. Responsabilité

Le Methodology Engine répond exclusivement à la question suivante :

> Quelle méthodologie quantitative est la plus adaptée au comportement actuellement observé ?

Il ne calcule aucun indicateur.

Il ne valide aucune opportunité.

Il ne décide jamais de l'exécution.

---

# 5. Quantitative Methodology

Une méthodologie quantitative représente un cadre mathématique permettant d'évaluer une opportunité de marché.

Une méthodologie n'est jamais un signal.

Une méthodologie ne contient aucune logique d'entrée.

Elle décrit uniquement les modèles scientifiques à utiliser.

---

# 6. Structure d'une Methodology

Chaque méthodologie possède les propriétés suivantes.

## Identification

Name

Description

Version

---

## Domaine d'application

Compatible Behaviours

Exemples :

- Mean Reverting
- Trending
- Stable Range
- Structural Break
- Random Walk

---

## Primary Model

Le modèle principal.

Exemples :

Ornstein-Uhlenbeck

Time Series Momentum

---

## Supporting Models

Liste des modèles complémentaires.

Exemples :

Kalman Filter

Dynamic Z-Score

Volatility Models

BOCPD

---

## Validation Model

Modèle chargé de décider si les évidences sont suffisantes.

Exemple :

Sequential Probability Ratio Test (SPRT)

---

## Extensions

Modules facultatifs.

Par exemple :

Bayesian Decision Theory

Order Flow

Market Profile

Liquidity Analysis

Machine Learning

---

# 7. Méthodologies de la Version 1

## Mean Reversion Methodology

Behaviour :

Mean Reverting

Primary Model :

Ornstein-Uhlenbeck

Supporting Models :

- Kalman Filter
- Dynamic Z-Score
- Volatility Models
- BOCPD

Validation :

SPRT

---

## Trend Following Methodology

Behaviour :

Trending

Primary Model :

Time Series Momentum

Supporting Models :

- Volatility Models
- BOCPD

Validation :

SPRT

---

## Structural Break Methodology

Behaviour :

Structural Break

Primary Model :

BOCPD

Supporting Models :

- Volatility Models

Validation :

SPRT

Politique :

WAIT par défaut.

---

## Random Walk Methodology

Behaviour :

Random Walk

Aucune méthodologie active.

Politique :

WAIT.

---

# 8. Rôle du Signal Engine

Le Signal Engine reçoit une méthodologie.

Il est responsable de :

- l'exécution des modèles scientifiques ;
- la collecte des évidences ;
- la fusion des résultats ;
- la génération d'un Signal Candidate.

Le Signal Engine est le seul moteur capable de produire une opportunité statistique.

---

# 9. Exemple

Decision Engine

↓

Mean Reverting

↓

Methodology Engine

↓

Mean Reversion Methodology

↓

Signal Engine

↓

Kalman Filter

↓

Ornstein-Uhlenbeck

↓

Dynamic Z-Score

↓

Volatility Models

↓

BOCPD

↓

SPRT

↓

Signal Candidate

↓

Entry Engine

↓

BUY / SELL / WAIT

---

# 10. Évolutions Futures

La méthodologie pourra être enrichie sans modifier le Methodology Engine.

Exemples :

Version 2

- Bayesian Decision Theory
- Hidden Markov Models
- Information Theory

Version 3

- Order Flow
- Auction Market Theory
- Market Microstructure
- Liquidity Analysis

Le Methodology Engine reste inchangé.

Seule la définition des méthodologies évolue.

---

# 11. Architecture Orientée Objet

Le Methodology Engine retourne un objet immutable.

Exemple :

```
QuantitativeMethodology

Name

Description

PrimaryModel

SupportingModels

ValidationModel

Extensions

CompatibleBehaviours

Version
```

Le Signal Engine consomme uniquement cet objet.

Il ne possède aucune connaissance métier sur les comportements de marché.

---

# 12. Avantages

Cette architecture présente plusieurs avantages.

## Découplage

Le Methodology Engine ne connaît jamais les modèles internes du Signal Engine.

---

## Modularité

Une nouvelle méthodologie peut être ajoutée sans modifier le moteur.

---

## Évolutivité

L'ajout de nouveaux modèles scientifiques ne nécessite pas de modifier les composants existants.

---

## Explicabilité

Chaque décision est justifiée par une méthodologie clairement identifiée.

---

## Testabilité

Chaque méthodologie peut être validée indépendamment.

---

# 13. Principes Fondamentaux

Le Decision Engine identifie le comportement.

Le Methodology Engine sélectionne la méthodologie.

Le Signal Engine recherche une opportunité statistique.

L'Entry Engine détermine le meilleur instant d'exécution.

Le Risk Engine optimise l'exposition.

Aucun moteur ne réalise la responsabilité d'un autre.

---

# 14. Conclusion

Le Methodology Engine constitue le pont entre la compréhension du marché et la génération d'une opportunité.

Il ne choisit jamais une stratégie.

Il ne génère jamais un ordre.

Il sélectionne uniquement le cadre mathématique le plus adapté au comportement détecté.

Cette architecture garantit une séparation stricte des responsabilités et permet au Signal Engine d'évoluer indépendamment du reste du système.

Elle constitue le fondement de l'architecture quantitative d'IQIA.