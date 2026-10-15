using Xunit;

namespace TinyEvents.Tests.Operations;

public sealed class TinyOutboxStatusTests
{
    private static readonly DateTimeOffset ObservedAtUtc =
        new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void No_pending_message_has_no_pending_age()
    {
        var status = new TinyOutboxStatus { ObservedAtUtc = ObservedAtUtc };

        Assert.Null(status.OldestPendingAge);
    }

    [Theory]
    [InlineData(-90, 90)]
    [InlineData(0, 0)]
    [InlineData(30, 0)]
    public void Pending_age_uses_the_observation_time_and_never_becomes_negative(
        int creationOffsetSeconds,
        int expectedAgeSeconds)
    {
        var status = new TinyOutboxStatus
        {
            ObservedAtUtc = ObservedAtUtc,
            OldestPendingAtUtc = ObservedAtUtc.AddSeconds(creationOffsetSeconds)
        };

        Assert.Equal(TimeSpan.FromSeconds(expectedAgeSeconds), status.OldestPendingAge);
    }
}
