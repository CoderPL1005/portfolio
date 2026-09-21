using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Common.Abstractions.Persistence;
using Portfolio.Application.Common.Abstractions.Validation;
using Portfolio.Application.Common.Configuration;
using Portfolio.Application.Common.Exceptions;
using Portfolio.Domain.Entities;

namespace Portfolio.Application.Features.JobHunting;

public sealed record CandidateJobPreferencesResult(
    Guid? Id, IReadOnlyCollection<string> TargetRoles, IReadOnlyCollection<string> PreferredTechnologies,
    IReadOnlyCollection<string> AcceptableLocations, IReadOnlyCollection<string> WorkplaceTypes,
    IReadOnlyCollection<string> EmploymentTypes, decimal? MinimumSalary, string? SalaryCurrency,
    string? SalaryPeriod, int Version, DateTimeOffset? CreatedAt, DateTimeOffset? UpdatedAt);

public sealed record GetCandidateJobPreferencesQuery : IRequest<CandidateJobPreferencesResult>;
public sealed record UpdateCandidateJobPreferencesCommand(
    int ExpectedVersion, IReadOnlyCollection<string> TargetRoles,
    IReadOnlyCollection<string> PreferredTechnologies, IReadOnlyCollection<string> AcceptableLocations,
    IReadOnlyCollection<string> WorkplaceTypes, IReadOnlyCollection<string> EmploymentTypes,
    decimal? MinimumSalary, string? SalaryCurrency, string? SalaryPeriod)
    : IRequest<CandidateJobPreferencesResult>;

public sealed record GetJobFitAnalysisQuery(Guid JobPostingId) : IRequest<JobFitAnalysisResult>;
public sealed record JobFitAnalysisResult(
    Guid JobPostingId, int JobVersion, int PreferenceVersion, int? OverallScore,
    int AvailableWeight, int TotalConfiguredWeight, int CoveragePercent,
    string JobVerificationStatus, IReadOnlyCollection<JobFitComponentResult> Components,
    IReadOnlyCollection<string> MatchedTechnologies, IReadOnlyCollection<string> DevelopingTechnologies,
    IReadOnlyCollection<string> MissingTechnologies, IReadOnlyCollection<string> UnknownFactors);
public sealed record JobFitComponentResult(
    string Key, string Label, int? Score, int ConfiguredWeight, string Status,
    string Explanation, IReadOnlyCollection<string> Evidence);

public sealed record CandidateFactualSnapshot(
    IReadOnlyCollection<string> DemonstratedTechnologies,
    IReadOnlyCollection<string> DevelopingTechnologies,
    IReadOnlyCollection<ExperienceInterval> ExperienceIntervals,
    IReadOnlyCollection<string>? PublishedProfileFacts = null,
    IReadOnlyCollection<string>? PublishedQualifications = null);
public sealed record ExperienceInterval(DateOnly Start, DateOnly End);

public interface ICandidateFactualSnapshotAssembler
{
    Task<CandidateFactualSnapshot> AssembleAsync(CancellationToken cancellationToken = default);
}

