namespace Mondje.Application.Imports.Common;

public record ImportDto(
    Guid id,
    string fileName,
    string format,
    string? agency,
    string status,
    long fileSizeBytes,
    int totalLines,
    int insertedCount,
    int duplicateCount,
    int errorCount,
    string? failureReason,
    DateTime createdAtUtc,
    DateTime? startedAtUtc,
    DateTime? completedAtUtc);