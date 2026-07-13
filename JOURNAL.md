# Journal de bord

Le fil chronologique du projet : ce qui a été fait, dans quel ordre, et
pourquoi. Les commits donnent le détail ; ici, c'est la narration et les
décisions. Entrées les plus récentes en haut.

---

## 2026-07-13 — L'auth de bout en bout : magic link, sessions, scoping mécanique

**Fait, en TDD brique par brique :**
- **Modèle** : `users` (compte = e-mail, créé à la première connexion),
  `login_tokens` et `sessions` — les tokens ne sont **jamais stockés en
  clair** (SHA-256 seulement), un lien est à usage unique et expire en
  15 minutes, une session est un cookie httpOnly opaque adossé à une ligne
  révocable en base (30 jours).
- **Endpoints** : `POST /auth/magic-link` (répond 202 que le compte existe
  ou non — pas d'énumération d'e-mails), `POST /auth/verify` (le lien
  pointe vers une page front qui POSTe le token : un GET consommable serait
  grillé par les préchargeurs d'e-mail), `GET /auth/me`, `POST /auth/logout`.
- **Scoping mécanique** (le point structurant de la tranche) : un filtre
  unique `RequireAccount()` sur tout groupe d'endpoints scopé — un handler
  ne peut pas oublier de vérifier ; `user_id` NOT NULL sur `analyses` ;
  l'analyse d'un autre compte répond **404**, indistinguable d'un id
  inexistant. Un test d'autorisation par endpoint. Le DDL miroir des tests
  du worker suit le nouveau schéma.
- **Front** : /sign-in, /verify, garde de route, en-tête de session, sign
  out. Le e2e refait le parcours réel complet : visiteur anonyme muré →
  demande de lien → **lecture du vrai mail via l'API Mailpit** → connexion
  → analyse jusqu'à `succeeded` → sign out.

**Le piège attrapé par le e2e** : les routes auth étaient mappées
`/api/auth/*` côté backend, alors que les deux proxys (Vite en dev, Caddy
en prod) **strippent** `/api` avant de forwarder — le contrat, établi par
`/analyses` en tranche 1, est « routes nues côté backend ». Les tests
d'intégration, qui parlent au backend en direct, ne pouvaient pas le voir ;
le parcours à travers la vraie pile l'a cassé immédiatement. C'est
exactement le rôle du squelette marchant.

Les analyses pré-auth (celles des smoke runs du squelette) sont supprimées
par la migration : elles n'ont pas de propriétaire possible et aucune
valeur.

## 2026-07-13 — Tranche 2 ouverte : l'e-mail d'abord, en vrai dès le premier test

**Décision :** les magic links partiront par **SMTP** (MailKit) derrière une
abstraction minimale — le protocole comme contrat, pas un SDK de
fournisseur. En prod : **Scaleway TEM** (mono-fournisseur avec
l'hébergement, déclarable dans le Terraform existant, DNS posable par
l'outillage déjà en place). En dev, en tests et en e2e : **Mailpit** dans le
compose, dont l'API REST permet de vérifier la réception réelle — le test
d'intégration du sender envoie un vrai mail SMTP et le relit via cette API,
aucun mock. Alternatives écartées : Resend et Postmark (très bons, mais un
fournisseur et un compte de plus pour un besoin que TEM couvre).

**Fait :** Mailpit épinglé par digest dans le compose (UI sur :8125),
`IEmailSender`/`SmtpEmailSender` dans le backend (TDD, rouge d'abord),
Mailpit ajouté à l'ApiFactory des tests. Stack dev vérifiée en réel :
mailpit healthy, backend Healthy avec sa nouvelle config. Pièges du jour :
`ContainerBuilder()` sans image est obsolète (même piège que
PostgreSqlBuilder en tranche 1), et `TestContext.Current` est du xUnit v3 —
le projet est en v2.

## 2026-07-12 — Le spike pipeline a tranché : découverte par batchs + consolidation

**Le dispositif** (`spike/`, script jetable hors des briques, conforme à la
roadmap) : deux stratégies de regroupement comparées à modèle constant
(claude-opus-4-8, structured outputs), sur le golden corpus synthétique
(101 verbatims, 6 thèmes plantés + bruit) puis sur le corpus réel State of
CSS 2021 (748 réponses libres, question « browser incompatibilities »).

- **`direct`** — découverte des thèmes par batchs de 100, puis une passe de
  consolidation qui fusionne les doublons inter-batchs.
- **`taxonomy`** — taxonomie émergente proposée sur un échantillon, puis
  classification de tout le corpus contre elle.

**Verdict : `direct` gagne sur tous les axes, c'est elle qu'on
industrialise en tranche 4.**

| run | résultat | coût | durée |
|---|---|---|---|
| direct / golden | 6/6 thèmes plantés, 100 % de couverture | $0.13 | 54 s |
| taxonomy / golden | 6/6 thèmes, 92-100 % | $0.20 | 122 s |
| direct / réel (748) | 12 thèmes justes (subgrid, « Safari nouveau IE », gap flexbox, fardeau IE11…), couverture complète | $0.48 | 2 min 45 |
| taxonomy / réel | **jamais complète en 4 tentatives** : 342 à 467 verbatims non classifiés | $0.6-1.1 | 10-25 min |

La stratégie `taxonomy` échoue sur un mode de défaillance structurel de la
classification pure : sur un batch entier, le modèle renvoie parfois **la
plus petite sortie qui satisfait le schéma** (un thème, liste d'ids vide) —
en alternance binaire avec des batchs parfaits (80/80). Réduire les chunks,
interdire les listes vides au prompt, ajouter une passe de rattrapage :
rien n'y a fait. La découverte par batchs, elle, n'a jamais failli en sept
runs : le modèle assigne bien quand il découvre, il démissionne quand il
classifie en masse.

**La fidélité par référence tient** : sur tous les runs, zéro violation —
le LLM ne retourne que des ids, chaque citation est résolue depuis le
corpus, et les textes cités sont vérifiés mot pour mot contre la source
(jusqu'aux espaces finales).

**Enseignements pour la tranche 4 :**
1. **La langue de sortie est une décision système**, injectée dans le
   prompt — laissée au LLM (« langue dominante du corpus »), elle a produit
   du français sur un corpus 95 % anglais.
2. **Les structured outputs garantissent la forme, jamais l'exhaustivité ni
   la lettre** : listes silencieusement sous-remplies, variantes de casse
   malgré l'enum, `minItems > 1` rejeté par l'API. Chaque étape du pipeline
   doit vérifier programmatiquement ce qu'elle reçoit (ids valides,
   couverture, noms connus) et prévoir un rattrapage — la V1 comptera et
   exposera ce qui n'a pas pu être traité plutôt que de le taire.
3. **Ordres de grandeur** (opus-4-8, sans parallélisation) : ~$0.50 et
   ~3 min pour 748 verbatims — extrapolé ~$3-4 et 15-20 min pour les 5 000
   de la V1. Le plafond de coût par analyse (déjà dans la spec) est
   confortable ; la latence plaide pour paralléliser les batchs de
   découverte dans le worker.
4. La consolidation inter-batchs produit des thèmes propres et sans
   doublons ; c'est l'appel le plus « intelligent » du pipeline, à soigner.

## 2026-07-12 — Trouver un corpus réel pour le spike pipeline

**Le problème :** le spike pipeline (voir la roadmap) se juge sur des
verbatims réels — du texte libre humain avec son bruit : fautes,
hors-sujet, doublons, longueurs imprévisibles. Or on n'a aucune donnée
client. Un corpus généré par LLM aurait le bon sujet mais pas le bon
bruit ; il ne répondrait pas à la question que le spike doit trancher.

**Pistes examinées :**
- **Sondages développeurs publics** — le meilleur alignement avec un cas
  d'usage type du produit. Stack Overflow Developer Survey publie ses
  données brutes mais quasi exclusivement du fermé (QCM). State of
  JS/CSS : les réponses individuelles complètes ne sont pas en
  libre-service (agrégats via API GraphQL ; le JSON complet se demande
  sur leur Discord).
- **Avis clients** (Kaggle, Hugging Face) : gros volumes très réels —
  US Airline Tweets, avis d'apps ; et pour le **français**, Allociné ou
  amazon_reviews_multi.
- **Consultations publiques françaises** : le Grand Débat National
  (data.gouv.fr, licence ouverte) — des centaines de milliers de
  contributions libres en français.
- **Génération synthétique** : écartée comme corpus principal, mais
  retenue pour le **golden corpus** — ~100-200 verbatims aux thèmes
  plantés et au bruit injecté volontairement, seule façon de connaître
  la vérité terrain et de juger objectivement le pipeline (thèmes
  retrouvés ? bons ids cités ?).

**Trouvé et vérifié :** un extrait officiel en libre accès — le Gist de
Sacha Greif (créateur des sondages) avec les réponses libres de State of
CSS 2021 : **1 099 verbatims réels** sur quatre questions ouvertes, dont
« browser incompatibilities » (748 réponses). Format trivial (JSON, liste
de chaînes → CSV à une colonne verbatim, exactement le contrat
d'ingestion V1), bruit authentique (de « gap » à des paragraphes
techniques). Pas de licence explicite sur le Gist, mais les sondages
annoncent publiquement des données « released openly » — et le corpus
reste de toute façon **hors repo** (règle : jamais de corpus de
verbatims dans le repo), utilisé localement pour le spike.

**Décision :** partir sur la question « browser incompatibilities »
(748 verbatims) comme corpus principal du spike — réel, bruité, proche
d'un cas d'usage cible (sondage développeurs, une question ouverte) —
complété d'un golden corpus synthétique avec attentes écrites. Un corpus
français (Allociné ou Grand Débat) viendra en second temps vérifier que
le pipeline tient dans les deux langues.

## 2026-07-12 — Le déploiement continu boucle la tranche 1

**Fait :** le job `deploy` clôt la chaîne : sur `main`, après lint + tests +
e2e + audit + publication des images, la CI rejoue exactement `make deploy`
avec le SHA du commit — le même geste qu'en local, aucune logique propre au
runner. `concurrency: production` interdit deux déploiements simultanés.
Les credentials (clé SSH de déploiement, secrets de prod) vivent en secrets
GitHub, reconstitués sur le runner à l'exécution.

Avec la revue par agent traitée, le parcours exercé en production et la CI
verte, **les quatre critères de « fini » sont remplis : la tranche 1 est
close.** Prochaine étape selon la roadmap : la tranche 2 (auth par magic
link), avec le spike pipeline à mener en parallèle sur un corpus réel.

## 2026-07-11 — Revue de la tranche 1 par agent, et ses conclusions traitées

**Fait :** la revue de code par agent (critère de « fini ») a passé toute
la tranche — trois briques, compose, Makefile, CI, Terraform, Ansible.
Verdict : aucun bug bloquant, la machine à états, la sécurité réseau et
les invariants annoncés tiennent. Cinq findings, tous traités :
- **Le déploiement retombait silencieusement sur `latest` sans `TAG`**
  (contraire à la pratique « jamais latest ») → le playbook réutilise
  désormais le tag actuellement déployé (changements d'infra purs), et
  refuse s'il n'y a ni TAG explicite ni déploiement antérieur. Vérifié en
  réel.
- **Fuite d'intervalle sur double-clic** dans `CreateAnalysis.vue` (deux
  POST concurrents orphelinaient un timer de polling) → garde `creating` +
  bouton désactivé, test unitaire rouge d'abord.
- **Backups Postgres absents** alors qu'architecture.md les déclare non
  négociables → triage assumé : aucune donnée personnelle en base tant que
  l'ingestion CSV n'existe pas ; la roadmap fait des backups un **prérequis
  explicite de la tranche 3**. L'écart doc ↔ réalité est désormais écrit.
- Dependabot ne couvrait ni l'image d'outillage Ansible ni les providers
  Terraform → ajoutés.
- Le boilerplate du scaffold Vue (tutoriel, logo) était encore l'écran
  d'accueil déployé → remplacé par une coquille produit minimale.

## 2026-07-11 — Premier déploiement : le squelette marche en production

**Fait :** `TAG=<sha> make deploy` a déployé le squelette sur
`https://verbatim.yantech.fr` — certificat Let's Encrypt émis
automatiquement par Caddy, basic-auth active (401 sans credentials), six
conteneurs healthy plus le job de migrations passé avant le backend.
**Parcours exercé en réel en production** : une analyse créée via l'API
publique traverse Caddy → backend → Postgres → Redis → worker et atteint
`succeeded` en ~3 s. Les images tirées de GHCR sont celles que la CI a
poussées au SHA du commit — publiques d'office, le package hérite de la
visibilité du repo.

**Smoke test intégré au déploiement** : après la convergence, le playbook
vérifie depuis le contrôleur, à travers l'edge public, que les requêtes
sans credentials prennent un 401 (la protection fait partie du contrat) et
que `/api/health` répond `Healthy` derrière l'auth — un déploiement qui ne
sert pas de trafic réel n'a pas eu lieu.

Reste pour clore la tranche 1 : le déclenchement automatique du
déploiement depuis la CI (un secret SSH côté GitHub) et la revue par
agent — les critères de « fini ».

## 2026-07-11 — Images de production et déploiement déclaratif de l'app

**Fait :** les images de prod des trois briques — multi-stage, non-root,
bases épinglées : frontend buildé puis servi par un Caddy statique minimal,
backend publié en Release, worker avec son venv uv recopié sur la base
Python slim assortie. Quatrième image : **`migrations`**, le bundle EF Core
auto-porté, exécuté en job one-shot avant le backend
(`depends_on: service_completed_successfully`) — « les migrations sont une
étape de déploiement », c'est maintenant mécanique. La CI les pousse sur
GHCR (tags : SHA du commit — l'unité de déploiement et de rollback — et
latest). Le rôle Ansible `app` installe compose de prod + Caddyfile (TLS +
basic-auth) + `.env` de secrets et converge la stack ;
`TAG=<sha> make deploy` déploie ou rollback.

**Décisions :**
- **Les secrets de prod vivent dans un fichier local hors repo**
  (`~/.config/verbatim-intelligence/prod-secrets.yml`, généré aléatoirement),
  monté en lecture seule dans le conteneur Ansible qui les template en
  `.env` (0600) sur le serveur. Ni dans le repo, ni dans les commandes.
- **Exceptions sécurité documentées et scopées** : l'image d'outillage
  Ansible tourne root (elle doit lire une clé SSH 0600 montée) —
  `.trivyignore.yaml` porte l'exception au seul fichier concerné, avec
  justification ; tout nouveau finding ailleurs casse toujours `make audit`.
- DNS : `verbatim.yantech.fr` → A `51.158.72.142`, posé via l'API Hostinger
  (les nameservers restent chez Hostinger : la zone porte le mail du
  domaine, la migrer vers Scaleway l'aurait cassé pour un bénéfice nul).

## 2026-07-11 — La configuration du serveur devient du code : Ansible

**Fait :** `infra/ansible/` décrit tout l'état du serveur — durcissement
(SSH par clé uniquement, ufw en défense en profondeur derrière le security
group, fail2ban, mises à jour de sécurité automatiques) et Docker Engine +
plugin compose depuis le dépôt officiel. `make configure` construit une
image d'outillage épinglée (ansible-core 2.21.1, collection
community.general 13.1.0) et rejoue le playbook. **Vérifié en réel deux
fois** : premier run 8 changements, second run `changed=0` — l'idempotence
est le test d'un déploiement déclaratif.

**Décision :** aucune commande manuelle sur le serveur ne fait foi. Si un
réglage doit changer, il change dans un rôle Ansible et se rejoue — la
machine est reconstructible de zéro (`make infra-apply && make configure`).

## 2026-07-11 — L'infrastructure devient du code : Terraform sur Scaleway

**Fait :** `infra/terraform/` décrit toute l'infrastructure de prod —
instance DEV1-M Ubuntu 24.04 (3 vCPU / 4 Go, ~15 €/mois, largement assez
pour le squelette), IP flexible, security group fermé sauf 22/80/443, clé
SSH dédiée. Appliqué et vérifié en réel : SSH répond sur l'IP publique.
Terraform tourne en conteneur (`make infra-plan|apply`), image épinglée —
Docker reste la seule dépendance.

**Décisions :**
- **Déploiement 100 % déclaratif** : Terraform pour l'infrastructure,
  Ansible (étape suivante) pour la configuration de la machine. Aucune
  commande manuelle sur le serveur ne fait foi — tout est relisible,
  rejouable, versionné.
- **State distant** dans un bucket Object Storage Scaleway (backend
  S3-compatible), créé par une mini-config de bootstrap au state local
  gitignoré (œuf-et-poule assumé, perte tolérable : un `terraform import`
  le recrée). Un state local pour l'infra elle-même aurait été un piège
  sur un repo public.
- **Credentials jamais dans le repo** : les cibles make lisent la config
  du CLI `scw` au moment de l'invocation et les passent en variables
  d'environnement au conteneur. Le `.terraform.lock.hcl` est versionné
  (il épingle les hashes des providers — même logique que les digests).
- **Registry : GHCR** — gratuit pour un repo public, authentification
  native `GITHUB_TOKEN` dans les workflows, aucun secret supplémentaire.
- Terraform (BUSL, usage interne libre) plutôt qu'OpenTofu, choix
  utilisateur explicite.

## 2026-07-10 — La CI attrape ce que macOS masquait : l'UID du bind mount

**Fait :** premier run GitHub Actions : `test` et `audit` verts du premier
coup (Testcontainers fonctionne sur les runners), mais `lint` rouge —
`dotnet build` ne pouvait pas créer `obj/` dans le bind mount. Sur un hôte
Linux, les fichiers du checkout appartiennent à l'uid du runner et
l'utilisateur non-root du conteneur ne peut pas y écrire ; Docker Desktop
sur macOS masque complètement ce problème. Troisième avatar du piège UID —
cette fois la leçon est structurelle : **rien ne doit écrire dans le bind
mount**.

**Décision :** les sorties de build vivent hors de l'arbre source. Côté
.NET, layout « artifacts » (`UseArtifactsOutput`) pointé via
`ARTIFACTS_PATH` hors du bind mount dans le conteneur ; côté Python, caches
ruff/mypy déplacés (`RUFF_CACHE_DIR`, `MYPY_CACHE_DIR`) et cache pytest
désactivé. Bénéfice collatéral : plus aucun `bin/`, `obj/` ou cache ne
pollue l'arbre de travail, sur aucune machine.

## 2026-07-10 — La CI est le Makefile

**Fait :** `make ci` enchaîne tout le pipeline en local — lint, tests,
build des images, stack up, e2e, audit — et sort à 0. Le workflow GitHub
Actions (`.github/workflows/ci.yml`) n'est qu'un **mince orchestrateur de
ces mêmes cibles make** : chaque job rejoue une commande exécutable sur
n'importe quel poste. Vert sur un laptop = vert en CI, et réciproquement —
pas de logique de pipeline enfermée dans le YAML. Dependabot activé (npm,
NuGet, uv, Docker, Actions), promis dans les pratiques depuis le début.

**Décisions :**
- Étages à feedback croissant : lint / tests / audit en parallèle, le e2e
  (le plus lent) ne part que si lint et tests passent.
- Actions épinglées par SHA de commit, pas par tag mutable — même logique
  que les digests d'images.
- `host.docker.internal` déclaré via `extra_hosts: host-gateway` dans le
  compose : les tests Testcontainers joignent les ports publiés sur l'hôte
  Docker, y compris sur un runner Linux nu.

Trouvaille en passant : le formateur ruff normalise vers la nouvelle
syntaxe Python 3.14 (PEP 758) — `except A, B:` sans parenthèses.

## 2026-07-10 — Le squelette est visible à l'écran, et un e2e le prouve

**Fait :** la page d'accueil gagne le bouton « Create analysis »
(`CreateAnalysis.vue`, TDD : trois tests Vitest à timers simulés — création,
polling jusqu'à l'état terminal puis arrêt, erreur réseau). Le statut est
pollé chaque seconde jusqu'à `succeeded`/`failed`. Et le **premier parcours
e2e Playwright** de la tranche : un conteneur Playwright (image épinglée,
version alignée sur `@playwright/test`) rejoint le réseau compose, ouvre la
page servie par le conteneur frontend, clique, et attend que l'analyse
traverse API → Postgres → Redis → worker jusqu'à `succeeded` — 1,6 s en
réel. `make e2e` (la stack doit tourner : `make up`).

**Décision :** le e2e s'exécute contre la **vraie stack de dev**, pas un
serveur éphémère — c'est la traversée réelle qu'on veut prouver, et c'est le
même parcours que la CI rejouera. Vite doit accepter l'hôte `frontend`
(`server.allowedHosts`) pour être joignable depuis le réseau compose.

## 2026-07-10 — La file Redis relie backend et worker : le squelette a une colonne vertébrale

**Fait :** Redis 8 dans le compose (mot de passe même en dev, **aucun
volume** : la file est un pur transport, l'état vit en base). Le
`POST /analyses` pousse l'id dans `analyses:pending` **après** le commit en
base ; le worker fait un `BLPOP`, un **claim atomique**
(`UPDATE … WHERE status = 'pending' … RETURNING`) puis passe l'analyse en
`succeeded` (le vrai pipeline attendra le spike). TDD des deux côtés :
côté C#, le test vérifie l'id dans la file (Testcontainers Redis) ; côté
Python, cinq tests couvrent pop/claim/complete — dont « une analyse déjà
`running` n'est pas re-claimable » et « un id malformé est ignoré ».
Vérifié en réel : POST depuis le front → `succeeded` en ~50 ms, file vide.

**Deux leçons de la première exécution réelle :**
- **redis-py 8 pose un socket timeout par défaut** : un `BLPOP` bloquant
  doit avoir un socket timeout supérieur au timeout serveur, sinon le
  client abandonne avant la réponse.
- **La boucle du worker mourait sur la première erreur transitoire**,
  conteneur toujours « healthy » — le silent failure type. La boucle
  attrape désormais les erreurs de dépendances, loggue et réessaie ; seul
  un bug inattendu la tue. La leçon rejoint la résilience déjà actée
  (claim/heartbeat/reaper, tranche 4).

La clé `analyses:pending` est un contrat entre briques
(`RedisKeys.PendingAnalyses` en C#, `PENDING_ANALYSES_KEY` en Python) —
comme le schéma. Un id poussé mais jamais consommé reste `pending` en
base : c'est le reaper qui remettra ces orphelines en file (tranche 4).

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
