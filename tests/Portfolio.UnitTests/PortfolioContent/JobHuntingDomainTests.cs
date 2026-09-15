using Portfolio.Domain.Constants;
using Portfolio.Domain.Entities;

namespace Portfolio.UnitTests.PortfolioContent;

public sealed class JobHuntingDomainTests
{
    [Fact]
    public void Entities_use_separate_state_defaults()
    {
        var raw = new RawJobPosting();
        var posting = new JobPosting();
        var application = new JobApplication();
        var applicationEvent = new JobApplicationEvent();

        Assert.Equal(RawJobPostingIngestionStatuses.Received, raw.IngestionStatus);
        Assert.Equal(JobPostingVerificationStatuses.Pending, posting.VerificationStatus);
        Assert.Equal(JobPostingSelectionStatuses.PendingAnalysis, posting.SelectionStatus);
        Assert.Equal(JobApplicationStatuses.Draft, application.Status);
        Assert.Equal(JobApplicationEventTypes.Created, applicationEvent.EventType);
        Assert.Equal(JobApplicationEventActorTypes.System, applicationEvent.ActorType);
        Assert.Null(raw.IngestionKey);
    }

    [Fact]
    public void Ingestion_identity_is_independent_from_source_identity_and_content()
    {
        var first = new RawJobPosting
        {
            Source = RawJobPostingSources.Facebook,
            SourceExternalId = "facebook-post-42",
            IngestionKey = "telegram:100:1",
            ContentHash = "same-content",
        };
        var second = new RawJobPosting
        {
            Source = RawJobPostingSources.Facebook,
            SourceExternalId = "facebook-post-43",
            IngestionKey = "telegram:100:2",
            ContentHash = "same-content",
        };

        Assert.Equal("facebook-post-42", first.SourceExternalId);
        Assert.Equal("telegram:100:1", first.IngestionKey);
        Assert.Equal(first.ContentHash, second.ContentHash);
        Assert.NotEqual(first.IngestionKey, second.IngestionKey);
    }

    [Fact]
    public void Persistence_values_match_the_approved_closed_sets()
    {
        Assert.Equal(
            ["FACEBOOK", "INSTAGRAM", "TOPCV", "VIETNAMWORKS", "COMPANY_SITE", "MANUAL", "OTHER"],
            ValuesOf(typeof(RawJobPostingSources)));
        Assert.Equal(
            ["RECEIVED", "NORMALIZED", "DUPLICATE", "REJECTED"],
            ValuesOf(typeof(RawJobPostingIngestionStatuses)));
        Assert.Equal(
            ["PENDING", "VERIFIED", "UNVERIFIED", "LIKELY_EXPIRED"],
            ValuesOf(typeof(JobPostingVerificationStatuses)));
        Assert.Equal(
            ["PENDING_ANALYSIS", "RECOMMENDED", "APPROVED", "SKIPPED"],
            ValuesOf(typeof(JobPostingSelectionStatuses)));
        Assert.Equal(
            ["DRAFT", "APPLIED", "INTERVIEW", "REJECTED", "OFFER", "WITHDRAWN"],
            ValuesOf(typeof(JobApplicationStatuses)));
        Assert.Equal(["EMAIL", "PLATFORM", "MANUAL", "OTHER"], ValuesOf(typeof(JobApplicationChannels)));
        Assert.Equal(["ADMIN", "SYSTEM", "TELEGRAM", "EMAIL_CONNECTOR"], ValuesOf(typeof(JobApplicationEventActorTypes)));
        Assert.Equal(
            ["CREATED", "STATUS_CHANGED", "NOTE_ADDED", "DOCUMENT_ATTACHED", "DOCUMENT_REMOVED"],
            ValuesOf(typeof(JobApplicationEventTypes)));
    }

    private static string[] ValuesOf(Type type) => type
        .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
        .OrderBy(field => field.MetadataToken)
        .Select(field => (string)field.GetRawConstantValue()!)
        .ToArray();
}
