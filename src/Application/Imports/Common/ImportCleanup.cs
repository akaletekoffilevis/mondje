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
    /// Deletes every row of the import that was never edited. The statements are set-based:
    /// one round trip for the search words, one for the rows, flat memory, and a result that
    /// either applies completely or not at all. Deleting a large import can take longer than
    /// the 30 second default, so the command timeout is raised.
    /// </summary>
    public static async Task<int> DeleteUneditedRowsAsync(
        IApplicationDbContext db, Guid importId, CancellationToken cancellationToken)
    {
        db.Database.SetCommandTimeout(TimeSpan.FromMinutes(10));

        // The word table has no foreign key anymore (maintaining it made a 1M-line import
        // write hundreds of thousands of random index entries), so the words of the
        // cancelled rows are removed in one set-based pass before the rows themselves.
        await db.Database.ExecuteSqlRawAsync(
            "DELETE k FROM TransactionTokens k " +
            "JOIN Transactions t ON t.Id = k.TransactionId " +
            "WHERE t.ImportId = @p0 AND t.EditedAtUtc IS NULL",
            new object[] { importId },
            cancellationToken);

        return await db.Transactions
            .Where(t => t.ImportId == importId && t.EditedAtUtc == null)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