public sealed class CandidateFactualSnapshotAssembler(IApplicationDbContext db, TimeProvider clock)
    : ICandidateFactualSnapshotAssembler
{
    public async Task<CandidateFactualSnapshot> AssembleAsync(CancellationToken ct = default)
    {
        var demonstrated = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var developing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var skills = await db.Skills.AsNoTracking().Where(x => x.IsPublished)
            .Select(x => new { x.Name, x.ExperienceLevel, TechnologyName = x.Technology != null && x.Technology.IsActive ? x.Technology.Name : null })
            .ToListAsync(ct);
        foreach (var skill in skills)
        {
            var target = string.Equals(skill.ExperienceLevel, "USED", StringComparison.OrdinalIgnoreCase)
                ? demonstrated : developing;
            target.Add(skill.Name);
            if (skill.TechnologyName is not null) target.Add(skill.TechnologyName);
        }

        var experienceTechnologies = await db.ExperienceTechnologies.AsNoTracking()
            .Where(x => x.Experience.IsPublished && x.Technology.IsActive)
            .Select(x => x.Technology.Name).ToListAsync(ct);
        var projectTechnologies = await db.ProjectTechnologies.AsNoTracking()
            .Where(x => x.Project.IsPublished && x.Technology.IsActive)
            .Select(x => x.Technology.Name).ToListAsync(ct);
        demonstrated.UnionWith(experienceTechnologies);
        demonstrated.UnionWith(projectTechnologies);

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var intervals = await db.Experiences.AsNoTracking()
            .Where(x => x.IsPublished && (x.IsCurrent || x.EndDate.HasValue))
            .OrderBy(x => x.StartDate).ThenBy(x => x.Id)
            .Select(x => new ExperienceInterval(x.StartDate, x.IsCurrent ? today : x.EndDate!.Value))
            .ToListAsync(ct);
        var profile = await db.Profiles.AsNoTracking().Where(x => x.IsPublished)
            .Select(x => new { x.ProfessionalTitle, x.SecondaryTitle, x.University, x.Major })
            .SingleOrDefaultAsync(ct);
        var profileFacts = profile is null ? [] : new[] { profile.ProfessionalTitle, profile.SecondaryTitle, profile.University, profile.Major };
        var education = await db.Educations.AsNoTracking().Where(x => x.IsPublished)
            .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Id).Select(x => x.Degree + " — " + x.FieldOfStudy + " — " + x.Institution).ToListAsync(ct);
        var training = await db.Trainings.AsNoTracking().Where(x => x.IsPublished)
            .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Id).Select(x => x.Title + " — " + x.Provider).ToListAsync(ct);
        var certificates = await db.Certificates.AsNoTracking().Where(x => x.IsPublished)
            .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Id).Select(x => x.Name + " — " + x.Issuer).ToListAsync(ct);
        return new(demonstrated.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
            developing.Except(demonstrated, StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray(),
            intervals, profileFacts.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!).ToArray(),
            education.Concat(training).Concat(certificates).ToArray());
    }
}

public sealed class GetCandidateJobPreferencesQueryHandler(IApplicationDbContext db)
    : IRequestHandler<GetCandidateJobPreferencesQuery, CandidateJobPreferencesResult>
{
    public async Task<CandidateJobPreferencesResult> HandleAsync(GetCandidateJobPreferencesQuery request, CancellationToken ct = default)
    {
        var entity = await db.CandidateJobPreferences.AsNoTracking().SingleOrDefaultAsync(x => x.SingletonKey == "CURRENT", ct);
        return entity is null ? PreferenceMapping.Empty : PreferenceMapping.Map(entity);
    }
}

public sealed class UpdateCandidateJobPreferencesCommandHandler(IApplicationDbContext db, TimeProvider clock)
    : IRequestHandler<UpdateCandidateJobPreferencesCommand, CandidateJobPreferencesResult>
{
    public async Task<CandidateJobPreferencesResult> HandleAsync(UpdateCandidateJobPreferencesCommand request, CancellationToken ct = default)
    {
        var entity = await db.CandidateJobPreferences.SingleOrDefaultAsync(x => x.SingletonKey == "CURRENT", ct);
        var now = clock.GetUtcNow();
        if (entity is null)
        {
            if (request.ExpectedVersion != 0)
                throw new ConflictException("CANDIDATE_PREFERENCES_VERSION_CONFLICT", "The candidate preferences were changed by another request.");
            entity = new CandidateJobPreferences { Id = Guid.NewGuid(), SingletonKey = "CURRENT", Version = 1, CreatedAt = now };
            db.CandidateJobPreferences.Add(entity);
        }
        else
        {
            if (entity.Version != request.ExpectedVersion)
                throw new ConflictException("CANDIDATE_PREFERENCES_VERSION_CONFLICT", "The candidate preferences were changed by another request.");
            entity.Version++;
        }

        PreferenceMapping.Assign(entity, request);
        entity.UpdatedAt = now;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        { throw new ConflictException("CANDIDATE_PREFERENCES_VERSION_CONFLICT", "The candidate preferences were changed by another request."); }
        catch (DbUpdateException)
        { throw new ConflictException("CANDIDATE_PREFERENCES_SINGLETON_CONFLICT", "Candidate preferences already exist."); }
        return PreferenceMapping.Map(entity);
    }
}

