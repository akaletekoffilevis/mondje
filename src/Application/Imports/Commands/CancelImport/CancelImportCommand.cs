using Mondje.Application.Common.Exceptions;
using Mondje.Application.Common.Interfaces;
using Mondje.Application.Imports.Common;
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

        // Stop the engine first: as soon as the batch is CANCELLED a running import
        // breaks out at its next tick and removes whatever it still managed to write.
        batch.Status = ImportBatch.Cancelled;
        batch.CompletedAtUtc = Timestamp.Now(time);
        await db.SaveChangesAsync(cancellationToken);

        // Challenge 9: surgical delete. Only the rows of this import that were never
        // edited disappear; the deletion is a single set-based statement so it stays
        // fast and flat in memory even on a 100k-row import (see ImportCleanup).
        await ImportCleanup.DeleteUneditedRowsAsync(db, command.ImportId, cancellationToken);
    }
}