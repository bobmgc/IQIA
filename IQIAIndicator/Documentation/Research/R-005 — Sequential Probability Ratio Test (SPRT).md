R-005 — Sequential Probability Ratio Test (SPRT)

Auteur : Abraham Wald (1943)

À mon avis, c'est probablement le meilleur candidat pour remplacer les règles du type :

SI A ET B ET C

↓

BUY
1. Pourquoi le SPRT existe ?

Pendant la Seconde Guerre mondiale, Abraham Wald travaille sur un problème très concret.

Imagine une usine qui fabrique des obus.

On ne peut pas tester :

100 000 obus

Il faut décider rapidement :

le lot est-il bon ?
faut-il continuer les tests ?
faut-il rejeter le lot ?

Le problème est exactement celui-ci :

Comment prendre une décision avec le moins d'observations possible tout en gardant un risque d'erreur contrôlé ?

C'est la naissance du Sequential Probability Ratio Test.

2. L'idée fondamentale

Contrairement aux tests classiques.

On ne fixe PAS :

100 observations

Puis on décide.

Le SPRT fait ceci :

Nouvelle observation

↓

Mettre à jour la probabilité

↓

Décider

ou

Continuer

Autrement dit :

Chaque nouvelle information compte.

3. Ce que fait réellement le SPRT

Il compare deux hypothèses.

Exemple :

H0

Pas de signal

contre

H1

Signal valide

Chaque nouvelle évidence modifie la probabilité.

4. Pourquoi je pense immédiatement à IQIA

Regarde ton architecture actuelle.

Aujourd'hui tu pourrais avoir :

Kalman

↓

OK

OU

↓

OK

Z-Score

↓

OK

Volatilité

↓

OK

↓

Signal

Mais...

Pourquoi quatre preuves suffisent-elles ?

Pourquoi pas trois ?

Pourquoi pas cinq ?

Le SPRT répond précisément à cette question.

5. Fonctionnement

Le test accumule les preuves.

Par exemple :

Evidence 1

↓

Probabilité = 55 %

Evidence 2

↓

65 %

Evidence 3

↓

78 %

Evidence 4

↓

91 %

↓

Signal.

Ou :

Evidence 1

↓

48 %

Evidence 2

↓

44 %

Evidence 3

↓

39 %

↓

Pas de signal.
6. Le gros avantage

Le moteur peut s'arrêter très tôt.

Exemple :

Premier test :

Très favorable.

Deuxième :

Très favorable.

Troisième :

Très favorable.

↓

Inutile d'attendre dix confirmations.

Le signal est déjà statistiquement solide.

7. Pourquoi les quants aiment ce modèle

Parce qu'il permet :

décision probabiliste ;
contrôle du risque d'erreur ;
adaptation dynamique ;
intégration naturelle de plusieurs sources.
8. Compatibilité avec IQIA

Je pense qu'elle est exceptionnelle.

Regarde.

Le Decision Engine fonctionne déjà comme une fusion.

Le Signal Engine pourrait faire exactement la même chose.

Evidence

↓

OU

↓

Kalman

↓

Dynamic Z

↓

Volatility

↓

SPRT

↓

Signal

Le SPRT devient :

le juge final.

9. Ce qu'il remplace

Il remplace :

SI

A

ET

B

ET

C

↓

BUY

qui est arbitraire.

Par :

Accumulation probabiliste
des preuves

↓

Décision.
10. Ce que je ne ferais PAS

Je ne créerais jamais :

SignalRule

if ZScore > 2

if OU > 0.8

if Volatility < X

BUY

Je créerais :

Evidence

↓

Likelihood

↓

SPRT

↓

BUY
11. Compatibilité avec OU

OU produit :

Probabilité de retour.

SPRT peut utiliser cette probabilité.

12. Compatibilité avec Kalman

Kalman produit :

Estimation

SPRT peut utiliser la qualité de cette estimation.

13. Compatibilité avec Z-Score

Le Z-Score fournit :

Distance statistique.

Encore une Evidence.

14. Compatibilité avec Momentum

Même chose.

Momentum produit :

Persistance.

Le SPRT peut intégrer cette information.

15. Une idée nouvelle

Je pense que le Signal Engine ne devrait même plus être constitué de "Rules".

Je vois plutôt :

Evidence Providers

↓

Probability Engine

↓

Sequential Decision

↓

Signal

Autrement dit :

Les Rules deviennent simplement des producteurs d'évidences.

La décision finale est confiée au SPRT.

16. Exemple IQIA
Decision

↓

Mean Reverting

↓

Methodology

↓

OU

↓

Kalman

↓

Dynamic Z

↓

Evidence

↓

Likelihood

↓

SPRT

↓

BUY
17. Les limites

Le SPRT n'invente rien.

Il ne crée aucune information.

Si les évidences sont mauvaises,

la décision sera mauvaise.

Autrement dit :

Le SPRT dépend énormément
de la qualité des modèles.

18. Note scientifique
Critère	Note
Fondement mathématique	⭐⭐⭐⭐⭐
Publications académiques	⭐⭐⭐⭐⭐
Objectivité	⭐⭐⭐⭐⭐
Explicabilité	⭐⭐⭐⭐⭐
Temps réel	⭐⭐⭐⭐⭐
Compatibilité IQIA	⭐⭐⭐⭐⭐⭐

Oui.

Je lui mets 6 étoiles.

Pourquoi ?

Parce que je pense que nous venons peut-être de trouver le moteur décisionnel idéal pour ton futur Signal Engine.