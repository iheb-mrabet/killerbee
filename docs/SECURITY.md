# Sécurité et limites vérifiables

## Authentification et autorisation

Le mode domaine utilise les comptes Windows existants, les groupes configurés par ASD et les droits SQL individuels provisionnés par Ramzi. Un rôle transmis librement par le navigateur ne constitue jamais une autorisation. L’application n’a ni inscription publique ni base de mots de passe métier.

Le registre partagé d’opérations contrôle les routes autorisées. Les contrôles sont répétés dans l’exécution et complétés par des restrictions sur les objets. En particulier, les listes et détails de production utilisent les vues `SCH_PROD` filtrées sur `Validated`, y compris pour un identifiant connu.

La démo utilise des sessions HTTP-only et des profils fictifs prédéfinis. Ce mode exige simultanément `Development`, une liaison loopback et des destinations loopback. Il est refusé en mode Production. Les identités internes de démo sont signées, datées, liées au chemin et au corps du message ; elles ne sont pas acceptées en mode domaine.

## Échanges chiffrés

Le chiffrement pédagogique entre middleware/plateforme et plateforme/composants comporte :

1. **Substitution affine des octets** : `y = (5x + 8) mod 256`, inversée avec `205`, inverse de `5` modulo 256.
2. **Substitution par table** : permutation des 256 octets dérivée de la clé. Le déchiffrement emploie la table inverse.
3. **Transposition par colonnes** : huit colonnes parcourues dans un ordre dérivé de la clé. La longueur est conservée ; les blocs incomplets sont réversibles.

Le traitement opère sur les octets UTF-8 et conserve accents, arabe et emoji, ce que vérifient les tests. Le Base64 sert uniquement au transport JSON du résultat binaire.

Une enveloppe authentifie version, nonce, horodatage et contenu avec HMAC-SHA256. La vérification d’intégrité précède le déchiffrement. Chaque lien utilise une clé dérivée pour sa propre finalité. Les nonces et l’horodatage sont contrôlés ; le cache antirejeu est en mémoire, par instance et sur une fenêtre limitée. Il n’assure ni déduplication globale entre VM, ni persistance après redémarrage.

**Ces transformations classiques n’offrent pas une confidentialité moderne.** L’enveloppe HMAC protège l’intégrité, pas la confidentialité. Le mode domaine exige TLS et valide les certificats pour tous les liens HTTP et SQL. Les échanges navigateur/middleware sont protégés par HTTPS ; la clé de transport serveur n’est pas embarquée dans le JavaScript.

## Contrôles applicatifs

- Validation serveur des champs, tailles, références, types, quantités et prix ; validation ergonomique dans le front.
- Requêtes paramétrées par EF Core et transactions pour les mutations métier et leur audit.
- Jeton d’antiforgery pour les opérations du navigateur, cookies SameSite, en-têtes CSP et limitation de requêtes.
- Vérification des révisions et protection contre les modifications simultanées silencieuses.
- Invalidation des approbations lors de toute modification influant sur la fabrication.
- Historique des exécutions conservé ; suppression interdite si elle détruit cet historique.
- Aucun URL de destination ni fragment SQL fourni par le client n’est exécuté.
- Vérification de l’identité SQL effective ; aucune désactivation de validation des certificats.

## Journalisation et données

L’audit en base contient acteur, opération, objet, date et identifiant de suivi pour les mutations réussies. Les rejets sont journalisés avec leur code de résultat dans les services. La traçabilité des ouvertures de session AD/Windows et la centralisation des journaux doivent être configurées par ASD.

Les recettes ne sont pas copiées dans les logs applicatifs. Les clés, certificats, bases locales et configurations privées sont ignorés par Git. Les durées de conservation, l’accès aux preuves, les sauvegardes et la minimisation des données nominatives sont à décider avec l’équipe et le cadre de l’exercice.

## Limites à présenter honnêtement

La CI vérifie la logique avec SQLite, les échanges HTTP de démonstration, les deux instances locales et le navigateur. Elle ne vérifie pas un domaine AD, les délégations Kerberos, SQL Server réel, les permissions T-SQL exécutées, les certificats de votre maquette ou le pare-feu physique.

Le workflow métier proposé et la lecture du « middleware local » sont documentés pour validation pédagogique. Les lectures réparties peuvent se replier sur une autre instance ; les écritures n’ont pas de relance automatique ni de garantie distribuée « exactement une fois ». Les listes et historiques sont bornés ; une vraie pagination et des tests de charge seraient nécessaires avant une exploitation avec de gros volumes. Les compteurs de supervision sont des indicateurs techniques de base, pas une plateforme de monitoring central déjà installée.
