using System.Text.Json;
using Portfolio.Application.Common.Abstractions.Notifications;
using Portfolio.Infrastructure.Notifications;

namespace Portfolio.IntegrationTests.PortfolioContent;

public sealed class WebPushSenderTests
{
    [Fact]
    public void Payload_uses_angular_click_action_with_safe_same_app_navigation()
    {
        var payload = WebPushSender.CreatePayload(new WebPushNotification(
            "Job Agent",
            "Web Push is working on your iPhone.",
            "/admin/job-hunting/jobs"));
        using var document = JsonDocument.Parse(payload);
        var notification = document.RootElement.GetProperty("notification");
        var click = notification.GetProperty("data").GetProperty("onActionClick").GetProperty("default");

        Assert.Equal("Job Agent", notification.GetProperty("title").GetString());
        Assert.Equal("Web Push is working on your iPhone.", notification.GetProperty("body").GetString());
        Assert.Equal("navigateLastFocusedOrOpen", click.GetProperty("operation").GetString());
        Assert.Equal("/admin/job-hunting/jobs", click.GetProperty("url").GetString());
    }

    [Theory]
    [InlineData("https://evil.example/phish")]
    [InlineData("//evil.example/phish")]
    [InlineData("/\\evil")]
    [InlineData("")]
    public void Payload_rejects_non_relative_or_unsafe_navigation(string url)
    {
        Assert.Throws<ArgumentException>(() => WebPushSender.CreatePayload(
            new WebPushNotification("Title", "Body", url)));
    }
}
