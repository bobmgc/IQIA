Verdict avant l'étude

Je pense déjà que le Z-Score sera probablement le cœur du Signal Engine.

Pas parce qu'il est populaire.

Parce qu'il est mathématiquement fondé.

1. Pourquoi le Z-Score existe ?

Imaginons une série de prix :

100
101
99
100
101
100
99
100
101
100

Question :

Est-ce que 102 est "loin" ?

Impossible à dire.

Pourquoi ?

Parce que "loin" dépend toujours de la volatilité.

Exemple A

100
100.2
99.8
100.1
99.9

102 est énorme.

Exemple B

100
105
95
104
96

102 est banal.

Le Z-Score répond précisément à ce problème.

2. Principe

Il mesure :

Combien d'écarts-types séparent le prix actuel de sa moyenne ?

Pas :

Prix - Moyenne

Mais :

(Prix - Moyenne)
/ Écart-type

Autrement dit :

Il normalise l'écart.

3. Formule
Z=
σ
X−μ
	​


avec :

X = prix actuel
μ = moyenne
σ = écart-type
4. Interprétation
Z = 0

Le prix est exactement sur sa moyenne.

Z = +1

Le prix est à

1 écart-type.

Situation normale.

Z = +2

Le prix est déjà très éloigné.

Z = +3

Très rare.

Dans une distribution normale :

≈0.3 %

Z = -2

Même logique.

Mais en dessous de la moyenne.

5. Pourquoi les quants adorent le Z-Score ?

Parce qu'il est :

indépendant du prix.

Exemple :

Un actif à :

20 €

et un autre à

2000 €

peuvent être comparés.

Impossible avec un simple écart.

6. Le gros problème

Les traders utilisent souvent :

Moyenne mobile

+

Écart-type fixe

Ce n'est pas idéal.

Pourquoi ?

Parce que le marché change.

Je préfère :

Rolling Mean
Rolling Std

Ou mieux :

EWMA

Encore mieux :

Kalman Filter

(on l'étudiera ensuite)

7. Ce que permet le Z-Score

Il permet de calculer :

Distance statistique.

Pas seulement distance en prix.

C'est énorme.

8. Utilisation en finance

Le Z-Score est utilisé dans :

Statistical Arbitrage
Mean Reversion
Pairs Trading
Spread Trading
ETF Arbitrage
Volatility Trading
9. Limites

Le Z-Score suppose implicitement :

distribution relativement stable.

En tendance :

100

102

104

106

108

110

Le Z-Score peut rester :

+2

+2.5

+3

+4

Le marché continue pourtant de monter.

Donc :

Un Z-Score élevé

≠

SELL.

Très important.

10. Compatibilité IQIA

Excellente.

Le Decision Engine peut dire :

Behaviour

↓

Mean Reverting

↓

Le Signal Engine est autorisé à utiliser :

Dynamic Z-Score

Si :

Behaviour

↓

Trending

↓

Le Z-Score ne sert plus
de déclencheur.

Il peut éventuellement servir
de filtre.

11. Une erreur énorme des traders

Ils font :

Z > 2

↓

SELL

Non.

Le bon raisonnement est :

Decision Engine

↓

Mean Reversion

↓

OU compatible

↓

Z-Score = +2.8

↓

Le prix est statistiquement
très éloigné

↓

Chercher une confirmation

↓

Signal.

Le Z-Score ne crée jamais le signal.

Il fournit une preuve.

12. Ce que je mettrais dans IQIA

Je ne créerais jamais :

ZScoreIndicator

Je créerais :

StatisticalDistanceEvidence

À l'intérieur :

Z-Score
Distance percentile
Distance normalisée
Distance historique

Toutes ces informations deviennent
une Evidence.

13. Encore mieux

Je pense que :

OU

Dynamic Z-Score

sont complémentaires.

OU répond :

Le prix doit-il revenir ?

Le Z-Score répond :

Le prix est-il suffisamment éloigné ?

Ensemble :

Behaviour

↓

Mean Reverting

↓

OU valide

↓

Z = +2.6

↓

Probabilité de retour élevée

↓

Attendre confirmation

↓

Signal
14. Ce que je retiens

Je pense que le Z-Score sera l'une des principales évidences du futur Signal Engine.

Mais il ne doit jamais être utilisé seul.

Il doit être combiné avec :

OU ;
le contexte de marché fourni par le Decision Engine ;
d'autres preuves indépendantes.
Note scientifique
Critère	Note
Fondement mathématique	⭐⭐⭐⭐⭐
Publications académiques	⭐⭐⭐⭐⭐
Objectivité	⭐⭐⭐⭐⭐
Robustesse	⭐⭐⭐⭐☆
Temps réel	⭐⭐⭐⭐⭐
Compatibilité IQIA	⭐⭐⭐⭐⭐