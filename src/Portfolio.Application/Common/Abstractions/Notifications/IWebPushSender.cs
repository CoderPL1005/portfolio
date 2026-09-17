namespace Portfolio.Application.Common.Abstractions.Notifications;

public sealed record WebPushSubscriptionTarget(string Endpoint, string P256dh, string Auth);
public sealed record WebPushNotification(string Title, string Body, string Url);

public enum WebPushDeliveryStatus
{
    Succeeded,
    Gone,
    Failed,
}

public interface IWebPushSender
{
    Task<WebPushDeliveryStatus> SendAsync(
        WebPushSubscriptionTarget subscription,
        WebPushNotification notification,
        CancellationToken cancellationToken = default);
}
