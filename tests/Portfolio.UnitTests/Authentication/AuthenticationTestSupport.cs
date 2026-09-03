using Portfolio.Application.Common.Abstractions.Authentication;
using Microsoft.EntityFrameworkCore;
using Portfolio.Application.Common.Abstractions.Persistence;
using Portfolio.Domain.Entities;

namespace Portfolio.UnitTests.Authentication;

internal sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => utcNow;
}

internal sealed class FakeJwtTokenService(DateTimeOffset now) : IJwtTokenService
{
    private int _counter;

    public AccessToken CreateAccessToken(Guid adminUserId, string email) =>
        new($"access-{adminUserId}", 900, now.AddMinutes(15));

    public RefreshToken CreateRefreshToken()
    {
        var value = $"raw-refresh-{Interlocked.Increment(ref _counter)}";
        return new RefreshToken(value, now.AddDays(7));
    }

    public string HashRefreshToken(string rawToken) => $"HASH:{rawToken}";
}

internal sealed class FakeCurrentUser(Guid? adminUserId, string? email = null) : ICurrentUser
{
    public bool IsAuthenticated => adminUserId.HasValue;
    public Guid? AdminUserId => adminUserId;
    public string? Email => email;
}

internal sealed class FakeRefreshTokenStore(
    Func<RefreshRotationResult>? rotate = null) : IRefreshTokenStore
{
    private readonly Func<RefreshRotationResult> _rotate = rotate ??
        (() => new RefreshRotationResult(RefreshRotationStatus.Succeeded, Guid.NewGuid(), "admin@example.com"));

    public int RotationCalls;
    public int RevocationCalls;
    public string? LastCurrentHash { get; private set; }
    public string? LastReplacementHash { get; private set; }

    public Task<RefreshRotationResult> RotateAsync(
        string currentTokenHash,
        Guid replacementTokenId,
        string replacementTokenHash,
        DateTimeOffset replacementExpiresAt,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref RotationCalls);
        LastCurrentHash = currentTokenHash;
        LastReplacementHash = replacementTokenHash;
        return Task.FromResult(_rotate());
    }

    public Task RevokeAsync(
        string tokenHash,
        Guid adminUserId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref RevocationCalls);
        LastCurrentHash = tokenHash;
        return Task.CompletedTask;
    }
}

internal sealed class AuthTestDbContext(DbContextOptions<AuthTestDbContext> options)
    : DbContext(options), IApplicationDbContext
{
    public DbSet<AdminUser> AdminUsers => Set<AdminUser>();
    public DbSet<AdminRefreshToken> AdminRefreshTokens => Set<AdminRefreshToken>();

    DbSet<MediaAsset> IApplicationDbContext.MediaAssets => throw new NotSupportedException();
    DbSet<Profile> IApplicationDbContext.Profiles => throw new NotSupportedException();
    DbSet<Experience> IApplicationDbContext.Experiences => throw new NotSupportedException();
    DbSet<Technology> IApplicationDbContext.Technologies => throw new NotSupportedException();
    DbSet<ExperienceTechnology> IApplicationDbContext.ExperienceTechnologies => throw new NotSupportedException();
    DbSet<Project> IApplicationDbContext.Projects => throw new NotSupportedException();
    DbSet<ProjectTechnology> IApplicationDbContext.ProjectTechnologies => throw new NotSupportedException();
    DbSet<ProjectSection> IApplicationDbContext.ProjectSections => throw new NotSupportedException();
    DbSet<ProjectMedia> IApplicationDbContext.ProjectMedia => throw new NotSupportedException();
    DbSet<Skill> IApplicationDbContext.Skills => throw new NotSupportedException();
    DbSet<Education> IApplicationDbContext.Educations => throw new NotSupportedException();
    DbSet<Training> IApplicationDbContext.Trainings => throw new NotSupportedException();
    DbSet<Certificate> IApplicationDbContext.Certificates => throw new NotSupportedException();
    DbSet<JourneyItem> IApplicationDbContext.JourneyItems => throw new NotSupportedException();
    DbSet<SocialLink> IApplicationDbContext.SocialLinks => throw new NotSupportedException();
    DbSet<SiteSetting> IApplicationDbContext.SiteSettings => throw new NotSupportedException();
    DbSet<AgentSetting> IApplicationDbContext.AgentSettings => throw new NotSupportedException();
    DbSet<KnowledgeDocument> IApplicationDbContext.KnowledgeDocuments => throw new NotSupportedException();
    DbSet<KnowledgeChunk> IApplicationDbContext.KnowledgeChunks => throw new NotSupportedException();
    DbSet<ChatSession> IApplicationDbContext.ChatSessions => throw new NotSupportedException();
    DbSet<ChatMessage> IApplicationDbContext.ChatMessages => throw new NotSupportedException();
    DbSet<ChatMessageSource> IApplicationDbContext.ChatMessageSources => throw new NotSupportedException();
    DbSet<ChatMessageFeedback> IApplicationDbContext.ChatMessageFeedback => throw new NotSupportedException();
    DbSet<ChatUsageDaily> IApplicationDbContext.ChatUsageDaily => throw new NotSupportedException();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Ignore<MediaAsset>();
        modelBuilder.Ignore<Profile>();
        modelBuilder.Ignore<Experience>();
        modelBuilder.Ignore<Technology>();
        modelBuilder.Ignore<ExperienceTechnology>();
        modelBuilder.Ignore<Project>();
        modelBuilder.Ignore<ProjectTechnology>();
        modelBuilder.Ignore<ProjectSection>();
        modelBuilder.Ignore<ProjectMedia>();
        modelBuilder.Ignore<Skill>();
        modelBuilder.Ignore<Education>();
        modelBuilder.Ignore<Training>();
        modelBuilder.Ignore<Certificate>();
        modelBuilder.Ignore<JourneyItem>();
        modelBuilder.Ignore<SocialLink>();
        modelBuilder.Ignore<SiteSetting>();
        modelBuilder.Ignore<AgentSetting>();
        modelBuilder.Ignore<KnowledgeDocument>();
        modelBuilder.Ignore<KnowledgeChunk>();
        modelBuilder.Ignore<ChatSession>();
        modelBuilder.Ignore<ChatMessage>();
        modelBuilder.Ignore<ChatMessageSource>();
        modelBuilder.Ignore<ChatMessageFeedback>();
        modelBuilder.Ignore<ChatUsageDaily>();
        modelBuilder.Entity<AdminUser>().HasKey(admin => admin.Id);
        modelBuilder.Entity<AdminRefreshToken>().HasKey(token => token.Id);
        modelBuilder.Entity<AdminRefreshToken>()
            .HasOne(token => token.AdminUser)
            .WithMany()
            .HasForeignKey(token => token.AdminUserId);
    }
}
