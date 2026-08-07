Parfait. Maintenant, on va équilibrer nos recherches.

Jusqu'à présent, nous avons étudié des modèles de Mean Reversion :

✅ Ornstein-Uhlenbeck
✅ Dynamic Z-Score
✅ Kalman Filter

Ils sont excellents... mais uniquement lorsque le Decision Engine détecte un comportement Mean Reverting.

Maintenant, il faut étudier le pendant naturel de ces modèles pour les marchés directionnels.

R-004 — Time Series Momentum (TSMOM)

À mon avis, c'est le modèle de référence pour les marchés Trending.

Contrairement à beaucoup de stratégies de momentum "retail", le Time Series Momentum repose sur une littérature académique très solide.

1. Pourquoi le Time Series Momentum existe ?

Pendant longtemps, la théorie financière dominante (notamment l'Efficient Market Hypothesis) considérait qu'il était impossible de prédire les rendements futurs à partir des rendements passés.

Pourtant, plusieurs études académiques ont observé un phénomène récurrent :

Les actifs qui ont récemment eu une performance positive ont tendance, dans certaines conditions et sur certains horizons, à continuer dans la même direction pendant un certain temps.

Ce phénomène est appelé Momentum.

Le Time Series Momentum (TSMOM) a été largement popularisé en finance quantitative, notamment par les travaux de Tobias Moskowitz, Yao Hua Ooi et Lasse Heje Pedersen (2012).

2. L'idée fondamentale

Le modèle ne demande pas :

"Le prix est-il au-dessus d'une moyenne mobile ?"

Il demande :

"La série présente-t-elle une persistance statistique dans sa direction ?"

C'est une différence fondamentale.

Le TSMOM ne s'intéresse pas au niveau du prix, mais à la continuité des rendements.

3. Principe

Le modèle calcule les rendements sur une période donnée.

Exemple simplifié :

Jour 1 : +0.4 %

Jour 2 : +0.3 %

Jour 3 : +0.5 %

Jour 4 : +0.2 %

Jour 5 : +0.6 %

Le signal n'est pas :

"Le prix est haut."

Le signal est :

"Les rendements présentent une persistance positive."

4. Pourquoi les fonds quantitatifs l'utilisent ?

Le Momentum est l'un des facteurs les plus étudiés en finance.

Il est utilisé dans :

CTA (Commodity Trading Advisors)
Managed Futures
Fonds systématiques
Trend Following
Portefeuilles factoriels

Ce n'est donc pas une méthode marginale.

5. Les fondements mathématiques

Le TSMOM repose sur :

les séries temporelles ;
les rendements logarithmiques ou simples ;
l'autocorrélation des rendements ;
les statistiques de persistance.

Autrement dit :

Il est naturellement cohérent avec les Evidence Models que tu as déjà développés.

6. Forces

Le modèle :

fonctionne sur plusieurs classes d'actifs ;
est entièrement objectivable ;
ne dépend pas d'un indicateur graphique ;
est compatible avec une architecture probabiliste.
7. Limites

Le TSMOM fonctionne mal :

pendant les marchés en range ;
lors des retournements rapides ;
lorsque la persistance disparaît.

Et c'est précisément là que le Decision Engine devient utile.

Si IQIA détecte :

Behaviour

↓

Stable Range

Le TSMOM ne devrait probablement pas être utilisé.

8. Compatibilité avec IQIA

Je vois une intégration très naturelle.

Decision Engine

↓

Behaviour

↓

Trending

↓

Methodology Engine

↓

Time Series Momentum

↓

Signal Engine

Le modèle n'est activé que si le contexte est favorable.

9. Complémentarité avec tes modèles

Tu possèdes déjà :

Persistence
Stationarity
Random Walk

Le TSMOM exploite directement ces informations.

Par exemple :

Persistence élevée → favorable.
Random Walk élevé → défavorable.
Stationarity élevée → souvent défavorable au momentum.

Autrement dit, le Decision Engine prépare déjà le terrain.

10. Ce que je ne ferais pas

Je n'utiliserais pas :

EMA 20 > EMA 50

↓

BUY

C'est une règle heuristique.

Je préférerais :

Decision Engine

↓

Trending

↓

Persistence élevée

↓

Time Series Momentum valide

↓

Volatilité acceptable

↓

Signal
11. Intégration dans IQIA

Je vois le TSMOM comme une méthodologie quantitative, pas comme un indicateur.

Le Signal Engine pourrait ensuite ajouter des preuves complémentaires :

volatilité ;
qualité de la tendance ;
confirmation statistique.
12. Comparaison avec OU
Ornstein-Uhlenbeck	Time Series Momentum
Retour à la moyenne	Persistance de la tendance
Hypothèse stationnaire	Hypothèse de continuité
Utilisé en Mean Reversion	Utilisé en Trend Following
Cherche l'équilibre	Cherche la continuité

Ces deux modèles ne sont pas concurrents.

Ils sont complémentaires.

13. Note scientifique
Critère	Note
Fondement mathématique	⭐⭐⭐⭐⭐
Publications académiques	⭐⭐⭐⭐⭐
Objectivité	⭐⭐⭐⭐⭐
Robustesse	⭐⭐⭐⭐☆
Temps réel	⭐⭐⭐⭐⭐
Compatibilité IQIA	⭐⭐⭐⭐⭐