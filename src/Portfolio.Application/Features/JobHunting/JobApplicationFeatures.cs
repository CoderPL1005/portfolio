using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Portfolio.Application.Common.Abstractions.Authentication;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Common.Abstractions.Persistence;
using Portfolio.Application.Common.Abstractions.Validation;
using Portfolio.Application.Common.Exceptions;
using Portfolio.Application.Common.Models;
using Portfolio.Domain.Constants;
using Portfolio.Domain.Entities;

namespace Portfolio.Application.Features.JobHunting;

public sealed record JobApplicationListItem(Guid Id,Guid JobPostingId,string CompanyName,string PositionTitle,string Status,string? Channel,DateTimeOffset? AppliedAt,DateTimeOffset? LastActivityAt,int Version);
public sealed record JobApplicationEventResult(Guid Id,string EventType,string? FromStatus,string? ToStatus,string ActorType,Guid? ActorAdminUserId,string? Note,JsonElement Metadata,DateTimeOffset OccurredAt,DateTimeOffset CreatedAt);
public sealed record JobApplicationDocumentResult(Guid Id,string DocumentType,string VersionLabel,string? FileName,string? StorageKey,string? ContentHash,JsonElement Metadata,DateTimeOffset CreatedAt,DateTimeOffset? RemovedAt);
public sealed record ApplicationJobSummary(Guid Id,string CompanyName,string PositionTitle,string Location,string VerificationStatus,string SelectionStatus,bool Archived);
public sealed record JobApplicationResult(Guid Id,Guid JobPostingId,string Status,string? Channel,string? ApplicationEmail,string? ApplicationUrl,string? ExternalApplicationId,DateTimeOffset? AppliedAt,DateTimeOffset? LastActivityAt,string? Notes,int Version,DateTimeOffset CreatedAt,DateTimeOffset UpdatedAt,ApplicationJobSummary Job,IReadOnlyCollection<JobApplicationEventResult> Events,IReadOnlyCollection<JobApplicationDocumentResult> Documents);

public sealed record GetJobApplicationsQuery(int Page,int PageSize,string? Search,string? Status,string? Channel):IRequest<PagedResult<JobApplicationListItem>>;
public sealed record GetJobApplicationQuery(Guid Id):IRequest<JobApplicationResult>;
public sealed record CreateJobApplicationCommand(Guid JobPostingId,int ExpectedJobVersion):IRequest<JobApplicationResult>;
public sealed record UpdateJobApplicationCommand(Guid Id,int ExpectedVersion,string? Channel,string? ApplicationEmail,string? ApplicationUrl,string? ExternalApplicationId,string? Notes):IRequest<JobApplicationResult>;
public sealed record TransitionJobApplicationCommand(Guid Id,string Status,int ExpectedVersion,string? Note,DateTimeOffset? OccurredAt):IRequest<JobApplicationResult>;
public sealed record AttachJobApplicationDocumentCommand(Guid JobApplicationId,string DocumentType,string VersionLabel,string? FileName,string? StorageKey,string? ContentHash,JsonElement? Metadata):IRequest<JobApplicationDocumentResult>;
public sealed record RemoveJobApplicationDocumentCommand(Guid JobApplicationId,Guid DocumentId):IRequest<bool>;

public sealed class GetJobApplicationsQueryHandler(IApplicationDbContext db):IRequestHandler<GetJobApplicationsQuery,PagedResult<JobApplicationListItem>>
{
    public async Task<PagedResult<JobApplicationListItem>> HandleAsync(GetJobApplicationsQuery r,CancellationToken ct=default){var q=db.JobApplications.AsNoTracking();if(!string.IsNullOrWhiteSpace(r.Search)){var v=r.Search.Trim().ToLowerInvariant();q=q.Where(x=>x.JobPosting.CompanyName.ToLower().Contains(v)||x.JobPosting.PositionTitle.ToLower().Contains(v));}if(!string.IsNullOrWhiteSpace(r.Status)){var v=r.Status.Trim().ToUpperInvariant();q=q.Where(x=>x.Status==v);}if(!string.IsNullOrWhiteSpace(r.Channel)){var v=r.Channel.Trim().ToUpperInvariant();q=q.Where(x=>x.Channel==v);}var total=await q.CountAsync(ct);var items=await q.OrderByDescending(x=>x.LastActivityAt??x.UpdatedAt).ThenByDescending(x=>x.CreatedAt).ThenBy(x=>x.Id).Skip((r.Page-1)*r.PageSize).Take(r.PageSize).Select(x=>new JobApplicationListItem(x.Id,x.JobPostingId,x.JobPosting.CompanyName,x.JobPosting.PositionTitle,x.Status,x.Channel,x.AppliedAt,x.LastActivityAt,x.Version)).ToListAsync(ct);return new(items,r.Page,r.PageSize,total);}
}
public sealed class GetJobApplicationQueryHandler(IApplicationDbContext db):IRequestHandler<GetJobApplicationQuery,JobApplicationResult>{public Task<JobApplicationResult> HandleAsync(GetJobApplicationQuery r,CancellationToken ct=default)=>JobApplicationMapping.GetAsync(db,r.Id,ct);}

