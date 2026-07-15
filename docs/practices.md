# Pratiques d'ingénierie

Les règles que ce projet s'impose. Elles existent avant la première ligne de
code et s'appliquent à tout ce qui suit.

## Git & commits

- **Trunk-based** : commits directement sur `main`, petits et fréquents.
  `main` est toujours déployable — c'est la branche qui part en production.
- **Conventional Commits 1.0.0**, en anglais : `<type>[scope]: <description>`,
  impératif, ≤ 72 caractères. Types : `feat | fix | docs | style | refactor |
  perf | test | build | ci | chore | revert`.
- **Historique honnête** : pas de rebase cosmétique, pas de squash qui efface
  le cheminement. Les reprises et corrections restent visibles.

## Tests

- **TDD strict** sur `backend/` et `ai-worker/` : le test s'écrit avant le
  code, en cycles courts.
- **Frontend** : tests ciblés sur la logique + un parcours e2e Playwright par
  tranche livrée.
- Les tests d'intégration s'exécutent contre de vraies dépendances
  (Testcontainers : Postgres et Redis réels), pas des mocks d'infrastructure.
- **Un test de contrat transverse** protège le schéma partagé : le backend
  écrit, le worker lit et écrit, le backend relit (voir
  [architecture.md](architecture.md)).
- Pas d'objectif de couverture chiffré : le critère est qu'un comportement
  livré est un comportement testé.

### Tester le pipeline LLM (non déterministe)

Le TDD classique ne s'applique pas à la sortie d'un LLM. Stratégie à deux
niveaux :

- **Invariants déterministes, bloquants en CI** : tout verbatim cité
  correspond à une ligne réelle du corpus (garanti par la sélection d'ids,
  vérifié par test) ; aucun thème vide ; sortie structurellement valide.
  Ces tests cassent le build.
- **Golden corpus + évaluations de qualité, non bloquants** : un corpus de
  référence anonymisé avec des attentes écrites (thèmes attendus, absence de
  doublons/chevauchements), ré-évalué à chaque itération de prompt pour
  détecter les régressions. Suivi, mais hors du gate.
- Paramètres reproductibles sur les runs de test (modèle épinglé), et
  **plafond de coût/tokens** sur les tests comme sur les analyses.
- **Jamais de corpus réel dans le repo ou les fixtures** — les verbatims
  réels sont des données personnelles de tiers.

## Qualité de code

- **Zéro warning, bloquant en CI**, dès le premier jour. Outillage par brique
  (état de l'art à revalider contre les docs officielles au démarrage de
  chaque brique) :

  | Brique | Lint + format | Types / analyse |
  |---|---|---|
  | `frontend/` | Biome (`noUnusedImports`/`noUnusedVariables` désactivées sur `*.vue` : Biome n'analyse que le bloc script, pas le template) | `vue-tsc` en CI |
  | `backend/` | Analyzers Roslyn (`AnalysisLevel=latest`, `EnforceCodeStyleInBuild`) + SonarAnalyzer.CSharp, `.editorconfig`, `TreatWarningsAsErrors` | nullable reference types activés |
  | `ai-worker/` | ruff (lint + format) | mypy strict |
  | Dockerfiles | hadolint (`make lint`) | Trivy : misconfigurations + CVE des lockfiles + secrets (`make audit`) |

- **Hooks pre-commit locaux** : lint + format avant chaque commit — la CI
  n'est jamais le premier filet.
- **Cohérence documentaire en deux couches.** Le hook pre-commit exécute
  `scripts/check_docs.py` (déterministe, bloquant : liens internes,
  définitions canoniques, vocabulaire rejeté par le glossaire). La
  vérification **sémantique** — ce changement impacte-t-il la spec,
  l'architecture, le glossaire, le journal ? — est déroulée par l'agent via
  le skill `doc-check` avant chaque commit. Installation du hook :
  `git config core.hooksPath scripts/githooks`.
- **Dependabot** activé : les mises à jour de dépendances arrivent en continu,
  pas en chantier de fin de projet.
- **Docker est la seule dépendance locale** (avec git). Tout l'outillage du
  projet — hooks, scripts, linters, tests, briques en dev — s'exécute dans
  des conteneurs : contribuer ne requiert aucun runtime installé localement
  (ni Python, ni Node, ni .NET). Les images d'outillage sont épinglées.
  Le `Makefile` racine est l'interface de dev (`make up`, `make down`,
  `make logs`, …) : une façade dont chaque cible délègue à `docker compose` —
  `make` est livré avec l'OS, ce n'est pas une dépendance de plus.
- **Même l'initialisation des briques passe par des conteneurs** : les
  scaffolds officiels (`npm create vue`, `dotnet new`, …) sont exécutés dans
  l'image du runtime concerné, montée sur le repo — les commandes exactes
  sont consignées au journal.

## Conception

- **Le bon pattern au bon endroit, choisi par le besoin.** De l'hexagonal là
  où la logique mérite d'être isolée de l'infrastructure, du CRUD assumé là
  où il n'y a rien à isoler, d'autres patterns si un besoin réel les appelle.
  Concrètement pour la V1 :
  - `ai-worker/` : ports & adaptateurs stricts (`llm`, `storage`, `queue`) —
    le pipeline est en fonctions pures sur des types du domaine et se teste
    sans appeler un LLM.
  - `backend/` : `Domain` (entités + règles, zéro dépendance),
    `Infrastructure` (EF Core, Redis), `Api` (endpoints). Pas de couche
    Application ni de médiateur tant qu'un endpoint = un cas d'usage.
  - `frontend/` : composants / composables / client API, sans hexagonal.
