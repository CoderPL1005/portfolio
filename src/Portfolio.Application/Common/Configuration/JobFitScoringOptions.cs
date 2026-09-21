using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Portfolio.Application.Common.Configuration;

public sealed class JobFitScoringOptions
{
    public const string SectionName = "JobFitScoring";
    public JobFitWeights Weights { get; set; } = new();
    public Dictionary<string, string> TechnologyAliases { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> RoleAliases { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> WorkplaceAliases { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> EmploymentAliases { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> LocationAliases { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class JobFitWeights
{
    public int TechnicalCapability { get; set; } = 30;
    public int RoleAlignment { get; set; } = 25;
    public int ExperienceRequirement { get; set; } = 15;
    public int Location { get; set; } = 10;
    public int PreferredTechnologyAlignment { get; set; } = 5;
    public int WorkplaceType { get; set; } = 5;
    public int EmploymentType { get; set; } = 5;
    public int Salary { get; set; } = 5;

    public int Total => TechnicalCapability + RoleAlignment + ExperienceRequirement + Location +
        PreferredTechnologyAlignment + WorkplaceType + EmploymentType + Salary;

    public IEnumerable<int> Values => [TechnicalCapability, RoleAlignment, ExperienceRequirement,
        Location, PreferredTechnologyAlignment, WorkplaceType, EmploymentType, Salary];
}

public sealed class JobFitScoringOptionsValidator : IValidateOptions<JobFitScoringOptions>
{
    public ValidateOptionsResult Validate(string? name, JobFitScoringOptions options)
    {
        var failures = new List<string>();
        if (options.Weights.Values.Any(weight => weight < 0))
            failures.Add("JobFitScoring weights cannot be negative.");
        if (options.Weights.Total <= 0)
            failures.Add("JobFitScoring total weight must be positive.");

        JobFitAliasMaps.TryCreate(options, failures, out _);
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}

public sealed record JobFitAliasMaps(
    IReadOnlyDictionary<string, string> Technology,
    IReadOnlyDictionary<string, string> Role,
    IReadOnlyDictionary<string, string> Workplace,
    IReadOnlyDictionary<string, string> Employment,
    IReadOnlyDictionary<string, string> Location)
{
    public static JobFitAliasMaps Create(JobFitScoringOptions options)
    {
        var failures = new List<string>();
        TryCreate(options, failures, out var maps);
        if (failures.Count > 0)
            throw new OptionsValidationException(JobFitScoringOptions.SectionName,
                typeof(JobFitScoringOptions), failures);
        return maps!;
    }

    internal static bool TryCreate(JobFitScoringOptions options, List<string> failures, out JobFitAliasMaps? maps)
    {
        var technology = Build("TechnologyAliases", options.TechnologyAliases, failures);
        var role = Build("RoleAliases", options.RoleAliases, failures);
        var workplace = Build("WorkplaceAliases", options.WorkplaceAliases, failures);
        var employment = Build("EmploymentAliases", options.EmploymentAliases, failures);
        var location = Build("LocationAliases", options.LocationAliases, failures);
        maps = failures.Count == 0 ? new(technology, role, workplace, employment, location) : null;
        return maps is not null;
    }

    private static IReadOnlyDictionary<string, string> Build(string group,
        IReadOnlyDictionary<string, string> aliases, List<string> failures)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in aliases.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            var key = NormalizeValue(pair.Key);
            var target = NormalizeValue(pair.Value);
            if (key.Length == 0 || target.Length == 0)
            {
                failures.Add($"JobFitScoring:{group} aliases must normalize to non-empty values.");
                continue;
            }

            if (result.TryGetValue(key, out var existing) && existing != target)
            {
                failures.Add($"JobFitScoring:{group} contains conflicting mappings for normalized alias '{key}'.");
                continue;
            }
            result[key] = target;
        }
        return result;
    }

    public static string NormalizeValue(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormKC).Trim().ToLowerInvariant();
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
            builder.Append(char.IsLetterOrDigit(character) || character is '+' or '#' ? character : ' ');
        return Regex.Replace(builder.ToString(), "\\s+", " ").Trim();
    }
}
