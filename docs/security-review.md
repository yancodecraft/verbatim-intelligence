# Security review d'ouverture — V1

État au **2026-07-14**. Cette review couvre les dix points de la checklist
minimale d'ouverture ([practices.md](practices.md)) : autorisation/IDOR,
tokens de partage, XSS, headers HTTP/CSP/HSTS, exposition réseau des datastores,
backups, rotation de la clé LLM, rate limiting, RGPD, robustesse du parsing CSV.

Elle a été menée par cinq audits de code en lecture seule (backend C#, frontend
Vue, worker Python, infra Ansible/Terraform, config Caddy) doublés de
vérifications sur la prod en ligne (headers, comportement des endpoints publics,
ports exposés). Aucune modification n'a été faite.

## Verdict

Le socle applicatif est **solide** : aucun IDOR, tokens de partage
irréprochables, XSS fermé par construction et testé, parsing CSV borné,
datastores injoignables de l'extérieur, backups chiffrés et restauration
exercée, historique git propre. Les faiblesses ne sont pas dans le code livré
mais dans **ce qui manque encore avant d'ouvrir au public** : la conformité
RGPD (le vrai chantier, et un prérequis que le projet s'est lui-même fixé), le
durcissement qui ne devient actif qu'au retrait du basic-auth (headers, rate
limiting réel par client), et quelques procédures d'exploitation.

**L'ouverture reste conditionnée** à la levée des bloquants ci-dessous. En
l'état, garder le basic-auth d'edge.

## Bloquants d'ouverture (RGPD — prérequis auto-imposé)

- **B1 — Aucun droit à l'effacement.** Le seul `DELETE` du backend est la
  révocation d'un lien de partage. Impossible de supprimer une analyse, un
  corpus ou un compte sans SQL manuel en prod — ce qui contredit « aucune
  commande manuelle sur le serveur ». Le schéma est pourtant prêt : toutes les
  FK sont `ON DELETE CASCADE`. Manque l'endpoint (ou le runbook opérateur) qui
  l'expose. `v1-spec.md` promet cette cascade ; elle n'est pas câblée.
- **B2 — CSV bruts conservés à vie, orphelins.** `uploads.content` stocke le
  fichier entier en `bytea`, **toutes colonnes comprises** (un export NPS porte
  souvent email/nom du répondant dans les colonnes non-verbatim). Aucun TTL,
  aucune purge, et **aucune FK entre `analyses` et `uploads`** : une fois
  l'analyse créée, la ligne d'upload devient invisible et immortelle. Même une
  future suppression d'analyse ne la toucherait pas.
- **B3 — Aucune information ni mention légale côté produit.** Pas de politique
  de confidentialité, pas de mention « contenu traité par un service tiers » à
  l'upload, pas de route dédiée. `v1-spec.md` en fait une exigence V1. Le **DPA
  Anthropic et le registre de sous-traitance** — prérequis d'ouverture explicite
  de practices.md — ne sont pas faits.
- **B4 — Le zero-data-retention Anthropic est affirmé, pas matérialisé.** Le
  texte intégral des verbatims part en clair dans les prompts. Les docs
  présentent le ZDR comme un « choix d'architecture », mais c'est un avenant
  contractuel au niveau de l'organisation API : rien dans le code ni la config
  ne le prouve, aucune trace qu'il soit en place. À obtenir/vérifier avec le DPA.

## À corriger avant de retirer le basic-auth

- **O1 — DoS trivial des rapports partagés (déjà actif aujourd'hui).** Le rate
  limit de `/api/shared/*` est une fenêtre fixe **globale** de 60 req/min, pas
  par IP. N'importe quel anonyme sature la fenêtre (~1 req/s) et met tous les
  liens partagés en 429 pour tout le monde. Cause racine : `UseForwardedHeaders`
  est absent, donc le backend ne voit que l'IP de Caddy. C'est le seul endpoint
  public déjà exposé — donc le seul finding « actif maintenant ».
- **O2 — DoS du login + comptes fantômes (actif au retrait).** Le magic link a
  un bon plafond par adresse (5/15 min) mais un cap **global** de 30/5 min : un
  script avec des adresses aléatoires bloque la connexion de tous les
  utilisateurs légitimes et brûle le quota d'envoi d'emails. Même cause racine
  que O1 (pas de dimension IP).
- **O3 — Headers de sécurité absents sur l'app (vérifié en prod).** CSP,
  `X-Content-Type-Options: nosniff`, `frame-ancestors`, `Referrer-Policy`,
  `Permissions-Policy` ne sont posés que sur `/shared/*`. Les routes
  authentifiées n'en ont aucun. **Aucun HSTS nulle part** (confirmé sur la prod :
  ni `Strict-Transport-Security`, ni `nosniff`). Masqué par le basic-auth
  aujourd'hui, actif dès son retrait. Correctif = un bloc `header` global dans
  le Caddyfile, à poser dans le même commit que le retrait du basic-auth.