- **Abstraire les frontières, pas les concepts internes.** Toute I/O (base,
  file, LLM, horloge, hasard) passe derrière une abstraction — c'est ce qui
  rend le TDD fluide. À l'inverse, une abstraction interne n'existe que si
  elle a au moins deux implémentations réelles, dont une de test
  (pas d'« interface-ite »).
- **SOLID et clean code comme boussole**, la revue de code comme gardienne :
  noms du domaine, fonctions courtes, pas de code mort, pas de commentaires
  qui paraphrasent le code.
- **DDD : le glossaire fait loi.** Le code emploie exclusivement les termes
  du [glossaire](glossary.md) (`Analysis`, `Verbatim`, `Theme`, `Synthesis`,
  `ColumnMapping`, `ShareToken`…) ; tout nouveau concept passe d'abord par le
  glossaire. `Analysis` est l'agrégat central et porte son cycle de vie ; des
  value objects là où un invariant existe. Pas de domain events, d'event
  sourcing ni de repositories génériques — un seul bounded context.

## CI/CD & exploitation

- **CI en étages à feedback croissant** : lint + tests unitaires d'abord
  (rapides, bloquants), puis intégration Testcontainers et e2e. Images de
  test épinglées par digest, stratégies d'attente explicites.
- **Politique anti-flaky** : retry autorisé uniquement sur le setup
  d'infrastructure, jamais sur les assertions ; un test flaky est mis en
  quarantaine et corrigé, pas ré-exécuté « pour voir ».
- **Migrations** : expand/contract systématique (une migration ne casse
  jamais la version N-1 déployée), appliquées avant le déploiement des
  briques, par EF Core uniquement.
- **Rollback** : images taguées par digest (jamais `latest`) ; revenir en
  arrière = redéployer le digest précédent.
- **Smoke test post-déploiement** : après chaque déploiement, un parcours
  minimal automatique s'exécute contre la production (health + traversée) —
  c'est le filet de sécurité en l'absence de staging.
- **Observabilité minimale** : logs structurés sur les trois briques ;
  métriques du pipeline (durée, coût/tokens, taux d'échec, file qui
  stagne) ; alerte sur analyse `failed` et file bloquée. Les logs ne
  contiennent ni secrets, ni tokens, ni verbatims en clair.

## Définition de « fini »

**Définition canonique — les autres documents y renvoient.** Une tranche est
finie quand :

1. Ses tests passent en CI.
2. Une **revue de code par agent** a été faite et ses conclusions traitées.
3. Le parcours a été exercé réellement sur l'environnement déployé.
4. Elle est en production.

## Sécurité

- **Secrets** : aucun dans le repo (`.env` locaux ignorés, secrets GitHub
  Actions en CI, variables d'environnement en prod) ; secret scanning + push
  protection GitHub activés ; procédure de rotation de la clé API Anthropic
  connue avant d'en avoir besoin.
- **Tout contenu verbatim est non fiable** : échappement systématique à
  l'affichage, `v-html` interdit sur du contenu utilisateur, CSP stricte
  (durcie sur la page de partage publique) ; en cas d'export futur,
  neutralisation des débuts de formule (`=`, `+`, `-`, `@`).
- **Prompt injection** : les verbatims sont des données, jamais des
  instructions — ils sont transmis au LLM dans des champs/balises de données
  distincts du prompt ; aucune décision de sécurité n'est déléguée au LLM.
  La fidélité par référence (le LLM sélectionne des ids) borne l'impact à
  l'intégrité des regroupements.
- **Scoping mécanique (anti-IDOR)** : identifiants exposés non séquentiels
  (UUID), toute requête filtrée par le compte de l'appelant en un point
  d'application unique (global query filter EF Core), et un test
  d'autorisation par endpoint — « B reçoit 404 sur une ressource de A ».
- **`ShareToken`** : ≥ 128 bits issus d'un CSPRNG, URL-safe, stocké hashé,
  comparé en temps constant ; pages de partage `noindex` +
  `Referrer-Policy: no-referrer` ; token jamais loggé ; révocation testée
  en e2e ; rate limiting sur l'endpoint public.
- **Rate limiting** aussi sur la connexion (magic links) et l'upload ; le
  coût LLM est plafonné par analyse.
- **Données personnelles (RGPD)** : traitement LLM sous le DPA d'Anthropic
  (pas d'entraînement, rétention log courte ; le ZDR strict est un arrangement
  entreprise à confirmer), information à l'upload, suppression en cascade, pas
  de corpus réels hors production. Le [registre de sous-traitance](rgpd.md) et
  la politique de confidentialité sont des prérequis d'ouverture.
- L'application déployée reste derrière un basic-auth de proxy tant que la
  V1 n'est pas ouverte ; l'ouverture publique est conditionnée à une
  **security review** dont la checklist minimale est : autorisation/IDOR
  par endpoint, tokens de partage (entropie, hachage, révocation,
  indexation), XSS sur les pages publiques, headers HTTP/CSP/HSTS,
  exposition réseau des datastores, backups (chiffrement + restauration),
  rotation de la clé LLM, rate limiting, RGPD (rétention, suppression,
  mention), robustesse du parsing CSV.

## Documentation & décisions

- Toute décision structurante est consignée dans [JOURNAL.md](../JOURNAL.md)
  (chronologie + justification) — il joue le rôle d'ADR, sans cérémonie
  supplémentaire.
- Les documents de référence vivent dans `docs/` : [spec](v1-spec.md),
  [architecture](architecture.md), [roadmap](roadmap.md),
  [glossaire](glossary.md), [RGPD](rgpd.md),
  [security review](security-review.md), [runbooks](runbooks.md), et ce document.

## Langues

- Documentation : français.
- Code, commits, identifiants, UI : anglais.
