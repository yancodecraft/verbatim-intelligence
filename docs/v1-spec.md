# Spec V1 — Verbatim Intelligence

Ce document fige le périmètre de la première version. Tout ce qui n'y figure pas
est hors périmètre jusqu'à décision contraire.

## Persona

**Le PM / responsable produit** d'une entreprise qui collecte du feedback
(réponses NPS, sondages, tickets support exportés). Il reçoit ces retours en
vrac, n'a pas le temps de tout lire, et doit pourtant prioriser sur cette base.
Il sait exporter un CSV depuis n'importe quel outil ; il ne veut ni configurer
des intégrations, ni apprendre un produit complexe.

## Parcours V1

Un parcours unique, de bout en bout :

1. **Créer un compte / se connecter** — auth simple (email), chaque compte voit
   uniquement ses propres analyses.
2. **Uploader un CSV** de verbatims (jusqu'à ~5 000 lignes).
3. **Mapper les colonnes** — aperçu du fichier, l'utilisateur désigne la colonne
   contenant le verbatim, plus éventuellement une ou deux colonnes de
   métadonnées (date, score, segment). Aucun format imposé.
4. **Lancer l'analyse** — progression visible pendant le traitement.
5. **Explorer les thèmes émergents** — les verbatims sont regroupés par thèmes
   découverts dans le corpus (pas de taxonomie prédéfinie), chaque thème étant
   pondéré par son volume.
6. **Lire la synthèse de chaque thème** — un résumé fidèle, accompagné des
   verbatims représentatifs **mot pour mot**. Chaque affirmation de synthèse
   doit être traçable vers des verbatims réels : pas de paraphrase présentée
   comme citation.
7. **Partager le rapport** — un lien public en lecture seule, à token non
   devinable et révocable, qui montre l'analyse complète sans compte.

## Décisions de périmètre

- **Ingestion : CSV uniquement.** C'est l'unique porte d'entrée ; « exportez en
  CSV » couvre la quasi-totalité des sources du persona.
- **Volume : ~5 000 verbatims par import**, traités proprement (batching, coût
  maîtrisé, progression visible).
- **Langue : UI en anglais.** Les synthèses sont produites dans la langue
  dominante du corpus, pour que citations et résumés restent dans la même
  langue.
- **Fidélité avant exhaustivité.** Un verbatim cité est toujours exact ; si un
  thème est incertain, on le dit plutôt que de lisser.

## Non-goals V1

Exclusions assumées, pas des oublis :

- **Connecteurs et API d'ingestion** (Typeform, Zendesk, endpoint REST…) — le
  CSV suffit au persona.
- **Suivi temporel des thèmes** — la V1 photographie un corpus à un instant
  donné ; elle ne compare pas des imports entre eux et ne détecte pas « un
  thème qui monte ». C'est la direction long terme du produit, pas la V1.
- **Équipes, rôles, invitations** — un compte = un espace ; le rapport
  partageable couvre le besoin de diffusion.
- **Facturation / plans payants** — pas de paiement ni de quotas commerciaux
  en V1.

## Prochaine étape

Définir le « comment » : architecture, stack, découpage du travail
(étape 2 du CLAUDE.md).
