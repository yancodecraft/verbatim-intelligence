# Architecture & stack — Verbatim Intelligence

Ce document fige les choix d'architecture et de stack de la V1, en regard du
périmètre défini dans [v1-spec.md](v1-spec.md).

## Vue d'ensemble

Trois briques, un seul contrat de données :

```
frontend/     Vue SPA ──HTTP──► backend/  C# ASP.NET Core (API REST)
                                    │  écrit les données + met l'analyse en file
                                    ▼
                               PostgreSQL  ◄── source de vérité unique
                                    ▲       (verbatims, analyses, thèmes,
                                    │        synthèses, progression)
ai-worker/    Python ───────────────┘
                 ▲
                 └── Redis : file des analyses à traiter (signal uniquement)
```

- Le **backend** possède l'API : auth, upload et parsing CSV, mapping des
  colonnes, lecture des analyses, liens de partage. Quand une analyse est
  demandée, il la crée en base et publie son id dans Redis.
- L'**ai-worker** consomme la file : il lit les verbatims en base, exécute le
  pipeline LLM (batching, thèmes émergents, synthèses avec verbatims exacts)
  et écrit résultats et progression en base au fil de l'eau.
- Le **frontend** interroge le backend, qui lit la progression et les
  résultats directement en base. Le worker n'expose aucune API.

## Choix et justifications

| Brique | Choix | Pourquoi |
|---|---|---|
| Frontend | Vue 3 + Vite + TypeScript (SPA) | Écosystème mûr, DX rapide pour un parcours unique |
| Backend | C# / ASP.NET Core | Langage expressif et typé ; API, auth et tâches de fond y sont des terrains bien balisés |
| Worker IA | Python + SDK Anthropic officiel | La langue naturelle d'un pipeline LLM ; découplé du backend, il peut évoluer (prompts, batching) sans toucher à l'API |
| Base | PostgreSQL | Source de vérité unique ; le schéma (migré, versionné) EST le contrat entre backend et worker |
| File d'attente | Redis | Transport minimal du signal « analyse à traiter » ; aucune donnée métier n'y transite |

### Décisions structurantes

- **Pas d'API interne entre backend et worker.** Le pipeline IA est asynchrone
  par nature (analyser des milliers de verbatims prend des minutes) ; le
  couplage se fait par la base et la file, pas par HTTP. Pas de contrat REST
  interne à versionner, pas d'auth service-à-service.
- **Postgres est la seule source de vérité.** Redis ne porte que des
  identifiants d'analyses ; si Redis tombe, on perd au pire un signal
  (re-publiable), jamais une donnée.
- **L'analyse est l'unité de travail asynchrone.** Elle porte elle-même son
  cycle de vie (`pending → running → succeeded / failed`), sa progression et
  son erreur éventuelle. Pas de table de « jobs » générique : un seul type de
  traitement existe ; on généralisera si un second apparaît un jour.
- **Redis plutôt qu'un broker (RabbitMQ).** La fiabilité ne repose pas sur
  la file : l'état des analyses vit en base, et la redelivery est assurée
  par claim atomique + heartbeat + reaper (voir Résilience). La file ne
  rend qu'un service — réveiller le worker sans polling — et une liste
  Redis y suffit. Les acks, la redelivery et le routage d'un broker
  feraient double emploi avec le mécanisme en base, pour un service
  stateful de plus à opérer. La décision se rouvrira si apparaissent
  plusieurs types de messages, du fan-out ou des retries différés.
- **La progression est une donnée comme une autre** : le worker met à jour
  l'analyse en base, le backend l'expose au front. Le mécanisme de push
  (polling ou SSE) est un détail d'implémentation du backend.
- **La fidélité des citations est garantie par construction.** Un verbatim
  représentatif est une **référence (clé étrangère) vers la ligne verbatim
  d'origine**, jamais du texte recopié : le LLM *sélectionne* des ids, il ne
  *produit* pas le texte cité. L'invariant « mot pour mot » devient ainsi
  vérifiable par un test, et une sortie LLM corrompue ne peut pas altérer
  une citation.
- **Le regroupement se fait par découverte par batchs + consolidation**
  (verdict du spike pipeline, consigné au [journal](../JOURNAL.md)) : chaque
  batch de verbatims produit ses thèmes candidats avec les ids qui les
  soutiennent, puis une passe de consolidation fusionne les doublons
  inter-batchs ; enfin chaque thème reçoit sa synthèse et ses ids
  représentatifs. L'alternative « taxonomie d'abord, classification
  ensuite » est écartée : en classification de masse, le modèle rend par
  batchs entiers des sorties vides mais conformes au schéma — un échec
  structurel et silencieux. Deux règles en découlent : **les sorties LLM
  structurées garantissent la forme, jamais l'exhaustivité** (chaque étape
  vérifie programmatiquement ce qu'elle reçoit — ids valides, couverture,
  noms connus — et rattrape ou compte ce qui manque) ; et **la langue des
  synthèses est détectée par le code et injectée au prompt**, jamais
  laissée à l'appréciation du modèle.
- **Zero-data-retention côté API Anthropic.** Les verbatims contiennent des
  données personnelles de tiers ; leur traitement par le LLM se fait sans
  rétention ni entraînement. C'est un choix d'architecture, pas un réglage.

### Résilience du traitement asynchrone

Le risque réel n'est pas la perte de Redis, c'est le **worker qui meurt après
avoir consommé le signal** (crash, OOM, redéploiement) — sans mécanisme, une
analyse resterait `running` pour toujours. Le modèle de reprise, piloté par
Postgres :

