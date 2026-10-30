using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Portfolio.Api.Authentication;
using Portfolio.Api.Contracts.Common;
using Portfolio.Application.Features.JobHunting;
namespace Portfolio.Api.Controllers;

[ApiController,Authorize(AuthenticationSchemes=DiscoveryExecutionAuthentication.SchemeName),Route("api/v1/discovery-execution-worker/{id:guid}")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class DiscoveryExecutionWorkerController(IDiscoveryExecutionService service):ControllerBase
{
    private string Run=>User.FindFirst("discovery_attempt")?.Value=="1"?User.FindFirst("discovery_run")!.Value:
        throw new Portfolio.Application.Common.Exceptions.ConflictException("DISCOVERY_WORKER_RERUN","A provider execution cannot be rerun.");
    [HttpGet("config")] public async Task<IActionResult> Config(Guid id,CancellationToken ct)=>Ok(ApiResponse<object>.Ok(await service.ConfigurationAsync(id,Run,ct)));
    [HttpPost("claim"),RequestSizeLimit(4096)] public async Task<IActionResult> Claim(Guid id,CancellationToken ct)=>Ok(ApiResponse<object>.Ok(await service.ClaimAsync(id,Run,ct)));
    [HttpPost("observations"),RequestSizeLimit(8*1024*1024)] public async Task<IActionResult> Observations(Guid id,DiscoveryExecutionResult result,CancellationToken ct)=>Ok(ApiResponse<object>.Ok(await service.ObservationsAsync(id,Run,result,ct)));
    [HttpPost("results"),RequestSizeLimit(8*1024*1024)] public async Task<IActionResult> Result(Guid id,DiscoveryExecutionResult result,CancellationToken ct)=>Ok(ApiResponse<object>.Ok(await service.CompleteAsync(id,Run,result,ct)));
    [HttpPost("extract"),RequestSizeLimit(100*1024)] public async Task<IActionResult> Extract(Guid id,DiscoverySemanticRequest request,CancellationToken ct)=>Ok(ApiResponse<DiscoveryTextGeneration>.Ok(await service.ExtractAsync(id,Run,request,ct)));
    [HttpPost("processing"),RequestSizeLimit(1024*1024)] public async Task<IActionResult> Process(Guid id,DiscoveryProcessingRequest request,CancellationToken ct)=>Ok(ApiResponse<object>.Ok(await service.ProcessAsync(id,request,Run,ct)));
}
