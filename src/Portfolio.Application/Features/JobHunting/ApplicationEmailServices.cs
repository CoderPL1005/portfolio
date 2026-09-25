using Microsoft.EntityFrameworkCore;
using Portfolio.Application.Common.Abstractions.Notifications;
using Portfolio.Application.Common.Abstractions.Persistence;
using Portfolio.Application.Common.Abstractions.Submission;

namespace Portfolio.Application.Features.JobHunting;

public sealed class DeterministicApplicationEmailComposer : IApplicationEmailComposer
{
    public ApplicationEmailMessage Compose(
        string recipient, string sender, string candidateName, string companyName,
        string positionTitle, string attachmentFileName, string attachmentContentType,
        byte[] attachmentContent)
    {
        var subject = $"Application for {positionTitle} - {candidateName}";
        var body = $"Dear Hiring Team,\n\nI am writing to apply for the {positionTitle} position at {companyName}. Please find my CV attached for your consideration.\n\nThank you for your time.\n\nSincerely,\n{candidateName}";
        return new(recipient, sender, subject, body, attachmentFileName,
            attachmentContentType, attachmentContent);
    }
}

public interface IAdminJobNotificationSender
{
    Task SendAsync(string code, string message, Guid jobPostingId, CancellationToken cancellationToken = default);
}

public sealed class AdminJobNotificationSender(
    IApplicationDbContext db,
    IWebPushSender sender,
    TimeProvider clock) : IAdminJobNotificationSender
{
    public async Task SendAsync(string code, string message, Guid jobPostingId, CancellationToken cancellationToken = default)
    {
        var subscriptions = await db.PushSubscriptions.Where(item => item.IsActive)
            .OrderBy(item => item.CreatedAt).ThenBy(item => item.Id).ToListAsync(cancellationToken);
        foreach (var subscription in subscriptions)
        {
            WebPushDeliveryStatus result;
            try
            {
                result = await sender.SendAsync(
                    new(subscription.Endpoint, subscription.P256dh, subscription.Auth),
                    new("Job Agent", message, $"/admin/job-hunting/jobs/{jobPostingId}"),
                    cancellationToken);
            }
            catch
            {
                continue;
            }
            var now = clock.GetUtcNow();
            if (result == WebPushDeliveryStatus.Succeeded)
            {
                subscription.LastUsedAt = now;
                subscription.UpdatedAt = now;
            }
            else if (result == WebPushDeliveryStatus.Gone)
            {
                subscription.IsActive = false;
                subscription.UpdatedAt = now;
            }
        }
        try { await db.SaveChangesAsync(cancellationToken); }
        catch { /* Notification bookkeeping never changes the submission outcome. */ }
    }
}