- **Claim atomique** : le worker prend une analyse via
  `UPDATE … WHERE status = 'pending' … RETURNING` — jamais deux workers sur
  la même analyse, même si un signal est publié deux fois. Le numéro de
  tentative retourné sert de **token de fencing** : toute écriture du
  worker est conditionnée par `attempts = <le sien>`, donc un worker
  « zombie » repris par le reaper abandonne bruyamment au lieu d'écraser
  le travail de son successeur.
- **Heartbeat** : le worker horodate régulièrement l'analyse qu'il traite.
- **Reaper** : un processus périodique (il tourne dans la boucle du worker)
  repasse en `pending` (et republie dans Redis) toute analyse `running` au
  heartbeat périmé — ou la passe en `failed` après N tentatives. Il republie
  aussi les analyses `pending` restées intouchées un timeout entier : c'est
  ce balayage qui rend vraie la promesse « si Redis tombe, on ne perd qu'un
  signal re-publiable » — un signal dupliqué est inoffensif, le claim
  atomique y veille.
- **Idempotence** : le pipeline purge les résultats partiels d'une analyse
  avant de la (re)traiter — rejouer ne duplique jamais.
- **Machine à états gardée en base** : deux processus écrivent l'analyse
  (le backend la crée, le worker la fait avancer) ; les transitions
  autorisées sont donc protégées au niveau du schéma (CHECK sur le statut,
  transitions par compare-and-set), pas seulement dans le code d'un seul
  des deux.

### Le schéma est un contrat — et se gère comme tel

- **EF Core (backend) est l'unique propriétaire des migrations.** Le worker
  lit et écrit un schéma qu'il ne migre jamais.
- **Expand/contract obligatoire** : tout changement de schéma se fait en
  deux temps (ajout compatible, puis retrait) — une migration ne casse
  jamais la version N-1 déployée d'une brique, condition de survie du
  déploiement continu.
- **Un test de contrat transverse** exerce le cycle complet : le backend
  crée analyse + verbatims, le worker les lit et écrit ses résultats, le
  backend les relit. C'est le test qui protège le contrat qu'aucun
  compilateur ne voit. Il vit dans le e2e : le parcours complet va jusqu'à
  relire par l'API les thèmes écrits par le vrai worker (LLM stub) et
  vérifier qu'une citation est une ligne du CSV de fixture, mot pour mot.
- Le schéma est documenté comme artefact ([schema.md](schema.md)) — il joue
  le rôle qu'aurait un OpenAPI : qui écrit quoi, et ce que chaque table
  garantit. Il se met à jour dans le même commit que toute migration.

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

## Hébergement

Un VPS unique chez **Scaleway** (`verbatim.yantech.fr`) : Docker Compose en
production, Caddy en frontal (TLS). **Tout est déclaratif, dans `infra/`** :
Terraform décrit l'infrastructure (instance, IP, security group, clé SSH —
state distant dans un bucket Object Storage), Ansible décrit la
configuration de la machine (durcissement, Docker, l'application). Les deux
s'exécutent en conteneurs via le Makefile ; aucune modification manuelle
sur le serveur. Les images de production sont publiées sur GHCR (gratuit
pour un repo public, authentification native des workflows).

Contraintes non négociables : Postgres et Redis ne sont **jamais** publiés
sur l'hôte (réseau Docker interne uniquement, Redis avec mot de passe) ;
secrets en variables d'environnement hors repo ; backups Postgres
automatiques, chiffrés, hors de la machine, avec restauration testée.

**Backups, le mécanisme** : un timer systemd (posé par Ansible) fait chaque
nuit `pg_dump` → chiffrement `age` → bucket Object Storage dédié, versionné,
rétention 30 jours. Asymétrie voulue : le serveur ne détient que la **clé
publique** — il chiffre des backups qu'il ne peut pas lire, et ses
credentials S3 (dédiés, moindre privilège) ne savent pas purger l'historique
versionné. La clé privée vit hors ligne, côté opérateur. La restauration
n'est pas un espoir mais une commande : `make backup-restore-test`
télécharge le dernier backup, le déchiffre localement et le restaure dans
un Postgres jetable en vérifiant le contenu — exercée en réel.

## E-mails transactionnels (magic links)

Le backend parle **SMTP** (MailKit) derrière une abstraction minimale
(`IEmailSender`) : le protocole est le contrat, pas un SDK de fournisseur.

- **Dev, tests et e2e : Mailpit** dans le compose — faux SMTP qui capture
  tout ; son UI et son API REST permettent aux humains comme aux tests de
  lire ce qui a réellement été envoyé. Les tests d'intégration vérifient la
  réception effective, jamais un mock.
- **Production : Scaleway TEM** (SMTP + STARTTLS) — mono-fournisseur avec
  l'hébergement, déclaré dans le Terraform existant, DNS (SPF/DKIM) posé
  par le même outillage que le reste.

Le même principe vaut pour l'API LLM : **dev et e2e tournent sur un stub
déterministe** (`PIPELINE_LLM=stub` dans le compose — le Mailpit du
pipeline) qui respecte le contrat ids-seulement ; aucun test n'appelle
l'API Anthropic. L'activation est explicite et opt-in : en production rien
n'est posé, une clé manquante échoue bruyamment — jamais de repli
silencieux vers des résultats factices.

## Ce qui n'est pas encore décidé

- Les choix fins par brique (librairies Python, framework de tests front) —
  décidés au moment où la brique démarre.
