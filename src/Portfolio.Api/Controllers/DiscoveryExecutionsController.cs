using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Portfolio.Api.Contracts.Common;
using Portfolio.Application.Features.JobHunting;
namespace Portfolio.Api.Controllers;

[ApiController,Authorize,Route("api/v1/admin/job-hunting/discovery/executions")]
public sealed class DiscoveryExecutionsController(IDiscoveryExecutionService service, IDiscoveryExecutionRecovery recovery):ControllerBase
{
    [HttpPost,RequestSizeLimit(4096)]
    public async Task<IActionResult> Start(StartDiscoveryExecutionRequest request,CancellationToken ct)=>Accepted(ApiResponse<object>.Ok(await service.StartAsync(request.ClientRequestId,"ADMIN_MANUAL",ct,request.ConfirmGeminiFreeTier)));
    [HttpGet,ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
    public async Task<IActionResult> History([FromQuery]int page=1,[FromQuery]int pageSize=20,CancellationToken ct=default)=>Ok(ApiResponse<object>.Ok(await service.HistoryAsync(page,pageSize,ct)));
    [HttpGet("{id:guid}"),ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
    public async Task<IActionResult> Detail(Guid id,CancellationToken ct)=>Ok(ApiResponse<object>.Ok(await service.GetAsync(id,ct)));
    [HttpPost("{id:guid}/processing"),RequestSizeLimit(1024*1024)]
    public async Task<IActionResult> Process(Guid id,DiscoveryProcessingRequest request,CancellationToken ct)=>Ok(ApiResponse<object>.Ok(await service.ProcessAsync(id,request,null,ct)));
    [HttpPost("{id:guid}/extract-persisted"),RequestSizeLimit(100*1024)]
    public async Task<IActionResult> ExtractPersisted(Guid id,DiscoveryPersistedSemanticRequest request,CancellationToken ct)=>Ok(ApiResponse<object>.Ok(await service.ExtractPersistedAsync(id,request,ct)));
    [HttpGet("{id:guid}/recovery"),ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
    public async Task<IActionResult> RecoveryPreview(Guid id,CancellationToken ct)=>Ok(ApiResponse<object>.Ok(await recovery.PreviewAsync(id,ct)));
    [HttpPost("{id:guid}/recovery/finalize"),RequestSizeLimit(8192)]
    public async Task<IActionResult> RecoveryFinalize(Guid id,DiscoveryRecoveryFinalize request,CancellationToken ct)=>Ok(ApiResponse<object>.Ok(await recovery.FinalizeAsync(id,request,ct)));
}
