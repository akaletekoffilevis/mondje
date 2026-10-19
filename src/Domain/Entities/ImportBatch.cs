namespace Mondje.Domain.Entities;

/// <summary>
/// One file import run. The batch row is the durable state of the import: it survives a
/// server restart so an interrupted import can be resumed (challenge 12) and every
/// transaction inserted by the file keeps a link to it (challenge 7).
/// </summary>
public class ImportBatch
{
    public const string Pending = "PENDING";
    public const string Running = "RUNNING";
    public const string Completed = "COMPLETED";
    public const string Failed = "FAILED";
    public const string Cancelled = "CANCELLED";

    public const string FormatTxt = "TXT";
    public const string FormatNdjson = "NDJSON";

    public Guid Id { get; set; }

    /// <summary>Original client file name, kept for display only.</summary>
    public string FileName { get; set; } = default!;

    /// <summary>Name of the file kept in the import folder (used to resume an import).</summary>
    public string StoredFileName { get; set; } = default!;

    public long FileSizeBytes { get; set; }

    /// <summary>TXT (id;type;montant;description) or NDJSON (one JSON object per line).</summary>
    public string Format { get; set; } = FormatTxt;

    /// <summary>Agency the whole file is attributed to.</summary>
    public string? Agency { get; set; }

    public string Status { get; set; } = Pending;

    public int TotalLines { get; set; }

    /// <summary>Rows actually written to the ledger.</summary>
    public int InsertedCount { get; set; }

    /// <summary>Rows skipped because their UUID already existed (first one wins).</summary>
    public int DuplicateCount { get; set; }

    /// <summary>Rows rejected as invalid; details are in <see cref="ImportErrorLine"/>.</summary>
    public int ErrorCount { get; set; }

    /// <summary>Technical reason the import failed (never sent as-is to a client).</summary>
    public string? FailureReason { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? StartedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    /// <summary>Lines fully processed and written so far (live progress + crash resume).</summary>
    public long ProcessedLines { get; set; }

    /// <summary>File offset (bytes) after the last flushed line: the resume point.</summary>
    public long BytesProcessed { get; set; }

    /// <summary>Request identifier that triggered the import, for log traceability.</summary>
    public string? RequestId { get; set; }
}
