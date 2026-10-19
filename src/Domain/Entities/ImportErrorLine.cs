namespace Mondje.Domain.Entities;

/// <summary>
/// One rejected line of an import file: the line number and the reason. Broken lines are
/// isolated this way, so they never block the valid rows of the same file.
/// </summary>
public class ImportErrorLine
{
    public const string KindFormat = "FORMAT";
    public const string KindValidation = "VALIDATION";

    public long Id { get; set; }

    public Guid ImportId { get; set; }

    /// <summary>1-based line number in the source file.</summary>
    public int LineNumber { get; set; }

    /// <summary>FORMAT (line unreadable) or VALIDATION (line readable but invalid).</summary>
    public string Kind { get; set; } = KindValidation;

    /// <summary>Human-readable reason, in French, safe to display to the user.</summary>
    public string Reason { get; set; } = default!;

    /// <summary>The offending line, truncated so a huge line cannot bloat the database.</summary>
    public string RawLine { get; set; } = default!;

    public DateTime CreatedAtUtc { get; set; }
}
