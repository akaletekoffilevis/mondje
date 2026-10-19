using Mondje.Domain.Entities;

namespace Mondje.Application.Transactions.Common;

public record TransactionDto(
    string id,
    string type,
    long amount,
    string description,
    DateTime createdAtUtc,
    string? agency,
    Guid? importId,
    int? sourceLine,
    DateTime? editedAtUtc,
    DateTime? deletedAtUtc)
{
    public static TransactionDto From(Transaction t) => new(
        t.Id, t.Type, t.Amount, t.Description, t.CreatedAtUtc,
        t.Agency, t.ImportId, t.SourceLine, t.EditedAtUtc, t.DeletedAtUtc);
}
