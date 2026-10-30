# Référence API

Base locale : `http://localhost:5001`. Toutes les dates sont en UTC et sérialisées en
ISO 8601. Les montants sont des entiers en **FCFA**.

Chaque réponse porte un en-tête `X-Request-Id`. Un identifiant fourni par le client
(64 caractères maximum) est réutilisé ; sinon l'API en génère un.

## Erreurs

Les erreurs suivent `ProblemDetails` (RFC 9457) :

| Statut | Cas |
|---:|---|
| `400` | requête HTTP mal formée |
| `404` | ressource absente |
| `409` | doublon d'UUID, ou restauration hors fenêtre de 24 h |
| `422` | règle métier violée, JSON invalide, fichier ou agence invalide |
| `500` | erreur inattendue, sans détail technique dans la réponse |

Exemple de `422` :

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.13",
  "title": "One or more validation failures have occurred.",
  "status": 422,
  "errors": {
    "Amount": ["Le montant doit être un entier strictement positif en FCFA, sans décimales."]
  }
}
```

Le corps ne contient jamais de stack trace, de nom de table ou de message SQL.

## Transactions

### `GET /api/Transactions`

Liste paginée par curseur, triée par date décroissante.

| Paramètre | Type | Défaut | Description |
|---|---|---|---|
| `limit` | entier | 50 | 1 à 200 |
| `cursor` | chaîne | — | curseur opaque issu de `nextCursor` |
| `type` | chaîne | — | `CREDIT` ou `DEBIT` |
| `minAmount` | entier | — | montant minimum inclus |
| `maxAmount` | entier | — | montant maximum inclus |
| `q` | chaîne | — | mot recherché (255 caractères maximum) |
| `includeDeleted` | booléen | `false` | inclure les lignes supprimées |

```bash
curl "http://localhost:5001/api/Transactions?q=electricite&type=DEBIT&limit=20"
```

```json
{
  "items": [
    {
      "id": "550e8400-e29b-41d4-a716-446655440000",
      "type": "DEBIT",
      "amount": 15000,
      "description": "Facture électricité",
      "createdAtUtc": "2026-10-19T12:00:00Z",
      "agency": "Douala",
      "importId": null,
      "sourceLine": null,
      "editedAtUtc": null,
      "deletedAtUtc": null,
      "sourceFileName": null
    }
  ],
  "nextCursor": "NjM4...",
  "hasMore": false,
  "limit": 20
}
```

Pour la page suivante, renvoyer `nextCursor` tel quel dans `cursor`. Un curseur invalide
produit un `422`.

### `GET /api/Transactions/{id}`

Renvoie une transaction par son UUID (insensible à la casse). `404` si absente ou
supprimée sans `includeDeleted`.

### `POST /api/Transactions`

Crée une transaction. Le montant peut être un nombre JSON ou une chaîne de chiffres.

```json
{
  "id": "550e8400-e29b-41d4-a716-446655440000",
  "type": "CREDIT",
  "amount": 15000,
  "description": "Vente du jour",
  "agency": "Douala"
}
```

Réponse `201 Created`, `Location: /api/Transactions/{id}`, corps = identifiant sous forme
de chaîne JSON. Un UUID déjà présent renvoie `409`. L'agence est facultative, mais si elle
est fournie elle doit valoir `Douala`, `Yaoundé` ou `Bafoussam`.

### `PUT /api/Transactions/{id}`

Modifie uniquement `amount` et `description`. Le type, l'UUID, l'agence et la date de
création ne changent pas. La ligne est marquée `editedAtUtc`, ce qui la protège d'une
annulation d'import ultérieure.

```json
{ "amount": 16000, "description": "Vente du jour corrigée" }
```

Réponse `204 No Content`.

### `DELETE /api/Transactions/{id}`

Suppression logique : `deletedAtUtc` est renseigné, la ligne reste restaurable pendant
24 heures. Réponse `204`.

### `POST /api/Transactions/{id}/restore`

Restaure une ligne supprimée. `409` si la fenêtre de 24 heures est dépassée, `404` si la
ligne n'existe pas ou n'est pas supprimée.

### `GET /api/Transactions/balance`

Somme `CREDIT - DEBIT` sur les lignes vivantes.

| Paramètre | Type | Défaut | Description |
|---|---|---|---|
| `at` | ISO 8601 | maintenant | date de coupure inclusive |

```json
{
  "at": "2026-10-19T12:00:00Z",
  "credit": 250000,
  "debit": 120000,
  "balance": 130000,
  "transactionCount": 42,
  "currency": "FCFA"
}
```

### `GET /api/Transactions/export`

Export streaming de toutes les lignes correspondant aux filtres (`type`, `minAmount`,
`maxAmount`, `q`, `includeDeleted`).

| Paramètre | Valeurs | Défaut | Description |
|---|---|---|---|
| `format` | `json`, `csv` | `json` | `json` produit du NDJSON (un objet par ligne) |

L'export `json` est directement réimportable : les champs `id`, `type`, `montant`,
`description` et `agency` respectent le contrat d'import. Le total de l'export égale le
solde affiché pour le même ensemble de lignes.

## Imports

### `POST /api/Imports`

Upload `multipart/form-data` :

| Champ | Requis | Description |
|---|---|---|
| `file` | oui | fichier `.txt` ou `.ndjson` non vide |
| `agency` | non | `Douala`, `Yaoundé` ou `Bafoussam` |

Le format est détecté sur la première ligne non vide :

- **TXT** : `id;type;montant;description`, une transaction par ligne ;
- **NDJSON** : un objet JSON par ligne, avec `id`, `type`, `montant` (ou `amount`),
  `description`. Un tableau JSON unique est refusé.

```bash
curl -F "file=@/tmp/transactions.txt" -F "agency=Douala" \
  http://localhost:5001/api/Imports
