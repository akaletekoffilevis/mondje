using System.Data;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Mondje.Application.Common.Interfaces;
using Mondje.Application.Transactions.Common;
using Mondje.Domain.Entities;
using Mondje.Domain.Search;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Mondje.Infrastructure.Imports;

/// <summary>
/// Parses one stored import file and writes it to the ledger without ever losing or
/// double-counting a row:
/// - lines are decoded in strict UTF-8, so a wrongly encoded line is reported instead of
///   silently mangled (challenge 4: "accent mal encodé");
/// - staged rows are flushed in chunks of 50 000 and deduplicated by SQL inside the same
///   statement that inserts them (challenges 2 and 13);
/// - every 5 seconds the durable progress (counters + file byte offset) is saved, so a
///   killed server resumes at that offset without re-reading the file (challenge 12) and
///   the UI shows live progress (challenge 6, bonus 2);
/// - the dedup insert retries on duplicate-key errors, so two identical files uploaded at
///   the same time converge to one ledger (challenge 8).
/// </summary>
public class ImportEngine(IConfiguration configuration, IApplicationDbContext db) : IImportEngine
{
    private const int ChunkSize = 20_000;
    private const int ErrorFlushSize = 1_000;
    private const int MaxRawLineLength = 1_000;
    private const int ReadBufferSize = 128 * 1024;
    private const int DedupMaxAttempts = 5;
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromSeconds(5);

    // Strict UTF-8 with throw-on-error so a bad byte becomes a reported line (challenge 4).
    private static readonly Encoding Utf8Strict =
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private string ConnectionString =>
        configuration.GetConnectionString("MondjeDb")
        ?? throw new InvalidOperationException("ConnectionStrings:MondjeDb is not configured.");

