using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Portfolio.Application.Common.Abstractions.Persistence;
using Portfolio.Application.Features.Journey;
using Portfolio.Application.Features.Phase4C;

namespace Portfolio.Application.Features.PortfolioContent.GetPublicPortfolio;

internal sealed record PublicJourneyProjectSource(
    Guid Id, string Title, string? Role, string? Subtitle, string? ShortDescription,
    DateOnly? StartDate, DateOnly? EndDate, string Status, int DisplayOrder);

internal sealed record PublicJourneyManualSource(
    Guid Id, string Title, string? Subtitle, string? Description, DateOnly? OccurredAt,
    string? IconKey, int DisplayOrder);

internal sealed record JourneyTimelineSnapshot(
    IReadOnlyCollection<PublicEducationResult> Educations,
    IReadOnlyCollection<PublicExperienceResult> Experiences,
    IReadOnlyCollection<PublicTrainingResult> Trainings,
    IReadOnlyCollection<PublicCertificateResult> Certificates,
    IReadOnlyCollection<PublicJourneyResult> PublicItems,
    IReadOnlyCollection<AdminJourneyTimelineResult> AdminItems);

internal static class PublicJourneyTimeline
{
    public static async Task<JourneyTimelineSnapshot> LoadAsync(
        IApplicationDbContext dbContext,
        CancellationToken cancellationToken = default)
    {
        var experiences = await dbContext.Experiences.AsNoTracking()
            .Where(item => item.IsPublished)
            .OrderBy(item => item.DisplayOrder).ThenBy(item => item.Id)
            .Select(item => new PublicExperienceResult(
                item.Id, item.CompanyName, item.RoleTitle, item.Location, item.StartDate,
                item.EndDate, item.IsCurrent, item.Summary, item.ResponsibilitiesMarkdown,
                item.CompanyUrl,
                dbContext.ExperienceTechnologies
                    .Where(link => link.ExperienceId == item.Id && link.Technology.IsActive)
                    .OrderBy(link => link.DisplayOrder).ThenBy(link => link.TechnologyId)
                    .Select(link => new TechnologySummary(
                        link.Technology.Id, link.Technology.Name, link.Technology.Category,
                        link.Technology.IconKey)).ToList()))
            .ToListAsync(cancellationToken);

        var educations = await dbContext.Educations.AsNoTracking()
            .Where(item => item.IsPublished)
            .OrderBy(item => item.DisplayOrder).ThenBy(item => item.Id)
            .Select(item => new PublicEducationResult(
                item.Id, item.Institution, item.Degree, item.FieldOfStudy, item.StartDate,
                item.EndDate, item.Description, item.Location))
            .ToListAsync(cancellationToken);

        var trainings = await dbContext.Trainings.AsNoTracking()
            .Where(item => item.IsPublished)
            .OrderBy(item => item.DisplayOrder).ThenBy(item => item.Id)
            .Select(item => new PublicTrainingResult(
                item.Id, item.Title, item.Provider, item.Description, item.StartDate,
                item.EndDate, item.CredentialUrl))
            .ToListAsync(cancellationToken);

        var certificates = await dbContext.Certificates.AsNoTracking()
            .Where(item => item.IsPublished)
            .OrderBy(item => item.DisplayOrder).ThenBy(item => item.Id)
            .Select(item => new PublicCertificateResult(
                item.Id, item.Name, item.Issuer, item.IssuedAt, item.ExpiresAt,
                item.CredentialId, item.CredentialUrl,
                item.CertificateMedia == null ? null : item.CertificateMedia.PublicUrl))
            .ToListAsync(cancellationToken);

        var projects = await dbContext.Projects.AsNoTracking()
            .Where(item => item.IsPublished)
            .OrderBy(item => item.DisplayOrder).ThenBy(item => item.Id)
            .Select(item => new PublicJourneyProjectSource(
                item.Id, item.Title, item.Role, item.Subtitle, item.ShortDescription,
                item.StartDate, item.EndDate, item.Status, item.DisplayOrder))
            .ToListAsync(cancellationToken);

        var manualItems = await dbContext.JourneyItems.AsNoTracking()
            .Where(item => item.IsPublished)
            .OrderBy(item => item.DisplayOrder).ThenBy(item => item.Id)
            .Select(item => new PublicJourneyManualSource(
                item.Id, item.Title, item.Subtitle, item.Description, item.OccurredAt,
                item.IconKey, item.DisplayOrder))
            .ToListAsync(cancellationToken);

        var entries = BuildEntries(
            educations, experiences, projects, trainings, certificates, manualItems);
        return new JourneyTimelineSnapshot(
            educations, experiences, trainings, certificates,
            entries.Select(ToPublic).ToList(), entries.Select(ToAdmin).ToList());
    }

