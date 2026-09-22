using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using System.Text.RegularExpressions;
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
        "chat_message_sources", "chat_message_feedback", "chat_usage_daily", "raw_job_postings",
        "raw_job_posting_attachments", "job_postings", "job_applications", "job_application_events", "job_application_documents",
        "push_subscriptions", "candidate_job_preferences", "canonical_cvs"
    ];

    [Fact]
    public void Model_contains_all_35_expected_tables_and_excludes_contact_messages()
    {
        using var context = CreateContext();
        var tables = context.Model.GetEntityTypes().Select(entity => entity.GetTableName()).Order().ToArray();

        Assert.Equal(ExpectedTables.Order(), tables);
        Assert.DoesNotContain("contact_messages", tables);
        Assert.Null(context.Model.FindEntityType("Portfolio.Domain.Entities.ContactMessage"));
    }

    [Fact]
    public void Application_context_contract_exposes_all_35_sets()
    {
        var dbSetCount = typeof(IApplicationDbContext).GetProperties()
            .Count(property => property.PropertyType.IsGenericType &&
                property.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>));

        Assert.Equal(35, dbSetCount);
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

        Assert.Equal(27, model.GetEntityTypes().SelectMany(entity => entity.GetForeignKeys()).Count());
        Assert.Equal(85, model.GetEntityTypes().SelectMany(entity => entity.GetCheckConstraints()).Count());
        Assert.Equal(47, model.GetEntityTypes().SelectMany(entity => entity.GetIndexes()).Count());

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

        Assert.Equal(15, migrations.Length);
        Assert.EndsWith("_InitialPortfolioSchema", migrations[0], StringComparison.Ordinal);
        Assert.EndsWith("_RemoveContactMessages", migrations[1], StringComparison.Ordinal);
        Assert.EndsWith("_AllowDuplicateSocialLinkPlatforms", migrations[2], StringComparison.Ordinal);
        Assert.EndsWith("_AllowProjectSlugUpdates", migrations[3], StringComparison.Ordinal);
        Assert.EndsWith("_AddDurableChatProtection", migrations[4], StringComparison.Ordinal);
        Assert.EndsWith("_AddJobHuntingFoundation", migrations[5], StringComparison.Ordinal);
        Assert.EndsWith("_AddRawJobPostingIngestionKey", migrations[6], StringComparison.Ordinal);
        Assert.EndsWith("_AddTelegramRawJobPostingAttachments", migrations[7], StringComparison.Ordinal);
        Assert.EndsWith("_AddWebPushSubscriptions", migrations[8], StringComparison.Ordinal);
        Assert.EndsWith("_GeneralizeRawJobPostingAttachmentsForPwa", migrations[9], StringComparison.Ordinal);
        Assert.EndsWith("_AddRawJobPostingAnalysisConcurrency", migrations[10], StringComparison.Ordinal);
        Assert.EndsWith("_AddCandidateJobPreferences", migrations[11], StringComparison.Ordinal);
        Assert.EndsWith("_EnforceSingleJobApplicationPerPosting", migrations[12], StringComparison.Ordinal);
        Assert.EndsWith("_AddPrivateCanonicalCv", migrations[13], StringComparison.Ordinal);
        Assert.EndsWith("_FinalizeApplicationPackage", migrations[14], StringComparison.Ordinal);
    }

    [Fact]
    public void Application_package_migration_only_adds_package_metadata_constraints_and_index()
    {
        using var context=CreateContext();var migrations=context.Database.GetMigrations().ToArray();var migrator=context.GetService<IMigrator>();var up=migrator.GenerateScript(migrations[13],migrations[14]);var down=migrator.GenerateScript(migrations[14],migrations[13]);
        Assert.Contains("package_status",up,StringComparison.OrdinalIgnoreCase);Assert.Contains("package_revision",up,StringComparison.OrdinalIgnoreCase);Assert.Contains("package_manifest_hash",up,StringComparison.OrdinalIgnoreCase);Assert.Contains("source_canonical_cv_version",up,StringComparison.OrdinalIgnoreCase);Assert.Contains("uq_job_application_documents_managed_cv_revision",up,StringComparison.Ordinal);Assert.Contains("PACKAGE_FINALIZED",up,StringComparison.Ordinal);Assert.DoesNotContain("CREATE TABLE",up,StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("DROP TABLE",up,StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("DELETE FROM",up,StringComparison.OrdinalIgnoreCase);Assert.Contains("DROP COLUMN package_status",down,StringComparison.OrdinalIgnoreCase);Assert.Contains("DOCUMENT_REMOVED",down,StringComparison.Ordinal);
    }

    [Fact]
    public void Canonical_cv_migration_only_adds_the_private_singleton_table()
    {
        using var context = CreateContext();
        var migrations = context.Database.GetMigrations().ToArray();
        var migrator = context.GetService<IMigrator>();
        var up = migrator.GenerateScript(migrations[12], migrations[13]);
        var down = migrator.GenerateScript(migrations[13], migrations[12]);

        Assert.Contains("CREATE TABLE canonical_cvs", up, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("uq_canonical_cvs_singleton", up, StringComparison.Ordinal);
        Assert.Contains("uq_canonical_cvs_storage_key", up, StringComparison.Ordinal);
        Assert.Contains("ck_canonical_cvs_content_hash", up, StringComparison.Ordinal);
        Assert.Contains("ck_canonical_cvs_file_size", up, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(up.ToUpperInvariant(), "CREATE TABLE"));
        Assert.DoesNotContain("DROP TABLE", up, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("job_applications", up, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("job_application_documents", up, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DROP TABLE canonical_cvs", down, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, CountOccurrences(down.ToUpperInvariant(), "DROP TABLE"));
    }

    [Fact]
    public void Application_uniqueness_migration_only_replaces_the_job_posting_index()
    {
        using var context = CreateContext();
        var migrations = context.Database.GetMigrations().ToArray();
        var migrator = context.GetService<IMigrator>();
        var up = migrator.GenerateScript(migrations[11], migrations[12]);
        var down = migrator.GenerateScript(migrations[12], migrations[11]);

        Assert.Contains("DROP INDEX ix_job_applications_job_posting_id", up, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATE UNIQUE INDEX ix_job_applications_job_posting_id", up, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DROP INDEX ix_job_applications_job_posting_id", down, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATE INDEX ix_job_applications_job_posting_id", down, StringComparison.OrdinalIgnoreCase);
        Assert.All(new[] { up, down }, script =>
        {
            Assert.DoesNotContain("ALTER TABLE", script, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("DROP TABLE", script, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("CREATE TABLE", script, StringComparison.OrdinalIgnoreCase);
            var indexes = Regex.Matches(script, "(?:CREATE(?: UNIQUE)? INDEX|DROP INDEX)\\s+\"?([^\"\\s;]+)", RegexOptions.IgnoreCase)
                .Select(match => match.Groups[1].Value)
                .ToArray();
            Assert.NotEmpty(indexes);
            Assert.All(indexes, name => Assert.Equal("ix_job_applications_job_posting_id", name));
        });
    }

    [Fact]
    public void Candidate_preferences_migration_only_adds_the_private_singleton_table()
    {
        using var context = CreateContext();
        var migrations = context.Database.GetMigrations().ToArray();
        var migrator = context.GetService<IMigrator>();
        var up = migrator.GenerateScript(migrations[10], migrations[11]);
        var down = migrator.GenerateScript(migrations[11], migrations[10]);
        Assert.Contains("CREATE TABLE candidate_job_preferences", up, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("uq_candidate_job_preferences_singleton_key", up, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(up.ToUpperInvariant(), "CREATE TABLE"));
        Assert.DoesNotContain("DROP TABLE", up, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("job_postings\" ALTER", up, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DROP TABLE candidate_job_preferences", down, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Web_push_migration_only_adds_and_removes_the_subscription_table()
    {
        using var context = CreateContext();
        var migrations = context.Database.GetMigrations().ToArray();
        var migrator = context.GetService<IMigrator>();
        var up = migrator.GenerateScript(migrations[7], migrations[8]);
        var down = migrator.GenerateScript(migrations[8], migrations[7]);

        Assert.Contains("CREATE TABLE push_subscriptions", up, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("uq_push_subscriptions_endpoint", up, StringComparison.Ordinal);
        Assert.Contains("ix_push_subscriptions_active", up, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(up.ToUpperInvariant(), "CREATE TABLE"));
        Assert.DoesNotContain("DROP TABLE", up, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM", up, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ALTER TABLE", up, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("DROP TABLE push_subscriptions", down, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, CountOccurrences(down.ToUpperInvariant(), "DROP TABLE"));
    }

    [Fact]
    public void Telegram_attachment_migration_only_adds_and_removes_the_attachment_table()
    {
        using var context=CreateContext();var migrations=context.Database.GetMigrations().ToArray();var migrator=context.GetService<IMigrator>();
        var up=migrator.GenerateScript(migrations[6],migrations[7]);var down=migrator.GenerateScript(migrations[7],migrations[6]);
        Assert.Contains("CREATE TABLE raw_job_posting_attachments",up,StringComparison.OrdinalIgnoreCase);Assert.Equal(1,CountOccurrences(up.ToUpperInvariant(),"CREATE TABLE"));
        Assert.Contains("uq_raw_job_posting_attachments_delivery",up,StringComparison.Ordinal);Assert.Contains("ON DELETE RESTRICT",up,StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("DROP TABLE",up,StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("DELETE FROM",up,StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("ALTER TABLE raw_job_postings",up,StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DROP TABLE raw_job_posting_attachments",down,StringComparison.OrdinalIgnoreCase);Assert.Equal(1,CountOccurrences(down.ToUpperInvariant(),"DROP TABLE"));
    }

    [Fact]
    public void Pwa_attachment_migration_only_generalizes_telegram_delivery_fields()
    {
        using var context=CreateContext();var migrations=context.Database.GetMigrations().ToArray();var migrator=context.GetService<IMigrator>();
        var up=migrator.GenerateScript(migrations[8],migrations[9]);var down=migrator.GenerateScript(migrations[9],migrations[8]);
        Assert.Contains("telegram_message_id",up,StringComparison.Ordinal);Assert.Contains("telegram_file_id",up,StringComparison.Ordinal);Assert.Contains("telegram_file_unique_id",up,StringComparison.Ordinal);
        Assert.Contains("telegram_message_id IS NOT NULL",up,StringComparison.Ordinal);Assert.Contains("telegram_message_id IS NULL OR telegram_message_id > 0",up,StringComparison.Ordinal);
        Assert.DoesNotContain("CREATE TABLE",up,StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("DROP TABLE",up,StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("DELETE FROM",up,StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("UPDATE ",up,StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SET NOT NULL",down,StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("UPDATE ",down,StringComparison.OrdinalIgnoreCase);
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

    [Fact]
    public void Job_hunting_foundation_migration_only_adds_the_five_domain_tables()
    {
        using var context = CreateContext();
        var migrations = context.Database.GetMigrations().ToArray();
        var migrator = context.GetService<IMigrator>();
        var upScript = migrator.GenerateScript(migrations[4], migrations[5]);
        var downScript = migrator.GenerateScript(migrations[5], migrations[4]);

        var expectedTables = new[]
        {
            "job_postings", "job_applications", "raw_job_postings",
            "job_application_documents", "job_application_events"
        };
        Assert.All(expectedTables, table =>
            Assert.Contains($"CREATE TABLE {table}", upScript, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(5, CountOccurrences(upScript.ToUpperInvariant(), "CREATE TABLE"));
        Assert.Equal(3, CountOccurrences(upScript, "CREATE TRIGGER"));
        Assert.Contains("REFERENCES admin_users", upScript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("knowledge_documents", upScript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("knowledge_chunks", upScript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("vector(1536)", upScript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ALTER TABLE admin_users", upScript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP TABLE", upScript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM", upScript, StringComparison.OrdinalIgnoreCase);

        Assert.All(expectedTables, table =>
            Assert.Contains($"DROP TABLE {table}", downScript, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(5, CountOccurrences(downScript.ToUpperInvariant(), "DROP TABLE"));
        Assert.DoesNotContain("knowledge_documents", downScript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("knowledge_chunks", downScript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ALTER TABLE admin_users", downScript, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Candidate_preferences_migration_contains_salary_consistency_constraint()
    {
        using var context = CreateContext();
        var migrations = context.Database.GetMigrations().ToArray();
        var script = context.GetService<IMigrator>().GenerateScript(migrations[10], migrations[11]);

        Assert.EndsWith("_AddCandidateJobPreferences", migrations[11], StringComparison.Ordinal);
        Assert.Contains("ck_candidate_job_preferences_salary_consistency", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("minimum_salary IS NULL AND salary_currency IS NULL AND salary_period IS NULL", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("minimum_salary IS NOT NULL AND salary_currency IS NOT NULL AND salary_period IS NOT NULL", script, StringComparison.OrdinalIgnoreCase);
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
