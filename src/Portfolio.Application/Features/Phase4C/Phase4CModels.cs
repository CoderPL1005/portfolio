namespace Portfolio.Application.Features.Phase4C;

public sealed record JourneyResult(Guid Id, string Title, string? Subtitle, string? Description,
    DateOnly? OccurredAt, string? IconKey, int DisplayOrder, bool IsPublished,
    DateTimeOffset UpdatedAt);
public sealed record PublicJourneyResult(Guid Id, string Title, string? Subtitle,
    string? Description, DateOnly? OccurredAt, string? IconKey);
public sealed record SocialLinkResult(Guid Id, string Platform, string? Label, string Url,
    string? IconKey, int DisplayOrder, bool IsVisible, DateTimeOffset UpdatedAt);
public sealed record PublicSocialLinkResult(Guid Id, string Platform, string? Label, string Url,
    string? IconKey);
public sealed record SiteSettingsResult(string SiteName, string? FooterText,
    bool ShowAvailability, bool EnableContactForm, bool ShowDownloadCv, bool ShowJourney,
    bool ShowAiAgent, string? DefaultSeoTitle, string? DefaultSeoDescription);
public sealed record ContactSubmissionResult(Guid Id, string Status);
public sealed record ContactMessageResult(Guid Id, string Name, string Email, string? Subject,
    string Message, string Status, DateTimeOffset ReceivedAt, DateTimeOffset? ReadAt,
    DateTimeOffset? RepliedAt);
public sealed record KnowledgeCountsResult(int Indexed, int Pending, int Failed);
public sealed record RecentUpdateResult(string ResourceType, Guid Id, string Title,
    DateTimeOffset UpdatedAt);
public sealed record DashboardResult(int Projects, int Experiences, int Skills, int Certificates,
    int UnreadContactMessages, KnowledgeCountsResult Knowledge, int Conversations,
    IReadOnlyCollection<RecentUpdateResult> RecentUpdates);
