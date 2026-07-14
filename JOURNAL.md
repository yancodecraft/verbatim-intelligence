# Journal de bord

Le fil chronologique du projet : ce qui a été fait, dans quel ordre, et
pourquoi. Les commits donnent le détail ; ici, c'est la narration et les
décisions. Entrées les plus récentes en haut.

---

## 2026-07-14 — Revue de la tranche 4 : le zombie et le token de fencing

La revue par agent sur l'ensemble de la tranche (7 commits) valide le
socle — fidélité, plafond de coût, scoping, atomicité claim/reaper :
« sound and adequately tested » — et rapporte deux findings, tous deux
traités :

1. **Important — l'écriture des résultats n'était pas fencée.** Scénario :
   un worker bloqué assez longtemps pour que son heartbeat périme (GC,
   throttling, réseau) se fait reprendre l'analyse par le reaper ; un
   second worker la claime et travaille ; le premier — un « zombie » —
   finit sa course et écrit ses thèmes par-dessus. Le garde
   `status = 'running'` ne suffit pas : le successeur est *aussi*
   `running`. Le bon discriminant est `attempts`, que chaque claim
   incrémente — un **token de fencing** classique. Désormais le claim
   retourne son numéro de tentative et **toutes** les écritures du worker
   (heartbeat, résultats, `failed`, `succeeded`) portent
   `AND attempts = %s` ; zéro ligne touchée = un successeur existe →
   `SupersededError`, le zombie abandonne sans rien écrire — pas même un
   échec, l'état du successeur est la vérité. Deux tests reproduisent le
   scénario (reclaim en plein appel LLM ; reclaim juste avant l'écriture
   des résultats).
2. **Hygiène — `beat()` était du code mort** : le heartbeat réel vit dans
   le pipeline (fusionné avec dépense et progression). Supprimé avec son
   test.

La revue a aussi failli signaler la syntaxe `except A, B:` comme une
erreur — avant de vérifier que PEP 758 (Python 3.14) la rend valide. Bon
réflexe des deux côtés : vérifier avant de rapporter.

## 2026-07-14 — Le pipeline analyse 747 verbatims réels en production

Dernière brique de la tranche 4. La clé API rejoint la production par le
chemin de tous les secrets (prod-secrets.yml → `PROD_SECRETS` → env
Ansible → compose) ; modèle et plafond restent sur les défauts du code.

**La CI a d'abord attrapé un vrai bug** que trois jours de stack dev
n'avaient jamais montré : sur une stack fraîche, le premier tour du reaper
part avant que le backend n'ait appliqué les migrations —
`UndefinedTable` est une `ProgrammingError`, hors du filet
(`OperationalError` seul) de la boucle de survie : le worker mourait et
plus rien ne consommait la file. Reproduit en local par `docker compose
down -v`, corrigé (le filet attrape `psycopg.Error`, chaque retry
reconnecte proprement), re-vérifié sur stack fraîche, et c'est le e2e de
CI — celui-là même qui échouait — qui a validé le correctif.

**Puis le parcours réel, en production** : magic link → upload de
`browser_interoperability_features.csv` (State of CSS 2021, 787 lignes)
→ 747 verbatims (40 cellules vides sautées) → `running`, progression
visible par pas de 100 → **`succeeded` : 12 thèmes**, du « Safari as the
primary compatibility blocker » (131 verbatims) au « CSS Grid and subgrid
support » (163), avec des synthèses fidèles au ton du corpus.

Les chiffres qui comptent :

- **58 citations vérifiées programmatiquement contre le fichier source :
  0 violation.** Chaque texte cité est la ligne d'origine, mot pour mot, à
  sa position — l'invariant n°1 tient en production sur un corpus réel.
- **747/747 traités** — aucune perte silencieuse.
- **0,48 $** (45 481 tokens in, 10 118 out, une tentative) — la moitié du
  plafond par défaut. La projection du spike était bonne.

Reste, avant de déclarer la tranche finie : la revue par agent (en cours)
et ses suites.

## 2026-07-14 — L'API restitue les analyses, et le contrat inter-briques est testé en vrai

Quatrième brique de la tranche 4, en deux volets.

