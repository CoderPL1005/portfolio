namespace Portfolio.Domain.Entities;

public sealed class SubmissionAttemptEvent
{
    public Guid Id { get; set; }
    public Guid SubmissionAttemptId { get; set; }
    public string? FromStatus { get; set; }
    public string ToStatus { get; set; } = null!;
    public Guid ActorAdminUserId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public SubmissionAttempt SubmissionAttempt { get; set; } = null!;
    public AdminUser ActorAdminUser { get; set; } = null!;
}
