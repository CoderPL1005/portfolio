using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Portfolio.Api.Contracts.Common;
using Portfolio.Application.Features.JobHunting;
namespace Portfolio.Api.Controllers;

[ApiController,Authorize,Route("api/v1/admin/job-hunting/discovery/executions")]
public sealed class DiscoveryExecutionsController(IDiscoveryExecutionService service):ControllerBase
{
    [HttpPost,RequestSizeLimit(4096)]
    public async Task<IActionResult> Start(StartDiscoveryExecutionRequest request,CancellationToken ct)=>Accepted(ApiResponse<object>.Ok(await service.StartAsync(request.ClientRequestId,"ADMIN_MANUAL",ct)));
    [HttpGet,ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
    public async Task<IActionResult> History([FromQuery]int page=1,[FromQuery]int pageSize=20,CancellationToken ct=default)=>Ok(ApiResponse<object>.Ok(await service.HistoryAsync(page,pageSize,ct)));
    [HttpGet("{id:guid}"),ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
    public async Task<IActionResult> Detail(Guid id,CancellationToken ct)=>Ok(ApiResponse<object>.Ok(await service.GetAsync(id,ct)));
}
