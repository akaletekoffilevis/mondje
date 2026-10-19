using System.Text.Json;

namespace Mondje.Application.Transactions.Common;

public static class JsonElementExtensions
{
    /// <summary>
    /// Accepts a JSON integer (15000) or a digits-only JSON string ("15000") and returns
    /// the value as a long. Floats (15.5), exponent notation and out-of-range values are
    /// rejected: amounts must be integers with no decimals.
    /// </summary>
    public static bool TryGetIntegerAmount(this JsonElement element, out long value)
    {
        value = 0;

        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out value))
        {
            return value > 0;
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            var s = element.GetString();
            if (!string.IsNullOrEmpty(s) && s.All(char.IsAsciiDigit) && long.TryParse(s, out value))
            {
                return value > 0;
            }
        }

        value = 0;
        return false;
    }
}
