# Performance et protocole de mesure

## Principes

Le brief impose des volumes importants (jusqu'à 1 million de lignes) avec une empreinte
mémoire limitée. Les choix ci-dessous visent trois objectifs : mémoire constante, débit
d'écriture élevé et lectures sans scan complet.

### Import

| Choix | Effet |
|---|---|
| Lecture en flux par blocs de 128 Ko | le fichier n'est jamais chargé en mémoire |
| Décodage UTF-8 strict | une ligne mal encodée devient une erreur explicite, sans corruption silencieuse |
| Lots de 20 000 lignes | compromis entre débit `SqlBulkCopy` et taille des transactions |
| Table de staging + insertion ensembliste | une seule transaction SQL par lot, déduplication côté base |
| `ROW_NUMBER()` + `NOT EXISTS` | premier arrivé gagne, sans verrou applicatif global |
| Nouvelle tentative sur erreurs 2601/2627/1205 | absorbe les collisions d'UUID et les deadlocks (5 essais, backoff) |
| Progression toutes les 5 secondes | reprise après crash sans réécrire le fichier |
| `SqlBulkCopy` pour les erreurs | les rapports de lignes cassées ne ralentissent pas le flux valide |

### Recherche

La recherche n'utilise pas SQL Server Full-Text (souvent absent des instances de
démonstration). Une table `TransactionTokens` stocke chaque mot normalisé avec la date de
la transaction. La clé cluster `(Token, CreatedAtUtc, TransactionId)` permet de lire
directement les lignes du mot recherché, déjà triées par date.

La normalisation est identique à l'indexation et à la requête :

- minuscules ;
- accents supprimés ;
- découpage sur les caractères non alphanumériques ;
- déduplication ;
- troncature à la taille maximale de la colonne.

### Solde

`IX_Transactions_Balance` est un index couvrant filtré sur `DeletedAtUtc IS NULL`. Le
solde historique est une somme sur un intervalle de cet index, sans recalcul de la table.

### Export

L'export utilise des lots keyset de 5 000 lignes. Chaque lot est une requête séparée :
la mémoire reste constante et les verrous sont relâchés entre les lots. Le NDJSON et le
CSV sont écrits directement dans le flux HTTP.

### Pagination

Le curseur encode `(CreatedAtUtc, Id)`. La page suivante se positionne par rapport à la
dernière ligne reçue, sans `OFFSET`. Les imports concurrents ne créent donc ni doublon ni
saut.

### Annulation

L'annulation supprime les lignes non éditées par une seule instruction SQL, sans les
charger dans le processus.

## Ce qui est mesuré, et ce qui ne l'est pas

Les tests automatisés vérifient la **correction** (pas de perte, pas de doublon, égalité
export/solde, reprise, annulation). Ils ne constituent pas une certification de temps ou
de mémoire : ces valeurs dépendent du processeur, du disque, de la mémoire allouée à SQL
Server et de la charge de la machine.

Aucun chiffre de performance n'est présenté comme mesuré dans ce dépôt. Le protocole
ci-dessous permet de produire des chiffres reproductibles et de les joindre à une
soumission.

## Protocole de mesure

### 1. Préparer l'environnement

- SQL Server 2022 avec au moins 2 Go de RAM alloués ;
- base vide ;
- API compilée en Release ;
- machine au repos, pas d'autre import en cours.

```bash
dotnet build Mondje.slnx --configuration Release

# Lancer l'application compilée, pas le wrapper `dotnet run`, pour mesurer le bon processus
dotnet artifacts/bin/Mondje.Web/release/Mondje.Web.dll --urls http://localhost:5001 &
echo $! > /tmp/mondje-api.pid
```

Vérifier que l'API répond :

```bash
curl -s http://localhost:5001/api/Status
```

### 2. Générer un jeu de données

```bash
# 100 000 lignes valides, format TXT
python3 scripts/generate_data.py --lines 100000 --broken 0 --output /tmp/mondje-100k.txt

# 100 000 lignes dont 2 % de lignes cassées
python3 scripts/generate_data.py --lines 100000 --broken 0.02 --output /tmp/mondje-100k-2pct.txt

# 1 000 000 de lignes NDJSON
python3 scripts/generate_data.py --lines 1000000 --format ndjson --output /tmp/mondje-1m.ndjson
```

