using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Portfolio.Api.Contracts.Common;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Features.JobHunting;

namespace Portfolio.Api.Controllers;

[ApiController, Authorize, Route("api/v1/admin/job-hunting/preferences")]
public sealed class AdminCandidateJobPreferencesController(IRequestDispatcher dispatcher) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<CandidateJobPreferencesResult>>> Get(CancellationToken ct) =>
        Ok(ApiResponse<CandidateJobPreferencesResult>.Ok(await dispatcher.DispatchAsync(new GetCandidateJobPreferencesQuery(), ct)));

    [HttpPut]
    public async Task<ActionResult<ApiResponse<CandidateJobPreferencesResult>>> Update(CandidateJobPreferencesRequest request, CancellationToken ct) =>
        Ok(ApiResponse<CandidateJobPreferencesResult>.Ok(await dispatcher.DispatchAsync(request.Command(), ct)));
}

public sealed record CandidateJobPreferencesRequest(int ExpectedVersion,
    IReadOnlyCollection<string> TargetRoles, IReadOnlyCollection<string> PreferredTechnologies,
    IReadOnlyCollection<string> AcceptableLocations, IReadOnlyCollection<string> WorkplaceTypes,
    IReadOnlyCollection<string> EmploymentTypes, decimal? MinimumSalary, string? SalaryCurrency,
    string? SalaryPeriod)
{
    public UpdateCandidateJobPreferencesCommand Command() => new(ExpectedVersion, TargetRoles,
        PreferredTechnologies, AcceptableLocations, WorkplaceTypes, EmploymentTypes,
        MinimumSalary, SalaryCurrency, SalaryPeriod);
}
