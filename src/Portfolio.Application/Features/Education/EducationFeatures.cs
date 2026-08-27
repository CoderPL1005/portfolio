using Microsoft.EntityFrameworkCore;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Common.Abstractions.Persistence;
using Portfolio.Application.Common.Abstractions.Validation;
using Portfolio.Application.Common.Exceptions;
using Portfolio.Application.Features.PortfolioContent;
using Portfolio.Domain.Entities;

namespace Portfolio.Application.Features.Education;

public sealed record GetEducationsQuery : IRequest<IReadOnlyCollection<EducationResult>>;
public sealed record GetEducationQuery(Guid Id) : IRequest<EducationResult>;
public sealed record CreateEducationCommand(
    string Institution, string? Degree, string? FieldOfStudy, DateOnly? StartDate,
    DateOnly? EndDate, string? Description, string? Location, int DisplayOrder,
    bool IsPublished) : IRequest<EducationResult>;
public sealed record UpdateEducationCommand(
    Guid Id, string Institution, string? Degree, string? FieldOfStudy, DateOnly? StartDate,
    DateOnly? EndDate, string? Description, string? Location, int DisplayOrder,
    bool IsPublished) : IRequest<EducationResult>;
public sealed record DeleteEducationCommand(Guid Id) : IRequest<bool>;
public sealed record ReorderEducationsCommand(IReadOnlyCollection<ReorderItem> Items) : IRequest<bool>;

public sealed class GetEducationsQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetEducationsQuery, IReadOnlyCollection<EducationResult>>
{
    public async Task<IReadOnlyCollection<EducationResult>> HandleAsync(GetEducationsQuery request, CancellationToken cancellationToken = default) =>
        await EducationProjection.Project(dbContext.Educations.AsNoTracking())
            .OrderBy(item => item.DisplayOrder).ThenBy(item => item.Id).ToListAsync(cancellationToken);
}

public sealed class GetEducationQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetEducationQuery, EducationResult>
{
    public async Task<EducationResult> HandleAsync(GetEducationQuery request, CancellationToken cancellationToken = default) =>
        await EducationProjection.Project(dbContext.Educations.AsNoTracking().Where(item => item.Id == request.Id))
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("EDUCATION_NOT_FOUND", "The education record was not found.");
}

public sealed class CreateEducationCommandHandler(IApplicationDbContext dbContext, TimeProvider timeProvider)
    : IRequestHandler<CreateEducationCommand, EducationResult>
{
    public async Task<EducationResult> HandleAsync(CreateEducationCommand request, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var entity = new Portfolio.Domain.Entities.Education
        {
            Id = Guid.NewGuid(), Institution = request.Institution.Trim(), Degree = request.Degree?.Trim(),
            FieldOfStudy = request.FieldOfStudy?.Trim(), StartDate = request.StartDate, EndDate = request.EndDate,
            Description = request.Description, Location = request.Location?.Trim(),
            DisplayOrder = request.DisplayOrder, IsPublished = request.IsPublished,
            CreatedAt = now, UpdatedAt = now
        };
        dbContext.Educations.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return EducationProjection.Map(entity);
    }
}

