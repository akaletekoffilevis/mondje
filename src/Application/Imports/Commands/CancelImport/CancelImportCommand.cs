using Mondje.Application.Common.Exceptions;
using Mondje.Application.Common.Interfaces;
using Mondje.Domain.Entities;
using Mondje.Application.Transactions.Common;
using Microsoft.EntityFrameworkCore;

namespace Mondje.Application.Imports.Commands.CancelImport;

public class CancelImportCommand : IRequest
{
    public Guid ImportId { get; set; }
}

public class CancelImportCommandHandler(IApplicationDbContext db, TimeProvider time)
    : IRequestHandler<CancelImportCommand>
{
    public async Task Handle(CancelImportCommand command, CancellationToken cancellationToken)
    {
        var batch = await db.ImportBatches
            .FirstOrDefaultAsync(b => b.Id == command.ImportId, cancellationToken)
            ?? throw new NotFoundException($"Import '{command.ImportId}' introuvable.");

        var toDelete = await db.Transactions
            .Where(t => t.ImportId == command.ImportId && t.EditedAtUtc == null)
            .ToListAsync(cancellationToken);

        foreach (var t in toDelete)
        {
            db.Transactions.Remove(t);
        }

        batch.Status = ImportBatch.Cancelled;
        batch.CompletedAtUtc = Timestamp.Now(time);

        await db.SaveChangesAsync(cancellationToken);
    }
}