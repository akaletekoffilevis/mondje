using System.Data;
using System.Text;
using System.Text.Json;
using Mondje.Application.Common.Interfaces;
using Mondje.Application.Transactions.Common;
using Mondje.Domain.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Mondje.Infrastructure.Imports;

public class ImportEngine(IConfiguration configuration, IApplicationDbContext db) : IImportEngine
{
    private const int ChunkSize = 50_000;
    private const int ErrorFlushSize = 1_000;
    private const int MaxRawLineLength = 1_000;

    private string ConnectionString =>
        configuration.GetConnectionString("MondjeDb")
        ?? throw new InvalidOperationException("ConnectionStrings:MondjeDb is not configured.");

    public async Task ProcessAsync(Guid importId, CancellationToken cancellationToken)
    {
        var batch = await db.ImportBatches
            .FirstOrDefaultAsync(b => b.Id == importId, cancellationToken)
            ?? throw new InvalidOperationException($"Import batch {importId} not found.");

        // Resume support: reset any leftover state from a previous (crashed) run.
        var oldErrors = await db.ImportErrorLines
            .Where(e => e.ImportId == importId)
            .ToListAsync(cancellationToken);
        if (oldErrors.Count > 0)
        {
            db.ImportErrorLines.RemoveRange(oldErrors);
        }

        batch.Status = ImportBatch.Running;
        batch.StartedAtUtc = Truncate(DateTime.UtcNow);
        batch.TotalLines = 0;
        batch.InsertedCount = 0;
        batch.DuplicateCount = 0;
        batch.ErrorCount = 0;
        batch.FailureReason = null;
        await db.SaveChangesAsync(cancellationToken);

        // Use a fixed UTC timestamp for this batch so all rows share one value.
        var createdAtUtc = Truncate(DateTime.UtcNow);

        try
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync(cancellationToken);

            await CreateStagingTableAsync(connection, cancellationToken);

            int totalLines = 0;
            int stagedCount = 0;
            int errorCount = 0;
            var errors = new List<ImportErrorLine>();
            bool isNdjson = batch.Format == ImportBatch.FormatNdjson;

            // Read the file from disk (kept there for resume after a crash).
            var fullPath = ImportStorage.GetPath(configuration, batch.StoredFileName);
            using var reader = new StreamReader(fullPath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

            // NDJSON: refuse a single JSON array up front.
            if (isNdjson)
            {
                var firstNonEmpty = await PeekFirstNonEmptyAsync(reader);
                if (firstNonEmpty is not null && firstNonEmpty.TrimStart().StartsWith('['))
                {
                    throw new InvalidOperationException(
                        "Un fichier NDJSON ne doit pas contenir de tableau JSON : chaque ligne doit être un objet JSON indépendant.");
                }
            }

            // Re-open for sequential reading (PeekFirstNonEmpty consumed nothing because StreamReader tracks position).
            // StreamReader above already advanced; instead we re-create the reader.
            reader.Dispose();
            using var reader2 = new StreamReader(fullPath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

            string? line;
            var staged = new List<StagingRow>(ChunkSize);

            while ((line = await reader2.ReadLineAsync(cancellationToken)) is not null)
            {
                totalLines++;

                // Cancellation check (worker sets Status = Cancelled in the DB).
                if (await IsCancelledAsync(importId, cancellationToken))
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (isNdjson)
                {
                    ParseNdjsonLine(line, batch, importId, createdAtUtc, totalLines, staged, errors, ref stagedCount, ref errorCount);
                }
                else
                {
                    ParseTxtLine(line, batch, importId, createdAtUtc, totalLines, staged, errors, ref stagedCount, ref errorCount);
                }

                if (staged.Count >= ChunkSize)
                {
                    await BulkCopyAsync(connection, staged, cancellationToken);
                    staged.Clear();
                }

                if (errors.Count >= ErrorFlushSize)
                {
                    await FlushErrorsAsync(errors, cancellationToken);
                    errors.Clear();
                }
            }

            if (staged.Count > 0)
            {
                await BulkCopyAsync(connection, staged, cancellationToken);
                staged.Clear();
            }

            if (errors.Count > 0)
            {
                await FlushErrorsAsync(errors, cancellationToken);
                errors.Clear();
            }

            // Deduplicate within the file and against the existing ledger, then insert.
            int insertedCount;
            using (var cmd = connection.CreateCommand())
            {
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
                insertedCount = await cmd.ExecuteNonQueryAsync(cancellationToken);
            }

            var duplicateCount = stagedCount - insertedCount;

            await DropStagingTableAsync(connection, cancellationToken);

            // Reload the batch (it may have been cancelled concurrently).
            var fresh = await db.ImportBatches.FirstAsync(b => b.Id == importId, cancellationToken);
            if (fresh.Status == ImportBatch.Cancelled)
            {
                // Cancellation already handled by the cancel endpoint; just leave counters.
                fresh.CompletedAtUtc = fresh.CompletedAtUtc ?? Truncate(DateTime.UtcNow);
            }
            else
            {
                fresh.Status = ImportBatch.Completed;
                fresh.TotalLines = totalLines;
                fresh.InsertedCount = insertedCount;
                fresh.DuplicateCount = duplicateCount;
                fresh.ErrorCount = errorCount;
                fresh.CompletedAtUtc = Truncate(DateTime.UtcNow);
            }

            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            var fresh = await db.ImportBatches.FirstOrDefaultAsync(b => b.Id == importId, cancellationToken);
            if (fresh is not null)
            {
                fresh.Status = ImportBatch.Failed;
                fresh.FailureReason = ex.Message;
                fresh.CompletedAtUtc = Truncate(DateTime.UtcNow);
                await db.SaveChangesAsync(cancellationToken);
            }

            throw;
        }
    }

    private async Task CreateStagingTableAsync(SqlConnection connection, CancellationToken ct)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
IF OBJECT_ID('tempdb..#Staging') IS NOT NULL DROP TABLE #Staging;
CREATE TABLE #Staging (
    Id char(36) NOT NULL,
    Type varchar(6) NOT NULL,
    Amount bigint NOT NULL,
    Description nvarchar(255) NOT NULL,
    Agency nvarchar(32) NULL,
    ImportId uniqueidentifier NOT NULL,
    SourceLine int NOT NULL,
    CreatedAtUtc datetime2(3) NOT NULL
);";
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task DropStagingTableAsync(SqlConnection connection, CancellationToken ct)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "IF OBJECT_ID('tempdb..#Staging') IS NOT NULL DROP TABLE #Staging;";
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

