using System.Text.Json;

namespace Mondje.Web.Models;

public record ApiStatus(string status, DateTimeOffset utc, string? version);

public record DashboardDto(
    DateTime generatedAtUtc,
    DashboardLedgerDto ledger,
    DashboardImportsDto imports,
    List<DashboardAgencyDto> agencies);

public record DashboardLedgerDto(
    long transactions, long credits, long debits, long creditAmount, long debitAmount,
    long balance, long deleted, long imported, long manual);

public record DashboardImportsDto(
    int total, int pending, int running, int completed, int failed, int cancelled,
    long totalLines, long processedLines, long inserted, long duplicates, long errors,
    long? lastDurationMs, DateTime? lastCompletedAtUtc, string? lastFileName);

public record DashboardAgencyDto(
    string agency, long transactions, long creditAmount, long debitAmount, long balance);

public record TransactionDto(
    string id, string type, long amount, string description,
    DateTime createdAtUtc, string? agency, Guid? importId, int? sourceLine,
    DateTime? editedAtUtc, DateTime? deletedAtUtc, string? sourceFileName);

public record TransactionListResponse(
    List<TransactionDto> items, string? nextCursor, bool hasMore, int limit);

public record BalanceDto(
    DateTime at, long credit, long debit, long balance, long transactionCount, string currency);

public record ImportDto(
    Guid id, string fileName, string format, string? agency, string status,
    long fileSizeBytes, int totalLines, int insertedCount, int duplicateCount,
    int errorCount, string? failureReason, DateTime createdAtUtc,
    DateTime? startedAtUtc, DateTime? completedAtUtc, long processedLines,
    long bytesProcessed, int progressPercent, long? durationMs);

public record ImportListResponse(
    List<ImportDto> items, string? nextCursor, bool hasMore, int limit);

public record ImportErrorDto(
    int lineNumber, string kind, string reason, string rawLine);

public record ImportErrorListResponse(
    List<ImportErrorDto> items, int? nextCursor, bool hasMore, int limit);

public record CreateTransactionRequest(
    string id, string type, long amount, string description, string? agency);

public record UpdateTransactionRequest(long amount, string description);