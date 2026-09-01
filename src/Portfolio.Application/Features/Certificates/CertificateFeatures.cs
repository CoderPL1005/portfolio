using Microsoft.EntityFrameworkCore;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Common.Abstractions.Persistence;
using Portfolio.Application.Common.Abstractions.Validation;
using Portfolio.Application.Common.Exceptions;
using Portfolio.Application.Features.PortfolioContent;

namespace Portfolio.Application.Features.Certificates;

public sealed record GetCertificatesQuery : IRequest<IReadOnlyCollection<CertificateResult>>;
public sealed record GetCertificateQuery(Guid Id) : IRequest<CertificateResult>;
public sealed record CreateCertificateCommand(string Name, string? Issuer, DateOnly? IssuedAt,
    DateOnly? ExpiresAt, string? CredentialId, string? CredentialUrl, Guid? CertificateMediaId,
    int DisplayOrder, bool IsPublished) : IRequest<CertificateResult>;
public sealed record UpdateCertificateCommand(Guid Id, string Name, string? Issuer, DateOnly? IssuedAt,
    DateOnly? ExpiresAt, string? CredentialId, string? CredentialUrl, Guid? CertificateMediaId,
    int DisplayOrder, bool IsPublished) : IRequest<CertificateResult>;
public sealed record DeleteCertificateCommand(Guid Id) : IRequest<bool>;
public sealed record ReorderCertificatesCommand(IReadOnlyCollection<ReorderItem> Items) : IRequest<bool>;

