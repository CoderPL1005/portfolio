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

    public async Task<object> StartAsync(Guid clientRequestId, string triggerSource, CancellationToken ct = default, bool confirmGeminiFreeTier = false)
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
            if (triggerSource == "DAILY_SCHEDULED")
            {
                if (!Settings.DailyEnabled) throw Conflict("DISCOVERY_DAILY_DISABLED");
                var (dayStartUtc, dayEndUtc) = DiscoveryDailySlots.VietnamBusinessDayBoundsUtc(clock.GetUtcNow());
                var count = await db.DiscoveryExecutions.CountAsync(x => x.TriggerSource == "DAILY_SCHEDULED" &&
                    x.CreatedAt >= dayStartUtc && x.CreatedAt < dayEndUtc, ct);
                if (count >= 6) throw Conflict("DISCOVERY_DAILY_LIMIT_REACHED");
            }
            // Only the server may resolve a scheduled window from the immutable slot request ID.
            var dailyWindow = triggerSource == "DAILY_SCHEDULED"
                ? DiscoveryDailySlots.ForScheduledRequest(clientRequestId, clock.GetUtcNow())
                : null;
            var configuration = await BuildConfigurationAsync(ct,
                confirmGeminiFreeTier && triggerSource == "ADMIN_MANUAL", dailyWindow, clock.GetUtcNow());
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

    private static string NormalizeQueryForPlanning(string query)
    {
        var q = query.Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder(q.Length);
        foreach (var ch in q)
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark)
                sb.Append(char.ToLowerInvariant(ch));
        var folded = sb.ToString().Trim();
        folded = Regex.Replace(folded, @"(?<![\p{L}\p{N}])(?:asp\.net\s*core|asp\s*net\s*core)(?![\p{L}\p{N}])", "ASP.NET Core", RegexOptions.IgnoreCase);
        folded = Regex.Replace(folded, @"(?<![\p{L}\p{N}])(?:ef\s*core|entity\s*framework\s*core)(?![\p{L}\p{N}])", "EF Core", RegexOptions.IgnoreCase);
        folded = Regex.Replace(folded, @"(?<![\p{L}\p{N}])(?:asp\.net|asp\s*net)(?![\p{L}\p{N}])", "ASP.NET", RegexOptions.IgnoreCase);
        folded = Regex.Replace(folded, @"(?<![\p{L}\p{N}])(?:\.net|dotnet)(?![\p{L}\p{N}])", ".NET", RegexOptions.IgnoreCase);
        folded = Regex.Replace(folded, @"(?<![\p{L}\p{N}])(?:c#|csharp|c\s*sharp)(?![\p{L}\p{N}])", "C#", RegexOptions.IgnoreCase);
        folded = folded.ToLowerInvariant();
        folded = Regex.Replace(folded, @"\bha noi\b", "hanoi");
        folded = Regex.Replace(folded, @"\b(?:ho chi minh|sai gon|saigon)\b", "hcm");
        return Regex.Replace(folded, @"\s+", " ").Trim();
    }

    private async Task<string> BuildConfigurationAsync(CancellationToken ct, bool confirmGeminiFreeTier, DiscoveryDailySearchWindow? dailyWindow, DateTimeOffset nowUtc)
    {
        if(Settings.EvidencePolicy is not("STRICT" or "V2"))throw Invalid("evidencePolicy");
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

        var vietnamBusinessDate = DiscoveryDailySlots.VietnamBusinessDate(nowUtc);
        var priorExecutions = await db.DiscoveryExecutions.AsNoTracking()
            .OrderByDescending(x => x.CreatedAt).Take(60)
            .Select(x => new { x.Id, x.CreatedAt, x.State, x.ConfigurationJson }).ToArrayAsync(ct);
        var priorIds = priorExecutions.Select(x => x.Id).ToArray();
        var priorAttempts = await db.DiscoveryExecutionAttempts.AsNoTracking()
            .Where(x => priorIds.Contains(x.ExecutionId))
            .Select(x => new { x.ExecutionId, x.CreditsDeducted, x.ActualRequests }).ToArrayAsync(ct);
        var priorCandidates = await db.DiscoveryCandidates.AsNoTracking()
            .OrderBy(x => x.IngestedAt).ThenBy(x => x.ItemIndex)
            .Select(x => new { x.RunId, x.Query, x.PageNumber, x.ItemIndex, x.TargetStatus, x.ContentGroupHash, x.Payload, x.IngestedAt }).ToArrayAsync(ct);
        var priorObservations = await db.DiscoveryExecutionObservations.AsNoTracking()
            .Where(x => priorIds.Contains(x.ExecutionId))
            .OrderBy(x => x.ExecutionId).ThenBy(x => x.ItemIndex)
            .Select(x => new { x.ExecutionId, x.ItemIndex, x.ContentHash, x.PayloadJson }).ToArrayAsync(ct);

        var seenGlobalPostIdentities = new HashSet<string>(StringComparer.Ordinal);
        var seenContentHashes = new HashSet<string>(StringComparer.Ordinal);
        var queryHistory = new List<object>();
        foreach (var exec in priorExecutions.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id))
        {
            var runKey = exec.Id.ToString();
            var runCandidates = priorCandidates.Where(c => c.RunId == runKey).ToArray();
            var runObservations = priorObservations.Where(o => o.ExecutionId == exec.Id).ToArray();
            var attempt = priorAttempts.FirstOrDefault(a => a.ExecutionId == exec.Id);
            string? execQuery = runCandidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c.Query))?.Query;
            string? profileFp = null; string? targetFp = null; int? execSlot = null;
            if (runCandidates.Length > 0)
            {
                try
                {
                    using var cDoc = JsonDocument.Parse(runCandidates[0].Payload);
                    profileFp = Text(cDoc.RootElement, "profileFingerprint");
                    targetFp = Text(cDoc.RootElement, "preferencesFingerprint");
                }
                catch (JsonException) { }
            }
            if (!string.IsNullOrWhiteSpace(exec.ConfigurationJson))
            {
                try
                {
                    using var cfgDoc = JsonDocument.Parse(exec.ConfigurationJson);
                    var root = cfgDoc.RootElement;
                    execQuery ??= Text(root, "query") ?? (root.TryGetProperty("querySelection", out var qs) ? Text(qs, "query") : null);
                    if (root.TryGetProperty("dailySearch", out var ds) && ds.ValueKind == JsonValueKind.Object && ds.TryGetProperty("slot", out var sl) && sl.TryGetInt32(out var slotNum))
                        execSlot = slotNum;
                }
                catch (JsonException) { }
            }
            if (string.IsNullOrWhiteSpace(execQuery)) continue;
            int rawCount = Math.Max(runObservations.Length, runCandidates.Length);
            int uniqueInRun = 0, newUniqueInRun = 0, newRelevantInRun = 0, duplicateInRun = 0;
            var runSeenIdentities = new HashSet<string>(StringComparer.Ordinal);
            if (runCandidates.Length > 0)
            {
                foreach (var c in runCandidates)
                {
                    string? postId = null;
                    try { using var doc = JsonDocument.Parse(c.Payload); postId = Text(doc.RootElement, "providerPostId") ?? Text(doc.RootElement, "sourceUrl"); } catch (JsonException) { }
                    var key = !string.IsNullOrWhiteSpace(postId) ? "post:" + postId : !string.IsNullOrWhiteSpace(c.ContentGroupHash) ? "hash:" + c.ContentGroupHash : $"item:{c.RunId}:{c.ItemIndex}";
                    if (runSeenIdentities.Add(key)) uniqueInRun++;
                    var alreadySeen = seenGlobalPostIdentities.Contains(key) || (!string.IsNullOrWhiteSpace(c.ContentGroupHash) && seenContentHashes.Contains(c.ContentGroupHash));
                    if (alreadySeen) duplicateInRun++;
                    else
                    {
                        newUniqueInRun++;
                        if (c.TargetStatus == "TARGET_MATCH") newRelevantInRun++;
                    }
                    seenGlobalPostIdentities.Add(key);
                    if (!string.IsNullOrWhiteSpace(c.ContentGroupHash)) seenContentHashes.Add(c.ContentGroupHash);
                }
            }
            else
            {
                foreach (var o in runObservations)
                {
                    string? postId = null;
                    try { using var doc = JsonDocument.Parse(o.PayloadJson); postId = Text(doc.RootElement, "providerPostId") ?? Text(doc.RootElement, "sourceUrl"); } catch (JsonException) { }
                    var key = !string.IsNullOrWhiteSpace(postId) ? "post:" + postId : !string.IsNullOrWhiteSpace(o.ContentHash) ? "hash:" + o.ContentHash : $"item:{o.ExecutionId}:{o.ItemIndex}";
                    if (runSeenIdentities.Add(key)) uniqueInRun++;
                    var alreadySeen = seenGlobalPostIdentities.Contains(key) || (!string.IsNullOrWhiteSpace(o.ContentHash) && seenContentHashes.Contains(o.ContentHash));
                    if (alreadySeen) duplicateInRun++;
                    else newUniqueInRun++;
                    seenGlobalPostIdentities.Add(key);
                    if (!string.IsNullOrWhiteSpace(o.ContentHash)) seenContentHashes.Add(o.ContentHash);
                }
            }
            var duplicateRate = rawCount > 0 ? Math.Round((double)duplicateInRun / rawCount, 4) : 0.0;
            queryHistory.Add(new
            {
                executionId = exec.Id,
                createdAt = exec.CreatedAt,
                vietnamBusinessDate = DiscoveryDailySlots.VietnamBusinessDate(exec.CreatedAt),
                slot = execSlot,
                query = execQuery,
                normalizedQueryIdentity = NormalizeQueryForPlanning(execQuery),
                pageNumber = 1,
                profileFingerprint = profileFp,
                targetPreferencesFingerprint = targetFp,
                creditsDeducted = attempt?.CreditsDeducted ?? (attempt?.ActualRequests > 0 ? 1m : 0m),
                rawObservationCount = rawCount,
                uniquePostCount = uniqueInRun,
                newUniquePosts = newUniqueInRun,
                newRelevantOpportunities = newRelevantInRun,
                duplicateRate
            });
        }

        var candidatePool = new List<(string Query, string Identity, string? Technology)>();
        var candidateIdentities = new HashSet<string>(StringComparer.Ordinal);
        void AddCandidate(string q, string? tech)
        {
            var id = NormalizeQueryForPlanning(q);
            if (candidateIdentities.Add(id)) candidatePool.Add((q, id, tech));
        }
        foreach (var level in Settings.Seniorities)
            foreach (var loc in locations)
                foreach (var tech in preferred)
                    if (candidatePool.Count < 31) AddCandidate($"{tech} Backend {level} {loc}", tech);
        if (Settings.Seniorities.Length > 0 && locations.Length > 0)
            AddCandidate($"Backend Developer {Settings.Seniorities[0]} {locations[0]}", null);
        foreach (var level in Settings.Seniorities)
            foreach (var loc in locations)
                foreach (var tech in preferred)
                    if (candidatePool.Count < 32) AddCandidate($"Tuyển dụng {tech} Backend {level} {loc}", tech);

        var historyJson = JsonSerializer.SerializeToElement(queryHistory, Json);
        var historyItems = historyJson.EnumerateArray().ToArray();
        var previousQueryIdentity = historyItems.Length > 0 ? Text(historyItems[^1], "normalizedQueryIdentity") : null;
        var rankedCandidates = candidatePool.Select((c, index) =>
        {
            var matching = historyItems.Where(h => Text(h, "normalizedQueryIdentity") == c.Identity).ToArray();
            var usedToday = matching.Any(h => Text(h, "vietnamBusinessDate") == vietnamBusinessDate);
            var repeatedImmediately = previousQueryIdentity is not null && previousQueryIdentity == c.Identity;
            decimal totalCredits = 0; int totalRelevant = 0; double? recentDupRate = null;
            foreach (var h in matching)
            {
                if (h.TryGetProperty("creditsDeducted", out var cd) && cd.TryGetDecimal(out var cred) && cred > 0)
                {
                    totalCredits += cred;
                    if (h.TryGetProperty("newRelevantOpportunities", out var nro) && nro.TryGetInt32(out var rel))
                        totalRelevant += rel;
                }
                if (h.TryGetProperty("duplicateRate", out var dr) && dr.TryGetDouble(out var dup))
                    recentDupRate = dup;
            }
            double? score = totalCredits > 0 ? (double)totalRelevant / (double)totalCredits : null;
            double baseVal = score ?? 0.25;
            double dupPenalty = recentDupRate is > 0 ? Math.Min(0.24, recentDupRate.Value * 0.2) : 0.0;
            double rankingValue = Math.Max(0.0, baseVal - dupPenalty);
            return new { c.Query, c.Identity, c.Technology, Index = index, UsedToday = usedToday, RepeatedImmediately = repeatedImmediately, Score = score, RecentDupRate = recentDupRate, RankingValue = rankingValue };
        }).ToArray();
        var allUsedToday = rankedCandidates.Length > 0 && rankedCandidates.All(c => c.UsedToday);
        var chosen = rankedCandidates
            .OrderBy(c => c.UsedToday ? 1 : 0)
            .ThenBy(c => c.RepeatedImmediately ? 1 : 0)
            .ThenByDescending(c => c.RankingValue)
            .ThenBy(c => c.Index)
            .First();
        var rotatedToday = rankedCandidates.Any(c => c.UsedToday) && !chosen.UsedToday;
        var selectionDecision = allUsedToday ? "ALL_ELIGIBLE_EXHAUSTED_SAFE_REUSE_BEST_YIELD"
            : rotatedToday ? "ROTATED_UNTRIED_TODAY"
            : chosen.Score.HasValue ? "RANKED_BY_YIELD" : "EXPLORATION_PRIOR";

        return JsonSerializer.Serialize(new {
            maxRequests = 1, maxCredits = 1, timeoutMs = 20000, freePlanConfirmed = true, noCashConfirmed = true,
            semanticEnabled = confirmGeminiFreeTier && Settings.SemanticEnabled && Settings.GeminiFreeTierConfirmed && semantic.IsConfigured,
            semanticModel = semantic.ModelIdentity,
            evidencePolicy = Settings.EvidencePolicy,
            dailySearch = dailyWindow,
            vietnamBusinessDate,
            query = chosen.Query,
            querySelection = new {
                query = chosen.Query,
                normalizedQueryIdentity = chosen.Identity,
                decision = selectionDecision,
                rankingBasis = chosen.Score.HasValue ? "OBSERVED_NEW_RELEVANT_OPPORTUNITIES_PER_CREDIT" : "UNMEASURED_EXPLORATION_PRIOR",
                score = chosen.Score,
                recentDuplicateRate = chosen.RecentDupRate,
                usedToday = chosen.UsedToday,
                allEligibleUsedToday = allUsedToday,
                vietnamBusinessDate,
                slot = dailyWindow?.Slot,
                previousQueryIdentity,
                historyEntriesConsidered = queryHistory.Count,
                candidateCount = rankedCandidates.Length,
                coverage = "ONE_QUERY_ONLY_NOT_ENTIRE_POOL"
            },
            queryHistory,
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
        var priorPostIds = new HashSet<string>(StringComparer.Ordinal);
        var priorContentHashes = new HashSet<string>(StringComparer.Ordinal);
        var priorObs = await db.DiscoveryExecutionObservations.AsNoTracking()
            .Join(db.DiscoveryExecutions.AsNoTracking().Where(e => e.CreatedAt < execution.CreatedAt), o => o.ExecutionId, e => e.Id, (o, _) => new { o.ContentHash, o.PayloadJson })
            .ToArrayAsync(ct);
        foreach (var po in priorObs)
        {
            if (!string.IsNullOrWhiteSpace(po.ContentHash)) priorContentHashes.Add(po.ContentHash);
            try { using var doc = JsonDocument.Parse(po.PayloadJson); if (Text(doc.RootElement, "providerPostId") is { Length: > 0 } pid) priorPostIds.Add(pid); } catch (JsonException) { }
        }
        var seenPostIdsInRun = new HashSet<string>(StringComparer.Ordinal);
        var seenHashesInRun = new HashSet<string>(StringComparer.Ordinal);
        int uniqueProviderPosts = 0, newUniquePosts = 0, exactDuplicates = 0, potentialReposts = 0, reliableInWindowPosts = 0, unknownDatePosts = 0, outOfWindowPosts = 0, relevantUniqueVacancies = 0, newRelevantUniqueVacancies = 0;

        JsonElement configElem;
        try { configElem = JsonSerializer.Deserialize<JsonElement>(execution.ConfigurationJson); }
        catch (JsonException) { configElem = JsonSerializer.SerializeToElement(new { }); }

        string? slotStartTime = null;
        string? slotEndTime = null;
        int? slotNumber = null;
        if (configElem.ValueKind == JsonValueKind.Object && configElem.TryGetProperty("dailySearch", out var ds) && ds.ValueKind == JsonValueKind.Object)
        {
            slotStartTime = Text(ds, "startTime");
            slotEndTime = Text(ds, "endTime");
            if (ds.TryGetProperty("slot", out var sl) && sl.TryGetInt32(out var sn)) slotNumber = sn;
        }

        foreach (var o in observations)
        {
            try
            {
                using var doc = JsonDocument.Parse(o.PayloadJson);
                var root = doc.RootElement;
                var pid = Text(root, "providerPostId") ?? Text(root, "sourceUrl");
                var ch = o.ContentHash ?? Text(root, "contentHash");
                var pub = Text(root, "publishedAt");
                var prov = Text(root, "publishedAtProvenance");
                var winStatus = Text(root, "windowMatchStatus");

                var hasValidUtcDate = GetDiscoveryOpportunitiesHandler.TryParseExplicitOffsetTimestamp(pub, out var parsedPub);

                bool isVerifiedInWindow = false;
                bool isOutOfWindow = winStatus == "OUT_OF_WINDOW";

                if (hasValidUtcDate && prov == "VERIFIED_SUPPORTED_TIMESTAMP")
                {
                    if (slotNumber is >= 1 and <= 5 && !string.IsNullOrEmpty(slotStartTime) && !string.IsNullOrEmpty(slotEndTime))
                    {
                        var vnDate = parsedPub.ToOffset(TimeSpan.FromHours(7)).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                        var inBounds = string.CompareOrdinal(vnDate, slotStartTime) >= 0 && string.CompareOrdinal(vnDate, slotEndTime) <= 0;
                        if (inBounds && winStatus == "VERIFIED_IN_WINDOW")
                        {
                            isVerifiedInWindow = true;
                        }
                        else
                        {
                            isOutOfWindow = true;
                        }
                    }
                }

                if (isVerifiedInWindow) reliableInWindowPosts++;
                else if (isOutOfWindow) outOfWindowPosts++;
                else unknownDatePosts++;

                var isUniqueInRun = pid is not null ? seenPostIdsInRun.Add(pid) : ch is null || seenHashesInRun.Add(ch);
                if (isUniqueInRun) uniqueProviderPosts++; else exactDuplicates++;
                var isHistoricalDup = (pid is not null && priorPostIds.Contains(pid)) || (ch is not null && priorContentHashes.Contains(ch));
                if (!isUniqueInRun || isHistoricalDup)
                {
                    if (pid is not null && !priorPostIds.Contains(pid) && ch is not null && priorContentHashes.Contains(ch)) potentialReposts++;
                }
                else newUniquePosts++;
                var isTargetMatch = root.TryGetProperty("processing", out var pr) && pr.TryGetProperty("review", out var rv) && rv.ValueKind == JsonValueKind.Object &&
                    rv.TryGetProperty("targetMatching", out var tm) && Text(tm, "status") == "TARGET_MATCH";
                if (isTargetMatch && isUniqueInRun)
                {
                    relevantUniqueVacancies++;
                    if (!isHistoricalDup) newRelevantUniqueVacancies++;
                }
                if (pid is not null) seenPostIdsInRun.Add(pid);
                if (ch is not null) seenHashesInRun.Add(ch);
            }
            catch (JsonException) { unknownDatePosts++; }
        }
        var credits = attempt?.CreditsDeducted;
        var newRelevantPerCredit = credits is > 0 ? Math.Round((decimal)newRelevantUniqueVacancies / credits.Value, 4) : (decimal?)null;
        var querySelection = configElem.ValueKind == JsonValueKind.Object && configElem.TryGetProperty("querySelection", out var qsElem) ? qsElem.Clone() : (JsonElement?)null;
        var results = observations.Select(o => new { itemIndex = o.ItemIndex, rawJobPostingId = o.RawJobPostingId,
            observation = JsonSerializer.Deserialize<JsonElement>(o.PayloadJson),
            semanticState=o.SemanticState,
            processing=JsonDocument.Parse(o.PayloadJson).RootElement.TryGetProperty("processing",out var processing)?processing.Clone():(JsonElement?)null,
            vacancies = vacancies.Where(v => v.ObservationId == o.Id).Select(v => new { v.Id, v.Status, v.JobPostingId,
                fields = JsonSerializer.Deserialize<JsonElement>(v.FieldsJson), missingFields = JsonSerializer.Deserialize<string[]>(v.MissingFieldsJson) }),
            staging = !vacancies.Any(v => v.ObservationId == o.Id && v.JobPostingId != null),
            status = vacancies.Any(v => v.ObservationId == o.Id && v.Status == "NORMALIZED") ? "NORMALIZED" : "NEEDS_REVIEW" });
        return new { execution.Id, execution.State, execution.TriggerSource, execution.WorkflowRunId, execution.Version, execution.ErrorCode,
            active = execution.ActiveKey != null, actualRequests = attempt is null ? (int?)0 : attempt.ActualRequests,
            creditsDeducted = attempt?.CreditsDeducted, creditsUsedAlternateHeader = attempt?.CreditsUsedAlternateHeader, httpStatus = attempt?.HttpStatus,
            execution.CreatedAt, execution.UpdatedAt,
            configuration=configElem,
            semanticReplay=new{enabled=Settings.SemanticEnabled&&Settings.GeminiFreeTierConfirmed&&semantic.IsConfigured,modelIdentity=semantic.ModelIdentity,
                attemptsUsed=execution.SemanticAttempts,maximumAttempts=Math.Clamp(Settings.MaximumSemanticAttempts,1,5),remainingAttempts=Math.Max(0,Math.Clamp(Settings.MaximumSemanticAttempts,1,5)-execution.SemanticAttempts)},
            yieldMetrics = new {
                rawProviderRows = observations.Length,
                uniqueProviderPosts,
                newUniquePosts,
                exactDuplicates,
                potentialReposts,
                reliableInWindowPosts,
                unknownDatePosts,
                outOfWindowPosts,
                relevantUniqueVacancies,
                newRelevantUniqueVacancies,
                creditsDeducted = credits,
                newRelevantUniqueVacanciesPerCredit = newRelevantPerCredit,
                querySelection
            },
            counts = new { fetched = observations.Length, normalized = vacancies.Count(v => v.Status == "NORMALIZED"),
                validRecruitment = observations.Count(o => JsonDocument.Parse(o.PayloadJson).RootElement.TryGetProperty("processing",out var p)&&p.TryGetProperty("review",out var r)&&r.ValueKind==JsonValueKind.Object&&r.TryGetProperty("contentValidity",out var c)&&Text(c,"status")=="VALID_RECRUITMENT"),
                duplicates = vacancies.Count(v => v.Status == "DUPLICATE"), extractionFailures = vacancies.Count(v => v.Status == "NEEDS_REVIEW") }, results,
            events = await db.DiscoveryExecutionEvents.AsNoTracking().Where(x => x.ExecutionId == execution.Id).OrderBy(x => x.At).Select(x => new { x.State, x.Reason, x.At }).ToArrayAsync(ct) };
    }
}
