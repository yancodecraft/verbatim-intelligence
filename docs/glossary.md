# Glossaire

Les termes récurrents des documents du projet, dans leur sens précis ici.

## Termes produit

- **Verbatim** — un retour client brut, mot pour mot : une réponse à un
  sondage, un avis, un extrait de ticket. L'unité de base du produit ; il
  n'est jamais reformulé, seulement regroupé et cité.
- **Corpus** — l'ensemble des verbatims d'un même import (un fichier CSV).
- **Thème (émergent)** — un regroupement de verbatims découvert dans le
  corpus lui-même, sans taxonomie prédéfinie, pondéré par son volume.
- **Synthèse** — le résumé fidèle d'un thème, accompagné de verbatims
  représentatifs cités mot pour mot. Chaque affirmation doit être traçable
  vers des verbatims réels.
- **Analyse** — le résultat complet d'un traitement de corpus : thèmes +
  synthèses + verbatims rattachés.
- **Mapping** — l'étape d'upload où l'utilisateur désigne quelle colonne du
  CSV contient le verbatim (et d'éventuelles métadonnées).
- **Rapport partageable** — un lien public en lecture seule vers une analyse,
  à token non devinable et révocable.

## Termes d'architecture

- **Brique** — l'un des trois composants déployables : `frontend/`,
  `backend/`, `ai-worker/`.
- **Worker** — le processus Python qui consomme les jobs d'analyse et exécute
  le pipeline LLM. Il n'expose aucune API : il lit et écrit en base.
- **Job** — une unité de travail asynchrone (typiquement : « analyser tel
  corpus »). Le signal transite par Redis ; l'état et la progression vivent
  en base, qui reste la source de vérité.
- **Pipeline (d'analyse)** — l'enchaînement exécuté par le worker : batching
  des verbatims → découverte des thèmes → consolidation → synthèses.

## Termes de méthode

- **Tranche (verticale)** — une unité de livraison qui traverse toutes les
  couches nécessaires (front, API, base, worker) pour livrer un comportement
  complet et démontrable, plutôt qu'une couche technique isolée. Après chaque
  tranche, le produit fonctionne un peu plus ; rien n'est à moitié câblé.
- **Squelette marchant** (*walking skeleton*) — la plus petite version du
  système qui traverse toutes les briques de bout en bout (front → back →
  Redis → worker → base), déployée en vrai. Elle ne fait rien d'utile, mais
  prouve que la tuyauterie complète fonctionne.
- **Fini** — testé en CI, exercé réellement sur l'environnement déployé, et
  en production. Voir [roadmap.md](roadmap.md).
- **TDD** (*test-driven development*) — écrire le test avant le code, en
  cycles courts rouge → vert → refactor. Appliqué strictement sur `backend/`
  et `ai-worker/`.
