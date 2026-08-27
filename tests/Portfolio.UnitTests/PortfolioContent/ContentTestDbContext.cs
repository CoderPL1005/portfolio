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

    DbSet<AdminUser> IApplicationDbContext.AdminUsers => throw new NotSupportedException();
    DbSet<AdminRefreshToken> IApplicationDbContext.AdminRefreshTokens => throw new NotSupportedException();
    DbSet<Project> IApplicationDbContext.Projects => throw new NotSupportedException();
    DbSet<ProjectTechnology> IApplicationDbContext.ProjectTechnologies => throw new NotSupportedException();
    DbSet<ProjectSection> IApplicationDbContext.ProjectSections => throw new NotSupportedException();
    DbSet<ProjectMedia> IApplicationDbContext.ProjectMedia => throw new NotSupportedException();
    DbSet<Skill> IApplicationDbContext.Skills => throw new NotSupportedException();
    DbSet<JourneyItem> IApplicationDbContext.JourneyItems => throw new NotSupportedException();
    DbSet<SocialLink> IApplicationDbContext.SocialLinks => throw new NotSupportedException();
    DbSet<SiteSetting> IApplicationDbContext.SiteSettings => throw new NotSupportedException();
    DbSet<ContactMessage> IApplicationDbContext.ContactMessages => throw new NotSupportedException();
    DbSet<AgentSetting> IApplicationDbContext.AgentSettings => throw new NotSupportedException();
    DbSet<KnowledgeDocument> IApplicationDbContext.KnowledgeDocuments => throw new NotSupportedException();
    DbSet<KnowledgeChunk> IApplicationDbContext.KnowledgeChunks => throw new NotSupportedException();
    DbSet<ChatSession> IApplicationDbContext.ChatSessions => throw new NotSupportedException();
    DbSet<ChatMessage> IApplicationDbContext.ChatMessages => throw new NotSupportedException();
    DbSet<ChatMessageSource> IApplicationDbContext.ChatMessageSources => throw new NotSupportedException();
    DbSet<ChatMessageFeedback> IApplicationDbContext.ChatMessageFeedback => throw new NotSupportedException();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Ignore<AdminUser>(); modelBuilder.Ignore<AdminRefreshToken>();
        modelBuilder.Ignore<Project>(); modelBuilder.Ignore<ProjectTechnology>();
        modelBuilder.Ignore<ProjectSection>(); modelBuilder.Ignore<ProjectMedia>();
        modelBuilder.Ignore<Skill>(); modelBuilder.Ignore<JourneyItem>();
        modelBuilder.Ignore<SocialLink>(); modelBuilder.Ignore<SiteSetting>();
        modelBuilder.Ignore<ContactMessage>(); modelBuilder.Ignore<AgentSetting>();
        modelBuilder.Ignore<KnowledgeDocument>(); modelBuilder.Ignore<KnowledgeChunk>();
        modelBuilder.Ignore<ChatSession>(); modelBuilder.Ignore<ChatMessage>();
        modelBuilder.Ignore<ChatMessageSource>(); modelBuilder.Ignore<ChatMessageFeedback>();

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
    }
}
