using Microsoft.EntityFrameworkCore;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Common.Abstractions.Persistence;
using Portfolio.Application.Common.Abstractions.Validation;
using Portfolio.Application.Common.Exceptions;
using Portfolio.Application.Features.Phase4B;
using Portfolio.Application.Features.PortfolioContent;
using Portfolio.Domain.Entities;

namespace Portfolio.Application.Features.Technologies;

public sealed record GetTechnologiesQuery(string? Search, string? Category) : IRequest<IReadOnlyCollection<TechnologyResult>>;
public sealed record CreateTechnologyCommand(string Name, string Category, string? IconKey,
    string? WebsiteUrl, int DisplayOrder, bool IsActive) : IRequest<TechnologyResult>;
public sealed record UpdateTechnologyCommand(Guid Id, string Name, string Category, string? IconKey,
    string? WebsiteUrl, int DisplayOrder, bool IsActive) : IRequest<TechnologyResult>;
public sealed record DeleteTechnologyCommand(Guid Id) : IRequest<bool>;

public sealed class GetTechnologiesQueryHandler(IApplicationDbContext db) : IRequestHandler<GetTechnologiesQuery, IReadOnlyCollection<TechnologyResult>>
{
    public async Task<IReadOnlyCollection<TechnologyResult>> HandleAsync(GetTechnologiesQuery request, CancellationToken ct = default)
    {
        var query = db.Technologies.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(request.Search)) { var value = request.Search.Trim().ToLowerInvariant(); query = query.Where(x => x.Name.ToLower().Contains(value)); }
        if (!string.IsNullOrWhiteSpace(request.Category)) { var value = request.Category.Trim().ToLowerInvariant(); query = query.Where(x => x.Category.ToLower() == value); }
        return await query.OrderBy(x => x.DisplayOrder).ThenBy(x => x.Id).Select(TechnologyMutation.Project).ToListAsync(ct);
    }
}
public sealed class CreateTechnologyCommandHandler(IApplicationDbContext db, TimeProvider clock) : IRequestHandler<CreateTechnologyCommand, TechnologyResult>
{
    public async Task<TechnologyResult> HandleAsync(CreateTechnologyCommand request, CancellationToken ct = default)
    {
        await TechnologyMutation.EnsureUniqueAsync(db, request.Name, null, ct);
        var now = clock.GetUtcNow(); var entity = new Technology { Id = Guid.NewGuid(), Name = request.Name.Trim(), Category = request.Category.Trim(), IconKey = request.IconKey?.Trim(), WebsiteUrl = request.WebsiteUrl?.Trim(), DisplayOrder = request.DisplayOrder, IsActive = request.IsActive, CreatedAt = now, UpdatedAt = now };
        db.Technologies.Add(entity); await db.SaveChangesAsync(ct); return TechnologyMutation.Map(entity);
    }
}
public sealed class UpdateTechnologyCommandHandler(IApplicationDbContext db, TimeProvider clock) : IRequestHandler<UpdateTechnologyCommand, TechnologyResult>
{
    public async Task<TechnologyResult> HandleAsync(UpdateTechnologyCommand request, CancellationToken ct = default)
    {
        var entity = await db.Technologies.SingleOrDefaultAsync(x => x.Id == request.Id, ct) ?? throw new NotFoundException("TECHNOLOGY_NOT_FOUND", "The technology was not found.");
        await TechnologyMutation.EnsureUniqueAsync(db, request.Name, request.Id, ct);
        entity.Name = request.Name.Trim(); entity.Category = request.Category.Trim(); entity.IconKey = request.IconKey?.Trim(); entity.WebsiteUrl = request.WebsiteUrl?.Trim(); entity.DisplayOrder = request.DisplayOrder; entity.IsActive = request.IsActive; entity.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct); return TechnologyMutation.Map(entity);
    }
}
public sealed class DeleteTechnologyCommandHandler(IApplicationDbContext db) : IRequestHandler<DeleteTechnologyCommand, bool>
{
    public async Task<bool> HandleAsync(DeleteTechnologyCommand request, CancellationToken ct = default)
    {
        var entity = await db.Technologies.SingleOrDefaultAsync(x => x.Id == request.Id, ct) ?? throw new NotFoundException("TECHNOLOGY_NOT_FOUND", "The technology was not found.");
        if (await db.ExperienceTechnologies.AnyAsync(x => x.TechnologyId == request.Id, ct) || await db.ProjectTechnologies.AnyAsync(x => x.TechnologyId == request.Id, ct) || await db.Skills.AnyAsync(x => x.TechnologyId == request.Id, ct))
            throw new ConflictException("TECHNOLOGY_IN_USE", "The technology is referenced by portfolio content.");
        db.Technologies.Remove(entity); await db.SaveChangesAsync(ct); return true;
    }
}
public sealed class CreateTechnologyCommandValidator : IRequestValidator<CreateTechnologyCommand>
{ public Task<IReadOnlyCollection<ValidationFailure>> ValidateAsync(CreateTechnologyCommand r, CancellationToken ct = default) => Task.FromResult<IReadOnlyCollection<ValidationFailure>>(TechnologyValidation.Validate(r.Name, r.Category, r.IconKey, r.WebsiteUrl, r.DisplayOrder)); }
public sealed class UpdateTechnologyCommandValidator : IRequestValidator<UpdateTechnologyCommand>
{ public Task<IReadOnlyCollection<ValidationFailure>> ValidateAsync(UpdateTechnologyCommand r, CancellationToken ct = default) => Task.FromResult<IReadOnlyCollection<ValidationFailure>>(TechnologyValidation.Validate(r.Name, r.Category, r.IconKey, r.WebsiteUrl, r.DisplayOrder)); }

internal static class TechnologyValidation
{
    public static List<ValidationFailure> Validate(string name, string category, string? icon, string? url, int order)
    { var f = new List<ValidationFailure>(); ContentValidation.RequiredText(f, "name", name, 100); ContentValidation.RequiredText(f, "category", category, 50); ContentValidation.OptionalText(f, "iconKey", icon, 100); ContentValidation.HttpUrl(f, "websiteUrl", url); ContentValidation.DisplayOrder(f, order); return f; }
}
internal static class TechnologyMutation
{
    public static readonly System.Linq.Expressions.Expression<Func<Technology, TechnologyResult>> Project = x => new(x.Id, x.Name, x.Category, x.IconKey, x.WebsiteUrl, x.DisplayOrder, x.IsActive, x.UpdatedAt);
    public static TechnologyResult Map(Technology x) => new(x.Id, x.Name, x.Category, x.IconKey, x.WebsiteUrl, x.DisplayOrder, x.IsActive, x.UpdatedAt);
    public static async Task EnsureUniqueAsync(IApplicationDbContext db, string name, Guid? exceptId, CancellationToken ct)
    { var value = name.Trim().ToLowerInvariant(); if (await db.Technologies.AnyAsync(x => x.Id != exceptId && x.Name.ToLower() == value, ct)) throw new ConflictException("TECHNOLOGY_NAME_EXISTS", "A technology with this name already exists."); }
}
