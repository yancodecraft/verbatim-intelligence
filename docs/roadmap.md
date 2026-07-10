# Découpage du travail — V1

Ce document ordonne le travail vers la V1 définie dans [v1-spec.md](v1-spec.md),
sur l'architecture de [architecture.md](architecture.md). Le principe : des
**tranches verticales** — chaque tranche traverse les briques concernées de
bout en bout, se termine testée et déployée, et laisse le produit démontrable.
(Les termes récurrents sont définis dans le [glossaire](glossary.md).)

## Définition de « fini »

Une tranche est finie quand :

1. Ses tests passent en CI (TDD sur backend et worker ; tests ciblés + un
   parcours e2e Playwright côté front).
2. Le parcours a été exercé réellement sur l'environnement déployé.
3. Elle est en production (déploiement continu depuis `main`).

## Les tranches, dans l'ordre

### 1. Squelette marchant + CI/CD + déploiement

Le produit ne fait rien, mais tout le reste s'y verse.

- Monorepo initialisé : `frontend/` (Vue 3 + Vite + TS), `backend/`
  (ASP.NET Core), `ai-worker/` (Python), `compose.yaml` (Postgres + Redis).
- Traversée minimale : le front affiche un statut servi par le back ; le back
  publie un job factice ; le worker le consomme et écrit en base.
- CI dès le premier jour avec les trois volets par brique : **lint** (Biome /
  analyzers Roslyn + `dotnet format` / ruff + mypy), **tests**, **build**
  d'images Docker.
- CD : déploiement sur un VPS (Docker Compose + Caddy pour le TLS), déclenché
  sur `main`. L'app est en ligne — protégée par un basic-auth au niveau du
  proxy tant que la V1 n'est pas ouverte.

### 2. Auth

Comptes (email + mot de passe ou magic link), sessions, et scoping : toute
donnée créée ensuite appartient à un compte. Faite tôt pour que chaque
tranche suivante naisse scopée — zéro rétrofit.

### 3. Ingestion CSV

Upload d'un fichier, aperçu des colonnes, mapping (colonne verbatim +
métadonnées optionnelles), insertion des verbatims en base. Limites et
messages d'erreur propres (fichier invalide, vide, trop gros).

### 4. Pipeline d'analyse

Le cœur du produit — placé au plus tôt car c'est le plus gros risque :
la qualité des thèmes et des synthèses fait ou défait la proposition de valeur.

- Publication d'un job d'analyse, consommation par le worker.
- Batching des verbatims, découverte des thèmes émergents, consolidation,
  synthèse par thème avec verbatims représentatifs **mot pour mot**.
- Progression écrite en base au fil de l'eau ; gestion des échecs (reprise,
  job en erreur visible).

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