public sealed class UpdateEducationCommandHandler(IApplicationDbContext dbContext, TimeProvider timeProvider)
    : IRequestHandler<UpdateEducationCommand, EducationResult>
{
    public async Task<EducationResult> HandleAsync(UpdateEducationCommand request, CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.Educations.SingleOrDefaultAsync(item => item.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("EDUCATION_NOT_FOUND", "The education record was not found.");
        entity.Institution = request.Institution.Trim(); entity.Degree = request.Degree?.Trim();
        entity.FieldOfStudy = request.FieldOfStudy?.Trim(); entity.StartDate = request.StartDate;
        entity.EndDate = request.EndDate; entity.Description = request.Description;
        entity.Location = request.Location?.Trim(); entity.DisplayOrder = request.DisplayOrder;
        entity.IsPublished = request.IsPublished; entity.UpdatedAt = timeProvider.GetUtcNow();
        await dbContext.SaveChangesAsync(cancellationToken);
        return EducationProjection.Map(entity);
    }
}

public sealed class DeleteEducationCommandHandler(IApplicationDbContext dbContext)
    : IRequestHandler<DeleteEducationCommand, bool>
{
    public async Task<bool> HandleAsync(DeleteEducationCommand request, CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.Educations.SingleOrDefaultAsync(item => item.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("EDUCATION_NOT_FOUND", "The education record was not found.");
        dbContext.Educations.Remove(entity); await dbContext.SaveChangesAsync(cancellationToken); return true;
    }
}

public sealed class ReorderEducationsCommandHandler(IApplicationDbContext dbContext)
    : IRequestHandler<ReorderEducationsCommand, bool>
{
    public async Task<bool> HandleAsync(ReorderEducationsCommand request, CancellationToken cancellationToken = default)
    {
        var ids = request.Items.Select(item => item.Id).ToArray();
        var entities = await dbContext.Educations.Where(item => ids.Contains(item.Id)).ToListAsync(cancellationToken);
        if (entities.Count != ids.Length) throw new NotFoundException("EDUCATION_NOT_FOUND", "One or more education records were not found.");
        var order = request.Items.ToDictionary(item => item.Id, item => item.DisplayOrder);
        foreach (var entity in entities) entity.DisplayOrder = order[entity.Id];
        await dbContext.SaveChangesAsync(cancellationToken); return true;
    }
}

public sealed class CreateEducationCommandValidator : IRequestValidator<CreateEducationCommand>
{
    public Task<IReadOnlyCollection<ValidationFailure>> ValidateAsync(CreateEducationCommand request, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyCollection<ValidationFailure>>(EducationValidation.Validate(request.Institution, request.Degree, request.FieldOfStudy, request.StartDate, request.EndDate, request.Location, request.DisplayOrder));
}
public sealed class UpdateEducationCommandValidator : IRequestValidator<UpdateEducationCommand>
{
    public Task<IReadOnlyCollection<ValidationFailure>> ValidateAsync(UpdateEducationCommand request, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyCollection<ValidationFailure>>(EducationValidation.Validate(request.Institution, request.Degree, request.FieldOfStudy, request.StartDate, request.EndDate, request.Location, request.DisplayOrder));
}
public sealed class ReorderEducationsCommandValidator : IRequestValidator<ReorderEducationsCommand>
{
    public Task<IReadOnlyCollection<ValidationFailure>> ValidateAsync(ReorderEducationsCommand request, CancellationToken cancellationToken = default)
    { var failures = new List<ValidationFailure>(); ContentValidation.ReorderItems(failures, request.Items); return Task.FromResult<IReadOnlyCollection<ValidationFailure>>(failures); }
}

internal static class EducationValidation
{
    public static List<ValidationFailure> Validate(string institution, string? degree, string? field, DateOnly? start, DateOnly? end, string? location, int displayOrder)
    {
        var failures = new List<ValidationFailure>(); ContentValidation.RequiredText(failures, "institution", institution, 255);
        ContentValidation.OptionalText(failures, "degree", degree, 255); ContentValidation.OptionalText(failures, "fieldOfStudy", field, 255);
        ContentValidation.OptionalText(failures, "location", location, 255); ContentValidation.DateRange(failures, "endDate", start, end);
        ContentValidation.DisplayOrder(failures, displayOrder); return failures;
    }
}

internal static class EducationProjection
{
    public static IQueryable<EducationResult> Project(IQueryable<Portfolio.Domain.Entities.Education> query) =>
        query.Select(item => new EducationResult(item.Id, item.Institution, item.Degree,
            item.FieldOfStudy, item.StartDate, item.EndDate, item.Description, item.Location,
            item.DisplayOrder, item.IsPublished));
    public static EducationResult Map(Portfolio.Domain.Entities.Education item) => new(item.Id, item.Institution, item.Degree, item.FieldOfStudy, item.StartDate, item.EndDate, item.Description, item.Location, item.DisplayOrder, item.IsPublished);
}