- **O4 — Pas de rate limit sur `/uploads` ni sur le lancement d'analyse.** La
  doc l'affirme pourtant. À l'ouverture, un compte gratuit (obtenu par magic
  link) peut boucler des uploads de 5 MB (remplissage Postgres, aggravé par
  l'absence de purge B2) et enchaîner des analyses à ~1 $ de LLM chacune.
- **O5 — L'utilisateur applicatif Postgres est superuser.** Backend, migration
  de schéma et worker se connectent tous avec le compte `verbatim` = superuser
  du cluster. Une injection SQL ou une compromission d'un service donne
  immédiatement les pleins pouvoirs (`COPY … TO PROGRAM`, lecture/altération de
  toutes les tables dont les tokens). Defense-in-depth : créer un rôle applicatif
  sans `SUPERUSER`/`CREATEDB`.

## Defense-in-depth & procédures (peut suivre l'ouverture)

- **D1 — Race sur l'usage unique du magic link.** Deux `POST /auth/verify`
  concurrents avec le même token créent chacun une session (pas de garde
  atomique). Exploitation exigeante (il faut déjà le lien), mais la garantie
  « works once » n'est pas tenue. Fix : `UPDATE … WHERE used_at IS NULL` + check
  du rowcount.
- **D2 — Backups : clé `age` sans copie de secours.** La clé privée vit
  uniquement sur le poste opérateur. Perte du laptop **et** du VPS (le sinistre
  contre lequel les backups existent) = backups définitivement illisibles. Une
  seconde copie hors-ligne (coffre/gestionnaire de mots de passe) est
  indispensable.
- **D3 — Backups : aucune alerte sur échec.** Service oneshot sans `OnFailure=`.
  Si `pg_dump` casse ou la clé S3 expire (échéance **2027-07-01**), échec
  silencieux ; au bout des 30 jours de rétention, plus aucun backup valide, sans
  signal. Poser une alerte.
- **D4 — Rotations non écrites/piégeuses.** Clé Anthropic : procédure promise
  mais non documentée, et la clé vit en **deux copies** (secret local +
  `PROD_SECRETS` GitHub) qui peuvent diverger — un redéploiement CI réinjecterait
  une clé révoquée. Mot de passe Postgres : le changer par le chemin nominal ne
  met pas à jour la base (n'agit qu'à l'initdb) → connexions cassées. À
  documenter (et idéalement scripter).
- **D5 — Hygiène des données.** Comptes jamais vérifiés et tokens
  expirés/consommés ne sont jamais purgés (accumulation illimitée, faible
  sensibilité car hashés). `/api/health` déclenche un check DB sans auth ni
  limite (marteau anonyme possible après l'ouverture). Encoder/valider
  `route.params.token` avant le fetch côté front (aiguillage d'endpoint via URL
  forgée). Commenter dans le Caddyfile l'interdiction d'access-logs non masqués
  sur `/shared/*` et `/verify` (le token est dans l'URL).
- **D6 — Tests d'autorisation manquants.** Session expirée rejetée, double-use
  concurrent de `/auth/verify`, cap global du magic link, assertions
  `SameSite`/`Secure` sur le cookie, test d'architecture « tout endpoint non
  public est derrière `RequireAccount` ».

## Vérifié solide (avec preuve)

- **Autorisation / IDOR** : double verrou mécanique — `RequireAccount()` +
  global query filters EF (`account_id`) sur `Analysis` et `Upload`. Aucun
  lookup par id brut, cross-compte = 404 nu indistinguable d'un id inconnu (pas
  d'oracle d'existence), testé sur les cinq chemins par id. Aucun IDOR.
- **Tokens de partage** : 256 bits CSPRNG base64url, **seul le SHA-256 stocké**,
  jamais loggé (aucun `ILogger` dans le backend), lookup par index unique,
  révocation par suppression, un seul lien actif garanti par index unique +
  `FOR UPDATE`, 404 nu identique pour inconnu/révoqué/remplacé, `no-store`,
  `noindex` + `no-referrer` + CSP à l'edge. Chaque propriété est couverte par
  un test.
- **XSS** : aucun `v-html`/`innerHTML` dans tout le front (test sentinelle qui
  monte un `<img onerror>` et vérifie l'échappement) ; tout contenu attaquant
  (verbatims, thèmes LLM, filename, preview CSV) rendu par interpolation ; CSP
  publique stricte (`default-src 'self'`, `frame-ancestors 'none'`,
  `base-uri 'none'`) — vérifiée en prod ; filename sanitizé à l'upload.
- **Exposition réseau** : Postgres et Redis sans aucun port publié (confirmé en
  prod : 5432/6379 filtrés, seuls 22/80/443 ouverts). Double firewall (security
  group Scaleway `drop` par défaut + ufw). Redis mot de passe, Postgres
  scram-sha-256, SSH sans mot de passe + fail2ban. HTTP→HTTPS forcé.
- **Backups** : `pg_dump | age | s3 cp` en flux direct (jamais de fichier
  local), chiffrement asymétrique (seule la clé **publique** sur le serveur),
  bucket versionné, rétention 30 j, restauration **scriptée et exercée
  réellement** (`make backup-restore-test`, journal du 2026-07-13).
- **Parsing CSV** : triple borne de taille (Kestrel 6 MB / check 5 MB / re-check),
  max 5 000 lignes, cellules ≤ 10 000 caractères, décodeur UTF-8 **strict**
  (binaire/latin-1/UTF-16 rejetés), jamais d'exception sur CSV malformé, messages
  d'erreur fixes sans stack trace. Bien testé.
- **Secrets** : injection propre (Ansible → `.env` 0600 → env du seul conteneur
  concerné), **aucun secret en clair dans les 95 commits de l'historique**
  (scan `git log -G`), Trivy en CI, stub déterministe en dev sans clé. Les
  sessions/magic-links/share-tokens n'utilisent **aucun secret statique** (tokens
  CSPRNG hashés) — rien à faire tourner.

## Séquencement suggéré

1. **RGPD** (B1→B4) : endpoint de suppression analyse + purge des uploads ;
   mention à l'upload + page privacy ; DPA/ZDR Anthropic vérifié + registre de
   sous-traitance ; politique de rétention écrite (incluant la borne backups 30 j).
2. **Retrait du basic-auth = un seul commit** qui pose simultanément les headers
   globaux (O3), le rate limiting réel par IP via `UseForwardedHeaders` (O1+O2)
   et le rate limit upload/analyse (O4).
3. **O5** (rôle Postgres non-superuser) avant toute ouverture réelle.
4. **Procédures** (D2→D4) : escrow clé age, alerte backup, rotations écrites —
   à traiter tôt car ce sont des risques de perte de données/service, pas de code.
5. **Defense-in-depth** (D1, D5, D6) : opportuniste, après l'ouverture.

## Suivi des corrections

- **2026-07-14 — durcissement pré-ouverture (derrière le basic-auth).**
  - **O1 corrigé** : le rate limit `/api/shared/*` est désormais partitionné par
    client. `UseForwardedHeaders` (confiance au réseau Docker privé, `ForwardLimit`
    1) résout l'IP réelle de l'appelant ; un seul client ne peut plus saturer la
    fenêtre pour tout le monde.
  - **O3 corrigé** : bloc `header` global dans Caddy sur toutes les routes — CSP,
    HSTS (`max-age` 1 an, `includeSubDomains`), `X-Content-Type-Options: nosniff`,
    `Referrer-Policy: no-referrer`, `Permissions-Policy`, `-Server`. Les pages
    `/shared/*` gardent en plus `X-Robots-Tag: noindex`.
  - **O4 corrigé** : fenêtres par client aussi sur `/uploads` (20/min) et la
    création d'analyse (10/min), limites configurables.
  - Restent ouverts : **O2** (le socle par-IP est là, mais le limiter du magic
    link — ledger en base — n'a pas encore été repartitionné), **O5** (rôle
    Postgres non-superuser), et tous les bloquants RGPD.
  - Le retrait effectif du basic-auth n'est pas fait : le push sur `main`
    auto-déploie, l'ouverture reste conditionnée aux bloquants RGPD.
- **2026-07-14 — effacement RGPD (backend).**
  - **B2 corrigé** : le CSV brut (`uploads.content`) est purgé à la création
    d'analyse, dès l'extraction des verbatims. Un upload devient à usage unique
    (décision produit : ni re-run ni multi-analyse exposés en V1). Plus de CSV
    orphelin conservé à vie.
  - **B1 corrigé côté backend** : `DELETE /analyses/{id}` (scopé compte, cascade
    DB vers verbatims/thèmes/share) et `DELETE /auth/account` (cascade vers
    analyses, uploads, tokens, sessions ; cookie de session effacé). Testés.
  - Restent : la page privacy complète, le **DPA/ZDR Anthropic** et le registre
    de sous-traitance (démarches non-code).
- **2026-07-14 — effacement RGPD (frontend) et mention à l'upload.**
  - **B1 exposé dans l'UI** : bouton « Delete analysis » (confirmation inline)
    sur le détail d'analyse, et lien « Delete my account » sur l'accueil. Le
    parcours e2e supprime désormais l'analyse et vérifie sa disparition.
  - **B3 partiellement traité** : mention factuelle sous le formulaire d'upload
    (contenu traité par un service tiers, Anthropic ; fichier brut purgé après
    analyse). La **politique de confidentialité complète** et le **DPA** restent
    à faire (démarches non-code).
