using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Portfolio.Api.Contracts.Common;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Features.PortfolioContent;
using Portfolio.Application.Features.PortfolioContent.GetPublicPortfolio;

namespace Portfolio.Api.Controllers;

[ApiController]
[Route("api/v1/public/portfolio")]
public sealed class PublicPortfolioController(IRequestDispatcher dispatcher) : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<PortfolioHomeResult>>> Get(CancellationToken cancellationToken) =>
        Ok(ApiResponse<PortfolioHomeResult>.Ok(
            await dispatcher.DispatchAsync(new GetPublicPortfolioQuery(), cancellationToken)));
}
