using Mondje.Domain.Entities;

namespace Mondje.Application.Common.Interfaces;

public interface IApplicationDbContext
{
    DbSet<Transaction> Transactions { get; }

    DbSet<ImportBatch> ImportBatches { get; }

    DbSet<ImportErrorLine> ImportErrorLines { get; }

    DbSet<TransactionToken> TransactionTokens { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
