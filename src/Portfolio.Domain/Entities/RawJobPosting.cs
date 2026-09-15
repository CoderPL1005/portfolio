using System.Text.Json;
using Portfolio.Domain.Constants;

namespace Portfolio.Domain.Entities;

public sealed class RawJobPosting
{
    public Guid Id { get; set; }
    public Guid? JobPostingId { get; set; }
    public string Source { get; set; } = RawJobPostingSources.Other;
    public string? SourceExternalId { get; set; }
    public string? SourceUrl { get; set; }
    public string? SourceUrlHash { get; set; }
    public string RawContent { get; set; } = null!;
    public string ContentHash { get; set; } = null!;
    public string? CompanyTitleFingerprint { get; set; }
    public string IngestionStatus { get; set; } = RawJobPostingIngestionStatuses.Received;
    public Guid? DuplicateOfRawJobPostingId { get; set; }
    public JsonDocument Metadata { get; set; } = null!;
    public DateTimeOffset DiscoveredAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public JobPosting? JobPosting { get; set; }
    public RawJobPosting? DuplicateOfRawJobPosting { get; set; }
    public ICollection<RawJobPosting> DuplicateRawJobPostings { get; set; } = [];
}
