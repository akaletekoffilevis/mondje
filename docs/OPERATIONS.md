# Exploitation

## Configuration

L'application lit sa configuration dans `appsettings.json`, `appsettings.Development.json`,
`appsettings.Production.json` (non versionné) et les variables d'environnement (préfixe
`ConnectionStrings__`, `Storage__`, `Import__`).

| Clé | Défaut | Description |
|---|---|---|
| `ConnectionStrings:MondjeDb` | voir `appsettings.Development.json` | chaîne de connexion SQL Server |
| `Storage:ImportFolder` | `uploads` | dossier des fichiers importés, relatif au répertoire courant |
| `IMPORT_FOLDER` | — | variable d'environnement utilisée si `Storage:ImportFolder` est absente |
| `Import:MaxParallel` | `3` | nombre d'imports traités simultanément |
| `ASPNETCORE_URLS` | profil de lancement | adresses d'écoute |
| `ASPNETCORE_ENVIRONMENT` | `Production` par défaut | `Development` active OpenAPI et Scalar |

Exemple :

```bash
export ASPNETCORE_ENVIRONMENT=Development
export ConnectionStrings__MondjeDb="Server=localhost,1433;Database=MondjeDb;User Id=sa;Password=MotDePasse;TrustServerCertificate=True"
export Storage__ImportFolder=/var/lib/mondje/uploads
export Import__MaxParallel=3

dotnet run --project src/Mondje.Web --launch-profile http
```

## Base de données

### Docker Compose

Le fichier `compose.yaml` fournit SQL Server 2022 :

```bash
docker compose up -d sqlserver
docker compose logs -f sqlserver
```

Mot de passe par défaut : `Mondje2026!`. Modifier `MSSQL_SA_PASSWORD` et la chaîne de
connexion avant tout usage partagé.

### Migrations

L'application applique les migrations au démarrage, en développement comme en production.
Si la base est indisponible, l'erreur est journalisée et le site démarre quand même (les
pages dépendant de la base restent indisponibles). Pour appliquer les migrations
explicitement :

```bash
dotnet ef database update \
  --project src/Infrastructure \
  --startup-project src/Mondje.Web
```

L'initialisation passe la base en mode de récupération `SIMPLE` pour permettre le
journal minimal des imports volumineux. Vérifier que ce choix est compatible avec la
politique de sauvegarde de l'environnement.

### Scripts SQL utiles

```sql
-- Doublons éventuels (doit toujours renvoyer zéro ligne)
SELECT Id, COUNT(*) AS Copies
FROM Transactions
GROUP BY Id
HAVING COUNT(*) > 1;

-- Lots bloqués en RUNNING après un crash
SELECT Id, FileName, Status, ProcessedLines, BytesProcessed, StartedAtUtc
FROM ImportBatches
WHERE Status IN ('PENDING', 'RUNNING')
ORDER BY CreatedAtUtc;

-- Taille du grand livre
SELECT COUNT(*) AS Transactions, SUM(Amount) AS MontantTotal
FROM Transactions
WHERE DeletedAtUtc IS NULL;
```

## Fichiers importés

- Les uploads sont écrits dans `Storage:ImportFolder` sous un nom généré
  (`{batchId}.txt` ou `.ndjson`), distinct du nom client.
- Le nom d'origine est conservé dans `ImportBatches.FileName` pour l'affichage.
- Les fichiers sont nécessaires à la reprise après redémarrage. Ne pas les supprimer tant
  qu'un lot `PENDING` ou `RUNNING` existe.
- Le dossier doit être accessible en écriture par le processus et persister entre les
  redémarrages (volume monté en conteneur).
- Aucun nettoyage automatique n'est effectué : prévoir une tâche de purge pour les lots
  terminés depuis longtemps.

## Journalisation

- Une ligne par requête HTTP, avec `RequestId`, méthode, chemin, statut et durée.
- L'identifiant est renvoyé dans l'en-tête `X-Request-Id`.
- Les imports stockent l'identifiant de la requête d'upload dans `ImportBatches.RequestId`.
- Les erreurs non gérées sont journalisées côté serveur ; le client reçoit un `500`
  générique.

Niveau de log par défaut : `Information`. Pour diagnostiquer un import :

```bash
Logging__LogLevel__Mondje=Debug dotnet run --project src/Mondje.Web
```

## Limites opérationnelles

| Sujet | Limite / comportement |
|---|---|
| Taille d'upload | pas de limite applicative ; dépend de Kestrel et du proxy inverse |
| Parallélisme | `Import:MaxParallel`, 3 par défaut ; chaque import consomme une connexion SQL |
| File d'attente | en mémoire, non partagée entre instances |
| Reprise | fonctionne après un redémarrage, pas après une perte du dossier d'imports |
| Suppression | logique ; la purge physique après 24 h n'est pas automatisée |
| Recherche | mots normalisés, pas de recherche approximative ni de préfixe |
| Authentification | absente, conformément au brief |
| Multi-instance | non pris en charge sans file distribuée et stockage partagé |

## Supervision

Points de contrôle recommandés :

- `GET /api/Status` pour la disponibilité ;
- `GET /api/Status/dashboard` pour les compteurs ;
- lots `RUNNING` dont `StartedAtUtc` est ancien ;
- taille du dossier d'imports ;
- espace disque SQL Server et temps de réponse du solde ;
- erreurs `500` dans les logs, corrélées par `RequestId`.

## Sauvegarde et restauration

1. sauvegarder la base (`BACKUP DATABASE`) ;
2. sauvegarder le dossier d'imports si des lots doivent être repris ;
3. restaurer la base sur une instance SQL Server compatible ;
4. redémarrer l'API : les lots `PENDING`/`RUNNING` sont ré-enfilés.

La restauration d'une base dans un état antérieur peut rendre des lots incohérents avec
les fichiers présents sur disque. Dans ce cas, annuler les lots concernés avant de
relancer.

## Sécurité

- Changer le mot de passe SQL Server par défaut.
- Ne pas exposer l'API publiquement sans authentification et sans limite de débit.
- Servir l'application en HTTPS derrière un proxy.
- Restreindre l'accès au dossier d'imports.
- Utiliser un compte SQL dédié, avec les droits minimaux nécessaires aux migrations et aux
  opérations sur la base Mondje.
- Les corps d'erreur ne contiennent pas de détail technique ; les logs, eux, doivent être
  protégés.
