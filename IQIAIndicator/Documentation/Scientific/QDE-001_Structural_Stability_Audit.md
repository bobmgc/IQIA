QDE-10X – Redesign of Structural Stability
IQIA Scientific Specification

Version : 1.0
Statut : Scientific Redesign Proposal
Module : Fusion Engine
Dimension : Structural Stability

1. Objectif

Cette spécification redéfinit le sens scientifique de la dimension Structural Stability.

L'objectif est de corriger une limitation conceptuelle identifiée lors des campagnes Replay ATAS.

La version actuelle mesure principalement :

l'historique des ruptures détectées

alors que la dimension doit représenter :

la stabilité structurelle actuelle du marché.

2. Problème identifié

La version actuelle utilise :

CUSUM
Bai-Perron

pour produire directement un score de stabilité.

Le problème est que ces modèles répondent principalement à la question :

Des ruptures ont-elles été détectées ?

Ils ne répondent pas à :

Le marché est-il actuellement redevenu stable ?
Exemple

Une séance typique :

09:30

Structural Break

↓

09:45

Recovery

↓

10:30

Trending

↓

12:00

Trending

↓

15:00

Trending

Le marché est redevenu parfaitement structuré.

Pourtant :

BreakCount = 6

↓

Structural Stability ≈ 0

pendant toute la journée.

Ce comportement ne représente pas correctement la réalité du marché.

3. Définition scientifique

La dimension Structural Stability est définie comme :

la capacité actuelle du marché à conserver une organisation structurelle cohérente malgré les fluctuations normales des prix.

Elle ne représente pas :

le nombre de ruptures historiques ;
la quantité de changements détectés ;
la volatilité.

Elle représente uniquement :

La cohérence actuelle de la structure du marché.
4. Distinction fondamentale
Ce qui ne doit PAS être mesuré
Nombre de ruptures

BreakCount

Historique des cassures
Ce qui doit être mesuré
Le marché possède-t-il actuellement
une structure identifiable ?
5. Philosophie

Une cassure structurelle est un événement.

La stabilité structurelle est un état.

Ces deux notions sont indépendantes.

Exemple :

Trending

↓

Structural Break

↓

Recovery

↓

Trending

La cassure est temporaire.

La stabilité revient progressivement.

6. Cycle de vie

La stabilité doit évoluer comme un processus.

Stable

↓

Break Detected

↓

Recovery

↓

Reconstructed

↓

Stable

Elle ne doit jamais rester bloquée à zéro uniquement parce qu'une cassure a été détectée auparavant.

7. Recovery Concept

Une nouvelle notion est introduite :

Structural Recovery

Elle représente la reconstruction progressive de la structure du marché après une rupture.

Recovery signifie :

nouveaux swings cohérents ;
diminution des ruptures ;
organisation retrouvée.
8. Utilisation des modèles scientifiques

Les modèles existants restent inchangés.

CUSUM

Continue à détecter :

Transition

Change Point

Instabilité récente
Bai-Perron

Continue à détecter :

Multiples ruptures structurelles

Ces modèles deviennent :

des détecteurs d'événements

et non plus des mesures directes de stabilité.

9. Nouveau pipeline

Ancien pipeline :

CUSUM

+

Bai-Perron

↓

Structural Stability

Nouveau pipeline :

CUSUM

↓

Transition Detector

Bai-Perron

↓

Historical Break Detector

↓

Recovery Evaluator

↓

Structural Stability
10. Structural Break

La notion de Structural Break est redéfinie.

Elle devient :

Market Event

et non plus :

Market State

Un Break possède :

Début

↓

Pic

↓

Diminution

↓

Fin
11. Structural Stability

La stabilité suit la logique inverse.

Stable

↓

Dégradation

↓

Minimum

↓

Recovery

↓

Stable
12. Propriétés attendues

Une bonne dimension Structural Stability doit :

✔ diminuer rapidement lorsqu'une rupture apparaît

✔ rester faible durant la transition

✔ remonter progressivement lorsque la structure revient

✔ atteindre de nouveau une valeur élevée après reconstruction

13. Conséquences sur Decision Engine

Le Decision Engine ne doit plus considérer :

Structural Stability faible

=

Structural Break permanent

Il doit considérer :

Structural Stability faible

+

Transition récente

↓

Structural Break temporaire

Puis :

Recovery

↓

Trending

ou

Stable Range

ou

Mean Reverting
14. Compatibilité

Aucune modification de :

ADF
KPSS
DFA
Half-Life
Variance Ratio
CUSUM
Bai-Perron

Ces modèles restent scientifiquement inchangés.

La modification concerne uniquement :

Fusion Layer
15. Bénéfices attendus

Cette refonte permettra :

une meilleure cohérence entre IQIA et la réalité observée sur les graphiques ;
des transitions de régime plus naturelles ;
un Decision Engine plus fiable ;
un futur Strategy Engine plus pertinent ;
une meilleure stabilité des Market States en Replay et en temps réel.
16. Vision long terme d'IQIA

À terme, IQIA distinguera clairement deux concepts :

Market State (état durable)
Stable Range

Trending

Mean Reverting

Random Walk

Indeterminate

Ces états peuvent durer de plusieurs dizaines à plusieurs centaines de bougies.

Market Events (événements transitoires)
Structural Break

Volatility Expansion

Volatility Compression

Liquidity Sweep

Regime Transition

Ces événements sont temporaires. Ils signalent qu'un changement est en cours, mais ne constituent pas un régime de marché durable.

Conclusion

Cette refonte établit une séparation claire entre les états de marché et les événements de marché. Elle permet à IQIA de décrire le marché de manière plus fidèle : les états représentent la structure durable observée, tandis que les événements décrivent les transitions qui mènent d'un état à un autre.

Cette distinction fournit une base scientifique plus robuste pour les futures couches Decision Engine, Strategy Engine, Signal Engine et Risk Engine, tout en conservant les modèles statistiques existants sans les modifier. À mon avis, c'est une évolution majeure qui renforcera la cohérence et la crédibilité d'IQIA.