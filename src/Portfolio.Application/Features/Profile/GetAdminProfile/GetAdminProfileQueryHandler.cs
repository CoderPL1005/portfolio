using Microsoft.EntityFrameworkCore;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Common.Abstractions.Persistence;
using Portfolio.Application.Common.Exceptions;
using Portfolio.Application.Features.PortfolioContent;

namespace Portfolio.Application.Features.Profile.GetAdminProfile;

public sealed class GetAdminProfileQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetAdminProfileQuery, AdminProfileResult>
{
    public async Task<AdminProfileResult> HandleAsync(
        GetAdminProfileQuery request,
        CancellationToken cancellationToken = default) =>
        await dbContext.Profiles.AsNoTracking()
            .Where(item => item.SingletonKey == 1)
            .Select(item => new AdminProfileResult(
                item.Id, item.FullName, item.ProfessionalTitle, item.SecondaryTitle,
                item.HeroHeadline, item.HeroSummary, item.AboutMarkdown, item.Email, item.Phone,
                item.Location, item.University, item.Major, item.AvailabilityStatus,
                item.ProfileImage == null ? null : new MediaSummary(
                    item.ProfileImage.Id, item.ProfileImage.PublicUrl,
                    item.ProfileImage.FileName, item.ProfileImage.AltText),
                item.CvMedia == null ? null : new MediaSummary(
                    item.CvMedia.Id, item.CvMedia.PublicUrl,
                    item.CvMedia.FileName, item.CvMedia.AltText),
                item.IsPublished, item.UpdatedAt))
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("PROFILE_NOT_FOUND", "The profile was not found.");
}
