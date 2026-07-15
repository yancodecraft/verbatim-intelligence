# RGPD — registre de sous-traitance et rétention

Ce document tient lieu de **registre des activités de traitement**
(RGPD Article 30) et rassemble les décisions de conformité. Il est le pendant
« conformité » de la [security review](security-review.md). La politique de
confidentialité destinée aux utilisateurs (page publique `/privacy`) en dérive.

## 1. Responsable de traitement

- **Responsable** : YANTECH (exploitant de Verbatim Intelligence).
- **Contact RGPD** : privacy@yantech.fr — *alias à créer/forwarder côté domaine
  yantech.fr s'il n'existe pas encore ; le TEM est déjà configuré sur ce domaine.*
- **DPO** : aucun — non requis pour cette structure (pas de traitement à grande
  échelle de données sensibles au sens de l'Article 37 RGPD).
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

**Décision (2026-07-15)** : on **reste sur le défaut commercial** (pas
d'entraînement, logs supprimés sous ~7 j). C'est conforme et suffisant pour
cette structure ; le ZDR strict est un dispositif entreprise dont on n'a pas
besoin. La politique de confidentialité décrit donc ce comportement réel — sans
prétendre au ZDR. À rouvrir seulement si un client l'exige contractuellement.

Références Anthropic : [API and data retention](https://platform.claude.com/docs/en/manage-claude/api-and-data-retention),
[ZDR — produits couverts](https://privacy.claude.com/en/articles/8956058-i-have-a-zero-data-retention-agreement-with-anthropic-what-products-does-it-apply-to).

## 6. Ce qui reste avant ouverture

- [x] Responsable de traitement (YANTECH) et contact RGPD renseignés (§1).
- [ ] Créer/forwarder l'alias `privacy@yantech.fr` (côté domaine).
- [ ] Vérifier que l'organisation Anthropic est bien sous Commercial Terms (DPA automatique). ZDR : non demandé, on reste sur le défaut (§5).
- [x] Politique de confidentialité publiée (page `/privacy`, dérivée de ce registre).
- [ ] Livrer la purge périodique des tokens/comptes expirés (finding D5).
