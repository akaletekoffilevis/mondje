using Mondje.Application.Transactions.Common;
using NUnit.Framework;
using Shouldly;

namespace Mondje.Application.UnitTests.Transactions.Common;

public class TimestampTests
{
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    [Test]
    public void Timestamp_is_utc()
    {
        var time = new FixedTimeProvider(new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero));

        Timestamp.Now(time).Kind.ShouldBe(DateTimeKind.Utc);
    }

    [Test]
    public void Timestamp_is_truncated_to_milliseconds_like_datetime2_3()
    {
        var precise = new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero)
            .AddTicks(1234567); // 123.4567 ms

        var result = Timestamp.Now(new FixedTimeProvider(precise));

        result.Ticks.ShouldBe(precise.UtcDateTime.Ticks - (precise.UtcDateTime.Ticks % TimeSpan.TicksPerMillisecond));
        (result.Ticks % TimeSpan.TicksPerMillisecond).ShouldBe(0);
    }

    [Test]
    public void Two_calls_at_the_same_instant_are_equal()
    {
        var time = new FixedTimeProvider(new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero));

        Timestamp.Now(time).ShouldBe(Timestamp.Now(time));
    }
}
