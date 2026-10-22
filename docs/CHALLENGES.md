# Les quinze défis du brief

Ce document décrit, pour chaque défi, l'implémentation retenue, les tests qui la couvrent
et la manière de la vérifier manuellement. Les seuils de temps et de mémoire ne sont pas
certifiés ici : le protocole de mesure est décrit dans
[PERFORMANCE.md](PERFORMANCE.md).

## ① Export de 100 000 transactions en `.json` en moins de 30 secondes

**Implémentation.** `GET /api/Transactions/export?format=json` produit du NDJSON : un
objet JSON par ligne. Les lignes sont lues par lots keyset de 5 000 et écrites directement
dans le flux HTTP, sans construction d'une liste en mémoire.

**Tests.** `Export_json_is_ndjson_with_import_field_names`,
`Export_csv_and_json_contain_the_same_transactions`.

**Vérification.**

```bash
time curl -s -o /tmp/export.ndjson \
  "http://localhost:5002/api/Transactions/export?format=json"
```

## ② Import de 100 000 transactions en moins de 60 secondes, avec durée réelle

**Implémentation.** L'import est asynchrone (`202 Accepted`). Le lot expose
`startedAtUtc`, `completedAtUtc` et `durationMs`. Le moteur traite par lots de 20 000
lignes avec `SqlBulkCopy`.

**Tests.** `Completed_import_exposes_progress_bytes_and_duration`,
`Upload_txt_returns_202_and_processes_valid_and_broken_lines`.

**Vérification.** `scripts/bench_import.py` affiche `totalMs` et `serverDurationMs`.

## ③ Recherche par mot avec un p95 inférieur à 200 ms sur 1 million de lignes

**Implémentation.** Table `TransactionTokens` avec clé cluster
`(Token, CreatedAtUtc, TransactionId)`. La requête part de l'index des mots, puis joint le
grand livre. La normalisation ignore la casse et les accents.

**Tests.** `Search_finds_a_word_regardless_of_case_and_accents`,
`Search_combines_with_type_and_amount_filters`,
`Search_pagination_never_skips_nor_duplicates`,
`Updating_the_description_updates_the_search_index`,
`Imported_descriptions_are_searchable`.

**Vérification.** `scripts/bench_search.py` rapporte `p95Ms`.

## ④ Import de 50 000 lignes dont 2 % de lignes cassées

**Implémentation.** Chaque ligne est validée indépendamment. Une ligne invalide produit
une entrée dans `ImportErrorLines` avec numéro de ligne, catégorie (`FORMAT` ou
`VALIDATION`), raison en français et contenu brut. Les lignes valides du même fichier sont
importées.

**Tests.** `Upload_txt_returns_202_and_processes_valid_and_broken_lines`,
`Ndjson_lines_are_parsed_and_invalid_amount_is_reported`.

**Vérification.**

```bash
python3 scripts/generate_data.py --lines 50000 --broken 0.02 --output /tmp/casse.txt
python3 scripts/bench_import.py --file /tmp/casse.txt --agency Douala
curl "http://localhost:5002/api/Imports/{id}/errors?limit=10"
```

## ⑤ Clic sur une erreur : affichage de la ligne brute

**Implémentation.** La vue web `/Imports/{id}/errors` liste les erreurs avec leur numéro,
leur raison et la ligne brute. L'API expose les mêmes données via
`GET /api/Imports/{id}/errors`, paginées par numéro de ligne.

**Tests.** Tests d'import fonctionnels et page d'erreurs alimentée par le client API.

**Vérification.** Ouvrir un import contenant des lignes cassées, cliquer sur « Erreurs ».

## ⑥ Import de trois fichiers en parallèle

**Implémentation.** `ImportWorker` démarre `Import:MaxParallel` consommateurs (3 par
défaut). Chaque import utilise son propre scope DI et son propre `DbContext`.

**Tests.** `Two_identical_files_uploaded_simultaneously_converge_to_one_ledger` vérifie la
convergence de deux imports concurrents. La progression est visible par lot.

**Vérification.** Envoyer trois fichiers en parallèle, puis suivre les trois lots via
`GET /api/Imports/{id}`.

## ⑦ Traçabilité fichier + ligne en moins de 5 secondes

**Implémentation.** Chaque transaction importée conserve `ImportId` et `SourceLine`.
`TransactionDto` ajoute `sourceFileName` en résolvant les noms de fichiers par une requête
groupée, sans jointure par ligne.

**Tests.** `Every_imported_transaction_keeps_its_source_file_and_line`.

**Vérification.**

```bash
curl "http://localhost:5002/api/Transactions?limit=5" | jq '.items[] | {id, sourceFileName, sourceLine}'
```

## ⑧ Même fichier importé deux fois : total identique

**Implémentation.** Règle « premier arrivé gagne ». L'insertion déduplique par UUID dans
la même transaction SQL (`ROW_NUMBER()` + `NOT EXISTS`) et réessaie sur les erreurs de
concurrence. Les doublons sont comptés dans `duplicateCount`.