    public async Task ProcessAsync(Guid importId, CancellationToken cancellationToken)
    {
        var batch = await db.ImportBatches
            .FirstOrDefaultAsync(b => b.Id == importId, cancellationToken)
            ?? throw new InvalidOperationException($"Import batch {importId} not found.");

        var fullPath = ImportStorage.GetPath(configuration, batch.StoredFileName);

        // Resume (challenge 12): a batch found RUNNING with a saved offset was interrupted;
        // restart from that offset instead of re-reading the file from the beginning.
        var isResume = batch.Status == ImportBatch.Running && batch.BytesProcessed > 0;

        if (isResume)
        {
            batch.Status = ImportBatch.Running;
            batch.FailureReason = null;
        }
        else
        {
            var leftoverErrors = await db.ImportErrorLines
                .Where(e => e.ImportId == importId)
                .ToListAsync(cancellationToken);
            if (leftoverErrors.Count > 0)
            {
                db.ImportErrorLines.RemoveRange(leftoverErrors);
            }

            batch.Status = ImportBatch.Running;
            batch.StartedAtUtc = Truncate(DateTime.UtcNow);
            batch.TotalLines = await CountLinesAsync(fullPath, cancellationToken);
            batch.ProcessedLines = 0;
            batch.BytesProcessed = 0;
            batch.InsertedCount = 0;
            batch.DuplicateCount = 0;
            batch.ErrorCount = 0;
            batch.FailureReason = null;
        }

        await db.SaveChangesAsync(cancellationToken);

        // Every row of the batch shares the timestamp of the start of the run.
        var createdAtUtc = Truncate(batch.StartedAtUtc ?? DateTime.UtcNow);
        long fileOffset = batch.BytesProcessed;

        try
        {
            var isNdjson = batch.Format == ImportBatch.FormatNdjson;
            if (isNdjson && await StartsWithJsonArrayAsync(fullPath, cancellationToken))
            {
                throw new InvalidOperationException(
                    "Un fichier NDJSON ne doit pas contenir de tableau JSON : chaque ligne doit être un objet JSON indépendant.");
            }

            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await CreateStagingTableAsync(connection, cancellationToken);

            var insertedTotal = batch.InsertedCount;
            var duplicateTotal = batch.DuplicateCount;
            var errorTotal = batch.ErrorCount;
            var lineNumber = batch.ProcessedLines;
            var cancelled = false;

            var staged = new List<StagingRow>(ChunkSize);
            var errors = new List<ImportErrorLine>();

            await using (var file = OpenFile(fullPath))
            {
                if (fileOffset > 0)
                {
                    // Challenge 12: resume at the exact byte where the last flushed line
                    // ended — the file is never re-read from the beginning.
                    file.Seek(fileOffset, SeekOrigin.Begin);
                }
                else
                {
                    fileOffset = await SkipBomAsync(file, cancellationToken);
                }

                var carry = new MemoryStream(1_024);
                var buffer = new byte[ReadBufferSize];
                var lastTick = Stopwatch.GetTimestamp();
                int read;

                while ((read = await file.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    fileOffset += read;
                    var start = 0;

                    for (var i = 0; i < read; i++)
                    {
                        if (buffer[i] != (byte)'\n')
                        {
                            continue;
                        }

                        lineNumber++;
                        ProcessLine(
                            JoinLine(carry, buffer, start, i - start),
                            isNdjson, batch, importId, createdAtUtc, lineNumber,
                            staged, errors, ref errorTotal);
                        carry.SetLength(0);
                        start = i + 1;
                    }

                    if (start < read)
                    {
                        carry.Write(buffer, start, read - start);
                    }

                    if (staged.Count >= ChunkSize)
                    {
                        var (inserted, duplicates) = await FlushStagedAsync(connection, staged, cancellationToken);
                        insertedTotal += inserted;
                        duplicateTotal += duplicates;
                    }

                    if (errors.Count >= ErrorFlushSize)
                    {
                        await FlushErrorsAsync(errors, cancellationToken);
                        errors.Clear();
                    }

                    if (Stopwatch.GetElapsedTime(lastTick) >= ProgressInterval)
                    {
                        lastTick = Stopwatch.GetTimestamp();

                        if (staged.Count > 0)
                        {
                            var (inserted, duplicates) = await FlushStagedAsync(connection, staged, cancellationToken);
                            insertedTotal += inserted;
                            duplicateTotal += duplicates;
                        }

                        if (errors.Count > 0)
                        {
                            await FlushErrorsAsync(errors, cancellationToken);
                            errors.Clear();
                        }

                        var status = await db.ImportBatches
                            .AsNoTracking()
                            .Where(b => b.Id == importId)
                            .Select(b => b.Status)
                            .FirstOrDefaultAsync(cancellationToken);

                        if (status == ImportBatch.Cancelled)
                        {
                            cancelled = true;
                            break;
                        }

                        // Durable progress: the first byte of the partial line is the safest
                        // resume point, every line before it is already in the ledger.
                        batch.ProcessedLines = lineNumber;
                        batch.BytesProcessed = fileOffset - carry.Length;
                        batch.InsertedCount = insertedTotal;
                        batch.DuplicateCount = duplicateTotal;
                        batch.ErrorCount = errorTotal;
                        await db.SaveChangesAsync(cancellationToken);
                    }
                }

                // Last line when the file has no trailing newline.
                if (carry.Length > 0 && !cancelled)
                {
                    lineNumber++;
                    ProcessLine(
                        carry.GetBuffer().AsSpan(0, (int)carry.Length),
                        isNdjson, batch, importId, createdAtUtc, lineNumber,
                        staged, errors, ref errorTotal);
                }
            }

            if (!cancelled && staged.Count > 0)
            {
                var (inserted, duplicates) = await FlushStagedAsync(connection, staged, cancellationToken);
                insertedTotal += inserted;
                duplicateTotal += duplicates;
            }

            if (errors.Count > 0)
            {
                await FlushErrorsAsync(errors, cancellationToken);
                errors.Clear();
            }

            await DropStagingTableAsync(connection, cancellationToken);

            // Read the live status: a cancellation may have been made concurrently from
            // another context (the tracked instance would still say RUNNING).
            var liveStatus = await db.ImportBatches
                .AsNoTracking()
                .Where(b => b.Id == importId)
                .Select(b => b.Status)
                .FirstOrDefaultAsync(CancellationToken.None);

            batch.ProcessedLines = lineNumber;
            batch.BytesProcessed = fileOffset;
            batch.InsertedCount = insertedTotal;
            batch.DuplicateCount = duplicateTotal;
            batch.ErrorCount = errorTotal;

            if (liveStatus == ImportBatch.Cancelled)
            {
                // The cancel endpoint deleted the rows it found; delete any straggler this
                // engine inserted after that moment. Manually edited rows are kept (challenge 9).
                batch.Status = ImportBatch.Cancelled;
                await db.Transactions
                    .Where(t => t.ImportId == importId && t.EditedAtUtc == null)
                    .ExecuteDeleteAsync(cancellationToken);

                batch.CompletedAtUtc ??= Truncate(DateTime.UtcNow);
            }
            else
            {
                batch.Status = ImportBatch.Completed;
                if (lineNumber > batch.TotalLines)
                {
                    batch.TotalLines = (int)lineNumber;
                }

                batch.CompletedAtUtc = Truncate(DateTime.UtcNow);
            }

            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // A cancellation wins over the failure caused by the interruption.
            var liveStatus = await db.ImportBatches
                .AsNoTracking()
                .Where(b => b.Id == importId)
                .Select(b => b.Status)
                .FirstOrDefaultAsync(CancellationToken.None);

            if (liveStatus != ImportBatch.Cancelled)
            {
                batch.Status = ImportBatch.Failed;
                batch.FailureReason = ex.Message.Length > 512 ? ex.Message[..512] : ex.Message;
                batch.CompletedAtUtc = Truncate(DateTime.UtcNow);
                await db.SaveChangesAsync(CancellationToken.None);
            }

            throw;
        }
    }

    private static void ProcessLine(
        ReadOnlySpan<byte> lineBytes,
        bool isNdjson,
        ImportBatch batch,
        Guid importId,
        DateTime createdAtUtc,
        long lineNumber,
        List<StagingRow> staged,
        List<ImportErrorLine> errors,
        ref int errorTotal)
    {
        // CRLF files: drop the carriage return kept by the '\n' split.
        if (lineBytes.Length > 0 && lineBytes[^1] == (byte)'\r')
        {
            lineBytes = lineBytes[..^1];
        }

        string? line;
        try
        {
            line = Utf8Strict.GetString(lineBytes);
        }
        catch (DecoderFallbackException)
        {
            // Challenge 4 lists "accent mal encodé" among the broken lines: report it with
            // a readable latin1 rendering so the user can still see the original content.
            AddError(errors, ref errorTotal, importId, (int)lineNumber, ImportErrorLine.KindFormat,
                "Encodage invalide : la ligne n'est pas en UTF-8 valide (accent mal encodé ou octets corrompus).",
                Encoding.Latin1.GetString(lineBytes));
            return;
        }

        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        if (isNdjson)
        {
            ParseNdjsonLine(line, batch, importId, createdAtUtc, (int)lineNumber, staged, errors, ref errorTotal);
        }
        else
        {
            ParseTxtLine(line, batch, importId, createdAtUtc, (int)lineNumber, staged, errors, ref errorTotal);
        }
    }

    private static void ParseTxtLine(
        string line,
        ImportBatch batch,
        Guid importId,
        DateTime createdAtUtc,
        int lineNumber,
        List<StagingRow> staged,
        List<ImportErrorLine> errors,
        ref int errorCount)
    {
        var fields = line.Split(';', 4);
        if (fields.Length < 4)
        {
            AddError(errors, ref errorCount, importId, lineNumber, ImportErrorLine.KindFormat,
                "Ligne incomplète : attendu 'id;type;montant;description'.", line);
            return;
        }

        var idRaw = fields[0].Trim();
        var type = fields[1].Trim().ToUpperInvariant();
        var amountRaw = fields[2].Trim();
        var description = fields[3];

        if (!UuidFormat.IsValid(idRaw))
        {
            AddError(errors, ref errorCount, importId, lineNumber, ImportErrorLine.KindValidation,
                "L'identifiant doit être un UUID valide.", line);
            return;
        }

        if (type is not (Transaction.Credit or Transaction.Debit))
        {
            AddError(errors, ref errorCount, importId, lineNumber, ImportErrorLine.KindValidation,
                "Le type doit être strictement CREDIT ou DEBIT.", line);
            return;
        }

        if (!TryParseAmount(amountRaw, out var amount))
        {
            AddError(errors, ref errorCount, importId, lineNumber, ImportErrorLine.KindValidation,
                "Le montant doit être un entier strictement positif en FCFA, sans décimales.", line);
            return;
        }

        if (description is null || description.Length > Transaction.MaxDescriptionLength)
        {
            AddError(errors, ref errorCount, importId, lineNumber, ImportErrorLine.KindValidation,
                $"La description ne peut pas dépasser {Transaction.MaxDescriptionLength} caractères.", line);
            return;
        }

        staged.Add(new StagingRow(
            idRaw.ToLowerInvariant(),
            type,
            amount,
            description,
            batch.Agency,
            importId,
            lineNumber,
            createdAtUtc));
    }

    private static void ParseNdjsonLine(
        string line,
        ImportBatch batch,
        Guid importId,
        DateTime createdAtUtc,
        int lineNumber,
        List<StagingRow> staged,
        List<ImportErrorLine> errors,
        ref int errorCount)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            AddError(errors, ref errorCount, importId, lineNumber, ImportErrorLine.KindFormat,
                "Ligne JSON invalide : impossible de lire l'objet.", line);
            return;
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                AddError(errors, ref errorCount, importId, lineNumber, ImportErrorLine.KindFormat,
                    "Chaque ligne NDJSON doit être un objet JSON.", line);
                return;
            }

