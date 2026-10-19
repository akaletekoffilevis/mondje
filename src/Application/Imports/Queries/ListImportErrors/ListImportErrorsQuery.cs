using Mondje.Application.Common.Interfaces;
using Mondje.Application.Imports.Common;
using Microsoft.EntityFrameworkCore;

namespace Mondje.Application.Imports.Queries.ListImportErrors;

public class ListImportErrorsQuery : IRequest<ImportErrorListResponse>
{
    public Guid ImportId { get; set; }
    public int? Limit { get; set; }
    public int? Cursor { get; set; }
}

public class ListImportErrorsQueryHandler(IApplicationDbContext db)
    : IRequestHandler<ListImportErrorsQuery, ImportErrorListResponse>
{
    public const int MaxLimit = 200;
    public const int DefaultLimit = 50;

    public async Task<ImportErrorListResponse> Handle(ListImportErrorsQuery query, CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(query.Limit ?? DefaultLimit, 1, MaxLimit);

        var q = db.ImportErrorLines
            .AsNoTracking()
            .Where(e => e.ImportId == query.ImportId);

        if (query.Cursor is int cursor && cursor > 0)
        {
            q = q.Where(e => e.LineNumber > cursor);
        }

        var rows = await q
            .OrderBy(e => e.LineNumber)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);

        var hasMore = rows.Count > limit;
        if (hasMore) rows.RemoveAt(rows.Count - 1);

        var nextCursor = hasMore && rows.Count > 0 ? rows[^1].LineNumber : (int?)null;

        return new ImportErrorListResponse(
            rows.Select(e => new ImportErrorDto(e.LineNumber, e.Kind, e.Reason, e.RawLine)).ToList(),
            nextCursor,
            hasMore,
            limit);
    }
}