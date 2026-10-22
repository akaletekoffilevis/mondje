# Mondje — grand livre de transactions

[![build](https://github.com/akaletekoffilevis/mondje/actions/workflows/build.yml/badge.svg)](https://github.com/akaletekoffilevis/mondje/actions/workflows/build.yml)
[![unit-tests](https://github.com/akaletekoffilevis/mondje/actions/workflows/unit-tests.yml/badge.svg)](https://github.com/akaletekoffilevis/mondje/actions/workflows/unit-tests.yml)
[![functional-tests](https://github.com/akaletekoffilevis/mondje/actions/workflows/functional-tests.yml/badge.svg)](https://github.com/akaletekoffilevis/mondje/actions/workflows/functional-tests.yml)
[![codeql](https://github.com/akaletekoffilevis/mondje/actions/workflows/codeql.yml/badge.svg)](https://github.com/akaletekoffilevis/mondje/actions/workflows/codeql.yml)

Application de grand livre pour la coopérative **Mondje**, réalisée pour le hackathon
**LesCracks 001**. Elle importe de gros volumes de transactions, les expose via une API
REST et un tableau de bord web, et garantit qu'aucune ligne n'est perdue ni comptée deux
fois.

Le projet couvre les quinze défis du brief : import TXT/NDJSON, export complet, recherche
par mot, pagination par curseur, solde historique, reprise après interruption, annulation
chirurgicale, imports parallèles, traçabilité fichier/ligne et tableau de bord.

## Sommaire

- [Démarrage rapide](#démarrage-rapide)
- [Fonctionnalités](#fonctionnalités)
- [Règles du grand livre](#règles-du-grand-livre)
- [Architecture](#architecture)
- [Choix de performance](#choix-de-performance)
- [Conflits de fichiers](#conflits-de-fichiers)
- [Tests et intégration continue](#tests-et-intégration-continue)
- [Reproduire les mesures](#reproduire-les-mesures)
- [Limites connues](#limites-connues)
- [Résultats des défis](#résultats-des-défis)
- [Documentation](#documentation)

## Démarrage rapide

Prérequis :

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- SQL Server 2022 (local, Docker, ou instance distante)

```bash
# 1. Base de données locale (Docker)
docker compose up -d sqlserver

# 2. API — http://localhost:5002
dotnet run --project src/Mondje.Api --launch-profile http

# 3. Interface web — http://localhost:5001
dotnet run --project src/Mondje.Web --launch-profile http
```

L'API applique automatiquement les migrations EF Core au démarrage en développement.
La référence OpenAPI est disponible sur `http://localhost:5002/openapi/v1.json` et
l'interface Scalar sur `http://localhost:5002/scalar`.

La chaîne de connexion par défaut se trouve dans
`src/Mondje.Api/appsettings.Development.json`. Pour utiliser une autre instance :

```bash
export ConnectionStrings__MondjeDb="Server=localhost,1433;Database=MondjeDb;User Id=sa;Password=MonMotDePasse;TrustServerCertificate=True"
```

## Fonctionnalités

- **Import asynchrone** de fichiers `.txt` (séparateur `;`) et `.ndjson`, avec progression,
  durée, compteurs d'insertions, doublons et erreurs.
- **Export streaming** en NDJSON (`.json`) ou CSV, sans charger le grand livre en mémoire.
- **Recherche par mot** insensible à la casse et aux accents, combinable avec le type, les
  montants et la pagination.
- **CRUD complet** : création, lecture, modification du montant et de la description,
  suppression réversible pendant 24 heures, restauration.
- **Imports parallèles** (3 par défaut, configurable), reprise après redémarrage,
  annulation chirurgicale.
- **Tableau de bord** : totaux du grand livre, résultat des imports et ventilation par
  agence (Douala, Yaoundé, Bafoussam).
- **Traçabilité** : chaque transaction importée conserve son fichier et son numéro de ligne.
- **Identifiant de requête** dans les logs et dans l'en-tête `X-Request-Id`.

## Règles du grand livre

| Règle | Implémentation |
|---|---|
| UUID unique, insensible à la casse | colonne `Id` en `char(36)` avec contrainte d'unicité et `CHECK (Id = LOWER(Id))` ; toute valeur est normalisée en minuscules |
| Montant entier strictement positif en FCFA | validation à la création, à la modification et à l'import ; les décimales, zéros et négatifs sont refusés |
| Erreur métier = `422` avec raison, sans trace technique | `ValidationException` et corps JSON invalide convertis en `ProblemDetails` ; le détail technique reste dans les logs |
| Listes paginées | curseur opaque `(CreatedAtUtc, Id)` ; aucune duplication ni omission pendant un import concurrent |
| Identifiant de requête dans les logs | middleware dédié, réutilisation d'un `X-Request-Id` client valide |
| Suppression réversible 24 h | `DeletedAtUtc`, endpoints `DELETE` puis `POST /restore` |
| Import TXT et NDJSON | détection automatique du format, une erreur par ligne refusée |

## Architecture

Solution .NET en couches, inspirée de la Clean Architecture :

```text
src/
  Domain/          entités, constantes, normalisation de recherche
  Application/     MediatR, validation FluentValidation, requêtes et commandes
  Infrastructure/  EF Core 10, SQL Server, moteur d'import, stockage des fichiers
  Mondje.Api/      API REST minimale, worker d'import, gestion des erreurs
  Mondje.Web/      interface MVC (Razor + Bootstrap) consommant l'API
tests/
  Domain.UnitTests/
  Application.UnitTests/
  Infrastructure.IntegrationTests/
  Application.FunctionalTests/   API réelle + SQL Server réel
```

Détails : [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Choix de performance

- **Import en flux** : lecture par blocs de 128 Ko, lots de 20 000 lignes, `SqlBulkCopy`
  vers une table de staging puis insertion ensembliste. Le fichier n'est jamais chargé
  entièrement.
- **Reprise sans relecture** : l'offset octet et la ligne courante sont enregistrés toutes
  les 5 secondes ; au redémarrage, le moteur reprend exactement à cet octet.
- **Déduplication par UUID** : `NOT EXISTS` + `ROW_NUMBER()` dans la transaction d'insertion,
  avec nouvelle tentative sur les erreurs SQL de concurrence.
- **Recherche sans full-text** : table `TransactionTokens` avec clé cluster
  `(Token, CreatedAtUtc, TransactionId)`. La recherche sonde l'index des mots puis joint le
  grand livre, sans tri ni scan complet.
- **Index couvrant pour le solde** : `IX_Transactions_Balance (CreatedAtUtc) INCLUDE
  (Type, Amount) WHERE DeletedAtUtc IS NULL`. Le solde est une somme sur un intervalle
  d'index.
- **Export par lots de 5 000** : re-requêtes keyset, mémoire constante et verrous relâchés
  entre les lots.
- **Annulation ensembliste** : `DELETE … WHERE ImportId = @id AND EditedAtUtc IS NULL`,
  sans chargement des lignes concernées dans le processus.
- **Parallélisme** : chaque import tourne dans son propre scope DI et son propre
  `DbContext`, ce qui évite tout partage de contexte EF Core.

Détails et protocole de mesure : [docs/PERFORMANCE.md](docs/PERFORMANCE.md).

## Conflits de fichiers

La règle appliquée est **le premier arrivé gagne**.

Lorsque deux imports, ou une création manuelle et un import, présentent le même UUID :

1. la première insertion valide est conservée ;
2. la suivante est comptée comme **doublon** (`duplicateCount`) et n'altère pas la ligne
   existante ;
3. le total du grand livre reste identique, même si deux fichiers identiques sont envoyés
   simultanément ;
4. une modification manuelle reste toujours possible et marque la ligne comme éditée, ce
   qui la protège d'une annulation d'import ultérieure.

## Tests et intégration continue

```bash
# Tout sauf les tests fonctionnels (pas besoin de SQL Server)
./scripts/verify.sh

# Tests fonctionnels : API réelle + SQL Server réel
docker compose up -d sqlserver
export ConnectionStrings__MondjeDb="Server=localhost,1433;Database=MondjeDb_Test;User Id=sa;Password=Mondje2026!;TrustServerCertificate=True"
dotnet test tests/Application.FunctionalTests/Application.FunctionalTests.csproj
```

La suite comporte **165 tests** :

| Projet | Tests | Portée |
|---|---:|---|
| `Domain.UnitTests` | 22 | normalisation, constantes, contrats du domaine |
| `Application.UnitTests` | 92 | UUID, montants, curseurs, horodatage, validateurs |
| `Infrastructure.IntegrationTests` | 4 | stockage et précédence de configuration des imports |
| `Application.FunctionalTests` | 47 | API, SQL Server, imports, recherche, export, tableau de bord |

Les workflows GitHub Actions s'exécutent à chaque push et à chaque pull request :

| Workflow | Rôle |
|---|---|
| `build.yml` | restauration et compilation Release, warnings traités comme erreurs |
| `unit-tests.yml` | tests domaine, Application et Infrastructure |
| `functional-tests.yml` | SQL Server 2022 en service, tests fonctionnels complets |
| `codeql.yml` | analyse statique de sécurité C# |

## Reproduire les mesures

Les scripts sont déterministes et n'utilisent que la bibliothèque standard Python :

```bash
# 1. Générer 100 000 lignes, dont 2 % de lignes invalides
python3 scripts/generate_data.py --lines 100000 --broken 0.02 --output /tmp/mondje-100k.txt

# 2. Mesurer un import de bout en bout
python3 scripts/bench_import.py --file /tmp/mondje-100k.txt --agency Douala

# 3. Mesurer la recherche (p50, p95, p99)
python3 scripts/bench_search.py --queries electricite ciment carburant --runs 300

# 4. Vérifier que l'export complet égale le solde affiché
python3 scripts/check_export_balance.py
```

Les résultats dépendent de la machine, de la mémoire allouée à SQL Server et du disque.
Le protocole complet, y compris la mesure de la mémoire du processus, est décrit dans
[docs/PERFORMANCE.md](docs/PERFORMANCE.md).

## Limites connues

- **File d'import en mémoire** : la file `Channel<Guid>` n'est pas distribuée. Plusieurs
  instances de l'API ne partagent pas la même file ; la reprise au démarrage fonctionne,
  mais un déploiement multi-instance demanderait une file externe.
- **Purge physique** : les lignes supprimées restent en base avec `DeletedAtUtc`. La purge
  après 24 heures n'est pas automatisée (les endpoints de lecture les excluent déjà).
- **Pas d'authentification** : conformément au brief, l'application est prévue pour une
  démonstration locale.
- **Pas de limite de taille d'upload** dans l'application ; la limite effective est celle
  de l'hébergement (Kestrel, proxy). Un fichier volumineux doit être découpé si le proxy
  impose une limite.
- **UTF-8 strict** : une ligne mal encodée est rejetée avec une raison explicite ; le
  contenu brut est conservé en Latin-1 pour l'affichage.
- **Rapport d'erreur tronqué** : les lignes brutes de plus de 1 000 caractères sont
  tronquées dans le rapport.
- **SQL Server requis** : aucun fournisseur de base en mémoire n'est utilisé pour les tests
  fonctionnels, afin de tester les vrais index et la vraie concurrence.
- **HTTPS** : le profil de développement HTTP est utilisé pour la démonstration ; activer
  le profil HTTPS et un certificat en production.

## Résultats des défis

Les fonctionnalités sont implémentées et couvertes par des tests automatisés. Les seuils de
temps et de mémoire ne sont pas certifiés sur toutes les machines : les scripts fournis
permettent de les mesurer dans les conditions du jury. Aucune valeur n'est présentée comme
mesurée sans l'avoir été.

| # | Défi | État | Preuve / vérification |
|---:|---|---|---|
| ① | Export 100k lignes `.json` en moins de 30 s | implémenté | export NDJSON streaming, test `Export_json_is_ndjson_with_import_field_names` |
| ② | Import 100k lignes en moins de 60 s, durée réelle | implémenté | `durationMs` exposé par le lot, `scripts/bench_import.py` |
| ③ | Recherche p95 < 200 ms sur 1M de lignes | implémenté | index de mots, tests `Search_*`, `scripts/bench_search.py` |
| ④ | Import 50k avec 2 % de lignes cassées | implémenté | rapport par ligne (numéro, contenu, raison), test `Upload_txt_returns_202_...` |
| ⑤ | Clic sur une erreur → ligne brute | implémenté | vue `/Imports/{id}/errors` |
| ⑥ | Trois fichiers en parallèle | implémenté | `Import:MaxParallel=3`, test de convergence simultanée |
| ⑦ | Traçabilité fichier + ligne | implémenté | `sourceFileName`, `sourceLine`, `importId`, test dédié |
| ⑧ | Même fichier importé deux fois | implémenté | déduplication UUID, test de double envoi simultané |
| ⑨ | Annulation chirurgicale | implémenté | seules les lignes non éditées disparaissent, test dédié |
| ⑩ | Pagination sans doublon ni saut | implémenté | curseur keyset, tests de pagination |
| ⑪ | Solde à une date passée < 100 ms | implémenté | index couvrant, test `Balance_*` |
| ⑫ | Reprise après arrêt brutal | implémenté | offset octet + statut `RUNNING`, test de reprise |
| ⑬ | 1M de lignes sans dépasser 256 Mo | implémenté | import en flux, protocole de mesure mémoire fourni |
| ⑭ | Export complet < 30 s, total = solde | implémenté | test d'égalité export/solde, `scripts/check_export_balance.py` |
| ⑮ | Tableau de bord | implémenté | `/api/Status/dashboard` + page `/`, tests dédiés |

Détail par défi : [docs/CHALLENGES.md](docs/CHALLENGES.md).

## Documentation

- [Architecture](docs/ARCHITECTURE.md)
- [Référence API](docs/API.md)
- [Performance et mesures](docs/PERFORMANCE.md)
- [Défis du brief](docs/CHALLENGES.md)
- [Exploitation](docs/OPERATIONS.md)

## Licence

Projet de hackathon, fourni à des fins de démonstration.
