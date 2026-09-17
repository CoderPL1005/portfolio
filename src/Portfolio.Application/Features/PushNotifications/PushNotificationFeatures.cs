using Microsoft.EntityFrameworkCore;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Common.Abstractions.Notifications;
using Portfolio.Application.Common.Abstractions.Persistence;
using Portfolio.Application.Common.Abstractions.Validation;
using Portfolio.Domain.Entities;

namespace Portfolio.Application.Features.PushNotifications;

public sealed record RegisterPushSubscriptionCommand(string Endpoint, string P256dh, string Auth) : IRequest<bool>;
public sealed record DisablePushSubscriptionCommand(string Endpoint) : IRequest<bool>;
public sealed record SendTestPushNotificationCommand : IRequest<PushTestSummary>;
public sealed record PushTestSummary(int Attempted, int Succeeded, int Deactivated, int Failed);

public sealed class RegisterPushSubscriptionCommandHandler(IApplicationDbContext db, TimeProvider clock)
    : IRequestHandler<RegisterPushSubscriptionCommand, bool>
{
    public async Task<bool> HandleAsync(RegisterPushSubscriptionCommand request, CancellationToken cancellationToken = default)
    {
        var endpoint = request.Endpoint.Trim();
        var now = clock.GetUtcNow();
        var subscription = await db.PushSubscriptions.SingleOrDefaultAsync(
            item => item.Endpoint == endpoint,
            cancellationToken);

        if (subscription is null)
        {
            subscription = new PushSubscription
            {
                Id = Guid.NewGuid(),
                Endpoint = endpoint,
                CreatedAt = now,
            };
            db.PushSubscriptions.Add(subscription);
        }

        subscription.P256dh = request.P256dh.Trim();
        subscription.Auth = request.Auth.Trim();
        subscription.IsActive = true;
        subscription.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}

public sealed class DisablePushSubscriptionCommandHandler(IApplicationDbContext db, TimeProvider clock)
    : IRequestHandler<DisablePushSubscriptionCommand, bool>
{
    public async Task<bool> HandleAsync(DisablePushSubscriptionCommand request, CancellationToken cancellationToken = default)
    {
        var endpoint = request.Endpoint.Trim();
        var subscription = await db.PushSubscriptions.SingleOrDefaultAsync(
            item => item.Endpoint == endpoint,
            cancellationToken);

        if (subscription is null || !subscription.IsActive)
        {
            return true;
        }

        subscription.IsActive = false;
        subscription.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}

public sealed class SendTestPushNotificationCommandHandler(
    IApplicationDbContext db,
    IWebPushSender sender,
    TimeProvider clock)
    : IRequestHandler<SendTestPushNotificationCommand, PushTestSummary>
{
    private static readonly WebPushNotification Notification = new(
        "Job Agent",
        "Web Push is working on your iPhone.",
        "/admin/job-hunting/jobs");

    public async Task<PushTestSummary> HandleAsync(
        SendTestPushNotificationCommand request,
        CancellationToken cancellationToken = default)
    {
        var subscriptions = await db.PushSubscriptions
            .Where(item => item.IsActive)
            .OrderBy(item => item.CreatedAt)
            .ThenBy(item => item.Id)
            .ToListAsync(cancellationToken);
        var succeeded = 0;
        var deactivated = 0;
        var failed = 0;

        foreach (var subscription in subscriptions)
        {
            var result = await sender.SendAsync(
                new WebPushSubscriptionTarget(subscription.Endpoint, subscription.P256dh, subscription.Auth),
                Notification,
                cancellationToken);
            var now = clock.GetUtcNow();

            switch (result)
            {
                case WebPushDeliveryStatus.Succeeded:
                    subscription.LastUsedAt = now;
                    subscription.UpdatedAt = now;
                    succeeded++;
                    break;
                case WebPushDeliveryStatus.Gone:
                    subscription.IsActive = false;
                    subscription.UpdatedAt = now;
                    deactivated++;
                    break;
                default:
                    failed++;
                    break;
            }
        }

        if (succeeded > 0 || deactivated > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return new PushTestSummary(subscriptions.Count, succeeded, deactivated, failed);
    }
}

public sealed class RegisterPushSubscriptionCommandValidator : IRequestValidator<RegisterPushSubscriptionCommand>
{
    public Task<IReadOnlyCollection<ValidationFailure>> ValidateAsync(
        RegisterPushSubscriptionCommand request,
        CancellationToken cancellationToken = default)
    {
        var failures = PushSubscriptionValidation.Endpoint(request.Endpoint);
        PushSubscriptionValidation.Secret(failures, "p256dh", request.P256dh);
        PushSubscriptionValidation.Secret(failures, "auth", request.Auth);
        return Task.FromResult<IReadOnlyCollection<ValidationFailure>>(failures);
    }
}

public sealed class DisablePushSubscriptionCommandValidator : IRequestValidator<DisablePushSubscriptionCommand>
{
    public Task<IReadOnlyCollection<ValidationFailure>> ValidateAsync(
        DisablePushSubscriptionCommand request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyCollection<ValidationFailure>>(PushSubscriptionValidation.Endpoint(request.Endpoint));
}

internal static class PushSubscriptionValidation
{
    public static List<ValidationFailure> Endpoint(string? value)
    {
        var failures = new List<ValidationFailure>();
        if (string.IsNullOrWhiteSpace(value))
        {
            failures.Add(new("endpoint", "endpoint is required."));
        }
        else if (value.Length > 2048)
        {
            failures.Add(new("endpoint", "endpoint must not exceed 2048 characters."));
        }
        else if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            failures.Add(new("endpoint", "endpoint must be an absolute HTTPS URL."));
        }

        return failures;
    }

    public static void Secret(ICollection<ValidationFailure> failures, string property, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            failures.Add(new(property, $"{property} is required."));
        }
        else if (value.Length > 512)
        {
            failures.Add(new(property, $"{property} must not exceed 512 characters."));
        }
        else if (value.Any(char.IsWhiteSpace))
        {
            failures.Add(new(property, $"{property} must not contain whitespace."));
        }
    }
}
