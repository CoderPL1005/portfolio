namespace Portfolio.Domain.Entities;

// An immutable review observation, NOT a verified Facebook object or JobPosting.
public sealed class DiscoveryCandidate
{
    public Guid Id { get; set; }
    public string RunId { get; set; } = null!;
    public string Query { get; set; } = null!;
    public int PageNumber { get; set; }
    public int ItemIndex { get; set; }
    public string ContentStatus { get; set; } = null!;
    public string TargetStatus { get; set; } = null!;
    public string? ContentGroupHash { get; set; }
    public bool IdentityWarning { get; set; }
    public string PayloadHash { get; set; } = null!;
    public string Payload { get; set; } = null!;
    public DateTimeOffset IngestedAt { get; set; }
}
