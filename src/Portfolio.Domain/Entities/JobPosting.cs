using System.Text.Json;
using Portfolio.Domain.Constants;

namespace Portfolio.Domain.Entities;

public sealed class JobPosting
{
    public Guid Id { get; set; }
    public string CompanyName { get; set; } = null!;
    public string PositionTitle { get; set; } = null!;
    public string Location { get; set; } = null!;
    public string? EmploymentType { get; set; }
    public string? WorkplaceType { get; set; }
    public decimal? SalaryMinimum { get; set; }
    public decimal? SalaryMaximum { get; set; }
    public string? SalaryCurrency { get; set; }
    public string? SalaryPeriod { get; set; }
    public string? ExperienceRequirements { get; set; }
    public string Description { get; set; } = null!;
    public JsonDocument TechnologyStack { get; set; } = null!;
    public string? ApplicationEmail { get; set; }
    public string? ApplicationUrl { get; set; }
    public string VerificationStatus { get; set; } = JobPostingVerificationStatuses.Pending;
    public string SelectionStatus { get; set; } = JobPostingSelectionStatuses.PendingAnalysis;
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? VerifiedAt { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public string? Notes { get; set; }
    public int Version { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public ICollection<RawJobPosting> RawJobPostings { get; set; } = [];
    public ICollection<JobApplication> JobApplications { get; set; } = [];
}
