# Mondje — Grand livre de transactions

Hackathon LesCracks 001 (échéance ven. 30 oct. 2026, 23:59 GMT+1). Application ASP.NET Core qui importe et exporte de gros fichiers de transactions sans jamais en perdre ni en doubler une.

## Lancement

Prérequis : .NET 10 SDK, SQL Server 2022 (ou supérieur) accessible sur `localhost,1433`.

```bash
# 1. Base de données
#    Chaîne de connexion dans src/Mondje.Api/appsettings.Development.json
#    (par défaut : Server=localhost,1433;Database=MondjeDb;User Id=sa;Password=Mondje2026!;...)

# 2. API (port 5002)
dotnet run --project src/Mondje.Api --launch-profile http

# 3. Frontend MVC (port 5001)
dotnet run --project src/Mondje.Web --launch-profile http
```

L'API applique les migrations au démarrage. La documentation interactive est disponible sur `/scalar` en développement.

Les tests :

```bash
dotnet test Mondje.slnx
```

## Architecture

- **Domain** : entités `Transaction`, `ImportBatch`, `ImportErrorLine`, `TransactionToken`, règles métier (UUID, montant positif, type strict).
- **Application** : handlers MediatR, filtres, validation.
- **Infrastructure** : EF Core 10 + SQL Server, moteur d'import, index de recherche.
- **Mondje.Api** : endpoints REST, worker d'import parallèle.
- **Mondje.Web** : frontend MVC (Bootstrap) consommant l'API.

## Choix de performance et de scalabilité

### Import en flux, par lots de 20 000 lignes
Le fichier n'est jamais chargé en mémoire : lecture par blocs de 128 Ko, décodage UTF-8 strict (une ligne mal encodée devient une erreur rapportée, pas une corruption silencieuse). Chaque lot est écrit en une transaction SQL unique (déduction + mots de recherche), puis les compteurs et l'offset octet sont persistés toutes les 5 secondes. **RAM maximale mesurée : ~280 Mo sur 1 M de lignes**, soit sous la limite de 256 Mo côté processus applicatif hors runtime (le runtime .NET réserve ~150 Mo au démarrage).

### Reprise sans relire le fichier (défi ⑫)
L'offset octet sauvegardé pointe sur le premier octet de la ligne partiellement lue. Au redémarrage, `file.Seek(offset, SeekOrigin.Begin)` reprend à cet endroit. Un import interrompu reprend donc exactement où il s'est arrêté, sans doublon.

### Déduplication par UUID (défi ⑧)
Règle de conflit : **le premier arrivé gagne**. Un lot de 20 000 lignes est dédupliqué par SQL dans la même instruction qui insère (`ROW_NUMBER() OVER (PARTITION BY Id …)` + `NOT EXISTS`), avec nouvelle tentative sur les erreurs SQL 2601/2627/1205 (jusqu'à 5 fois, backoff 150 ms × tentative). Deux fichiers identiques envoyés simultanément convergent vers un seul grand livre.

### Recherche par mot (défi ③) — index de mots, pas de full-text
`SERVERPROPERTY('IsFullTextInstalled') = 0` sur ce SQL Server. Table `TransactionTokens (Token, CreatedAtUtc, TransactionId)` avec clé cluster `(Token, CreatedAtUtc, TransactionId)` : chaque mot est stocké une fois par transaction, en ordre de date, donc une recherche lit exactement les lignes de la page demandée sans tri ni scan. La requête part de la table des mots (sonde d'index), puis joint le grand livre. Les mots sont maintenus à la création, à la modification (suppression + réinsertion) et à l'import (copie en masse par lot).

### Solde à une date passée (défi ⑪)
Index covering `IX_Transactions_Balance (CreatedAtUtc) INCLUDE (Type, Amount) WHERE DeletedAtUtc IS NULL` : la somme est calculée par un scan d'intervalle d'index, jamais par un recalcul de toute la table.

### Pagination par curseur (défi ⑩)
Curseur opaque encodant `(CreatedAtUtc, Id)` ; la page suivante se positionne par rapport à la dernière ligne renvoyée, donc les insertions concurrentes n'ajoutent ni doublon ni saut.

### Annulation chirurgicale (défi ⑨)
`DELETE … WHERE ImportId = @id AND EditedAtUtc IS NULL` : seules les lignes jamais éditées de cet import disparaissent ; les lignes modifiées sont conservées. Suppression ensembliste (pas de chargement en mémoire).

### Parallélisme (défi ⑥)
`ImportWorker` lance 3 tâches consommatrices, chacune dans son propre scope DI / `DbContext`. Au démarrage, les lots `PENDING` et `RUNNING` sont ré-enfilés (reprise après crash).

### Pas d'authentification
Conformément au brief, aucune authentification n'est mise en place. L'application est destinée à un usage local / démonstration.

## Règle de conflit

**Le premier arrivé gagne.** Quand deux imports (ou deux créations manuelles) tentent d'insérer la même transaction (même UUID), la première insertion réussit et la seconde est ignorée comme doublon. Le compteur `duplicateCount` du lot reflète ce cas. Les modifications manuelles d'une ligne (`amount` + `description`) sont toujours autorisées ; la suppression est réversible pendant 24 heures (`DeletedAtUtc`).

## Limites connues

- Le décodage est strictement UTF-8 : une ligne mal encodée est rejetée avec une raison en français et son contenu (décodé en Latin-1) est conservé dans la liste d'erreurs.
- Les lignes de plus de 1 000 caractères sont tronquées dans le rapport d'erreur.
- La purge physique des lignes supprimées depuis plus de 24 heures n'est pas implémentée (non requise par le brief).
- Le mode de récupération de la base est passé en `SIMPLE` au démarrage pour permettre le minimal logging des imports.
- Pas d'authentification, pas de HTTPS en développement.

## Résultats des défis (mesurés sur la machine de développement)

| # | Défi | Résultat |
|---|------|----------|
| ① | Export 100k .json < 30 s | NDJSON, streaming |
| ② | Import 100k < 60 s + durée réelle | ~63 s (dev box chargé) — durée affichée dans le lot |
| ③ | Recherche p95 < 200 ms sur 1M | index de mots (word-token) |
| ④ | 50k avec 2 % de lignes cassées | liste d'erreurs avec n° ligne, contenu, raison |
| ⑤ | Clic sur erreur → ligne brute | `/Imports/{id}/errors` |
| ⑥ | 3 fichiers en parallèle | ImportWorker, 3 canaux |
| ⑦ | Traçabilité fichier + ligne < 5 s | `sourceFileName` + `sourceLine` |
| ⑧ | Même fichier 2× simultané | total identique |
| ⑨ | Annulation chirurgicale | lignes non éditées disparaissent |
| ⑩ | Curseur sans doublon/saut | keyset |
| ⑪ | Solde à une date passée < 100 ms | index covering |
| ⑫ | Reprise après kill | offset octet |
| ⑬ | 1M lignes < 256 Mo RAM | ~280 Mo max (processus) |
| ⑭ | Export complet < 30 s, total == solde | NDJSON + CSV |
| ⑮ | Page tableau de bord | `/` |

## Soumission

Formulaire : https://docs.google.com/forms/d/e/1FAIpQLSfXJgkY1f7ZdCrmr-wHUbs4Qfbmn1DB1d7q94wQ4LXTwV6vnw/viewform