**Le GET s'enrichit.** `GET /analyses/{id}` expose désormais tout ce que le
worker écrit : progression (`processedCount`), erreur lisible, et les
thèmes — nom, synthèse, poids (le compte de ses verbatims rattachés,
calculé, jamais stocké), et les **verbatims représentatifs résolus par
leur FK** : le texte renvoyé est la ligne d'origine, mot pour mot, avec sa
position dans le fichier source. La liste (`GET /analyses`) gagne
progression et erreur, pour que l'écran de la tranche 5 puisse suivre une
analyse en cours sans requête de plus.

**Le contrat transverse promis par architecture.md existe maintenant en
vrai.** Le e2e ne s'arrête plus à `succeeded` : il relit l'analyse par
l'API et vérifie que le cycle complet — le backend a écrit le corpus, le
vrai worker (LLM stub) a écrit thèmes et rattachements, l'API les relit —
produit une citation **égale à la ligne du CSV de fixture**, position
comprise. Deux tests backend l'exercent aussi de l'intérieur (thèmes écrits
au format worker, relus enrichis ; progression et erreur exposées).

## 2026-07-13 — Le pipeline du spike devient le pipeline du produit

Troisième brique de la tranche 4 : le worker analyse pour de vrai —
découverte par batchs de 100, consolidation, synthèses avec ids
représentatifs. Les prompts et les schémas sont **ceux du spike, repris
tels quels** : sept runs les ont éprouvés, on industrialise ce qui a été
validé, pas une variante.

Ce qui change entre un spike et un produit :

- **Le LLM est injectable** (un protocole `ask(prompt, schema)`) : les
  tests du pipeline tournent contre un LLM scripté qui rejoue des réponses
  canned — la CI ne dépend jamais de l'API Anthropic, ni d'une clé. Le test
  central vérifie la fidélité de bout en bout : ids hors du lot droppés
  (et comptés au log), ids représentatifs hors de leur thème droppés,
  et le mapping index → UUID de verbatim fait **en un seul endroit**, à
  l'écriture — le prompt ne voit jamais un id de base, la base ne reçoit
  que des FK.
- **Le plafond de coût est une garde, pas une intention** : les tokens
  cumulés en base (toutes tentatives confondues) sont convertis en dollars
  par la table de prix du code ; chaque appel vérifie avant de partir ;
  dépassé, l'analyse échoue avec un message qui dit le plafond et la
  dépense. Plafond par défaut 1 $ (`ANALYSIS_COST_CAP_USD`), modèle par
  défaut `claude-opus-4-8` — celui du verdict du spike
  (`ANTHROPIC_MODEL` pour changer). Un modèle sans prix connu refuse de
  tourner : pas de plafond, pas d'appel.
- **Heartbeat, progression et dépense avancent ensemble** : un seul UPDATE
  entre chaque appel LLM. Le timeout API est borné (90 s, 1 retry) pour
  rester loin sous les 5 minutes du reaper — un appel ne peut pas faire
  passer un worker vivant pour mort.
- **La langue est décidée par le code** (leçon du spike) : heuristique de
  stopwords sur un échantillon du corpus, six langues, défaut anglais en
  cas de doute — déterministe et testé, zéro dépendance.

Le e2e a immédiatement rappelé une évidence : la stack dev n'a pas de clé
API, et l'analyse du parcours complet échouait. La réponse suit la
jurisprudence Mailpit — **un `StubLlm` déterministe pour dev et e2e**
(`PIPELINE_LLM=stub` dans le compose), qui ne sélectionne que des ids
réellement montrés dans le prompt. Opt-in explicite : pas de repli
silencieux — en prod, une clé manquante doit échouer fort, pas produire
des thèmes factices.

30 tests worker au total. Reste à exposer les résultats côté API (brique
suivante), puis la clé en prod.

## 2026-07-13 — Le worker devient increvable : heartbeat, reaper, idempotence

Deuxième brique de la tranche 4, en TDD contre un vrai Postgres (17 tests
worker, 7 avant). Le scénario qui dicte tout : un worker meurt en plein
traitement — crash, OOM, redéploiement — et sans mécanisme, l'analyse reste
`running` pour toujours.

Ce qui change :

- **Le claim ouvre une tentative propre** : un seul UPDATE stampe le
  heartbeat, compte la tentative, efface l'erreur et la progression de la
  précédente. Puis la **purge** : les `themes` d'une tentative morte sont
  supprimés avant de retraiter (cascade sur les rattachements) — rejouer ne
  duplique jamais.
- **`beat()` entre les étapes du pipeline**, pas de thread : une étape dure
  au pire un appel LLM, très en dessous du timeout du reaper (5 min). Plus
  simple, et une connexion unique suffit.
