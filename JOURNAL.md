# Journal de bord

Le fil chronologique du projet : ce qui a été fait, dans quel ordre, et
pourquoi. Les commits donnent le détail ; ici, c'est la narration et les
décisions. Entrées les plus récentes en haut.

---

## 2026-07-10 — Naissance de l'ai-worker

**Fait :** troisième brique — `ai-worker/` (Python 3.14, gestion **uv**,
lockfile `uv.lock`). À ce stade une boucle placeholder : elle prouve que le
worker tourne, joint Postgres (`is_database_ready`, TDD contre un Postgres
Testcontainers) et loggue — la consommation de la file arrive avec Redis.
Hot reload par **watchfiles** vérifié (édition sur l'hôte → boucle
relancée). Commandes d'initialisation exactes (image
`ghcr.io/astral-sh/uv@sha256:7cf7…e6c1`, uv 0.9.30) :

```sh
uv init --package ai-worker
uv add 'psycopg[binary]'
uv add --dev pytest mypy ruff 'testcontainers[postgres]' watchfiles
```

**Décisions :**
- **Python 3.14** (dernière stable — pas de notion de LTS en Python) sur
  base **bookworm-slim**, pas alpine : les wheels binaires (psycopg) sont
  universels sous glibc, musl reste une loterie.
