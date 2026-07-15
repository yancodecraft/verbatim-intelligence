# RGPD — registre de sous-traitance et rétention

Ce document tient lieu de **registre des activités de traitement**
(RGPD Article 30) et rassemble les décisions de conformité. Il est le pendant
« conformité » de la [security review](security-review.md). La politique de
confidentialité destinée aux utilisateurs (page publique `/privacy`) en dérive.

> **À compléter par Yannick** (informations que le code ne peut pas fournir) :
> - **Responsable de traitement** : identité légale (personne physique ou
>   société, n° SIREN le cas échéant), adresse.
> - **Contact** : e-mail dédié aux demandes RGPD (droits des personnes).
> - Ces deux éléments sont publics par nature (ils figurent dans la politique
>   de confidentialité) ; ils ne relèvent pas des « constantes privées » du repo.

## 1. Responsable de traitement

- **Responsable** : `[À COMPLÉTER : identité légale]`
- **Contact RGPD** : `[À COMPLÉTER : e-mail]`
- **Rôle** : Verbatim Intelligence agit comme **responsable de traitement**
  pour les comptes (e-mails de connexion) et comme **sous-traitant** pour les
  verbatims que ses clients y déposent (données de leurs propres répondants).

## 2. Finalités et données traitées

| Finalité | Données | Base légale |
|---|---|---|
| Authentification (magic link) | e-mail du compte | Exécution du service / intérêt légitime |
| Analyse de verbatims | texte des verbatims (données personnelles de tiers possibles), nom du fichier | Sous-traitance pour le compte du client |
| Partage de rapport | rapport (thèmes, verbatims cités) via lien à token | Exécution du service |

Verbatim Intelligence **ne demande jamais** d'autres catégories ; il incombe au
client de ne déposer que des verbatims qu'il est en droit de traiter (rappelé
par la mention à l'upload).

## 3. Sous-traitants ultérieurs

| Sous-traitant | Traitement | Localisation | Encadrement |
|---|---|---|---|
| **Anthropic PBC** | Traitement LLM des verbatims (regroupement en thèmes, synthèses) | États-Unis (sous-traitant ultérieur : AWS) | DPA d'Anthropic, intégré d'office aux *Commercial Terms* depuis le 2026-01-01. Entrées/sorties **non utilisées pour l'entraînement** ; logs opérationnels supprimés sous ~7 jours. Voir §5. |
| **Scaleway SAS** | Hébergement du VPS, stockage des backups chiffrés (Object Storage), envoi des e-mails transactionnels (TEM) | France (UE) | DPA Scaleway ; hébergement UE, pas de transfert hors UE pour l'hébergement/backup. |

Aucun autre sous-traitant. Aucun outil d'analytics ni de télémétrie tiers
(vérifié : aucun Sentry/GA/gtag dans le code).

## 4. Rétention et effacement

| Donnée | Rétention | Mécanisme |
|---|---|---|
| CSV brut uploadé | **Purgé dès la création de l'analyse** | `uploads.content` vidé (code) |
| Verbatims, thèmes, synthèses | Tant que l'analyse existe | Suppression sur action utilisateur |
| Analyse | Jusqu'à suppression par l'utilisateur | `DELETE /analyses/{id}` (cascade) |
| Compte + toutes ses données | Jusqu'à suppression par l'utilisateur | `DELETE /auth/account` (cascade) |
| Login tokens / sessions expirés, comptes non vérifiés | Purge périodique | *À livrer (finding D5) — voir security-review.md* |
| Backups Postgres | 30 jours (chiffrés) | Rotation Object Storage. Une donnée supprimée persiste au plus 30 j dans les backups — borne assumée. |

Droits des personnes (accès, rectification, effacement, opposition) : exerçables
via le contact RGPD ci-dessus ; l'effacement d'un compte est self-service dans
l'app.

## 5. Statut DPA / ZDR Anthropic — runbook

**DPA** : le DPA d'Anthropic est **automatiquement incorporé aux Commercial
Terms** (effectif 2026-01-01), sans signature séparée. **À vérifier** : que
l'organisation API du projet est bien sous les *Commercial Terms* (compte API
commercial, pas un usage consumer). Si oui, le DPA est en place — prérequis
d'ouverture satisfait de ce côté.

**ZDR (zero-data-retention)** : contrairement à ce que les docs affirmaient, le
ZDR **n'est pas le comportement par défaut**. C'est un arrangement réservé à
certains clients Claude API / entreprise, **soumis à l'approbation d'Anthropic**,
à demander à l'équipe commerciale. Par défaut (sans ZDR) : pas d'entraînement,
mais rétention des logs opérationnels ~7 jours.

**À décider** : le défaut (pas d'entraînement, logs ~7 j) est-il acceptable, ou
faut-il demander le ZDR strict ? Pour le demander : contacter Anthropic (sales /
support) depuis l'organisation API. Tant que le ZDR n'est pas confirmé par
écrit, ne pas l'affirmer publiquement — la politique de confidentialité doit
décrire le comportement **réel**.

Références Anthropic : [API and data retention](https://platform.claude.com/docs/en/manage-claude/api-and-data-retention),
[ZDR — produits couverts](https://privacy.claude.com/en/articles/8956058-i-have-a-zero-data-retention-agreement-with-anthropic-what-products-does-it-apply-to).

## 6. Ce qui reste avant ouverture

- [ ] Renseigner le responsable de traitement et le contact RGPD (§1).
- [ ] Vérifier que l'organisation Anthropic est sous Commercial Terms (DPA) ; décider ZDR ou non (§5).
- [ ] Publier la **politique de confidentialité** (page `/privacy`), dérivée de ce registre.
- [ ] Livrer la purge périodique des tokens/comptes expirés (finding D5).
