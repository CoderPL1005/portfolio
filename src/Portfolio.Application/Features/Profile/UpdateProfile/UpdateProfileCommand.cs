using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Features.PortfolioContent;

namespace Portfolio.Application.Features.Profile.UpdateProfile;

public sealed record UpdateProfileCommand(
    string FullName, string? ProfessionalTitle, string? SecondaryTitle,
    string? HeroHeadline, string? HeroSummary, string? AboutMarkdown, string? Email,
    string? Phone, string? Location, string? University, string? Major,
    string? AvailabilityStatus, Guid? ProfileImageId, Guid? CvMediaId,
    bool IsPublished) : IRequest<AdminProfileResult>;