- **ruff en `select = ["ALL"]`** : tout activé, exclusions explicites et
  commentées (opt-out plutôt qu'opt-in, cohérent avec zéro warning) ;
  mypy strict.
- Le healthcheck du conteneur réutilise `is_database_ready` : « healthy »
  signifie « le worker voit la base ».

Piège récurrent des volumes de deps, deuxième occurrence (venv pollué par
un run root) : les runs de test root passent désormais en `--no-sync`, et
`make rebuild` reste la remise à zéro standard.

## 2026-07-10 — La table `analyses` et le lien backend ↔ Postgres

**Fait :** Postgres 18 dans le compose (digest épinglé, aucun port publié —
`make psql` ouvre un client dans le conteneur), et la première table du
schéma-contrat : `analyses` (id UUID v7, status contraint par CHECK,
created_at). EF Core en est l'unique propriétaire : entité `Analysis`,
`AppDbContext`, migration `InitialCreate` générée par `dotnet ef` en
conteneur, appliquée au démarrage en dev (`Database__MigrateOnStartup` —
en prod ce sera une étape de déploiement). Endpoints `POST /analyses`
(crée une analyse `pending`) et `GET /analyses/{id}`, écrits en TDD.

**Décisions :**
- **Le schéma parle snake_case** (`UseSnakeCaseNamingConvention`) et les
  statuts sont des chaînes en minuscules : le worker Python lira et écrira
  ces colonnes, le schéma est un contrat entre deux langages, pas un détail
  C#. Le CHECK sur `status` est le garde partagé — vérifié en réel : un
  `UPDATE … SET status='bogus'` est rejeté par la base.
- **Testcontainers dès le premier test d'intégration** : l'API est testée
  contre un vrai Postgres jetable, jamais un fake. Les tests tournant déjà
  en conteneur, `make test` monte le socket Docker (et
  `TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal`, les ports mappés
  étant publiés sur l'hôte Docker) — compromis dev assumé.
- **`/health` inclut désormais la base** (`AddDbContextCheck`) : « Backend
  is up » sur la page d'accueil signifie API **et** base joignables.
- L'horloge est injectée (`TimeProvider`) — on abstrait les frontières,
  pas les concepts.

Piège Postgres 18 : l'image officielle veut son volume monté sur
`/var/lib/postgresql` (plus `…/data`), pour des `pg_upgrade` sans franchir
un point de montage.

## 2026-07-10 — Le front parle au back

**Fait :** première traversée entre briques — la page d'accueil affiche
l'état du backend (`BackendStatus.vue`, testé fetch mocké : test rouge
d'abord). Vérifié en réel dans le navigateur, dans les deux sens : backend
démarré → « Backend is up » ; conteneur backend arrêté → « Backend is
down ».

**Décision — proxy Vite plutôt que CORS.** Le front appelle `/api/*`, le
dev server Vite retire le préfixe et forwarde vers `backend:8080` — le
même contrat de routage que Caddy assurera en prod. Aucune configuration
CORS nulle part : le navigateur ne voit qu'une seule origine, en dev comme
en prod.

## 2026-07-10 — Naissance du backend

**Fait :** deuxième brique — `backend/` (ASP.NET Core minimal API, .NET 10
LTS, SDK 10.0.301) avec un seul endpoint `GET /health`, écrit en TDD : le
test d'intégration (`WebApplicationFactory`) a échoué avant que l'endpoint
existe. Solution au format `.slnx` (le nouveau défaut du SDK 10), projet
API + projet de tests xUnit, branchée au compose (port hôte 8180) avec
reload-sur-sauvegarde vérifié à travers le bind mount. Commandes
d'initialisation exactes (image `mcr.microsoft.com/dotnet/sdk@sha256:940f…3d89`) :

```sh
dotnet new sln -n VerbatimIntelligence -o backend   # produit un .slnx
dotnet new webapi -o backend/src/VerbatimIntelligence.Api
dotnet new xunit -o backend/tests/VerbatimIntelligence.Api.Tests
dotnet new editorconfig -o backend
dotnet new gitignore -o backend
dotnet sln backend/VerbatimIntelligence.slnx add backend/src/... backend/tests/...
dotnet add backend/tests/... reference backend/src/...
dotnet add backend/tests/... package Microsoft.AspNetCore.Mvc.Testing
```

**La politique zéro warning a mordu dès le scaffold** (`Directory.Build.props` :
`TreatWarningsAsErrors`, `AnalysisLevel=latest-recommended`,
`EnforceCodeStyleInBuild`) :
- NU1903 : le transitif `Microsoft.OpenApi` 2.0.0 porte une CVE haute
  sévérité — remonté en référence directe 2.10.0, à retirer quand
  `Microsoft.AspNetCore.OpenApi` tirera une version corrigée.
- CA1707 neutralisée sur les seuls projets de tests (les underscores dans
  les noms de tests sont la convention xUnit).

**Décision — le build est le linter.** Sur .NET, l'analyse vit dans le
compilateur : `make lint` fait donc un `dotnet build` (tous les analyzers +
audit NuGet), `dotnet format --verify-no-changes` ne gardant que la porte
« rien de non-formaté ». En profondeur au-delà du SDK : un seul pack tiers,
**SonarAnalyzer.CSharp** (bugs, smells, sécurité) — Roslynator ou Meziantou
seulement si des manques réels apparaissent, même logique anti-double-emploi
que Trivy. Ses premières prises sur le scaffold : S6966 (`await
app.RunAsync()` plutôt que `Run()`) et S1118 (constructeur protégé sur la
classe `Program` exposée aux tests).

**Décision — `dotnet watch --no-hot-reload` :** le hot reload .NET patche
le code top-level sans effet jusqu'au restart (ENC0118) — or nos routes
minimal API vivent précisément là : un endpoint ajouté ne répondrait
jamais, piège silencieux. Un restart complet à chaque sauvegarde est
déterministe. À revoir quand les routes quitteront `Program.cs`.

Piège vécu sur le volume NuGet : ensemencé une première fois en root, il
était illisible pour l'utilisateur non-root du conteneur — `make rebuild`
(purge des volumes de deps) est la réponse standard à ce genre d'état.

## 2026-07-10 — Trivy comme scanner sécurité unique

**Décision :** Trivy seul pour la sécurité (`make audit`), challengé contre
les spécialistes. L'alternative « best of breed » — Grype (CVE) + Checkov
(misconfigurations) + Gitleaks (secrets) — gagne chaque discipline mais
coûte trois images à maintenir, trois formats de sortie, trois sources de
faux positifs. Trivy est assez bon sur les quatre besoins (misconfig
Dockerfile/compose, CVE des lockfiles, secrets, et demain le scan des
images en CI), OSS Apache 2.0, standard de facto. Même logique que Redis
vs RabbitMQ : pas de double emploi. Faiblesses assumées : les secrets ne
sont scannés que sur l'arbre de travail, pas l'historique git (la push
protection GitHub couvre ce trou) ; les checks de misconfig sont moins
riches que Checkov (hadolint compense sur les Dockerfiles). **À rouvrir
si** : des contributeurs externes arrivent (→ Gitleaks, l'historique
devient une surface) ou du vrai IaC apparaît (→ Checkov).

## 2026-07-10 — Naissance du frontend, compose de dev et Makefile

**Fait :** la première brique tourne — `frontend/` (Vue 3 + Vite + TS +
vue-router + Vitest, Biome en linter/formateur — pas d'ESLint/Prettier),
servie par le `compose.yaml` de dev avec hot reload vérifié à travers le
bind mount, et pilotée par un `Makefile` en façade
(`make up|down|rebuild|logs|ps|lint|audit|outdated`).

**Décision :** monter la stack brique par brique, chaque composant vérifié
seul avant d'être relié au suivant (front → back → base → worker → file),
plutôt qu'un squelette monté d'un bloc. Chaque brique est initialisée avec
son outillage officiel, exécuté dans un conteneur — Docker reste la seule
dépendance locale — après vérification des versions : runtime sur la LTS
active (Node 24, pas la maintenance 22), dépendances scaffoldées contrôlées
à jour (`npm outdated`). Commandes d'initialisation exactes :

```sh
docker run --rm -v "$PWD":/work -w /work \
  node:24-alpine@sha256:a0b9bf06e4e6…fbfd \
  sh -c "npm create --yes vue@latest frontend -- --ts --router --vitest"

docker run --rm -v "$PWD/frontend":/app -w /app \
  node:24-alpine@sha256:a0b9bf06e4e6…fbfd \
  npm install --package-lock-only --ignore-scripts

docker run --rm -v "$PWD/frontend":/app -w /app \
  node:24-alpine@sha256:a0b9bf06e4e6…fbfd \
  npm install -D --package-lock-only --ignore-scripts @biomejs/biome@2.5.3
```

**Qualité et sécurité dès la première brique :**
- Biome sur les défauts (le scaffold est reformaté une fois), avec un
  override sur `*.vue` : `noUnusedImports`/`noUnusedVariables` désactivées —
  Biome n'analyse que le bloc script, les imports du template sont des faux
  positifs.
- hadolint sur les Dockerfiles (`make lint`), Trivy en misconfigurations +
  CVE + secrets (`make audit`) — leurs premières passes ont imposé l'image
  épinglée, `USER node` (non-root) et un `HEALTHCHECK` (sur `127.0.0.1` :
  busybox wget résout `localhost` en `::1`, Vite n'écoute qu'en IPv4).

Choix du `Dockerfile.dev` : les dépendances sont installées dans l'image
(`npm ci`, reproductible) et un volume nommé masque `node_modules` sous le
bind mount — jamais les modules de l'hôte dans le conteneur ; le volume
n'étant ensemencé qu'à sa création, `make rebuild` le supprime après un
changement de dépendances. Port hôte 5180 (5173 est souvent pris par
d'autres projets Vite).

## 2026-07-10 — Docker comme seule dépendance locale

**Décision :** contribuer au projet ne requiert que git et Docker. Tout
l'outillage (hooks, scripts, plus tard linters, tests et briques en dev)
s'exécute dans des conteneurs — premier appliqué : le hook pre-commit, qui
lance `check_docs.py` dans `python:3.13-alpine` (le hook fait les appels git
côté hôte et passe le nécessaire au conteneur ; le script n'a aucune
dépendance à git). Aucun runtime à installer, aucune dérive « ça marche sur
ma machine ».

## 2026-07-10 — Garde-fous de cohérence documentaire

**Fait :** un hook pre-commit + un skill agent pour empêcher la dérive
docs ↔ réalité (le défaut exact que la revue d'experts venait d'attraper).

**Décision :** deux couches, pas une. Le déterministe dans le hook git
(`scripts/check_docs.py` : liens internes, définitions canoniques,
vocabulaire rejeté — rapide, bloquant) ; le sémantique dans un skill
(`doc-check`) que l'agent déroule avant chaque commit — un appel LLM n'a
rien à faire dans un hook git (lent, coûteux, non déterministe), mais un
script ne sait pas juger si un changement impacte la spec.

## 2026-07-10 — Revue des docs par un panel d'experts

**Fait :** avant d'écrire la moindre ligne de code, quatre revues croisées
(architecture, craft/qualité, produit/delivery, sécurité) menées par des
agents spécialisés sur l'ensemble des documents. Leurs critiques convergentes
ont été intégrées partout.

**Décisions issues de la revue :**
- **La fidélité devient un invariant technique** : un verbatim cité est une
  référence (FK) vers la ligne d'origine — le LLM sélectionne des ids, il ne
  régénère jamais le texte. Testable, donc testé.
- **Spike pipeline** mené en parallèle du squelette (ce n'est pas une
  tranche : exploration jetable, rien n'est livré) — le plus gros risque
  produit (qualité des thèmes/synthèses) se vérifie sur corpus réel avant
  d'industrialiser, jugé sur un golden corpus.
- **Résilience du traitement** : claim atomique, heartbeat, reaper,
  idempotence, machine à états gardée en base — le vrai risque était le
  worker qui meurt après avoir pris le signal, pas la perte de Redis.
- **Le schéma-contrat se gère comme un contrat** : EF Core seul propriétaire
  des migrations, expand/contract (jamais casser N-1), test de contrat
  transverse C# ↔ Python.
- **Auth tranchée : magic link seul** — le compromis produit × sécurité
  (scoping dès le départ, zéro mot de passe à protéger).
- **Périmètre raboté** : le mapping ne demande que la colonne verbatim ;
  « aucun format imposé » remplacé par un contrat CSV explicite.
- **Sécurité** : RGPD assumé (zero-data-retention API, suppression en
  cascade, jamais de corpus réels en repo), spec du ShareToken (CSPRNG,
  hashé, noindex), anti-IDOR mécanique, contenu verbatim non fiable,
  durcissement VPS, checklist de security review.
- **Exploitation** : CI en étages, rollback par digest, smoke test
  post-déploiement, observabilité et coût LLM plafonné.

## 2026-07-10 — Pratiques d'ingénierie

**Fait :** [pratiques](docs/practices.md), définies avant la première ligne de code.

**Décisions :**
- Trunk-based : commits directs sur `main`, toujours déployable — les PR à
  soi-même sont de la cérémonie pour un solo.
- Zéro warning, bloquant en CI dès le premier jour (`TreatWarningsAsErrors`,
  ruff + mypy strict, Biome en mode erreur) : aucune dette à rattraper.
- La définition de « fini » gagne une étape : revue de code par agent avant de
  déclarer une tranche terminée.
- Security review formelle avant d'ouvrir l'app au public ; Dependabot et
  hooks pre-commit dès la tranche 1.
- Conception : le bon pattern au bon endroit (hexagonal strict dans le
  worker, Domain isolé côté C#, CRUD assumé quand il n'y a rien à isoler) ;
  on abstrait les frontières (I/O, LLM, horloge), pas les concepts internes.
- DDD : le glossaire fait loi dans le code (ubiquitous language), `Analysis`
  comme agrégat central, value objects sur les invariants réels. Pas de
  domain events ni de repositories génériques — un seul bounded context.

## 2026-07-10 — L'analyse comme unité de travail (pas de « jobs »)

**Fait :** reprise des docs — la table générique `jobs` disparaît au profit
de l'entité `analyses` qui porte son propre cycle de vie
(`pending → running → succeeded / failed`), sa progression et son erreur.

**Décision :** nommer par le domaine plutôt que par la technique. Un seul
type de traitement asynchrone existe en V1 ; une abstraction « job »
généralisait pour des besoins imaginaires (YAGNI). La file Redis ne
transporte que des ids d'analyses. Si un second type de traitement apparaît,
on généralisera avec un vrai besoin sous les yeux.

## 2026-07-10 — Découpage du travail en tranches verticales

**Fait :** [roadmap](docs/roadmap.md) en six tranches + [glossaire](docs/glossary.md).

**Décisions :**
- Des tranches verticales plutôt qu'un développement couche par couche : après
  chaque tranche, le produit est démontrable et rien n'est à moitié câblé.
- Tranche 1 = squelette marchant **déployé** : la CI/CD et l'hébergement se
  valident au jour 1, pas à la fin — le déploiement tardif est le risque
  classique d'un projet solo.
- Auth en tranche 2 (juste après le squelette) pour que toute donnée naisse
  scopée — zéro rétrofit.
- Le pipeline d'analyse au plus tôt (tranche 4) : c'est le cœur et le plus
  gros risque produit.
- TDD strict sur backend et worker ; côté front, tests ciblés + un parcours
  e2e Playwright par tranche.
- Hébergement : VPS + Docker Compose — le plus simple pour les cinq
  conteneurs applicatifs (Postgres, Redis, trois briques), avec Caddy en
  frontal pour le TLS.

## 2026-07-10 — Architecture et stack

**Fait :** [architecture](docs/architecture.md) — trois briques, un contrat de données.

**Décisions :**
- Frontend Vue 3 + Vite + TS, backend C#/ASP.NET Core, worker IA en Python
  (SDK Anthropic officiel). Trois langages assumés : chaque brique a un rôle
  net, et le repo sert aussi de vitrine polyglotte.
- **Pas d'API interne entre backend et worker.** Le pipeline étant asynchrone
  par nature, le couplage passe par PostgreSQL (source de vérité unique) et
  Redis (transport du signal « analyse à traiter », rien d'autre). Un service
  IA HTTP aurait coûté un contrat interne, de l'auth service-à-service et du
  debugging réseau pour un bénéfice nul à cette échelle.
- **Redis plutôt que RabbitMQ pour la file.** L'état et la reprise des
  analyses vivent en base (claim atomique, heartbeat, reaper) ; la file ne
  sert qu'à réveiller le worker. Les garanties d'un broker (acks,
  redelivery, routage) feraient double emploi avec le mécanisme en base,
  au prix d'un service stateful de plus sur un VPS unique. À rouvrir si
  plusieurs types de messages ou du fan-out apparaissent.
- Reporté sciemment : choix fins par brique (ORM, libs), hébergement de prod.

## 2026-07-10 — Spec produit V1

**Fait :** [spec V1](docs/v1-spec.md) — persona, parcours, périmètre, non-goals.

**Décisions :**
- Persona unique : le PM qui reçoit du feedback en vrac et doit prioriser.
- Un seul parcours : compte → upload CSV (~5 000 lignes) → mapping des
  colonnes → analyse avec progression → thèmes émergents → synthèses avec
  verbatims mot pour mot → lien de partage public révocable.
- Principe produit : **fidélité avant exhaustivité** — jamais de paraphrase
  présentée comme citation.
- Non-goals assumés : connecteurs/API d'ingestion, suivi temporel des thèmes,
  équipes/rôles, facturation. Le suivi temporel est la direction long terme,
  pas la V1.

## 2026-07-09 — Naissance du repo

**Fait :** repo initialisé avec le pitch et la méthode dans [CLAUDE.md](CLAUDE.md).

**Décision :** le repo grandit organiquement — définir le quoi, puis le
comment, puis construire tranche par tranche. Chaque étape est discutée avant
d'être committée ; l'historique reste honnête (pas de rebase cosmétique).
