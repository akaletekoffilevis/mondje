namespace Mondje.Application.Imports.Common;

public record ImportErrorDto(
    int lineNumber,
    string kind,
    string reason,
    string rawLine);