# KillerBee — atelier de fabrication

[![KillerBee CI](https://github.com/iheb-mrabet/killerbee/actions/workflows/ci.yml/badge.svg)](https://github.com/iheb-mrabet/killerbee/actions/workflows/ci.yml)

Application intranet du projet Cybersécurité A5 : concevoir les modèles de frisbees et leurs compositions, décrire les procédés, exécuter les tests et transmettre les versions validées à la production. Ce dépôt couvre le développement attribué à **Iheb**.

## Démarrer la démonstration

Prérequis : **SDK .NET 10**, **Node.js 24 LTS** et npm. Les téléchargements de dépendances nécessitent Internet au premier démarrage. L’application construite fonctionne ensuite sans service externe.

```bash
git clone https://github.com/iheb-mrabet/killerbee.git
cd killerbee
node scripts/demo.mjs
```

Ouvrir **http://127.0.0.1:5100**. Choisir le profil fictif **R&D**, **Tests** ou **Production**. Arrêter avec `Ctrl+C`.

Le script construit l’interface et les services, initialise SQLite avec des données fictives, puis lance le middleware applicatif, la plateforme et **deux instances de composants**. Les deux instances partagent le même fichier SQLite sur cette machine. La clé de transport est générée à chaque lancement et reste en mémoire. Les données et journaux locaux sont dans `.demo/`, exclu de Git.

**Cette démonstration ne valide ni Active Directory, ni SQL Server, ni un déploiement sur deux VM distinctes.** Le mode domaine est implémenté séparément et refuse les authentifications de démonstration. Son installation demande les serveurs, certificats, comptes et délégations de votre maquette : [déploiement Windows / AD / SQL](docs/DEPLOYMENT.md).

## Fonctions livrées

- Interface en français, responsive, formulaires accessibles et messages d’erreur avec identifiant de suivi.
- Ajout, modification, recherche et suppression des modèles, ingrédients et procédés, avec contrôle des références.
- Composition exacte en **centigrammes** et prix en **centimes**, sans arrondi des saisies trop précises.
- Caractéristiques communes ou propres à un modèle : texte, nombre, booléen, unité et caractère obligatoire.
- Étapes ordonnées et réordonnables ; définitions des tests globales ou liées à une étape.
- Exécution réussie/échouée, examen du résultat, approbation/refus et historique conservé.
- Publication automatique à la production quand tous les tests de la version courante sont réussis et approuvés.
- Toute modification de recette, d’ingrédient, de caractéristique, de procédé ou de test remet les modèles concernés en brouillon et invalide l’utilisation des anciens résultats.
- Permissions vérifiées dans la résolution **et** dans l’exécution ; vues SQL dédiées à la production.
- Journal des modifications, diagnostic de l’identité SQL et compteurs de supervision.
- Chiffrement pédagogique : substitution affine, substitution par table dérivée de la clé, transposition par colonnes ; enveloppes authentifiées par HMAC et contrôle de fraîcheur.

Les noms de caractéristiques, recettes et modes opératoires fournis sont des **exemples fictifs**. Le workflow et la matrice métier sont une interprétation documentée du sujet, à convenir avec l’équipe.

## Stack et composants

| Dossier | Responsabilité |
| --- | --- |
| `frontend/` | React, TypeScript, Vite, composants et abstraction des appels API |
| `src/KillerBee.Local/` | Middleware propre à l’application, sessions, CSRF et proxy vers la plateforme |
| `src/KillerBee.Platform/` | Point d’entrée unique, résolution des demandes, droits, répartition et repli des lectures |
| `src/KillerBee.Components/` | Hôte répliqué sur les deux VM, point d’entrée transactionnel et supervision |
| `src/KillerBee.Data/` | Plugins Catalogue/Procédés/Tests/Reporting, transactions, EF Core et proxy de données |
| `src/KillerBee.Core/` | Contrats, registre des opérations, validations et chiffrement pédagogique |
| `src/KillerBee.Hosting/` | Authentification Windows, relais de démonstration, délégation et protections HTTP |
| `database/` | Schéma SQL Server et permissions de référence à faire valider par Ramzi |
| `docs/` | Architecture, API, sécurité, déploiement, démonstration et couverture de l’énoncé |

Le middleware est local **à l’application web**, distinct de la plateforme. L’énoncé ne précise pas explicitement qu’il doit être installé sur chaque poste utilisateur ; cette lecture est signalée dans le document d’architecture.

## Vérifier le projet

```bash
dotnet build KillerBee.slnx --configuration Release
dotnet test tests/KillerBee.Tests --configuration Release
cd frontend
npm ci
npm test
npm run build
npx playwright install chromium
cd ..
node tests/e2e.mjs
```

Fermer une démonstration déjà ouverte avant de lancer les tests E2E : ils utilisent les ports 5100, 5101, 5201 et 5202. Les tests E2E utilisent la base locale de démonstration et y créent des données fictives nommées `E2E`. Les tests unitaires utilisent des bases temporaires isolées.

GitHub Actions construit sur Linux et Windows, exécute les tests métier, construit le front, vérifie les dépendances de production et teste l’application complète dans Chromium. Les captures et journaux de démonstration sont disponibles dans l’artefact **backend-evidence** du workflow. Aucun secret de domaine n’est nécessaire à la CI.

## Pour la soutenance

Suivre [le parcours de démonstration](docs/DEMO.md). Utiliser [la matrice de couverture](docs/REQUIREMENTS.md) pour relier chaque exigence à son code et à sa preuve. Les éléments à fournir par ASD, Ramzi, Meriem et Mehdi sont explicités dans [le déploiement](docs/DEPLOYMENT.md).

Le chiffrement classique répond à l’exercice pédagogique ; la confidentialité effective du mode domaine repose sur **TLS avec certificats vérifiés**. Voir [sécurité et limites](docs/SECURITY.md).