Les fichiers sont déterministes : le même `--seed` et le même nombre de lignes produisent
le même contenu.

### 3. Mesurer un import

```bash
python3 scripts/bench_import.py --file /tmp/mondje-100k.txt --agency Douala
```

Le script affiche :

- `uploadMs` : temps de transfert et de création du lot ;
- `totalMs` : de l'upload au statut terminal ;
- `serverDurationMs` : durée rapportée par le moteur ;
- `inserted`, `duplicates`, `errors` ;
- `linesPerSecond`.

Pour un import de 1 M de lignes, prévoir un délai de polling plus long :

```bash
python3 scripts/bench_import.py --file /tmp/mondje-1m.ndjson --timeout 3600
```

### 4. Mesurer la mémoire du processus

Pendant l'import, échantillonner la mémoire résidente du processus API :

```bash
API_PID=$(cat /tmp/mondje-api.pid)
: > /tmp/mondje-rss.txt
while kill -0 "$API_PID" 2>/dev/null; do
  ps -o rss= -p "$API_PID" >> /tmp/mondje-rss.txt
  sleep 1
done
```

Convertir le pic en mégaoctets :

```bash
sort -n /tmp/mondje-rss.txt | tail -1 | awk '{ printf "pic RSS : %.1f Mo\n", $1/1024 }'
```

Mesurer séparément la mémoire de SQL Server. Le seuil du brief concerne le processus
applicatif, mais SQL Server doit disposer d'assez de mémoire pour le buffer pool.

Sous Windows, utiliser Task Manager ou :

```powershell
(Get-Process Mondje.Web).PeakWorkingSet64 / 1MB
```

### 5. Mesurer la recherche

Charger au préalable un volume représentatif (100 000 à 1 000 000 de lignes), puis :

```bash
python3 scripts/bench_search.py --queries electricite ciment carburant loyer --runs 300
```

Le script effectue un échauffement, puis rapporte `p50`, `p95`, `p99`, la moyenne et le
maximum. Le seuil du brief porte sur le `p95`.

Pour comparer avec et sans index, supprimer temporairement
`IX_Transactions_CreatedAtUtc_Id` ou la table de tokens sur une copie de la base
uniquement. Ne jamais modifier la base de démonstration pendant une évaluation.

### 6. Mesurer l'export et vérifier le solde

```bash
# Temps de téléchargement complet
time curl -s -o /tmp/export.ndjson \
  "http://localhost:5001/api/Transactions/export?format=json"

# Vérifier que le total exporté égale le solde
python3 scripts/check_export_balance.py
```

Pour un filtrage identique, passer les mêmes paramètres (`q`, `type`, `at`) au contrôle et
à l'endpoint de solde.

### 7. Mesurer la reprise

1. lancer un import de 1 M de lignes ;
2. tuer brutalement le processus API (`kill -9`) pendant le traitement ;
3. relancer l'API ;
4. suivre le lot : il doit repasser `RUNNING`, reprendre à `bytesProcessed`, puis finir
   avec le bon nombre de lignes ;
5. vérifier qu'aucun UUID n'est présent deux fois.

```sql
SELECT Id, COUNT(*) AS Copies
FROM Transactions
GROUP BY Id
HAVING COUNT(*) > 1;
```

### 8. Mesurer l'annulation chirurgicale

1. importer un fichier de plusieurs lignes ;
2. modifier une ligne via `PUT /api/Transactions/{id}` ;
3. annuler l'import ;
4. vérifier que la ligne modifiée existe toujours et que les autres lignes du lot ont
   disparu.

Les tests fonctionnels automatisent ce scénario.

## Bonnes pratiques pour un résultat crédible

- indiquer le processeur, la RAM, le disque (SSD/NVMe) et la version de SQL Server ;
- préciser si SQL Server tourne dans Docker et avec quelle limite mémoire ;
- faire trois exécutions et rapporter la médiane, pas un unique essai ;
- vider le cache SQL Server entre deux essais si l'on mesure un démarrage à froid :

```sql
CHECKPOINT;
DBCC DROPCLEANBUFFERS;
```

- distinguer le temps serveur (`durationMs`) du temps perçu (file d'attente incluse) ;
- ne pas comparer un import à chaud et un import à froid ;
- conserver les fichiers générés et les commandes exactes pour la reproductibilité.
