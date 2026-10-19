# Couverture de l’énoncé — partie Iheb

Les responsabilités proviennent de la répartition fournie par le groupe. La colonne « preuve » distingue les vérifications automatisées des essais encore nécessaires dans la maquette.

| Exigence | Implémentation | Preuve et état |
| --- | --- | --- |
| Développer l’interface | `frontend/src/`, pages françaises, responsive, formulaires et états de traitement | Build TypeScript/Vite et parcours Chromium automatisés |
| CRUD/recherche modèles | `CataloguePlugin`, `Models.tsx` | Tests métier/API ; précision contrôlée |
| CRUD/recherche ingrédients | `CataloguePlugin`, `ReferenceData.tsx` | Tests API et création/édition/suppression dans le vrai navigateur |
| Caractéristiques inconnues à l’avance | Définitions et valeurs, portée commune ou spécifique, formulaire dynamique | Test ajout d’une nouvelle définition sans migration |
| Grammage à 0,01 g | Entiers `QuantityCg`, conversion exacte `scaled.mjs` | Tests de décimales et aller-retour API |
| Procédés et étapes | `ProcessPlugin`, `Processes.tsx`, ordre explicite des étapes | Tests de création et réordonnancement |
| Tests et validations | `TestPlugin`, `Tests.tsx` | Résultat échoué refusé, validation complète, historique et anciennes révisions |
| Séparation front/middleware/proxy | Front, hôte Local, `ApplicationApi`, `SecureProxy` | Parcours complet traverse ces composants ; placement du middleware documenté |
| Messages normalisés | `DispatchRequest`, `ApiReply`, codes et identifiants de suivi | Vérifications HTTP des succès/erreurs, interface pending durant l’appel |
| Point d’entrée et documentation | `/platform/dispatch`, `/components/execute`, `docs/API.md` | Documentation fournie ; appels directs non authentifiés refusés |
| Contrôleur de résolution | Registre fermé, type/version du client, rôle puis dispatch | Version non prise en charge et droits interdits testés |
| Deux VM et répartition | Hôte Components réplicable, deux destinations et rotation | Deux instances locales vérifiées ; installation de deux VM réelles à faire avec l’équipe |
| Exécution transactionnelle et plugins | `OperationEngine`, `IBusinessPlugin`, `DataProxy` | Tests de rollback, références et révisions |
| Connexion SQL Server avec Ramzi | Provider SQL Server, schémas explicites, `schema.sql`, permissions de référence | Compilation et génération SQL vérifiées ; exécution sur son SQL Server à valider |
| Comptes AD individuels jusqu’à SQL | Auth Windows, délégation explicite, contrôle `ORIGINAL_LOGIN()` | Code et configuration fournis ; preuve sur votre domaine à produire avec ASD/Ramzi |
| Restrictions de routes/données | Double contrôle des opérations et vues de production | Compte PROD interdit de prototype, d’écriture et de référentiel brut en démo ; permissions SQL à tester en maquette |
| Chiffrement avec Mehdi | Deux substitutions distinctes et transposition, enveloppe HMAC | Tests sur octets/Unicode, altération, finalité et expiration ; revue de Mehdi et TLS réel à faire |
| Validation et gestion des erreurs | `Validation`, contrôles par plugin, CSRF, codes communs | Tests entrée invalide, CSRF, 401/403/404/409 et UI d’erreur |
| Supervision/reporting | Tableau de bord, audit, `/metrics`, identité et instance | Compteurs et diagnostic implémentés ; raccordement à la supervision de l’équipe à faire |
| Livrables et preuves | README, docs, code, scripts, CI, captures et rapports | Artefacts de workflow ; compléter les captures AD/SQL/VLAN réelles |

## Ce qui reste aux autres membres

Meriem : équipements Cisco/Stormshield, VLAN/IP/Wi-Fi et flux. ASD : domaine, utilisateurs/groupes, GPO et journalisation des sessions. Ramzi : administration SQL, comptes individuels et de service, profils DBA/BCK/USR, revue des permissions et sauvegarde/restauration. Mehdi : revue du chiffrement pédagogique, coordination des tests de sécurité et recommandations.

Le dépôt apporte les contrats et points d’intégration nécessaires. Il ne prétend pas configurer à distance des machines auxquelles aucun accès n’a été fourni.

## Suite en phase Red Team

L’énoncé prévoit ensuite l’audit d’un périmètre autorisé. Les contrats API et l’audit facilitent ce travail, mais aucun audit d’un autre groupe n’est déclaré réalisé. Son rapport devra contenir périmètre, tests, outils, paramètres, résultats, criticité et recommandations réelles.