public sealed class CreateJobApplicationCommandHandler(IApplicationDbContext db,TimeProvider clock,ICurrentUser currentUser,IJobApplicationConflictDetector conflictDetector,IJobApplicationCreationTransactionFactory transactionFactory):IRequestHandler<CreateJobApplicationCommand,JobApplicationResult>
{
    public async Task<JobApplicationResult> HandleAsync(CreateJobApplicationCommand r,CancellationToken ct=default)
    {
        await using var transaction=await transactionFactory.BeginAsync(r.JobPostingId,ct);
        var job=transaction.JobPosting??throw new NotFoundException("JOB_POSTING_NOT_FOUND","The job posting was not found.");
        if(job.Version!=r.ExpectedJobVersion)throw new ConflictException("JOB_POSTING_VERSION_CONFLICT","The job posting has been modified.");
        if(job.ArchivedAt is not null)throw new ConflictException("JOB_POSTING_ARCHIVED","An application cannot be created for an archived job posting.");
        if(job.SelectionStatus!=JobPostingSelectionStatuses.Approved)throw new ConflictException("JOB_POSTING_NOT_APPROVED","The job posting must be approved before an application can be created.");
        if(await db.JobApplications.AnyAsync(x=>x.JobPostingId==r.JobPostingId,ct))throw DuplicateApplication();
        var now=clock.GetUtcNow();
        var x=new JobApplication{Id=Guid.NewGuid(),JobPostingId=r.JobPostingId,Status=JobApplicationStatuses.Draft,Channel=null,ApplicationEmail=JobText.TrimOrNull(job.ApplicationEmail),ApplicationUrl=JobText.TrimOrNull(job.ApplicationUrl),AppliedAt=null,LastActivityAt=now,Version=1,CreatedAt=now,UpdatedAt=now};
        var e=JobApplicationMapping.Event(x.Id,JobApplicationEventTypes.Created,null,JobApplicationStatuses.Draft,currentUser.AdminUserId,null,now);
        db.JobApplications.Add(x);db.JobApplicationEvents.Add(e);
        try{await db.SaveChangesAsync(ct);}catch(DbUpdateException exception)when(conflictDetector.IsDuplicateJobPosting(exception)){throw DuplicateApplication();}
        await transaction.CommitAsync(ct);
        return await JobApplicationMapping.GetAsync(db,x.Id,ct);
    }