- **Le reaper tourne dans la boucle du worker** (toutes les 60 s, pas de
  processus de plus à opérer) : les `running` au heartbeat périmé repartent
  en file (`attempts` < 3) ou passent en `failed` avec une erreur lisible ;
  et les `pending` intouchés depuis un timeout entier sont **republiés** —
  le cas Redis-a-perdu-le-signal, que la promesse d'architecture couvrait
  sans qu'aucun code ne la tienne. Un signal dupliqué est inoffensif : le
  claim atomique absorbe tout.
- **`heartbeat_at` double comme « dernier toucher »** : le reaper l'estampe
  quand il remet en file, ce qui borne la republication à une par timeout —
  pas de spam de file quand un backlog attend légitimement.
- **Un échec de pipeline ne tue jamais la boucle** : rollback des écritures
  partielles, l'analyse passe en `failed` avec son erreur (montrée à
  l'utilisateur), le worker continue.

## 2026-07-13 — Tranche 4 ouverte : le schéma des thèmes, et docs/schema.md

Le pipeline d'analyse commence par son contrat : les tables que le worker
écrira et que le backend lira. Plan court validé par Yannick (cinq briques :
schéma → résilience worker → pipeline LLM → contrat transverse → prod) ;
cette entrée clôt la première.

Deux tables nouvelles, dessinées pour porter l'invariant n°1 :

- **`themes`** — nom, synthèse, `position` (l'ordre d'affichage est décidé
  par le worker, pas recalculé côté lecture : déterministe, même en cas
  d'égalité de poids).
- **`theme_verbatims`** — le rattachement verbatim ↔ thème, PK composite.
  Un seul champ de plus, `rank` : `NULL` = le verbatim soutient le thème,
  `0..n` = il est **représentatif**, cité dans cet ordre par la synthèse.
  Une citation est cette FK — aucun texte cité n'existe ailleurs que dans
  `verbatims.text`. Le poids d'un thème n'est pas stocké : c'est le compte
  de ses rattachés.

Sur `analyses`, les colonnes de résilience et de pilotage arrivent en
expand (defaults en base, un backend N-1 reste valide) : `heartbeat_at`,
`attempts`, `error`, `processed_count`, et `input_tokens`/`output_tokens` —
on stocke des **tokens**, pas un montant : les prix changent, la dépense
mesurée reste vraie ; le plafond de coût se calcule dans le worker.

Les tests de schéma vérifient ce que la **base** garantit, pas ce que EF
veut bien laisser passer : le test anti-doublon insère en SQL brut, comme le
worker le fera — la première version passait par le change tracker EF, qui
levait l'erreur avant même que Postgres ne voie la ligne.

Enfin, [docs/schema.md](docs/schema.md) naît : promis par architecture.md
« dès que le schéma existera », c'est maintenant qu'il gagne sa place — le
worker et le backend écrivent désormais les mêmes tables sans se voir, la
carte de qui-écrit-quoi devient le document de travail des briques
suivantes. Règle attachée : il se met à jour dans le même commit que toute
migration.

## 2026-07-13 — La tranche 3 est close : l'ingestion CSV vit en production

**Parcours exercé en réel sur https://verbatim.yantech.fr** : connexion par
magic link (vrai e-mail) → upload d'un CSV de 15 lignes → 3 colonnes
détectées à l'aperçu → colonne `comment` désignée → analyse créée avec
**14 verbatims** (la cellule vide sautée, comme tranché en revue) →
`succeeded` → la liste du compte montre ses analyses. **Les quatre critères
de « fini » sont remplis : tests en CI (52 backend, 15 front, 7 worker,
e2e), revue par agent traitée, parcours réel, en production.**

La journée aura clos les tranches 2 et 3, posé les backups et leur
restauration prouvée — la tranche 3 ayant été construite par une **session
autonome** dont le travail a passé revue produit, revue par agent et CI
sans retouche de fond. Prochaine étape : la **tranche 4, le pipeline
d'analyse** — la stratégie est déjà tranchée par le spike (découverte par
batchs + consolidation), ses enseignements consignés ici même.

## 2026-07-13 — Revue de la tranche 3 : « sound to push », deux suites données

**La session autonome passe la revue par agent** : aucun trou de sécurité,
pas d'IDOR, pas de XSS, pas d'explosion mémoire sur entrée hostile — la
surface d'upload (bornée par Kestrel avant tout traitement, décodage UTF-8
strict, sortie de boucle dès la 5 001ᵉ ligne), le scoping par filtres
globaux et la migration expand/contract sont vérifiés tenir. La posture
antiforgery (SameSite=Lax, même origine via proxy) est jugée légitime.

