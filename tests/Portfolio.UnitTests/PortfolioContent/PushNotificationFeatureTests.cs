using Microsoft.EntityFrameworkCore;
using Portfolio.Application.Common.Abstractions.Notifications;
using Portfolio.Application.Features.PushNotifications;
using Portfolio.Domain.Entities;

namespace Portfolio.UnitTests.PortfolioContent;

public sealed class PushNotificationFeatureTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 17, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task Registration_is_idempotent_updates_keys_and_reactivates_existing_endpoint()
    {
        await using var db = CreateContext();
        var originalCreatedAt = Now.AddDays(-1);
        db.PushSubscriptions.Add(new PushSubscription
        {
            Id = Guid.NewGuid(), Endpoint = "https://push.example/subscription", P256dh = "old-key",
            Auth = "old-auth", CreatedAt = originalCreatedAt, UpdatedAt = originalCreatedAt, IsActive = false,
        });
        await db.SaveChangesAsync();
        var handler = new RegisterPushSubscriptionCommandHandler(db, new FixedTimeProvider(Now));

        await handler.HandleAsync(new RegisterPushSubscriptionCommand(
            " https://push.example/subscription ", "new-key", "new-auth"));
        await handler.HandleAsync(new RegisterPushSubscriptionCommand(
            "https://push.example/subscription", "newest-key", "newest-auth"));

        var rows = await db.PushSubscriptions.ToListAsync();
        var row = Assert.Single(rows);
        Assert.True(row.IsActive);
        Assert.Equal("newest-key", row.P256dh);
        Assert.Equal("newest-auth", row.Auth);
        Assert.Equal(originalCreatedAt, row.CreatedAt);
        Assert.Equal(Now, row.UpdatedAt);
    }

    [Fact]
    public async Task Disable_is_idempotent_and_preserves_the_row()
    {
        await using var db = CreateContext();
        var row = Subscription("https://push.example/disable");
        db.PushSubscriptions.Add(row);
        await db.SaveChangesAsync();
        var handler = new DisablePushSubscriptionCommandHandler(db, new FixedTimeProvider(Now));

        Assert.True(await handler.HandleAsync(new DisablePushSubscriptionCommand(row.Endpoint)));
        Assert.True(await handler.HandleAsync(new DisablePushSubscriptionCommand(row.Endpoint)));

        Assert.False(row.IsActive);
        Assert.Equal(Now, row.UpdatedAt);
        Assert.Single(await db.PushSubscriptions.ToListAsync());
    }

    [Fact]
    public async Task Validators_reject_invalid_endpoints_keys_and_whitespace_secrets()
    {
        var register = new RegisterPushSubscriptionCommandValidator();
        var disable = new DisablePushSubscriptionCommandValidator();

        var registrationFailures = await register.ValidateAsync(
            new RegisterPushSubscriptionCommand("http://push.example/sub", "bad key", ""));
        var disableFailures = await disable.ValidateAsync(new DisablePushSubscriptionCommand("relative"));

        Assert.Contains(registrationFailures, failure => failure.PropertyName == "endpoint");
        Assert.Contains(registrationFailures, failure => failure.PropertyName == "p256dh");
        Assert.Contains(registrationFailures, failure => failure.PropertyName == "auth");
        Assert.Single(disableFailures, failure => failure.PropertyName == "endpoint");
    }

    [Fact]
    public async Task Test_send_updates_success_deactivates_gone_counts_failures_and_excludes_inactive()
    {
        await using var db = CreateContext();
        var success = Subscription("https://push.example/success", Now.AddMinutes(-4));
        var gone = Subscription("https://push.example/gone", Now.AddMinutes(-3));
        var failed = Subscription("https://push.example/failed", Now.AddMinutes(-2));
        var inactive = Subscription("https://push.example/inactive", Now.AddMinutes(-1));
        inactive.IsActive = false;
        db.PushSubscriptions.AddRange(success, gone, failed, inactive);
        await db.SaveChangesAsync();
        var sender = new FakeSender(
            WebPushDeliveryStatus.Succeeded,
            WebPushDeliveryStatus.Gone,
            WebPushDeliveryStatus.Failed);
        var handler = new SendTestPushNotificationCommandHandler(db, sender, new FixedTimeProvider(Now));

        var summary = await handler.HandleAsync(new SendTestPushNotificationCommand());

        Assert.Equal(new PushTestSummary(3, 1, 1, 1), summary);
        Assert.Equal(Now, success.LastUsedAt);
        Assert.True(success.IsActive);
        Assert.False(gone.IsActive);
        Assert.Null(failed.LastUsedAt);
        Assert.False(inactive.IsActive);
        Assert.Equal(3, sender.Targets.Count);
        Assert.DoesNotContain(sender.Targets, target => target.Endpoint == inactive.Endpoint);
        Assert.All(sender.Notifications, notification =>
        {
            Assert.Equal("Job Agent", notification.Title);
            Assert.Equal("/admin/job-hunting/jobs", notification.Url);
            Assert.StartsWith("/", notification.Url, StringComparison.Ordinal);
        });
        Assert.Equal(
            [nameof(PushTestSummary.Attempted), nameof(PushTestSummary.Deactivated), nameof(PushTestSummary.Failed), nameof(PushTestSummary.Succeeded)],
            typeof(PushTestSummary).GetProperties().Select(property => property.Name).Order().ToArray());
    }

    private static ContentTestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ContentTestDbContext>()
            .UseInMemoryDatabase($"push-{Guid.NewGuid()}")
            .Options;
        return new ContentTestDbContext(options);
    }

    private static PushSubscription Subscription(string endpoint, DateTimeOffset? createdAt = null) => new()
    {
        Id = Guid.NewGuid(), Endpoint = endpoint, P256dh = "public-key", Auth = "auth-key",
        CreatedAt = createdAt ?? Now.AddHours(-1), UpdatedAt = createdAt ?? Now.AddHours(-1), IsActive = true,
    };

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeSender(params WebPushDeliveryStatus[] results) : IWebPushSender
    {
        private readonly Queue<WebPushDeliveryStatus> results = new(results);
        public List<WebPushSubscriptionTarget> Targets { get; } = [];
        public List<WebPushNotification> Notifications { get; } = [];

        public Task<WebPushDeliveryStatus> SendAsync(
            WebPushSubscriptionTarget subscription,
            WebPushNotification notification,
            CancellationToken cancellationToken = default)
        {
            Targets.Add(subscription);
            Notifications.Add(notification);
            return Task.FromResult(results.Dequeue());
        }
    }
}
