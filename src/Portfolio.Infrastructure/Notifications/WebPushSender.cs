using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Portfolio.Application.Common.Abstractions.Notifications;
using Portfolio.Application.Common.Configuration;
using WebPush;

namespace Portfolio.Infrastructure.Notifications;

public sealed class WebPushSender(IOptions<WebPushSettings> options) : IWebPushSender
{
    public async Task<WebPushDeliveryStatus> SendAsync(
        WebPushSubscriptionTarget subscription,
        WebPushNotification notification,
        CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        var target = new PushSubscription(subscription.Endpoint, subscription.P256dh, subscription.Auth);
        var vapid = new VapidDetails(settings.Subject, settings.PublicKey, settings.PrivateKey);
        var payload = CreatePayload(notification);

        try
        {
            using var client = new WebPushClient();
            await client.SendNotificationAsync(target, payload, vapid, cancellationToken);
            return WebPushDeliveryStatus.Succeeded;
        }
        catch (WebPushException exception)
            when (exception.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            return WebPushDeliveryStatus.Gone;
        }
        catch (WebPushException)
        {
            return WebPushDeliveryStatus.Failed;
        }
        catch (HttpRequestException)
        {
            return WebPushDeliveryStatus.Failed;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return WebPushDeliveryStatus.Failed;
        }
    }

    internal static string CreatePayload(WebPushNotification notification)
    {
        EnsureSafeRelativeUrl(notification.Url);
        return JsonSerializer.Serialize(new
        {
            notification = new
            {
                title = notification.Title,
                body = notification.Body,
                icon = "/icons/icon-192.png",
                data = new
                {
                    url = notification.Url,
                    onActionClick = new Dictionary<string, object>
                    {
                        ["default"] = new
                        {
                            operation = "navigateLastFocusedOrOpen",
                            url = notification.Url,
                        },
                    },
                },
            },
        });
    }

    internal static void EnsureSafeRelativeUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url) ||
            !url.StartsWith("/", StringComparison.Ordinal) ||
            url.StartsWith("//", StringComparison.Ordinal) ||
            url.Contains('\\') ||
            Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            throw new ArgumentException("Web Push navigation must use a same-app relative path.", nameof(url));
        }
    }
}
