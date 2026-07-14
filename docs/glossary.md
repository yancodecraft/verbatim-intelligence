# Glossaire

Les termes récurrents des documents du projet, dans leur sens précis ici.

## Termes produit

- **Verbatim** — un retour client brut, mot pour mot : une réponse à un
  sondage, un avis, un extrait de ticket. L'unité de base du produit ; il
  n'est jamais reformulé, seulement regroupé et cité.
- **Corpus** — l'ensemble des verbatims d'un même import (un fichier CSV).
- **Upload** (`Upload` dans le code) — un fichier CSV soumis par un compte et
  stocké tel quel, en attente d'analyse. Il porte ses colonnes détectées et
  son nombre de lignes de données ; c'est la source depuis laquelle, une fois
  la colonne verbatim désignée (voir *Mapping*), les verbatims sont extraits
  pour constituer le *Corpus* d'une analyse. Un même upload peut fonder
  plusieurs analyses.
- **Thème (émergent)** (`Theme` dans le code) — un regroupement de verbatims
  découvert dans le corpus lui-même, sans taxonomie prédéfinie, pondéré par
  son volume. Chaque verbatim du thème lui est **rattaché** (`ThemeVerbatim`
  dans le code) ; parmi les rattachés, quelques-uns sont **représentatifs**
  (ordonnés par un rang) : ce sont eux que la synthèse cite.
- **Synthèse** — le résumé fidèle d'un thème, accompagné de verbatims
  représentatifs cités mot pour mot. Un verbatim cité est une **référence à
  la ligne d'origine** (le LLM sélectionne des ids, il ne régénère jamais le
  texte) : la fidélité est vérifiée par test.
- **Analyse** — le résultat complet d'un traitement de corpus : thèmes +
  synthèses + verbatims rattachés.
- **Non classé** (`unclassified` dans le code) — un verbatim du corpus
  qu'aucune étape n'a rattaché à un thème. Jamais une perte silencieuse :
  le compte des non classés est toujours calculé (jamais stocké) et visible
  dans les résultats de l'analyse.
- **Mapping** (`ColumnMapping` dans le code) — l'étape d'upload où
  l'utilisateur désigne quelle colonne du CSV contient le verbatim.
- **Compte** (`User` dans le code) — l'identité d'un utilisateur, réduite en
  V1 à son adresse e-mail. Créé à la première connexion (pas d'inscription
  séparée) ; toute donnée (analyses, corpus) appartient à exactement un
  compte.
- **Magic link** — le mécanisme d'authentification de la V1 : un lien de
  connexion à usage unique envoyé par e-mail. Pas de mot de passe. Porté par
  un **`LoginToken`** : aléatoire fort, stocké **hashé**, expirant vite,
  consommé au premier usage.
- **Session** — la connexion établie d'un compte : un token opaque en cookie
  `httpOnly`, adossé à une ligne en base (stockée hashée), donc révocable
  côté serveur.
- **Rapport partageable** — un lien public en lecture seule vers une analyse,
  porté par un **`ShareToken`** : non devinable, révocable et non indexable.

## Termes d'architecture

- **Brique** — l'un des trois composants déployables : `frontend/`,
  `backend/`, `ai-worker/`.
- **Worker** — le processus Python qui consomme les analyses en attente et
  exécute le pipeline LLM. Il n'expose aucune API : il lit et écrit en base.
- **Cycle de vie d'une analyse** — `pending → running → succeeded / failed`.
  L'analyse est l'unité de travail asynchrone du système : son id transite
  par Redis (la file), son état et sa progression vivent en base, qui reste
  la source de vérité. Pas de notion de « job » générique — un seul type de
  traitement existe.
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
- **Fini** — définition canonique en quatre critères dans
  [practices.md](practices.md#définition-de--fini-) : tests en CI, revue de
  code par agent, parcours exercé réellement, en production.
- **TDD** (*test-driven development*) — écrire le test avant le code, en
  cycles courts rouge → vert → refactor. Appliqué strictement sur `backend/`
  et `ai-worker/`.
- **Spike** — une exploration jetable et bornée pour lever un risque avant
  d'industrialiser (ici : le spike pipeline, mené en parallèle de la
  tranche 1). Son code ne
  rejoint pas les briques ; sa conclusion rejoint le journal.
- **Golden corpus** — un corpus de référence anonymisé, aux attentes écrites,
  qui sert à évaluer la qualité du pipeline et à détecter les régressions de
  prompt.
- **Claim atomique** — la prise d'une analyse par un worker en une seule
  opération conditionnelle en base (`UPDATE … WHERE status = 'pending'`) :
  deux workers ne peuvent jamais traiter la même analyse.
- **Heartbeat** — l'horodatage régulier, par le worker, de l'analyse qu'il
  traite ; un heartbeat périmé signale un worker mort.
- **Reaper** — le processus périodique qui détecte les analyses au heartbeat
  périmé (worker mort) et les remet en file ou les passe en échec.
