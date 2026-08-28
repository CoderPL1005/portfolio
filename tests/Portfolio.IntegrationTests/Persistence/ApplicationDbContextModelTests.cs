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
        "chat_message_sources", "chat_message_feedback"
    ];

    [Fact]
    public void Model_contains_all_25_expected_tables_and_excludes_contact_messages()
    {
        using var context = CreateContext();
        var tables = context.Model.GetEntityTypes().Select(entity => entity.GetTableName()).Order().ToArray();

        Assert.Equal(ExpectedTables.Order(), tables);
        Assert.DoesNotContain("contact_messages", tables);
        Assert.Null(context.Model.FindEntityType("Portfolio.Domain.Entities.ContactMessage"));
    }

    [Fact]
    public void Application_context_contract_exposes_all_25_sets()
    {
        var dbSetCount = typeof(IApplicationDbContext).GetProperties()
            .Count(property => property.PropertyType.IsGenericType &&
                property.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>));

        Assert.Equal(25, dbSetCount);
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
        Assert.Equal(33, model.GetEntityTypes().SelectMany(entity => entity.GetCheckConstraints()).Count());
        Assert.Equal(22, model.GetEntityTypes().SelectMany(entity => entity.GetIndexes()).Count());

        var experienceTechnology = model.FindEntityType(typeof(ExperienceTechnology))!;
        Assert.Equal(2, experienceTechnology.FindPrimaryKey()!.Properties.Count);
        var projectTechnology = model.FindEntityType(typeof(ProjectTechnology))!;
        Assert.Equal(2, projectTechnology.FindPrimaryKey()!.Properties.Count);

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

        Assert.Equal(2, migrations.Length);
        Assert.EndsWith("_InitialPortfolioSchema", migrations[0], StringComparison.Ordinal);
        Assert.EndsWith("_RemoveContactMessages", migrations[1], StringComparison.Ordinal);
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