    internal static IReadOnlyCollection<JourneyTimelineEntry> BuildEntries(
        IReadOnlyCollection<PublicEducationResult> educations,
        IReadOnlyCollection<PublicExperienceResult> experiences,
        IReadOnlyCollection<PublicJourneyProjectSource> projects,
        IReadOnlyCollection<PublicTrainingResult> trainings,
        IReadOnlyCollection<PublicCertificateResult> certificates,
        IReadOnlyCollection<PublicJourneyManualSource> manualItems)
    {
        var items = new List<JourneyTimelineEntry>(
            educations.Count + experiences.Count + projects.Count + trainings.Count +
            certificates.Count + manualItems.Count);

        items.AddRange(manualItems.Select(item => new JourneyTimelineEntry(
            item.Id, item.Id, "MANUAL", 4, item.DisplayOrder, item.Title, item.Subtitle,
            item.Description, item.OccurredAt, item.IconKey, item.OccurredAt, null, false,
            "POINT")));
        items.AddRange(educations.Select(item =>
        {
            var hasDegree = !string.IsNullOrWhiteSpace(item.Degree);
            return new JourneyTimelineEntry(
                GeneratedId("EDUCATION", item.Id), item.Id, "EDUCATION", 0, 0,
                hasDegree ? item.Degree! : item.Institution,
                hasDegree ? item.Institution : NonBlank(item.FieldOfStudy),
                item.Description, item.StartDate, null, item.StartDate, item.EndDate,
                item.StartDate.HasValue && !item.EndDate.HasValue, "PERIOD");
        }));
        items.AddRange(experiences.Select(item => new JourneyTimelineEntry(
            GeneratedId("EXPERIENCE", item.Id), item.Id, "EXPERIENCE", 1, 0,
            item.RoleTitle, item.CompanyName, item.Summary, item.StartDate, null,
            item.StartDate, item.EndDate, item.IsCurrent && !item.EndDate.HasValue, "PERIOD")));
        items.AddRange(projects.Select(item => new JourneyTimelineEntry(
            GeneratedId("PROJECT", item.Id), item.Id, "PROJECT", 3, item.DisplayOrder,
            item.Title, NonBlank(item.Role) ?? NonBlank(item.Subtitle),
            item.ShortDescription, item.StartDate, null, item.StartDate, item.EndDate,
            item.StartDate.HasValue && !item.EndDate.HasValue &&
                item.Status is "ACTIVE" or "IN_PROGRESS", "PERIOD")));
        items.AddRange(trainings.Select(item => new JourneyTimelineEntry(
            GeneratedId("TRAINING", item.Id), item.Id, "TRAINING", 2, 0,
            item.Title, item.Provider, item.Description, item.StartDate, null,
            item.StartDate, item.EndDate, false, "PERIOD")));
        items.AddRange(certificates.Select(item => new JourneyTimelineEntry(
            GeneratedId("CERTIFICATE", item.Id), item.Id, "CERTIFICATE", 4, 0,
            item.Name, item.Issuer, null, item.IssuedAt, null, item.IssuedAt, null,
            false, "POINT")));

        return items
            .OrderBy(item => item.SourceOrder)
            .ThenBy(item => item.StartAt.HasValue ? 1 : 0)
            .ThenBy(item => item.StartAt)
            .ThenBy(item => item.EndAt)
            .ThenBy(item => item.SourceType == "MANUAL" ? item.DisplayOrder : 0)
            .ThenBy(item => item.SourceId)
            .ToList();
    }

    private static PublicJourneyResult ToPublic(JourneyTimelineEntry item) => new(
        item.Id, item.Title, item.Subtitle, item.Description, item.OccurredAt, item.IconKey,
        item.SourceType, item.SourceId, item.StartAt, item.EndAt, item.IsOngoing,
        item.TimelineKind);

    private static AdminJourneyTimelineResult ToAdmin(JourneyTimelineEntry item) => new(
        item.Id, item.Title, item.Subtitle, item.Description, item.OccurredAt, item.IconKey,
        item.SourceType, item.SourceId, item.SourceType == "MANUAL", item.StartAt,
        item.EndAt, item.IsOngoing, item.TimelineKind);

    private static string? NonBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static Guid GeneratedId(string sourceType, Guid sourceId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"portfolio-journey-v1:{sourceType}:{sourceId:D}"));
        hash[6] = (byte)((hash[6] & 0x0f) | 0x50);
        hash[8] = (byte)((hash[8] & 0x3f) | 0x80);
        return new Guid(hash.AsSpan(0, 16));
    }

    internal sealed record JourneyTimelineEntry(
        Guid Id, Guid SourceId, string SourceType, int SourceOrder, int DisplayOrder,
        string Title, string? Subtitle, string? Description, DateOnly? OccurredAt,
        string? IconKey, DateOnly? StartAt, DateOnly? EndAt, bool IsOngoing,
        string TimelineKind);
}
