using Mondje.Application.Common.Exceptions;
using Mondje.Application.Common.Interfaces;
using Mondje.Application.Transactions.Common;
using Microsoft.EntityFrameworkCore;

namespace Mondje.Application.Transactions.Queries.GetTransaction;

public class GetTransactionQuery : IRequest<TransactionDto>
{
    public string Id { get; set; } = default!;
}

public class GetTransactionQueryHandler(IApplicationDbContext db)
    : IRequestHandler<GetTransactionQuery, TransactionDto>
{
    public async Task<TransactionDto> Handle(GetTransactionQuery query, CancellationToken cancellationToken)
    {
        var id = query.Id.Trim().ToLowerInvariant();

        var entity = await db.Transactions
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken)
            ?? throw new NotFoundException($"Transaction '{id}' introuvable.");

        // One extra dictionary query resolves the source file name (challenge 7).
        var withSource = await TransactionDto.FromAsync(db, [entity], cancellationToken);
        return withSource[0];
    }
}
