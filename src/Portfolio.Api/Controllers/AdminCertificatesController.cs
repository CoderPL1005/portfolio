using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Portfolio.Api.Contracts.Common;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Features.Certificates;
using Portfolio.Application.Features.PortfolioContent;

namespace Portfolio.Api.Controllers;

[ApiController, Authorize, Route("api/v1/admin/certificates")]
public sealed class AdminCertificatesController(IRequestDispatcher dispatcher) : ControllerBase
{
    [HttpGet] public async Task<ActionResult<ApiResponse<IReadOnlyCollection<CertificateResult>>>> List(CancellationToken ct) => Ok(ApiResponse<IReadOnlyCollection<CertificateResult>>.Ok(await dispatcher.DispatchAsync(new GetCertificatesQuery(), ct)));
    [HttpGet("{id:guid}")] public async Task<ActionResult<ApiResponse<CertificateResult>>> Get(Guid id, CancellationToken ct) => Ok(ApiResponse<CertificateResult>.Ok(await dispatcher.DispatchAsync(new GetCertificateQuery(id), ct)));
    [HttpPost] public async Task<ActionResult<ApiResponse<CertificateResult>>> Create(CertificateRequest request, CancellationToken ct) { var result = await dispatcher.DispatchAsync(request.Create(), ct); return CreatedAtAction(nameof(Get), new { id = result.Id }, ApiResponse<CertificateResult>.Ok(result)); }
    [HttpPut("{id:guid}")] public async Task<ActionResult<ApiResponse<CertificateResult>>> Update(Guid id, CertificateRequest request, CancellationToken ct) => Ok(ApiResponse<CertificateResult>.Ok(await dispatcher.DispatchAsync(request.Update(id), ct)));
    [HttpDelete("{id:guid}")] public async Task<IActionResult> Delete(Guid id, CancellationToken ct) { await dispatcher.DispatchAsync(new DeleteCertificateCommand(id), ct); return NoContent(); }
    [HttpPut("reorder")] public async Task<ActionResult<ApiResponse>> Reorder(ReorderRequest request, CancellationToken ct) { await dispatcher.DispatchAsync(new ReorderCertificatesCommand(request.Items), ct); return Ok(ApiResponse.Ok()); }
}
public sealed record CertificateRequest(string Name, string? Issuer, DateOnly? IssuedAt, DateOnly? ExpiresAt, string? CredentialId, string? CredentialUrl, Guid? CertificateMediaId, int DisplayOrder, bool IsPublished)
{
    public CreateCertificateCommand Create() => new(Name, Issuer, IssuedAt, ExpiresAt, CredentialId, CredentialUrl, CertificateMediaId, DisplayOrder, IsPublished);
    public UpdateCertificateCommand Update(Guid id) => new(id, Name, Issuer, IssuedAt, ExpiresAt, CredentialId, CredentialUrl, CertificateMediaId, DisplayOrder, IsPublished);
}
