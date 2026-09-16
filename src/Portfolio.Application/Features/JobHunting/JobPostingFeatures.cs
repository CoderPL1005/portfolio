using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Common.Abstractions.Persistence;
using Portfolio.Application.Common.Abstractions.Validation;
using Portfolio.Application.Common.Exceptions;
using Portfolio.Application.Common.Models;
using Portfolio.Domain.Constants;
using Portfolio.Domain.Entities;

namespace Portfolio.Application.Features.JobHunting;

public sealed record JobPostingListItem(Guid Id,string CompanyName,string PositionTitle,string Location,string? Source,int SourceCount,string VerificationStatus,string SelectionStatus,bool Archived,DateTimeOffset CreatedAt,DateTimeOffset UpdatedAt,int ApplicationCount,int Version);
public sealed record RawJobPostingResult(Guid Id,string Source,string? SourceExternalId,string? SourceUrl,string? SourceUrlHash,string RawContent,string ContentHash,string? CompanyTitleFingerprint,string IngestionStatus,Guid? DuplicateOfRawJobPostingId,JsonElement Metadata,DateTimeOffset DiscoveredAt,DateTimeOffset CreatedAt,DateTimeOffset UpdatedAt);
public sealed record JobApplicationSummary(Guid Id,string Status,string? Channel,DateTimeOffset? AppliedAt,DateTimeOffset? LastActivityAt,int Version);
public sealed record JobPostingResult(Guid Id,string CompanyName,string PositionTitle,string Location,string? EmploymentType,string? WorkplaceType,decimal? SalaryMinimum,decimal? SalaryMaximum,string? SalaryCurrency,string? SalaryPeriod,string? ExperienceRequirements,string Description,JsonElement TechnologyStack,string? ApplicationEmail,string? ApplicationUrl,string VerificationStatus,string SelectionStatus,DateTimeOffset? ExpiresAt,DateTimeOffset? VerifiedAt,DateTimeOffset? ArchivedAt,string? Notes,int Version,DateTimeOffset CreatedAt,DateTimeOffset UpdatedAt,IReadOnlyCollection<RawJobPostingResult> Sources,IReadOnlyCollection<JobApplicationSummary> Applications);

public sealed record GetJobPostingsQuery(int Page,int PageSize,string? Search,string? Source,string? VerificationStatus,string? SelectionStatus,bool? Archived):IRequest<PagedResult<JobPostingListItem>>;
public sealed record GetJobPostingQuery(Guid Id):IRequest<JobPostingResult>;
public sealed record CreateJobPostingCommand(string Source,string? SourceExternalId,string? SourceUrl,string RawContent,string CompanyName,string PositionTitle,string Location,string? EmploymentType,string? WorkplaceType,decimal? SalaryMinimum,decimal? SalaryMaximum,string? SalaryCurrency,string? SalaryPeriod,string? ExperienceRequirements,string Description,JsonElement TechnologyStack,string? ApplicationEmail,string? ApplicationUrl,DateTimeOffset? ExpiresAt,string? Notes):IRequest<JobPostingResult>;
public sealed record UpdateJobPostingCommand(Guid Id,int ExpectedVersion,string CompanyName,string PositionTitle,string Location,string? EmploymentType,string? WorkplaceType,decimal? SalaryMinimum,decimal? SalaryMaximum,string? SalaryCurrency,string? SalaryPeriod,string? ExperienceRequirements,string Description,JsonElement TechnologyStack,string? ApplicationEmail,string? ApplicationUrl,DateTimeOffset? ExpiresAt,string? Notes):IRequest<JobPostingResult>;
public sealed record UpdateJobVerificationCommand(Guid Id,string Status,int ExpectedVersion):IRequest<JobPostingResult>;
public sealed record UpdateJobSelectionCommand(Guid Id,string Status,int ExpectedVersion):IRequest<JobPostingResult>;
public sealed record ArchiveJobPostingCommand(Guid Id,int ExpectedVersion):IRequest<JobPostingResult>;

