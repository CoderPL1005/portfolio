using Portfolio.Application.Features.JobHunting;

namespace Portfolio.UnitTests.PortfolioContent;

public sealed class DiscoveryDailySlotsTests
{
    [Theory]
    [InlineData("0 0 * * *", 0)] [InlineData("0 3 * * *", 3)]
    [InlineData("0 6 * * *", 6)] [InlineData("0 9 * * *", 9)]
    [InlineData("0 12 * * *", 12)] [InlineData("0 15 * * *", 15)]
    public void All_six_slots_resolve_on_time(string expression, int hour)
    {
        var now = new DateTimeOffset(2026, 11, 2, hour, 18, 0, TimeSpan.Zero);
        Assert.True(DiscoveryDailySlots.TryResolve(expression, now, out var planned));
        Assert.Equal(hour, planned.Hour);
        Assert.Equal(0, planned.Minute);
        Assert.Equal(TimeSpan.Zero, planned.Offset);
        Assert.Equal(DiscoveryDailySlots.RequestId(planned), DiscoveryDailySlots.RequestId(planned));
    }

    [Theory]
    [InlineData(0, 1, "2026-11-01", "2026-11-01")]
    [InlineData(3, 2, "2026-11-01", "2026-11-02")]
    [InlineData(6, 3, "2026-11-01", "2026-11-02")]
    [InlineData(9, 4, "2026-11-02", "2026-11-02")]
    [InlineData(12, 5, "2026-11-02", "2026-11-02")]
    public void Windows_follow_local_Vietnam_calendar(int utcHour, int slot, string start, string end)
    {
        var when = new DateTimeOffset(2026, 11, 2, utcHour, 0, 0, TimeSpan.Zero);
        var window = DiscoveryDailySlots.Window(when);
        Assert.Equal(slot, window.Slot);
        Assert.Equal(start, window.StartTime);
        Assert.Equal(end, window.EndTime);
        Assert.Equal("Asia/Ho_Chi_Minh", window.TimeZone);
        Assert.Equal(window, DiscoveryDailySlots.ForScheduledRequest(DiscoveryDailySlots.RequestId(when), when.AddMinutes(18)));
    }

    [Fact]
    public void Sixth_slot_must_not_create_a_search_window()
    {
        var planned = new DateTimeOffset(2026, 11, 2, 15, 0, 0, TimeSpan.Zero);
        Assert.Equal(6, DiscoveryDailySlots.SlotNumber(planned));
        Assert.Throws<ArgumentException>(() => DiscoveryDailySlots.Window(planned));
        Assert.Throws<ArgumentException>(() => DiscoveryDailySlots.ForScheduledRequest(DiscoveryDailySlots.RequestId(planned), planned.AddMinutes(18)));
    }

    [Fact]
    public void Rejects_early_late_and_unlisted_schedule()
    {
        var when = new DateTimeOffset(2026, 11, 1, 23, 59, 0, TimeSpan.Zero);
        Assert.False(DiscoveryDailySlots.TryResolve("0 0 * * *", when, out _));
        Assert.False(DiscoveryDailySlots.TryResolve("0 0 * * *", when.AddMinutes(92), out _));
        Assert.False(DiscoveryDailySlots.TryResolve("0 1 * * *", when, out _));
        Assert.False(DiscoveryDailySlots.TryResolve("0 0 * * *; touch pwn", when, out _));
    }

    [Fact]
    public void Each_slot_and_day_has_distinct_stable_request_id()
    {
        var day = new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.Zero);
        var keys = Enumerable.Range(0, 6).Select(i => DiscoveryDailySlots.RequestId(day.AddHours(i * 3))).ToArray();
        Assert.Equal(6, keys.Distinct().Count());
        Assert.NotEqual(keys[0], DiscoveryDailySlots.RequestId(day.AddDays(1)));
    }
}
