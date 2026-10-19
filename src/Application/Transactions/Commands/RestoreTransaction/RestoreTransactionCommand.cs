using Mondje.Application.Common.Exceptions;
using Mondje.Application.Common.Interfaces;
using Mondje.Application.Transactions.Common;
using Microsoft.EntityFrameworkCore;

namespace Mondje.Application.Transactions.Commands.RestoreTransaction;

/// <summary>Restores a soft-deleted transaction while still inside the 24-hour window.</summary>
public class RestoreTransactionCommand : IRequest
{
    public string Id { get; set; } = default!;
}

public class RestoreTransactionCommandHandler(IApplicationDbContext db, TimeProvider time)
    : IRequestHandler<RestoreTransactionCommand>
{
    private static readonly TimeSpan RestoreWindow = TimeSpan.FromHours(24);

    public async Task Handle(RestoreTransactionCommand command, CancellationToken cancellationToken)
    {
        var id = command.Id.Trim().ToLowerInvariant();

        var entity = await db.Transactions
            .FirstOrDefaultAsync(t => t.Id == id && t.DeletedAtUtc != null, cancellationToken)
            ?? throw new NotFoundException($"Transaction '{id}' introuvable ou non supprimée.");

        if (Timestamp.Now(time) - entity.DeletedAtUtc!.Value > RestoreWindow)
        {
            throw new ConflictException(
                $"La fenêtre de restauration de 24 heures est dépassée pour la transaction '{id}'.");
        }

        entity.DeletedAtUtc = null;

        await db.SaveChangesAsync(cancellationToken);
    }
}
