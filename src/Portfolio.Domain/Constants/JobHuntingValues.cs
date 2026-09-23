namespace Portfolio.Domain.Constants;

public static class RawJobPostingSources
{
    public const string Facebook = "FACEBOOK";
    public const string Instagram = "INSTAGRAM";
    public const string TopCv = "TOPCV";
    public const string VietnamWorks = "VIETNAMWORKS";
    public const string CompanySite = "COMPANY_SITE";
    public const string Manual = "MANUAL";
    public const string Other = "OTHER";
}

public static class RawJobPostingIngestionStatuses
{
    public const string Received = "RECEIVED";
    public const string Normalized = "NORMALIZED";
    public const string Duplicate = "DUPLICATE";
    public const string Rejected = "REJECTED";
}

public static class RawJobPostingAttachmentTypes
{
    public const string Image = "IMAGE";
}

public static class JobPostingVerificationStatuses
{
    public const string Pending = "PENDING";
    public const string Verified = "VERIFIED";
    public const string Unverified = "UNVERIFIED";
    public const string LikelyExpired = "LIKELY_EXPIRED";
}

public static class JobPostingSelectionStatuses
{
    public const string PendingAnalysis = "PENDING_ANALYSIS";
    public const string Recommended = "RECOMMENDED";
    public const string Approved = "APPROVED";
    public const string Skipped = "SKIPPED";
}

public static class JobApplicationStatuses
{
    public const string Draft = "DRAFT";
    public const string Applied = "APPLIED";
    public const string Interview = "INTERVIEW";
    public const string Rejected = "REJECTED";
    public const string Offer = "OFFER";
    public const string Withdrawn = "WITHDRAWN";
}

public static class JobApplicationPackageStatuses
{
    public const string Draft = "DRAFT";
    public const string Finalized = "FINALIZED";
}

public static class JobApplicationChannels
{
    public const string Email = "EMAIL";
    public const string Platform = "PLATFORM";
    public const string Manual = "MANUAL";
    public const string Other = "OTHER";
}

public static class JobApplicationEventActorTypes
{
    public const string Admin = "ADMIN";
    public const string System = "SYSTEM";
    public const string Telegram = "TELEGRAM";
    public const string EmailConnector = "EMAIL_CONNECTOR";
}

public static class JobApplicationEventTypes
{
    public const string Created = "CREATED";
    public const string StatusChanged = "STATUS_CHANGED";
    public const string NoteAdded = "NOTE_ADDED";
    public const string DocumentAttached = "DOCUMENT_ATTACHED";
    public const string DocumentRemoved = "DOCUMENT_REMOVED";
    public const string PackageFinalized = "PACKAGE_FINALIZED";
}

public static class SubmissionProviders
{
    public const string Email = "EMAIL";
    public const string CompanySite = "COMPANY_SITE";
    public const string TopCv = "TOPCV";
    public const string VietnamWorks = "VIETNAMWORKS";
    public const string Manual = "MANUAL";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    { Email, CompanySite, TopCv, VietnamWorks, Manual };
}

public static class SubmissionAttemptStatuses
{
    public const string Created = "CREATED";
    public const string Approved = "APPROVED";
    public const string Submitting = "SUBMITTING";
    public const string Succeeded = "SUCCEEDED";
    public const string Failed = "FAILED";
    public const string Unknown = "UNKNOWN";
}

public static class SubmissionOutcomes
{
    public const string Success = "SUCCESS";
    public const string Failure = "FAILURE";
    public const string Unknown = "UNKNOWN";
}
