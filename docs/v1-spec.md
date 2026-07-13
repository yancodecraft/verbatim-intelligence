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

1. **Créer un compte / se connecter** — par magic link (e-mail), sans mot de
   passe. Chaque compte voit uniquement ses propres analyses.
2. **Uploader un CSV** de verbatims (jusqu'à ~5 000 lignes).
3. **Mapper la colonne verbatim** — aperçu du fichier, l'utilisateur désigne
   la colonne contenant le verbatim. Rien d'autre : les métadonnées (date,
   score, segment) ne sont pas collectées tant que rien ne les consomme.
4. **Lancer l'analyse** — progression visible pendant le traitement.
5. **Explorer les thèmes émergents** — les verbatims sont regroupés par thèmes
   découverts dans le corpus (pas de taxonomie prédéfinie), chaque thème étant
   pondéré par son volume.
6. **Lire la synthèse de chaque thème** — un résumé fidèle, accompagné des
   verbatims représentatifs **mot pour mot**. Un verbatim cité est une
   **référence à la ligne d'origine du corpus** — jamais un texte régénéré :
   la fidélité est un invariant vérifié par le système, pas une promesse de
   prompt.
7. **Partager le rapport** — un lien public en lecture seule, à token non
   devinable, révocable et non indexable, qui montre l'analyse complète sans
   compte.

## Décisions de périmètre

- **Ingestion : CSV uniquement**, sous contrat explicite : UTF-8 (BOM toléré),
  délimiteur auto-détecté (`,` ou `;`), limites de taille, de lignes et de
  longueur de cellule, rejet propre avec message clair. « Exportez en CSV »
  couvre la quasi-totalité des sources du persona.
- **Volume : ~5 000 verbatims par import**, traités proprement (batching, coût
  plafonné par analyse, progression visible).
- **Langue : UI en anglais.** Les synthèses sont produites dans la langue
  dominante du corpus, pour que citations et résumés restent dans la même
  langue. Cette langue est **détectée par le système**, pas laissée à
  l'appréciation du LLM (le spike a montré qu'il se trompe).
- **Fidélité avant exhaustivité.** Un verbatim cité est toujours exact — par
  construction (référence à la ligne d'origine, jamais de texte régénéré) ;
  si un thème est incertain, on le dit plutôt que de lisser.
- **Aucune perte silencieuse.** Le spike a montré que le LLM peut « oublier »
  des pans entiers de corpus tout en rendant des réponses bien formées. La
  V1 compte donc ce qu'elle traite : tout verbatim qu'aucune étape n'a pu
  rattacher à l'analyse est comptabilisé et **visible dans les résultats**
  (« N verbatims non classés »), jamais passé sous silence.
- **Les verbatims sont des données personnelles de tiers.** Les retours
  clients contiennent presque toujours des données personnelles des clients
  de nos utilisateurs. Conséquences V1 : traitement via l'API Anthropic en
  zero-data-retention, information claire à l'upload (« contenu traité par un
  service tiers »), suppression en cascade (corpus → analyse → partages) à la
  demande, et jamais de corpus réel dans le repo ou les fixtures.

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
