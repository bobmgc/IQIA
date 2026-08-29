# EntryTrigger

Cette couche implémente le moteur de timing d'entrée IQIA.

## Objectifs

- Lire uniquement les résultats existants : OpportunityPresentation, ScientificAssessment, EntryAssessment, EntryCandidate.
- Déterminer un statut de trigger sans recalculer la science.
- Proposer un signal directionnel à partir des informations disponibles.
- Ne jamais effectuer de trading automatique.
