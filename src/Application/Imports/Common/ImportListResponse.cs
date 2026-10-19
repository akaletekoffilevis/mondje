namespace Mondje.Application.Imports.Common;

public record ImportListResponse(
    IReadOnlyList<ImportDto> items,
    string? nextCursor,
    bool hasMore,
    int limit);