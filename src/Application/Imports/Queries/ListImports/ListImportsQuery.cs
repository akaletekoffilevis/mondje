using Mondje.Application.Common.Interfaces;
using Mondje.Application.Imports.Common;
using Mondje.Application.Imports.Queries.GetImport;
using Mondje.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Mondje.Application.Imports.Queries.ListImports;

public class ListImportsQuery : IRequest<ImportListResponse>
{
    public int? Limit { get; set; }
    public string? Cursor { get; set; }
}

public class ListImportsQueryHandler(IApplicationDbContext db)
    : IRequestHandler<ListImportsQuery, ImportListResponse>
{
    public const int MaxLimit = 100;
    public const int DefaultLimit = 20;

    public async Task<ImportListResponse> Handle(ListImportsQuery query, CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(query.Limit ?? DefaultLimit, 1, MaxLimit);

        var q = db.ImportBatches.AsNoTracking().AsQueryable();

        if (CursorCodec.TryParse(query.Cursor, out var cursorTicks, out var cursorId))
        {
            var cursorTime = new DateTime(cursorTicks, DateTimeKind.Utc);
            q = q.Where(b =>
                b.CreatedAtUtc < cursorTime
                || (b.CreatedAtUtc == cursorTime && b.Id.CompareTo(cursorId) < 0));
        }

        var rows = await q
            .OrderByDescending(b => b.CreatedAtUtc)
            .ThenByDescending(b => b.Id)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);

        var hasMore = rows.Count > limit;
        if (hasMore) rows.RemoveAt(rows.Count - 1);

        var nextCursor = hasMore && rows.Count > 0
            ? CursorCodec.Encode(rows[^1].CreatedAtUtc, rows[^1].Id)
            : null;

        return new ImportListResponse(
            rows.Select(GetImportQueryHandler.ToDto).ToList(),
            nextCursor,
            hasMore,
            limit);
    }
}

public static class CursorCodec
{
    public static string Encode(DateTime createdAtUtc, Guid id)
    {
        var raw = $"{createdAtUtc.Ticks}:{id}";
        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(raw))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static bool TryParse(string? cursor, out long ticks, out Guid id)
    {
        ticks = 0; id = Guid.Empty;
        if (string.IsNullOrEmpty(cursor)) return false;
        try
        {
            var base64 = cursor.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
            var raw = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(base64));
            var sep = raw.LastIndexOf(':');
            if (sep <= 0 || !long.TryParse(raw[..sep], out ticks)) return false;
            return Guid.TryParse(raw[(sep + 1)..], out id);
        }
        catch (FormatException)
        {
            ticks = 0; id = Guid.Empty; return false;
        }
    }
}