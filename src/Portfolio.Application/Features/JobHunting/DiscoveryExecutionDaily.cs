using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace Portfolio.Application.Features.JobHunting;

/// <summary>Six fixed slots (UTC+7): 07,10,13,16,19,22. Sixth slot is
/// reserved for a future count of actually submitted CV applications; it
/// currently performs NO discovery and consumes NO provider credits.</summary>
public static class DiscoveryDailySlots
{
    private static readonly IReadOnlyDictionary<string, int> UtcHours = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["0 0 * * *"] = 0, ["0 3 * * *"] = 3, ["0 6 * * *"] = 6,
        ["0 9 * * *"] = 9, ["0 12 * * *"] = 12, ["0 15 * * *"] = 15,
        // Safeguard: a backup trigger uses EXACTLY the primary slot identity.
        ["30 0 * * *"] = 0, ["30 3 * * *"] = 3, ["30 6 * * *"] = 6,
        ["30 9 * * *"] = 9, ["30 12 * * *"] = 12
    };

    public static bool IsKnownExpression(string? expression) => expression is not null && UtcHours.ContainsKey(expression);

    public static bool TryResolve(string? expression, DateTimeOffset now, out DateTimeOffset plannedUtc)
    {
        plannedUtc = default;
        if (expression is null || !UtcHours.TryGetValue(expression, out var hour)) return false;
        var utc = now.ToUniversalTime();
        plannedUtc = new DateTimeOffset(utc.Year, utc.Month, utc.Day, hour, 0, 0, TimeSpan.Zero);
        return utc >= plannedUtc && utc <= plannedUtc.AddMinutes(90);
    }

    public static Guid RequestId(DateTimeOffset plannedUtc)
    {
        if (plannedUtc.Offset != TimeSpan.Zero) throw new ArgumentException("A UTC slot is required.", nameof(plannedUtc));
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes("discovery-daily-v1|" +
            plannedUtc.ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture)));
        return new Guid(bytes.AsSpan(0, 16));
    }

    public static int SlotNumber(DateTimeOffset plannedUtc)
    {
        if (plannedUtc.Offset != TimeSpan.Zero || plannedUtc.Minute != 0 || plannedUtc.Second != 0 ||
            plannedUtc.Millisecond != 0 || !UtcHours.Values.Contains(plannedUtc.Hour))
            throw new ArgumentException("Unrecognized UTC daily slot.", nameof(plannedUtc));
        return plannedUtc.Hour / 3 + 1;
    }

    public static DiscoveryDailySearchWindow Window(DateTimeOffset plannedUtc)
    {
        var slot = SlotNumber(plannedUtc);
        if (slot == 6) throw new ArgumentException("Slot 6 is reserved and cannot request a provider.", nameof(plannedUtc));
        var vietnam = plannedUtc.ToOffset(TimeSpan.FromHours(7));
        var today = DateOnly.FromDateTime(vietnam.Date);
        var yesterday = today.AddDays(-1);
        var start = slot <= 3 ? yesterday : today;
        var end = slot == 1 ? yesterday : today;
        return new DiscoveryDailySearchWindow(slot,
            start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            "Asia/Ho_Chi_Minh");
    }

    public static DiscoveryDailySearchWindow ForScheduledRequest(Guid requestId, DateTimeOffset now)
    {
        foreach (var expression in UtcHours.Keys)
        {
            if (TryResolve(expression, now, out var planned) && RequestId(planned) == requestId)
                return Window(planned); // rejects reserved slot 6
        }
        throw new ArgumentException("Daily request has no current authorized slot.", nameof(requestId));
    }
}

public sealed record DiscoveryDailySearchWindow(int Slot, string StartTime, string EndTime, string TimeZone);

public sealed partial class DiscoveryExecutionService
{
    public async Task<object> StartScheduledAsync(string scheduleExpression, CancellationToken ct = default)
    {
        if (!Settings.DailyEnabled) return new { state = "DISABLED" };
        if (!DiscoveryDailySlots.TryResolve(scheduleExpression, clock.GetUtcNow(), out var planned))
        {
            // Schedules may arrive hours late. Never run an old window in a later slot.
            // An explicit, recognized but expired cron is a safe no-provider skip.
            if (DiscoveryDailySlots.IsKnownExpression(scheduleExpression))
                return new { state = "SKIPPED_EXPIRED", providerRequests = 0 };
            throw Invalid("scheduleExpression");
        }

        // Future rule: if fewer than FIVE CV submissions across the first FOUR slots,
        // broaden the search at 22:00. Application counting is not yet implemented,
        // so this slot is reserved (not run against a wrong proxy like fetched jobs).
        if (DiscoveryDailySlots.SlotNumber(planned) == 6)
            return new { state = "RESERVED_FUTURE_CV_APPLICATION_THRESHOLD", plannedUtc = planned,
                threshold = 5, observedCount = (int?)null, providerRequests = 0 };

        var key = DiscoveryDailySlots.RequestId(planned);
        var previous = await db.DiscoveryExecutions.AsNoTracking()
            .Where(x => x.ClientRequestId == key).Select(x => new { x.Id, x.State }).FirstOrDefaultAsync(ct);
        if (previous is not null) return new { state = "ALREADY_CREATED", executionId = (Guid?)previous.Id, plannedUtc = planned };

        if (await db.DiscoveryExecutions.AnyAsync(x => x.ActiveKey != null, ct))
            return new { state = "SKIPPED_ACTIVE", executionId = (Guid?)null, plannedUtc = planned };

        var day = new DateTimeOffset(planned.Year, planned.Month, planned.Day, 0, 0, 0, TimeSpan.Zero);
        if (await db.DiscoveryExecutions.CountAsync(x => x.TriggerSource == "DAILY_SCHEDULED" &&
            x.CreatedAt >= day && x.CreatedAt < day.AddDays(1), ct) >= 6)
            return new { state = "SKIPPED_DAILY_LIMIT", executionId = (Guid?)null, plannedUtc = planned };

        // BuildConfigurationAsync freezes the authorized day window. Scheduled Gemini is OFF.
        await StartAsync(key, "DAILY_SCHEDULED", ct, confirmGeminiFreeTier: false);
        var created = await db.DiscoveryExecutions.AsNoTracking().Where(x => x.ClientRequestId == key)
            .Select(x => new { x.Id, x.State }).SingleAsync(ct);
        return new { state = created.State, executionId = (Guid?)created.Id, plannedUtc = planned };
    }
}
