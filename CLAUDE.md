# CLAUDE.md — Verbatim Intelligence

## Le pitch

Toute entreprise qui écoute ses clients finit noyée : réponses NPS, avis, sondages,
tickets support, verbatims d'entretiens — éparpillés dans dix outils, lus par personne,
résumés de mémoire dans des slides. Les décisions se prennent sur les 20 derniers
retours lus, pas sur les 5 000 reçus. Les signaux faibles (un thème qui monte, une
friction récurrente formulée avec les mots exacts des clients) passent à la trappe.

**Verbatim Intelligence** transforme ces retours bruts en décisions : on ingère les
verbatims quelle que soit leur source, on les regroupe par thèmes émergents, et on en
sort des synthèses fidèles — avec les verbatims représentatifs mot pour mot, parce
qu'un verbatim exact vaut mieux que dix paraphrases — jusqu'au tableau de bord que les
équipes produit, marketing et support peuvent réellement utiliser.

Ce qu'on sait déjà : ce sera une **app SaaS, sur le web**.

## Où on en est

1. ~~**Définir ce qu'on veut**~~ — fait : voir [docs/v1-spec.md](docs/v1-spec.md)
   (persona, parcours V1, périmètre, non-goals).
2. ~~**Définir comment on va le faire**~~ — fait : architecture et stack dans
   [docs/architecture.md](docs/architecture.md), découpage en tranches dans
   [docs/roadmap.md](docs/roadmap.md).
3. **Construire, tranche par tranche** — voir la roadmap. ← on en est là
   (tranche 1 finie : le squelette tourne en production sur
   https://verbatim.yantech.fr avec CI/CD complet ; à suivre : tranche 2,
   l'auth par magic link, et le spike pipeline en parallèle).

Une tranche n'est « finie » que testée, exercée réellement, et déployée.

## Règles de travail — à ne jamais oublier

Les pratiques complètes sont dans [docs/practices.md](docs/practices.md).
Les invariants ci-dessous priment sur tout le reste :

1. **Fidélité par construction** : un verbatim cité est une référence (FK)
   vers la ligne d'origine — le LLM sélectionne des ids, il ne régénère
   JAMAIS un texte cité. Cet invariant est testé, tout code qui le
   contourne est un bug.
2. **Une tranche à la fois**, dans l'ordre de la
   [roadmap](docs/roadmap.md) ; avant tout code non trivial, un plan court
   validé par Yannick. Pas de scaffold anticipé.
3. **« Fini » = les 4 critères** de
   [practices.md](docs/practices.md#définition-de--fini-) : tests en CI,
   revue par agent, parcours exercé réellement, en production. Rien n'est
   fini sans vérification réelle.
4. **TDD** sur `backend/` et `ai-worker/` — le test d'abord. Zéro warning,
   bloquant.
5. **Le glossaire fait loi** ([docs/glossary.md](docs/glossary.md)) : le
   code emploie exclusivement ses termes ; tout nouveau concept y passe
   d'abord.
6. **Jamais dans le repo** : secrets, clés API, corpus de verbatims réels
   (données personnelles de tiers) — y compris dans les fixtures de test.
7. **Historique honnête** : Conventional Commits en anglais, pas de rebase
   cosmétique, les reprises restent visibles. Toute décision structurante
   est consignée dans [JOURNAL.md](JOURNAL.md).
8. **Avant chaque `git commit` : dérouler le skill `doc-check`** (cohérence
   sémantique changement ↔ docs). Le hook pre-commit fait les vérifications
   mécaniques.
9. Docs en français ; code, commits et UI en anglais.
