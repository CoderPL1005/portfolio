using Microsoft.EntityFrameworkCore;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Common.Abstractions.Persistence;
using Portfolio.Application.Common.Exceptions;
using Portfolio.Application.Features.PortfolioContent;
using Portfolio.Domain.Entities;

namespace Portfolio.Application.Features.Experiences;

public sealed record CreateExperienceCommand(
    string CompanyName, string RoleTitle, string? Location, DateOnly StartDate,
    DateOnly? EndDate, bool IsCurrent, string? Summary, string? ResponsibilitiesMarkdown,
    string? CompanyUrl, int DisplayOrder, bool IsPublished,
    IReadOnlyCollection<Guid> TechnologyIds) : IRequest<AdminExperienceResult>;

public sealed record UpdateExperienceCommand(
    Guid Id, string CompanyName, string RoleTitle, string? Location, DateOnly StartDate,
    DateOnly? EndDate, bool IsCurrent, string? Summary, string? ResponsibilitiesMarkdown,
    string? CompanyUrl, int DisplayOrder, bool IsPublished,
    IReadOnlyCollection<Guid> TechnologyIds) : IRequest<AdminExperienceResult>;

public sealed record DeleteExperienceCommand(Guid Id) : IRequest<bool>;
public sealed record ReorderExperiencesCommand(IReadOnlyCollection<ReorderItem> Items) : IRequest<bool>;

public sealed class CreateExperienceCommandHandler(
    IApplicationDbContext dbContext,
    TimeProvider timeProvider) : IRequestHandler<CreateExperienceCommand, AdminExperienceResult>
{
    public async Task<AdminExperienceResult> HandleAsync(
        CreateExperienceCommand request,
        CancellationToken cancellationToken = default)
    {
        await ExperienceMutation.ValidateTechnologiesAsync(dbContext, request.TechnologyIds, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var entity = new Experience
        {
            Id = Guid.NewGuid(), CompanyName = request.CompanyName.Trim(), RoleTitle = request.RoleTitle.Trim(),
            Location = request.Location?.Trim(), StartDate = request.StartDate, EndDate = request.EndDate,
            IsCurrent = request.IsCurrent, Summary = request.Summary,
            ResponsibilitiesMarkdown = request.ResponsibilitiesMarkdown,
            CompanyUrl = request.CompanyUrl?.Trim(), DisplayOrder = request.DisplayOrder,
            IsPublished = request.IsPublished, CreatedAt = now, UpdatedAt = now
        };
        dbContext.Experiences.Add(entity);
        dbContext.ExperienceTechnologies.AddRange(request.TechnologyIds.Select((id, index) =>
            new ExperienceTechnology { ExperienceId = entity.Id, TechnologyId = id, DisplayOrder = index }));
        await dbContext.SaveChangesAsync(cancellationToken);
        return await ExperienceMutation.GetResultAsync(dbContext, entity.Id, cancellationToken);
    }
}

public sealed class UpdateExperienceCommandHandler(
    IApplicationDbContext dbContext,
    TimeProvider timeProvider) : IRequestHandler<UpdateExperienceCommand, AdminExperienceResult>
{
    public async Task<AdminExperienceResult> HandleAsync(
        UpdateExperienceCommand request,
        CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.Experiences.SingleOrDefaultAsync(
            item => item.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("EXPERIENCE_NOT_FOUND", "The experience was not found.");
        await ExperienceMutation.ValidateTechnologiesAsync(dbContext, request.TechnologyIds, cancellationToken);
        entity.CompanyName = request.CompanyName.Trim();
        entity.RoleTitle = request.RoleTitle.Trim();
        entity.Location = request.Location?.Trim();
        entity.StartDate = request.StartDate;
        entity.EndDate = request.EndDate;
        entity.IsCurrent = request.IsCurrent;
        entity.Summary = request.Summary;
        entity.ResponsibilitiesMarkdown = request.ResponsibilitiesMarkdown;
        entity.CompanyUrl = request.CompanyUrl?.Trim();
        entity.DisplayOrder = request.DisplayOrder;
        entity.IsPublished = request.IsPublished;
        entity.UpdatedAt = timeProvider.GetUtcNow();
        var links = await dbContext.ExperienceTechnologies
            .Where(link => link.ExperienceId == entity.Id).ToListAsync(cancellationToken);
        dbContext.ExperienceTechnologies.RemoveRange(links);
        dbContext.ExperienceTechnologies.AddRange(request.TechnologyIds.Select((id, index) =>
            new ExperienceTechnology { ExperienceId = entity.Id, TechnologyId = id, DisplayOrder = index }));
        await dbContext.SaveChangesAsync(cancellationToken);
        return await ExperienceMutation.GetResultAsync(dbContext, entity.Id, cancellationToken);
    }
}

public sealed class DeleteExperienceCommandHandler(IApplicationDbContext dbContext)
    : IRequestHandler<DeleteExperienceCommand, bool>
{
    public async Task<bool> HandleAsync(DeleteExperienceCommand request, CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.Experiences.SingleOrDefaultAsync(
            item => item.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("EXPERIENCE_NOT_FOUND", "The experience was not found.");
        dbContext.Experiences.Remove(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}

public sealed class ReorderExperiencesCommandHandler(IApplicationDbContext dbContext)
    : IRequestHandler<ReorderExperiencesCommand, bool>
{
    public async Task<bool> HandleAsync(ReorderExperiencesCommand request, CancellationToken cancellationToken = default)
    {
        var ids = request.Items.Select(item => item.Id).ToArray();
        var entities = await dbContext.Experiences.Where(item => ids.Contains(item.Id)).ToListAsync(cancellationToken);
        if (entities.Count != ids.Length)
        {
            throw new NotFoundException("EXPERIENCE_NOT_FOUND", "One or more experiences were not found.");
        }

        var order = request.Items.ToDictionary(item => item.Id, item => item.DisplayOrder);
        foreach (var entity in entities) entity.DisplayOrder = order[entity.Id];
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}

internal static class ExperienceMutation
{
    public static async Task ValidateTechnologiesAsync(
        IApplicationDbContext dbContext,
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken)
    {
        var distinct = ids.Distinct().ToArray();
        if (distinct.Length != ids.Count ||
            await dbContext.Technologies.CountAsync(item => distinct.Contains(item.Id), cancellationToken) != distinct.Length)
        {
            throw new NotFoundException("TECHNOLOGY_NOT_FOUND", "One or more technologies were not found.");
        }
    }

    public static async Task<AdminExperienceResult> GetResultAsync(
        IApplicationDbContext dbContext, Guid id, CancellationToken cancellationToken) =>
        await ExperienceProjection.Admin(
            dbContext.Experiences.AsNoTracking().Where(item => item.Id == id), dbContext)
            .SingleAsync(cancellationToken);
}
