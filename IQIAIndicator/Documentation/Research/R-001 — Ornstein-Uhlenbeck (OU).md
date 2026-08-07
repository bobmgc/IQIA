R-001 — Ornstein-Uhlenbeck (OU)

Je commence volontairement par OU, parce que je pense que c'est probablement le modèle le plus important pour IQIA.

1. Pourquoi OU existe ?

Avant OU, il faut comprendre un problème.

Imaginons une action.

Elle passe de :

100

↓

102

↓

104

↓

103

↓

101

↓

99

↓

101

↓

100

Question :

Le prix revient-il vers une valeur "naturelle" ?

Ou bien est-ce complètement aléatoire ?

Les modèles classiques ne répondaient pas correctement à cette question.

En 1930, George Uhlenbeck et Leonard Ornstein introduisent un processus pour décrire un système qui est :

perturbé en permanence,
mais attiré vers un équilibre.

Au départ, ils ne parlaient pas de finance.

Ils décrivaient le mouvement d'une particule dans un fluide (mouvement brownien avec frottement).

Plus tard, les financiers ont compris que ce modèle décrivait remarquablement certains actifs financiers.

2. L'idée fondamentale

Le prix subit deux forces.

Force 1

Le hasard.

Brownian Motion

Le marché est perturbé.

Force 2

Le retour vers une moyenne.

Mean Reversion

Le marché est attiré vers une valeur d'équilibre.

OU combine ces deux forces.

3. Formule

Le modèle continu est :

dX
t
	​

=θ(μ−X
t
	​

)dt+σdW
t
	​


Elle paraît impressionnante, mais chaque terme a un sens très intuitif.

X
t
	​


Valeur actuelle.

μ

Valeur d'équilibre.

Le marché cherche à revenir ici.

θ

Vitesse de retour.

Si :

θ grand

↓

Le marché revient vite.

Si :

θ petit

↓

Le marché revient lentement.

σ

Le bruit.

Plus il est grand,

plus le marché est chaotique.

dW
t
	​


Le hasard.

Le Brownian Motion.

4. Pourquoi OU est extraordinaire ?

Parce qu'il modélise parfaitement ceci :

Prix

      ↑

105

104

103

102

101

100 ← moyenne

99

98

97

96

95

Le prix peut s'éloigner.

Mais plus il s'éloigne,

plus la force de rappel augmente.

C'est exactement ce que l'on observe sur beaucoup de marchés en range.

5. Ce qu'il permet de calculer

OU fournit énormément d'informations.

Par exemple :

Valeur d'équilibre
μ
Force du retour
θ
Volatilité
σ
Probabilité de retour

Très intéressant.

Temps moyen de retour

Encore plus intéressant.

Half-Life

Que tu utilises déjà.

En réalité,

Half-Life provient directement du modèle OU.

Donc IQIA exploite déjà une partie de ce modèle.

6. Utilisation en finance

OU est utilisé notamment pour :

Statistical Arbitrage
Pairs Trading
Spread Trading
Volatility Modeling
Fixed Income
Commodities
Options

Il est omniprésent dans les desks quantitatifs.

7. Les limites

OU suppose que :

la moyenne existe ;
elle reste relativement stable ;
le processus est stationnaire.

Donc :

En tendance forte,

OU devient mauvais.

Très mauvais.

C'est important.

Il ne faut jamais appliquer OU partout.

8. Compatibilité avec IQIA

Je pense qu'elle est excellente.

Pourquoi ?

IQIA possède déjà :

✅ Stationarity

↓

OU a besoin de stationnarité.

IQIA possède :

✅ Mean Reversion

↓

OU est précisément un modèle de Mean Reversion.

IQIA possède :

✅ Half-Life

↓

C'est déjà un paramètre du modèle.

IQIA possède :

✅ Decision Engine

↓

Il peut décider :

OU autorisé

ou

OU interdit

selon le comportement détecté.

9. Ce que je ne ferais PAS

Beaucoup de traders font :

ZScore > 2

↓

SELL

Je ne veux pas ça.

Je préférerais :

Decision Engine

↓

Mean Reverting

↓

OU valide

↓

Le prix est éloigné de μ

↓

La probabilité de retour est élevée

↓

Signal.

OU devient une preuve,

pas un déclencheur.

10. Ce que je retiens

Pour moi,

OU n'est PAS une stratégie.

C'est un modèle probabiliste.

C'est très différent.

Note scientifique
Critère	Note
Fondement mathématique	⭐⭐⭐⭐⭐
Publications académiques	⭐⭐⭐⭐⭐
Robustesse	⭐⭐⭐⭐⭐
Objectivité	⭐⭐⭐⭐⭐
Explicabilité	⭐⭐⭐⭐⭐
Compatibilité IQIA	⭐⭐⭐⭐⭐
Mon verdict

Je pense qu'OU doit devenir l'un des piliers du futur Signal Engine, mais uniquement lorsque le Decision Engine a déjà identifié un comportement compatible avec la Mean Reversion.

Je ne l'utiliserais jamais comme un indicateur universel.