    private static ConflictException DuplicateApplication()=>new("JOB_APPLICATION_ALREADY_EXISTS","An application already exists for this job posting.");
}
public sealed class UpdateJobApplicationCommandHandler(IApplicationDbContext db,TimeProvider clock):IRequestHandler<UpdateJobApplicationCommand,JobApplicationResult>
{public async Task<JobApplicationResult> HandleAsync(UpdateJobApplicationCommand r,CancellationToken ct=default){var x=await JobApplicationMapping.RequireVersion(db,r.Id,r.ExpectedVersion,ct);x.Channel=JobText.TrimOrNull(r.Channel)?.ToUpperInvariant();x.ApplicationEmail=JobText.TrimOrNull(r.ApplicationEmail);x.ApplicationUrl=JobText.TrimOrNull(r.ApplicationUrl);x.ExternalApplicationId=JobText.TrimOrNull(r.ExternalApplicationId);x.Notes=JobText.TrimOrNull(r.Notes);x.Version++;x.UpdatedAt=clock.GetUtcNow();await JobApplicationMapping.SaveVersioned(db,ct);return await JobApplicationMapping.GetAsync(db,x.Id,ct);}}
public sealed class TransitionJobApplicationCommandHandler(IApplicationDbContext db,TimeProvider clock,ICurrentUser currentUser,ApplicationSubmissionReadinessEvaluator submissionReadiness):IRequestHandler<TransitionJobApplicationCommand,JobApplicationResult>
{
    public async Task<JobApplicationResult> HandleAsync(TransitionJobApplicationCommand r,CancellationToken ct=default){var x=await JobApplicationMapping.RequireVersion(db,r.Id,r.ExpectedVersion,ct);var target=r.Status.Trim().ToUpperInvariant();if(!JobApplicationTransitionPolicy.CanTransition(x.Status,target))throw new ConflictException("JOB_APPLICATION_TRANSITION_INVALID",$"Transition from {x.Status} to {target} is not allowed.");var now=clock.GetUtcNow();var occurred=r.OccurredAt??now;if(occurred>now)throw new ValidationException([new("occurredAt","Occurred time cannot be in the future.")]);if(x.Status==JobApplicationStatuses.Draft&&target==JobApplicationStatuses.Applied){var managedCv=await db.JobApplicationDocuments.AsNoTracking().SingleOrDefaultAsync(document=>document.JobApplicationId==x.Id&&document.DocumentType=="CV"&&document.PackageRevision==1&&document.RemovedAt==null,ct);var readiness=submissionReadiness.Evaluate(x,managedCv);if(!readiness.IsReady){var blocker=readiness.Blockers.First();throw new ConflictException(blocker.Code,blocker.Message);}}JobApplicationLifecycle.ApplyTransition(db,x,target,currentUser.AdminUserId,JobText.TrimOrNull(r.Note),occurred,now);await JobApplicationMapping.SaveVersioned(db,ct);return await JobApplicationMapping.GetAsync(db,x.Id,ct);}
}
public sealed class AttachJobApplicationDocumentCommandHandler(IApplicationDbContext db,TimeProvider clock,ICurrentUser currentUser):IRequestHandler<AttachJobApplicationDocumentCommand,JobApplicationDocumentResult>
{
    public async Task<JobApplicationDocumentResult> HandleAsync(AttachJobApplicationDocumentCommand r,CancellationToken ct=default){if(!await db.JobApplications.AnyAsync(x=>x.Id==r.JobApplicationId,ct))throw new NotFoundException("JOB_APPLICATION_NOT_FOUND","The job application was not found.");var now=clock.GetUtcNow();var x=new JobApplicationDocument{Id=Guid.NewGuid(),JobApplicationId=r.JobApplicationId,DocumentType=r.DocumentType.Trim().ToUpperInvariant(),VersionLabel=r.VersionLabel.Trim(),FileName=JobText.TrimOrNull(r.FileName),StorageKey=JobText.TrimOrNull(r.StorageKey),ContentHash=JobText.TrimOrNull(r.ContentHash)?.ToLowerInvariant(),Metadata=JsonDocument.Parse(r.Metadata is { ValueKind:JsonValueKind.Object } m?m.GetRawText():"{}"),CreatedAt=now};db.JobApplicationDocuments.Add(x);db.JobApplicationEvents.Add(JobApplicationMapping.Event(r.JobApplicationId,JobApplicationEventTypes.DocumentAttached,null,null,currentUser.AdminUserId,$"{x.DocumentType} {x.VersionLabel}",now));await db.SaveChangesAsync(ct);return JobApplicationMapping.Map(x);}
}
public sealed class RemoveJobApplicationDocumentCommandHandler(IApplicationDbContext db,TimeProvider clock,ICurrentUser currentUser):IRequestHandler<RemoveJobApplicationDocumentCommand,bool>
{
    public async Task<bool> HandleAsync(RemoveJobApplicationDocumentCommand r,CancellationToken ct=default){if(!await db.JobApplications.AnyAsync(x=>x.Id==r.JobApplicationId,ct))throw new NotFoundException("JOB_APPLICATION_NOT_FOUND","The job application was not found.");var x=await db.JobApplicationDocuments.SingleOrDefaultAsync(x=>x.Id==r.DocumentId&&x.JobApplicationId==r.JobApplicationId,ct)??throw new NotFoundException("JOB_APPLICATION_DOCUMENT_NOT_FOUND","The application document was not found.");if(x.PackageRevision is not null)throw new ConflictException("APPLICATION_PACKAGE_DOCUMENT_IMMUTABLE","A finalized application package document cannot be removed.");if(x.RemovedAt is not null)throw new ConflictException("JOB_APPLICATION_DOCUMENT_REMOVED","The application document is already removed.");var now=clock.GetUtcNow();x.RemovedAt=now;db.JobApplicationEvents.Add(JobApplicationMapping.Event(r.JobApplicationId,JobApplicationEventTypes.DocumentRemoved,null,null,currentUser.AdminUserId,$"{x.DocumentType} {x.VersionLabel}",now));await db.SaveChangesAsync(ct);return true;}
}

