# Le schéma — contrat entre backend et worker

Le schéma Postgres est le contrat que les deux briques écrivent sans se voir
(voir [architecture.md](architecture.md#le-schéma-est-un-contrat--et-se-gère-comme-tel)) :
**EF Core (backend) est l'unique propriétaire des migrations** — les fichiers
sous `backend/src/VerbatimIntelligence.Api/Migrations/` font foi — et le worker
lit et écrit ce schéma en SQL brut sans jamais le migrer. Ce document est la
carte : qui écrit quoi, et ce que chaque table garantit. Tout est en
`snake_case` ; les identifiants sont des UUID v7.

## Vue d'ensemble

```
users ─┬─< sessions
       ├─< login_tokens
       ├─< uploads
       └─< analyses ─┬─< verbatims ──< theme_verbatims >── themes
                     └─< themes ─────────────────────────────┘
```

Toutes les FK sont `ON DELETE CASCADE` : supprimer un compte emporte tout ce
qu'il possède ; supprimer une analyse emporte son corpus et ses thèmes.

## Les tables du compte (écrites par le backend seul)

- **`users`** — le compte (glossaire : *Compte*) : `email` unique (≤ 320).
- **`login_tokens`** — les magic links : `token_hash` (SHA-256 hex, unique),
  usage unique (`used_at`), expiration courte. Sert aussi de registre au
  rate limiting des demandes de connexion.
- **`sessions`** — les sessions établies : `token_hash` unique, révocables
  côté serveur.
- **`uploads`** — le fichier CSV tel quel (`content` bytea), ses `columns`
  détectées (jsonb, liste de chaînes) et son `row_count`.

## Les tables de l'analyse (le contrat inter-briques)

### `analyses` — écrite par les deux, chacun ses colonnes

Le backend **crée** la ligne (`pending`) ; le worker la fait **avancer**.

| Colonne | Écrivain | Rôle |
|---|---|---|
| `user_id`, `source_filename`, `verbatim_count`, `created_at` | backend | identité et dénormalisations pour la liste |
| `status` | les deux | `pending → running → succeeded / failed`, gardé par un CHECK ; transitions par compare-and-set (`UPDATE … WHERE status = …`) |
| `heartbeat_at` | worker/reaper | dernier signe de vie : battu par le worker entre les étapes du pipeline, estampillé par le reaper quand il remet en file ou republie (ce qui borne la fréquence de republication) |
| `attempts` | worker/reaper | nombre de claims ; au-delà d'un seuil le reaper passe en `failed` |
| `error` | worker | la cause d'un échec, montrée à l'utilisateur |
| `processed_count` | worker | verbatims passés par la découverte — la progression |
| `input_tokens`, `output_tokens` | worker | dépense LLM cumulée, adosse le plafond de coût |

Les colonnes worker ont toutes un défaut en base : un backend N-1 qui les
ignore insère des lignes valides (expand/contract).

### `verbatims` — écrite par le backend, lue par le worker

Le corpus : `analysis_id`, `position` (index 0-based de la ligne source,
trous possibles), `text` (**exact, jamais retaillé** — l'invariant no-trim est
testé). Le worker ne modifie jamais cette table.

### `themes` — écrite par le worker, lue par le backend

Un thème émergent : `analysis_id`, `name` (≤ 200), `synthesis` (texte,
dans la langue du corpus), `position` (ordre d'affichage 0-based, décidé par
le worker — poids décroissant).

### `theme_verbatims` — écrite par le worker ; la fidélité vit ici

Le rattachement d'un verbatim à un thème. PK composite
(`theme_id`, `verbatim_id`) — un verbatim ne se rattache qu'une fois par
thème. `rank` : `NULL` = rattaché (soutient le thème), `0..n` = représentatif
(ordre de citation dans la synthèse). **Une citation est cette FK** : le LLM
sélectionne des ids, aucun texte cité n'est stocké ni régénéré — c'est
l'invariant n°1, que la base elle-même garantit (une citation ne peut
référencer qu'une ligne `verbatims` existante) et que les tests de schéma
vérifient.

## Règles de coexistence

- **Expand/contract obligatoire** : une migration ne casse jamais la version
  N-1 déployée de l'autre brique.
- **Idempotence du worker** : avant de (re)traiter une analyse, il purge ses
  `themes` (les `theme_verbatims` suivent par cascade) — rejouer ne duplique
  jamais.
- Ce document se met à jour **dans le même commit** que toute migration.
