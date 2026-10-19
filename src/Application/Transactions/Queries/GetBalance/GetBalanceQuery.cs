using System.Globalization;
using FluentValidation.Results;
using Mondje.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;
using ValidationException = Mondje.Application.Common.Exceptions.ValidationException;

namespace Mondje.Application.Transactions.Queries.GetBalance;

/// <summary>
/// Balance (CREDIT minus DEBIT) at a point in time over live rows only — the same row set
/// as the export, so the export total equals the displayed balance (challenge 14).
/// Challenge 11: computed by SUM over a narrow covering index (IX_Transactions_Balance),
/// never by recalculating the whole table.
/// </summary>
public class GetBalanceQuery : IRequest<BalanceDto>
{
    /// <summary>ISO 8601 cutoff (inclusive). Defaults to now, in UTC.</summary>
    public string? At { get; set; }
}

public record BalanceDto(
    DateTime at,
    long credit,
    long debit,
    long balance,
    long transactionCount,
    string currency);

public class GetBalanceQueryHandler(IApplicationDbContext db)
    : IRequestHandler<GetBalanceQuery, BalanceDto>
{
    public async Task<BalanceDto> Handle(GetBalanceQuery query, CancellationToken cancellationToken)
    {
        var at = DateTime.UtcNow;

        if (!string.IsNullOrWhiteSpace(query.At))
        {
            if (!DateTime.TryParse(
                    query.At,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                    out at))
            {
                throw new ValidationException(new[]
                {
                    new ValidationFailure(
                        nameof(query.At),
                        $"La date 'at' doit être une date ISO 8601 valide (ex. 2026-10-19T12:00:00Z), reçu '{query.At}'."),
                });
            }
        }

        var row = await db.Transactions
            .AsNoTracking()
            .Where(t => t.CreatedAtUtc <= at && t.DeletedAtUtc == null)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Credit = g.Where(t => t.Type == "CREDIT").Sum(t => (long?)t.Amount) ?? 0,
                Debit = g.Where(t => t.Type == "DEBIT").Sum(t => (long?)t.Amount) ?? 0,
                Count = g.Count(),
            })
            .FirstOrDefaultAsync(cancellationToken);

        var credit = row?.Credit ?? 0;
        var debit = row?.Debit ?? 0;

        return new BalanceDto(at, credit, debit, credit - debit, row?.Count ?? 0, "FCFA");
    }
}
