using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Common.Abstractions.Persistence;
using Portfolio.Application.Common.Abstractions.Validation;
using Portfolio.Application.Common.Exceptions;
using Portfolio.Application.Common.Models;
using Portfolio.Domain.Entities;

namespace Portfolio.Application.Features.JobHunting;

public sealed record ReviewDecision(string Status, string[] ReasonCodes, string Explanation, string[] EvidenceSnippets);
public sealed record DiscoveryReviewItem(int ItemIndex, string? ProviderPostId, string? SourceUrl, string? RawContent,
    string? ContentHash, string? ContentGroupHash, DateTimeOffset? PublishedAt, ReviewDecision ContentValidity,
    ReviewDecision TargetMatching, string VerificationStatus, JsonElement Extracted, string[] MissingFields,
    string[] SourceWarnings, string ClassifierVersion, DateTimeOffset EvaluatedAt,
    string? ProfileFingerprint = null, string? PreferencesFingerprint = null);
public sealed record DiscoveryReviewImport(int SchemaVersion, string Provider, string RunId, string Query, int PageNumber, DiscoveryReviewItem[] Items);
public sealed record ImportDiscoveryReviewCommand(DiscoveryReviewImport Bundle) : IRequest<DiscoveryImportResult>;
public sealed record DiscoveryImportResult(int Inserted, int Unchanged);
public sealed record GetDiscoveryReviewQuery(int Page = 1, int PageSize = 20, string? ContentStatus = null,
    string? TargetStatus = null, string? RunId = null, bool? IdentityWarning = null, bool? DuplicatesOnly = null, string? ReviewState = null,
    DateTimeOffset? CollectedFrom = null, DateTimeOffset? CollectedBefore = null) : IRequest<DiscoveryReviewList>;
public sealed record GetDiscoveryCandidateQuery(Guid Id) : IRequest<DiscoveryReviewDetail>;
public sealed record DiscoveryReviewDetail(Guid Id, string RunId, string Query, int PageNumber, DateTimeOffset IngestedAt,
    int DuplicateCount, DiscoveryReviewItem? Item, HumanReviewSummary HumanReview,
    string Origin = "CLASSIFIER", string? OriginalQuery = null, string QueryAvailability = "AVAILABLE",
    object? OriginalClassifierResult = null, DiscoveryRecoveryEnvelope? Recovery = null, bool IdentityWarning = false,
    DiscoveryObservationRead? Observation = null);
public sealed record DiscoveryReviewCounts(int Total, int Valid, int Invalid, int NeedsReview, int TargetMatch, int IdentityWarnings, int DuplicateItems);
public sealed record DiscoveryReviewList(PagedResult<DiscoveryReviewDetail> Results, DiscoveryReviewCounts Counts);

