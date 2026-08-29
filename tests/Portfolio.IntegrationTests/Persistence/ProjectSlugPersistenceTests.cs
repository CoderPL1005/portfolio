using Microsoft.EntityFrameworkCore;
using Portfolio.Domain.Entities;
using Portfolio.Infrastructure.Persistence.Configurations;

namespace Portfolio.IntegrationTests.Persistence;

public sealed class ProjectSlugPersistenceTests
{
    [Fact]
    public async Task Production_project_configuration_allows_a_tracked_slug_to_change_and_preserves_id_relationships()
    {
        var options = new DbContextOptionsBuilder<ProjectPersistenceDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var context = new ProjectPersistenceDbContext(options);
        var projectId = Guid.NewGuid();
        var technologyId = Guid.NewGuid();
        context.Projects.Add(new Project
        {
            Id = projectId,
            Slug = "old-slug",
            Title = "Project",
            Status = "ACTIVE",
        });
        context.Technologies.Add(new Technology
        {
            Id = technologyId,
            Name = "Technology",
            Category = "Backend",
        });
        context.ProjectTechnologies.Add(new ProjectTechnology
        {
            ProjectId = projectId,
            TechnologyId = technologyId,
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var project = await context.Projects.SingleAsync(item => item.Id == projectId);
        project.Slug = "new-slug";
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        Assert.Equal("new-slug", (await context.Projects.SingleAsync(item => item.Id == projectId)).Slug);
        Assert.Equal(projectId, (await context.ProjectTechnologies.SingleAsync()).ProjectId);
    }

    private sealed class ProjectPersistenceDbContext(DbContextOptions<ProjectPersistenceDbContext> options)
        : DbContext(options)
    {
        public DbSet<Project> Projects => Set<Project>();
        public DbSet<Technology> Technologies => Set<Technology>();
        public DbSet<ProjectTechnology> ProjectTechnologies => Set<ProjectTechnology>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            new ProjectConfiguration().Configure(modelBuilder.Entity<Project>());
            new TechnologyConfiguration().Configure(modelBuilder.Entity<Technology>());
            new ProjectTechnologyConfiguration().Configure(modelBuilder.Entity<ProjectTechnology>());
        }
    }
}
