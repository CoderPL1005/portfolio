namespace Portfolio.Domain.Entities;

public sealed class SubmissionAttempt
{
    public Guid Id { get; set; }
    public Guid JobApplicationId { get; set; }
    public string Provider { get; set; } = null!;
    public string Status { get; set; } = null!;
    public string IdempotencyKey { get; set; } = null!;
    public int PackageRevision { get; set; }
    public string PackageManifestHash { get; set; } = null!;
    public int ApplicationVersionAtCreation { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid CreatedByAdminUserId { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? ProviderSubmissionId { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }
    public int Version { get; set; } = 1;
    public JobApplication JobApplication { get; set; } = null!;
    public AdminUser CreatedByAdminUser { get; set; } = null!;
    public ICollection<SubmissionAttemptEvent> Events { get; set; } = [];
}
