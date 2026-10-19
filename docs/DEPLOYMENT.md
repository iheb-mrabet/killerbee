# Déploiement de la maquette Windows / Active Directory / SQL Server

## Périmètre et prérequis

Ce document décrit l’installation du code applicatif d’Iheb. Il ne prouve pas la configuration de votre domaine, des VLAN, du pare-feu ou des comptes SQL : ces environnements ne sont pas disponibles dans le dépôt ni dans la CI.

Le contrôleur de domaine **Windows Server 2012 R2** reste une contrainte du sujet portée par ASD. Les hôtes applicatifs doivent utiliser une version de Windows compatible avec .NET 10 ; Windows Server 2022/2025 sont des cibles possibles. SQL Server **Standard ou Enterprise**, choisi et administré par Ramzi, doit être compatible avec le pilote et le schéma fournis. Ne pas confondre le serveur AD imposé et les serveurs applicatifs.

| Hôte proposé | Programme | URL d’exemple |
| --- | --- | --- |
| `kb-app` | `KillerBee.Local` et front construit | `https://kb-app.killerbee.test:7440` |
| `kb-platform` | `KillerBee.Platform` | `https://kb-platform.killerbee.test:7441/platform/dispatch` |
| VM `kb-component-1` | `KillerBee.Components` | `https://kb-component-1.killerbee.test:7442/components/execute` |
| VM `kb-component-2` | Même code `KillerBee.Components` | `https://kb-component-2.killerbee.test:7442/components/execute` |
| `kb-sql` | SQL Server et base `KillerBee` | TCP 1433 ou port convenu avec Ramzi |

Ces noms et ports sont des exemples réservés à adapter à votre plan réseau. Les deux composants doivent pointer vers la même base SQL. La démo SQLite partagée sur un poste ne doit pas être copiée entre deux VM comme une solution de stockage réparti.

## Préparer les fichiers

Sur le poste de construction, installer le SDK .NET 10 et Node.js 24, puis lancer :

```powershell
powershell -File scripts/publish.ps1
```

Copier le contenu de `artifacts/Local`, `artifacts/Platform` et `artifacts/Components` sur les machines correspondantes. Copier le même paquet Components sur les deux VM. Installer le runtime ASP.NET Core 10 correspondant sur chaque hôte. Le paquet Local contient le front ; Node.js et un CDN ne sont pas nécessaires sur les serveurs pour servir l’application construite.

## Base SQL : travail à coordonner avec Ramzi

1. Ramzi crée la base selon sa stratégie de récupération et de sauvegarde, puis examine et exécute `database/schema.sql` dans cette base vide.
2. Il vérifie `database/permissions.sql` et provisionne **un login Windows individuel et son utilisateur de base pour chaque collaborateur**, avec le rôle métier adapté. Les groupes AD servent à l’autorisation applicative ; ils ne remplacent pas les utilisateurs SQL individuels imposés par l’énoncé.
3. Il conserve la séparation DBA/BCK/USR, les comptes de service SQL/Agent, les restrictions du port, la neutralisation SA et les sauvegardes dans son périmètre d’administration.
4. La connexion applicative utilise `Integrated Security=true;Encrypt=true;TrustServerCertificate=false`. Le nom DNS et le certificat SQL doivent être cohérents et approuvés par les hôtes de composants.

Le programme n’installe aucun schéma automatiquement en mode domaine et ne possède pas de compte SQL partagé. Le schéma de référence peut être régénéré, sans connexion à SQL :

```powershell
dotnet run --project src/KillerBee.Components --configuration Release -- --schema > database/generated-schema.sql
```

Le propriétaire commun des schémas/vues est nécessaire pour la chaîne de propriété de `SCH_PROD`. Les comptes production reçoivent les vues validées, sans autorisation sur les tables brutes de `SCH_RD`.

## Identité et délégation : travail avec ASD

Le chemin est **navigateur → middleware applicatif → plateforme → composants → SQL Server**. L’authentification du premier écran ne suffit pas à propager l’identité jusqu’à la base.

ASD doit joindre les hôtes au domaine, créer les groupes métier et préparer les identités de service ainsi que les SPN correspondant aux noms réellement utilisés. Le proxy HTTP applicatif effectue l’appel sous le jeton Windows de l’utilisateur ; chaque saut exige une délégation Kerberos utilisable.

Il faut configurer une délégation **contrainte aux services nécessaires**, examiner les SPN HTTP et `MSSQLSvc`, les identités de processus, la résolution DNS et la synchronisation horaire. Les comptes déclarés non déléguables ou une négociation NTLM empêchent ce parcours. Ne pas contourner ces erreurs en donnant un compte SQL partagé à l’application.