public sealed class GetJobPostingsQueryHandler(IApplicationDbContext db):IRequestHandler<GetJobPostingsQuery,PagedResult<JobPostingListItem>>
{
    public async Task<PagedResult<JobPostingListItem>> HandleAsync(GetJobPostingsQuery r,CancellationToken ct=default)
    {
        var q=db.JobPostings.AsNoTracking();
        if(!string.IsNullOrWhiteSpace(r.Search)){var v=r.Search.Trim().ToLowerInvariant();q=q.Where(x=>x.CompanyName.ToLower().Contains(v)||x.PositionTitle.ToLower().Contains(v));}
        if(!string.IsNullOrWhiteSpace(r.Source)){var v=r.Source.Trim().ToUpperInvariant();q=q.Where(x=>x.RawJobPostings.Any(y=>y.Source==v));}
        if(!string.IsNullOrWhiteSpace(r.VerificationStatus)){var v=r.VerificationStatus.Trim().ToUpperInvariant();q=q.Where(x=>x.VerificationStatus==v);}
        if(!string.IsNullOrWhiteSpace(r.SelectionStatus)){var v=r.SelectionStatus.Trim().ToUpperInvariant();q=q.Where(x=>x.SelectionStatus==v);}
        if(r.Archived.HasValue)q=q.Where(x=>(x.ArchivedAt!=null)==r.Archived.Value);
        var total=await q.CountAsync(ct);
        var items=await q.OrderByDescending(x=>x.CreatedAt).ThenBy(x=>x.Id).Skip((r.Page-1)*r.PageSize).Take(r.PageSize)
            .Select(x=>new JobPostingListItem(x.Id,x.CompanyName,x.PositionTitle,x.Location,x.RawJobPostings.OrderBy(y=>y.CreatedAt).ThenBy(y=>y.Id).Select(y=>y.Source).FirstOrDefault(),x.RawJobPostings.Count,x.VerificationStatus,x.SelectionStatus,x.ArchivedAt!=null,x.CreatedAt,x.UpdatedAt,x.JobApplications.Count,x.Version)).ToListAsync(ct);
        return new(items,r.Page,r.PageSize,total);
    }
}
public sealed class GetJobPostingQueryHandler(IApplicationDbContext db):IRequestHandler<GetJobPostingQuery,JobPostingResult>{public Task<JobPostingResult> HandleAsync(GetJobPostingQuery r,CancellationToken ct=default)=>JobPostingMapping.GetAsync(db,r.Id,ct);}

public sealed class CreateJobPostingCommandHandler(IApplicationDbContext db,TimeProvider clock):IRequestHandler<CreateJobPostingCommand,JobPostingResult>
{
    public async Task<JobPostingResult> HandleAsync(CreateJobPostingCommand r,CancellationToken ct=default)
    {
        var source=r.Source.Trim().ToUpperInvariant();var raw=JobHashing.NormalizeText(r.RawContent);var contentHash=JobHashing.Sha256(raw);var sourceUrl=JobText.TrimOrNull(r.SourceUrl);var urlHash=sourceUrl is null?null:JobHashing.Sha256(JobHashing.NormalizeUrl(sourceUrl));
        if(!string.IsNullOrWhiteSpace(r.SourceExternalId)&&await db.RawJobPostings.AnyAsync(x=>x.Source==source&&x.SourceExternalId==r.SourceExternalId.Trim(),ct))throw new ConflictException("JOB_SOURCE_EXTERNAL_ID_EXISTS","This source external ID already exists.");
        if(urlHash is not null&&await db.RawJobPostings.AnyAsync(x=>x.Source==source&&x.SourceUrlHash==urlHash,ct))throw new ConflictException("JOB_SOURCE_URL_EXISTS","This source URL already exists.");
        var now=clock.GetUtcNow();var posting=new JobPosting{Id=Guid.NewGuid(),VerificationStatus=JobPostingVerificationStatuses.Pending,SelectionStatus=JobPostingSelectionStatuses.PendingAnalysis,Version=1,CreatedAt=now,UpdatedAt=now};
        JobPostingMapping.Assign(posting,r.CompanyName,r.PositionTitle,r.Location,r.EmploymentType,r.WorkplaceType,r.SalaryMinimum,r.SalaryMaximum,r.SalaryCurrency,r.SalaryPeriod,r.ExperienceRequirements,r.Description,r.TechnologyStack,r.ApplicationEmail,r.ApplicationUrl,r.ExpiresAt,r.Notes);
        var sourceRow=new RawJobPosting{Id=Guid.NewGuid(),JobPostingId=posting.Id,Source=source,SourceExternalId=JobText.TrimOrNull(r.SourceExternalId),SourceUrl=sourceUrl,SourceUrlHash=urlHash,RawContent=r.RawContent,ContentHash=contentHash,CompanyTitleFingerprint=JobHashing.Fingerprint(posting.CompanyName,posting.PositionTitle),IngestionStatus=RawJobPostingIngestionStatuses.Normalized,Metadata=JsonDocument.Parse("{}"),DiscoveredAt=now,CreatedAt=now,UpdatedAt=now};
        db.JobPostings.Add(posting);db.RawJobPostings.Add(sourceRow);
        try{await db.SaveChangesAsync(ct);}catch(DbUpdateException){throw new ConflictException("JOB_SOURCE_EXISTS","The source identity already exists.");}
        return await JobPostingMapping.GetAsync(db,posting.Id,ct);
    }
}

