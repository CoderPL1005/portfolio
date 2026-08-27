using Microsoft.EntityFrameworkCore;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Common.Abstractions.Persistence;
using Portfolio.Application.Common.Abstractions.Validation;
using Portfolio.Application.Common.Exceptions;
using Portfolio.Application.Features.Phase4B;
using Portfolio.Application.Features.PortfolioContent;
using Portfolio.Domain.Entities;

namespace Portfolio.Application.Features.Skills;

public sealed record GetSkillsQuery(string? Category, string? ExperienceLevel) : IRequest<IReadOnlyCollection<SkillResult>>;
public sealed record CreateSkillCommand(string Name, string Category, string ExperienceLevel,
    string? Description, Guid? TechnologyId, int DisplayOrder, bool IsPublished) : IRequest<SkillResult>;
public sealed record UpdateSkillCommand(Guid Id, string Name, string Category, string ExperienceLevel,
    string? Description, Guid? TechnologyId, int DisplayOrder, bool IsPublished) : IRequest<SkillResult>;
public sealed record DeleteSkillCommand(Guid Id) : IRequest<bool>;
public sealed record ReorderSkillsCommand(IReadOnlyCollection<ReorderItem> Items) : IRequest<bool>;

public sealed class GetSkillsQueryHandler(IApplicationDbContext db) : IRequestHandler<GetSkillsQuery, IReadOnlyCollection<SkillResult>>
{
    public async Task<IReadOnlyCollection<SkillResult>> HandleAsync(GetSkillsQuery request, CancellationToken ct = default)
    { var q = db.Skills.AsNoTracking(); if (!string.IsNullOrWhiteSpace(request.Category)) { var v = request.Category.Trim().ToLowerInvariant(); q = q.Where(x => x.Category.ToLower() == v); } if (!string.IsNullOrWhiteSpace(request.ExperienceLevel)) { var v = request.ExperienceLevel.Trim().ToUpperInvariant(); q = q.Where(x => x.ExperienceLevel == v); } return await q.OrderBy(x => x.DisplayOrder).ThenBy(x => x.Id).Select(SkillMutation.Project).ToListAsync(ct); }
}
public sealed class CreateSkillCommandHandler(IApplicationDbContext db, TimeProvider clock) : IRequestHandler<CreateSkillCommand, SkillResult>
{
    public async Task<SkillResult> HandleAsync(CreateSkillCommand r, CancellationToken ct = default)
    { await SkillMutation.ValidateReferencesAsync(db, r.Name, r.Category, r.TechnologyId, null, ct); var now = clock.GetUtcNow(); var x = new Skill { Id = Guid.NewGuid(), Name = r.Name.Trim(), Category = r.Category.Trim(), ExperienceLevel = r.ExperienceLevel.Trim().ToUpperInvariant(), Description = r.Description, TechnologyId = r.TechnologyId, DisplayOrder = r.DisplayOrder, IsPublished = r.IsPublished, CreatedAt = now, UpdatedAt = now }; db.Skills.Add(x); await db.SaveChangesAsync(ct); return SkillMutation.Map(x); }
}
public sealed class UpdateSkillCommandHandler(IApplicationDbContext db, TimeProvider clock) : IRequestHandler<UpdateSkillCommand, SkillResult>
{
    public async Task<SkillResult> HandleAsync(UpdateSkillCommand r, CancellationToken ct = default)
    { var x = await db.Skills.SingleOrDefaultAsync(x => x.Id == r.Id, ct) ?? throw new NotFoundException("SKILL_NOT_FOUND", "The skill was not found."); await SkillMutation.ValidateReferencesAsync(db, r.Name, r.Category, r.TechnologyId, r.Id, ct); x.Name = r.Name.Trim(); x.Category = r.Category.Trim(); x.ExperienceLevel = r.ExperienceLevel.Trim().ToUpperInvariant(); x.Description = r.Description; x.TechnologyId = r.TechnologyId; x.DisplayOrder = r.DisplayOrder; x.IsPublished = r.IsPublished; x.UpdatedAt = clock.GetUtcNow(); await db.SaveChangesAsync(ct); return SkillMutation.Map(x); }
}
public sealed class DeleteSkillCommandHandler(IApplicationDbContext db) : IRequestHandler<DeleteSkillCommand, bool>
{ public async Task<bool> HandleAsync(DeleteSkillCommand r, CancellationToken ct = default) { var x = await db.Skills.SingleOrDefaultAsync(x => x.Id == r.Id, ct) ?? throw new NotFoundException("SKILL_NOT_FOUND", "The skill was not found."); db.Skills.Remove(x); await db.SaveChangesAsync(ct); return true; } }
public sealed class ReorderSkillsCommandHandler(IApplicationDbContext db) : IRequestHandler<ReorderSkillsCommand, bool>
{ public async Task<bool> HandleAsync(ReorderSkillsCommand r, CancellationToken ct = default) { var ids = r.Items.Select(x => x.Id).ToArray(); var rows = await db.Skills.Where(x => ids.Contains(x.Id)).ToListAsync(ct); if (rows.Count != ids.Length) throw new NotFoundException("SKILL_NOT_FOUND", "One or more skills were not found."); var map = r.Items.ToDictionary(x => x.Id, x => x.DisplayOrder); foreach (var x in rows) x.DisplayOrder = map[x.Id]; await db.SaveChangesAsync(ct); return true; } }

