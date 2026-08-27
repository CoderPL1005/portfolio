using Microsoft.EntityFrameworkCore;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Common.Abstractions.Persistence;
using Portfolio.Application.Common.Exceptions;
using Portfolio.Application.Features.PortfolioContent;

namespace Portfolio.Application.Features.Profile.UpdateProfile;

public sealed class UpdateProfileCommandHandler(
    IApplicationDbContext dbContext,
    TimeProvider timeProvider) : IRequestHandler<UpdateProfileCommand, AdminProfileResult>
{
    public async Task<AdminProfileResult> HandleAsync(
        UpdateProfileCommand request,
        CancellationToken cancellationToken = default)
    {
        var profile = await dbContext.Profiles.SingleOrDefaultAsync(
            item => item.SingletonKey == 1, cancellationToken)
            ?? throw new NotFoundException("PROFILE_NOT_FOUND", "The profile was not found.");
        var profileImage = await GetMediaAsync(request.ProfileImageId, cancellationToken);
        var cvMedia = await GetMediaAsync(request.CvMediaId, cancellationToken);

        profile.FullName = request.FullName.Trim();
        profile.ProfessionalTitle = request.ProfessionalTitle?.Trim();
        profile.SecondaryTitle = request.SecondaryTitle?.Trim();
        profile.HeroHeadline = request.HeroHeadline?.Trim();
        profile.HeroSummary = request.HeroSummary;
        profile.AboutMarkdown = request.AboutMarkdown;
        profile.Email = request.Email?.Trim();
        profile.Phone = request.Phone?.Trim();
        profile.Location = request.Location?.Trim();
        profile.University = request.University?.Trim();
        profile.Major = request.Major?.Trim();
        profile.AvailabilityStatus = request.AvailabilityStatus?.Trim();
        profile.ProfileImageId = request.ProfileImageId;
        profile.CvMediaId = request.CvMediaId;
        profile.IsPublished = request.IsPublished;
        profile.UpdatedAt = timeProvider.GetUtcNow();
        await dbContext.SaveChangesAsync(cancellationToken);

        return new AdminProfileResult(
            profile.Id, profile.FullName, profile.ProfessionalTitle, profile.SecondaryTitle,
            profile.HeroHeadline, profile.HeroSummary, profile.AboutMarkdown, profile.Email,
            profile.Phone, profile.Location, profile.University, profile.Major,
            profile.AvailabilityStatus, profileImage, cvMedia, profile.IsPublished, profile.UpdatedAt);
    }

    private async Task<MediaSummary?> GetMediaAsync(Guid? mediaId, CancellationToken cancellationToken)
    {
        if (!mediaId.HasValue)
        {
            return null;
        }

        return await dbContext.MediaAssets.AsNoTracking()
            .Where(item => item.Id == mediaId.Value)
            .Select(item => new MediaSummary(item.Id, item.PublicUrl, item.FileName, item.AltText))
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("MEDIA_NOT_FOUND", "The selected media asset was not found.");
    }
}
