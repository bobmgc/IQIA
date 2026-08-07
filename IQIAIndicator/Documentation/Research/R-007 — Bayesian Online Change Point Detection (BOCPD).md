R-007 — Bayesian Online Change Point Detection (BOCPD)

Adams & MacKay (2007)

Verdict avant l'étude

Je pense que ce modèle pourrait remplacer, à terme, une partie de la logique actuelle basée sur CUSUM.

Pas immédiatement.

Mais il mérite clairement une étude approfondie.

1. Pourquoi le BOCPD existe ?

Imagine une série de prix.

100

101

102

103

104

105

106

Puis soudain :

101

99

96

94

Question :

À quel moment le marché a-t-il réellement changé ?

Pas :

"Le marché est baissier."

Mais :

"Quand la tendance haussière s'est-elle terminée ?"

C'est exactement le problème du Change Point Detection.

2. Les méthodes classiques

On utilise souvent :

CUSUM
Chow Test
Bai-Perron
Page-Hinkley

Ces méthodes détectent souvent le changement après plusieurs observations.

Le BOCPD tente de faire mieux.

3. L'idée fondamentale

Le modèle ne demande jamais :

"Y a-t-il eu un changement ?"

Il demande :

"Quelle est la probabilité qu'un nouveau régime commence maintenant ?"

C'est une énorme différence.

4. Le concept de Run Length

Le BOCPD suit une variable :

Run Length

C'est :

Le nombre d'observations depuis le dernier changement de régime.

Exemple :

Trend

↓

Run Length

100

Puis :

Nouvelle observation

↓

Run Length

101

Si tout va bien.

Mais si une cassure apparaît :

Run Length

↓

0

Un nouveau régime commence.

5. Fonctionnement

À chaque nouvelle observation :

Le modèle met à jour :

P(Change)

↓

P(No Change)

↓

Run Length Distribution

Autrement dit :

Il garde en mémoire toutes les hypothèses.

6. Pourquoi c'est intéressant ?

Parce que :

Le changement n'est jamais brutal.

Le modèle dit par exemple :

Observation 1

↓

5 %

Puis :

Observation 2

↓

11 %

Puis :

Observation 3

↓

28 %

Puis :

Observation 4

↓

63 %

Puis :

Observation 5

↓

95 %

Le système voit le changement se construire.

7. Comparaison avec CUSUM

CUSUM :

Accumule

↓

Dépasse un seuil

↓

Cassure

BOCPD :

Met à jour

↓

Probabilité

↓

Cassure

Le second est plus probabiliste.

8. Utilisation en finance

BOCPD est utilisé pour :

Détection de changement de volatilité
Régime de marché
Trading adaptatif
Anomalies
Monitoring en temps réel
9. Compatibilité avec IQIA

Très intéressante.

Aujourd'hui :

Evidence

↓

CUSUM

↓

Structural Stability

Demain :

Evidence

↓

CUSUM

+

BOCPD

↓

Transition Probability
10. Ce que je ne ferais PAS

Je ne remplacerais pas immédiatement CUSUM.

Pourquoi ?

CUSUM est :

rapide ;
robuste ;
simple ;
très bien testé.

Le BOCPD est plus complexe.

11. Ce que je ferais

Je créerais un nouveau modèle :

Transition Probability

Produit par :

CUSUM

+

BOCPD

Ainsi :

CUSUM fournit :

Détection

BOCPD fournit :

Confiance probabiliste
12. Les limites

Le BOCPD est :

plus coûteux en calcul ;
plus difficile à calibrer ;
dépend d'un modèle probabiliste bien défini.

Pour du très haute fréquence, cela peut devenir un enjeu.

13. Compatibilité avec le Signal Engine

Je pense qu'il ne doit pas produire un signal.

Il doit agir comme un gardien.

Exemple :

Mean Reversion

↓

Signal prêt

↓

BOCPD

↓

Transition Probability = 82 %

↓

Signal annulé

Pourquoi ?

Parce que le marché est peut-être en train de quitter son régime.

14. Compatibilité avec Bayes

Très forte.

BOCPD est lui-même basé sur des idées bayésiennes.

Il s'intègre naturellement à une architecture probabiliste.

15. Compatibilité avec SPRT

Je vois une architecture intéressante :

Evidence

↓

Bayes

↓

BOCPD

↓

SPRT

↓

Signal

BOCPD vérifie :

Le régime est-il toujours valide ?

SPRT vérifie :

Les preuves sont-elles suffisantes ?

16. Note scientifique
Critère	Note
Fondement mathématique	⭐⭐⭐⭐⭐
Publications académiques	⭐⭐⭐⭐⭐
Objectivité	⭐⭐⭐⭐⭐
Temps réel	⭐⭐⭐⭐☆
Compatibilité IQIA	⭐⭐⭐⭐⭐