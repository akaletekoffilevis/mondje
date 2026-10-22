using Mondje.Application.Common.Interfaces;
using Mondje.Domain.Constants;
using Mondje.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Mondje.Application.Dashboard.GetDashboard;

/// <summary>
/// Aggregated figures for the dashboard (challenge 15): ledger totals, import outcomes
/// (time, lines processed, errors) and a per-agency breakdown. Everything is read with
/// set-based SQL aggregates; no transaction row is materialised.
/// </summary>
public class GetDashboardQuery : IRequest<DashboardDto>;

public record DashboardDto(
    DateTime generatedAtUtc,
    DashboardLedgerDto ledger,
    DashboardImportsDto imports,
    IReadOnlyList<DashboardAgencyDto> agencies);

public record DashboardLedgerDto(
    long transactions,
    long credits,
    long debits,
    long creditAmount,
    long debitAmount,
    long balance,
    long deleted,
    long imported,
    long manual);

public record DashboardImportsDto(
    int total,
    int pending,
    int running,
    int completed,
    int failed,
    int cancelled,
    long totalLines,
    long processedLines,
    long inserted,
    long duplicates,
    long errors,
    long? lastDurationMs,
    DateTime? lastCompletedAtUtc,
    string? lastFileName);

public record DashboardAgencyDto(
    string agency,
    long transactions,
    long creditAmount,
    long debitAmount,
    long balance);

public class GetDashboardQueryHandler(IApplicationDbContext db, TimeProvider time)
    : IRequestHandler<GetDashboardQuery, DashboardDto>
{
    public async Task<DashboardDto> Handle(GetDashboardQuery query, CancellationToken cancellationToken)
    {
        // One grouped query over live rows only (same row set as the balance and the
        // export, so the three figures always agree - challenge 14).
        var byAgency = await db.Transactions
            .AsNoTracking()
            .Where(t => t.DeletedAtUtc == null)
            .GroupBy(t => t.Agency)
            .Select(g => new AgencyAggregate(
                g.Key,
                g.Count(),
                g.Count(t => t.Type == Transaction.Credit),
                g.Count(t => t.Type == Transaction.Debit),
                g.Where(t => t.Type == Transaction.Credit).Sum(t => (long?)t.Amount) ?? 0,
                g.Where(t => t.Type == Transaction.Debit).Sum(t => (long?)t.Amount) ?? 0,
                g.Count(t => t.ImportId != null)))
            .ToListAsync(cancellationToken);

        var deleted = await db.Transactions
            .AsNoTracking()
            .CountAsync(t => t.DeletedAtUtc != null, cancellationToken);

        var creditAmount = byAgency.Sum(a => a.CreditAmount);
        var debitAmount = byAgency.Sum(a => a.DebitAmount);

        var ledger = new DashboardLedgerDto(
            transactions: byAgency.Sum(a => (long)a.Count),
            credits: byAgency.Sum(a => (long)a.Credits),
            debits: byAgency.Sum(a => (long)a.Debits),
            creditAmount: creditAmount,
            debitAmount: debitAmount,
            balance: creditAmount - debitAmount,
            deleted: deleted,
            imported: byAgency.Sum(a => (long)a.Imported),
            manual: byAgency.Sum(a => (long)(a.Count - a.Imported)));

        // Import batches are few compared to transactions, so materialising them to
        // compute outcome counters and the last duration is cheap and keeps the SQL simple.
        var batches = await db.ImportBatches
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var lastCompleted = batches
            .Where(b => b.CompletedAtUtc is not null)
            .OrderByDescending(b => b.CompletedAtUtc)
            .FirstOrDefault();

        var imports = new DashboardImportsDto(
            total: batches.Count,
            pending: batches.Count(b => b.Status == ImportBatch.Pending),
            running: batches.Count(b => b.Status == ImportBatch.Running),
            completed: batches.Count(b => b.Status == ImportBatch.Completed),
            failed: batches.Count(b => b.Status == ImportBatch.Failed),
            cancelled: batches.Count(b => b.Status == ImportBatch.Cancelled),
            totalLines: batches.Sum(b => (long)b.TotalLines),
            processedLines: batches.Sum(b => b.ProcessedLines),
            inserted: batches.Sum(b => (long)b.InsertedCount),
            duplicates: batches.Sum(b => (long)b.DuplicateCount),
            errors: batches.Sum(b => (long)b.ErrorCount),
            lastDurationMs: lastCompleted?.StartedAtUtc is DateTime started
                && lastCompleted.CompletedAtUtc is DateTime ended
                    ? (long)Math.Max(0, (ended - started).TotalMilliseconds)
                    : null,
            lastCompletedAtUtc: lastCompleted?.CompletedAtUtc,
            lastFileName: lastCompleted?.FileName);

        // Always show the three cooperatives, plus "Non attribuée" when rows exist
        // outside an agency. Agencies with no transaction still appear with zeros.
        var lookup = byAgency.ToDictionary(a => a.Agency ?? string.Empty);
        var agencies = new List<DashboardAgencyDto>(Agencies.All.Count + 1);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var agency in Agencies.All)
        {
            seen.Add(agency);
            agencies.Add(ToDto(agency, lookup.GetValueOrDefault(agency)));
        }

        foreach (var extra in byAgency.Where(a => a.Agency is null || !seen.Contains(a.Agency)))
        {
            agencies.Add(ToDto(extra.Agency ?? "Non attribuée", extra));
        }

        return new DashboardDto(
            generatedAtUtc: time.GetUtcNow().UtcDateTime,
            ledger,
            imports,
            agencies);
    }

    private static DashboardAgencyDto ToDto(string name, AgencyAggregate? row) =>
        row is null
            ? new DashboardAgencyDto(name, 0, 0, 0, 0)
            : new DashboardAgencyDto(
                name,
                row.Count,
                row.CreditAmount,
                row.DebitAmount,
                row.CreditAmount - row.DebitAmount);

    private sealed record AgencyAggregate(
        string? Agency,
        int Count,
        int Credits,
        int Debits,
        long CreditAmount,
        long DebitAmount,
        int Imported);
}
