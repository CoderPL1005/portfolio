using Microsoft.EntityFrameworkCore;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Common.Abstractions.Persistence;
using Portfolio.Application.Common.Abstractions.Validation;
using Portfolio.Application.Common.Exceptions;
using Portfolio.Application.Features.PortfolioContent;

namespace Portfolio.Application.Features.Trainings;

public sealed record GetTrainingsQuery : IRequest<IReadOnlyCollection<TrainingResult>>;
public sealed record GetTrainingQuery(Guid Id) : IRequest<TrainingResult>;
public sealed record CreateTrainingCommand(string Title, string? Provider, string? Description,
    DateOnly? StartDate, DateOnly? EndDate, string? CredentialUrl, int DisplayOrder,
    bool IsPublished) : IRequest<TrainingResult>;
public sealed record UpdateTrainingCommand(Guid Id, string Title, string? Provider, string? Description,
    DateOnly? StartDate, DateOnly? EndDate, string? CredentialUrl, int DisplayOrder,
    bool IsPublished) : IRequest<TrainingResult>;
public sealed record DeleteTrainingCommand(Guid Id) : IRequest<bool>;
public sealed record ReorderTrainingsCommand(IReadOnlyCollection<ReorderItem> Items) : IRequest<bool>;

public sealed class GetTrainingsQueryHandler(IApplicationDbContext dbContext) : IRequestHandler<GetTrainingsQuery, IReadOnlyCollection<TrainingResult>>
{
    public async Task<IReadOnlyCollection<TrainingResult>> HandleAsync(GetTrainingsQuery request, CancellationToken cancellationToken = default) =>
        await TrainingProjection.Project(dbContext.Trainings.AsNoTracking()).OrderBy(item => item.DisplayOrder).ThenBy(item => item.Id).ToListAsync(cancellationToken);
}
public sealed class GetTrainingQueryHandler(IApplicationDbContext dbContext) : IRequestHandler<GetTrainingQuery, TrainingResult>
{
    public async Task<TrainingResult> HandleAsync(GetTrainingQuery request, CancellationToken cancellationToken = default) =>
        await TrainingProjection.Project(dbContext.Trainings.AsNoTracking().Where(item => item.Id == request.Id)).SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("TRAINING_NOT_FOUND", "The training record was not found.");
}
public sealed class CreateTrainingCommandHandler(IApplicationDbContext dbContext, TimeProvider timeProvider) : IRequestHandler<CreateTrainingCommand, TrainingResult>
{
    public async Task<TrainingResult> HandleAsync(CreateTrainingCommand request, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow(); var entity = new Portfolio.Domain.Entities.Training
        { Id = Guid.NewGuid(), Title = request.Title.Trim(), Provider = request.Provider?.Trim(), Description = request.Description, StartDate = request.StartDate, EndDate = request.EndDate, CredentialUrl = request.CredentialUrl?.Trim(), DisplayOrder = request.DisplayOrder, IsPublished = request.IsPublished, CreatedAt = now, UpdatedAt = now };
        dbContext.Trainings.Add(entity); await dbContext.SaveChangesAsync(cancellationToken); return TrainingProjection.Map(entity);
    }
}
public sealed class UpdateTrainingCommandHandler(IApplicationDbContext dbContext, TimeProvider timeProvider) : IRequestHandler<UpdateTrainingCommand, TrainingResult>
{
    public async Task<TrainingResult> HandleAsync(UpdateTrainingCommand request, CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.Trainings.SingleOrDefaultAsync(item => item.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("TRAINING_NOT_FOUND", "The training record was not found.");
        entity.Title = request.Title.Trim(); entity.Provider = request.Provider?.Trim(); entity.Description = request.Description;
        entity.StartDate = request.StartDate; entity.EndDate = request.EndDate; entity.CredentialUrl = request.CredentialUrl?.Trim();
        entity.DisplayOrder = request.DisplayOrder; entity.IsPublished = request.IsPublished; entity.UpdatedAt = timeProvider.GetUtcNow();
        await dbContext.SaveChangesAsync(cancellationToken); return TrainingProjection.Map(entity);
    }
}
public sealed class DeleteTrainingCommandHandler(IApplicationDbContext dbContext) : IRequestHandler<DeleteTrainingCommand, bool>
{
    public async Task<bool> HandleAsync(DeleteTrainingCommand request, CancellationToken cancellationToken = default)
    { var entity = await dbContext.Trainings.SingleOrDefaultAsync(item => item.Id == request.Id, cancellationToken) ?? throw new NotFoundException("TRAINING_NOT_FOUND", "The training record was not found."); dbContext.Trainings.Remove(entity); await dbContext.SaveChangesAsync(cancellationToken); return true; }
}
public sealed class ReorderTrainingsCommandHandler(IApplicationDbContext dbContext) : IRequestHandler<ReorderTrainingsCommand, bool>
{
    public async Task<bool> HandleAsync(ReorderTrainingsCommand request, CancellationToken cancellationToken = default)
    { var ids = request.Items.Select(item => item.Id).ToArray(); var entities = await dbContext.Trainings.Where(item => ids.Contains(item.Id)).ToListAsync(cancellationToken); if (entities.Count != ids.Length) throw new NotFoundException("TRAINING_NOT_FOUND", "One or more training records were not found."); var order = request.Items.ToDictionary(item => item.Id, item => item.DisplayOrder); foreach (var entity in entities) entity.DisplayOrder = order[entity.Id]; await dbContext.SaveChangesAsync(cancellationToken); return true; }
}

public sealed class CreateTrainingCommandValidator : IRequestValidator<CreateTrainingCommand>
{ public Task<IReadOnlyCollection<ValidationFailure>> ValidateAsync(CreateTrainingCommand request, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyCollection<ValidationFailure>>(TrainingValidation.Validate(request.Title, request.Provider, request.StartDate, request.EndDate, request.CredentialUrl, request.DisplayOrder)); }
public sealed class UpdateTrainingCommandValidator : IRequestValidator<UpdateTrainingCommand>
{ public Task<IReadOnlyCollection<ValidationFailure>> ValidateAsync(UpdateTrainingCommand request, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyCollection<ValidationFailure>>(TrainingValidation.Validate(request.Title, request.Provider, request.StartDate, request.EndDate, request.CredentialUrl, request.DisplayOrder)); }
public sealed class ReorderTrainingsCommandValidator : IRequestValidator<ReorderTrainingsCommand>
{ public Task<IReadOnlyCollection<ValidationFailure>> ValidateAsync(ReorderTrainingsCommand request, CancellationToken cancellationToken = default) { var failures = new List<ValidationFailure>(); ContentValidation.ReorderItems(failures, request.Items); return Task.FromResult<IReadOnlyCollection<ValidationFailure>>(failures); } }

internal static class TrainingValidation
{
    public static List<ValidationFailure> Validate(string title, string? provider, DateOnly? start, DateOnly? end, string? url, int displayOrder)
    { var failures = new List<ValidationFailure>(); ContentValidation.RequiredText(failures, "title", title, 255); ContentValidation.OptionalText(failures, "provider", provider, 255); ContentValidation.DateRange(failures, "endDate", start, end); ContentValidation.HttpUrl(failures, "credentialUrl", url); ContentValidation.DisplayOrder(failures, displayOrder); return failures; }
}
internal static class TrainingProjection
{
    public static IQueryable<TrainingResult> Project(IQueryable<Portfolio.Domain.Entities.Training> query) => query.Select(item => new TrainingResult(item.Id, item.Title, item.Provider, item.Description, item.StartDate, item.EndDate, item.CredentialUrl, item.DisplayOrder, item.IsPublished));
    public static TrainingResult Map(Portfolio.Domain.Entities.Training item) => new(item.Id, item.Title, item.Provider, item.Description, item.StartDate, item.EndDate, item.CredentialUrl, item.DisplayOrder, item.IsPublished);
}
