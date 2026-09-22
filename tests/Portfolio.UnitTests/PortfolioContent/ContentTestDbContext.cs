using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Portfolio.Application.Common.Abstractions.Persistence;
using Portfolio.Domain.Entities;

namespace Portfolio.UnitTests.PortfolioContent;

internal sealed class ContentTestDbContext(DbContextOptions<ContentTestDbContext> options)
    : DbContext(options), IApplicationDbContext
{
    public bool FailSaveChanges { get; set; }
    public int ConcurrencyFailuresRemaining { get; set; }
    public DbSet<MediaAsset> MediaAssets => Set<MediaAsset>();
    public DbSet<Profile> Profiles => Set<Profile>();
    public DbSet<Experience> Experiences => Set<Experience>();
    public DbSet<Technology> Technologies => Set<Technology>();
    public DbSet<ExperienceTechnology> ExperienceTechnologies => Set<ExperienceTechnology>();
    public DbSet<Education> Educations => Set<Education>();
    public DbSet<Training> Trainings => Set<Training>();
    public DbSet<Certificate> Certificates => Set<Certificate>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProjectTechnology> ProjectTechnologies => Set<ProjectTechnology>();
    public DbSet<ProjectSection> ProjectSections => Set<ProjectSection>();
    public DbSet<ProjectMedia> ProjectMedia => Set<ProjectMedia>();
    public DbSet<Skill> Skills => Set<Skill>();
    public DbSet<JourneyItem> JourneyItems => Set<JourneyItem>();
    public DbSet<SocialLink> SocialLinks => Set<SocialLink>();
    public DbSet<SiteSetting> SiteSettings => Set<SiteSetting>();
    public DbSet<KnowledgeDocument> KnowledgeDocuments => Set<KnowledgeDocument>();
    public DbSet<AgentSetting> AgentSettings => Set<AgentSetting>();
    public DbSet<KnowledgeChunk> KnowledgeChunks => Set<KnowledgeChunk>();
    public DbSet<ChatSession> ChatSessions => Set<ChatSession>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<ChatMessageSource> ChatMessageSources => Set<ChatMessageSource>();
    public DbSet<ChatMessageFeedback> ChatMessageFeedback => Set<ChatMessageFeedback>();
    public DbSet<ChatUsageDaily> ChatUsageDaily => Set<ChatUsageDaily>();
    public DbSet<RawJobPosting> RawJobPostings => Set<RawJobPosting>();
    public DbSet<RawJobPostingAttachment> RawJobPostingAttachments => Set<RawJobPostingAttachment>();
    public DbSet<JobPosting> JobPostings => Set<JobPosting>();
    public DbSet<JobApplication> JobApplications => Set<JobApplication>();
    public DbSet<JobApplicationEvent> JobApplicationEvents => Set<JobApplicationEvent>();
    public DbSet<JobApplicationDocument> JobApplicationDocuments => Set<JobApplicationDocument>();
    public DbSet<PushSubscription> PushSubscriptions => Set<PushSubscription>();
    public DbSet<CandidateJobPreferences> CandidateJobPreferences => Set<CandidateJobPreferences>();
    public DbSet<CanonicalCv> CanonicalCvs => Set<CanonicalCv>();

    DbSet<AdminUser> IApplicationDbContext.AdminUsers => throw new NotSupportedException();
    DbSet<AdminRefreshToken> IApplicationDbContext.AdminRefreshTokens => throw new NotSupportedException();

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        if (FailSaveChanges) throw new DbUpdateException("Simulated failure");
        if (ConcurrencyFailuresRemaining > 0)
        {
            ConcurrencyFailuresRemaining--;
            throw new DbUpdateConcurrencyException("Simulated concurrency failure");
        }
        return base.SaveChangesAsync(cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Ignore<AdminUser>(); modelBuilder.Ignore<AdminRefreshToken>();

        modelBuilder.Entity<MediaAsset>().HasKey(item => item.Id);
        modelBuilder.Entity<Profile>().HasKey(item => item.Id);
        modelBuilder.Entity<Profile>().HasOne(item => item.ProfileImage).WithMany().HasForeignKey(item => item.ProfileImageId);
        modelBuilder.Entity<Profile>().HasOne(item => item.CvMedia).WithMany().HasForeignKey(item => item.CvMediaId);
        modelBuilder.Entity<Experience>().HasKey(item => item.Id);
        modelBuilder.Entity<Technology>().HasKey(item => item.Id);
        modelBuilder.Entity<ExperienceTechnology>().HasKey(item => new { item.ExperienceId, item.TechnologyId });
        modelBuilder.Entity<ExperienceTechnology>().HasOne(item => item.Experience).WithMany().HasForeignKey(item => item.ExperienceId);
        modelBuilder.Entity<ExperienceTechnology>().HasOne(item => item.Technology).WithMany().HasForeignKey(item => item.TechnologyId);
        modelBuilder.Entity<Education>().HasKey(item => item.Id);
        modelBuilder.Entity<Training>().HasKey(item => item.Id);
        modelBuilder.Entity<Certificate>().HasKey(item => item.Id);
        modelBuilder.Entity<Certificate>().HasOne(item => item.CertificateMedia).WithMany().HasForeignKey(item => item.CertificateMediaId);
        modelBuilder.Entity<Project>().HasKey(item => item.Id);
        modelBuilder.Entity<Project>().HasOne(item => item.ThumbnailMedia).WithMany().HasForeignKey(item => item.ThumbnailMediaId);
        modelBuilder.Entity<ProjectTechnology>().HasKey(item => new { item.ProjectId, item.TechnologyId });
        modelBuilder.Entity<ProjectTechnology>().HasOne(item => item.Project).WithMany().HasForeignKey(item => item.ProjectId);
        modelBuilder.Entity<ProjectTechnology>().HasOne(item => item.Technology).WithMany().HasForeignKey(item => item.TechnologyId);
        modelBuilder.Entity<ProjectSection>().HasKey(item => item.Id);
        modelBuilder.Entity<ProjectSection>().Property(item => item.ContentJson)
            .HasConversion(
                value => value.RootElement.GetRawText(),
                value => System.Text.Json.JsonDocument.Parse(
                    value, default(System.Text.Json.JsonDocumentOptions)));
        modelBuilder.Entity<ProjectSection>().HasOne(item => item.Project).WithMany().HasForeignKey(item => item.ProjectId);
        modelBuilder.Entity<ProjectMedia>().HasKey(item => item.Id);
        modelBuilder.Entity<ProjectMedia>().HasOne(item => item.Project).WithMany().HasForeignKey(item => item.ProjectId);
        modelBuilder.Entity<ProjectMedia>().HasOne(item => item.MediaAsset).WithMany().HasForeignKey(item => item.MediaAssetId);
        modelBuilder.Entity<Skill>().HasKey(item => item.Id);
        modelBuilder.Entity<Skill>().HasOne(item => item.Technology).WithMany().HasForeignKey(item => item.TechnologyId);
        modelBuilder.Entity<JourneyItem>().HasKey(item => item.Id);
        modelBuilder.Entity<SocialLink>().HasKey(item => item.Id);
        modelBuilder.Entity<SiteSetting>().HasKey(item => item.Key);
        modelBuilder.Entity<SiteSetting>().Property(item => item.Value).HasConversion(
            value => value.RootElement.GetRawText(), value => System.Text.Json.JsonDocument.Parse(
                value, default(System.Text.Json.JsonDocumentOptions)));
        modelBuilder.Entity<KnowledgeDocument>().HasKey(item => item.Id);
        modelBuilder.Entity<KnowledgeDocument>().HasAlternateKey(item => item.SourceKey);
        modelBuilder.Entity<AgentSetting>().HasKey(item => item.Id);
        modelBuilder.Entity<KnowledgeChunk>().HasKey(item => item.Id);
        modelBuilder.Entity<KnowledgeChunk>().Ignore(item => item.Embedding);
        modelBuilder.Entity<KnowledgeChunk>().Property(item=>item.Metadata).HasConversion(value=>value.RootElement.GetRawText(),value=>System.Text.Json.JsonDocument.Parse(value,default(System.Text.Json.JsonDocumentOptions)));
        modelBuilder.Entity<KnowledgeChunk>().HasOne(item=>item.KnowledgeDocument).WithMany().HasForeignKey(item=>item.KnowledgeDocumentId);
        modelBuilder.Entity<KnowledgeDocument>().Property(item => item.Metadata).HasConversion(
            value => value.RootElement.GetRawText(), value => System.Text.Json.JsonDocument.Parse(
                value, default(System.Text.Json.JsonDocumentOptions)));
        modelBuilder.Entity<ChatSession>().HasKey(item => item.Id);
        modelBuilder.Entity<ChatMessage>().HasKey(item=>item.Id);modelBuilder.Entity<ChatMessage>().HasOne(item=>item.ChatSession).WithMany().HasForeignKey(item=>item.ChatSessionId);
        modelBuilder.Entity<ChatMessageSource>().HasKey(item=>item.Id);modelBuilder.Entity<ChatMessageSource>().HasOne(item=>item.ChatMessage).WithMany().HasForeignKey(item=>item.ChatMessageId);modelBuilder.Entity<ChatMessageSource>().HasOne(item=>item.KnowledgeChunk).WithMany().HasForeignKey(item=>item.KnowledgeChunkId);
        modelBuilder.Entity<ChatMessageFeedback>().HasKey(item=>item.Id);modelBuilder.Entity<ChatMessageFeedback>().HasOne(item=>item.ChatMessage).WithMany().HasForeignKey(item=>item.ChatMessageId);
        modelBuilder.Entity<ChatUsageDaily>().HasKey(item => new { item.UsageDate, item.VisitorKey });
        modelBuilder.Entity<RawJobPosting>().HasKey(item => item.Id);
        modelBuilder.Entity<RawJobPosting>().Property(item => item.Version).IsConcurrencyToken();
        modelBuilder.Entity<RawJobPosting>().HasOne(item => item.JobPosting).WithMany(item => item.RawJobPostings).HasForeignKey(item => item.JobPostingId);
        modelBuilder.Entity<RawJobPosting>().HasOne(item => item.DuplicateOfRawJobPosting).WithMany(item => item.DuplicateRawJobPostings).HasForeignKey(item => item.DuplicateOfRawJobPostingId);
        modelBuilder.Entity<RawJobPosting>().Property(item => item.Metadata).HasConversion(value => value.RootElement.GetRawText(), value => System.Text.Json.JsonDocument.Parse(value, default(System.Text.Json.JsonDocumentOptions)));
        modelBuilder.Entity<RawJobPostingAttachment>().HasKey(item => item.Id);
        modelBuilder.Entity<RawJobPostingAttachment>().HasOne(item => item.RawJobPosting).WithMany(item => item.Attachments).HasForeignKey(item => item.RawJobPostingId);
        modelBuilder.Entity<RawJobPostingAttachment>().HasIndex(item => new { item.RawJobPostingId, item.TelegramMessageId }).IsUnique();
        modelBuilder.Entity<JobPosting>().HasKey(item => item.Id);
        modelBuilder.Entity<JobPosting>().Property(item => item.TechnologyStack).HasConversion(value => value.RootElement.GetRawText(), value => System.Text.Json.JsonDocument.Parse(value, default(System.Text.Json.JsonDocumentOptions)));
        modelBuilder.Entity<JobApplication>().HasKey(item => item.Id);
        modelBuilder.Entity<JobApplication>().HasOne(item => item.JobPosting).WithMany(item => item.JobApplications).HasForeignKey(item => item.JobPostingId);
        modelBuilder.Entity<JobApplication>().HasIndex(item => item.JobPostingId).IsUnique();
        modelBuilder.Entity<JobApplicationEvent>().HasKey(item => item.Id);
        modelBuilder.Entity<JobApplicationEvent>().HasOne(item => item.JobApplication).WithMany(item => item.Events).HasForeignKey(item => item.JobApplicationId);
        modelBuilder.Entity<JobApplicationEvent>().HasOne(item => item.ActorAdminUser).WithMany().HasForeignKey(item => item.ActorAdminUserId);
        modelBuilder.Entity<JobApplicationEvent>().Property(item => item.Metadata).HasConversion(value => value.RootElement.GetRawText(), value => System.Text.Json.JsonDocument.Parse(value, default(System.Text.Json.JsonDocumentOptions)));
        modelBuilder.Entity<JobApplicationDocument>().HasKey(item => item.Id);
        modelBuilder.Entity<JobApplicationDocument>().HasOne(item => item.JobApplication).WithMany(item => item.Documents).HasForeignKey(item => item.JobApplicationId);
        modelBuilder.Entity<JobApplicationDocument>().Property(item => item.Metadata).HasConversion(value => value.RootElement.GetRawText(), value => System.Text.Json.JsonDocument.Parse(value, default(System.Text.Json.JsonDocumentOptions)));
        modelBuilder.Entity<PushSubscription>().HasKey(item => item.Id);
        modelBuilder.Entity<PushSubscription>().HasIndex(item => item.Endpoint).IsUnique();
        modelBuilder.Entity<CandidateJobPreferences>().HasKey(item => item.Id);
        modelBuilder.Entity<CandidateJobPreferences>().HasIndex(item => item.SingletonKey).IsUnique();
        modelBuilder.Entity<CandidateJobPreferences>().Property(item => item.Version).IsConcurrencyToken();
        modelBuilder.Entity<CanonicalCv>().HasKey(item => item.Id);
        modelBuilder.Entity<CanonicalCv>().HasIndex(item => item.SingletonKey).IsUnique();
        modelBuilder.Entity<CanonicalCv>().HasIndex(item => item.StorageKey).IsUnique();
        modelBuilder.Entity<CanonicalCv>().Property(item => item.Version).IsConcurrencyToken();
        foreach (var property in new[] { nameof(Portfolio.Domain.Entities.CandidateJobPreferences.TargetRoles), nameof(Portfolio.Domain.Entities.CandidateJobPreferences.PreferredTechnologies), nameof(Portfolio.Domain.Entities.CandidateJobPreferences.AcceptableLocations), nameof(Portfolio.Domain.Entities.CandidateJobPreferences.WorkplaceTypes), nameof(Portfolio.Domain.Entities.CandidateJobPreferences.EmploymentTypes) })
            modelBuilder.Entity<CandidateJobPreferences>().Property<JsonDocument>(property).HasConversion(value => value.RootElement.GetRawText(), value => JsonDocument.Parse(value, default(JsonDocumentOptions)));
        modelBuilder.Entity<ChatSession>().Property(item => item.Metadata).HasConversion(
            value => value.RootElement.GetRawText(), value => System.Text.Json.JsonDocument.Parse(
                value, default(System.Text.Json.JsonDocumentOptions)));
    }
}
