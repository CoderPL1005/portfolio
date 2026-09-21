using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Portfolio.Application.Common.Abstractions.Authentication;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Common.Exceptions;
using Portfolio.Application.Common.Models;
using Portfolio.Application.Features.JobHunting;
using Portfolio.IntegrationTests.Authentication;

namespace Portfolio.IntegrationTests.PortfolioContent;

public sealed class JobHuntingApiTests(AuthApiFactory root):IClassFixture<AuthApiFactory>
{
    [Theory]
    [InlineData("GET","/api/v1/admin/job-postings")][InlineData("POST","/api/v1/admin/job-postings")][InlineData("GET","/api/v1/admin/job-postings/11111111-1111-1111-1111-111111111111")][InlineData("PUT","/api/v1/admin/job-postings/11111111-1111-1111-1111-111111111111")][InlineData("PUT","/api/v1/admin/job-postings/11111111-1111-1111-1111-111111111111/selection")][InlineData("POST","/api/v1/admin/job-postings/11111111-1111-1111-1111-111111111111/archive")][InlineData("GET","/api/v1/admin/job-postings/11111111-1111-1111-1111-111111111111/fit-analysis")][InlineData("GET","/api/v1/admin/job-hunting/preferences")][InlineData("PUT","/api/v1/admin/job-hunting/preferences")][InlineData("GET","/api/v1/admin/job-applications")][InlineData("POST","/api/v1/admin/job-applications")][InlineData("GET","/api/v1/admin/job-applications/22222222-2222-2222-2222-222222222222")][InlineData("PUT","/api/v1/admin/job-applications/22222222-2222-2222-2222-222222222222/status")][InlineData("POST","/api/v1/admin/job-applications/22222222-2222-2222-2222-222222222222/documents")]
    public async Task All_job_hunting_routes_require_authentication(string method,string path){using var client=root.CreateClient();using var response=await client.SendAsync(new HttpRequestMessage(new HttpMethod(method),path){Content=JsonContent.Create(new{})});Assert.Equal(HttpStatusCode.Unauthorized,response.StatusCode);}

