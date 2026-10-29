using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Common.Abstractions.Persistence;
using Portfolio.Application.Common.Abstractions.Validation;
using Portfolio.Application.Common.Exceptions;

namespace Portfolio.Application.Features.JobHunting;

public sealed record GetDiscoveryQualityMetricsQuery(string? RunId = null) : IRequest<DiscoveryQualityReport>;
public sealed record QualityCounts(int Total, int Reviewed, int Unreviewed, int Confirmed, int Corrected, int Uncertain);
public sealed record ClassQuality(string Label, int Tp, int Fp, int Fn, int Support, int PrecisionDenominator,
    int RecallDenominator, decimal? Precision, decimal? Recall);
public sealed record MatrixCell(string Prediction, string Human, int Count);
public sealed record DimensionQuality(int EligibleSamples, int Abstentions, int Unreviewed, int Uncertain,
    int HumanConflicts, int PredictionConflicts, IReadOnlyList<MatrixCell> Matrix, IReadOnlyList<ClassQuality> Classes);
public sealed record QualityLevel(DimensionQuality ContentValidity, DimensionQuality TargetMatching);
public sealed record QualityCohort(string ClassifierVersion, string? ProfileFingerprint, string? PreferencesFingerprint,
    QualityCounts Counts, QualityLevel ObservationLevel, QualityLevel UniqueContentLevel);
public sealed record DiscoveryQualityReport(QualityCounts Counts, QualityLevel ObservationLevel, QualityLevel UniqueContentLevel,
    IReadOnlyList<QualityCohort> Cohorts, bool OverallMatricesAvailable, DateTimeOffset EvaluatedAt, string Notice,
    RecoveryQualityCoverage? RecoveryCoverage = null);
public sealed record RecoveryQualityCoverage(string Origin,string Version,int Total,int Reviewed,int Unreviewed,int Uncertain,int ReviewedRecovery,
    int UniqueContents,bool PredictionUnavailable=true,decimal? Precision=null,decimal? Recall=null);
public sealed record QualitySample(Guid Id, string? ContentGroupHash, string ContentPrediction, string TargetPrediction,
    string ClassifierVersion, string? ProfileFingerprint, string? PreferencesFingerprint, HumanReviewSummary Review);
public sealed record DiscoveryQualityInput(Guid Id, string? ContentGroupHash, string ContentStatus, string TargetStatus,
    string Payload, string Status, int Version, string? HumanContent, string? HumanTarget, string CandidateOrigin="CLASSIFIER");

public static class DiscoveryQualityMetrics
{
    public static QualityCounts Counts(IReadOnlyCollection<QualitySample> rows) => new(rows.Count,
        rows.Count(x => x.Review.Status != "UNREVIEWED"), rows.Count(x => x.Review.Status == "UNREVIEWED"),
        rows.Count(x => x.Review.Status == "CONFIRMED"), rows.Count(x => x.Review.Status == "CORRECTED"), rows.Count(x => x.Review.Status == "UNCERTAIN"));

    public static QualityLevel Level(IReadOnlyCollection<QualitySample> rows, bool unique) => new(
        Dimension(rows, unique, true), Dimension(rows, unique, false));

    private static DimensionQuality Dimension(IReadOnlyCollection<QualitySample> rows, bool unique, bool content)
    {
        var labels = content ? DiscoveryReviewRules.ContentStatuses : DiscoveryReviewRules.TargetStatuses;
        var abstain = content ? "NEEDS_REVIEW" : "INSUFFICIENT_DATA";
        var pairs = new List<(string P, string H)>();
        int abstentions = 0, unreviewed = 0, uncertain = 0, humanConflicts = 0, predictionConflicts = 0;
        var groups = rows.GroupBy(x => unique && x.ContentGroupHash is not null ? "hash:" + x.ContentGroupHash : "id:" + x.Id);
        foreach (var group in groups)
        {
            var reviewed = group.Where(x => x.Review.Status is "CONFIRMED" or "CORRECTED").ToArray();
            if (reviewed.Length == 0)
            {
                if (group.Any(x => x.Review.Status == "UNCERTAIN")) uncertain++; else unreviewed++;
                continue;
            }
            var human = reviewed.Select(x => content ? x.Review.ContentStatus : x.Review.TargetStatus).Distinct().ToArray();
            var predictions = group.Select(x => content ? x.ContentPrediction : x.TargetPrediction).Distinct().ToArray();
            var conflict = false;
            if (human.Length != 1) { humanConflicts++; conflict = true; }
            if (predictions.Length != 1) { predictionConflicts++; conflict = true; }
            if (conflict) continue;
            if (human[0] is null || human[0] == abstain) { abstentions++; continue; }
            pairs.Add((predictions[0], human[0]!));
        }
        var matrix = labels.SelectMany(p => labels.Select(h => new MatrixCell(p, h, pairs.Count(x => x.P == p && x.H == h)))).ToArray();
        var classes = labels.Select(label =>
        {
            var tp = pairs.Count(x => x.P == label && x.H == label);
            var fp = pairs.Count(x => x.P == label && x.H != label);
            var fn = pairs.Count(x => x.P != label && x.H == label);
            return new ClassQuality(label, tp, fp, fn, tp + fn, tp + fp, tp + fn,
                tp + fp == 0 ? null : (decimal)tp / (tp + fp), tp + fn == 0 ? null : (decimal)tp / (tp + fn));
        }).ToArray();
        return new(pairs.Count, abstentions, unreviewed, uncertain, humanConflicts, predictionConflicts, matrix, classes);
    }
}

