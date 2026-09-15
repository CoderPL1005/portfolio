using Portfolio.Domain.Constants;

namespace Portfolio.Domain.Entities;

public sealed class JobApplication
{
    public Guid Id { get; set; }
    public Guid JobPostingId { get; set; }
    public string Status { get; set; } = JobApplicationStatuses.Draft;
    public string? Channel { get; set; }
    public string? ApplicationEmail { get; set; }
    public string? ApplicationUrl { get; set; }
    public string? ExternalApplicationId { get; set; }
    public DateTimeOffset? AppliedAt { get; set; }
    public DateTimeOffset? LastActivityAt { get; set; }
    public string? Notes { get; set; }
    public int Version { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public JobPosting JobPosting { get; set; } = null!;
    public ICollection<JobApplicationEvent> Events { get; set; } = [];
    public ICollection<JobApplicationDocument> Documents { get; set; } = [];
}
