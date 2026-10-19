namespace Mondje.Application.Transactions.Common;

public static class Timestamp
{
    /// <summary>
    /// Server-side UTC timestamp truncated to milliseconds, matching datetime2(3).
    /// Truncating here (instead of letting SQL Server round) keeps in-memory values
    /// identical to stored values, which matters for cursor comparisons.
    /// </summary>
    public static DateTime Now(TimeProvider time)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var truncated = now.Ticks - (now.Ticks % TimeSpan.TicksPerMillisecond);
        return new DateTime(truncated, DateTimeKind.Utc);
    }
}
