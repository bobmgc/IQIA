R-005 — Sequential Probability Ratio Test (SPRT)

Ce document est la référence scientifique officielle du `Sequential Probability Ratio Test` pour IQIA.

1. Pourquoi le SPRT existe ?

Abraham Wald développe le SPRT pendant la Seconde Guerre mondiale pour résoudre un problème clair : comment décider rapidement si un lot est bon ou défectueux sans tester inutilement toutes les pièces.

Le SPRT répond au problème suivant :

- un test classique fixe un nombre d'observations avant de décider ;
- en séquence, les données arrivent une à une ;
- le SPRT cherche à prendre une décision aussi tôt que possible tout en contrôlant le risque d'erreur.

Wald montre qu'un test séquentiel peut atteindre les mêmes erreurs de type I et II qu'un test à taille fixée, avec en moyenne beaucoup moins d'observations. C'est cette efficacité séquentielle qui justifie l'existence du SPRT.

2. L'idée fondamentale

Le SPRT ne fixe pas un nombre d'observations à l'avance.

À chaque nouvelle donnée :

- on met à jour le rapport de vraisemblance ;
- on compare ce rapport à deux bornes ;
- si les bornes sont franchies, on accepte H0 ou H1 ;
- sinon, on continue.

Ainsi, le test décide le plus tôt possible lorsque l'hypothèse est suffisamment forte. Si les observations sont ambiguës, il continue ; si elles convergent rapidement, il s'arrête rapidement.

3. Théorie académique

Le SPRT compare deux hypothèses rivales :

- H0 : hypothèse nulle ;
- H1 : hypothèse alternative.

Pour chaque observation x_i, on calcule le rapport de vraisemblance :

Likelihood Ratio :
Λ_n = Π_{i=1}^n f(x_i | H1) / f(x_i | H0)

En pratique, on travaille souvent sur la somme logarithmique :

Log Likelihood Ratio :
S_n = log Λ_n = Σ_{i=1}^n log( f(x_i | H1) / f(x_i | H0) )

Les bornes de Wald sont déterminées par les niveaux d'erreur désirés α et β :

A = (1 - β) / α
B = β / (1 - α)

En termes log :

a = log A
b = log B

La règle séquentielle est :

- si S_n ≥ a, accepter H1 ;
- si S_n ≤ b, accepter H0 ;
- sinon, continuer avec la prochaine observation.

Variables :

- x_i : i-ème observation ;
- f(x | H0) : densité de probabilité sous H0 ;
- f(x | H1) : densité de probabilité sous H1 ;
- Λ_n : rapport de vraisemblance cumulative ;
- S_n : log rapport de vraisemblance ;
- α : risque de type I (faux positif) ;
- β : risque de type II (faux négatif) ;
- A, B : bornes de Wald ;
- a, b : bornes logarithmiques.

Hypothèses IQIA

Dans IQIA, les hypothèses du SPRT sont explicitement liées au pipeline de Mean Reversion.

- H0 : les preuves scientifiques accumulées ne sont pas suffisantes pour considérer qu'une opportunité de Mean Reversion est statistiquement crédible.
- H1 : les preuves scientifiques accumulées sont suffisamment fortes pour considérer qu'une opportunité de Mean Reversion est statistiquement crédible.

Ces hypothèses ne portent pas sur un prix isolé, mais sur la cohérence de l'ensemble des preuves issues du Kalman, de l'Ornstein-Uhlenbeck, du Dynamic Z-Score et de la volatilité.

Construction de la Likelihood

La construction de la vraisemblance dans IQIA repose exclusivement sur les métriques déjà produites par le pipeline scientifique. Le SPRT ne recalcule jamais ces métriques ; il les consomme comme preuve.

Le rôle de chaque métrique est le suivant :

- `DynamicZScore` fournit la preuve centrale d'écart statistique par rapport à l'équilibre latent ;
- `MeanReversionStrength` évalue la plausibilité du retour vers l'équilibre ;
- `RelativeVolatility` ajuste l'interprétation de l'écart en fonction du niveau de volatilité courant ;
- `VolatilityConfidence` pondère la fiabilité de cette évaluation de volatilité ;
- `DynamicConfidence` pondère la confiance intrinsèque du Dynamic Z-Score ;
- `HalfLife` apporte l'horizon temporel attendu du retour ;
- `CurrentVolatility` indique le niveau de bruit actuel à prendre en compte.

Dans IQIA, ces métriques sont des entrées de la preuve séquentielle. Elles ne sont jamais recalculées par le SPRT ; elles sont uniquement consommées.

Paramètres α et β

Dans le contrat IQIA, les paramètres α et β définissent le compromis entre vitesse de décision et contrôle du risque d'erreur. Ils représentent respectivement :

