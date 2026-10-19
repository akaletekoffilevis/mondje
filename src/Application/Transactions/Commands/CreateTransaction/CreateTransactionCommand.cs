using System.Text.Json;
using Mondje.Application.Common.Exceptions;
using Mondje.Application.Common.Interfaces;
using Mondje.Application.Transactions.Common;
using Mondje.Domain.Entities;
using Mondje.Domain.Search;
using Microsoft.EntityFrameworkCore;

namespace Mondje.Application.Transactions.Commands.CreateTransaction;

public class CreateTransactionCommand : IRequest<string>
{
    public string Id { get; set; } = default!;
    public string Type { get; set; } = default!;
    public JsonElement Amount { get; set; }
    public string Description { get; set; } = default!;
    public string? Agency { get; set; }
}

public class CreateTransactionCommandHandler(IApplicationDbContext db, TimeProvider time)
    : IRequestHandler<CreateTransactionCommand, string>
{
    public async Task<string> Handle(CreateTransactionCommand command, CancellationToken cancellationToken)
    {
        // UUIDs are case-insensitive: store the lowercase form, whatever the client sent.
        var id = command.Id.Trim().ToLowerInvariant();

        if (await db.Transactions.AnyAsync(t => t.Id == id, cancellationToken))
        {
            throw new ConflictException($"Une transaction avec l'identifiant '{id}' existe déjà.");
        }

        command.Amount.TryGetIntegerAmount(out var amount);

        var entity = new Transaction
        {
            Id = id,
            Type = command.Type,
            Amount = amount,
            Description = command.Description,
            Agency = command.Agency,
            CreatedAtUtc = Timestamp.Now(time),
        };

        db.Transactions.Add(entity);

        // Feed the word index in the same save: the row and its search words are
        // written atomically (challenge 3).
        foreach (var token in SearchTokens.FromDescription(entity.Description))
        {
            db.TransactionTokens.Add(new TransactionToken
            {
                Token = token,
                TransactionId = entity.Id,
                CreatedAtUtc = entity.CreatedAtUtc,
            });
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Racing duplicate (two simultaneous creations of the same UUID):
            // the primary key rejects the second insert. Everything else should
            // already be caught by validation, so this is a conflict.
            throw new ConflictException($"Une transaction avec l'identifiant '{id}' existe déjà.");
        }

        return id;
    }
}
