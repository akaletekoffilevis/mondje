namespace Mondje.Application.Imports.Common;

public record ImportErrorListResponse(
    IReadOnlyList<ImportErrorDto> items,
    int? nextCursor,
    bool hasMore,
    int limit);