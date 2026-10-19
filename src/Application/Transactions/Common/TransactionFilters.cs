using Mondje.Domain.Entities;

namespace Mondje.Application.Transactions.Common;

/// <summary>
/// Filter predicates shared by the list, export and balance queries so every read path
/// agrees on which rows exist. Soft-deleted rows are excluded by default: the export
/// total must equal the displayed balance (challenge 14).
/// </summary>
public static class TransactionFilters
{
    public static IQueryable<Transaction> Apply(
        IQueryable<Transaction> source,
        string? type,
        long? minAmount,
        long? maxAmount,
        string? descriptionSearch,
        bool? includeDeleted)
    {
        var q = source;

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

        if (!string.IsNullOrEmpty(descriptionSearch))
        {
            q = q.Where(t => t.Description.Contains(descriptionSearch));
        }

        return q;
    }
}
