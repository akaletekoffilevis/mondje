# Architecture

## Vue d'ensemble

Mondje est une solution .NET 10 organisée en couches. Les dépendances pointent vers
l'intérieur : `Domain` ne dépend de rien, `Application` dépend de `Domain`, `Infrastructure`
implémente les interfaces de `Application`, et `Mondje.Web` compose le tout dans un seul
site exécutable (interface MVC + API Minimal).

```text
┌────────────────────────────────────────────────────┐
│                    Mondje.Web                      │
│  MVC / Razor  ──▶  MondjeApiClient  ──▶  /api/...  │
│  Minimal API, worker d'import, middlewares         │
└─────────────────────────┬──────────────────────────┘
                          │ MediatR
                ┌─────────▼──────────┐
                │    Application     │
                │ commandes/queries  │
                │ validation         │
                └─────────┬──────────┘
                          │ interfaces
                ┌─────────▼──────────┐
                │   Infrastructure   │
                │ EF Core, SQL Server│
                │ moteur d'import    │
                └─────────┬──────────┘
                          │
                ┌─────────▼──────────┐
                │       Domain       │
                │ entités, règles    │
                └────────────────────┘
```

## Projets

| Projet | Rôle |
|---|---|
| `src/Domain` | entités `Transaction`, `ImportBatch`, `ImportErrorLine`, `TransactionToken`, constantes d'agence, normalisation `SearchTokens` |
| `src/Application` | requêtes et commandes MediatR, DTO, validateurs FluentValidation, interfaces de persistance |
| `src/Infrastructure` | `ApplicationDbContext`, configurations EF Core, migrations, `ImportEngine`, `ImportStorage` |
| `src/Mondje.Web` | site unique : endpoints REST (`Api/`), middleware d'identifiant de requête, gestion globale des erreurs, file et worker d'import, contrôleurs MVC et vues Razor qui consomment l'API via `MondjeApiClient` |
| `src/Shared` | types partagés entre l'API et le frontend |

## Modèle de données

### `Transactions`

| Colonne | Type | Rôle |
|---|---|---|
| `Id` | `char(36)` | UUID fourni par le client, normalisé en minuscules |
| `Type` | `varchar(6)` | `CREDIT` ou `DEBIT` |
| `Amount` | `bigint` | montant entier en FCFA, strictement positif |
| `Description` | `nvarchar(255)` | libellé |
| `CreatedAtUtc` | `datetime2(3)` | date serveur, jamais fournie par le client |
| `Agency` | `nvarchar(32)` | agence ou `NULL` |
| `ImportId` | `uniqueidentifier` | lot d'import d'origine |
| `SourceLine` | `int` | numéro de ligne dans le fichier source |
| `EditedAtUtc` | `datetime2(3)` | marque une modification manuelle |
| `DeletedAtUtc` | `datetime2(3)` | suppression réversible |

Contraintes SQL :

- clé primaire sur `Id` ;
- `CK_Transactions_IdLower` : `Id = LOWER(Id)` ;
- `CK_Transactions_Type` : type limité à `CREDIT`/`DEBIT` ;
- `CK_Transactions_Amount` : montant `> 0` ;
- `CK_Transactions_Description` : longueur `<= 255`.

Index :

- `IX_Transactions_CreatedAtUtc_Id` : pagination keyset et export, filtré sur les lignes
  vivantes ;
- `IX_Transactions_Balance` : index couvrant `(CreatedAtUtc) INCLUDE (Type, Amount)`,
  filtré sur les lignes vivantes, pour le solde historique ;
- `IX_Transactions_ImportId` : traçabilité et annulation d'import.

### `ImportBatches`

État durable d'un import : nom du fichier d'origine, nom du fichier stocké, format,
agence, statut (`PENDING`, `RUNNING`, `COMPLETED`, `FAILED`, `CANCELLED`), compteurs,
offset octet, ligne traitée, identifiant de requête, dates. C'est cette ligne qui permet
la reprise après redémarrage.

### `ImportErrorLines`

Une ligne par rejet : numéro de ligne, catégorie (`FORMAT` ou `VALIDATION`), raison en
français, contenu brut tronqué à 1 000 caractères. Les lignes invalides n'interrompent
jamais l'import des lignes valides.

### `TransactionTokens`

Index de mots : `(Token, CreatedAtUtc, TransactionId)` en clé cluster. Chaque mot normalisé
d'une description est stocké une fois par transaction. La recherche part de cet index,
ce qui évite un scan de `Transactions`.

## Flux d'un import

