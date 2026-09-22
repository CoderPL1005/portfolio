using System.Text.Json;
using Microsoft.Net.Http.Headers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Portfolio.Api.Contracts.Common;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Common.Models;
using Portfolio.Application.Features.JobHunting;

namespace Portfolio.Api.Controllers;

[ApiController,Authorize,Route("api/v1/admin/job-applications")]
public sealed class AdminJobApplicationsController(IRequestDispatcher dispatcher):ControllerBase
{
    [HttpGet]public async Task<ActionResult<ApiResponse<PagedResult<JobApplicationListItem>>>> List([FromQuery]int page=1,[FromQuery]int pageSize=20,[FromQuery]string? search=null,[FromQuery]string? status=null,[FromQuery]string? channel=null,CancellationToken ct=default)=>Ok(ApiResponse<PagedResult<JobApplicationListItem>>.Ok(await dispatcher.DispatchAsync(new GetJobApplicationsQuery(page,pageSize,search,status,channel),ct)));
    [HttpGet("{id:guid}")]public async Task<ActionResult<ApiResponse<JobApplicationResult>>> Get(Guid id,CancellationToken ct)=>Ok(ApiResponse<JobApplicationResult>.Ok(await dispatcher.DispatchAsync(new GetJobApplicationQuery(id),ct)));
    [HttpGet("/api/v1/admin/job-hunting/applications/{applicationId:guid}/package-readiness")]
    public async Task<ActionResult<ApiResponse<ApplicationPackageReadinessResult>>> PackageReadiness(Guid applicationId,CancellationToken ct)=>Ok(ApiResponse<ApplicationPackageReadinessResult>.Ok(await dispatcher.DispatchAsync(new GetApplicationPackageReadinessQuery(applicationId),ct)));
    [HttpGet("/api/v1/admin/job-hunting/applications/{applicationId:guid}/package")]
    public async Task<ActionResult<ApiResponse<ApplicationPackageResult>>> Package(Guid applicationId,CancellationToken ct)=>Ok(ApiResponse<ApplicationPackageResult>.Ok(await dispatcher.DispatchAsync(new GetApplicationPackageQuery(applicationId),ct)));
    [HttpPost("/api/v1/admin/job-hunting/applications/{applicationId:guid}/package/finalize")]
    public async Task<ActionResult<ApiResponse<ApplicationPackageResult>>> FinalizePackage(Guid applicationId,FinalizeApplicationPackageRequest request,CancellationToken ct)=>Ok(ApiResponse<ApplicationPackageResult>.Ok(await dispatcher.DispatchAsync(request.Command(applicationId),ct)));
    [HttpGet("/api/v1/admin/job-hunting/applications/{applicationId:guid}/package/cv")]
    public async Task<IActionResult> PackageCv(Guid applicationId,[FromQuery]bool download=false,CancellationToken ct=default)
    {
        var result=await dispatcher.DispatchAsync(new GetApplicationPackageContentQuery(applicationId),ct);
        Response.Headers[HeaderNames.XContentTypeOptions]="nosniff";
        Response.Headers.CacheControl="no-store, private";
        if(download)return File(result.Content,result.ContentType,result.FileName,enableRangeProcessing:false);
        var disposition=new ContentDispositionHeaderValue("inline"){FileNameStar=result.FileName};
        Response.Headers.ContentDisposition=disposition.ToString();
        return File(result.Content,result.ContentType);
    }
    [HttpPost]public async Task<ActionResult<ApiResponse<JobApplicationResult>>> Create(JobApplicationCreateRequest request,CancellationToken ct){var result=await dispatcher.DispatchAsync(request.Command(),ct);return CreatedAtAction(nameof(Get),new{id=result.Id},ApiResponse<JobApplicationResult>.Ok(result));}
    [HttpPut("{id:guid}")]public async Task<ActionResult<ApiResponse<JobApplicationResult>>> Update(Guid id,JobApplicationUpdateRequest request,CancellationToken ct)=>Ok(ApiResponse<JobApplicationResult>.Ok(await dispatcher.DispatchAsync(request.Command(id),ct)));
    [HttpPut("{id:guid}/status")]public async Task<ActionResult<ApiResponse<JobApplicationResult>>> Status(Guid id,JobApplicationStatusRequest request,CancellationToken ct)=>Ok(ApiResponse<JobApplicationResult>.Ok(await dispatcher.DispatchAsync(new TransitionJobApplicationCommand(id,request.Status,request.ExpectedVersion,request.Note,request.OccurredAt),ct)));
    [HttpPost("{id:guid}/documents")]public async Task<ActionResult<ApiResponse<JobApplicationDocumentResult>>> AttachDocument(Guid id,JobApplicationDocumentRequest request,CancellationToken ct){var result=await dispatcher.DispatchAsync(request.Command(id),ct);return StatusCode(StatusCodes.Status201Created,ApiResponse<JobApplicationDocumentResult>.Ok(result));}
    [HttpDelete("{id:guid}/documents/{documentId:guid}")]public async Task<IActionResult> RemoveDocument(Guid id,Guid documentId,CancellationToken ct){await dispatcher.DispatchAsync(new RemoveJobApplicationDocumentCommand(id,documentId),ct);return NoContent();}
}

public sealed record JobApplicationCreateRequest(Guid JobPostingId,int ExpectedJobVersion){public CreateJobApplicationCommand Command()=>new(JobPostingId,ExpectedJobVersion);}
public sealed record JobApplicationUpdateRequest(int ExpectedVersion,string? Channel,string? ApplicationEmail,string? ApplicationUrl,string? ExternalApplicationId,string? Notes){public UpdateJobApplicationCommand Command(Guid id)=>new(id,ExpectedVersion,Channel,ApplicationEmail,ApplicationUrl,ExternalApplicationId,Notes);}
public sealed record JobApplicationStatusRequest(string Status,int ExpectedVersion,string? Note,DateTimeOffset? OccurredAt);
public sealed record JobApplicationDocumentRequest(string DocumentType,string VersionLabel,string? FileName,string? StorageKey,string? ContentHash,JsonElement? Metadata){public AttachJobApplicationDocumentCommand Command(Guid id)=>new(id,DocumentType,VersionLabel,FileName,StorageKey,ContentHash,Metadata);}
public sealed record FinalizeApplicationPackageRequest(int ExpectedApplicationVersion,int ExpectedJobPostingVersion,int ExpectedCanonicalCvVersion)
{public FinalizeApplicationPackageCommand Command(Guid applicationId)=>new(applicationId,ExpectedApplicationVersion,ExpectedJobPostingVersion,ExpectedCanonicalCvVersion);}
