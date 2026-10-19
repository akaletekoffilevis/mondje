using System.Text.Json;
using Mondje.Application.Common.Exceptions;
using Mondje.Application.Common.Interfaces;
using Mondje.Application.Transactions.Common;
using Mondje.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Mondje.Application.Transactions.Commands.UpdateTransaction;

/// <summary>Updates only the amount and the description, as required by the rules.</summary>
public class UpdateTransactionCommand : IRequest
{
    public string Id { get; set; } = default!;
    public JsonElement Amount { get; set; }
    public string Description { get; set; } = default!;
}

public class UpdateTransactionCommandHandler(IApplicationDbContext db, TimeProvider time)
    : IRequestHandler<UpdateTransactionCommand>
{
    public async Task Handle(UpdateTransactionCommand command, CancellationToken cancellationToken)
    {
        var id = command.Id.Trim().ToLowerInvariant();

        var entity = await db.Transactions
            .FirstOrDefaultAsync(t => t.Id == id && t.DeletedAtUtc == null, cancellationToken)
            ?? throw new NotFoundException($"Transaction '{id}' introuvable.");

        command.Amount.TryGetIntegerAmount(out var amount);

        entity.Amount = amount;
        entity.Description = command.Description;
        entity.EditedAtUtc = Timestamp.Now(time);

        await db.SaveChangesAsync(cancellationToken);
    }
}
