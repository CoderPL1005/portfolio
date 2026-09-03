using Microsoft.EntityFrameworkCore;
using Portfolio.Application.Common.Abstractions.Persistence;
using Portfolio.Domain.Entities;

namespace Portfolio.UnitTests.PortfolioContent;

internal sealed class ContentTestDbContext(DbContextOptions<ContentTestDbContext> options)
    : DbContext(options), IApplicationDbContext
{
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

    DbSet<AdminUser> IApplicationDbContext.AdminUsers => throw new NotSupportedException();
    DbSet<AdminRefreshToken> IApplicationDbContext.AdminRefreshTokens => throw new NotSupportedException();

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
        modelBuilder.Entity<ChatSession>().Property(item => item.Metadata).HasConversion(
            value => value.RootElement.GetRawText(), value => System.Text.Json.JsonDocument.Parse(
                value, default(System.Text.Json.JsonDocumentOptions)));
    }
}
