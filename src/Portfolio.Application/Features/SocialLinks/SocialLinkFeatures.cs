using Microsoft.EntityFrameworkCore;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Common.Abstractions.Persistence;
using Portfolio.Application.Common.Abstractions.Validation;
using Portfolio.Application.Common.Exceptions;
using Portfolio.Application.Features.Phase4C;
using Portfolio.Application.Features.PortfolioContent;
using Portfolio.Domain.Entities;

namespace Portfolio.Application.Features.SocialLinks;

public sealed record GetSocialLinksQuery : IRequest<IReadOnlyCollection<SocialLinkResult>>;
public sealed record CreateSocialLinkCommand(string Platform, string? Label, string Url,
    string? IconKey, int DisplayOrder, bool IsVisible) : IRequest<SocialLinkResult>;
public sealed record UpdateSocialLinkCommand(Guid Id, string Platform, string? Label, string Url,
    string? IconKey, int DisplayOrder, bool IsVisible) : IRequest<SocialLinkResult>;
public sealed record DeleteSocialLinkCommand(Guid Id) : IRequest<bool>;
public sealed record ReorderSocialLinksCommand(IReadOnlyCollection<ReorderItem> Items) : IRequest<bool>;

public sealed class GetSocialLinksQueryHandler(IApplicationDbContext db) : IRequestHandler<GetSocialLinksQuery, IReadOnlyCollection<SocialLinkResult>>
{ public async Task<IReadOnlyCollection<SocialLinkResult>> HandleAsync(GetSocialLinksQuery r, CancellationToken ct = default) => await db.SocialLinks.AsNoTracking().OrderBy(x => x.DisplayOrder).ThenBy(x => x.Id).Select(SocialLinkMapping.Project).ToListAsync(ct); }
public sealed class CreateSocialLinkCommandHandler(IApplicationDbContext db, TimeProvider clock) : IRequestHandler<CreateSocialLinkCommand, SocialLinkResult>
{ public async Task<SocialLinkResult> HandleAsync(CreateSocialLinkCommand r, CancellationToken ct = default) { await SocialLinkMapping.Unique(db, r.Platform, null, ct); var now = clock.GetUtcNow(); var x = new SocialLink { Id = Guid.NewGuid(), CreatedAt = now, UpdatedAt = now }; SocialLinkMapping.Assign(x, r.Platform, r.Label, r.Url, r.IconKey, r.DisplayOrder, r.IsVisible); db.SocialLinks.Add(x); await db.SaveChangesAsync(ct); return SocialLinkMapping.Map(x); } }
public sealed class UpdateSocialLinkCommandHandler(IApplicationDbContext db, TimeProvider clock) : IRequestHandler<UpdateSocialLinkCommand, SocialLinkResult>
{ public async Task<SocialLinkResult> HandleAsync(UpdateSocialLinkCommand r, CancellationToken ct = default) { var x = await db.SocialLinks.SingleOrDefaultAsync(x => x.Id == r.Id, ct) ?? throw new NotFoundException("SOCIAL_LINK_NOT_FOUND", "The social link was not found."); await SocialLinkMapping.Unique(db, r.Platform, r.Id, ct); SocialLinkMapping.Assign(x, r.Platform, r.Label, r.Url, r.IconKey, r.DisplayOrder, r.IsVisible); x.UpdatedAt = clock.GetUtcNow(); await db.SaveChangesAsync(ct); return SocialLinkMapping.Map(x); } }
public sealed class DeleteSocialLinkCommandHandler(IApplicationDbContext db) : IRequestHandler<DeleteSocialLinkCommand, bool>
{ public async Task<bool> HandleAsync(DeleteSocialLinkCommand r, CancellationToken ct = default) { var x = await db.SocialLinks.SingleOrDefaultAsync(x => x.Id == r.Id, ct) ?? throw new NotFoundException("SOCIAL_LINK_NOT_FOUND", "The social link was not found."); db.SocialLinks.Remove(x); await db.SaveChangesAsync(ct); return true; } }
public sealed class ReorderSocialLinksCommandHandler(IApplicationDbContext db) : IRequestHandler<ReorderSocialLinksCommand, bool>
{ public async Task<bool> HandleAsync(ReorderSocialLinksCommand r, CancellationToken ct = default) { var ids = r.Items.Select(x => x.Id).ToArray(); var rows = await db.SocialLinks.Where(x => ids.Contains(x.Id)).ToListAsync(ct); if (rows.Count != ids.Length) throw new NotFoundException("SOCIAL_LINK_NOT_FOUND", "One or more social links were not found."); var map = r.Items.ToDictionary(x => x.Id, x => x.DisplayOrder); foreach (var x in rows) x.DisplayOrder = map[x.Id]; await db.SaveChangesAsync(ct); return true; } }
public sealed class CreateSocialLinkCommandValidator : IRequestValidator<CreateSocialLinkCommand>
{ public Task<IReadOnlyCollection<ValidationFailure>> ValidateAsync(CreateSocialLinkCommand r, CancellationToken ct = default) => Task.FromResult<IReadOnlyCollection<ValidationFailure>>(SocialLinkValidation.Validate(r.Platform, r.Label, r.Url, r.IconKey, r.DisplayOrder)); }
public sealed class UpdateSocialLinkCommandValidator : IRequestValidator<UpdateSocialLinkCommand>
{ public Task<IReadOnlyCollection<ValidationFailure>> ValidateAsync(UpdateSocialLinkCommand r, CancellationToken ct = default) => Task.FromResult<IReadOnlyCollection<ValidationFailure>>(SocialLinkValidation.Validate(r.Platform, r.Label, r.Url, r.IconKey, r.DisplayOrder)); }
public sealed class ReorderSocialLinksCommandValidator : IRequestValidator<ReorderSocialLinksCommand>
{ public Task<IReadOnlyCollection<ValidationFailure>> ValidateAsync(ReorderSocialLinksCommand r, CancellationToken ct = default) { var f = new List<ValidationFailure>(); ContentValidation.ReorderItems(f, r.Items); return Task.FromResult<IReadOnlyCollection<ValidationFailure>>(f); } }
internal static class SocialLinkValidation
{ public static List<ValidationFailure> Validate(string platform, string? label, string url, string? icon, int order) { var f = new List<ValidationFailure>(); ContentValidation.RequiredText(f, "platform", platform, 100); ContentValidation.OptionalText(f, "label", label, 100); ContentValidation.RequiredText(f, "url", url, 2048); if (!string.IsNullOrWhiteSpace(url) && (!Uri.TryCreate(url, UriKind.Absolute, out var parsed) || parsed.Scheme is not ("http" or "https" or "mailto"))) f.Add(new("url", "URL must use HTTP, HTTPS, or mailto.")); ContentValidation.OptionalText(f, "iconKey", icon, 100); ContentValidation.DisplayOrder(f, order); return f; } }
internal static class SocialLinkMapping
{ public static readonly System.Linq.Expressions.Expression<Func<SocialLink, SocialLinkResult>> Project = x => new(x.Id, x.Platform, x.Label, x.Url, x.IconKey, x.DisplayOrder, x.IsVisible, x.UpdatedAt); public static SocialLinkResult Map(SocialLink x) => new(x.Id, x.Platform, x.Label, x.Url, x.IconKey, x.DisplayOrder, x.IsVisible, x.UpdatedAt); public static void Assign(SocialLink x, string platform, string? label, string url, string? icon, int order, bool visible) { x.Platform = platform.Trim(); x.Label = label?.Trim(); x.Url = url.Trim(); x.IconKey = icon?.Trim(); x.DisplayOrder = order; x.IsVisible = visible; } public static async Task Unique(IApplicationDbContext db, string platform, Guid? exceptId, CancellationToken ct) { var value = platform.Trim().ToLowerInvariant(); if (await db.SocialLinks.AnyAsync(x => x.Id != exceptId && x.Platform.ToLower() == value, ct)) throw new ConflictException("SOCIAL_LINK_PLATFORM_EXISTS", "A social link for this platform already exists."); } }