**Tests.** `Create_duplicate_uuid_returns_409`,
`Reuploading_the_same_file_never_double_counts`,
`Two_identical_files_uploaded_simultaneously_converge_to_one_ledger`.

**Vérification.**

```bash
python3 scripts/check_export_balance.py
```

Le nombre de lignes et le solde ne doivent pas changer après le second import.

## ⑨ Annulation chirurgicale d'un import

**Implémentation.** `DELETE /api/Imports/{id}` passe le lot en `CANCELLED` et exécute
`DELETE … WHERE ImportId = @id AND EditedAtUtc IS NULL`. Les lignes corrigées à la main
restent. Un import annulé avant son démarrage ne traite rien.

**Tests.** `Cancel_import_deletes_only_its_own_untouched_transactions`,
`Cancelling_a_queued_import_never_inserts_anything`,
`Cancelling_an_import_keeps_the_rows_edited_by_hand`.

## ⑩ Pagination sans doublon ni saut pendant un import

**Implémentation.** Curseur opaque `(CreatedAtUtc, Id)` pour les transactions et les
imports. Aucun `OFFSET` : la page suivante se positionne par rapport à la dernière ligne
reçue.

**Tests.** `List_pagination_never_skips_nor_duplicates_across_pages`,
`Search_pagination_never_skips_nor_duplicates`,
`Import_list_is_paginated`.

**Vérification.** Parcourir toutes les pages avec `nextCursor` pendant un import et
vérifier l'unicité des identifiants.

## ⑪ Solde à une date passée en moins de 100 ms

**Implémentation.** `GET /api/Transactions/balance?at=...` calcule la somme sur
`IX_Transactions_Balance (CreatedAtUtc) INCLUDE (Type, Amount)`, filtré sur les lignes
vivantes. Aucun recalcul de toute la table.

**Tests.** `Balance_returns_sums_and_rejects_bad_date`,
`Balance_excludes_soft_deleted_rows`,
`Dashboard_balance_equals_the_balance_endpoint`.

**Vérification.**

```bash
for i in $(seq 1 100); do
  curl -s -o /dev/null -w "%{time_total}\n" \
    "http://localhost:5002/api/Transactions/balance?at=2026-10-19T12:00:00Z"
done | sort -n | tail -5
```

## ⑫ Reprise après arrêt brutal

**Implémentation.** Le lot stocke `BytesProcessed`, `ProcessedLines` et son statut. Au
démarrage, `ImportWorker` ré-enfile les lots `PENDING` et `RUNNING`. Le moteur se positionne
à l'offset sauvegardé, au début de la première ligne non validée.

**Tests.** `Interrupted_import_resumes_at_saved_offset_without_rereading_the_file`.

**Vérification.** Lancer un import volumineux, tuer l'API avec `kill -9`, relancer, suivre
le lot et vérifier l'absence de doublons.

## ⑬ Un million de lignes sans dépasser 256 Mo de RAM

**Implémentation.** Lecture en flux, lots bornés, export et import sans matérialisation de
la table. Seuls les compteurs, le tampon de lecture et le lot courant sont en mémoire.

**Vérification.** Protocole d'échantillonnage `ps -o rss=` décrit dans
[PERFORMANCE.md](PERFORMANCE.md). Mesurer le pic du processus API, pas la mémoire de SQL
Server. Aucune valeur n'est avancée sans mesure reproductible.

## ⑭ Export complet en moins de 30 secondes, total égal au solde

**Implémentation.** L'export et le solde partagent le même ensemble de lignes
(`DeletedAtUtc IS NULL`) et les mêmes filtres. L'export est streamé, donc la mémoire reste
constante.

**Tests.** `Export_total_equals_displayed_balance`,
`Export_csv_and_json_contain_the_same_transactions`,
`Export_respects_type_filter`.

**Vérification.** `scripts/check_export_balance.py` compare le total du NDJSON au solde et
retourne un code de sortie non nul en cas d'écart.

## ⑮ Tableau de bord

**Implémentation.** `GET /api/Status/dashboard` agrège en SQL :

- totaux du grand livre (transactions, crédits, débits, montants, solde, supprimées,
  importées, manuelles) ;
- résultat des imports (statuts, lignes traitées, erreurs, doublons, dernière durée) ;
- ventilation par agence, avec les trois agences toujours présentes.

La page d'accueil `Mondje.Web` affiche ces chiffres en direct, ainsi que la liste des
derniers imports et transactions.

**Tests.** `Empty_ledger_dashboard_exposes_the_three_agencies_with_zeros`,
`Dashboard_totals_match_created_transactions`,
`Dashboard_balance_equals_the_balance_endpoint`,
`Dashboard_reports_import_lines_duration_and_errors`,
`Dashboard_excludes_soft_deleted_transactions`.

**Vérification.** Ouvrir `http://localhost:5001` après quelques créations et imports.
