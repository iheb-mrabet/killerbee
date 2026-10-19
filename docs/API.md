# Contrat des API

## API du front

`GET /api/session` retourne l’utilisateur courant, ses rôles, le mode démo et un jeton d’antiforgery. Toutes les opérations passent ensuite par **`POST /api/operations`**, avec le cookie de session/les identifiants Windows et l’en-tête `X-CSRF-TOKEN` obtenu pour cet utilisateur.

```json
{
  "requestId": "27518595-2e52-4f72-b8e2-b01cf71cae4f",
  "client": { "kind": "killerbee-web", "version": "1.0" },
  "operation": "models.list",
  "payload": { "search": "Competition", "range": "Sport" }
}
```

Réponse : `status` (`success`/`failure`), `code`, `message`, `data`, `correlationId` et éventuellement `errors` par champ. L’interface affiche l’état de traitement pendant les appels. Les opérations actuelles sont synchrones ; aucun faux suivi de tâche asynchrone n’est renvoyé.

## Opérations

| Opération | Données attendues | Services autorisés |
| --- | --- | --- |
| `dashboard.read` | `{}` | RD, TEST, PROD |
| `models.list` | `search?`, `range?` | RD, TEST, PROD |
| `models.get` | `id` | RD, TEST, PROD |
| `models.save` | `id?`, `revision`, `name`, `description`, `unitPriceCents`, `range`, `ingredients`, `characteristics` | RD |
| `models.delete`, `models.submit` | `id`, `revision` | RD |
| `ingredients.list` | `search?` | RD, TEST |
| `ingredients.save` | `id?`, `revision`, `name`, `description` | RD |
| `ingredients.delete` | `id`, `revision` | RD |
| `characteristics.list` | `modelId?` | RD, TEST |
| `characteristics.save` | `id?`, `revision`, `name`, `kind`, `unit`, `required`, `modelId?` | RD |
| `characteristics.delete` | `id`, `revision` | RD |
| `processes.list` | `search?`, `modelId?` | RD, TEST, PROD |
| `processes.get` | `id` | RD, TEST, PROD |
| `processes.save` | `id?`, `revision`, `modelId`, `name`, `description`, `steps` | RD |
| `processes.delete` | `id`, `revision` | RD |
| `tests.list` | `search?`, `modelId?` | RD, TEST |
| `tests.save` | `id?`, `revision`, `processId`, `stepId?`, `name`, `description`, `expectedResult` | RD |
| `tests.delete` | `id`, `revision` | RD |
| `tests.execute` | `testId`, `outcome`, `observations` | TEST |
| `tests.validate` | `executionId`, `approved`, `comment` | TEST |
| `audit.list` | `{}` | RD |
| `system.identity` | `{}` | RD, TEST, PROD |

`id?` est null/absent à la création ; `revision` est la révision reçue à la lecture pour les modifications/suppressions. La suppression est bloquée pour les références encore utilisées. Un test ayant des exécutions ne peut être supprimé : son historique est conservé.

Une composition est une liste de `{ "ingredientId": "GUID", "quantityCg": 12537 }`. Le même ingrédient ne peut y apparaître deux fois. La limite est de 100 lignes et de 100 000 000 centigrammes par ligne.

`characteristics` est un objet dont les clés sont les GUID des définitions et les valeurs des chaînes. `kind` vaut `text`, `number` ou `boolean`. Une valeur numérique utilise un point décimal ; un booléen vaut `"true"` ou `"false"`. `modelId = null` rend la définition commune à tous les modèles. Les champs inconnus/non applicables sont refusés.

`steps` est une liste de `{ "id": null, "name": "Peser", "description": "..." }`, dont l’ordre donne les positions. Une étape existante garde son identifiant lors d’un réordonnancement. La limite est de 100 étapes.

`outcome` vaut `Passed` ou `Failed`. Une exécution appartient à la révision courante du modèle et commence à l’état `Pending`. La décision devient `Approved` ou `Rejected`. Seule la dernière exécution courante d’un test peut être examinée. Un résultat échoué ne peut être approuvé.

Les listes renvoient au plus 500 éléments ; rechercher plus précisément si nécessaire. `tests.list` inclut au plus 30 exécutions par test ; l’audit montre les 200 dernières mutations. Ces limites conviennent au jeu pédagogique ; une pagination complète est à prévoir avant une utilisation avec de gros volumes.

## Codes d’erreur

| HTTP | Exemple de code | Signification |
| --- | --- | --- |
| 400 | `VALIDATION_FAILED`, `CLIENT_UNSUPPORTED`, `REQUEST_INVALID` | Données, version ou CSRF invalides |
| 401 | `AUTH_REQUIRED` | Session nécessaire |
| 403 | `ACCESS_DENIED`, `DELEGATION_REQUIRED`, `SQL_IDENTITY_MISMATCH` | Droit métier ou identité déléguée insuffisante |
| 404 | `NOT_FOUND`, `OPERATION_UNKNOWN` | Objet non visible/inexistant ou opération absente |
| 409 | `REVISION_CONFLICT`, `REFERENCED`, `STALE_TEST`, `HISTORY_PROTECTED` | Conflit métier ; relire les données avant d’agir |
| 429 | — | Limite de requêtes atteinte |
| 502/503 | `UPSTREAM_AUTH_OR_TRANSPORT`, `SERVICE_UNAVAILABLE` | Service distant inaccessible ou authentification interserveurs incorrecte |

Après une erreur de transport sur une écriture, consulter l’état ou l’historique avant de renouveler l’action. Il n’existe pas de garantie distribuée « exactement une fois ».

## Contrats internes

La plateforme utilise `/platform/dispatch`, les instances `/components/execute`. Le corps est une enveloppe `{ version, nonce, issuedAt, payload, tag }`. `payload` contient le contrat chiffré ; `tag` authentifie son intégrité. Les deux sauts utilisent des dérivations de clé distinctes. La réponse interne contient `{ httpStatus, reply }` dans une enveloppe chiffrée.

Le front ne choisit jamais une URL de destination. Le registre fermé dans `Operations.Permissions` fixe les opérations et rôles ; le contrôleur d’exécution applique également ses restrictions sur les données.

En démo uniquement : `POST /auth/demo-login` avec `{ "role": "RD" }` et CSRF ; `POST /auth/logout` avec CSRF. En mode domaine : `GET /auth/windows` déclenche l’authentification du navigateur. Les comptes et groupes sont administrés dans AD.
