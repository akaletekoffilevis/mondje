using System.Text.RegularExpressions;

namespace Mondje.Application.Transactions.Common;

public static partial class UuidFormat
{
    [GeneratedRegex("^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$")]
    public static partial Regex Pattern();

    public static bool IsValid(string? value) =>
        !string.IsNullOrWhiteSpace(value) && Pattern().IsMatch(value.Trim());
}
