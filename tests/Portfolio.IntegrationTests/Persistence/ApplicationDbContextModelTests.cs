using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector.EntityFrameworkCore;
using Portfolio.Application.Common.Abstractions.Persistence;
using Portfolio.Domain.Entities;
using Portfolio.Infrastructure.Persistence;

namespace Portfolio.IntegrationTests.Persistence;

public sealed class ApplicationDbContextModelTests
{
    private static readonly string[] ExpectedTables =
    [
        "admin_users", "admin_refresh_tokens", "media_assets", "profiles", "experiences",
        "technologies", "experience_technologies", "projects", "project_technologies",
        "project_sections", "project_media", "skills", "educations", "trainings", "certificates",
        "journey_items", "social_links", "site_settings", "agent_settings",
        "knowledge_documents", "knowledge_chunks", "chat_sessions", "chat_messages",
        "chat_message_sources", "chat_message_feedback", "chat_usage_daily"
    ];

    [Fact]
    public void Model_contains_all_26_expected_tables_and_excludes_contact_messages()
    {
        using var context = CreateContext();
        var tables = context.Model.GetEntityTypes().Select(entity => entity.GetTableName()).Order().ToArray();

        Assert.Equal(ExpectedTables.Order(), tables);
        Assert.DoesNotContain("contact_messages", tables);
        Assert.Null(context.Model.FindEntityType("Portfolio.Domain.Entities.ContactMessage"));
    }

    [Fact]
    public void Application_context_contract_exposes_all_26_sets()
    {
        var dbSetCount = typeof(IApplicationDbContext).GetProperties()
            .Count(property => property.PropertyType.IsGenericType &&
                property.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>));

