# Découpage du travail — V1

Ce document ordonne le travail vers la V1 définie dans [v1-spec.md](v1-spec.md),
sur l'architecture de [architecture.md](architecture.md). Le principe : des
**tranches verticales** — chaque tranche traverse les briques concernées de
bout en bout, se termine testée et déployée, et laisse le produit démontrable.
(Les termes récurrents sont définis dans le [glossaire](glossary.md).)

## Définition de « fini »

La définition canonique (quatre critères, dont la revue de code par agent)
est dans [practices.md](practices.md#définition-de--fini-) — elle s'applique
à chaque tranche.

## Le spike pipeline — mené en parallèle de la tranche 1

Un spike n'est pas une tranche (voir le [glossaire](glossary.md)) : rien
n'est livré, on lève un risque. Le plus gros risque du produit est la
qualité des thèmes et des synthèses ; il se vérifie avant d'industrialiser
quoi que ce soit. Un **script Python autonome et jetable** (hors des
briques, non déployé) fait tourner le pipeline LLM sur un corpus réel : il
tranche la stratégie de regroupement, mesure un ordre de grandeur de coût
et de latence par analyse, et se juge sur un golden corpus avec des
attentes écrites. Sa conclusion est consignée au [journal](../JOURNAL.md)
avant la tranche 4 — c'est elle qu'on industrialise, pas une hypothèse.

## Les tranches, dans l'ordre

### 1. Squelette marchant + CI/CD + déploiement

Le produit ne fait rien, mais tout le reste s'y verse.

- Monorepo initialisé : `frontend/` (Vue 3 + Vite + TS), `backend/`
  (ASP.NET Core), `ai-worker/` (Python), `compose.yaml` (Postgres + Redis +
  les trois briques).
- Traversée minimale : le front affiche un statut servi par le back ; le back
  crée une analyse vide et la met en file ; le worker la consomme et fait
  évoluer son statut en base.
- CI dès le premier jour avec les trois volets par brique : **lint** (Biome /
  analyzers Roslyn + `dotnet format` / ruff + mypy), **tests**, **build**
  d'images Docker.
- CD : déploiement sur un VPS (Docker Compose + Caddy pour le TLS), déclenché
  sur `main`. L'app est en ligne — protégée par un basic-auth au niveau du
  proxy tant que la V1 n'est pas ouverte.

### 2. Auth

Comptes par **magic link** (pas de mots de passe : rien à stocker, pas de
reset, pas de brute-force), sessions, et scoping mécanique : identifiants
exposés en UUID, filtrage par compte en un point unique, un test
d'autorisation par endpoint. Faite tôt pour que chaque tranche suivante
naisse scopée — zéro rétrofit.

### 3. Ingestion CSV

Upload d'un fichier sous le contrat CSV de la [spec](v1-spec.md) (UTF-8,
délimiteur auto-détecté, limites), aperçu des colonnes, désignation de la
colonne verbatim, insertion des verbatims en base. Rejet propre avec message
clair (fichier invalide, vide, trop gros) ; le contenu verbatim est traité
comme non fiable dès cette tranche (échappement à l'affichage).

**Prérequis de cette tranche : les backups Postgres** (automatiques,
chiffrés, hors machine, restauration testée — la contrainte non négociable
d'[architecture.md](architecture.md#hébergement)). Dès cette tranche, la
base contient des données personnelles de tiers : pas d'ingestion réelle
sans sauvegarde restaurable.

### 4. Pipeline d'analyse

Le cœur du produit — placé au plus tôt car c'est le plus gros risque :
la qualité des thèmes et des synthèses fait ou défait la proposition de valeur.

- Industrialisation de la stratégie validée par le spike pipeline.
- L'analyse est mise en file par le backend, consommée par le worker (claim
  atomique, heartbeat, reaper, pipeline idempotent — voir
  [architecture.md](architecture.md)).
- Batching des verbatims, découverte des thèmes émergents, consolidation,
  synthèses avec verbatims représentatifs sélectionnés **par référence**
  (le LLM retourne des ids ; la fidélité est testée, pas espérée).
- Progression écrite en base au fil de l'eau ; échec visible avec son
  erreur ; coût plafonné par analyse.

### 5. Restitution

L'écran d'analyse : thèmes pondérés par volume, synthèse par thème, verbatims
exacts mis en avant. La progression de l'analyse en cours est visible.

### 6. Rapport partageable

Lien public en lecture seule (token non devinable, révocable) vers une
analyse. Dernière tranche : si le périmètre doit être réduit, c'est la
première coupée — la V1 reste cohérente sans elle.

## Règles de travail

- Une tranche à la fois ; on ne commence pas la suivante tant que la
  précédente n'est pas « finie » au sens ci-dessus.
- Le pipeline d'analyse (tranche 4) sera itéré sur des corpus réels dès que
  possible — la qualité des synthèses se juge sur de vraies données, pas sur
  des fixtures.
