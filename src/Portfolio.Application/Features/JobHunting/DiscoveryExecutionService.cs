using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Portfolio.Application.Common.Abstractions.Authentication;
using Portfolio.Application.Common.Abstractions.Persistence;
using Portfolio.Application.Common.Exceptions;
using Portfolio.Domain.Entities;

namespace Portfolio.Application.Features.JobHunting;

public sealed partial class DiscoveryExecutionService(IApplicationDbContext db, IDiscoveryGitHubClient github,
    IDiscoveryTextExtractor semantic, IOptions<DiscoveryExecutionOptions> options, ICurrentUser user, TimeProvider clock)
    : IDiscoveryExecutionService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { MaxDepth = 20 };
    // Process-local contention reduction only. PostgreSQL unique active_key,
    // attempt PK/unique execution_id and version tokens are authoritative.
    private static readonly SemaphoreSlim Contention = new(1, 1);
    private static ConflictException Conflict(string code) => new(code, "Discovery execution cannot advance safely. Reload its status.");
    private static ValidationException Invalid(string field) => new([new(field, "Invalid discovery execution data.")]);
    private DiscoveryExecutionOptions Settings => options.Value;

    public async Task<object> StartAsync(Guid clientRequestId, string triggerSource, CancellationToken ct = default)
    {
        if (clientRequestId == Guid.Empty || triggerSource is not ("ADMIN_MANUAL" or "DAILY_SCHEDULED")) throw Invalid("clientRequestId");
        // This service is reusable by a future scheduler. No scheduler is registered.
        if (!Settings.Enabled || !Settings.FreePlanConfirmed || !Settings.NoCashConfirmed)
            throw new ServiceUnavailableException("DISCOVERY_DISABLED", "Discovery execution is disabled or Free/no-cash consent is unavailable.");
        DiscoveryExecution execution;
        await Contention.WaitAsync(ct);
        try
        {
            var prior = await db.DiscoveryExecutions.AsNoTracking().SingleOrDefaultAsync(x => x.ClientRequestId == clientRequestId, ct);
            if (prior is not null) return await ViewAsync(prior, ct);
            if (await db.DiscoveryExecutions.AnyAsync(x => x.ActiveKey != null, ct)) throw Conflict("DISCOVERY_EXECUTION_ACTIVE");
            var configuration = await BuildConfigurationAsync(ct);
            execution = new() { Id = Guid.NewGuid(), ClientRequestId = clientRequestId, CreatedByAdminId = user.AdminUserId,
                TriggerSource = triggerSource, ConfigurationJson = configuration, CreatedAt = clock.GetUtcNow(), UpdatedAt = clock.GetUtcNow() };
            db.DiscoveryExecutions.Add(execution); Transition(execution, "DISPATCHING");
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException) { throw Conflict("DISCOVERY_EXECUTION_ACTIVE"); }
        }
        finally { Contention.Release(); }
        // No database transaction/lock held during dispatch; unknown never redispatched.
        string outcome;
        try { outcome = await github.DispatchAsync(execution.Id, ct); }
        catch { outcome = "UNKNOWN"; }
        // A fast worker may already have claimed while dispatch was in flight.
        // Never regress CLAIMED/INGESTING to QUEUED or release its active lock.
        await db.DiscoveryExecutions.Entry(execution).ReloadAsync(CancellationToken.None);
        if(execution.State!="DISPATCHING")return await ViewAsync(execution,CancellationToken.None);
        Transition(execution, outcome == "ACCEPTED" ? "QUEUED" : outcome == "REJECTED" ? "FAILED" : "DISPATCH_UNKNOWN",
            outcome == "ACCEPTED" ? null : "DISCOVERY_DISPATCH_" + outcome);
        if (outcome == "REJECTED") execution.ActiveKey = null;
        try { await db.SaveChangesAsync(CancellationToken.None); }
        catch(DbUpdateConcurrencyException)
        {
            foreach(var entry in db.DiscoveryExecutionEvents.Local.Where(x=>x.ExecutionId==execution.Id&&db.DiscoveryExecutionEvents.Entry(x).State==EntityState.Added).ToArray())
                db.DiscoveryExecutionEvents.Entry(entry).State=EntityState.Detached;
            await db.DiscoveryExecutions.Entry(execution).ReloadAsync(CancellationToken.None);
        }
        return await ViewAsync(execution, CancellationToken.None);
    }

    private async Task<string> BuildConfigurationAsync(CancellationToken ct)
    {
        var technologies = await db.Technologies.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.Name, x.Category, x.IsActive }).ToArrayAsync(ct);
        var skills = await db.Skills.AsNoTracking().Where(x => x.IsPublished && x.TechnologyId != null).OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.Name, x.TechnologyId, x.ExperienceLevel, x.IsPublished }).ToArrayAsync(ct);
        var experiences = await db.Experiences.AsNoTracking().Where(x => x.IsPublished).OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.IsPublished }).ToArrayAsync(ct);
        var experienceLinks = await db.ExperienceTechnologies.AsNoTracking().ToArrayAsync(ct);
        var projects = await db.Projects.AsNoTracking().Where(x => x.IsPublished).OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.Status, x.IsPublished }).ToArrayAsync(ct);
        var projectLinks = await db.ProjectTechnologies.AsNoTracking().ToArrayAsync(ct);
        var preferences = await db.CandidateJobPreferences.AsNoTracking().SingleOrDefaultAsync(x => x.SingletonKey == "CURRENT", ct);
        if (preferences is null || technologies.Length == 0 || skills.Length + projects.Length + experiences.Length == 0)
            throw Conflict("DISCOVERY_PROFILE_UNAVAILABLE");
        string[] Values(JsonDocument value) => value.RootElement.EnumerateArray().Select(x => x.GetString()!).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
        var preferred = Values(preferences.PreferredTechnologies); var locations = Values(preferences.AcceptableLocations);
        if (preferred.Length == 0 || locations.Length == 0) throw Conflict("DISCOVERY_PREFERENCES_UNAVAILABLE");
        return JsonSerializer.Serialize(new {
            maxRequests = 1, maxCredits = 1, timeoutMs = 20000, freePlanConfirmed = true, noCashConfirmed = true,
            semanticEnabled = Settings.SemanticEnabled && Settings.GeminiFreeTierConfirmed,
            semanticModel = semantic.ModelIdentity,
            preferenceVersion = preferences.Version,
            portfolioSnapshot = new { schemaVersion = 1, source = "PORTFOLIO_PUBLIC_API_EXPORT", technologies, skills,
                experiences = experiences.Select(x => new { x.Id, x.IsPublished, technologies = experienceLinks.Where(l => l.ExperienceId == x.Id).Select(l => new { id = l.TechnologyId }) }),
                projects = projects.Select(x => new { x.Id, x.Status, x.IsPublished, technologies = projectLinks.Where(l => l.ProjectId == x.Id).Select(l => new { id = l.TechnologyId }) }) },
            preferences = new { version = 1, primaryRole = Settings.PrimaryRole, allowFullStack = Settings.AllowFullStack,
                stackGroups = new[] { new { preference = "PREFERRED", technologies = preferred } },
                seniorities = Settings.Seniorities, locations, preferredExperienceMax = Settings.PreferredExperienceMax, fallbackExperienceMax = Settings.FallbackExperienceMax },
            targetRoles = Values(preferences.TargetRoles)
        }, Json);
    }

    public async Task<object> GetAsync(Guid id, CancellationToken ct = default) => await ViewAsync(await RequireAsync(id, ct), ct);
    public async Task<object> HistoryAsync(int page, int pageSize, CancellationToken ct = default)
    {
        if (page < 1 || page > 10000 || pageSize is < 1 or > 100) throw Invalid("page");
        var total = await db.DiscoveryExecutions.CountAsync(ct);
        var rows = await db.DiscoveryExecutions.AsNoTracking().OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(ct);
        var items = new List<object>(); foreach (var row in rows) items.Add(await ViewAsync(row, ct));
        return new { items, page, pageSize, total, totalPages = (int)Math.Ceiling((double)total / pageSize) };
    }
    private async Task<DiscoveryExecution> RequireAsync(Guid id, CancellationToken ct) => await db.DiscoveryExecutions.SingleOrDefaultAsync(x => x.Id == id, ct)
        ?? throw new NotFoundException("DISCOVERY_EXECUTION_NOT_FOUND", "Discovery execution was not found.");
    private void Transition(DiscoveryExecution execution, string state, string? reason = null)
    {
        execution.State = state; execution.ErrorCode = reason; execution.Version++; execution.UpdatedAt = clock.GetUtcNow();
        db.DiscoveryExecutionEvents.Add(new() { Id = Guid.NewGuid(), ExecutionId = execution.Id, State = state, Reason = reason, At = clock.GetUtcNow() });
    }
    private async Task SaveAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { throw Conflict("DISCOVERY_EXECUTION_CONFLICT"); }
    }
    private async Task<DiscoveryExecution> WorkerAsync(Guid id, string runId, CancellationToken ct)
    {
        if (!Regex.IsMatch(runId, "^[1-9][0-9]{0,19}$")) throw Conflict("DISCOVERY_WORKER_RUN_INVALID");
        var execution = await RequireAsync(id, ct);
        if (execution.WorkflowRunId is not null && execution.WorkflowRunId != runId || !await github.VerifyRunAsync(id, runId, ct))
            throw Conflict("DISCOVERY_WORKER_RUN_INVALID");
        return execution;
    }
    public async Task<object> ConfigurationAsync(Guid id, string runId, CancellationToken ct)
    {
        var execution = await WorkerAsync(id, runId, ct);
        if (execution.ActiveKey is null || execution.State is not ("QUEUED" or "DISPATCHING")) throw Conflict("DISCOVERY_EXECUTION_NOT_QUEUED");
        return new { executionId = id, triggerSource = execution.TriggerSource,
            configuration = JsonSerializer.Deserialize<JsonElement>(execution.ConfigurationJson),
            // Flat fields retain the worker contract without exposing any credentials.
            maxRequests = 1, maxCredits = 1, portfolioSnapshot = JsonDocument.Parse(execution.ConfigurationJson).RootElement.GetProperty("portfolioSnapshot").Clone() };
    }
    public async Task<object> ClaimAsync(Guid id, string runId, CancellationToken ct)
    {
        await Contention.WaitAsync(ct);
        try
        {
            var execution = await WorkerAsync(id, runId, ct);
            if (execution.ActiveKey is null || execution.State is not ("QUEUED" or "DISPATCHING") || await db.DiscoveryExecutionAttempts.AnyAsync(x => x.ExecutionId == id, ct))
                throw Conflict("DISCOVERY_PROVIDER_ALREADY_CLAIMED");
            execution.WorkflowRunId = runId; Transition(execution, "CLAIMED");
            db.DiscoveryExecutionAttempts.Add(new() { Id = id, ExecutionId = id, WorkflowRunId = runId, ClaimedAt = clock.GetUtcNow() });
            await SaveAsync(ct); return new { claimed = true };
        }
        finally { Contention.Release(); }
    }
    private async Task<object> ViewAsync(DiscoveryExecution execution, CancellationToken ct)
    {
        var attempt = await db.DiscoveryExecutionAttempts.AsNoTracking().SingleOrDefaultAsync(x => x.ExecutionId == execution.Id, ct);
        var observations = await db.DiscoveryExecutionObservations.AsNoTracking().Where(x => x.ExecutionId == execution.Id).OrderBy(x => x.ItemIndex).ToArrayAsync(ct);
        var ids = observations.Select(x => x.Id).ToArray();
        var vacancies = await db.DiscoveryExecutionVacancies.AsNoTracking().Where(x => ids.Contains(x.ObservationId)).ToArrayAsync(ct);
        var results = observations.Select(o => new { itemIndex = o.ItemIndex, rawJobPostingId = o.RawJobPostingId,
            observation = JsonSerializer.Deserialize<JsonElement>(o.PayloadJson),
            vacancies = vacancies.Where(v => v.ObservationId == o.Id).Select(v => new { v.Id, v.Status, v.JobPostingId,
                fields = JsonSerializer.Deserialize<JsonElement>(v.FieldsJson), missingFields = JsonSerializer.Deserialize<string[]>(v.MissingFieldsJson) }),
            staging = !vacancies.Any(v => v.ObservationId == o.Id && v.JobPostingId != null),
            status = vacancies.Any(v => v.ObservationId == o.Id && v.Status == "NORMALIZED") ? "NORMALIZED" : "NEEDS_REVIEW" });
        return new { execution.Id, execution.State, execution.TriggerSource, execution.WorkflowRunId, execution.Version, execution.ErrorCode,
            active = execution.ActiveKey != null, actualRequests = attempt is null ? (int?)0 : attempt.ActualRequests,
            creditsDeducted = attempt?.CreditsDeducted, creditsUsedAlternateHeader = attempt?.CreditsUsedAlternateHeader, httpStatus = attempt?.HttpStatus,
            execution.CreatedAt, execution.UpdatedAt,
            counts = new { fetched = observations.Length, normalized = vacancies.Count(v => v.Status == "NORMALIZED"),
                validRecruitment = observations.Count(o => JsonDocument.Parse(o.PayloadJson).RootElement.TryGetProperty("vacancies", out var a) && a.ValueKind == JsonValueKind.Array && a.GetArrayLength() > 0),
                duplicates = vacancies.Count(v => v.Status == "DUPLICATE"), extractionFailures = vacancies.Count(v => v.Status == "NEEDS_REVIEW") }, results,
            events = await db.DiscoveryExecutionEvents.AsNoTracking().Where(x => x.ExecutionId == execution.Id).OrderBy(x => x.At).Select(x => new { x.State, x.Reason, x.At }).ToArrayAsync(ct) };
    }
}
