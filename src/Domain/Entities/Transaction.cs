namespace Mondje.Domain.Entities;

/// <summary>
/// A financial transaction in the Mondje ledger.
/// The Id is a client-provided UUID stored as a 36-character lowercase string;
/// SQL Server's default case-insensitive collation makes 'ABC-...' and 'abc-...'
/// collide in the primary key, so case duplicates are rejected at the database level.
/// </summary>
public class Transaction
{
    public const int MaxDescriptionLength = 255;
    public const string Credit = "CREDIT";
    public const string Debit = "DEBIT";

    /// <summary>UUID normalised to lowercase by the application before persisting.</summary>
    public string Id { get; set; } = default!;

    /// <summary>Strictly "CREDIT" or "DEBIT" (enforced by a database CHECK constraint).</summary>
    public string Type { get; set; } = default!;

    /// <summary>Integer amount in FCFA, strictly positive (enforced by a database CHECK constraint).</summary>
    public long Amount { get; set; }

    public string Description { get; set; } = default!;

    /// <summary>Server-side creation timestamp (UTC). Never supplied by clients.</summary>
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>Agency the transaction belongs to (Douala, Yaoundé, Bafoussam).</summary>
    public string? Agency { get; set; }

    /// <summary>Import batch this line came from, when created by a file import.</summary>
    public Guid? ImportId { get; set; }

    /// <summary>1-based line number in the source file (provenance, challenge 7).</summary>
    public int? SourceLine { get; set; }

    /// <summary>Set when the amount/description is edited by hand. Import cancellation
    /// (challenge 9) only deletes rows whose EditedAtUtc is still null.</summary>
    public DateTime? EditedAtUtc { get; set; }

    /// <summary>Soft delete marker; the row stays restorable for 24 hours.</summary>
    public DateTime? DeletedAtUtc { get; set; }
}