        Assert.Equal(26, dbSetCount);
    }

    [Fact]
    public void Seed_helper_fields_do_not_create_database_columns()
    {
        using var context = CreateContext();

        Assert.DoesNotContain(context.Model.GetEntityTypes().SelectMany(entity => entity.GetProperties()),
            property => property.Name.Equals("SeedKey", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Model_contains_expected_relational_constraints()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;

        Assert.Equal(19, model.GetEntityTypes().SelectMany(entity => entity.GetForeignKeys()).Count());
        Assert.Equal(35, model.GetEntityTypes().SelectMany(entity => entity.GetCheckConstraints()).Count());
        Assert.Equal(22, model.GetEntityTypes().SelectMany(entity => entity.GetIndexes()).Count());

        var experienceTechnology = model.FindEntityType(typeof(ExperienceTechnology))!;
        Assert.Equal(2, experienceTechnology.FindPrimaryKey()!.Properties.Count);
        var projectTechnology = model.FindEntityType(typeof(ProjectTechnology))!;
        Assert.Equal(2, projectTechnology.FindPrimaryKey()!.Properties.Count);
        var project = model.FindEntityType(typeof(Project))!;
        Assert.Equal(new[] { nameof(Project.Id) }, project.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Single(project.GetKeys());
        Assert.DoesNotContain(project.GetKeys(), key => key.Properties.Any(property => property.Name == nameof(Project.Slug)));
        var projectDependents = model.GetEntityTypes().SelectMany(entity => entity.GetForeignKeys())
            .Where(foreignKey => foreignKey.PrincipalEntityType == project).ToArray();
        Assert.Equal(3, projectDependents.Length);
        Assert.All(projectDependents, foreignKey =>
            Assert.Equal(new[] { nameof(Project.Id) }, foreignKey.PrincipalKey.Properties.Select(property => property.Name)));
        var socialLink = model.FindEntityType(typeof(SocialLink))!;
        Assert.Equal(new[] { nameof(SocialLink.Id) }, socialLink.FindPrimaryKey()!.Properties.Select(property => property.Name));
        var usage = model.FindEntityType(typeof(ChatUsageDaily))!;
        Assert.Equal(
            new[] { nameof(ChatUsageDaily.UsageDate), nameof(ChatUsageDaily.VisitorKey) },
            usage.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.NotNull(model.FindEntityType(typeof(ChatSession))!.FindProperty(nameof(ChatSession.UserMessageCount)));

        var chunkForeignKey = model.FindEntityType(typeof(KnowledgeChunk))!.GetForeignKeys().Single();
        Assert.Equal(DeleteBehavior.Cascade, chunkForeignKey.DeleteBehavior);
    }

    [Fact]
    public void Model_configures_pgvector_extension_column_and_hnsw_index()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;
        var annotations = model.GetAnnotations().Select(annotation => annotation.Name).ToArray();

        Assert.Contains(annotations, name => name.Contains("PostgresExtension:pgcrypto", StringComparison.Ordinal));
        Assert.Contains(annotations, name => name.Contains("PostgresExtension:vector", StringComparison.Ordinal));

        var entity = model.FindEntityType(typeof(KnowledgeChunk))!;
        Assert.Equal("vector(1536)", entity.FindProperty(nameof(KnowledgeChunk.Embedding))!.GetColumnType());
        var index = entity.GetIndexes().Single(item => item.GetDatabaseName() == "ix_knowledge_chunks_embedding_hnsw");
        Assert.Equal("hnsw", index.FindAnnotation("Npgsql:IndexMethod")!.Value);
        Assert.Equal(new[] { "vector_cosine_ops" }, (string[])index.FindAnnotation("Npgsql:IndexOperators")!.Value!);
    }

    [Fact]
    public void Migration_is_discoverable_without_connecting_to_a_database()
    {
        using var context = CreateContext();
        var migrations = context.Database.GetMigrations().ToArray();

        Assert.Equal(5, migrations.Length);
        Assert.EndsWith("_InitialPortfolioSchema", migrations[0], StringComparison.Ordinal);
        Assert.EndsWith("_RemoveContactMessages", migrations[1], StringComparison.Ordinal);
        Assert.EndsWith("_AllowDuplicateSocialLinkPlatforms", migrations[2], StringComparison.Ordinal);
        Assert.EndsWith("_AllowProjectSlugUpdates", migrations[3], StringComparison.Ordinal);
        Assert.EndsWith("_AddDurableChatProtection", migrations[4], StringComparison.Ordinal);
    }

    [Fact]
    public void Migration_script_contains_schema_only_postgresql_objects()
    {
        using var context = CreateContext();
        var migration = context.Database.GetMigrations().First();
        var script = context.GetService<IMigrator>().GenerateScript(Migration.InitialDatabase, migration);

        Assert.Contains("CREATE EXTENSION IF NOT EXISTS pgcrypto", script, StringComparison.Ordinal);
        Assert.Contains("CREATE EXTENSION IF NOT EXISTS vector", script, StringComparison.Ordinal);
        Assert.Contains("vector(1536)", script, StringComparison.Ordinal);
        Assert.Contains("USING hnsw (embedding vector_cosine_ops)", script, StringComparison.Ordinal);
        Assert.Equal(16, CountOccurrences(script, "CREATE TRIGGER"));
        Assert.Equal(5, CountOccurrences(script, "CREATE UNIQUE INDEX"));
        Assert.DoesNotContain("CREATE INDEX \"IX_", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Removal_migration_drops_only_the_contact_messages_table()
    {
        using var context = CreateContext();
        var migrations = context.Database.GetMigrations().ToArray();
        var script = context.GetService<IMigrator>().GenerateScript(migrations[0], migrations[1]);

        Assert.Contains("DROP TABLE contact_messages", script, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, CountOccurrences(script.ToUpperInvariant(), "DROP TABLE"));
    }

    [Fact]
    public void Duplicate_social_platform_migration_only_drops_and_recreates_the_unique_index()
    {
        using var context = CreateContext();
        var migrations = context.Database.GetMigrations().ToArray();
        var migrator = context.GetService<IMigrator>();
        var upScript = migrator.GenerateScript(migrations[1], migrations[2]);
        var downScript = migrator.GenerateScript(migrations[2], migrations[1]);

        Assert.Contains("DROP INDEX uq_social_links_platform_ci", upScript, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(upScript, "DROP INDEX"));
        Assert.DoesNotContain("DROP TABLE", upScript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CREATE TABLE", upScript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ALTER TABLE", upScript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM social_links", upScript, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("CREATE UNIQUE INDEX uq_social_links_platform_ci", downScript, StringComparison.Ordinal);
        Assert.Contains("ON social_links (LOWER(platform))", downScript, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(downScript, "CREATE UNIQUE INDEX"));
        Assert.DoesNotContain("DROP TABLE", downScript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CREATE TABLE", downScript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ALTER TABLE", downScript, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Project_slug_migration_only_removes_and_recreates_the_redundant_constraint()
    {
        using var context = CreateContext();
        var migrations = context.Database.GetMigrations().ToArray();
        var migrator = context.GetService<IMigrator>();
        var initialScript = migrator.GenerateScript(Migration.InitialDatabase, migrations[0]);
        var upScript = migrator.GenerateScript(migrations[2], migrations[3]);
        var downScript = migrator.GenerateScript(migrations[3], migrations[2]);

        Assert.Contains("CREATE UNIQUE INDEX uq_projects_slug_ci", initialScript, StringComparison.Ordinal);
        Assert.Contains("DROP CONSTRAINT uq_projects_slug", upScript, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(upScript, "DROP CONSTRAINT"));
        Assert.DoesNotContain("uq_projects_slug_ci", upScript, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP TABLE", upScript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CREATE TABLE", upScript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP INDEX", upScript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM projects", upScript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("FOREIGN KEY", upScript, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("ADD CONSTRAINT uq_projects_slug UNIQUE (slug)", downScript, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(downScript, "ADD CONSTRAINT"));
        Assert.DoesNotContain("uq_projects_slug_ci", downScript, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP TABLE", downScript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CREATE TABLE", downScript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("FOREIGN KEY", downScript, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Chat_protection_migration_is_additive_and_preserves_vector_schema()
    {
        using var context = CreateContext();
        var migrations = context.Database.GetMigrations().ToArray();
        var migrator = context.GetService<IMigrator>();
        var upScript = migrator.GenerateScript(migrations[3], migrations[4]);
        var downScript = migrator.GenerateScript(migrations[4], migrations[3]);

        Assert.Contains("ADD user_message_count integer", upScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATE TABLE chat_usage_daily", upScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PRIMARY KEY (usage_date, visitor_key)", upScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("message.role = 'USER'", upScript, StringComparison.Ordinal);
        Assert.DoesNotContain("knowledge_chunks", upScript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("vector(1536)", upScript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP TABLE", upScript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM", upScript, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("DROP TABLE chat_usage_daily", downScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DROP COLUMN user_message_count", downScript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("knowledge_chunks", downScript, StringComparison.OrdinalIgnoreCase);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=portfolio_model_test", npgsql => npgsql.UseVector())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static int CountOccurrences(string value, string pattern) =>
        value.Split(pattern, StringSplitOptions.None).Length - 1;
}
