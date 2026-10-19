using System.Text;
using Mondje.Application.Common.Interfaces;
using Mondje.Application.Transactions.Common;
using Mondje.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Mondje.Application.Transactions.Queries.ListTransactions;

/// <summary>
/// Cursor-paginated (keyset) transaction list. The cursor is an opaque base64url token
/// encoding the (CreatedAtUtc, Id) position of the last returned row. Because the next
/// page is positioned relative to that row instead of an offset, concurrent imports can
/// insert rows without shifting the pages the user is walking through: no duplicates,
/// no skipped transactions (challenge 10).
/// </summary>
public class ListTransactionsQuery : IRequest<TransactionListResponse>
{
    /// <summary>Page size. Optional in the query string; defaults to 50.</summary>
    public int? Limit { get; set; }
    public string? Cursor { get; set; }
    public string? Type { get; set; }
    public long? MinAmount { get; set; }
    public long? MaxAmount { get; set; }
    public string? Q { get; set; }
    /// <summary>Optional: include soft-deleted rows (default false).</summary>
    public bool? IncludeDeleted { get; set; }
}

public record TransactionListResponse(
    IReadOnlyList<TransactionDto> items,
    string? nextCursor,
    bool hasMore,
    int limit);

public class ListTransactionsQueryHandler(IApplicationDbContext db)
    : IRequestHandler<ListTransactionsQuery, TransactionListResponse>
{
    public const int MaxLimit = 200;
    public const int DefaultLimit = 50;

    public async Task<TransactionListResponse> Handle(ListTransactionsQuery query, CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(query.Limit ?? DefaultLimit, 1, MaxLimit);

        var q = TransactionFilters.Apply(
            db.Transactions.AsNoTracking(),
            query.Type, query.MinAmount, query.MaxAmount, query.Q, query.IncludeDeleted);

        if (CursorCodec.TryParse(query.Cursor, out var cursorTicks, out var cursorId))
        {
            var cursorTime = new DateTime(cursorTicks, DateTimeKind.Utc);
            // Keyset comparison written as string.CompareTo so EF translates it to SQL.
            // The Id comparison uses the database collation, exactly like the ORDER BY
            // below, so both stay consistent.
            q = q.Where(t =>
                t.CreatedAtUtc < cursorTime
                || (t.CreatedAtUtc == cursorTime && t.Id.CompareTo(cursorId) < 0));
        }

        var rows = await q
            .OrderByDescending(t => t.CreatedAtUtc)
            .ThenByDescending(t => t.Id)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);

        var hasMore = rows.Count > limit;
        if (hasMore)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        var nextCursor = hasMore && rows.Count > 0 ? CursorCodec.Encode(rows[^1].CreatedAtUtc, rows[^1].Id) : null;

        var items = await TransactionDto.FromAsync(db, rows, cancellationToken);

        return new TransactionListResponse(items, nextCursor, hasMore, limit);
    }
}

/// <summary>base64url-encoded "ticks:id" cursor. Invalid cursors are simply ignored
/// (the list starts from the beginning); the validator rejects malformed cursors with 422.</summary>
public static class CursorCodec
{
    public static string Encode(DateTime createdAtUtc, string id)
    {
        var raw = $"{createdAtUtc.Ticks}:{id}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(raw))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static bool TryParse(string? cursor, out long ticks, out string id)
    {
        ticks = 0;
        id = string.Empty;

        if (string.IsNullOrEmpty(cursor))
        {
            return false;
        }

        try
        {
            var base64 = cursor.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
            var raw = Encoding.UTF8.GetString(Convert.FromBase64String(base64));

            var separator = raw.LastIndexOf(':');
            if (separator <= 0 || !long.TryParse(raw[..separator], out ticks))
            {
                ticks = 0;
                return false;
            }

            id = raw[(separator + 1)..];
            return true;
        }
        catch (FormatException)
        {
            ticks = 0;
            return false;
        }
    }
}
