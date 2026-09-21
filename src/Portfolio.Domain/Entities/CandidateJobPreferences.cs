using System.Text.Json;

namespace Portfolio.Domain.Entities;

public sealed class CandidateJobPreferences
{
    public Guid Id { get; set; }
    public string SingletonKey { get; set; } = "CURRENT";
    public JsonDocument TargetRoles { get; set; } = null!;
    public JsonDocument PreferredTechnologies { get; set; } = null!;
    public JsonDocument AcceptableLocations { get; set; } = null!;
    public JsonDocument WorkplaceTypes { get; set; } = null!;
    public JsonDocument EmploymentTypes { get; set; } = null!;
    public decimal? MinimumSalary { get; set; }
    public string? SalaryCurrency { get; set; }
    public string? SalaryPeriod { get; set; }
    public int Version { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