public sealed class CreateSkillCommandValidator : IRequestValidator<CreateSkillCommand>
{ public Task<IReadOnlyCollection<ValidationFailure>> ValidateAsync(CreateSkillCommand r, CancellationToken ct = default) => Task.FromResult<IReadOnlyCollection<ValidationFailure>>(SkillValidation.Validate(r.Name, r.Category, r.ExperienceLevel, r.DisplayOrder)); }
public sealed class UpdateSkillCommandValidator : IRequestValidator<UpdateSkillCommand>
{ public Task<IReadOnlyCollection<ValidationFailure>> ValidateAsync(UpdateSkillCommand r, CancellationToken ct = default) => Task.FromResult<IReadOnlyCollection<ValidationFailure>>(SkillValidation.Validate(r.Name, r.Category, r.ExperienceLevel, r.DisplayOrder)); }
public sealed class ReorderSkillsCommandValidator : IRequestValidator<ReorderSkillsCommand>
{ public Task<IReadOnlyCollection<ValidationFailure>> ValidateAsync(ReorderSkillsCommand r, CancellationToken ct = default) { var f = new List<ValidationFailure>(); ContentValidation.ReorderItems(f, r.Items); return Task.FromResult<IReadOnlyCollection<ValidationFailure>>(f); } }
internal static class SkillValidation
{ private static readonly string[] Levels = ["USED", "LEARNING", "EXPLORING"]; public static List<ValidationFailure> Validate(string name, string category, string level, int order) { var f = new List<ValidationFailure>(); ContentValidation.RequiredText(f, "name", name, 100); ContentValidation.RequiredText(f, "category", category, 50); ContentValidation.RequiredText(f, "experienceLevel", level, 30); if (!string.IsNullOrWhiteSpace(level) && !Levels.Contains(level.Trim().ToUpperInvariant())) f.Add(new("experienceLevel", "Experience level must be USED, LEARNING, or EXPLORING.")); ContentValidation.DisplayOrder(f, order); return f; } }
internal static class SkillMutation
{
    public static readonly System.Linq.Expressions.Expression<Func<Skill, SkillResult>> Project = x => new(x.Id, x.Name, x.Category, x.ExperienceLevel, x.Description, x.TechnologyId, x.DisplayOrder, x.IsPublished, x.UpdatedAt);
    public static SkillResult Map(Skill x) => new(x.Id, x.Name, x.Category, x.ExperienceLevel, x.Description, x.TechnologyId, x.DisplayOrder, x.IsPublished, x.UpdatedAt);
    public static async Task ValidateReferencesAsync(IApplicationDbContext db, string name, string category, Guid? technologyId, Guid? exceptId, CancellationToken ct)
    { if (technologyId.HasValue && !await db.Technologies.AnyAsync(x => x.Id == technologyId.Value, ct)) throw new NotFoundException("TECHNOLOGY_NOT_FOUND", "The selected technology was not found."); var n = name.Trim().ToLowerInvariant(); var c = category.Trim().ToLowerInvariant(); if (await db.Skills.AnyAsync(x => x.Id != exceptId && x.Name.ToLower() == n && x.Category.ToLower() == c, ct)) throw new ConflictException("SKILL_NAME_EXISTS", "A skill with this name and category already exists."); }
}