            string? idRaw = null;
            if (root.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
            {
                idRaw = idEl.GetString();
            }

            string? type = null;
            if (root.TryGetProperty("type", out var typeEl) && typeEl.ValueKind == JsonValueKind.String)
            {
                type = typeEl.GetString()?.Trim().ToUpperInvariant();
            }

            long amount = 0;
            var amountOk = false;
            if (root.TryGetProperty("montant", out var montantEl) || root.TryGetProperty("amount", out montantEl))
            {
                if (montantEl.ValueKind == JsonValueKind.Number && montantEl.TryGetInt64(out amount) && amount > 0)
                {
                    amountOk = true;
                }
                else if (montantEl.ValueKind == JsonValueKind.String)
                {
                    amountOk = TryParseAmount(montantEl.GetString() ?? string.Empty, out amount);
                }
            }

            string? description = null;
            if (root.TryGetProperty("description", out var descEl) && descEl.ValueKind == JsonValueKind.String)
            {
                description = descEl.GetString();
            }

            if (!UuidFormat.IsValid(idRaw))
            {
                AddError(errors, ref errorCount, importId, lineNumber, ImportErrorLine.KindValidation,
                    "L'identifiant doit être un UUID valide.", line);
                return;
            }

            if (type is not (Transaction.Credit or Transaction.Debit))
            {
                AddError(errors, ref errorCount, importId, lineNumber, ImportErrorLine.KindValidation,
                    "Le type doit être strictement CREDIT ou DEBIT.", line);
                return;
            }

            if (!amountOk)
            {
                AddError(errors, ref errorCount, importId, lineNumber, ImportErrorLine.KindValidation,
                    "Le montant doit être un entier strictement positif en FCFA, sans décimales.", line);
                return;
            }

            if (description is null || description.Length > Transaction.MaxDescriptionLength)
            {
                AddError(errors, ref errorCount, importId, lineNumber, ImportErrorLine.KindValidation,
                    $"La description ne peut pas dépasser {Transaction.MaxDescriptionLength} caractères.", line);
                return;
            }

            staged.Add(new StagingRow(
                idRaw!.Trim().ToLowerInvariant(),
                type!,
                amount,
                description!,
                batch.Agency,
                importId,
                lineNumber,
                createdAtUtc));
        }
    }

    private static bool TryParseAmount(string raw, out long amount)
    {
        amount = 0;
        if (string.IsNullOrEmpty(raw) || !raw.All(char.IsAsciiDigit)) return false;
        if (!long.TryParse(raw, out amount)) return false;
        return amount > 0;
    }

    /// <summary>
    /// Writes one chunk: transactions (deduplicated by UUID) and the words of their
    /// descriptions are committed in the same database transaction, so the ledger and
    /// its search index never disagree. Duplicate-key/deadlock failures roll the whole
    /// chunk back and are retried: the next attempt simply sees the winner's rows as
    /// duplicates (challenge 8).
    /// </summary>
    private async Task<(int Inserted, int Duplicates)> FlushStagedAsync(
        SqlConnection connection, List<StagingRow> rows, CancellationToken cancellationToken)
    {
        await BulkCopyAsync(connection, rows, cancellationToken);
        await BulkCopyTokensAsync(connection, rows, cancellationToken);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
                var inserted = await DedupInsertAsync(connection, (SqlTransaction)transaction, cancellationToken);
                await InsertTokensAsync(connection, (SqlTransaction)transaction, cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                using (var clear = connection.CreateCommand())
                {
                    clear.CommandText = "DELETE FROM #Staging; DELETE FROM #StagingTokens;";
                    await clear.ExecuteNonQueryAsync(cancellationToken);
                }

                return (inserted, rows.Count - inserted);
            }
            catch (SqlException ex) when ((ex.Number is 2601 or 2627 or 1205) && attempt < DedupMaxAttempts)
            {
                // 2601/2627: another import inserted the same UUIDs first (challenge 8).
                // 1205: deadlock between two concurrent bulk inserts. The chunk
                // transaction was rolled back as a whole, so retrying it is safe.
                await Task.Delay(TimeSpan.FromMilliseconds(150 * attempt), cancellationToken);
            }
        }
    }

    private static async Task<int> DedupInsertAsync(
        SqlConnection connection, SqlTransaction transaction, CancellationToken cancellationToken)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = @"