public sealed class UpdateJobPostingCommandHandler(IApplicationDbContext db,TimeProvider clock):IRequestHandler<UpdateJobPostingCommand,JobPostingResult>
{
    public async Task<JobPostingResult> HandleAsync(UpdateJobPostingCommand r,CancellationToken ct=default){var x=await JobPostingMapping.RequireVersion(db,r.Id,r.ExpectedVersion,ct);JobPostingMapping.Assign(x,r.CompanyName,r.PositionTitle,r.Location,r.EmploymentType,r.WorkplaceType,r.SalaryMinimum,r.SalaryMaximum,r.SalaryCurrency,r.SalaryPeriod,r.ExperienceRequirements,r.Description,r.TechnologyStack,r.ApplicationEmail,r.ApplicationUrl,r.ExpiresAt,r.Notes);x.Version++;x.UpdatedAt=clock.GetUtcNow();await JobPostingMapping.SaveVersioned(db,ct);return await JobPostingMapping.GetAsync(db,x.Id,ct);}
}
public sealed class UpdateJobVerificationCommandHandler(IApplicationDbContext db,TimeProvider clock):IRequestHandler<UpdateJobVerificationCommand,JobPostingResult>
{public async Task<JobPostingResult> HandleAsync(UpdateJobVerificationCommand r,CancellationToken ct=default){var x=await JobPostingMapping.RequireVersion(db,r.Id,r.ExpectedVersion,ct);var now=clock.GetUtcNow();x.VerificationStatus=r.Status.Trim().ToUpperInvariant();x.VerifiedAt=x.VerificationStatus==JobPostingVerificationStatuses.Verified?now:null;x.Version++;x.UpdatedAt=now;await JobPostingMapping.SaveVersioned(db,ct);return await JobPostingMapping.GetAsync(db,x.Id,ct);}}
public sealed class UpdateJobSelectionCommandHandler(IApplicationDbContext db,TimeProvider clock):IRequestHandler<UpdateJobSelectionCommand,JobPostingResult>
{public async Task<JobPostingResult> HandleAsync(UpdateJobSelectionCommand r,CancellationToken ct=default){var x=await JobPostingMapping.RequireVersion(db,r.Id,r.ExpectedVersion,ct);x.SelectionStatus=r.Status.Trim().ToUpperInvariant();x.Version++;x.UpdatedAt=clock.GetUtcNow();await JobPostingMapping.SaveVersioned(db,ct);return await JobPostingMapping.GetAsync(db,x.Id,ct);}}
public sealed class ArchiveJobPostingCommandHandler(IApplicationDbContext db,TimeProvider clock):IRequestHandler<ArchiveJobPostingCommand,JobPostingResult>
{public async Task<JobPostingResult> HandleAsync(ArchiveJobPostingCommand r,CancellationToken ct=default){var x=await JobPostingMapping.RequireVersion(db,r.Id,r.ExpectedVersion,ct);if(x.ArchivedAt is not null)throw new ConflictException("JOB_ALREADY_ARCHIVED","The job posting is already archived.");var now=clock.GetUtcNow();x.ArchivedAt=now;x.Version++;x.UpdatedAt=now;await JobPostingMapping.SaveVersioned(db,ct);return await JobPostingMapping.GetAsync(db,x.Id,ct);}}

