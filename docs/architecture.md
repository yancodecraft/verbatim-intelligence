# Architecture & stack — Verbatim Intelligence

Ce document fige les choix d'architecture et de stack de la V1, en regard du
périmètre défini dans [v1-spec.md](v1-spec.md).

## Vue d'ensemble

Trois briques, un seul contrat de données :

```
frontend/     Vue SPA ──HTTP──► backend/  C# ASP.NET Core (API REST)
                                    │  écrit les données + publie un job
                                    ▼
                               PostgreSQL  ◄── source de vérité unique
                                    ▲       (verbatims, thèmes, synthèses,
                                    │        progression des jobs)
ai-worker/    Python ───────────────┘
                 ▲
                 └── Redis : file d'attente des jobs (signal uniquement)
```

- Le **backend** possède l'API : auth, upload et parsing CSV, mapping des
  colonnes, lecture des analyses, liens de partage. Quand une analyse est
  demandée, il insère les verbatims en base et publie un job dans Redis.
- L'**ai-worker** consomme les jobs : il lit les verbatims en base, exécute le
  pipeline LLM (batching, thèmes émergents, synthèses avec verbatims exacts)
  et écrit résultats et progression en base au fil de l'eau.
- Le **frontend** interroge le backend, qui lit la progression et les
  résultats directement en base. Le worker n'expose aucune API.

## Choix et justifications

| Brique | Choix | Pourquoi |
|---|---|---|
| Frontend | Vue 3 + Vite + TypeScript (SPA) | Écosystème mûr, DX rapide pour un parcours unique |
| Backend | C# / ASP.NET Core | Langage expressif et typé ; API, auth et jobs de fond y sont des terrains bien balisés |
| Worker IA | Python + SDK Anthropic officiel | La langue naturelle d'un pipeline LLM ; découplé du backend, il peut évoluer (prompts, batching) sans toucher à l'API |
| Base | PostgreSQL | Source de vérité unique ; le schéma (migré, versionné) EST le contrat entre backend et worker |
| File d'attente | Redis | Transport minimal du signal « job à traiter » ; aucune donnée métier n'y transite |

### Décisions structurantes

- **Pas d'API interne entre backend et worker.** Le pipeline IA est asynchrone
  par nature (analyser des milliers de verbatims prend des minutes) ; le
  couplage se fait par la base et la file, pas par HTTP. Pas de contrat REST
  interne à versionner, pas d'auth service-à-service.
- **Postgres est la seule source de vérité.** Redis ne porte que des
  identifiants de jobs ; si Redis tombe, on perd au pire un signal (re-publiable),
  jamais une donnée.
- **La progression est une donnée comme une autre** : le worker met à jour le
  job en base, le backend l'expose au front. Le mécanisme de push (polling ou
  SSE) est un détail d'implémentation du backend.

## Organisation du repo

Monorepo — trois briques, une PR/un commit peuvent traverser les frontières
quand une feature l'exige :

```
verbatim-intelligence/
├─ frontend/     # Vue 3 + Vite + TS
├─ backend/      # ASP.NET Core (API, auth, CSV, share)
├─ ai-worker/    # Python (pipeline LLM)
├─ docs/         # spec, architecture, décisions
└─ compose.yaml  # Postgres + Redis + les trois briques, dev local
```

Chaque brique porte ses propres tests ; rien n'est « fini » sans eux.

## Ce qui n'est pas encore décidé

- Le découpage du travail en étapes livrables (prochain document).
- Les choix fins par brique (ORM/migrations côté C#, librairies Python,
  framework de tests front) — décidés au moment où la brique démarre.
- L'hébergement de production — décidé quand on approchera du déploiement.
