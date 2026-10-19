namespace Mondje.Domain.Entities;

/// <summary>
/// One word of a transaction description, normalised (lowercase, accents removed).
/// The word index answers the search challenge without scanning the ledger: the
/// clustered key (Token, CreatedAtUtc, TransactionId) keeps every word's rows already
/// ordered by date, so "find a word, newest first, 50 rows" is an index range read.
/// Rows are maintained on create, on edit and during imports.
/// </summary>
public class TransactionToken
{
    /// <summary>Normalised word (see <see cref="Search.SearchTokens"/>).</summary>
    public string Token { get; set; } = default!;

    /// <summary>Copy of the transaction date so the index itself is date-ordered.</summary>
    public DateTime CreatedAtUtc { get; set; }

    public string TransactionId { get; set; } = default!;
}
