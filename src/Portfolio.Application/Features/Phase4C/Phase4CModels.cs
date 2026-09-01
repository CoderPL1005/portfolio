namespace Portfolio.Application.Features.Phase4C;

public sealed record JourneyResult(Guid Id, string Title, string? Subtitle, string? Description,
    DateOnly? OccurredAt, string? IconKey, int DisplayOrder, bool IsPublished,
    DateTimeOffset UpdatedAt);
public sealed record PublicJourneyResult(Guid Id, string Title, string? Subtitle,
    string? Description, DateOnly? OccurredAt, string? IconKey, string SourceType,
    Guid SourceId, DateOnly? StartAt, DateOnly? EndAt, bool IsOngoing,
    string TimelineKind);
public sealed record SocialLinkResult(Guid Id, string Platform, string? Label, string Url,
    string? IconKey, int DisplayOrder, bool IsVisible, DateTimeOffset UpdatedAt);
public sealed record PublicSocialLinkResult(Guid Id, string Platform, string? Label, string Url,
    string? IconKey);
public sealed record SiteSettingsResult(string SiteName, string? FooterText,
    bool ShowAvailability, bool ShowDownloadCv, bool ShowJourney,
    bool ShowAiAgent, string? DefaultSeoTitle, string? DefaultSeoDescription);
public sealed record KnowledgeCountsResult(int Indexed, int Pending, int Failed);
public sealed record RecentUpdateResult(string ResourceType, Guid Id, string Title,
    DateTimeOffset UpdatedAt);
public sealed record DashboardResult(int Projects, int Experiences, int Skills, int Certificates,
    KnowledgeCountsResult Knowledge, int Conversations,
    IReadOnlyCollection<RecentUpdateResult> RecentUpdates);