- α : le risque de type I, c'est-à-dire la probabilité de conclure à tort qu'une opportunité de Mean Reversion est crédible ;
- β : le risque de type II, c'est-à-dire la probabilité de ne pas détecter une opportunité crédible.

A ce stade, la décision de les fixer ou de les rendre configurables appartient à la méthodologie IQIA. Si l'implémentation ne précise pas ces valeurs, elles doivent rester dépendantes de la méthodologie Mean Reversion et de la politique de risque opérationnelle.

4. Pourquoi SPRT est intéressant pour IQIA ?

IQIA est une plateforme temps réel. Le SPRT est adapté à ce contexte car il :

- ne dépend pas d'un nombre d'observations fixe ;
- intègre les preuves en continu ;
- permet d'arrêter l'analyse dès que les preuves sont suffisantes ;
- offre un contrôle clair du risque d'erreur.

Un simple seuil fixe manque de flexibilité : il exige une seule métrique et un seul niveau de tolérance. Le SPRT, en revanche, accepte ou rejette une hypothèse en fonction d'un cumul de preuves.

En IQIA, le SPRT complète parfaitement les étapes scientifiques précédentes :

Kalman
↓
OU
↓
Dynamic Z
↓
Volatility
↓
SPRT

Il ne remplace aucun de ces modèles ; il les enrichit en agrégeant leur preuve dans une logique séquentielle.

5. Variante IQIA Real-Time SPRT

L'adaptation IQIA du SPRT est conçue comme une étape de validation scientifique.

Le modèle ne recalcule jamais :

- EstimatedMean
- InnovationStd
- Theta
- HalfLife
- DynamicZScore
- RelativeVolatility
- VolatilityRegime

Il utilise uniquement les résultats déjà produits par le pipeline précédent.

Cette adaptation sépare clairement :

- la théorie académique du SPRT ;
- l'implémentation IQIA qui consomme des métriques existantes comme preuve.

6. Principe d'architecture

Le SPRT est positionné après la chaîne de validation existante.

Market Data
↓
Kalman
↓
Ornstein-Uhlenbeck
↓
Dynamic Z-Score
↓
Volatility
↓
SPRT

Le SPRT enrichit la chaîne scientifique. Il n'en remplace jamais les modèles précédents. Il ne sert pas à générer une nouvelle estimation d'équilibre ou de volatilité : il valide la cohérence de la preuve accumulée.

Pipeline IQIA

Market Data
↓
Kalman
↓
Ornstein-Uhlenbeck
↓
Dynamic Z-Score
↓
Volatility
↓
SPRT
↓
LikelihoodRatio
↓
LogLikelihoodRatio
↓
DecisionStrength
↓
EvidenceStrength
↓
SPRTConfidence
↓
SignalCandidate

Synchronisation avec le pipeline

Le SPRT ne recalcule jamais :

- EstimatedMean
- InnovationStd
- Theta
- HalfLife
- DynamicZScore
- RelativeVolatility
- VolatilityRegime
- CurrentVolatility
- DynamicConfidence
- VolatilityConfidence

Toutes ces informations proviennent déjà des modèles précédents. Le SPRT les exploite uniquement.

Une information scientifique = une seule source de vérité.

7. Données consommées

Le SPRT doit consommer précisément les données produites en amont :

- `DynamicZScore` : mesure de la distance normalisée au prix d'équilibre. Elle fournit la preuve centrale d'écart statistique.
- `RelativeVolatility` : indique si le marché est plus ou moins turbulent que sa référence. Elle ajuste l'interprétation de l'écart.
- `VolatilityRegime` : label qualitatif du contexte de volatilité. Il oriente la logique de validation vers des régimes calmes, moyens ou agités.
- `MeanReversionStrength` : force du retour vers l'équilibre. Elle renseigne sur la plausibilité de l'hypothèse mean reversion.
- `HalfLife` : horizon attendu de retour. Elle permet de calibrer la vitesse d'acceptation/rejet.
- `CurrentVolatility` : niveau de bruit actuel. Elle sert à normaliser l'influence des observations.
- `DynamicConfidence` : confiance du Dynamic Z-Score. Elle participe à la pondération des preuves.
- `VolatilityConfidence` : confiance de l'évaluation de volatilité. Elle est utilisée comme facteur de fiabilité des preuves.

Chaque métrique est utilisée pour transformer l'ensemble des preuves précédentes en un score séquentiel. Le SPRT ne ré-estime jamais ces grandeurs : il les consomme pour juger l'hypothèse.

8. Données produites

Le SPRT doit produire les métriques suivantes :