```

Réponse `202 Accepted` avec le lot créé :

```json
{
  "id": "b6f1c4a0-7a3e-4f1e-9f1a-2f3c4d5e6f70",
  "fileName": "transactions.txt",
  "format": "TXT",
  "agency": "Douala",
  "status": "PENDING",
  "fileSizeBytes": 1234567,
  "totalLines": 0,
  "insertedCount": 0,
  "duplicateCount": 0,
  "errorCount": 0,
  "failureReason": null,
  "createdAtUtc": "2026-10-19T12:00:00Z",
  "startedAtUtc": null,
  "completedAtUtc": null,
  "processedLines": 0,
  "bytesProcessed": 0,
  "progressPercent": 0,
  "durationMs": null
}
```

### `GET /api/Imports`

Historique paginé par curseur (`limit` de 1 à 100, défaut 20, `cursor`).

### `GET /api/Imports/{id}`

État courant du lot. `status` vaut `PENDING`, `RUNNING`, `COMPLETED`, `FAILED` ou
`CANCELLED`. `durationMs` est la durée mesurée par le moteur.

### `GET /api/Imports/{id}/errors`

Lignes rejetées, triées par numéro de ligne. `limit` de 1 à 200 (défaut 50), `cursor` =
dernier numéro de ligne reçu.

```json
{
  "items": [
    {
      "lineNumber": 3,
      "kind": "VALIDATION",
      "reason": "Le montant doit être un entier strictement positif en FCFA, sans décimales.",
      "rawLine": "pas-un-uuid;CREDIT;15.5;Ligne cassée"
    }
  ],
  "nextCursor": null,
  "hasMore": false,
  "limit": 50
}
```

### `DELETE /api/Imports/{id}`

Annule le lot et supprime uniquement ses transactions **jamais éditées**. Les lignes
modifiées manuellement restent dans le grand livre.

Réponse `204 No Content`.

## Tableau de bord

### `GET /api/Status`

État de l'API : `status`, `utc`, `version`.

### `GET /api/Status/dashboard`

Toutes les valeurs sont calculées par des agrégats SQL, sans matérialiser les transactions.

```json
{
  "generatedAtUtc": "2026-10-19T12:00:00Z",
  "ledger": {
    "transactions": 42,
    "credits": 25,
    "debits": 17,
    "creditAmount": 250000,
    "debitAmount": 120000,
    "balance": 130000,
    "deleted": 2,
    "imported": 30,
    "manual": 12
  },
  "imports": {
    "total": 3,
    "pending": 0,
    "running": 0,
    "completed": 3,
    "failed": 0,
    "cancelled": 0,
    "totalLines": 100000,
    "processedLines": 100000,
    "inserted": 98000,
    "duplicates": 0,
    "errors": 2000,
    "lastDurationMs": 42000,
    "lastCompletedAtUtc": "2026-10-19T11:59:00Z",
    "lastFileName": "transactions.txt"
  },
  "agencies": [
    {
      "agency": "Douala",
      "transactions": 20,
      "creditAmount": 150000,
      "debitAmount": 50000,
      "balance": 100000
    },
    {
      "agency": "Yaoundé",
      "transactions": 12,
      "creditAmount": 60000,
      "debitAmount": 40000,
      "balance": 20000
    },
    {
      "agency": "Bafoussam",
      "transactions": 10,
      "creditAmount": 40000,
      "debitAmount": 30000,
      "balance": 10000
    }
  ]
}
```

Les trois agences sont toujours présentes, même sans transaction. Une ligne sans agence
apparaît dans une entrée supplémentaire `Non attribuée`.

## Contrats d'import

### TXT

```text
550e8400-e29b-41d4-a716-446655440000;CREDIT;15000;Vente du jour
550e8400-e29b-41d4-a716-446655440001;DEBIT;4500;Achat carburant
```

### NDJSON

```json
{"id":"550e8400-e29b-41d4-a716-446655440000","type":"CREDIT","montant":15000,"description":"Vente du jour"}
{"id":"550e8400-e29b-41d4-a716-446655440001","type":"DEBIT","montant":4500,"description":"Achat carburant"}
```

Règles communes :

- UUID valide, normalisé en minuscules ;
- `type` strictement `CREDIT` ou `DEBIT` ;
- montant entier strictement positif, sans décimales ;
- description non nulle, 255 caractères maximum ;
- l'agence vient du formulaire d'upload, pas du fichier ;
- une ligne invalide est rejetée et n'empêche pas l'import des autres.