public sealed class GetJobApplicationsQueryValidator:IRequestValidator<GetJobApplicationsQuery>{public Task<IReadOnlyCollection<ValidationFailure>> ValidateAsync(GetJobApplicationsQuery r,CancellationToken ct=default){var f=JobValidation.Paging(r.Page,r.PageSize);JobValidation.OptionalClosed(f,"status",r.Status,JobValidation.ApplicationStatuses);JobValidation.OptionalClosed(f,"channel",r.Channel,JobValidation.Channels);return Task.FromResult<IReadOnlyCollection<ValidationFailure>>(f);}}
public sealed class CreateJobApplicationCommandValidator:IRequestValidator<CreateJobApplicationCommand>{public Task<IReadOnlyCollection<ValidationFailure>> ValidateAsync(CreateJobApplicationCommand r,CancellationToken ct=default){var f=new List<ValidationFailure>();JobValidation.Version(f,r.ExpectedJobVersion);return Task.FromResult<IReadOnlyCollection<ValidationFailure>>(f);}}
public sealed class UpdateJobApplicationCommandValidator:IRequestValidator<UpdateJobApplicationCommand>{public Task<IReadOnlyCollection<ValidationFailure>> ValidateAsync(UpdateJobApplicationCommand r,CancellationToken ct=default){var f=ApplicationValidation.Metadata(r.Channel,r.ApplicationEmail,r.ApplicationUrl,r.ExternalApplicationId,r.Notes);JobValidation.Version(f,r.ExpectedVersion);return Task.FromResult<IReadOnlyCollection<ValidationFailure>>(f);}}
public sealed class TransitionJobApplicationCommandValidator:IRequestValidator<TransitionJobApplicationCommand>{public Task<IReadOnlyCollection<ValidationFailure>> ValidateAsync(TransitionJobApplicationCommand r,CancellationToken ct=default){var f=new List<ValidationFailure>();JobValidation.RequiredClosed(f,"status",r.Status,JobValidation.ApplicationStatuses);JobValidation.Version(f,r.ExpectedVersion);JobValidation.Optional(f,"note",r.Note,200000);return Task.FromResult<IReadOnlyCollection<ValidationFailure>>(f);}}
public sealed class AttachJobApplicationDocumentCommandValidator:IRequestValidator<AttachJobApplicationDocumentCommand>{public Task<IReadOnlyCollection<ValidationFailure>> ValidateAsync(AttachJobApplicationDocumentCommand r,CancellationToken ct=default){var f=new List<ValidationFailure>();JobValidation.Required(f,"documentType",r.DocumentType,50);JobValidation.Required(f,"versionLabel",r.VersionLabel,100);JobValidation.Optional(f,"fileName",r.FileName,500);JobValidation.Optional(f,"storageKey",r.StorageKey,1000);if(!string.IsNullOrWhiteSpace(r.ContentHash)&&!Regex.IsMatch(r.ContentHash,"^[0-9a-fA-F]{64}$"))f.Add(new("contentHash","Content hash must be 64 hexadecimal characters."));if(r.Metadata is { ValueKind:not JsonValueKind.Object })f.Add(new("metadata","Metadata must be a JSON object."));return Task.FromResult<IReadOnlyCollection<ValidationFailure>>(f);}}