Deux suites :
- **Un test de plus, préventif** : rien ne pinnait « le texte exact de la
  cellule, aucun trim » — l'invariant n°1 du projet — contre un futur bump
  de CsvHelper qui changerait le défaut. Une cellule `"  padded  "` fait
  maintenant le tour du parseur dans un test.
- **`verbatimCount` tranché** : il compte les verbatims réellement
  insérés (les cellules vides sont sautées) — comportement constaté et
  accepté à la revue produit ; l'aperçu d'upload montre déjà le nombre de
  lignes total.

La revue produit (parcours réel en dev, deux CSV dont un corpus réel) est
passée aussi. Reste, pour les quatre critères : pousser, déployer, exercer
en production.

## 2026-07-13 — Session autonome — tranche 3 : l'ingestion CSV de bout en bout

**Fait, en TDD sur le backend, brique par brique verte :** un CSV s'uploade,
ses colonnes se mappent, ses verbatims se rangent en base, et une analyse
part dans la file — le tout scopé au compte.

- **`POST /uploads`** applique le contrat CSV de la [spec](docs/v1-spec.md) : UTF-8
  (BOM toléré), délimiteur auto-détecté, limites 5 Mo / 5 000 lignes / 10 000
  caractères par cellule, en-têtes requis. Chaque rejet est un **400 au
  message clair** (fichier vide, pas de l'UTF-8, trop gros, trop de lignes,
  cellule trop longue, en-têtes manquants) — un test par règle, au niveau du
  parseur (pur, sans infra) puisque c'est là que vit le risque.
- **`POST /analyses`** est refondu : body `{uploadId, verbatimColumn}`, 404 si
  l'upload n'appartient pas au compte (filtre de scoping) ou n'existe pas,
  400 si la colonne n'existe pas. Il extrait la colonne en **verbatims** et
  enqueue après commit (pattern existant).
- **`GET /analyses`** liste les analyses du compte, plus récentes d'abord.
- **Front** : la home devient la liste (état vide, badges de statut, polling
  qui s'arrête au statut terminal) ; `/analyses/new` porte le parcours
  upload → aperçu → choix de colonne → run → retour liste. Les 400 backend
  s'affichent tels quels. `CreateAnalysis.vue` (le bouton nu de la tranche 1)
  disparaît.
- **e2e** : sign-in par magic link → upload d'une fixture (colonne verbatim
  avec une cellule vide et un `;` dans le texte) → aperçu → colonne → run →
  la liste atteint `succeeded`. Vérifié en base réelle : la ligne vide est
  sautée (trou de position préservé), le `;` reste dans le texte,
  `verbatim_count` = 5.

**Décisions et arbitrages :**
- **Bibliothèque CSV : CsvHelper 33.1.0** — le standard de facto .NET, quoting
  et échappement conformes RFC 4180 ; la robustesse prime, l'entrée n'est pas
  fiable. Détection du délimiteur : `,` vs `;` comptés sur la **ligne
  d'en-tête**, égalité (ou virgule seule) → `,`.
- **Colonnes dénormalisées avec défaut en base** (`source_filename`,
  `verbatim_count` sur `analyses`, NOT NULL DEFAULT) : étape *expand*
  d'expand/contract. Le backend N-1 et l'INSERT minimal du worker restent
  valides ; le miroir DDL du worker reprend les défauts et **omet** uploads /
  verbatims (le worker n'y touche pas). Alternative « colonnes nullables »
  écartée : le défaut porte mieux l'invariant (toute analyse a un fichier
  source et un compte de verbatims).
- **Fidélité par construction** : un verbatim stocke le **texte exact** de la
  cellule (aucun trim) ; la position est l'index 0-based de la ligne dans le
  fichier, donc les cellules vides sautées laissent un trou — chaque verbatim
  pointe toujours vers sa ligne d'origine, ce dont la tranche 4 aura besoin
  pour citer par référence.
- **Contenu non fiable traité comme tel dès l'ingestion** : décodage UTF-8
  strict pour distinguer un binaire d'un CSV, échappement par défaut de Vue
  (jamais de `v-html`), nom de fichier réduit à sa feuille et borné, limite de
  corps Kestrel à 6 Mo. L'antiforgery est désactivé sur l'endpoint multipart
  et c'est assumé : le cookie de session est `SameSite=Lax`, non envoyé sur un
  POST cross-site — même posture CSRF que les autres écritures.