    [Fact]public async Task Authorized_list_and_create_routes_use_api_response_contract()
    {
        using var factory=CreateFactory();using var client=Authenticated(factory);var jobs=await client.GetAsync("/api/v1/admin/job-postings?page=1&pageSize=20");var apps=await client.GetAsync("/api/v1/admin/job-applications");Assert.Equal(HttpStatusCode.OK,jobs.StatusCode);Assert.Equal(HttpStatusCode.OK,apps.StatusCode);Assert.True((await jobs.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("success").GetBoolean());
        var createJob=await client.PostAsJsonAsync("/api/v1/admin/job-postings",new{source="MANUAL",sourceExternalId="admin-1",sourceUrl="https://example.com/job",rawContent="raw",companyName="Acme",positionTitle="Developer",location="Hanoi",description="Description",technologyStack=new[]{"C#"},applicationEmail="jobs@example.com",applicationUrl="https://example.com/apply"});Assert.Equal(HttpStatusCode.Created,createJob.StatusCode);var jobBody=await createJob.Content.ReadFromJsonAsync<JsonElement>();Assert.Equal(Fakes.JobId,jobBody.GetProperty("data").GetProperty("id").GetGuid());
        var createApp=await client.PostAsJsonAsync("/api/v1/admin/job-applications",new{jobPostingId=Fakes.JobId,expectedJobVersion=1});Assert.Equal(HttpStatusCode.Created,createApp.StatusCode);Assert.Equal(Fakes.ApplicationId,(await createApp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("id").GetGuid());
    }

    [Fact]public async Task Validation_not_found_and_conflict_use_existing_error_envelopes()
    {
        using var factory=CreateFactory();using var client=Authenticated(factory);var validation=await client.GetAsync("/api/v1/admin/job-postings?page=0&pageSize=500");Assert.Equal(HttpStatusCode.BadRequest,validation.StatusCode);Assert.Equal("VALIDATION_ERROR",(await validation.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetProperty("code").GetString());var invalidCreate=await client.PostAsJsonAsync("/api/v1/admin/job-applications",new{jobPostingId=Fakes.JobId});Assert.Equal(HttpStatusCode.BadRequest,invalidCreate.StatusCode);var missing=await client.GetAsync($"/api/v1/admin/job-postings/{Guid.Empty}");Assert.Equal(HttpStatusCode.NotFound,missing.StatusCode);Assert.Equal("JOB_POSTING_NOT_FOUND",(await missing.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetProperty("code").GetString());var conflict=await client.PostAsJsonAsync($"/api/v1/admin/job-postings/{Fakes.JobId}/archive",new{expectedVersion=1});Assert.Equal(HttpStatusCode.Conflict,conflict.StatusCode);Assert.Equal("JOB_ALREADY_ARCHIVED",(await conflict.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]public async Task Authorized_private_preferences_and_fit_analysis_use_api_envelopes()
    {
        using var factory=CreateFactory();using var client=Authenticated(factory);
        var empty=await client.GetAsync("/api/v1/admin/job-hunting/preferences");Assert.Equal(HttpStatusCode.OK,empty.StatusCode);Assert.Equal(0,(await empty.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("version").GetInt32());
        var update=await client.PutAsJsonAsync("/api/v1/admin/job-hunting/preferences",new{expectedVersion=0,targetRoles=new[]{"Backend Developer"},preferredTechnologies=Array.Empty<string>(),acceptableLocations=Array.Empty<string>(),workplaceTypes=Array.Empty<string>(),employmentTypes=Array.Empty<string>(),minimumSalary=(decimal?)null,salaryCurrency=(string?)null,salaryPeriod=(string?)null});Assert.Equal(HttpStatusCode.OK,update.StatusCode);
        var fit=await client.GetAsync($"/api/v1/admin/job-postings/{Fakes.JobId}/fit-analysis");Assert.Equal(HttpStatusCode.OK,fit.StatusCode);var data=(await fit.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");Assert.Equal(Fakes.JobId,data.GetProperty("jobPostingId").GetGuid());Assert.Equal(0,data.GetProperty("availableWeight").GetInt32());Assert.Equal("NEEDS_REVIEW",data.GetProperty("recommendation").GetString());
    }

    private WebApplicationFactory<Program> CreateFactory()=>root.WithWebHostBuilder(builder=>builder.ConfigureServices(services=>
    {
        Replace<GetJobPostingsQuery,PagedResult<JobPostingListItem>,Fakes>(services);
        Replace<GetJobPostingQuery,JobPostingResult,Fakes>(services);Replace<CreateJobPostingCommand,JobPostingResult,Fakes>(services);Replace<ArchiveJobPostingCommand,JobPostingResult,Fakes>(services);
        Replace<GetJobApplicationsQuery,PagedResult<JobApplicationListItem>,Fakes>(services);Replace<GetJobApplicationQuery,JobApplicationResult,Fakes>(services);Replace<CreateJobApplicationCommand,JobApplicationResult,Fakes>(services);
        Replace<GetCandidateJobPreferencesQuery,CandidateJobPreferencesResult,Fakes>(services);Replace<UpdateCandidateJobPreferencesCommand,CandidateJobPreferencesResult,Fakes>(services);Replace<GetJobFitAnalysisQuery,JobFitAnalysisResult,Fakes>(services);
    }));
    private static void Replace<TRequest,TResult,THandler>(IServiceCollection services)where TRequest:IRequest<TResult>where THandler:class,IRequestHandler<TRequest,TResult>{services.RemoveAll<IRequestHandler<TRequest,TResult>>();services.AddScoped<IRequestHandler<TRequest,TResult>,THandler>();}
    private static HttpClient Authenticated(WebApplicationFactory<Program> factory){var client=factory.CreateClient();using var scope=factory.Services.CreateScope();var token=scope.ServiceProvider.GetRequiredService<IJwtTokenService>().CreateAccessToken(AuthApiFactory.AdminId,"admin@example.com");client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",token.Value);return client;}

    public sealed class Fakes:IRequestHandler<GetJobPostingsQuery,PagedResult<JobPostingListItem>>,IRequestHandler<GetJobPostingQuery,JobPostingResult>,IRequestHandler<CreateJobPostingCommand,JobPostingResult>,IRequestHandler<ArchiveJobPostingCommand,JobPostingResult>,IRequestHandler<GetJobApplicationsQuery,PagedResult<JobApplicationListItem>>,IRequestHandler<GetJobApplicationQuery,JobApplicationResult>,IRequestHandler<CreateJobApplicationCommand,JobApplicationResult>,IRequestHandler<GetCandidateJobPreferencesQuery,CandidateJobPreferencesResult>,IRequestHandler<UpdateCandidateJobPreferencesCommand,CandidateJobPreferencesResult>,IRequestHandler<GetJobFitAnalysisQuery,JobFitAnalysisResult>
    {
        public static readonly Guid JobId=Guid.Parse("11111111-1111-1111-1111-111111111111");public static readonly Guid ApplicationId=Guid.Parse("22222222-2222-2222-2222-222222222222");private static JsonElement EmptyArray(){using var d=JsonDocument.Parse("[]");return d.RootElement.Clone();}
        private static JobPostingResult Job()=>new(JobId,"Acme","Developer","Hanoi",null,null,null,null,null,null,null,"Description",EmptyArray(),null,null,"PENDING","PENDING_ANALYSIS",null,null,null,null,1,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,[],[]);
        private static JobApplicationResult Application()=>new(ApplicationId,JobId,"DRAFT","MANUAL",null,null,null,null,null,null,1,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,new(JobId,"Acme","Developer","Hanoi","PENDING","PENDING_ANALYSIS",false),[],[]);
        public Task<PagedResult<JobPostingListItem>> HandleAsync(GetJobPostingsQuery r,CancellationToken ct=default)=>Task.FromResult(new PagedResult<JobPostingListItem>([],r.Page,r.PageSize,0));
        public Task<JobPostingResult> HandleAsync(GetJobPostingQuery r,CancellationToken ct=default)=>r.Id==Guid.Empty?throw new NotFoundException("JOB_POSTING_NOT_FOUND","The job posting was not found."):Task.FromResult(Job());
        public Task<JobPostingResult> HandleAsync(CreateJobPostingCommand r,CancellationToken ct=default)=>Task.FromResult(Job());
        public Task<JobPostingResult> HandleAsync(ArchiveJobPostingCommand r,CancellationToken ct=default)=>throw new ConflictException("JOB_ALREADY_ARCHIVED","The job posting is already archived.");
        public Task<PagedResult<JobApplicationListItem>> HandleAsync(GetJobApplicationsQuery r,CancellationToken ct=default)=>Task.FromResult(new PagedResult<JobApplicationListItem>([],r.Page,r.PageSize,0));
        public Task<JobApplicationResult> HandleAsync(GetJobApplicationQuery r,CancellationToken ct=default)=>Task.FromResult(Application());
        public Task<JobApplicationResult> HandleAsync(CreateJobApplicationCommand r,CancellationToken ct=default)=>Task.FromResult(Application());
        public Task<CandidateJobPreferencesResult> HandleAsync(GetCandidateJobPreferencesQuery r,CancellationToken ct=default)=>Task.FromResult(new CandidateJobPreferencesResult(null,[],[],[],[],[],null,null,null,0,null,null));
        public Task<CandidateJobPreferencesResult> HandleAsync(UpdateCandidateJobPreferencesCommand r,CancellationToken ct=default)=>Task.FromResult(new CandidateJobPreferencesResult(Guid.NewGuid(),r.TargetRoles,r.PreferredTechnologies,r.AcceptableLocations,r.WorkplaceTypes,r.EmploymentTypes,r.MinimumSalary,r.SalaryCurrency,r.SalaryPeriod,1,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow));
        public Task<JobFitAnalysisResult> HandleAsync(GetJobFitAnalysisQuery r,CancellationToken ct=default)=>Task.FromResult(new JobFitAnalysisResult(r.JobPostingId,1,0,null,0,100,0,"PENDING",[],[],[],[],["Role alignment"]){Recommendation="NEEDS_REVIEW",Concerns=["No fit score can be calculated from the available evidence."]});
    }
}
