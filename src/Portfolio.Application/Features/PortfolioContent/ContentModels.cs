namespace Portfolio.Application.Features.PortfolioContent;

using Portfolio.Application.Features.Phase4B;
using Portfolio.Application.Features.Phase4C;

public sealed record MediaSummary(Guid Id, string PublicUrl, string FileName, string? AltText);
public sealed record TechnologySummary(Guid Id, string Name, string Category, string? IconKey);

public sealed record AdminProfileResult(
    Guid Id, string FullName, string? ProfessionalTitle, string? SecondaryTitle,
    string? HeroHeadline, string? HeroSummary, string? AboutMarkdown, string? Email,
    string? Phone, string? Location, string? University, string? Major,
    string? AvailabilityStatus, MediaSummary? ProfileImage, MediaSummary? CvMedia,
    bool IsPublished, DateTimeOffset UpdatedAt);

public sealed record PublicProfileResult(
    string FullName, string? ProfessionalTitle, string? SecondaryTitle,
    string? HeroHeadline, string? HeroSummary, string? AboutMarkdown, string? Email,
    string? Location, string? University, string? Major, string? AvailabilityStatus,
    string? ProfileImageUrl, string? CvUrl);

public sealed record AdminExperienceResult(
    Guid Id, string CompanyName, string RoleTitle, string? Location, DateOnly StartDate,
    DateOnly? EndDate, bool IsCurrent, string? Summary, string? ResponsibilitiesMarkdown,
    string? CompanyUrl, int DisplayOrder, bool IsPublished,
    IReadOnlyCollection<TechnologySummary> Technologies);

public sealed record PublicExperienceResult(
    Guid Id, string CompanyName, string RoleTitle, string? Location, DateOnly StartDate,
    DateOnly? EndDate, bool IsCurrent, string? Summary, string? ResponsibilitiesMarkdown,
    string? CompanyUrl, IReadOnlyCollection<TechnologySummary> Technologies);

public sealed record EducationResult(
    Guid Id, string Institution, string? Degree, string? FieldOfStudy,
    DateOnly? StartDate, DateOnly? EndDate, string? Description, string? Location,
    int DisplayOrder, bool IsPublished);

public sealed record PublicEducationResult(
    Guid Id, string Institution, string? Degree, string? FieldOfStudy,
    DateOnly? StartDate, DateOnly? EndDate, string? Description, string? Location);

public sealed record TrainingResult(
    Guid Id, string Title, string? Provider, string? Description,
    DateOnly? StartDate, DateOnly? EndDate, string? CredentialUrl,
    int DisplayOrder, bool IsPublished);

public sealed record PublicTrainingResult(
    Guid Id, string Title, string? Provider, string? Description,
    DateOnly? StartDate, DateOnly? EndDate, string? CredentialUrl);

public sealed record CertificateResult(
    Guid Id, string Name, string? Issuer, DateOnly? IssuedAt, DateOnly? ExpiresAt,
    string? CredentialId, string? CredentialUrl, Guid? CertificateMediaId,
    int DisplayOrder, bool IsPublished);

public sealed record PublicCertificateResult(
    Guid Id, string Name, string? Issuer, DateOnly? IssuedAt, DateOnly? ExpiresAt,
    string? CredentialId, string? CredentialUrl, string? CertificateUrl);

public sealed record PortfolioHomeResult(
    PublicProfileResult Profile,
    IReadOnlyCollection<PublicExperienceResult> Experiences,
    IReadOnlyCollection<PublicProjectListItem> FeaturedProjects,
    IReadOnlyCollection<PublicSkillResult> Skills,
    IReadOnlyCollection<PublicEducationResult> Educations,
    IReadOnlyCollection<PublicTrainingResult> Trainings,
    IReadOnlyCollection<PublicCertificateResult> Certificates,
    IReadOnlyCollection<PublicJourneyResult> Journey,
    IReadOnlyCollection<PublicSocialLinkResult> SocialLinks);