public sealed class GetJobPostingsQueryValidator:IRequestValidator<GetJobPostingsQuery>{public Task<IReadOnlyCollection<ValidationFailure>> ValidateAsync(GetJobPostingsQuery r,CancellationToken ct=default){var f=JobValidation.Paging(r.Page,r.PageSize);JobValidation.OptionalClosed(f,"source",r.Source,JobValidation.Sources);JobValidation.OptionalClosed(f,"verificationStatus",r.VerificationStatus,JobValidation.Verification);JobValidation.OptionalClosed(f,"selectionStatus",r.SelectionStatus,JobValidation.Selection);return Task.FromResult<IReadOnlyCollection<ValidationFailure>>(f);}}
public sealed class CreateJobPostingCommandValidator:IRequestValidator<CreateJobPostingCommand>{public Task<IReadOnlyCollection<ValidationFailure>> ValidateAsync(CreateJobPostingCommand r,CancellationToken ct=default)=>Task.FromResult<IReadOnlyCollection<ValidationFailure>>(JobValidation.Posting(r.Source,r.SourceExternalId,r.SourceUrl,r.RawContent,r.CompanyName,r.PositionTitle,r.Location,r.EmploymentType,r.WorkplaceType,r.SalaryMinimum,r.SalaryMaximum,r.SalaryCurrency,r.SalaryPeriod,r.Description,r.TechnologyStack,r.ApplicationEmail,r.ApplicationUrl,r.Notes));}
public sealed class UpdateJobPostingCommandValidator:IRequestValidator<UpdateJobPostingCommand>{public Task<IReadOnlyCollection<ValidationFailure>> ValidateAsync(UpdateJobPostingCommand r,CancellationToken ct=default){var f=JobValidation.Posting(null,null,null,null,r.CompanyName,r.PositionTitle,r.Location,r.EmploymentType,r.WorkplaceType,r.SalaryMinimum,r.SalaryMaximum,r.SalaryCurrency,r.SalaryPeriod,r.Description,r.TechnologyStack,r.ApplicationEmail,r.ApplicationUrl,r.Notes);JobValidation.Version(f,r.ExpectedVersion);return Task.FromResult<IReadOnlyCollection<ValidationFailure>>(f);}}
public sealed class UpdateJobVerificationCommandValidator:IRequestValidator<UpdateJobVerificationCommand>{public Task<IReadOnlyCollection<ValidationFailure>> ValidateAsync(UpdateJobVerificationCommand r,CancellationToken ct=default){var f=new List<ValidationFailure>();JobValidation.RequiredClosed(f,"status",r.Status,JobValidation.Verification);JobValidation.Version(f,r.ExpectedVersion);return Task.FromResult<IReadOnlyCollection<ValidationFailure>>(f);}}
public sealed class UpdateJobSelectionCommandValidator:IRequestValidator<UpdateJobSelectionCommand>{public Task<IReadOnlyCollection<ValidationFailure>> ValidateAsync(UpdateJobSelectionCommand r,CancellationToken ct=default){var f=new List<ValidationFailure>();JobValidation.RequiredClosed(f,"status",r.Status,JobValidation.Selection);JobValidation.Version(f,r.ExpectedVersion);return Task.FromResult<IReadOnlyCollection<ValidationFailure>>(f);}}
public sealed class ArchiveJobPostingCommandValidator:IRequestValidator<ArchiveJobPostingCommand>{public Task<IReadOnlyCollection<ValidationFailure>> ValidateAsync(ArchiveJobPostingCommand r,CancellationToken ct=default){var f=new List<ValidationFailure>();JobValidation.Version(f,r.ExpectedVersion);return Task.FromResult<IReadOnlyCollection<ValidationFailure>>(f);}}

