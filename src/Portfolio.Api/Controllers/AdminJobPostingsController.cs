using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Portfolio.Api.Contracts.Common;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Common.Models;
using Portfolio.Application.Features.JobHunting;

namespace Portfolio.Api.Controllers;

[ApiController,Authorize,Route("api/v1/admin/job-postings")]
public sealed class AdminJobPostingsController(IRequestDispatcher dispatcher):ControllerBase
{
    [HttpGet]public async Task<ActionResult<ApiResponse<PagedResult<JobPostingListItem>>>> List([FromQuery]int page=1,[FromQuery]int pageSize=20,[FromQuery]string? search=null,[FromQuery]string? source=null,[FromQuery]string? verificationStatus=null,[FromQuery]string? selectionStatus=null,[FromQuery]bool? archived=null,CancellationToken ct=default)=>Ok(ApiResponse<PagedResult<JobPostingListItem>>.Ok(await dispatcher.DispatchAsync(new GetJobPostingsQuery(page,pageSize,search,source,verificationStatus,selectionStatus,archived),ct)));
    [HttpGet("{id:guid}")]public async Task<ActionResult<ApiResponse<JobPostingResult>>> Get(Guid id,CancellationToken ct)=>Ok(ApiResponse<JobPostingResult>.Ok(await dispatcher.DispatchAsync(new GetJobPostingQuery(id),ct)));
    [HttpPost]public async Task<ActionResult<ApiResponse<JobPostingResult>>> Create(JobPostingCreateRequest request,CancellationToken ct){var result=await dispatcher.DispatchAsync(request.Command(),ct);return CreatedAtAction(nameof(Get),new{id=result.Id},ApiResponse<JobPostingResult>.Ok(result));}
    [HttpPut("{id:guid}")]public async Task<ActionResult<ApiResponse<JobPostingResult>>> Update(Guid id,JobPostingUpdateRequest request,CancellationToken ct)=>Ok(ApiResponse<JobPostingResult>.Ok(await dispatcher.DispatchAsync(request.Command(id),ct)));
    [HttpPut("{id:guid}/verification")]public async Task<ActionResult<ApiResponse<JobPostingResult>>> Verification(Guid id,JobStateRequest request,CancellationToken ct)=>Ok(ApiResponse<JobPostingResult>.Ok(await dispatcher.DispatchAsync(new UpdateJobVerificationCommand(id,request.Status,request.ExpectedVersion),ct)));
    [HttpPut("{id:guid}/selection")]public async Task<ActionResult<ApiResponse<JobPostingResult>>> Selection(Guid id,JobStateRequest request,CancellationToken ct)=>Ok(ApiResponse<JobPostingResult>.Ok(await dispatcher.DispatchAsync(new UpdateJobSelectionCommand(id,request.Status,request.ExpectedVersion),ct)));
    [HttpPost("{id:guid}/archive")]public async Task<ActionResult<ApiResponse<JobPostingResult>>> Archive(Guid id,ExpectedVersionRequest request,CancellationToken ct)=>Ok(ApiResponse<JobPostingResult>.Ok(await dispatcher.DispatchAsync(new ArchiveJobPostingCommand(id,request.ExpectedVersion),ct)));
    [HttpGet("{id:guid}/fit-analysis")]public async Task<ActionResult<ApiResponse<JobFitAnalysisResult>>> FitAnalysis(Guid id,CancellationToken ct)=>Ok(ApiResponse<JobFitAnalysisResult>.Ok(await dispatcher.DispatchAsync(new GetJobFitAnalysisQuery(id),ct)));
}

public sealed record JobPostingCreateRequest(string Source,string? SourceExternalId,string? SourceUrl,string RawContent,string CompanyName,string PositionTitle,string Location,string? EmploymentType,string? WorkplaceType,decimal? SalaryMinimum,decimal? SalaryMaximum,string? SalaryCurrency,string? SalaryPeriod,string? ExperienceRequirements,string Description,JsonElement TechnologyStack,string? ApplicationEmail,string? ApplicationUrl,DateTimeOffset? ExpiresAt,string? Notes)
{public CreateJobPostingCommand Command()=>new(Source,SourceExternalId,SourceUrl,RawContent,CompanyName,PositionTitle,Location,EmploymentType,WorkplaceType,SalaryMinimum,SalaryMaximum,SalaryCurrency,SalaryPeriod,ExperienceRequirements,Description,TechnologyStack,ApplicationEmail,ApplicationUrl,ExpiresAt,Notes);}
public sealed record JobPostingUpdateRequest(int ExpectedVersion,string CompanyName,string PositionTitle,string Location,string? EmploymentType,string? WorkplaceType,decimal? SalaryMinimum,decimal? SalaryMaximum,string? SalaryCurrency,string? SalaryPeriod,string? ExperienceRequirements,string Description,JsonElement TechnologyStack,string? ApplicationEmail,string? ApplicationUrl,DateTimeOffset? ExpiresAt,string? Notes)
{public UpdateJobPostingCommand Command(Guid id)=>new(id,ExpectedVersion,CompanyName,PositionTitle,Location,EmploymentType,WorkplaceType,SalaryMinimum,SalaryMaximum,SalaryCurrency,SalaryPeriod,ExperienceRequirements,Description,TechnologyStack,ApplicationEmail,ApplicationUrl,ExpiresAt,Notes);}
public sealed record JobStateRequest(string Status,int ExpectedVersion);
public sealed record ExpectedVersionRequest(int ExpectedVersion);
