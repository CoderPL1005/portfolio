using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Common.Abstractions.Persistence;
using Portfolio.Application.Common.Abstractions.Validation;
using Portfolio.Application.Common.Exceptions;
using Portfolio.Application.Features.Phase4B;
using Portfolio.Application.Features.Phase4C;
using Portfolio.Application.Features.PortfolioContent;
using Portfolio.Domain.Entities;

namespace Portfolio.Application.Features.ContactMessages;

public sealed record SubmitContactMessageCommand(string Name, string Email, string? Subject,
    string Message) : IRequest<ContactSubmissionResult>;
public sealed record GetContactMessagesQuery(int Page, int PageSize, string? Status)
    : IRequest<PagedResult<ContactMessageResult>>;
public sealed record GetContactMessageQuery(Guid Id) : IRequest<ContactMessageResult>;
public sealed record UpdateContactMessageStatusCommand(Guid Id, string Status)
    : IRequest<ContactMessageResult>;

public sealed class SubmitContactMessageCommandHandler(IApplicationDbContext db, TimeProvider clock) : IRequestHandler<SubmitContactMessageCommand, ContactSubmissionResult>
{ public async Task<ContactSubmissionResult> HandleAsync(SubmitContactMessageCommand r, CancellationToken ct = default) { var x = new ContactMessage { Id = Guid.NewGuid(), Name = r.Name.Trim(), Email = r.Email.Trim(), Subject = r.Subject?.Trim(), Message = r.Message, Status = "NEW", ReceivedAt = clock.GetUtcNow() }; db.ContactMessages.Add(x); await db.SaveChangesAsync(ct); return new(x.Id, x.Status); } }
public sealed class GetContactMessagesQueryHandler(IApplicationDbContext db) : IRequestHandler<GetContactMessagesQuery, PagedResult<ContactMessageResult>>
{ public async Task<PagedResult<ContactMessageResult>> HandleAsync(GetContactMessagesQuery r, CancellationToken ct = default) { var q = db.ContactMessages.AsNoTracking(); if (!string.IsNullOrWhiteSpace(r.Status)) { var value = r.Status.Trim().ToUpperInvariant(); q = q.Where(x => x.Status == value); } var total = await q.CountAsync(ct); var items = await q.OrderByDescending(x => x.ReceivedAt).ThenByDescending(x => x.Id).Skip((r.Page - 1) * r.PageSize).Take(r.PageSize).Select(ContactMapping.Project).ToListAsync(ct); return new(items, r.Page, r.PageSize, total, (int)Math.Ceiling(total / (double)r.PageSize)); } }
public sealed class GetContactMessageQueryHandler(IApplicationDbContext db) : IRequestHandler<GetContactMessageQuery, ContactMessageResult>
{ public async Task<ContactMessageResult> HandleAsync(GetContactMessageQuery r, CancellationToken ct = default) => await db.ContactMessages.AsNoTracking().Where(x => x.Id == r.Id).Select(ContactMapping.Project).SingleOrDefaultAsync(ct) ?? throw new NotFoundException("CONTACT_MESSAGE_NOT_FOUND", "The contact message was not found."); }
public sealed class UpdateContactMessageStatusCommandHandler(IApplicationDbContext db, TimeProvider clock) : IRequestHandler<UpdateContactMessageStatusCommand, ContactMessageResult>
{ public async Task<ContactMessageResult> HandleAsync(UpdateContactMessageStatusCommand r, CancellationToken ct = default) { var x = await db.ContactMessages.SingleOrDefaultAsync(x => x.Id == r.Id, ct) ?? throw new NotFoundException("CONTACT_MESSAGE_NOT_FOUND", "The contact message was not found."); var status = r.Status.Trim().ToUpperInvariant(); var now = clock.GetUtcNow(); x.Status = status; if (status == "READ" && !x.ReadAt.HasValue) x.ReadAt = now; if (status == "REPLIED") { if (!x.ReadAt.HasValue) x.ReadAt = now; if (!x.RepliedAt.HasValue) x.RepliedAt = now; } await db.SaveChangesAsync(ct); return ContactMapping.Map(x); } }

public sealed class SubmitContactMessageCommandValidator : IRequestValidator<SubmitContactMessageCommand>
{ public Task<IReadOnlyCollection<ValidationFailure>> ValidateAsync(SubmitContactMessageCommand r, CancellationToken ct = default) { var f = new List<ValidationFailure>(); ContentValidation.RequiredText(f, "name", r.Name, 255); ContentValidation.RequiredText(f, "email", r.Email, 255); if (!string.IsNullOrWhiteSpace(r.Email) && !MailAddress.TryCreate(r.Email, out _)) f.Add(new("email", "Email must be valid.")); ContentValidation.OptionalText(f, "subject", r.Subject, 255); ContentValidation.RequiredText(f, "message", r.Message, 5000); return Task.FromResult<IReadOnlyCollection<ValidationFailure>>(f); } }
public sealed class GetContactMessagesQueryValidator : IRequestValidator<GetContactMessagesQuery>
{ public Task<IReadOnlyCollection<ValidationFailure>> ValidateAsync(GetContactMessagesQuery r, CancellationToken ct = default) { var f = new List<ValidationFailure>(); if (r.Page < 1) f.Add(new("page", "Page must be at least 1.")); if (r.PageSize is < 1 or > 100) f.Add(new("pageSize", "Page size must be between 1 and 100.")); ContactValidation.Status(f, r.Status, true); return Task.FromResult<IReadOnlyCollection<ValidationFailure>>(f); } }
public sealed class UpdateContactMessageStatusCommandValidator : IRequestValidator<UpdateContactMessageStatusCommand>
{ public Task<IReadOnlyCollection<ValidationFailure>> ValidateAsync(UpdateContactMessageStatusCommand r, CancellationToken ct = default) { var f = new List<ValidationFailure>(); ContactValidation.Status(f, r.Status, false); return Task.FromResult<IReadOnlyCollection<ValidationFailure>>(f); } }
internal static class ContactValidation
{ private static readonly string[] Statuses = ["NEW", "READ", "REPLIED", "ARCHIVED"]; public static void Status(ICollection<ValidationFailure> f, string? status, bool optional) { if (string.IsNullOrWhiteSpace(status)) { if (!optional) f.Add(new("status", "Status is required.")); return; } if (!Statuses.Contains(status.Trim().ToUpperInvariant())) f.Add(new("status", "Status must be NEW, READ, REPLIED, or ARCHIVED.")); } }
internal static class ContactMapping
{ public static readonly System.Linq.Expressions.Expression<Func<ContactMessage, ContactMessageResult>> Project = x => new(x.Id, x.Name, x.Email, x.Subject, x.Message, x.Status, x.ReceivedAt, x.ReadAt, x.RepliedAt); public static ContactMessageResult Map(ContactMessage x) => new(x.Id, x.Name, x.Email, x.Subject, x.Message, x.Status, x.ReceivedAt, x.ReadAt, x.RepliedAt); }
