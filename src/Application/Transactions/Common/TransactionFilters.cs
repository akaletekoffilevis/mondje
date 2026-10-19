using Mondje.Application.Common.Interfaces;
using Mondje.Domain.Entities;
using Mondje.Domain.Search;
using Microsoft.EntityFrameworkCore;

namespace Mondje.Application.Transactions.Common;

/// <summary>
/// Filter predicates shared by the list and export queries so both agree on which rows
/// exist. Soft-deleted rows are excluded by default: the export total must equal the
/// displayed balance (challenge 14).
/// </summary>
public static class TransactionFilters
{
    public static IQueryable<Transaction> Apply(
        IApplicationDbContext db,
        string? type,
        long? minAmount,
        long? maxAmount,
        string? descriptionSearch,
        bool? includeDeleted)
    {
        var q = BuildSource(db, descriptionSearch);

        if (includeDeleted != true)
        {
            q = q.Where(t => t.DeletedAtUtc == null);
        }

        if (!string.IsNullOrEmpty(type))
        {
            q = q.Where(t => t.Type == type);
        }

        if (minAmount is long min)
        {
            q = q.Where(t => t.Amount >= min);
        }

        if (maxAmount is long max)
        {
            q = q.Where(t => t.Amount <= max);
        }

        return q.AsNoTracking();
    }

    /// <summary>
    /// Searches a word in the description through the word index (challenge 3): the
    /// query starts from the token table, whose clustered key keeps each word's rows in
    /// date order, then joins the ledger. One word is the fast path; several words are
    /// ANDed and each one is still an index probe.
    /// </summary>
    private static IQueryable<Transaction> BuildSource(IApplicationDbContext db, string? descriptionSearch)
    {
        var words = SearchTokens.FromQuery(descriptionSearch);

        if (words.Count == 1)
        {
            var term = words[0];
            return from token in db.TransactionTokens
                   where token.Token == term
                   join t in db.Transactions on token.TransactionId equals t.Id
                   select t;
        }

        var q = db.Transactions.AsNoTracking();
        foreach (var word in words)
        {
            var term = word;
            q = q.Where(t => db.TransactionTokens.Any(k => k.TransactionId == t.Id && k.Token == term));
        }

        return q;
    }
}