Le code contrôle le niveau de délégation du jeton avant les appels distants. À l’arrivée SQL, il compare `ORIGINAL_LOGIN()` à l’identité de l’utilisateur applicatif ; un écart provoque `SQL_IDENTITY_MISMATCH` et bloque l’opération.

Les groupes sont configurés par variables d’environnement, par exemple :

```powershell
$env:KillerBee__Groups__RD = 'KILLERBEE\GG_KB_RD'
$env:KillerBee__Groups__TEST = 'KILLERBEE\GG_KB_TEST'
$env:KillerBee__Groups__PROD = 'KILLERBEE\GG_KB_PROD'
```

## Configuration par hôte

Consulter `.env.example` pour les noms des variables. Ce fichier est une documentation, il n’est pas chargé automatiquement. Utiliser une configuration de processus/service protégée, hors du dépôt.

Commun aux programmes : `ASPNETCORE_ENVIRONMENT=Production`, `KillerBee__Demo=false`, les groupes métier, une clé `KillerBee__TransportKey` aléatoire commune aux trois couches et un endpoint HTTPS avec un certificat valide. Générer une clé de 48 octets avec un générateur cryptographique ; aucun exemple de clé utilisable n’est publié.

| Programme | Variables propres |
| --- | --- |
| Local | `ASPNETCORE_URLS`, `KillerBee__PlatformUrl` |
| Platform | `ASPNETCORE_URLS`, `KillerBee__Components__0`, `KillerBee__Components__1` |
| Components | `ASPNETCORE_URLS`, `ConnectionStrings__KillerBee`, `KillerBee__Instance` distinct sur chaque VM |

Kestrel peut charger son certificat via `Kestrel__Certificates__Default__Path` et `Kestrel__Certificates__Default__Password`. Le certificat, la clé, son mot de passe et les paramètres réels doivent rester dans un emplacement privé, accessible seulement à l’identité de service concernée. Ne pas envoyer ces fichiers sur GitHub.

Lancer chaque exécutable publié sous l’identité de service définie avec ASD. Le support du cycle de vie des services Windows est inclus via `UseWindowsService` ; la création du service et son identité sont des opérations d’administration à réaliser dans votre maquette. Fixer son répertoire et sa configuration explicitement. Le déploiement direct Kestrel/HTTPS évite de supposer qu’un reverse proxy transmet automatiquement l’identité Windows.

## Flux à transmettre à Meriem

| Source | Destination | Flux applicatif |
| --- | --- | --- |
| Postes autorisés | Local | HTTPS 7440 |
| Local | Platform | HTTPS 7441 |
| Platform | VM 1 / VM 2 | HTTPS 7442 |
| VM 1 / VM 2 | SQL Server | TCP du moteur SQL |
| Hôtes membres | AD / DNS / temps | Ports du domaine, à définir par ASD et Meriem |

La base et les composants ne sont pas destinés à un accès direct des navigateurs. Ne pas publier l’application sur Internet : le sujet exclut cet accès de l’architecture. Les ressources web sont servies par Local.

## Recette obligatoire dans la maquette

- Avec deux comptes humains distincts, exécuter `system.identity` et capturer le nom applicatif, `sqlIdentity`, `originalLogin` et l’instance. Chaque compte doit obtenir sa propre identité SQL.
- Vérifier qu’un compte sans groupe métier n’accède à aucune opération et qu’un compte Production ne lit ni prototype ni référentiel d’ingrédients.
- Vérifier les droits SQL directement sous les comptes de test, notamment l’impossibilité pour Production de lire les tables brutes.
- Répéter le parcours R&D → Tests → Production sur la vraie base, puis modifier la recette et vérifier la révocation.
- Arrêter une VM de composants **dans votre maquette de test** et vérifier qu’une lecture utilise l’autre instance. Le rééquilibrage est applicatif ; l’OS/VM reste à configurer.
- Vérifier le rejet des certificats non approuvés, l’absence de flux applicatifs en clair, les compteurs et la centralisation des journaux.
- Faire valider le mécanisme pédagogique et sa gestion des clés par Mehdi. Ramzi réalise son propre test de sauvegarde/restauration.

Sources techniques : [authentification Windows ASP.NET Core](https://learn.microsoft.com/aspnet/core/security/authentication/windowsauth?view=aspnetcore-10.0), [services Windows](https://learn.microsoft.com/aspnet/core/host-and-deploy/windows-service?view=aspnetcore-10.0), [permissions SQL](https://learn.microsoft.com/sql/t-sql/statements/grant-object-permissions-transact-sql).