public sealed class GetCertificatesQueryHandler(IApplicationDbContext dbContext) : IRequestHandler<GetCertificatesQuery, IReadOnlyCollection<CertificateResult>>
{
    public async Task<IReadOnlyCollection<CertificateResult>> HandleAsync(GetCertificatesQuery request, CancellationToken cancellationToken = default) =>
        await CertificateProjection.Project(dbContext.Certificates.AsNoTracking().OrderBy(item => item.DisplayOrder).ThenBy(item => item.Id)).ToListAsync(cancellationToken);
}
public sealed class GetCertificateQueryHandler(IApplicationDbContext dbContext) : IRequestHandler<GetCertificateQuery, CertificateResult>
{
    public async Task<CertificateResult> HandleAsync(GetCertificateQuery request, CancellationToken cancellationToken = default) =>
        await CertificateProjection.Project(dbContext.Certificates.AsNoTracking().Where(item => item.Id == request.Id)).SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("CERTIFICATE_NOT_FOUND", "The certificate was not found.");
}
public sealed class CreateCertificateCommandHandler(IApplicationDbContext dbContext, TimeProvider timeProvider) : IRequestHandler<CreateCertificateCommand, CertificateResult>
{
    public async Task<CertificateResult> HandleAsync(CreateCertificateCommand request, CancellationToken cancellationToken = default)
    {
        await CertificateMutation.ValidateMediaAsync(dbContext, request.CertificateMediaId, cancellationToken);
        var now = timeProvider.GetUtcNow(); var entity = new Portfolio.Domain.Entities.Certificate
        { Id = Guid.NewGuid(), Name = request.Name.Trim(), Issuer = request.Issuer?.Trim(), IssuedAt = request.IssuedAt, ExpiresAt = request.ExpiresAt, CredentialId = request.CredentialId?.Trim(), CredentialUrl = request.CredentialUrl?.Trim(), CertificateMediaId = request.CertificateMediaId, DisplayOrder = request.DisplayOrder, IsPublished = request.IsPublished, CreatedAt = now, UpdatedAt = now };
        dbContext.Certificates.Add(entity); await dbContext.SaveChangesAsync(cancellationToken); return CertificateProjection.Map(entity);
    }
}
public sealed class UpdateCertificateCommandHandler(IApplicationDbContext dbContext, TimeProvider timeProvider) : IRequestHandler<UpdateCertificateCommand, CertificateResult>
{
    public async Task<CertificateResult> HandleAsync(UpdateCertificateCommand request, CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.Certificates.SingleOrDefaultAsync(item => item.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("CERTIFICATE_NOT_FOUND", "The certificate was not found.");
        await CertificateMutation.ValidateMediaAsync(dbContext, request.CertificateMediaId, cancellationToken);
        entity.Name = request.Name.Trim(); entity.Issuer = request.Issuer?.Trim(); entity.IssuedAt = request.IssuedAt; entity.ExpiresAt = request.ExpiresAt;
        entity.CredentialId = request.CredentialId?.Trim(); entity.CredentialUrl = request.CredentialUrl?.Trim(); entity.CertificateMediaId = request.CertificateMediaId;
        entity.DisplayOrder = request.DisplayOrder; entity.IsPublished = request.IsPublished; entity.UpdatedAt = timeProvider.GetUtcNow();
        await dbContext.SaveChangesAsync(cancellationToken); return CertificateProjection.Map(entity);
    }
}
public sealed class DeleteCertificateCommandHandler(IApplicationDbContext dbContext) : IRequestHandler<DeleteCertificateCommand, bool>
{
    public async Task<bool> HandleAsync(DeleteCertificateCommand request, CancellationToken cancellationToken = default)
    { var entity = await dbContext.Certificates.SingleOrDefaultAsync(item => item.Id == request.Id, cancellationToken) ?? throw new NotFoundException("CERTIFICATE_NOT_FOUND", "The certificate was not found."); dbContext.Certificates.Remove(entity); await dbContext.SaveChangesAsync(cancellationToken); return true; }
}
public sealed class ReorderCertificatesCommandHandler(IApplicationDbContext dbContext) : IRequestHandler<ReorderCertificatesCommand, bool>
{
    public async Task<bool> HandleAsync(ReorderCertificatesCommand request, CancellationToken cancellationToken = default)
    { var ids = request.Items.Select(item => item.Id).ToArray(); var entities = await dbContext.Certificates.Where(item => ids.Contains(item.Id)).ToListAsync(cancellationToken); if (entities.Count != ids.Length) throw new NotFoundException("CERTIFICATE_NOT_FOUND", "One or more certificates were not found."); var order = request.Items.ToDictionary(item => item.Id, item => item.DisplayOrder); foreach (var entity in entities) entity.DisplayOrder = order[entity.Id]; await dbContext.SaveChangesAsync(cancellationToken); return true; }
}

public sealed class CreateCertificateCommandValidator : IRequestValidator<CreateCertificateCommand>
{ public Task<IReadOnlyCollection<ValidationFailure>> ValidateAsync(CreateCertificateCommand request, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyCollection<ValidationFailure>>(CertificateValidation.Validate(request.Name, request.Issuer, request.IssuedAt, request.ExpiresAt, request.CredentialId, request.CredentialUrl, request.DisplayOrder)); }
public sealed class UpdateCertificateCommandValidator : IRequestValidator<UpdateCertificateCommand>
{ public Task<IReadOnlyCollection<ValidationFailure>> ValidateAsync(UpdateCertificateCommand request, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyCollection<ValidationFailure>>(CertificateValidation.Validate(request.Name, request.Issuer, request.IssuedAt, request.ExpiresAt, request.CredentialId, request.CredentialUrl, request.DisplayOrder)); }
public sealed class ReorderCertificatesCommandValidator : IRequestValidator<ReorderCertificatesCommand>
{ public Task<IReadOnlyCollection<ValidationFailure>> ValidateAsync(ReorderCertificatesCommand request, CancellationToken cancellationToken = default) { var failures = new List<ValidationFailure>(); ContentValidation.ReorderItems(failures, request.Items); return Task.FromResult<IReadOnlyCollection<ValidationFailure>>(failures); } }

internal static class CertificateValidation
{
    public static List<ValidationFailure> Validate(string name, string? issuer, DateOnly? issued, DateOnly? expires, string? credentialId, string? url, int displayOrder)
    { var failures = new List<ValidationFailure>(); ContentValidation.RequiredText(failures, "name", name, 255); ContentValidation.OptionalText(failures, "issuer", issuer, 255); ContentValidation.OptionalText(failures, "credentialId", credentialId, 255); ContentValidation.DateRange(failures, "expiresAt", issued, expires); ContentValidation.HttpUrl(failures, "credentialUrl", url); ContentValidation.DisplayOrder(failures, displayOrder); return failures; }
}
internal static class CertificateProjection
{
    public static IQueryable<CertificateResult> Project(IQueryable<Portfolio.Domain.Entities.Certificate> query) => query.Select(item => new CertificateResult(item.Id, item.Name, item.Issuer, item.IssuedAt, item.ExpiresAt, item.CredentialId, item.CredentialUrl, item.CertificateMediaId, item.DisplayOrder, item.IsPublished));
    public static CertificateResult Map(Portfolio.Domain.Entities.Certificate item) => new(item.Id, item.Name, item.Issuer, item.IssuedAt, item.ExpiresAt, item.CredentialId, item.CredentialUrl, item.CertificateMediaId, item.DisplayOrder, item.IsPublished);
}
internal static class CertificateMutation
{
    public static async Task ValidateMediaAsync(IApplicationDbContext dbContext, Guid? mediaId, CancellationToken cancellationToken)
    { if (mediaId.HasValue && !await dbContext.MediaAssets.AnyAsync(item => item.Id == mediaId.Value, cancellationToken)) throw new NotFoundException("MEDIA_NOT_FOUND", "The selected media asset was not found."); }
}
