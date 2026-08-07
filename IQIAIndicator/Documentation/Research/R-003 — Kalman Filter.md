R-003 — Kalman Filter
Verdict avant l'étude

Je pense que le Kalman Filter est un candidat sérieux pour devenir le moteur d'estimation du prix d'équilibre d'IQIA.

Il pourrait remplacer avantageusement les moyennes mobiles ou les moyennes glissantes dans de nombreux cas.

1. Pourquoi le Kalman Filter existe ?

Retour en 1960.

La NASA développe les missions Apollo.

Problème :

Un satellite reçoit des mesures bruitées.

Exemple :

La vraie position est :

100

Le capteur mesure :

101

98

103

99

102

Quelle est la vraie position ?

Faire une moyenne est insuffisant.

Pourquoi ?

Parce que le satellite continue de bouger.

Il faut un estimateur capable de :

suivre le mouvement ;
filtrer le bruit ;
s'adapter en permanence.

Rudolf Kalman invente alors le Kalman Filter.

2. L'idée fondamentale

Le Kalman Filter ne demande jamais :

Quel est le prix ?

Il demande :

Quelle est la meilleure estimation du prix réel compte tenu de toutes les informations disponibles ?

C'est une énorme différence.

3. Le principe

À chaque nouvelle observation :

Le filtre combine :

Prédiction

+

Nouvelle observation

↓

Nouvelle estimation

Il ne fait confiance ni à l'une ni à l'autre.

Il trouve un compromis optimal.

4. Pourquoi est-il intéressant en finance ?

Les prix sont extrêmement bruités.

Le prix observé est une combinaison de :

Prix fondamental

+

Bruit

+

Microstructure

+

Ordres

+

Liquidité

+

Volatilité

Le Kalman Filter tente d'estimer le prix latent.

5. Comparaison avec une moyenne mobile

Moyenne mobile :

100

101

102

103

↓

Moyenne = 101.5

Problème :

Elle réagit avec retard.

Kalman :

Il adapte automatiquement son estimation.

Si le marché accélère :

Il suit plus vite.

Si le marché est calme :

Il filtre davantage.

6. Les deux étapes
Prediction

Le modèle prédit :

Où devrait être le prix ?

Update

Le marché apporte une nouvelle information.

Le filtre corrige son estimation.

Cette boucle se répète à chaque tick ou à chaque bougie.

7. Pourquoi les quants l'utilisent ?

Parce qu'il permet :

d'estimer un prix "juste" ;
de suivre une tendance sans trop de retard ;
de filtrer le bruit ;
d'estimer des spreads ;
de construire des modèles adaptatifs.

Il est très utilisé en :

Statistical Arbitrage
Pairs Trading
Market Making
Fixed Income
Trading algorithmique
8. Les limites

Le Kalman Filter suppose un modèle d'évolution.

Si ce modèle est mauvais :

Le filtre devient mauvais.

Il faut aussi choisir correctement :

le bruit du processus ;
le bruit de mesure.

Ce sont des paramètres importants.

9. Compatibilité avec IQIA

Je pense qu'elle est excellente.

Aujourd'hui IQIA utilise implicitement :

Rolling Mean

ou

EMA

Le Kalman pourrait fournir une estimation plus robuste.

Par exemple :

Decision

↓

Mean Reverting

↓

Kalman

↓

Prix d'équilibre estimé

↓

OU

↓

Distance statistique

↓

Signal
10. Complémentarité avec OU

OU a besoin d'un équilibre :

μ

Mais comment estimer :

μ

Le Kalman peut justement fournir une estimation adaptative de cette moyenne.

C'est une complémentarité très intéressante.

11. Complémentarité avec le Z-Score

Aujourd'hui :

Z =

Prix

-

Rolling Mean

Demain :

Z =

Prix

-

Kalman Mean

L'écart statistique est alors calculé par rapport à une moyenne plus dynamique.

12. Peut-il remplacer les EMA ?

Pas complètement.

Les EMA restent utiles.

Mais le Kalman est souvent plus élégant pour estimer un état latent lorsque le bruit est important.

13. Où l'intégrer dans IQIA ?

Je ne l'intégrerais pas dans le Decision Engine.

Je le mettrais dans le Signal Engine.

Pourquoi ?

Parce que le Decision Engine répond :

Quel comportement domine ?

Le Kalman répond :

Où se situe probablement l'équilibre actuel ?

Ce n'est pas la même responsabilité.

14. Exemple concret

Supposons :

Behaviour

↓

Mean Reverting

Le Signal Engine pourrait faire :

Kalman

↓

Prix d'équilibre

↓

OU

↓

Probabilité de retour

↓

Dynamic Z-Score

↓

Distance statistique

↓

Signal

On n'a toujours utilisé aucun RSI.

Aucune EMA comme déclencheur.

Uniquement des modèles probabilistes.

15. Verdict scientifique
Critère	Note
Fondement mathématique	⭐⭐⭐⭐⭐
Publications académiques	⭐⭐⭐⭐⭐
Objectivité	⭐⭐⭐⭐⭐
Temps réel	⭐⭐⭐⭐☆
Interprétabilité	⭐⭐⭐⭐☆
Compatibilité IQIA	⭐⭐⭐⭐⭐