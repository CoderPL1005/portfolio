using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Portfolio.Api.Contracts.Common;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Features.PortfolioContent;
using Portfolio.Application.Features.Profile.GetAdminProfile;
using Portfolio.Application.Features.Profile.UpdateProfile;

namespace Portfolio.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/admin/profile")]
public sealed class AdminProfileController(IRequestDispatcher dispatcher) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<AdminProfileResult>>> Get(CancellationToken cancellationToken) =>
        Ok(ApiResponse<AdminProfileResult>.Ok(await dispatcher.DispatchAsync(new GetAdminProfileQuery(), cancellationToken)));

    [HttpPut]
    public async Task<ActionResult<ApiResponse<AdminProfileResult>>> Update(ProfileRequest request, CancellationToken cancellationToken) =>
        Ok(ApiResponse<AdminProfileResult>.Ok(await dispatcher.DispatchAsync(new UpdateProfileCommand(
            request.FullName, request.ProfessionalTitle, request.SecondaryTitle, request.HeroHeadline,
            request.HeroSummary, request.AboutMarkdown, request.Email, request.Phone, request.Location,
            request.University, request.Major, request.AvailabilityStatus, request.ProfileImageId,
            request.CvMediaId, request.IsPublished), cancellationToken)));
}

public sealed record ProfileRequest(
    string FullName, string? ProfessionalTitle, string? SecondaryTitle, string? HeroHeadline,
    string? HeroSummary, string? AboutMarkdown, string? Email, string? Phone, string? Location,
    string? University, string? Major, string? AvailabilityStatus, Guid? ProfileImageId,
    Guid? CvMediaId, bool IsPublished);