public sealed class UpdateCandidateJobPreferencesCommandValidator : IRequestValidator<UpdateCandidateJobPreferencesCommand>
{
    public Task<IReadOnlyCollection<ValidationFailure>> ValidateAsync(UpdateCandidateJobPreferencesCommand r, CancellationToken ct = default)
    {
        var failures = new List<ValidationFailure>();
        if (r.ExpectedVersion < 0) failures.Add(new("expectedVersion", "expectedVersion must be zero or greater."));
        ValidateValues(failures, "targetRoles", r.TargetRoles);
        ValidateValues(failures, "preferredTechnologies", r.PreferredTechnologies);
        ValidateValues(failures, "acceptableLocations", r.AcceptableLocations);
        ValidateValues(failures, "workplaceTypes", r.WorkplaceTypes);
        ValidateValues(failures, "employmentTypes", r.EmploymentTypes);
        if (r.MinimumSalary is < 0 or > 9999999999999999.99m || r.MinimumSalary.HasValue && decimal.Round(r.MinimumSalary.Value, 2) != r.MinimumSalary)
            failures.Add(new("minimumSalary", "minimumSalary must be a non-negative numeric(18,2) value."));
        var currency = r.SalaryCurrency?.Trim(); var period = r.SalaryPeriod?.Trim();
        if (r.MinimumSalary.HasValue && (string.IsNullOrWhiteSpace(currency) || string.IsNullOrWhiteSpace(period)))
            failures.Add(new("salary", "Currency and period are required when minimum salary is set."));
        if (!r.MinimumSalary.HasValue && (!string.IsNullOrWhiteSpace(currency) || !string.IsNullOrWhiteSpace(period)))
            failures.Add(new("salary", "Currency and period require a minimum salary."));
        if (!string.IsNullOrWhiteSpace(currency) && !Regex.IsMatch(currency, "^[A-Za-z]{3}$"))
            failures.Add(new("salaryCurrency", "salaryCurrency must contain exactly three letters."));
        if (period?.Length > 30) failures.Add(new("salaryPeriod", "salaryPeriod must not exceed 30 characters."));
        return Task.FromResult<IReadOnlyCollection<ValidationFailure>>(failures);
    }

    private static void ValidateValues(List<ValidationFailure> failures, string name, IReadOnlyCollection<string>? values)
    {
        if (values is null) { failures.Add(new(name, $"{name} is required.")); return; }
        if (values.Count > 100) failures.Add(new(name, $"{name} cannot contain more than 100 values."));
        if (values.Any(x => string.IsNullOrWhiteSpace(x) || x.Trim().Length > 100))
            failures.Add(new(name, $"{name} values must contain 1 to 100 characters."));
    }
}

public sealed class GetJobFitAnalysisQueryHandler(
    IApplicationDbContext db, ICandidateFactualSnapshotAssembler assembler, JobFitScorer scorer)
    : IRequestHandler<GetJobFitAnalysisQuery, JobFitAnalysisResult>
{
    public async Task<JobFitAnalysisResult> HandleAsync(GetJobFitAnalysisQuery request, CancellationToken ct = default)
    {
        var job = await db.JobPostings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.JobPostingId, ct)
            ?? throw new NotFoundException("JOB_POSTING_NOT_FOUND", "The job posting was not found.");
        var entity = await db.CandidateJobPreferences.AsNoTracking().SingleOrDefaultAsync(x => x.SingletonKey == "CURRENT", ct);
        var preferences = entity is null ? PreferenceMapping.Empty : PreferenceMapping.Map(entity);
        return scorer.Score(job, preferences, await assembler.AssembleAsync(ct));
    }
}

public sealed class JobFitScorer
{
    private readonly JobFitScoringOptions options;
    private readonly JobFitAliasMaps aliases;

    public JobFitScorer(IOptions<JobFitScoringOptions> configured)
    {
        options = configured.Value;
        aliases = JobFitAliasMaps.Create(options);
    }