public static class DiscoveryReviewRules
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { MaxDepth = 16 };
    public static readonly string[] ContentStatuses = ["VALID_RECRUITMENT", "INVALID_RECRUITMENT", "NEEDS_REVIEW"];
    public static readonly string[] TargetStatuses = ["TARGET_MATCH", "FALLBACK_MATCH", "POTENTIAL_MATCH", "NOT_MATCHED", "INSUFFICIENT_DATA"];
    public static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    private static bool Text(string? s, int max, bool required = false) => s is null ? !required : s.Length <= max && (!required || !string.IsNullOrWhiteSpace(s)) && !s.Contains('\0');
    private static bool HashValid(string? h) => h is not null && Regex.IsMatch(h, "^[0-9a-f]{64}$");
    public static bool SafeUrl(string? s) => s is null || s.Length <= 2048 && Uri.TryCreate(s, UriKind.Absolute, out var u)
        && u.Scheme == "https" && u.IsDefaultPort && u.UserInfo.Length == 0
        && new[] { "www.facebook.com", "facebook.com", "m.facebook.com" }.Contains(u.Host)
        && (Regex.IsMatch(u.AbsolutePath, "^/[^/]+/posts/(?:[0-9]+|pfbid[A-Za-z0-9]+)/?$")
            || Regex.IsMatch(u.AbsolutePath, "^/groups/[A-Za-z0-9.]+/(?:posts|permalink)/(?:[0-9]+|pfbid[A-Za-z0-9]+)/?$"))
        && u.Query.Length == 0 && u.Fragment.Length == 0;
    private static bool Decision(ReviewDecision? d, string[] statuses, string? text) => d is not null && statuses.Contains(d.Status)
        && d.ReasonCodes is { Length: > 0 and <= 20 } && d.ReasonCodes.All(c => c is not null && Regex.IsMatch(c, "^[A-Z0-9_]{1,80}$"))
        && Text(d.Explanation, 2000, true) && d.EvidenceSnippets is { Length: <= 10 }
        && d.EvidenceSnippets.All(s => s is not null && s.Length is > 0 and <= 500 && text is not null && text.Contains(s, StringComparison.Ordinal));
    // Extraction is informational and never used for application/fit authorization.
    // Reject unknown object keys instead of persisting arbitrary provider metadata.
    private static readonly HashSet<string> ExtractionKeys = new(["company", "role", "location", "technologies", "experience", "salary", "contact", "vacancies",
        "minimum", "maximum", "maximumExclusive", "evidence", "raw", "currency", "scope", "appliesToAllSeniorityLevels", "email", "phone", "method",
        "title", "requirements", "targetStatus", "reason"]);
    private static bool Extraction(JsonElement e, int depth = 0) => depth <= 6 && (e.ValueKind switch
    {
        JsonValueKind.Object => e.EnumerateObject().Count() <= 25 && e.EnumerateObject().All(p => ExtractionKeys.Contains(p.Name) && Extraction(p.Value, depth + 1)),
        JsonValueKind.Array => e.GetArrayLength() <= 100 && e.EnumerateArray().All(v => Extraction(v, depth + 1)),
        JsonValueKind.String => Text(e.GetString(), 24000),
        JsonValueKind.Number or JsonValueKind.Null or JsonValueKind.True or JsonValueKind.False => true,
        _ => false
    });
    public static bool Valid(DiscoveryReviewImport? b)
    {
        try { return ValidCore(b); }
        catch (JsonException) { return false; }
        catch (ArgumentException) { return false; }
    }
    private static bool ValidCore(DiscoveryReviewImport? b)
    {
        if (b is null || b.SchemaVersion != 1 || b.Provider != "SOCIALAPIS" || b.RunId is null || !Regex.IsMatch(b.RunId, "^[A-Za-z0-9._-]{1,100}$")
            || !Text(b.Query, 300, true) || b.PageNumber is < 1 or > 6 || b.Items is not { Length: <= 200 }) return false;
        if (JsonSerializer.SerializeToUtf8Bytes(b, Json).Length > 8 * 1024 * 1024) return false;
        return b.Items.Select((i, n) => i is not null && i.ItemIndex == n && Text(i.RawContent, 24000)
            && (i.RawContent is null ? i.ContentHash is null && i.ContentGroupHash is null : !string.IsNullOrWhiteSpace(i.RawContent)
                && i.ContentHash == Hash(i.RawContent) && i.ContentGroupHash == Hash(i.RawContent.Normalize().Replace("\r\n", "\n").Replace('\r', '\n').Trim()))
            && (i.ProviderPostId is null || Regex.IsMatch(i.ProviderPostId, "^(?:[0-9]{1,100}|pfbid[A-Za-z0-9]{1,200})$"))
            && SafeUrl(i.SourceUrl) && Decision(i.ContentValidity, ContentStatuses, i.RawContent) && Decision(i.TargetMatching, TargetStatuses, i.RawContent)
            && (i.RawContent is not null || i.ContentValidity.Status == "NEEDS_REVIEW" && i.TargetMatching.Status == "INSUFFICIENT_DATA")
            && i.VerificationStatus == "NOT_CHECKED" && i.ClassifierVersion == "content-first-v1" && i.EvaluatedAt != default
            && (i.ProfileFingerprint is null || HashValid(i.ProfileFingerprint)) && (i.PreferencesFingerprint is null || HashValid(i.PreferencesFingerprint))
            && i.MissingFields is { Length: <= 20 } && i.MissingFields.All(s => s is not null && Regex.IsMatch(s, "^[A-Z0-9_]{1,80}$"))
            && i.SourceWarnings is { Length: <= 20 } && i.SourceWarnings.All(s => s is not null && Regex.IsMatch(s, "^[A-Z0-9_]{1,80}$"))
            && i.Extracted.ValueKind == JsonValueKind.Object && Extraction(i.Extracted)).All(v => v);
    }
    public static bool ValidRecoveryContent(DiscoveryRecoveryEnvelope e)
    {
        try { return ValidRecoveryCore(e); }
        catch(Exception error) when(error is ArgumentException or JsonException or InvalidOperationException){return false;}
    }
    private static bool ValidRecoveryCore(DiscoveryRecoveryEnvelope e) => e is not null && e.Origin == DiscoveryRecoveryProvenance.Origin && e.Version == DiscoveryRecoveryProvenance.Version &&
        e.ExecutionId != Guid.Empty && e.ObservationId != Guid.Empty && e.OperationId != Guid.Empty && e.ReceiptId != Guid.Empty && e.AuditId != Guid.Empty &&
        e.ItemIndex is >= 0 and < 200 && e.WorkflowRunId is not null && Regex.IsMatch(e.WorkflowRunId, "^[1-9][0-9]{0,19}$") && HashValid(e.SourceFingerprint) &&
        e.OriginalQuery is null && e.OriginalClassifierResult is null && e.OriginalReviewBundle is null &&
        Text(e.RawContent,24000,true) && e.ContentHash == Hash(e.RawContent) && e.ContentGroupHash == Hash(e.RawContent.Normalize().Replace("\r\n","\n").Replace('\r','\n').Trim()) &&
        e.ProviderPostId is not null && Regex.IsMatch(e.ProviderPostId,"^(?:[0-9]{1,100}|pfbid[A-Za-z0-9]{1,200})$") && SafeUrl(e.SourceUrl) &&
        Text(e.ProviderSourceUrl,2048) && e.RecoveryAt != default && e.RecoveryDecision == new RecoveryDecision("NEEDS_REVIEW","INSUFFICIENT_DATA","NOT_CHECKED") &&
        e.SourceWarnings is {Length:<=20} && e.SourceWarnings.All(x=>x is not null && Regex.IsMatch(x,"^[A-Z0-9_]{1,80}$")) &&
        e.MissingFields is {Length:<=20} && e.MissingFields.All(x=>x is not null && Regex.IsMatch(x,"^[A-Z0-9_]{1,80}$")) &&
        (e.SourceWarnings.Contains("POST_ID_URL_MISMATCH") ? e.SourceUrl is null && e.ProviderSourceUrl is not null && SafeUrl(e.ProviderSourceUrl) &&
            new Uri(e.ProviderSourceUrl).AbsolutePath.TrimEnd('/').Split('/').Last()!=e.ProviderPostId :
            e.SourceUrl is not null && new Uri(e.SourceUrl).AbsolutePath.TrimEnd('/').Split('/').Last()==e.ProviderPostId);
    public static DiscoveryReviewDetail Detail(DiscoveryCandidate c, int duplicates, DiscoveryCandidateReview? review = null)
    {
        if(c.CandidateOrigin==DiscoveryRecoveryProvenance.Origin) return new(c.Id,c.RunId,c.Query,c.PageNumber,c.IngestedAt,duplicates,null,
            HumanReviewSummary.From(review),c.CandidateOrigin,null,"UNAVAILABLE",null,DiscoveryRecoveryProvenance.Read(c),c.IdentityWarning);
        if(c.CandidateOrigin!=DiscoveryRecoveryProvenance.Normal) throw DiscoveryRecoveryProvenance.Invalid();
        var item=JsonSerializer.Deserialize<DiscoveryReviewItem>(c.Payload,Json)!;
        return new(c.Id,c.RunId,c.Query,c.PageNumber,c.IngestedAt,duplicates,item,HumanReviewSummary.From(review),
            DiscoveryRecoveryProvenance.Normal,c.Query,"AVAILABLE",item,null,c.IdentityWarning);
    }
}

