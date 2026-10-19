using Mondje.Application.Common.Interfaces;
using Mondje.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Mondje.Application.Transactions.Common;

public record TransactionDto(
    string id,
    string type,
    long amount,
    string description,
    DateTime createdAtUtc,
    string? agency,
    Guid? importId,
    int? sourceLine,
    DateTime? editedAtUtc,
    DateTime? deletedAtUtc,
    string? sourceFileName)
{
    public static TransactionDto From(Transaction t, string? sourceFileName = null) => new(
        t.Id, t.Type, t.Amount, t.Description, t.CreatedAtUtc,
        t.Agency, t.ImportId, t.SourceLine, t.EditedAtUtc, t.DeletedAtUtc, sourceFileName);

    /// <summary>
    /// Challenge 7: for any transaction the report must tell which file and line it came
    /// from. Rows already carry ImportId/SourceLine; one extra dictionary query per page
    /// resolves the file names without a per-row join.
    /// </summary>
    public static async Task<IReadOnlyList<TransactionDto>> FromAsync(
        IApplicationDbContext db,
        IReadOnlyList<Transaction> rows,
        CancellationToken cancellationToken)
    {
        var importIds = rows
            .Where(r => r.ImportId is not null)
            .Select(r => r.ImportId!.Value)
            .Distinct()
            .ToArray();

        Dictionary<Guid, string> fileNames = importIds.Length == 0
            ? new Dictionary<Guid, string>()
            : await db.ImportBatches
                .AsNoTracking()
                .Where(b => importIds.Contains(b.Id))
                .ToDictionaryAsync(b => b.Id, b => b.FileName, cancellationToken);

        return rows
            .Select(r => From(
                r,
                r.ImportId is Guid importId && fileNames.TryGetValue(importId, out var fileName)
                    ? fileName
                    : null))
            .ToList();
    }
}