internal static class JobPostingMapping
{
    public static void Assign(JobPosting x,string company,string title,string location,string? employment,string? workplace,decimal? min,decimal? max,string? currency,string? period,string? experience,string description,JsonElement stack,string? email,string? url,DateTimeOffset? expires,string? notes){x.CompanyName=company.Trim();x.PositionTitle=title.Trim();x.Location=location.Trim();x.EmploymentType=JobText.TrimOrNull(employment);x.WorkplaceType=JobText.TrimOrNull(workplace);x.SalaryMinimum=min;x.SalaryMaximum=max;x.SalaryCurrency=JobText.TrimOrNull(currency)?.ToUpperInvariant();x.SalaryPeriod=JobText.TrimOrNull(period);x.ExperienceRequirements=JobText.TrimOrNull(experience);x.Description=description.Trim();x.TechnologyStack=JsonDocument.Parse(stack.GetRawText());x.ApplicationEmail=JobText.TrimOrNull(email);x.ApplicationUrl=JobText.TrimOrNull(url);x.ExpiresAt=expires;x.Notes=JobText.TrimOrNull(notes);}
    public static async Task<JobPosting> RequireVersion(IApplicationDbContext db,Guid id,int version,CancellationToken ct){var x=await db.JobPostings.SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new NotFoundException("JOB_POSTING_NOT_FOUND","The job posting was not found.");if(x.Version!=version)throw new ConflictException("JOB_POSTING_VERSION_CONFLICT","The job posting has been modified.");return x;}
    public static async Task SaveVersioned(IApplicationDbContext db,CancellationToken ct){try{await db.SaveChangesAsync(ct);}catch(DbUpdateConcurrencyException){throw new ConflictException("JOB_POSTING_VERSION_CONFLICT","The job posting has been modified.");}}
    public static async Task<JobPostingResult> GetAsync(IApplicationDbContext db,Guid id,CancellationToken ct){var x=await db.JobPostings.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new NotFoundException("JOB_POSTING_NOT_FOUND","The job posting was not found.");var sourceRows=await db.RawJobPostings.AsNoTracking().Where(y=>y.JobPostingId==id).OrderBy(y=>y.DiscoveredAt).ThenBy(y=>y.Id).ToListAsync(ct);var sources=sourceRows.Select(y=>new RawJobPostingResult(y.Id,y.Source,y.SourceExternalId,y.SourceUrl,y.SourceUrlHash,y.RawContent,y.ContentHash,y.CompanyTitleFingerprint,y.IngestionStatus,y.DuplicateOfRawJobPostingId,y.Metadata.RootElement.Clone(),y.DiscoveredAt,y.CreatedAt,y.UpdatedAt)).ToArray();var apps=await db.JobApplications.AsNoTracking().Where(y=>y.JobPostingId==id).OrderByDescending(y=>y.CreatedAt).ThenBy(y=>y.Id).Select(y=>new JobApplicationSummary(y.Id,y.Status,y.Channel,y.AppliedAt,y.LastActivityAt,y.Version)).ToListAsync(ct);return new(x.Id,x.CompanyName,x.PositionTitle,x.Location,x.EmploymentType,x.WorkplaceType,x.SalaryMinimum,x.SalaryMaximum,x.SalaryCurrency,x.SalaryPeriod,x.ExperienceRequirements,x.Description,x.TechnologyStack.RootElement.Clone(),x.ApplicationEmail,x.ApplicationUrl,x.VerificationStatus,x.SelectionStatus,x.ExpiresAt,x.VerifiedAt,x.ArchivedAt,x.Notes,x.Version,x.CreatedAt,x.UpdatedAt,sources,apps);}
}
internal static class JobHashing
{
    public static string NormalizeText(string value)=>value.Replace("\r\n","\n",StringComparison.Ordinal).Replace('\r','\n').Normalize(NormalizationForm.FormC).Trim();
    public static string NormalizeUrl(string value){var uri=new Uri(value.Trim(),UriKind.Absolute);var b=new UriBuilder(uri){Scheme=uri.Scheme.ToLowerInvariant(),Host=uri.Host.ToLowerInvariant()};if(uri.IsDefaultPort)b.Port=-1;return b.Uri.AbsoluteUri;}
    public static string Fingerprint(string company,string title)=>Sha256($"{NormalizeIdentity(company)}\n{NormalizeIdentity(title)}");
    private static string NormalizeIdentity(string value)=>Regex.Replace(value.Normalize(NormalizationForm.FormKC).Trim(),"\\s+"," ").ToLowerInvariant();
    public static string Sha256(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    public static string Sha256Bytes(ReadOnlySpan<byte> value)=>Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
}
internal static class JobText{public static string? TrimOrNull(string? value)=>string.IsNullOrWhiteSpace(value)?null:value.Trim();}
internal static class JobValidation
{
    public static readonly string[] Sources=["FACEBOOK","INSTAGRAM","TOPCV","VIETNAMWORKS","COMPANY_SITE","MANUAL","OTHER"];
    public static readonly string[] Verification=["PENDING","VERIFIED","UNVERIFIED","LIKELY_EXPIRED"];
    public static readonly string[] Selection=["PENDING_ANALYSIS","RECOMMENDED","APPROVED","SKIPPED"];
    public static readonly string[] ApplicationStatuses=["DRAFT","APPLIED","INTERVIEW","REJECTED","OFFER","WITHDRAWN"];
    public static readonly string[] Channels=["EMAIL","PLATFORM","MANUAL","OTHER"];
    public static List<ValidationFailure> Paging(int page,int size){var f=new List<ValidationFailure>();if(page<1)f.Add(new("page","Page must be at least 1."));if(size is <1 or >100)f.Add(new("pageSize","Page size must be between 1 and 100."));return f;}
    public static List<ValidationFailure> Posting(string? source,string? external,string? sourceUrl,string? raw,string company,string title,string location,string? employment,string? workplace,decimal? min,decimal? max,string? currency,string? period,string description,JsonElement stack,string? email,string? url,string? notes){var f=new List<ValidationFailure>();if(source is not null)RequiredClosed(f,"source",source,Sources);Optional(f,"sourceExternalId",external,500);if(sourceUrl is not null)Url(f,"sourceUrl",sourceUrl);if(raw is not null)Required(f,"rawContent",raw,200000);Required(f,"companyName",company,255);Required(f,"positionTitle",title,255);Required(f,"location",location,255);Optional(f,"employmentType",employment,50);Optional(f,"workplaceType",workplace,50);if(min<0)f.Add(new("salaryMinimum","Salary minimum cannot be negative."));if(max<0)f.Add(new("salaryMaximum","Salary maximum cannot be negative."));if(min.HasValue&&max.HasValue&&max<min)f.Add(new("salaryMaximum","Salary maximum must be at least salary minimum."));if(!string.IsNullOrWhiteSpace(currency)&&!Regex.IsMatch(currency.Trim(),"^[A-Za-z]{3}$"))f.Add(new("salaryCurrency","Currency must contain exactly three letters."));Optional(f,"salaryPeriod",period,30);Required(f,"description",description,200000);if(stack.ValueKind!=JsonValueKind.Array)f.Add(new("technologyStack","Technology stack must be an array."));else{var items=stack.EnumerateArray().ToArray();if(items.Length>100)f.Add(new("technologyStack","Technology stack cannot contain more than 100 items."));if(items.Any(x=>x.ValueKind!=JsonValueKind.String||string.IsNullOrWhiteSpace(x.GetString())||x.GetString()!.Length>100))f.Add(new("technologyStack","Technology stack items must be non-empty strings no longer than 100 characters."));}Email(f,"applicationEmail",email);if(url is not null)Url(f,"applicationUrl",url);Optional(f,"notes",notes,200000);return f;}
    public static void Required(List<ValidationFailure> f,string p,string? v,int max){if(string.IsNullOrWhiteSpace(v))f.Add(new(p,$"{p} is required."));else if(v.Length>max)f.Add(new(p,$"{p} cannot exceed {max} characters."));}
    public static void Optional(List<ValidationFailure> f,string p,string? v,int max){if(v?.Length>max)f.Add(new(p,$"{p} cannot exceed {max} characters."));}
    public static void Version(List<ValidationFailure> f,int v){if(v<1)f.Add(new("expectedVersion","Expected version must be greater than zero."));}
    public static void RequiredClosed(List<ValidationFailure> f,string p,string? v,string[] values){Required(f,p,v,50);if(!string.IsNullOrWhiteSpace(v)&&!values.Contains(v.Trim().ToUpperInvariant()))f.Add(new(p,$"{p} is invalid."));}
    public static void OptionalClosed(List<ValidationFailure> f,string p,string? v,string[] values){if(!string.IsNullOrWhiteSpace(v)&&!values.Contains(v.Trim().ToUpperInvariant()))f.Add(new(p,$"{p} is invalid."));}
    public static void Url(List<ValidationFailure> f,string p,string? v){if(!string.IsNullOrWhiteSpace(v)&&(!Uri.TryCreate(v.Trim(),UriKind.Absolute,out var u)||u.Scheme is not ("http" or "https")))f.Add(new(p,$"{p} must be an absolute HTTP or HTTPS URL."));}
    public static void Email(List<ValidationFailure> f,string p,string? v){if(!string.IsNullOrWhiteSpace(v)&&(!System.Net.Mail.MailAddress.TryCreate(v.Trim(),out var a)||!string.Equals(a.Address,v.Trim(),StringComparison.OrdinalIgnoreCase)))f.Add(new(p,$"{p} must be a valid email address."));}
}
