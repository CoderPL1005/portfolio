using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Portfolio.Api.Contracts.Common;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Features.Dashboard;
using Portfolio.Application.Features.Phase4C;

namespace Portfolio.Api.Controllers;

[ApiController, Authorize, Route("api/v1/admin/dashboard")]
public sealed class AdminDashboardController(IRequestDispatcher dispatcher) : ControllerBase
{
    [HttpGet] public async Task<ActionResult<ApiResponse<DashboardResult>>> Get(CancellationToken ct) => Ok(ApiResponse<DashboardResult>.Ok(await dispatcher.DispatchAsync(new GetDashboardQuery(), ct)));
}