- `LikelihoodRatio` : rapport de vraisemblance cumulatif entre H1 et H0.
  - Rôle : accumuler la preuve observée contre ou pour l'hypothèse de signal.
  - Interprétation : valeurs élevées favorisent H1, valeurs faibles favorisent H0.
  - Limites : nécessite un modèle de vraisemblance bien défini.

- `LogLikelihoodRatio` : somme logarithmique du rapport de vraisemblance.
  - Rôle : simplifier le calcul séquentiel et éviter les sous-flux numériques.
  - Interprétation : signe et amplitude indiquent la direction et la force de la preuve.
  - Limites : dépend de la modélisation des densités sous H0 et H1.

- `SPRTDecision` : résultat séquentiel.
  - Rôle : indiquer si le SPRT accepte H1, accepte H0 ou continue.
  - Interprétation : `ACCEPT_H1`, `ACCEPT_H0`, `CONTINUE`.
  - Limites : ne doit pas être confondu avec un signal de trading.

- `DecisionStrength` : distance relative au seuil de décision.
  - Rôle : mesurer à quel point la preuve est proche d'une décision définitive.
  - Interprétation : plus la valeur est grande, plus la décision est robuste.
  - Limites : métrique d'orientation interne, non une probabilité.

- `EvidenceStrength` : amplitude de la preuve actuelle.
  - Rôle : quantifier la qualité de l'information accumulée.
  - Interprétation : elle renseigne sur la solidité statistique de la preuve.
  - Limites : dépend du modèle de vraisemblance et du calibrage des bornes.

- `SPRTConfidence` : confiance interne du résultat SPRT.
  - Rôle : indiquer la fiabilité relative de la décision.
  - Interprétation : valeur interne bornée entre 0 et 1.
  - Limites : c'est un indicateur de robustesse, pas une probabilité d'exactitude.
  - Statut : sa formule exacte sera précisée dans l'implémentation scientifique si elle n'est pas encore définie.

- `Diagnostics` : ensemble des paramètres et métriques utilisés.
  - Rôle : fournir l'auditabilité scientifique du calcul.
  - Interprétation : trace détaillée des preuves, des bornes et des métriques consommées.
  - Limites : ne remplace pas une revue complète des hypothèses.

9. Hypothèses

Le SPRT repose sur les hypothèses suivantes :

✔ Kalman exécuté
✔ Ornstein-Uhlenbeck exécuté
✔ Dynamic Z-Score exécuté
✔ Volatility exécuté
✔ Mean Reversion validée
✔ ScientificResults disponibles et cohérents

Si l'une de ces hypothèses est violée, le SPRT ne doit pas produire de preuve scientifique.

10. Limites

Le SPRT devient peu fiable lorsque :

- le marché est non stationnaire ;
- il existe une rupture structurelle ;
- la volatilité change brutalement ;
- le comportement de mean reversion est absent ;
- les données sont insuffisantes ou erratiques.

Dans ces cas, les densités de vraisemblance sont mal calibrées et le SPRT peut arrêter trop tôt ou trop tard.

11. Compatibilité IQIA

Le SPRT s'intègre naturellement à IQIA :

- Decision Engine : il fournit une preuve supplémentaire, sans remplacer la décision finale.
- Methodology Engine : il s'applique dans la méthodologie Mean Reversion en tant que validation scientifique.
- ScientificModels : il consomme les sorties antérieures et les enrichit par un test séquentiel.
- Signal Engine : il ne génère pas de signal, il fournit un score de preuve exploitable par l'Entry Engine.

12. Principe fondamental

Le SPRT ne déclenche jamais une position.

Il ne décide jamais :

- BUY
- SELL
- WAIT

Il produit uniquement une preuve quantitative.

La décision finale appartient exclusivement au futur Entry Engine.

Évolutions futures

La construction exacte de la Likelihood, ainsi que la calibration finale de α et de β, pourront évoluer dans les futures versions d'IQIA.

Toute évolution devra :

- être documentée dans R-005 ;
- être synchronisée avec le code.

13. Conclusion

Le SPRT est un validateur scientifique de preuve séquentielle.

Il agrège les résultats des modèles antérieurs, il contrôle le risque d'erreur et il indique si la preuve est suffisante pour accepter ou rejeter l'hypothèse de signal.

Il est un complément du pipeline IQIA, jamais une stratégie autonome.

14. Note scientifique

Critère | Note
--- | ---
Fondement mathématique | ⭐⭐⭐⭐⭐
Publications académiques | ⭐⭐⭐⭐⭐
Robustesse | ⭐⭐⭐⭐
Explicabilité | ⭐⭐⭐⭐
Compatibilité IQIA | ⭐⭐⭐⭐⭐

15. Verdict

Le SPRT doit être la validation séquentielle du pipeline IQIA.

Il confirme la qualité de la preuve avant que l'Entry Engine n'examine la position.