    private async Task FlushErrorsAsync(List<ImportErrorLine> errors, CancellationToken ct)
    {
        db.ImportErrorLines.AddRange(errors);
        await db.SaveChangesAsync(ct);
    }

    private async Task<bool> IsCancelledAsync(Guid importId, CancellationToken ct)
    {
        var status = await db.ImportBatches
            .AsNoTracking()
            .Where(b => b.Id == importId)
            .Select(b => b.Status)
            .FirstOrDefaultAsync(ct);
        return status == ImportBatch.Cancelled;
    }

    private static async Task<string?> PeekFirstNonEmptyAsync(StreamReader reader)
    {
        // Read lines until a non-empty one is found; we cannot "unread", so this helper
        // is only used to decide the format before re-opening the file.
        string? line;
        while ((line = await reader.ReadLineAsync()) is not null)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                return line;
            }
        }
        return null;
    }

    private static void ParseTxtLine(
        string line,
        ImportBatch batch,
        Guid importId,
        DateTime createdAtUtc,
        int lineNumber,
        List<StagingRow> staged,
        List<ImportErrorLine> errors,
        ref int stagedCount,
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
            idRaw.Trim().ToLowerInvariant(),
            type,
            amount,
            description,
            batch.Agency,
            importId,
            lineNumber,
            createdAtUtc));
        stagedCount++;
    }

    private static void ParseNdjsonLine(
        string line,
        ImportBatch batch,
        Guid importId,
        DateTime createdAtUtc,
        int lineNumber,
        List<StagingRow> staged,
        List<ImportErrorLine> errors,
        ref int stagedCount,
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

            string? amountRaw = null;
            long amount = 0;
            bool amountOk = false;
            if (root.TryGetProperty("montant", out var montantEl) || root.TryGetProperty("amount", out montantEl))
            {
                if (montantEl.ValueKind == JsonValueKind.Number && montantEl.TryGetInt64(out amount) && amount > 0)
                {
                    amountOk = true;
                }
                else if (montantEl.ValueKind == JsonValueKind.String)
                {
                    amountRaw = montantEl.GetString();
                    amountOk = TryParseAmount(amountRaw ?? string.Empty, out amount);
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
            stagedCount++;
        }
    }

    private static bool TryParseAmount(string raw, out long amount)
    {
        amount = 0;
        if (string.IsNullOrEmpty(raw) || !raw.All(char.IsAsciiDigit)) return false;
        if (!long.TryParse(raw, out amount)) return false;
        return amount > 0;
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
