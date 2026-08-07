R-011 — Cointegration

Auteurs de référence :

Engle & Granger (1987)
Prix Nobel d'économie (Engle, 2003)
⭐ Verdict avant l'étude

Je pense que la cointégration ne sera pas utilisée dans IQIA V1.

Mais...

Je pense qu'elle deviendra probablement un pilier si IQIA évolue vers :

Multi-actifs
Spread Trading
Statistical Arbitrage
ETF Arbitrage
Futures Intermarket

Autrement dit :

Ce n'est pas un modèle pour aujourd'hui. C'est un modèle pour demain.

1. Pourquoi la Cointegration existe ?

Imaginons deux actifs.

Brent

100
102
104
103
105
WTI

95
97
99
98
100

Ils montent ensemble.

Question :

Sont-ils liés ?

Pas forcément.

Ils peuvent simplement monter au même moment.

2. Corrélation ≠ Cointegration

C'est LA première erreur que font les traders.

Deux séries peuvent être très corrélées.

Puis :

Actif A

↑↑↑↑↑↑↑↑

Actif B

↓↓↓↓↓↓↓↓

La corrélation disparaît.

La cointégration pose une autre question :

Existe-t-il une relation d'équilibre à long terme entre ces deux séries ?

3. Exemple

Supposons :

Gold

↑

↓

↑

↓

↑

Silver :

↑

↓

↑

↓

↑

Le ratio entre les deux reste relativement stable.

Même si les prix changent.

Ils peuvent être cointégrés.

4. L'idée fondamentale

Le prix n'a pas besoin d'être stationnaire.

En revanche :

Le spread entre les deux séries peut l'être.

C'est ça la magie.

5. Pourquoi les fonds quantitatifs adorent ça

Parce qu'ils ne tradent pas :

Gold

Ils tradent :

Gold

-

Silver

Ou :

ES

-

NQ

Ou :

Brent

-

WTI

Ils exploitent :

Le retour du spread vers son équilibre.

6. Comment fonctionne le modèle ?

On cherche une combinaison :

A

-

βB

Si cette combinaison est stationnaire :

↓

Les deux actifs sont probablement cointégrés.

7. Compatibilité avec IQIA

Aujourd'hui :

IQIA analyse :

Un seul actif

Donc :

La Cointegration ne sert pratiquement à rien.

Mais demain :

Si IQIA devient :

Multi Assets

Alors :

Elle devient probablement indispensable.

8. Exemple futur

Imaginons :

ES

NQ

RTY

YM

IQIA pourrait détecter :

Spread ES-NQ

↓

Écart statistiquement anormal

↓

OU

↓

Kalman

↓

Signal

On voit que la cointégration ne remplace pas OU.

Elle prépare simplement les données.

9. Compatibilité avec les modèles étudiés

Très forte.

Une fois le spread construit :

On peut appliquer :

OU
Kalman
Dynamic Z
SPRT

Autrement dit :

Tous les modèles étudiés restent valables.

10. Compatibilité avec le Decision Engine

Je ne toucherais pas au Decision Engine.

Je créerais un nouveau niveau :

Market Selection

↓

Cointegration

↓

Decision Engine

Le moteur décide :

Quel spread mérite d'être analysé.

11. Les limites

La cointégration :

nécessite plusieurs actifs ;
demande beaucoup de données historiques ;
peut disparaître avec le temps ;
doit être recalibrée.

Ce n'est donc pas un modèle "plug-and-play".

12. Pourquoi je ne l'intégrerais pas aujourd'hui

Parce que ton objectif actuel est :

Intraday

↓

Mono Actif

La Cointegration ne t'apporterait quasiment rien dans cette configuration.

13. Ce que j'en retiens

Elle est extrêmement puissante.

Mais uniquement pour :

Pairs Trading
Statistical Arbitrage
Relative Value

Pas pour un Future ES analysé seul.

14. Note scientifique
Critère	Note
Fondement mathématique	⭐⭐⭐⭐⭐
Publications académiques	⭐⭐⭐⭐⭐
Objectivité	⭐⭐⭐⭐⭐
Temps réel	⭐⭐⭐⭐☆
Compatibilité IQIA V1	⭐⭐☆☆☆
Compatibilité IQIA V2	⭐⭐⭐⭐⭐