using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Portfolio.Api.Contracts.Common;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Features.Phase4C;
using Portfolio.Application.Features.SiteSettings;

namespace Portfolio.Api.Controllers;

[ApiController, Authorize, Route("api/v1/admin/site-settings")]
public sealed class AdminSiteSettingsController(IRequestDispatcher dispatcher) : ControllerBase
{
    [HttpGet] public async Task<ActionResult<ApiResponse<SiteSettingsResult>>> Get(CancellationToken ct) => Ok(ApiResponse<SiteSettingsResult>.Ok(await dispatcher.DispatchAsync(new GetSiteSettingsQuery(), ct)));
    [HttpPut] public async Task<ActionResult<ApiResponse<SiteSettingsResult>>> Update(SiteSettingsRequest request, CancellationToken ct) => Ok(ApiResponse<SiteSettingsResult>.Ok(await dispatcher.DispatchAsync(request.Command(), ct)));
}
public sealed record SiteSettingsRequest(string SiteName, string? FooterText, bool ShowAvailability,
    bool ShowDownloadCv, bool ShowJourney, bool ShowAiAgent,
    string? DefaultSeoTitle, string? DefaultSeoDescription)
{ public UpdateSiteSettingsCommand Command() => new(SiteName, FooterText, ShowAvailability, ShowDownloadCv, ShowJourney, ShowAiAgent, DefaultSeoTitle, DefaultSeoDescription); }
