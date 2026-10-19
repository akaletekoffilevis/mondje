using System.Globalization;
using System.Text;

namespace Mondje.Domain.Search;

/// <summary>
/// Normalises search words so a query matches regardless of case or accents.
/// The same function builds the index (imports, manual writes) and interprets user
/// queries, so both sides always agree: "Électricité", "electricite" and "ELECTRICITE"
/// all meet on the token "electricite".
/// </summary>
public static class SearchTokens
{
    public const int MaxTokenLength = 64;

    /// <summary>Words of a description, normalised, deduplicated, in first-seen order.</summary>
    public static IReadOnlyList<string> FromDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return Array.Empty<string>();
        }

        var tokens = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var current = new StringBuilder();

        foreach (var c in description)
        {
            if (char.IsLetterOrDigit(c))
            {
                current.Append(c);
                continue;
            }

            Flush(current, tokens, seen);
        }

        Flush(current, tokens, seen);
        return tokens;
    }

    /// <summary>Words of a user query, normalised like the indexed words.</summary>
    public static IReadOnlyList<string> FromQuery(string? query) => FromDescription(query);

    private static void Flush(StringBuilder current, List<string> tokens, HashSet<string> seen)
    {
        if (current.Length == 0)
        {
            return;
        }

        var token = Normalize(current.ToString());
        current.Clear();

        if (token.Length > MaxTokenLength)
        {
            // Truncated identically on both sides, so an over-long word still matches.
            token = token[..MaxTokenLength];
        }

        if (token.Length > 0 && seen.Add(token))
        {
            tokens.Add(token);
        }
    }

    /// <summary>Lowercase + diacritics removed (é → e), so words compare byte for byte.</summary>
    public static string Normalize(string word)
    {
        var decomposed = word.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(c);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
    }
}
