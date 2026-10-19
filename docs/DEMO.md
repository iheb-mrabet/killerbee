# Parcours de démonstration

## Préparer

Lancer `node scripts/demo.mjs` et ouvrir `http://127.0.0.1:5100`. Les données sont fictives. Les profils se changent avec le bouton de déconnexion en haut à droite. Le modèle **KB Loisir** est déjà validé ; **KB Competition** est un brouillon.

## Parcours complet

1. Choisir **R&D**. Créer un ingrédient « Résine de démonstration » avec sa description.
2. Créer un modèle « KB Démonstration », gamme Sport, prix `24,99`, composition `125,37 g` et diamètre `27,3`.
3. Dans Caractéristiques, créer un champ texte propre à ce modèle : « Finition ». Revenir modifier le modèle et saisir « Mate ». Aucun changement de code n’est nécessaire.
4. Créer un procédé pour le modèle, avec deux étapes : Peser puis Mouler. Utiliser les flèches pour montrer le réordonnancement, puis rétablir l’ordre voulu.
5. Définir un test pour ce procédé : « Contrôle de masse », résultat attendu `125,37 g`.
6. Consulter le modèle et cliquer **Soumettre aux tests**. Le statut devient En test.
7. Passer au profil **Production** : le prototype ne doit pas apparaître.
8. Passer au profil **Tests**. Enregistrer un résultat échoué ; son approbation doit être impossible. Le refuser avec une explication.
9. Enregistrer un résultat réussi avec les mesures. Examiner cette exécution et l’approuver.
10. Revenir en Production : le modèle apparaît, sa composition exacte et ses instructions sont consultables. Les actions d’édition et le référentiel brut d’ingrédients ne sont pas disponibles.
11. Revenir en R&D et modifier la quantité à `125,38 g`. Le modèle repasse Brouillon ; Production ne peut plus le consulter et les anciennes exécutions restent dans l’historique.
12. Dans Traçabilité, montrer les actions des deux services et leurs identifiants de suivi.

## Preuves techniques complémentaires

Dans Ma connexion, vérifier l’instance qui répond ; des appels successifs passent par les deux instances. En démo, l’identité SQL affiche explicitement SQLite et aucune vérification AD n’est annoncée.

Dans la vraie maquette, refaire cette vérification avec deux utilisateurs AD différents : capturer le compte applicatif, `sqlIdentity`, `originalLogin` et le nom de l’instance. Vérifier les permissions directement dans SQL et les communications TLS avec l’équipe.

Les tests automatisés couvrent aussi les références empêchant une suppression, les anciennes révisions, les accès non authentifiés, l’antiforgery, les versions de client et les champs dynamiques. Le workflow GitHub conserve les captures Chromium, les logs de démo et le rapport de tests.

## Dépannage rapide

| Symptôme | Action |
| --- | --- |
| `dotnet` introuvable | Installer le SDK .NET 10 puis rouvrir le terminal. |
| npm/Vite refuse la version de Node | Utiliser Node.js 24 LTS. |
| Port déjà occupé | Arrêter la démonstration précédente ; le script ne tue pas les programmes d’un autre utilisateur. |
| Écran vide ou services indisponibles | Lire `.demo/runner.log`, `local.log`, `platform.log`, `components-a.log`, `components-b.log`. |
| Erreur de jeton de formulaire après un changement de session | Recharger la page pour obtenir le jeton lié à la session courante. |
| Révision en conflit | Recharger l’élément avant de refaire la modification. |
| Suppression bloquée | Retirer les références inutilisées ; l’historique des tests est protégé. |
| `DELEGATION_REQUIRED` en maquette | Vérifier Kerberos, SPN et délégations avec ASD. |
| `SQL_IDENTITY_MISMATCH` | Vérifier la propagation du compte utilisateur avec ASD et Ramzi. |

Pour repartir d’une base fictive vierge, arrêter les processus et déplacer le dossier `.demo` ailleurs avant relancement. Le script ne supprime pas vos données automatiquement.
