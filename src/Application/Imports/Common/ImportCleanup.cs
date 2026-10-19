using Mondje.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Mondje.Application.Imports.Common;

/// <summary>
/// Removes the transactions of an import without mapping them through the change
/// tracker. Used by the cancellation endpoint and by the import engine when it notices
/// a cancellation after writing its last chunk (challenge 9).
/// </summary>
public static class ImportCleanup
{
    /// <summary>
    /// Deletes every row of the import that was never edited. The statement is set-based
    /// and lets the database cascade the search words away: one round trip, flat memory,
    /// and a result that either applies completely or not at all. Deleting a large import
    /// can take longer than the 30 second default, so the command timeout is raised for
    /// this single statement.
    /// </summary>
    public static async Task<int> DeleteUneditedRowsAsync(
        IApplicationDbContext db, Guid importId, CancellationToken cancellationToken)
    {
        db.Database.SetCommandTimeout(TimeSpan.FromMinutes(10));

        return await db.Transactions
            .Where(t => t.ImportId == importId && t.EditedAtUtc == null)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