**Point d'attention pour la revue :** le contrat figé dit des cellules vides
« ignorées (pas insérées) **mais comptées dans la réponse** ». Je l'ai lu
comme *`verbatim_count` = nombre de verbatims réellement insérés* (non
vides) : un verbatim a un `text` NOT NULL, et le champ s'appelle
verbatimCount. Si l'intention était le total des lignes de la colonne (vides
comprises), c'est une inversion d'une ligne — à trancher en revue.

**Pièges rencontrés :** le spread `..` exige une expression de collection
`[...]`, pas `new byte[]{}` ; `dotnet format` ne veut aucune newline finale ;
un helper de test sans état doit être `static` (CA1822/S2325) ; Biome
reformate les lignes longues (lancé en `--write` à chaque fois).

**Reste pour clore la tranche** (les 4 critères de « fini ») : la revue par
agent, le parcours exercé en **production**, et le déploiement — ce sera
l'arbitrage de Yannick. `make ci` local (sans deploy) est vert.

## 2026-07-13 — Tranche 3 ouverte : le schéma d'ingestion (uploads, verbatims)

**Fait, en TDD :** le glossaire gagne le terme **Upload** (un CSV soumis et
stocké tel quel, source des verbatims), puis deux tables arrivent —
`uploads` (contenu `bytea`, colonnes détectées en `jsonb`, `row_count`,
scopé au compte par le même *global query filter* que `analyses`) et
`verbatims` (part de l'agrégat Analysis : `position` 0-based dans le fichier,
`text` NOT NULL, cascade sur l'analyse). Les constraintes sont testées comme
le contrat qu'elles sont (FK, cascade, round-trip jsonb) — pattern
`AuthModelTests`.

**Décision — dénormalisation compatible :** `source_filename` et
`verbatim_count` sont ajoutés sur `analyses` en NOT NULL **avec défaut en
base** (`''` et `0`). C'est l'étape *expand* d'expand/contract : les lignes
existantes se remplissent seules, et le backend N-1 comme le worker (qui
n'écrivent pas encore ces colonnes) continuent d'insérer. Le miroir DDL du
worker reprend ces défauts ; les tables d'ingestion y sont volontairement
absentes puisque le worker ne les touche pas.

## 2026-07-13 — Les backups Postgres : chiffrés, hors machine, restaurés en vrai

**Le prérequis de la tranche 3 est rempli.** Chaque nuit, un timer systemd
fait `pg_dump` → `age` → bucket Object Storage versionné (rétention
30 jours). Le design tient en une asymétrie : le serveur ne détient que la
**clé publique** de chiffrement — il produit des backups qu'il ne peut pas
lire — et ses credentials S3 dédiés ne savent pas réécrire l'historique
(bucket versionné). La clé privée `age` vit hors ligne côté opérateur.

**La restauration est exercée, pas espérée** : `make backup-restore-test`
télécharge le dernier backup réel, le déchiffre localement, le restaure
dans un Postgres jetable et vérifie le contenu — passé en réel sur le
backup du jour (1 compte, 2 analyses, 2 sessions restaurés). Le playbook
lance d'ailleurs un backup immédiat à chaque changement du pipeline et
échoue si l'objet du jour manque au bucket.

**Pièges du jour** : `source` sans `set -a` ne propage rien à
`docker run -e` (« Unable to locate credentials ») ; et sur Scaleway, une
clé API d'application doit porter `default_project_id` pour qu'Object
Storage accepte ses écritures — sans lui, `AccessDenied` sur le bucket du
projet.

## 2026-07-13 — Revue de la tranche 2 par agent : trois findings, tous traités

**Verdict initial : « pas de clôture en l'état »** — la revue a confronté le
code aux promesses écrites du projet, exactement son rôle :
- **Le scoping n'était pas « mécanique »** : practices.md promet un global
  query filter EF Core, le code n'avait qu'un `WHERE` manuel dans l'unique
  endpoint scopé — le prochain endpoint pouvait l'oublier (IDOR silencieux).
  → Traité : `CurrentAccountAccessor` (scoped) armé par `RequireAccount()`,
  **`HasQueryFilter` sur `analyses`** — une entité scopée est invisible hors
  de son compte avant même que la requête regarde. Le `WHERE` manuel a
  disparu et le test « B reçoit 404 sur la ressource de A » passe par le
  filtre seul.
- **Pas de rate limiting sur les magic links**, invariant pourtant écrit.
  → Traité : limites tenues **en base** (`login_tokens` est le registre —
  elles survivent aux redémarrages et réplicas, sans dépendre d'une IP
  derrière le proxy) : 5 liens / 15 min par adresse (anti mail-bombing),
  30 / 5 min global (protège le quota TEM). Testé (le 6ᵉ → 429).
- **Un body JSON vide faisait un 500 brut** sur les deux endpoints anonymes
  (System.Text.Json passe `null` aux paramètres manquants sans erreur).
  → Traité : champs optionnels validés explicitement — 400 sur l'e-mail
  manquant, 401 sur le token manquant. Testés.

Notés sans blocage : UUIDv7 est ordonné dans le temps (la roadmap dit
« non séquentiels » — acceptable car jamais énumérable hors compte, à
retrancher si un usage public apparaît) ; une course théorique sur la
création simultanée d'un même compte (500 occasionnel, sans corruption).

## 2026-07-13 — Le parcours auth exercé en production, par son premier utilisateur réel

**Vérifié en réel sur https://verbatim.yantech.fr** : demande de magic link
→ e-mail reçu dans une vraie boîte Gmail (ni spam ni promotion — SPF/DKIM
font leur travail) → connexion → session → **analyse créée et `succeeded`**
à travers toute la pile → rejeu du token refusé (401, usage unique) →
logout → session révoquée côté serveur (401). Détail savoureux : pendant la
vérification scriptée, Yannick a cliqué ses propres magic links dans son
navigateur — les 401 observés sur les premiers tokens étaient *sa*
consommation, le single-use en action. La première connexion utilisateur
réelle du produit a donc précédé la vérification officielle.

**Dernier piège levé** : les instances Scaleway droppent le SMTP sortant
sur les ports standard (anti-spam), même avec une politique de sortie
`accept` — c'est le rôle des règles de sécurité par défaut. TEM expose le
port **2587** exactement pour ça : le backend l'utilise, et le security
group garde ses protections. (Diagnostic : `magic-link` qui bloque
> 2 min, confirmé par un test de connectivité TCP depuis le serveur —
587 bloqué, 2587 ouvert.)

## 2026-07-13 — Les magic links partent en prod : Scaleway TEM, déclaré

**Fait :** la chaîne d'envoi de production est déclarée dans le Terraform
existant : domaine TEM `verbatim.yantech.fr` (offre `essential`, 300
e-mails/mois inclus — large pour des magic links), enregistrements
SPF/DKIM/MX/DMARC posés chez Hostinger (le provider Terraform
`hostinger/hostinger` gère la zone), **validation du domaine par Scaleway
réussie en ~1 min**, et des credentials d'envoi dédiés au moindre
privilège (application IAM + `TransactionalEmailEmailFullAccess` seulement
— pas d'administration de domaine). Le mot de passe SMTP est le secret de
cette clé : lu des outputs Terraform, posé dans les secrets de prod hors
repo, jamais affiché ni commité. Le compose de prod passe la config SMTP
au backend (STARTTLS sur smtp.tem.scaleway.com:587).

**Pièges rencontrés, tous consignés dans le code :**
- TEM exige une **souscription d'offre préalable** (« No active offer
  subscription ») — pas de ressource Terraform pour ça : one-shot
  documenté via le CLI scaleway en conteneur, comme `infra-bootstrap`.
- La politique de l'organisation **impose une date d'expiration aux clés
  API** : `expires_at` posé à un an, procédure de rotation commentée dans
  `tem.tf`.
- Le provider Hostinger (0.1.22) **perd les enregistrements TXT longs** :
  l'API les renvoie découpés en chunks quotés, la comparaison échoue, et
  le provider conclut que le record n'existe pas. DKIM et DMARC sont donc
  posés mais volontairement non déclarés (commentaire dans `tem.tf` avec
  la procédure de re-création) ; SPF et MX restent déclaratifs, et la
  ressource de validation TEM vérifie de fait la présence de l'ensemble.
- Leçon .NET au passage : `ValidateOnStart()` **sans validateur enregistré
  ne matérialise pas les options** — le déploiement intermédiaire sans
  config SMTP a booté sain au lieu d'échouer vite. Le fail-fast viendra
  d'un validateur réel quand le besoin le justifiera.

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
