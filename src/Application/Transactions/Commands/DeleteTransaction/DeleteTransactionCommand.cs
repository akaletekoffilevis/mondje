using Mondje.Application.Common.Exceptions;
using Mondje.Application.Common.Interfaces;
using Mondje.Application.Transactions.Common;
using Microsoft.EntityFrameworkCore;

namespace Mondje.Application.Transactions.Commands.DeleteTransaction;

/// <summary>Soft delete: the row stays restorable for 24 hours.</summary>
public class DeleteTransactionCommand : IRequest
{
    public string Id { get; set; } = default!;
}

public class DeleteTransactionCommandHandler(IApplicationDbContext db, TimeProvider time)
    : IRequestHandler<DeleteTransactionCommand>
{
    public async Task Handle(DeleteTransactionCommand command, CancellationToken cancellationToken)
    {
        var id = command.Id.Trim().ToLowerInvariant();

        var entity = await db.Transactions
            .FirstOrDefaultAsync(t => t.Id == id && t.DeletedAtUtc == null, cancellationToken)
            ?? throw new NotFoundException($"Transaction '{id}' introuvable ou déjà supprimée.");

        entity.DeletedAtUtc = Timestamp.Now(time);

        await db.SaveChangesAsync(cancellationToken);
    }
}