    public JobFitAnalysisResult Score(JobPosting job, CandidateJobPreferencesResult prefs, CandidateFactualSnapshot facts)
    {
        var jdTechnologies = ReadArray(job.TechnologyStack).Select(x => Normalize(x, aliases.Technology)).Where(x => x.Length > 0).Distinct().ToArray();
        var demonstrated = facts.DemonstratedTechnologies.Select(x => Normalize(x, aliases.Technology)).ToHashSet();
        var developing = facts.DevelopingTechnologies.Select(x => Normalize(x, aliases.Technology)).ToHashSet();
        var matched = jdTechnologies.Where(demonstrated.Contains).ToArray();
        var learning = jdTechnologies.Where(x => !demonstrated.Contains(x) && developing.Contains(x)).ToArray();
        var missing = jdTechnologies.Where(x => !demonstrated.Contains(x) && !developing.Contains(x)).ToArray();
        var w = options.Weights;
        var components = new List<Component>();
        components.Add(Fraction("TECHNICAL_CAPABILITY", "Technical capability", w.TechnicalCapability, jdTechnologies.Length == 0 ? null : (double)matched.Length / jdTechnologies.Length,
            jdTechnologies.Length == 0 ? "The job has no structured technology requirements." : $"Demonstrated {matched.Length} of {jdTechnologies.Length} requested technologies.", matched));
        components.Add(Role(job.PositionTitle, prefs.TargetRoles, w.RoleAlignment));
        components.Add(Experience(job.ExperienceRequirements, facts.ExperienceIntervals, w.ExperienceRequirement));
        components.Add(Choice("LOCATION", "Location", job.Location, prefs.AcceptableLocations, w.Location, aliases.Location, IsAmbiguousLocation));
        components.Add(Overlap("PREFERRED_TECHNOLOGY_ALIGNMENT", "Preferred technologies", jdTechnologies, prefs.PreferredTechnologies, w.PreferredTechnologyAlignment, aliases.Technology));
        components.Add(Choice("WORKPLACE_TYPE", "Workplace type", job.WorkplaceType, prefs.WorkplaceTypes, w.WorkplaceType, aliases.Workplace));
        components.Add(Choice("EMPLOYMENT_TYPE", "Employment type", job.EmploymentType, prefs.EmploymentTypes, w.EmploymentType, aliases.Employment));
        components.Add(Salary(job, prefs, w.Salary));
        var available = components.Where(x => x.Score.HasValue).Sum(x => x.Weight);
        var earned = components.Where(x => x.Score.HasValue).Sum(x => x.Score!.Value * x.Weight);
        var total = w.Total;
        var overall = available == 0 ? null : (int?)Math.Round(earned / available * 100, MidpointRounding.AwayFromZero);
        var coverage = total == 0 ? 0 : (int)Math.Round((double)available / total * 100, MidpointRounding.AwayFromZero);
        return new(job.Id, job.Version, prefs.Version, overall, available, total, coverage, job.VerificationStatus,
            components.Select(x => new JobFitComponentResult(x.Key, x.Label, Percentage(x.Score),
                x.Weight, Status(x.Score), x.Explanation, x.Evidence)).ToArray(),
            matched, learning, missing, components.Where(x => !x.Score.HasValue).Select(x => x.Label).ToArray());
    }

    private Component Role(string jobRole, IReadOnlyCollection<string> targets, int weight)
    {
        if (targets.Count == 0) return Unknown("ROLE_ALIGNMENT", "Role alignment", weight, "No target roles are configured.");
        var role = Normalize(jobRole, aliases.Role);
        var normalized = targets.Select(x => Normalize(x, aliases.Role)).Distinct().ToArray();
        if (normalized.Contains(role)) return Value("ROLE_ALIGNMENT", "Role alignment", weight, 1, "The job title matches a configured target role.", [jobRole]);
        var roleTokens = role.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var best = normalized.Select(x => { var t = x.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(); var common = t.Intersect(roleTokens).Count(); return common >= 2 ? (double)common / t.Union(roleTokens).Count() : 0; }).Max();
        return Value("ROLE_ALIGNMENT", "Role alignment", weight, best, best > 0 ? "The job title partially overlaps a target role." : "The job title does not match a target role.", [jobRole]);
    }

