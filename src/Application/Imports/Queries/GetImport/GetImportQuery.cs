using Mondje.Application.Common.Exceptions;
using Mondje.Application.Common.Interfaces;
using Mondje.Application.Imports.Common;
using Mondje.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Mondje.Application.Imports.Queries.GetImport;

public class GetImportQuery : IRequest<ImportDto>
{
    public Guid Id { get; set; }
}

public class GetImportQueryHandler(IApplicationDbContext db)
    : IRequestHandler<GetImportQuery, ImportDto>
{
    public async Task<ImportDto> Handle(GetImportQuery query, CancellationToken cancellationToken)
    {
        var b = await db.ImportBatches
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == query.Id, cancellationToken)
            ?? throw new NotFoundException($"Import '{query.Id}' introuvable.");

        return ToDto(b);
    }

    internal static ImportDto ToDto(ImportBatch b) => new(
        b.Id, b.FileName, b.Format, b.Agency, b.Status, b.FileSizeBytes,
        b.TotalLines, b.InsertedCount, b.DuplicateCount, b.ErrorCount,
        b.FailureReason, b.CreatedAtUtc, b.StartedAtUtc, b.CompletedAtUtc);
}