public sealed class ImportDiscoveryReviewValidator : IRequestValidator<ImportDiscoveryReviewCommand>
{
    public Task<IReadOnlyCollection<ValidationFailure>> ValidateAsync(ImportDiscoveryReviewCommand r, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyCollection<ValidationFailure>>(DiscoveryReviewRules.Valid(r.Bundle) ? [] : [new("bundle", "Invalid or unsafe discovery review export.")]);
}
public sealed class ImportDiscoveryReviewHandler(IApplicationDbContext db, TimeProvider clock) : IRequestHandler<ImportDiscoveryReviewCommand, DiscoveryImportResult>
{
    public async Task<DiscoveryImportResult> HandleAsync(ImportDiscoveryReviewCommand r, CancellationToken ct = default)
    {
        var b = r.Bundle;
        var existing = await db.DiscoveryCandidates.Where(c => c.RunId == b.RunId && c.PageNumber == b.PageNumber).ToListAsync(ct);
        if (existing.Count > 0 && existing.Count != b.Items.Length) throw new ConflictException("DISCOVERY_IMPORT_CONFLICT", "Run/page already has different observations.");
        var added = new List<DiscoveryCandidate>(); var unchanged = 0;
        foreach (var item in b.Items)
        {
            var payload = JsonSerializer.Serialize(item, DiscoveryReviewRules.Json);
            var payloadHash = DiscoveryReviewRules.Hash(b.Query + "\n" + payload);
            var previous = existing.SingleOrDefault(c => c.ItemIndex == item.ItemIndex);
            if (previous is not null)
            {
                if (previous.CandidateOrigin != DiscoveryRecoveryProvenance.Normal || previous.PayloadHash != payloadHash) throw new ConflictException("DISCOVERY_IMPORT_CONFLICT", "Immutable observation differs from existing import.");
                unchanged++; continue;
            }
            added.Add(new DiscoveryCandidate { Id = Guid.NewGuid(), RunId = b.RunId, Query = b.Query, PageNumber = b.PageNumber, ItemIndex = item.ItemIndex,
                ContentStatus = item.ContentValidity.Status, TargetStatus = item.TargetMatching.Status, ContentGroupHash = item.ContentGroupHash,
                IdentityWarning = item.SourceWarnings.Contains("POST_ID_URL_MISMATCH"), PayloadHash = payloadHash, Payload = payload, IngestedAt = clock.GetUtcNow() });
        }
        db.DiscoveryCandidates.AddRange(added);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            foreach (var item in added) db.DiscoveryCandidates.Entry(item).State = EntityState.Detached;
            // Concurrent identical imports are safe to repeat; never overwrite.
            throw new ConflictException("DISCOVERY_IMPORT_CONFLICT", "Import could not commit; reload and retry the same file.");
        }
        return new(added.Count, unchanged);
    }
}
public sealed class GetDiscoveryReviewValidator : IRequestValidator<GetDiscoveryReviewQuery>
{
    public Task<IReadOnlyCollection<ValidationFailure>> ValidateAsync(GetDiscoveryReviewQuery r, CancellationToken ct = default)
    {
        var f = new List<ValidationFailure>();
        if (r.Page < 1 || r.Page > 100000 || r.PageSize is < 1 or > 100) f.Add(new("page", "Invalid pagination."));
        if (r.ContentStatus is not null && !DiscoveryReviewRules.ContentStatuses.Contains(r.ContentStatus)) f.Add(new("contentStatus", "Invalid content status."));
        if (r.TargetStatus is not null && !DiscoveryReviewRules.TargetStatuses.Contains(r.TargetStatus)) f.Add(new("targetStatus", "Invalid target status."));
        if (r.RunId?.Length > 100) f.Add(new("runId", "Invalid run ID."));
        if (r.CollectedFrom >= r.CollectedBefore) f.Add(new("collectedFrom", "Invalid collection date range."));
        if (r.ReviewState is not null && !new[] { "REVIEWED", "UNREVIEWED", "CONFIRMED", "CORRECTED", "UNCERTAIN", "REVIEWED_RECOVERY" }.Contains(r.ReviewState)) f.Add(new("reviewState", "Invalid review filter."));
        return Task.FromResult<IReadOnlyCollection<ValidationFailure>>(f);
    }
}
public sealed class GetDiscoveryReviewHandler(IApplicationDbContext db) : IRequestHandler<GetDiscoveryReviewQuery, DiscoveryReviewList>
{
    public async Task<DiscoveryReviewList> HandleAsync(GetDiscoveryReviewQuery r, CancellationToken ct = default)
    {
        var all = db.DiscoveryCandidates.AsNoTracking(); var q = all;
        if (r.RunId is not null) q = q.Where(c => c.RunId == r.RunId);
        if (r.CollectedFrom.HasValue) q = q.Where(c => c.IngestedAt >= r.CollectedFrom.Value);
        if (r.CollectedBefore.HasValue) q = q.Where(c => c.IngestedAt < r.CollectedBefore.Value);
        if (r.ContentStatus is not null) q = q.Where(c => c.ContentStatus == r.ContentStatus);
        if (r.TargetStatus is not null) q = q.Where(c => c.TargetStatus == r.TargetStatus);
        if (r.IdentityWarning.HasValue) q = q.Where(c => c.IdentityWarning == r.IdentityWarning.Value);
        if (r.DuplicatesOnly == true) q = q.Where(c => c.ContentGroupHash != null && all.Count(d => d.ContentGroupHash == c.ContentGroupHash) > 1);
        if (r.ReviewState == "UNREVIEWED") q = q.Where(c => !db.DiscoveryCandidateReviews.Any(v => v.CandidateId == c.Id));
        else if (r.ReviewState == "REVIEWED") q = q.Where(c => db.DiscoveryCandidateReviews.Any(v => v.CandidateId == c.Id));
        else if (r.ReviewState is not null) q = q.Where(c => db.DiscoveryCandidateReviews.Any(v => v.CandidateId == c.Id && v.Status == r.ReviewState));
        var total = await q.CountAsync(ct);
        var counts = new DiscoveryReviewCounts(total, await q.CountAsync(c => c.ContentStatus == "VALID_RECRUITMENT", ct),
            await q.CountAsync(c => c.ContentStatus == "INVALID_RECRUITMENT", ct), await q.CountAsync(c => c.ContentStatus == "NEEDS_REVIEW", ct),
            await q.CountAsync(c => c.TargetStatus == "TARGET_MATCH", ct), await q.CountAsync(c => c.IdentityWarning, ct),
            await q.CountAsync(c => c.ContentGroupHash != null && all.Count(d => d.ContentGroupHash == c.ContentGroupHash) > 1, ct));
        var rows = await q.OrderByDescending(c => c.TargetStatus == "TARGET_MATCH" && !db.DiscoveryCandidateReviews.Any(v => v.CandidateId == c.Id))
            .ThenByDescending(c => c.IngestedAt).ThenBy(c => c.RunId).ThenBy(c => c.PageNumber).ThenBy(c => c.ItemIndex)
            .Skip((r.Page - 1) * r.PageSize).Take(r.PageSize).ToListAsync(ct);
        var hashes = rows.Where(c => c.ContentGroupHash != null).Select(c => c.ContentGroupHash!).Distinct().ToArray();
        var groups = await all.Where(c => hashes.Contains(c.ContentGroupHash!)).GroupBy(c => c.ContentGroupHash).Select(g => new { Hash = g.Key, Count = g.Count() }).ToListAsync(ct);
        var ids = rows.Select(c => c.Id).ToArray();
        var reviews = await db.DiscoveryCandidateReviews.AsNoTracking().Where(v => ids.Contains(v.CandidateId)).ToDictionaryAsync(v => v.CandidateId, ct);
        return new(new(rows.Select(c => DiscoveryReviewRules.Detail(c, groups.FirstOrDefault(g => g.Hash == c.ContentGroupHash)?.Count ?? 0, reviews.GetValueOrDefault(c.Id))).ToArray(), r.Page, r.PageSize, total), counts);
    }
}
public sealed class GetDiscoveryCandidateHandler(IApplicationDbContext db) : IRequestHandler<GetDiscoveryCandidateQuery, DiscoveryReviewDetail>
{
    public async Task<DiscoveryReviewDetail> HandleAsync(GetDiscoveryCandidateQuery r, CancellationToken ct = default)
    {
        var c = await db.DiscoveryCandidates.AsNoTracking().SingleOrDefaultAsync(x => x.Id == r.Id, ct)
            ?? throw new NotFoundException("DISCOVERY_CANDIDATE_NOT_FOUND", "Discovery observation not found.");
        return DiscoveryReviewRules.Detail(c, c.ContentGroupHash is null ? 0 : await db.DiscoveryCandidates.CountAsync(x => x.ContentGroupHash == c.ContentGroupHash, ct),
            await db.DiscoveryCandidateReviews.AsNoTracking().SingleOrDefaultAsync(v => v.CandidateId == c.Id, ct));
    }
}
