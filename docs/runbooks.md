# Runbooks d'exploitation

Procédures opérationnelles à connaître **avant** d'en avoir besoin
(security review, findings D2/D4). Elles n'ont pas de secret en clair — juste
les gestes.

## Où vivent les secrets

Chaque secret existe en **deux endroits** qu'il faut garder synchronisés :
- `~/.config/verbatim-intelligence/prod-secrets.yml` (poste opérateur, monté par
  `make deploy`) — la source pour un déploiement manuel ;
- le secret GitHub Actions **`PROD_SECRETS`** — la source pour la CD sur `main`.

Ansible les rend sur le serveur dans `/opt/verbatim/.env` (mode 0600). Piège :
un déploiement CI ultérieur **réinjecte `PROD_SECRETS`**. Donc toute rotation
doit mettre à jour **les deux copies**, sinon le prochain merge sur `main`
restaure l'ancienne valeur.

## Rotation de la clé API Anthropic

1. Console Anthropic → créer une nouvelle clé, révoquer l'ancienne.
2. Mettre à jour la clé dans `prod-secrets.yml` **et** dans `PROD_SECRETS` (GitHub).
3. Redéployer (`make deploy` ou un push sur `main`). Le conteneur `ai-worker`
   est recréé au deploy et recharge la clé depuis l'environnement.
4. Vérifier qu'une nouvelle analyse aboutit.

## Rotation des mots de passe Postgres

Il y a **deux** mots de passe depuis O5 : le superuser `verbatim`
(`postgres_password`, utilisé par `migrate`) et le rôle applicatif
`verbatim_app` (`app_db_password`, utilisé par backend et worker à l'exécution).

**Rôle applicatif `verbatim_app` (le cas courant) — trivial :** changer
`app_db_password` dans les **deux copies** (`prod-secrets.yml`, `PROD_SECRETS`)
et redéployer. Le one-shot `db-init` fait un `ALTER ROLE … PASSWORD` à chaque
déploiement, donc la base et les chaînes de connexion restent cohérentes — pas
de piège d'init.

**Superuser `verbatim` — piégeux :** ⚠️ le chemin nominal ne suffit pas.
`POSTGRES_PASSWORD` n'agit qu'à l'initialisation du volume. Changer le secret
puis déployer met à jour la chaîne de `migrate` mais **pas** le mot de passe
réel en base → `migrate` perd l'accès. Procédure correcte :
1. `ALTER USER verbatim WITH PASSWORD '<nouveau>';` dans la base (via un shell
   psql sur le conteneur postgres).
2. Mettre à jour `postgres_password` dans les deux copies.
3. Redéployer pour propager la chaîne de connexion.

## Rotation Redis / basic-auth d'edge / clé SMTP

- **Redis** et **basic-auth** : mêmes deux copies + redeploy. Pour le
  basic-auth, régénérer le hash bcrypt et le mot de passe clair (le smoke test
  de deploy s'en sert).
- **SMTP (TEM Scaleway)** : la clé IAM est gérée par Terraform ; procédure
  documentée en commentaire dans `infra/terraform/tem.tf`. Expire 2027-07-01.

## Escrow de la clé de backup `age` (D2)

Les backups Postgres sont chiffrés avec une clé `age` **asymétrique** : le
serveur ne détient que la clé **publique**. La clé **privée** (qui déchiffre)
vit uniquement sur le poste opérateur (`~/.config/verbatim-intelligence/backup-age.key`).

⚠️ **Point unique de défaillance.** Perdre ce poste **et** le VPS (le sinistre
exact contre lequel les backups existent) rend tous les backups illisibles à
jamais.

**Action requise (manuelle) :** garder une **seconde copie hors ligne** de la
clé privée `age` — gestionnaire de mots de passe chiffré et/ou papier en lieu
sûr. À faire une fois, et à revérifier après toute régénération de la clé.

## Alerte d'échec de backup (dead-man's-switch)

Le backup « ping » une URL à chaque succès ; l'absence de ping déclenche
l'alerte. Le câblage est en place (`backup.sh` pinge `BACKUP_PING_URL` après un
upload réussi) mais **inactif tant que l'URL n'est pas fournie**.

Activation (une fois) :
1. Créer un check sur [healthchecks.io](https://healthchecks.io) (gratuit),
   période 1 jour + grâce, avec ton canal d'alerte (e-mail).
2. Copier l'URL de ping du check dans `backup_ping_url` — dans
   `prod-secrets.yml` **et** le secret GitHub `PROD_SECRETS`.
3. Redéployer. Dès lors : un backup qui échoue (ou un serveur mort) = pas de
   ping = alerte.

## Restauration d'un backup

`make backup-restore-test` télécharge le dernier backup, le déchiffre (clé
privée `age`), le restaure dans un Postgres jetable et vérifie le contenu.
C'est aussi la répétition à faire périodiquement — la restauration testée, pas
espérée.