internal static class ApplicationValidation{public static List<ValidationFailure> Metadata(string? channel,string? email,string? url,string? external,string? notes){var f=new List<ValidationFailure>();JobValidation.OptionalClosed(f,"channel",channel,JobValidation.Channels);JobValidation.Email(f,"applicationEmail",email);if(url is not null)JobValidation.Url(f,"applicationUrl",url);JobValidation.Optional(f,"externalApplicationId",external,500);JobValidation.Optional(f,"notes",notes,200000);return f;}}
internal static class JobApplicationTransitionPolicy
{
    private static readonly IReadOnlyDictionary<string,string[]> Allowed=new Dictionary<string,string[]>{{"DRAFT",["APPLIED","WITHDRAWN"]},{"APPLIED",["INTERVIEW","REJECTED","WITHDRAWN"]},{"INTERVIEW",["REJECTED","OFFER","WITHDRAWN"]},{"OFFER",["WITHDRAWN"]},{"REJECTED",[]},{"WITHDRAWN",[]}};
    public static bool CanTransition(string from,string to)=>Allowed.TryGetValue(from,out var targets)&&targets.Contains(to);
}
internal static class JobApplicationLifecycle
{
    public static void ApplyTransition(IApplicationDbContext db,JobApplication application,string target,Guid? adminUserId,string? note,DateTimeOffset occurredAt,DateTimeOffset updatedAt)
    {
        if(!JobApplicationTransitionPolicy.CanTransition(application.Status,target))throw new ConflictException("JOB_APPLICATION_TRANSITION_INVALID",$"Transition from {application.Status} to {target} is not allowed.");
        var from=application.Status;application.Status=target;if(target==JobApplicationStatuses.Applied&&application.AppliedAt is null)application.AppliedAt=occurredAt;application.LastActivityAt=occurredAt;application.Version++;application.UpdatedAt=updatedAt;db.JobApplicationEvents.Add(JobApplicationMapping.Event(application.Id,JobApplicationEventTypes.StatusChanged,from,target,adminUserId,note,occurredAt,updatedAt));
    }
}
internal static class JobApplicationMapping
{
    public static JobApplicationEvent Event(Guid appId,string type,string? from,string? to,Guid? admin,string? note,DateTimeOffset occurred,DateTimeOffset? created=null)=>new(){Id=Guid.NewGuid(),JobApplicationId=appId,EventType=type,FromStatus=from,ToStatus=to,ActorType=JobApplicationEventActorTypes.Admin,ActorAdminUserId=admin,Note=note,Metadata=JsonDocument.Parse("{}"),OccurredAt=occurred,CreatedAt=created??occurred};
    public static async Task<JobApplication> RequireVersion(IApplicationDbContext db,Guid id,int version,CancellationToken ct){var x=await db.JobApplications.SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new NotFoundException("JOB_APPLICATION_NOT_FOUND","The job application was not found.");if(x.Version!=version)throw new ConflictException("JOB_APPLICATION_VERSION_CONFLICT","The job application has been modified.");return x;}
    public static async Task SaveVersioned(IApplicationDbContext db,CancellationToken ct){try{await db.SaveChangesAsync(ct);}catch(DbUpdateConcurrencyException){throw new ConflictException("JOB_APPLICATION_VERSION_CONFLICT","The job application has been modified.");}}
    public static JobApplicationDocumentResult Map(JobApplicationDocument x)=>new(x.Id,x.DocumentType,x.VersionLabel,x.FileName,x.StorageKey,x.ContentHash,x.Metadata.RootElement.Clone(),x.CreatedAt,x.RemovedAt);
    public static async Task<JobApplicationResult> GetAsync(IApplicationDbContext db,Guid id,CancellationToken ct){var x=await db.JobApplications.AsNoTracking().Include(x=>x.JobPosting).SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new NotFoundException("JOB_APPLICATION_NOT_FOUND","The job application was not found.");var eventsRows=await db.JobApplicationEvents.AsNoTracking().Where(y=>y.JobApplicationId==id).OrderBy(y=>y.OccurredAt).ThenBy(y=>y.Id).ToListAsync(ct);var docsRows=await db.JobApplicationDocuments.AsNoTracking().Where(y=>y.JobApplicationId==id&&y.PackageRevision==null).OrderBy(y=>y.CreatedAt).ThenBy(y=>y.Id).ToListAsync(ct);var events=eventsRows.Select(y=>new JobApplicationEventResult(y.Id,y.EventType,y.FromStatus,y.ToStatus,y.ActorType,y.ActorAdminUserId,y.Note,y.Metadata.RootElement.Clone(),y.OccurredAt,y.CreatedAt)).ToArray();var docs=docsRows.Select(Map).ToArray();var job=new ApplicationJobSummary(x.JobPosting.Id,x.JobPosting.CompanyName,x.JobPosting.PositionTitle,x.JobPosting.Location,x.JobPosting.VerificationStatus,x.JobPosting.SelectionStatus,x.JobPosting.ArchivedAt!=null);return new(x.Id,x.JobPostingId,x.Status,x.Channel,x.ApplicationEmail,x.ApplicationUrl,x.ExternalApplicationId,x.AppliedAt,x.LastActivityAt,x.Notes,x.Version,x.CreatedAt,x.UpdatedAt,job,events,docs);}
}
