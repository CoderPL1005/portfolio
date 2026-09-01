using Microsoft.EntityFrameworkCore;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Common.Abstractions.Persistence;
using Portfolio.Application.Common.Abstractions.Validation;
using Portfolio.Application.Common.Exceptions;
using Portfolio.Application.Features.Phase4C;
using Portfolio.Application.Features.PortfolioContent;
using Portfolio.Application.Features.PortfolioContent.GetPublicPortfolio;
using Portfolio.Domain.Entities;

namespace Portfolio.Application.Features.Journey;

public sealed record GetJourneyItemsQuery : IRequest<IReadOnlyCollection<JourneyResult>>;
public sealed record GetAdminJourneyTimelineQuery : IRequest<IReadOnlyCollection<AdminJourneyTimelineResult>>;
public sealed record AdminJourneyTimelineResult(
    Guid Id, string Title, string? Subtitle, string? Description, DateOnly? OccurredAt,
    string? IconKey, string SourceType, Guid SourceId, bool IsManual, DateOnly? StartAt,
    DateOnly? EndAt, bool IsOngoing, string TimelineKind);
public sealed record GetJourneyItemQuery(Guid Id) : IRequest<JourneyResult>;
public sealed record CreateJourneyItemCommand(string Title, string? Subtitle, string? Description,
    DateOnly? OccurredAt, string? IconKey, int DisplayOrder, bool IsPublished) : IRequest<JourneyResult>;
public sealed record UpdateJourneyItemCommand(Guid Id, string Title, string? Subtitle,
    string? Description, DateOnly? OccurredAt, string? IconKey, int DisplayOrder,
    bool IsPublished) : IRequest<JourneyResult>;
public sealed record DeleteJourneyItemCommand(Guid Id) : IRequest<bool>;
public sealed record ReorderJourneyItemsCommand(IReadOnlyCollection<ReorderItem> Items) : IRequest<bool>;

