using Microsoft.EntityFrameworkCore;
using Portfolio.Domain.Entities;

namespace Portfolio.Application.Common.Abstractions.Persistence;

public interface IApplicationDbContext
{
    DbSet<AdminUser> AdminUsers { get; }
    DbSet<AdminRefreshToken> AdminRefreshTokens { get; }
    DbSet<MediaAsset> MediaAssets { get; }
    DbSet<Profile> Profiles { get; }
    DbSet<Experience> Experiences { get; }
    DbSet<Technology> Technologies { get; }
    DbSet<ExperienceTechnology> ExperienceTechnologies { get; }
    DbSet<Project> Projects { get; }
    DbSet<ProjectTechnology> ProjectTechnologies { get; }
    DbSet<ProjectSection> ProjectSections { get; }
    DbSet<ProjectMedia> ProjectMedia { get; }
    DbSet<Skill> Skills { get; }
    DbSet<Education> Educations { get; }
    DbSet<Training> Trainings { get; }
    DbSet<Certificate> Certificates { get; }
    DbSet<JourneyItem> JourneyItems { get; }
    DbSet<SocialLink> SocialLinks { get; }
    DbSet<SiteSetting> SiteSettings { get; }
    DbSet<AgentSetting> AgentSettings { get; }
    DbSet<KnowledgeDocument> KnowledgeDocuments { get; }
    DbSet<KnowledgeChunk> KnowledgeChunks { get; }
    DbSet<ChatSession> ChatSessions { get; }
    DbSet<ChatMessage> ChatMessages { get; }
    DbSet<ChatMessageSource> ChatMessageSources { get; }
    DbSet<ChatMessageFeedback> ChatMessageFeedback { get; }
    DbSet<ChatUsageDaily> ChatUsageDaily { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