```text
Upload HTTP
   │
   ├─ validation du fichier et de l'agence (422 sinon)
   ├─ détection TXT / NDJSON sur la première ligne non vide
   ├─ écriture du fichier dans le dossier d'import
   ├─ création du lot PENDING
   └─ enqueue du lot (Channel)  ──▶ 202 Accepted + ImportDto

ImportWorker (3 consommateurs par défaut)
   │
   ├─ scope DI dédié + DbContext dédié
   ├─ ProcessImportCommand
   │    ├─ passage RUNNING, StartedAtUtc
   │    ├─ reprise éventuelle à BytesProcessed
   │    ├─ lecture en flux (blocs de 128 Ko)
   │    ├─ parsing TXT ou NDJSON
   │    ├─ validation ligne par ligne
   │    ├─ lots de 20 000 lignes
   │    │    ├─ staging + SqlBulkCopy
   │    │    ├─ déduplication UUID et insertion ensembliste
   │    │    └─ alimentation de TransactionTokens
   │    ├─ flush des erreurs
   │    ├─ sauvegarde de la progression toutes les 5 s
   │    └─ statut COMPLETED / FAILED / CANCELLED
   └─ libération du scope
```

### Reprise

`BytesProcessed` est enregistré après chaque lot et périodiquement. Au redémarrage,
`ImportWorker` recharge les lots `PENDING` et `RUNNING`, puis `ImportEngine` se positionne
à l'offset sauvegardé. L'offset correspond au début de la première ligne non encore
validée, ce qui évite à la fois la relecture du début et les doublons.

### Annulation

`DELETE /api/Imports/{id}` :

1. passe le lot en `CANCELLED` ;
2. supprime les transactions du lot dont `EditedAtUtc IS NULL` ;
3. conserve les lignes corrigées manuellement ;
4. si le moteur n'a pas encore commencé, `ProcessAsync` s'arrête immédiatement ;
5. si le moteur tourne, il vérifie le statut entre les lots et s'arrête au point sûr
   suivant.

### Parallélisme

`ImportWorker` lance `Import:MaxParallel` consommateurs (3 par défaut). Chaque import
possède son propre scope DI, donc son propre `DbContext` : aucun état EF Core n'est partagé
entre deux imports. La file est un `Channel<Guid>` non borné, ce qui ne bloque pas les
uploads.

## Flux de lecture

### Liste et pagination

`GET /api/Transactions` applique les filtres (type, montants, recherche), puis un ordre
`CreatedAtUtc DESC, Id DESC`. La page suivante utilise un curseur opaque encodant la
position de la dernière ligne. Une insertion concurrente ne décale donc pas les pages
suivantes.

### Recherche

`SearchTokens` normalise la casse, supprime les accents, découpe sur tout caractère non
alphanumérique, déduplique et tronque à la taille de colonne. La même fonction est utilisée
pour indexer les descriptions et pour analyser la requête, ce qui garantit un traitement
symétrique.

### Solde et export

Le solde et l'export utilisent le même ensemble de lignes : `DeletedAtUtc IS NULL`, avec
un filtre `CreatedAtUtc <= at` optionnel. C'est ce qui garantit l'égalité entre le total
exporté et le solde affiché.

## API et erreurs

- Le pipeline MediatR exécute les validateurs FluentValidation avant les handlers.
- `ValidationException` devient un `422` `ProblemDetails` avec les messages par champ.
- `NotFoundException` devient `404`, `ConflictException` devient `409`.
- Un corps JSON mal formé devient `422` avec une explication en français.
- Toute autre exception devient un `500` générique ; le détail reste dans les logs.
- `RequestIdMiddleware` réutilise un `X-Request-Id` client valide (64 caractères maximum),
  sinon en génère un, l'ajoute à l'en-tête de réponse, au `TraceIdentifier` et à la ligne de
  log de la requête.

## Frontend

`Mondje.Web` est une application MVC qui appelle l'API avec `HttpClient`. Le contrôleur
`HomeController` charge le tableau de bord, les derniers imports et les dernières
transactions. La page d'import permet de choisir l'agence, de suivre la progression et de
consulter les erreurs ligne par ligne. Aucun accès direct à la base n'est effectué depuis le
frontend.

## Stratégie de test

| Niveau | Ce qui est testé |
|---|---|
| Domaine | normalisation de recherche, constantes, contrats |
| Application | validateurs, format UUID, montants JSON, curseurs, horodatage |
| Infrastructure | stockage des fichiers, précédence de configuration |
| Fonctionnel | API réelle sur SQL Server réel : CRUD, imports, reprise, annulation, recherche, export, tableau de bord |

Les tests fonctionnels utilisent `WebApplicationFactory`, une base `MondjeDb_Test` et
`Respawn` pour repartir d'un état propre entre les tests. Ils ne mockent ni EF Core ni SQL
Server, afin de vérifier les contraintes, les index et la concurrence réelles.
