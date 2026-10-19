using Mondje.Domain.Entities;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Mondje.Application.Common.Interfaces;

public interface IApplicationDbContext
{
    DatabaseFacade Database { get; }

    DbSet<Transaction> Transactions { get; }

    DbSet<ImportBatch> ImportBatches { get; }

    DbSet<ImportErrorLine> ImportErrorLines { get; }

    DbSet<TransactionToken> TransactionTokens { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