public sealed class GetJourneyItemsQueryHandler(IApplicationDbContext db) : IRequestHandler<GetJourneyItemsQuery, IReadOnlyCollection<JourneyResult>>
{ public async Task<IReadOnlyCollection<JourneyResult>> HandleAsync(GetJourneyItemsQuery r, CancellationToken ct = default) => await db.JourneyItems.AsNoTracking().OrderBy(x => x.DisplayOrder).ThenBy(x => x.Id).Select(JourneyMapping.Project).ToListAsync(ct); }
public sealed class GetAdminJourneyTimelineQueryHandler(IApplicationDbContext db) : IRequestHandler<GetAdminJourneyTimelineQuery, IReadOnlyCollection<AdminJourneyTimelineResult>>
{ public async Task<IReadOnlyCollection<AdminJourneyTimelineResult>> HandleAsync(GetAdminJourneyTimelineQuery r, CancellationToken ct = default) => (await PublicJourneyTimeline.LoadAsync(db, ct)).AdminItems; }
public sealed class GetJourneyItemQueryHandler(IApplicationDbContext db) : IRequestHandler<GetJourneyItemQuery, JourneyResult>
{ public async Task<JourneyResult> HandleAsync(GetJourneyItemQuery r, CancellationToken ct = default) => await db.JourneyItems.AsNoTracking().Where(x => x.Id == r.Id).Select(JourneyMapping.Project).SingleOrDefaultAsync(ct) ?? throw new NotFoundException("JOURNEY_ITEM_NOT_FOUND", "The journey item was not found."); }
public sealed class CreateJourneyItemCommandHandler(IApplicationDbContext db, TimeProvider clock) : IRequestHandler<CreateJourneyItemCommand, JourneyResult>
{ public async Task<JourneyResult> HandleAsync(CreateJourneyItemCommand r, CancellationToken ct = default) { var now = clock.GetUtcNow(); var x = new JourneyItem { Id = Guid.NewGuid(), CreatedAt = now, UpdatedAt = now }; JourneyMapping.Assign(x, r.Title, r.Subtitle, r.Description, r.OccurredAt, r.IconKey, r.DisplayOrder, r.IsPublished); db.JourneyItems.Add(x); await db.SaveChangesAsync(ct); return JourneyMapping.Map(x); } }
public sealed class UpdateJourneyItemCommandHandler(IApplicationDbContext db, TimeProvider clock) : IRequestHandler<UpdateJourneyItemCommand, JourneyResult>
{ public async Task<JourneyResult> HandleAsync(UpdateJourneyItemCommand r, CancellationToken ct = default) { var x = await db.JourneyItems.SingleOrDefaultAsync(x => x.Id == r.Id, ct) ?? throw new NotFoundException("JOURNEY_ITEM_NOT_FOUND", "The journey item was not found."); JourneyMapping.Assign(x, r.Title, r.Subtitle, r.Description, r.OccurredAt, r.IconKey, r.DisplayOrder, r.IsPublished); x.UpdatedAt = clock.GetUtcNow(); await db.SaveChangesAsync(ct); return JourneyMapping.Map(x); } }
public sealed class DeleteJourneyItemCommandHandler(IApplicationDbContext db) : IRequestHandler<DeleteJourneyItemCommand, bool>
{ public async Task<bool> HandleAsync(DeleteJourneyItemCommand r, CancellationToken ct = default) { var x = await db.JourneyItems.SingleOrDefaultAsync(x => x.Id == r.Id, ct) ?? throw new NotFoundException("JOURNEY_ITEM_NOT_FOUND", "The journey item was not found."); db.JourneyItems.Remove(x); await db.SaveChangesAsync(ct); return true; } }
public sealed class ReorderJourneyItemsCommandHandler(IApplicationDbContext db) : IRequestHandler<ReorderJourneyItemsCommand, bool>
{ public async Task<bool> HandleAsync(ReorderJourneyItemsCommand r, CancellationToken ct = default) { var ids = r.Items.Select(x => x.Id).ToArray(); var rows = await db.JourneyItems.Where(x => ids.Contains(x.Id)).ToListAsync(ct); if (rows.Count != ids.Length) throw new NotFoundException("JOURNEY_ITEM_NOT_FOUND", "One or more journey items were not found."); var map = r.Items.ToDictionary(x => x.Id, x => x.DisplayOrder); foreach (var x in rows) x.DisplayOrder = map[x.Id]; await db.SaveChangesAsync(ct); return true; } }
public sealed class CreateJourneyItemCommandValidator : IRequestValidator<CreateJourneyItemCommand>
{ public Task<IReadOnlyCollection<ValidationFailure>> ValidateAsync(CreateJourneyItemCommand r, CancellationToken ct = default) => Task.FromResult<IReadOnlyCollection<ValidationFailure>>(JourneyValidation.Validate(r.Title, r.Subtitle, r.IconKey, r.DisplayOrder)); }
public sealed class UpdateJourneyItemCommandValidator : IRequestValidator<UpdateJourneyItemCommand>
{ public Task<IReadOnlyCollection<ValidationFailure>> ValidateAsync(UpdateJourneyItemCommand r, CancellationToken ct = default) => Task.FromResult<IReadOnlyCollection<ValidationFailure>>(JourneyValidation.Validate(r.Title, r.Subtitle, r.IconKey, r.DisplayOrder)); }
public sealed class ReorderJourneyItemsCommandValidator : IRequestValidator<ReorderJourneyItemsCommand>
{ public Task<IReadOnlyCollection<ValidationFailure>> ValidateAsync(ReorderJourneyItemsCommand r, CancellationToken ct = default) { var f = new List<ValidationFailure>(); ContentValidation.ReorderItems(f, r.Items); return Task.FromResult<IReadOnlyCollection<ValidationFailure>>(f); } }
internal static class JourneyValidation
{ public static List<ValidationFailure> Validate(string title, string? subtitle, string? icon, int order) { var f = new List<ValidationFailure>(); ContentValidation.RequiredText(f, "title", title, 255); ContentValidation.OptionalText(f, "subtitle", subtitle, 255); ContentValidation.OptionalText(f, "iconKey", icon, 100); ContentValidation.DisplayOrder(f, order); return f; } }
internal static class JourneyMapping
{ public static readonly System.Linq.Expressions.Expression<Func<JourneyItem, JourneyResult>> Project = x => new(x.Id, x.Title, x.Subtitle, x.Description, x.OccurredAt, x.IconKey, x.DisplayOrder, x.IsPublished, x.UpdatedAt); public static JourneyResult Map(JourneyItem x) => new(x.Id, x.Title, x.Subtitle, x.Description, x.OccurredAt, x.IconKey, x.DisplayOrder, x.IsPublished, x.UpdatedAt); public static void Assign(JourneyItem x, string title, string? subtitle, string? description, DateOnly? occurredAt, string? icon, int order, bool published) { x.Title = title.Trim(); x.Subtitle = subtitle?.Trim(); x.Description = description; x.OccurredAt = occurredAt; x.IconKey = icon?.Trim(); x.DisplayOrder = order; x.IsPublished = published; } }
