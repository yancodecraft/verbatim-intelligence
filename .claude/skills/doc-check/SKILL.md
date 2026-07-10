---
name: doc-check
description: Vérifie la cohérence sémantique entre les changements stagés et la documentation du projet (spec, architecture, roadmap, glossaire, practices, journal). À dérouler systématiquement AVANT chaque `git commit`, une fois les fichiers stagés. Déclencher dès qu'un commit est demandé ou sur le point d'être créé.
---

# Doc-check — cohérence changement ↔ documentation

Le hook pre-commit (`scripts/githooks/pre-commit`, qui exécute
`scripts/check_docs.py` dans un conteneur) fait les vérifications mécaniques
(liens, canonicité, vocabulaire). Ce skill couvre ce qu'un script ne peut
pas juger : **le sens**. Dérouler les étapes dans l'ordre, sur le diff stagé
(`git diff --cached`).

## Étapes

1. **Qualifier le changement.** Pour chaque fichier stagé, déterminer ce
   qu'il change : comportement produit ? structure technique ? méthode de
   travail ? simple correction ?

2. **Vérifier l'impact documentaire.** Pour chaque impact détecté, le doc
   correspondant doit être stagé aussi (ou déjà à jour) :

   | Le changement touche… | Alors vérifier… |
   |---|---|
   | Périmètre, parcours, comportement visible | `docs/v1-spec.md` |
   | Structure des briques, schéma, flux, résilience, dépendances d'infra | `docs/architecture.md` (et `docs/schema.md` quand il existera) |
   | Ordre, contenu ou périmètre d'une tranche | `docs/roadmap.md` |
   | Un concept nouveau ou renommé | `docs/glossary.md` — le glossaire fait loi |
   | Une règle de travail (test, CI, sécurité, style) | `docs/practices.md` |
   | Une décision structurante, un arbitrage, une reprise | `JOURNAL.md` — entrée datée avec la justification |
   | Une règle que l'IA ne doit jamais oublier | `CLAUDE.md` (règles de travail) |

3. **Vérifier la réciproque.** Les docs stagés ne promettent-ils rien que le
   changement ne fait pas ? (Une doc en avance sur le code est une dérive
   aussi.)

4. **Vérifier la terminologie.** Les identifiants et textes du changement
   emploient les termes du glossaire — pas de synonymes inventés.

5. **Conclure.** Si tout est cohérent : le dire en une phrase et committer.
   Sinon : corriger ou compléter les docs stagés AVANT le commit — jamais
   « on documentera après ».