    private static Component Experience(string? requirement, IReadOnlyCollection<ExperienceInterval> intervals, int weight)
    {
        var years = ParseYears(requirement);
        if (!years.HasValue) return Unknown("EXPERIENCE_REQUIREMENT", "Experience", weight, "No conservative general minimum experience requirement could be parsed.");
        var days = MergeDays(intervals);
        var score = days >= years.Value * 365.2425 ? 1d : days / (years.Value * 365.2425);
        return Value("EXPERIENCE_REQUIREMENT", "Experience", weight, score, $"Documented published experience is {days / 365.2425:0.0} years against a {years:0.##}-year minimum.", [$"{days} documented days"]);
    }

    private static Component Salary(JobPosting job, CandidateJobPreferencesResult prefs, int weight)
    {
        if (!prefs.MinimumSalary.HasValue) return Unknown("SALARY", "Salary", weight, "No minimum salary preference is configured.");
        if (!job.SalaryMaximum.HasValue) return Unknown("SALARY", "Salary", weight, "The job does not provide a comparable maximum salary.");
        if (!string.Equals(job.SalaryCurrency?.Trim(), prefs.SalaryCurrency?.Trim(), StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(job.SalaryPeriod?.Trim(), prefs.SalaryPeriod?.Trim(), StringComparison.OrdinalIgnoreCase))
            return Unknown("SALARY", "Salary", weight, "Salary currency or period is not directly comparable.");
        var matches = job.SalaryMaximum.Value >= prefs.MinimumSalary.Value;
        return Value("SALARY", "Salary", weight, matches ? 1 : 0, matches ? "The job maximum satisfies the configured floor." : "The job maximum is below the configured floor.", [$"{job.SalaryMaximum} {job.SalaryCurrency}/{job.SalaryPeriod}"]);
    }

    private static Component Choice(string key, string label, string? jobValue, IReadOnlyCollection<string> accepted, int weight,
        IReadOnlyDictionary<string, string> aliases, Func<string, bool>? ambiguous = null)
    {
        if (accepted.Count == 0) return Unknown(key, label, weight, $"No acceptable {label.ToLowerInvariant()} values are configured.");
        if (string.IsNullOrWhiteSpace(jobValue) || ambiguous?.Invoke(jobValue) == true) return Unknown(key, label, weight, $"The job {label.ToLowerInvariant()} is missing or ambiguous.");
        var actual = Normalize(jobValue, aliases);
        var matches = accepted.Select(x => Normalize(x, aliases)).Any(x => actual == x || key == "LOCATION" && TokenContains(actual, x));
        return Value(key, label, weight, matches ? 1 : 0, matches ? $"{label} matches a configured preference." : $"{label} does not match a configured preference.", [jobValue]);
    }

    private static Component Overlap(string key, string label, IReadOnlyCollection<string> actual, IReadOnlyCollection<string> desired,
        int weight, IReadOnlyDictionary<string, string> aliases)
    {
        if (desired.Count == 0) return Unknown(key, label, weight, "No preferred technologies are configured.");
        if (actual.Count == 0) return Unknown(key, label, weight, "The job has no structured technology requirements.");
        var wanted = desired.Select(x => Normalize(x, aliases)).Distinct().ToArray();
        var matches = actual.Intersect(wanted).ToArray();
        return Fraction(key, label, weight, (double)matches.Length / wanted.Length, $"{matches.Length} of {wanted.Length} preferred technologies appear in the job.", matches);
    }

    internal static string Normalize(string value, IReadOnlyDictionary<string, string> aliases)
    {
        var normalized = JobFitAliasMaps.NormalizeValue(value);
        return aliases.TryGetValue(normalized, out var alias) ? alias : normalized;
    }
    private static string Basic(string value) => JobFitAliasMaps.NormalizeValue(value);
    private static bool TokenContains(string text, string value) { var a = text.Split(' ').ToHashSet(); var b = value.Split(' ').ToHashSet(); return b.Count > 0 && b.IsSubsetOf(a); }
    private static bool IsAmbiguousLocation(string value) => new[] { "various", "multiple locations", "n/a", "unknown" }.Contains(Basic(value));
    private static IReadOnlyCollection<string> ReadArray(JsonDocument document) => document.RootElement.ValueKind == JsonValueKind.Array ? document.RootElement.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToArray() : [];
    public static string Status(double? score) => !score.HasValue ? "UNKNOWN" : score.Value == 1 ? "MATCH" : score.Value == 0 ? "MISMATCH" : "PARTIAL";
    public static int? Percentage(double? score)
    {
        if (!score.HasValue) return null;
        if (score.Value == 0) return 0;
        if (score.Value == 1) return 100;
        return Math.Clamp((int)Math.Round(score.Value * 100, MidpointRounding.AwayFromZero), 1, 99);
    }
    private static Component Fraction(string key, string label, int weight, double? score, string explanation, IEnumerable<string> evidence) => score.HasValue ? Value(key, label, weight, score.Value, explanation, evidence) : Unknown(key, label, weight, explanation);
    private static Component Value(string key, string label, int weight, double score, string explanation, IEnumerable<string> evidence) => new(key, label, weight, Math.Clamp(score, 0, 1), explanation, evidence.ToArray());
    private static Component Unknown(string key, string label, int weight, string explanation) => new(key, label, weight, null, explanation, []);
    private sealed record Component(string Key, string Label, int Weight, double? Score, string Explanation, IReadOnlyCollection<string> Evidence);

    public static double? ParseYears(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var value = input.Normalize(NormalizationForm.FormKC).Trim().ToLowerInvariant();
        var match = Regex.Match(value, @"^(?:(?:at least|minimum)\s+)?(?<n>\d+(?:\.\d+)?)\+?\s+years?(?:\s+(?:of\s+)?experience)?[.!]?$", RegexOptions.CultureInvariant);
        if (!match.Success) match = Regex.Match(value, @"^(?:(?:tối thiểu|ít nhất)\s+)(?<n>\d+(?:[.,]\d+)?)\s+năm(?:\s+kinh nghiệm)?[.!]?$", RegexOptions.CultureInvariant);
        return match.Success && double.TryParse(match.Groups["n"].Value.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var years) && years > 0 ? years : null;
    }

    public static int MergeDays(IReadOnlyCollection<ExperienceInterval> intervals)
    {
        var ordered = intervals.Where(x => x.End >= x.Start).OrderBy(x => x.Start).ThenBy(x => x.End).ToArray();
        if (ordered.Length == 0) return 0;
        var total = 0; var start = ordered[0].Start; var end = ordered[0].End;
        foreach (var current in ordered.Skip(1))
        {
            if (current.Start <= end.AddDays(1)) { if (current.End > end) end = current.End; }
            else { total += end.DayNumber - start.DayNumber + 1; start = current.Start; end = current.End; }
        }
        return total + end.DayNumber - start.DayNumber + 1;
    }
}

internal static class PreferenceMapping
{
    public static CandidateJobPreferencesResult Empty { get; } = new(null, [], [], [], [], [], null, null, null, 0, null, null);
    public static CandidateJobPreferencesResult Map(CandidateJobPreferences x) => new(x.Id, Read(x.TargetRoles), Read(x.PreferredTechnologies), Read(x.AcceptableLocations), Read(x.WorkplaceTypes), Read(x.EmploymentTypes), x.MinimumSalary, x.SalaryCurrency, x.SalaryPeriod, x.Version, x.CreatedAt, x.UpdatedAt);
    public static void Assign(CandidateJobPreferences x, UpdateCandidateJobPreferencesCommand r)
    {
        x.TargetRoles = Json(r.TargetRoles); x.PreferredTechnologies = Json(r.PreferredTechnologies);
        x.AcceptableLocations = Json(r.AcceptableLocations); x.WorkplaceTypes = Json(r.WorkplaceTypes);
        x.EmploymentTypes = Json(r.EmploymentTypes); x.MinimumSalary = r.MinimumSalary;
        x.SalaryCurrency = Trim(r.SalaryCurrency)?.ToUpperInvariant(); x.SalaryPeriod = Trim(r.SalaryPeriod)?.ToUpperInvariant();
    }
    private static JsonDocument Json(IEnumerable<string> values) => JsonDocument.Parse(JsonSerializer.Serialize(values.Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray()));
    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string[] Read(JsonDocument value) => value.RootElement.EnumerateArray().Select(x => x.GetString()!).ToArray();
}
