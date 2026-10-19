using Mondje.Domain.Entities;

namespace Mondje.Application.Common.Interfaces;

public interface IApplicationDbContext
{
    DbSet<Transaction> Transactions { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