WITH ranked AS (
    SELECT Id, Type, Amount, Description, Agency, ImportId, SourceLine, CreatedAtUtc,
           ROW_NUMBER() OVER (PARTITION BY Id ORDER BY SourceLine) AS rn
    FROM #Staging
)
INSERT INTO Transactions (Id, Type, Amount, Description, CreatedAtUtc, Agency, ImportId, SourceLine)
SELECT r.Id, r.Type, r.Amount, r.Description, r.CreatedAtUtc, r.Agency, r.ImportId, r.SourceLine
FROM ranked r
WHERE r.rn = 1
  AND NOT EXISTS (SELECT 1 FROM Transactions t WHERE t.Id = r.Id);";

        return await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Adds the words of this chunk to the search index. Rows skipped as duplicates
    /// already have their words, hence the NOT EXISTS; the EXISTS keeps the foreign key
    /// satisfied if a cancellation deleted the batch's rows concurrently.
    /// </summary>
    private static async Task InsertTokensAsync(
        SqlConnection connection, SqlTransaction transaction, CancellationToken cancellationToken)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = @"
INSERT INTO TransactionTokens (Token, CreatedAtUtc, TransactionId)
SELECT DISTINCT s.Token, s.CreatedAtUtc, s.TransactionId
FROM #StagingTokens s
WHERE EXISTS (SELECT 1 FROM Transactions t WHERE t.Id = s.TransactionId)
  AND NOT EXISTS (
      SELECT 1 FROM TransactionTokens k
      WHERE k.Token = s.Token AND k.CreatedAtUtc = s.CreatedAtUtc AND k.TransactionId = s.TransactionId);";

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task CreateStagingTableAsync(SqlConnection connection, CancellationToken ct)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
IF OBJECT_ID('tempdb..#Staging') IS NOT NULL DROP TABLE #Staging;
IF OBJECT_ID('tempdb..#StagingTokens') IS NOT NULL DROP TABLE #StagingTokens;
CREATE TABLE #Staging (
    Id char(36) NOT NULL,
    Type varchar(6) NOT NULL,
    Amount bigint NOT NULL,
    Description nvarchar(255) NOT NULL,
    Agency nvarchar(32) NULL,
    ImportId uniqueidentifier NOT NULL,
    SourceLine int NOT NULL,
    CreatedAtUtc datetime2(3) NOT NULL
);
CREATE TABLE #StagingTokens (
    Token nvarchar(64) NOT NULL,
    CreatedAtUtc datetime2(3) NOT NULL,
    TransactionId char(36) NOT NULL
);";
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task DropStagingTableAsync(SqlConnection connection, CancellationToken ct)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
IF OBJECT_ID('tempdb..#Staging') IS NOT NULL DROP TABLE #Staging;
IF OBJECT_ID('tempdb..#StagingTokens') IS NOT NULL DROP TABLE #StagingTokens;";
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task BulkCopyAsync(SqlConnection connection, List<StagingRow> rows, CancellationToken ct)
    {
        using var bulk = new SqlBulkCopy(connection)
        {
            DestinationTableName = "#Staging",
            BatchSize = rows.Count,
            BulkCopyTimeout = 300,
        };
        var table = new DataTable();
        table.Columns.Add("Id", typeof(string));
        table.Columns.Add("Type", typeof(string));
        table.Columns.Add("Amount", typeof(long));
        table.Columns.Add("Description", typeof(string));
        table.Columns.Add("Agency", typeof(string));
        table.Columns.Add("ImportId", typeof(Guid));
        table.Columns.Add("SourceLine", typeof(int));
        table.Columns.Add("CreatedAtUtc", typeof(DateTime));

        foreach (var r in rows)
        {
            table.Rows.Add(r.Id, r.Type, r.Amount, r.Description, (object?)r.Agency ?? DBNull.Value, r.ImportId, r.SourceLine, r.CreatedAtUtc);
        }

        await bulk.WriteToServerAsync(table, ct);
    }

    /// <summary>Stages the normalised words of every valid line of the chunk.</summary>
    private static async Task BulkCopyTokensAsync(SqlConnection connection, List<StagingRow> rows, CancellationToken ct)
    {
        var table = new DataTable();
        table.Columns.Add("Token", typeof(string));
        table.Columns.Add("CreatedAtUtc", typeof(DateTime));
        table.Columns.Add("TransactionId", typeof(string));

        foreach (var r in rows)
        {
            foreach (var token in SearchTokens.FromDescription(r.Description))
            {
                table.Rows.Add(token, r.CreatedAtUtc, r.Id);
            }
        }

        if (table.Rows.Count == 0)
        {
            return;
        }

        using var bulk = new SqlBulkCopy(connection)
        {
            DestinationTableName = "#StagingTokens",
            BatchSize = table.Rows.Count,
            BulkCopyTimeout = 300,
        };
        await bulk.WriteToServerAsync(table, ct);
    }

    private async Task FlushErrorsAsync(List<ImportErrorLine> errors, CancellationToken ct)
    {
        db.ImportErrorLines.AddRange(errors);
        await db.SaveChangesAsync(ct);
    }

    private static FileStream OpenFile(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.Read, ReadBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

    /// <summary>Counts lines exactly like the parser: one per '\n', plus a final partial line.</summary>
    private static async Task<int> CountLinesAsync(string path, CancellationToken ct)
    {
        await using var file = OpenFile(path);
        var buffer = new byte[ReadBufferSize];
        long newlines = 0;
        var lastByte = -1;
        int read;

        while ((read = await file.ReadAsync(buffer, ct)) > 0)
        {
            lastByte = buffer[read - 1];
            for (var i = 0; i < read; i++)
            {
                if (buffer[i] == (byte)'\n')
                {
                    newlines++;
                }
            }
        }

        if (newlines == 0)
        {
            return lastByte < 0 ? 0 : 1;
        }

        return (int)(lastByte == '\n' ? newlines : newlines + 1);
    }

    private static async Task<long> SkipBomAsync(FileStream file, CancellationToken ct)
    {
        var head = new byte[3];
        var read = await file.ReadAsync(head, ct);
        if (read == 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF)
        {
            return 3; // the BOM is part of the file: keep the offset aligned with its bytes
        }

        file.Seek(0, SeekOrigin.Begin);
        return 0;
    }

    private static async Task<bool> StartsWithJsonArrayAsync(string path, CancellationToken ct)
    {
        await using var file = OpenFile(path);
        var buffer = new byte[4_096];
        var read = await file.ReadAsync(buffer, ct);
        var span = buffer.AsSpan(0, read);

        if (span.Length >= 3 && span[0] == 0xEF && span[1] == 0xBB && span[2] == 0xBF)
        {
            span = span[3..];
        }

        foreach (var b in span)
        {
            if (b is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
            {
                continue;
            }

            return b == (byte)'[';
        }

        return false;
    }

    /// <summary>Full line bytes = bytes carried over from the previous read + this segment.</summary>
    private static ReadOnlySpan<byte> JoinLine(MemoryStream carry, byte[] buffer, int start, int length)
    {
        if (carry.Length == 0)
        {
            return buffer.AsSpan(start, length);
        }

        carry.Write(buffer, start, length);
        return carry.GetBuffer().AsSpan(0, (int)carry.Length);
    }

    private static void AddError(
        List<ImportErrorLine> errors,
        ref int errorCount,
        Guid importId,
        int lineNumber,
        string kind,
        string reason,
        string rawLine)
    {
        errors.Add(new ImportErrorLine
        {
            ImportId = importId,
            LineNumber = lineNumber,
            Kind = kind,
            Reason = reason,
            RawLine = rawLine.Length > MaxRawLineLength ? rawLine[..MaxRawLineLength] : rawLine,
            CreatedAtUtc = Truncate(DateTime.UtcNow),
        });
        errorCount++;
    }

    private static DateTime Truncate(DateTime value) =>
        new(value.Ticks - (value.Ticks % TimeSpan.TicksPerMillisecond), DateTimeKind.Utc);

    private sealed record StagingRow(
        string Id,
        string Type,
        long Amount,
        string Description,
        string? Agency,
        Guid ImportId,
        int SourceLine,
        DateTime CreatedAtUtc);
}
