# Journal de bord

Le fil chronologique du projet : ce qui a été fait, dans quel ordre, et
pourquoi. Les commits donnent le détail ; ici, c'est la narration et les
décisions. Entrées les plus récentes en haut.

---

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
