# Spike pipeline

Script **autonome et jetable** (voir [la roadmap](../docs/roadmap.md)) : il
lève le plus gros risque du produit — la qualité des thèmes et des synthèses —
avant que la tranche 4 n'industrialise quoi que ce soit. Son code ne rejoint
pas les briques ; sa conclusion rejoint le [journal](../JOURNAL.md).

## Ce qu'il compare

Deux stratégies de regroupement, à corpus et modèle constants :

- **`direct`** — découverte des thèmes par batchs, puis une passe de
  consolidation qui fusionne les doublons inter-batchs.
- **`taxonomy`** — une taxonomie émergente proposée sur un échantillon, puis
  classification de tout le corpus contre elle.

Dans les deux cas la fidélité est **par construction** : le LLM ne retourne
que des ids de verbatims ; toute citation du rapport est résolue depuis le
corpus par id. Un id cité hors de son thème est rejeté et compté comme
violation.

## Corpus

- `golden/corpus.json` — **golden corpus synthétique** (~100 verbatims, produit
  SaaS fictif) : 6 thèmes plantés + bruit (hors-sujet, doublons, multilingue).
  Les étiquettes `expected` ne sont lues que par l'évaluation, jamais par le
  pipeline. Aucune donnée réelle : c'est le seul corpus qui vit dans le repo.
- Corpus réels : dans `../corpus/` (gitignoré — règle 6), voir leur
  `PROVENANCE.md`. CSV à une colonne `verbatim`.

## Lancer

La clé API est lue de `~/.config/verbatim-intelligence/anthropic.key`
(hors repo, `chmod 600`). Docker est la seule dépendance :

```sh
docker run --rm \
  -v "$PWD/spike:/spike" -v "$PWD/corpus:/corpus:ro" -w /spike \
  -e ANTHROPIC_API_KEY="$(cat ~/.config/verbatim-intelligence/anthropic.key)" \
  ghcr.io/astral-sh/uv@sha256:7cf77f594be8042dab6daa9fe326f90962252268b4f120a7f5dccce4d947e6c1 \
  uv run pipeline.py --input golden/corpus.json --strategy direct
```

Autres entrées : `--input /corpus/state-of-css-2021/browser_interoperability_features.csv`,
`--strategy taxonomy`, `--model claude-sonnet-5|claude-haiku-4-5`.

Les rapports tombent dans `spike/results/` (gitignoré : les runs sur corpus
réels citent des verbatims réels). Sur le golden corpus, le rapport se termine
par l'évaluation contre les thèmes plantés.