public sealed class GetDiscoveryQualityMetricsValidator : IRequestValidator<GetDiscoveryQualityMetricsQuery>
{
    public Task<IReadOnlyCollection<ValidationFailure>> ValidateAsync(GetDiscoveryQualityMetricsQuery r, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyCollection<ValidationFailure>>(r.RunId?.Length > 100 ? [new("runId", "Invalid run ID.")] : []);
}
public sealed class GetDiscoveryQualityMetricsHandler(IApplicationDbContext db, TimeProvider clock)
    : IRequestHandler<GetDiscoveryQualityMetricsQuery, DiscoveryQualityReport>
{
    public const int MaximumObservations = 10000;
    public static IQueryable<DiscoveryQualityInput> Query(IApplicationDbContext db, string? runId) =>
        (from c in db.DiscoveryCandidates.AsNoTracking().Where(x => runId == null || x.RunId == runId)
         join v in db.DiscoveryCandidateReviews.AsNoTracking() on c.Id equals v.CandidateId into matches
         from v in matches.DefaultIfEmpty()
         orderby c.Id
         select new DiscoveryQualityInput(c.Id, c.ContentGroupHash, c.ContentStatus, c.TargetStatus, c.Payload,
             v == null ? "UNREVIEWED" : v.Status, v == null ? 0 : v.Version,
             v == null ? null : v.ContentStatus, v == null ? null : v.TargetStatus,c.CandidateOrigin)).Take(MaximumObservations + 1);
    public async Task<DiscoveryQualityReport> HandleAsync(GetDiscoveryQualityMetricsQuery r, CancellationToken ct = default)
    {
        // Bounded private read; never expose original text/notes in statistics.
        // One streamed SELECT avoids paging drift and keeps private payloads out of retained samples.
        // No row locks or writes. EvaluatedAt denotes evaluation, not a serializable transaction.
        var samples = new List<QualitySample>();
        var recoveryRows=new List<DiscoveryQualityInput>();var seen=0;
        await foreach (var row in Query(db, r.RunId).AsAsyncEnumerable().WithCancellation(ct))
        {
            if (++seen > MaximumObservations) throw new ServiceUnavailableException("DISCOVERY_METRICS_COHORT_TOO_LARGE", "Filter by run to evaluate a complete bounded cohort.");
            if(row.CandidateOrigin==DiscoveryRecoveryProvenance.Origin){
                var recovery=JsonSerializer.Deserialize<DiscoveryRecoveryEnvelope>(row.Payload,DiscoveryReviewRules.Json);
                if(recovery is null||!DiscoveryReviewRules.ValidRecoveryContent(recovery))throw DiscoveryRecoveryProvenance.Invalid();
                recoveryRows.Add(row);continue;
            }
            if(row.CandidateOrigin!=DiscoveryRecoveryProvenance.Normal)throw DiscoveryRecoveryProvenance.Invalid();
            using var payload = JsonDocument.Parse(row.Payload);
            var p = payload.RootElement;
            samples.Add(new(row.Id, row.ContentGroupHash, row.ContentStatus, row.TargetStatus,
                p.GetProperty("classifierVersion").GetString()!, Value("profileFingerprint"), Value("preferencesFingerprint"),
                new(row.Status, row.Version, row.HumanContent, row.HumanTarget, null, null, null)));
            string? Value(string key) => p.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        }
        var cohorts = samples.GroupBy(x => new { x.ClassifierVersion, x.ProfileFingerprint, x.PreferencesFingerprint })
            .OrderBy(g => g.Key.ClassifierVersion, StringComparer.Ordinal).ThenBy(g => g.Key.ProfileFingerprint, StringComparer.Ordinal)
            .ThenBy(g => g.Key.PreferencesFingerprint, StringComparer.Ordinal)
            .Select(g => { var rows = g.ToArray(); return new QualityCohort(g.Key.ClassifierVersion, g.Key.ProfileFingerprint,
                g.Key.PreferencesFingerprint, DiscoveryQualityMetrics.Counts(rows), DiscoveryQualityMetrics.Level(rows, false), DiscoveryQualityMetrics.Level(rows, true)); }).ToArray();
        var single = cohorts.Length <= 1;
        return new(DiscoveryQualityMetrics.Counts(samples), single ? DiscoveryQualityMetrics.Level(samples, false) : DiscoveryQualityMetrics.Level([], false),
            single ? DiscoveryQualityMetrics.Level(samples, true) : DiscoveryQualityMetrics.Level([], true), cohorts, single, clock.GetUtcNow(),
            "Chỉ tập được Admin gán nhãn; không đại diện toàn bộ Facebook. Metrics không theo review-state filter. Một SELECT, không row locks/write transaction. Khi nhiều cohorts, chỉ xem matrices từng cohort.",
            new(DiscoveryRecoveryProvenance.Origin,DiscoveryRecoveryProvenance.Version,recoveryRows.Count,
                recoveryRows.Count(x=>x.Status!="UNREVIEWED"),recoveryRows.Count(x=>x.Status=="UNREVIEWED"),recoveryRows.Count(x=>x.Status=="UNCERTAIN"),
                recoveryRows.Count(x=>x.Status=="REVIEWED_RECOVERY"),recoveryRows.Select(x=>x.ContentGroupHash??x.Id.ToString()).Distinct().Count()));
    }
}
