using System.Text.Json;
using System.Text.Json.Serialization;

namespace Portfolio.Infrastructure.Persistence.Seeding;

public sealed class PortfolioSeedData
{
    public string SeedVersion { get; init; } = null!;
    public ProfileSeed Profile { get; init; } = null!;
    public List<TechnologySeed> Technologies { get; init; } = [];
    public List<ExperienceSeed> Experiences { get; init; } = [];
    public List<ProjectSeed> Projects { get; init; } = [];
    public List<SkillSeed> Skills { get; init; } = [];
    public List<EducationSeed> Educations { get; init; } = [];
    public List<TrainingSeed> Trainings { get; init; } = [];
    public List<CertificateSeed> Certificates { get; init; } = [];
    public List<JourneySeed> Journey { get; init; } = [];
    public List<SocialLinkSeed> SocialLinks { get; init; } = [];
    public JsonElement SiteSettings { get; init; }
    public AgentSettingSeed AgentSettings { get; init; } = null!;

    public static async Task<PortfolioSeedData> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<PortfolioSeedData>(stream, SerializerOptions, cancellationToken)
            ?? throw new InvalidDataException($"Seed file '{path}' is empty or invalid.");
    }

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
}

public sealed record ProfileSeed(
    string FullName, string? ProfessionalTitle, string? SecondaryTitle, string? HeroHeadline,
    string? HeroSummary, string? AboutMarkdown, string? Email, string? Phone, string? Location,
    string? University, string? Major, string? AvailabilityStatus, string? ProfileImageStorageKey,
    string? CvStorageKey, bool IsPublished);

public sealed record TechnologySeed(
    string Name, string Category, string? IconKey, string? WebsiteUrl, int DisplayOrder, bool IsActive);

public sealed record ExperienceSeed(
    string SeedKey, string CompanyName, string RoleTitle, string? Location, DateOnly StartDate,
    DateOnly? EndDate, bool IsCurrent, string? Summary, string? ResponsibilitiesMarkdown,
    string? CompanyUrl, int DisplayOrder, bool IsPublished, List<string> Technologies);

public sealed record ProjectSeed(
    string SeedKey, string Slug, string Title, string? Subtitle, string? ShortDescription,
    string? OverviewMarkdown, string? Role, int? TeamSize, DateOnly? StartDate, DateOnly? EndDate,
    string Status, string? GithubUrl, string? LiveUrl, string? ThumbnailStorageKey, bool Featured,
    bool IsPublished, int DisplayOrder, string? SeoTitle, string? SeoDescription,
    List<string> Technologies, List<ProjectSectionSeed> Sections);

public sealed record ProjectSectionSeed(
    string SectionType, string? Title, string? Subtitle, string? ContentMarkdown,
    JsonElement Content, int DisplayOrder, bool IsVisible);

public sealed record SkillSeed(
    string Name, string Category, string ExperienceLevel, string? Description,
    string? Technology, int DisplayOrder, bool IsPublished);

public sealed record EducationSeed(
    string SeedKey, string Institution, string? Degree, string? FieldOfStudy,
    DateOnly? StartDate, DateOnly? EndDate, string? Description, string? Location,
    int DisplayOrder, bool IsPublished);

public sealed record TrainingSeed(
    string SeedKey, string Title, string? Provider, string? Description, DateOnly? StartDate,
    DateOnly? EndDate, string? CredentialUrl, int DisplayOrder, bool IsPublished);

public sealed record CertificateSeed(
    string SeedKey, string Name, string? Issuer, DateOnly? IssuedAt, DateOnly? ExpiresAt,
    string? CredentialId, string? CredentialUrl, string? CertificateStorageKey,
    int DisplayOrder, bool IsPublished, [property: JsonPropertyName("_note")] string? Note);

public sealed record JourneySeed(
    string SeedKey, string Title, string? Subtitle, string? Description, DateOnly? OccurredAt,
    string? IconKey, int DisplayOrder, bool IsPublished);

public sealed record SocialLinkSeed(
    string Platform, string? Label, string Url, string? IconKey, int DisplayOrder, bool IsVisible);

public sealed record AgentSettingSeed(
    string Name, bool Enabled, string? Provider, string? ModelName, string? EmbeddingProvider,
    string? EmbeddingModel, int EmbeddingDimensions, string SystemPrompt, string? WelcomeMessage,
    string? FallbackMessage, int MaxContextChunks, decimal? MinimumSimilarity, decimal Temperature);
