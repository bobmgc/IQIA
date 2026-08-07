# QDE-012 – Strategy Engine Architecture

Version : 1.0  
Statut : Architecture Validée  
Auteur : IQIA Research

---

# 1. Objectif

Le Strategy Engine constitue la première couche métier d'IQIA.

Les couches précédentes répondent à la question :

> Quel est le comportement actuel du marché ?

Le Strategy Engine répond à une question différente :

> Quelle famille de stratégie est la plus adaptée à ce comportement ?

Le Strategy Engine ne produit jamais un signal d'achat ou de vente.

Il sélectionne uniquement une famille de stratégie compatible avec le comportement détecté.

---

# 2. Position dans l'architecture

```
Evidence Models
        │
        ▼
Fusion Engine
        │
        ▼
Fusion Snapshot
        │
        ▼
Decision Engine
        │
        ▼
Decision Result
        │
        ▼
Strategy Engine
        │
        ▼
Strategy Result
        │
        ▼
Signal Engine
```

Le Strategy Engine ne lit jamais directement :

- les prix
- les bougies
- les indicateurs
- les Evidence Models
- les Fusion Rules

Il dépend exclusivement du Decision Engine.

---

# 3. Responsabilité

Le moteur possède une unique responsabilité.

Transformer une décision comportementale en recommandation de famille de stratégie.

Il ne décide jamais :

- quand entrer
- quand sortir
- où placer un Stop Loss
- quelle taille de position utiliser

Ces responsabilités appartiennent respectivement :

- Signal Engine
- Risk Engine

---

# 4. Entrée

Le moteur reçoit uniquement :

```
DecisionResult
```

Ce résultat contient notamment :

- Winner
- WinnerScore
- Ambiguity
- Candidates
- Explanation

Le moteur n'utilise aucune autre source scientifique.

---

# 5. Sortie

Le moteur produit :

```
StrategyResult
```

Ce résultat décrit :

- la famille de stratégie retenue
- les candidats évalués
- le score du gagnant
- l'ambiguïté
- l'explication

---

# 6. Pipeline

```
DecisionResult
        │
        ▼
Strategy Rules
        │
        ▼
StrategyCandidate
        │
        ▼
StrategyArbitrator
        │
        ▼
StrategyResult
```

Le pipeline est volontairement identique au Decision Engine.

Cette homogénéité facilite :

- les tests
- la maintenance
- l'évolution future

---

# 7. Structure du module

```
Engine/

    Strategy/

        Core/

            StrategyEngine.cs

            StrategyResult.cs

            StrategyCandidate.cs

            StrategyResultBuilder.cs

            StrategyArbitrator.cs

            StrategyFamily.cs

        Rules/

        Tests/
```

---

# 8. StrategyFamily

Le moteur ne choisit pas une stratégie précise.

Il sélectionne uniquement une famille.

```
Unknown

RangeTrading

MeanReversion

TrendFollowing

Transition

NoTrading
```

Chaque famille sera développée ultérieurement
par le Signal Engine.

---

# 9. StrategyCandidate

Chaque règle produit un candidat indépendant.

Chaque candidat contient :

```
StrategyFamily

ScientificScore

QualityScore

FinalScore

Explanation
```

Aucun recalcul scientifique n'est autorisé.

Les scores sont produits uniquement par la règle.

---

# 10. StrategyArbitrator

L'arbitrateur reçoit tous les candidats.

Il réalise :

```
Winner

Runner Up

Winner Score

Runner Up Score

Difference

Ambiguity
```

L'arbitrateur ne modifie jamais les scores.

Il sélectionne uniquement le meilleur candidat.

---

# 11. StrategyResultBuilder

Le Builder garantit une construction uniforme du résultat.

Les Strategy Rules ne construisent jamais directement StrategyResult.

Cette séparation respecte le principe :

Single Responsibility Principle (SRP)

---

# 12. Contraintes

Le Strategy Engine :

✔ ne connaît pas les prix

✔ ne connaît pas les chandeliers

✔ ne connaît pas les indicateurs

✔ ne connaît pas le carnet d'ordres

✔ ne connaît pas le volume

✔ ne calcule aucune statistique

✔ ne modifie jamais DecisionResult

✔ ne modifie jamais FusionSnapshot

✔ ne produit jamais un ordre

✔ ne produit jamais un signal

✔ ne calcule jamais le risque

---

# 13. Philosophie

Le moteur répond uniquement à la question :

> Quelle famille de stratégie est compatible avec le comportement actuel ?

Il ne répond jamais :

Quand entrer ?

Quand sortir ?

Combien risquer ?

---

# 14. SOLID

Le moteur respecte :

## Single Responsibility

Une seule responsabilité :

Choisir une famille de stratégie.

---

## Open / Closed

Les nouvelles familles pourront être ajoutées
sans modifier les familles existantes.

---

## Dependency Inversion

Le moteur dépend :

```
DecisionResult
```

Il ne dépend pas directement des modèles scientifiques.

---

# 15. Évolutions futures

Le Strategy Engine sera complété par :

```
RangeStrategyRule

TrendStrategyRule

MeanReversionStrategyRule

TransitionStrategyRule

NoTradingStrategyRule
```

Ces règles seront développées
dans les prochains sprints.

---

# 16. Validation

Le Sprint 7.1 est considéré terminé lorsque :

✔ Architecture créée

✔ DTO créés

✔ Builder créé

✔ Arbitrator créé

✔ Engine créé

✔ Tests d'architecture validés

✔ Aucune logique de stratégie implémentée

✔ Build sans erreur

---

# Conclusion

Le Strategy Engine constitue le lien entre la compréhension scientifique du marché (Decision Engine) et la logique opérationnelle (Signal Engine).

Il ne prend aucune décision d'exécution.

Il identifie uniquement la famille de stratégie la plus cohérente avec le comportement actuellement détecté.