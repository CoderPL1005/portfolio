namespace Portfolio.Domain.Entities;

// Append-only after-decision snapshot. Original classifier payload is not copied.
public sealed class DiscoveryCandidateReviewEvent
{
    public Guid Id { get; set; }
    public Guid CandidateId { get; set; }
    public int Version { get; set; }
    public string Status { get; set; } = null!;
    public string? ContentStatus { get; set; }
    public string? TargetStatus { get; set; }
    public string Notes { get; set; } = null!;
    public DateTimeOffset ReviewedAt { get; set; }
    public Guid ReviewedByAdminId { get; set; }
    public string ObservationPayloadHash { get; set; } = null!;
}
