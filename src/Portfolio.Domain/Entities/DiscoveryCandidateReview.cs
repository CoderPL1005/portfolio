namespace Portfolio.Domain.Entities;

public sealed class DiscoveryCandidateReview
{
    public Guid CandidateId { get; set; }
    public int Version { get; set; }
    public string Status { get; set; } = null!;
    public string? ContentStatus { get; set; }
    public string? TargetStatus { get; set; }
    public string Notes { get; set; } = null!;
    public DateTimeOffset ReviewedAt { get; set; }
    public Guid ReviewedByAdminId { get; set; }
}
