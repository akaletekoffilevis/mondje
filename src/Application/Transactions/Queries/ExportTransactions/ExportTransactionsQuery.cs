using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Mondje.Application.Common.Interfaces;
using Mondje.Application.Transactions.Common;
using Mondje.Domain.Entities;

namespace Mondje.Application.Transactions.Queries.ExportTransactions;

/// <summary>
/// Streams every matching transaction in keyset batches so an export of 1M rows never
/// materialises in memory (challenges 1 and 14). Rows are ordered ascending by
/// (CreatedAtUtc, Id) and re-queried in batches of 5 000: locks are released between
/// batches, memory stays flat, and no row can be duplicated or skipped.
/// </summary>
public class ExportTransactionsQuery : IRequest<IAsyncEnumerable<TransactionExportLine>>
{
    /// <summary>json (NDJSON, one object per line) or csv.</summary>
    public string? Format { get; set; }
    public string? Type { get; set; }
    public long? MinAmount { get; set; }
    public long? MaxAmount { get; set; }
    public string? Q { get; set; }
    /// <summary>Optional: include soft-deleted rows (default false, keeps export = balance).</summary>
    public bool? IncludeDeleted { get; set; }
}

/// <summary>
/// One exported line. Property names mirror the NDJSON import contract (id, type,
/// montant, description) so an export can be re-imported as-is.
/// </summary>
public record TransactionExportLine(
    string id,
    string type,
    long montant,
    string description,
    string? agency,
    DateTime createdAtUtc);

public class ExportTransactionsQueryHandler(IApplicationDbContext db)
    : IRequestHandler<ExportTransactionsQuery, IAsyncEnumerable<TransactionExportLine>>
{
    public const int BatchSize = 5000;

    public Task<IAsyncEnumerable<TransactionExportLine>> Handle(
        ExportTransactionsQuery query,
        CancellationToken cancellationToken)
        => Task.FromResult(StreamAsync(query, cancellationToken));

    private async IAsyncEnumerable<TransactionExportLine> StreamAsync(
        ExportTransactionsQuery query,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var baseQuery = TransactionFilters.Apply(
            db.Transactions.AsNoTracking(),
            query.Type, query.MinAmount, query.MaxAmount, query.Q, query.IncludeDeleted);

        DateTime? cursorTime = null;
        string? cursorId = null;

        while (true)
        {
            var batch = baseQuery;

            if (cursorTime is DateTime time && cursorId is not null)
            {
                // Ascending keyset comparison written as string.CompareTo so EF
                // translates it to SQL (same collation as the ORDER BY below).
                batch = batch.Where(t =>
                    t.CreatedAtUtc > time
                    || (t.CreatedAtUtc == time && t.Id.CompareTo(cursorId) > 0));
            }

            var rows = await batch
                .OrderBy(t => t.CreatedAtUtc)
                .ThenBy(t => t.Id)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);

            if (rows.Count == 0)
            {
                yield break;
            }

            foreach (var row in rows)
            {
                yield return new TransactionExportLine(
                    row.Id, row.Type, row.Amount, row.Description, row.Agency, row.CreatedAtUtc);
            }

            if (rows.Count < BatchSize)
            {
                yield break;
            }

            cursorTime = rows[^1].CreatedAtUtc;
            cursorId = rows[^1].Id;
        }
    }
}
