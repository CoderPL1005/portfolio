using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Pgvector.EntityFrameworkCore;
using Portfolio.Domain.Entities;
using Portfolio.Infrastructure.Persistence;

namespace Portfolio.IntegrationTests.Persistence;

public sealed class JobHuntingPersistenceModelTests
{
    [Fact]
    public void Model_maps_entities_json_and_concurrency_tokens()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;

        Assert.Equal("raw_job_postings", model.FindEntityType(typeof(RawJobPosting))!.GetTableName());
        Assert.Equal("job_postings", model.FindEntityType(typeof(JobPosting))!.GetTableName());
        Assert.Equal("job_applications", model.FindEntityType(typeof(JobApplication))!.GetTableName());
        Assert.Equal("job_application_events", model.FindEntityType(typeof(JobApplicationEvent))!.GetTableName());
        Assert.Equal("job_application_documents", model.FindEntityType(typeof(JobApplicationDocument))!.GetTableName());
        Assert.Equal("raw_job_posting_attachments", model.FindEntityType(typeof(RawJobPostingAttachment))!.GetTableName());
        Assert.Equal("candidate_job_preferences", model.FindEntityType(typeof(CandidateJobPreferences))!.GetTableName());

        Assert.Equal("jsonb", Property<RawJobPosting>(model, nameof(RawJobPosting.Metadata)).GetColumnType());
        Assert.True(Property<RawJobPostingAttachment>(model, nameof(RawJobPostingAttachment.TelegramMessageId)).IsNullable);
        Assert.True(Property<RawJobPostingAttachment>(model, nameof(RawJobPostingAttachment.TelegramFileId)).IsNullable);
        Assert.True(Property<RawJobPostingAttachment>(model, nameof(RawJobPostingAttachment.TelegramFileUniqueId)).IsNullable);
        var ingestionKey = Property<RawJobPosting>(model, nameof(RawJobPosting.IngestionKey));
        Assert.Equal("ingestion_key", ingestionKey.GetColumnName());
        Assert.Equal("character varying(500)", ingestionKey.GetColumnType());
        Assert.Equal(500, ingestionKey.GetMaxLength());
        Assert.True(ingestionKey.IsNullable);
        Assert.Equal("jsonb", Property<JobPosting>(model, nameof(JobPosting.TechnologyStack)).GetColumnType());
        Assert.Equal("jsonb", Property<JobApplicationEvent>(model, nameof(JobApplicationEvent.Metadata)).GetColumnType());
        Assert.Equal("jsonb", Property<JobApplicationDocument>(model, nameof(JobApplicationDocument.Metadata)).GetColumnType());
        Assert.True(Property<JobPosting>(model, nameof(JobPosting.Version)).IsConcurrencyToken);
        Assert.True(Property<RawJobPosting>(model, nameof(RawJobPosting.Version)).IsConcurrencyToken);
        Assert.True(Property<JobApplication>(model, nameof(JobApplication.Version)).IsConcurrencyToken);
        Assert.True(Property<CandidateJobPreferences>(model, nameof(CandidateJobPreferences.Version)).IsConcurrencyToken);
        Assert.Equal("numeric(18,2)", Property<CandidateJobPreferences>(model, nameof(CandidateJobPreferences.MinimumSalary)).GetColumnType());
    }

    [Fact]
    public void Model_enforces_state_value_and_data_integrity_constraints()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;

        AssertConstraint<RawJobPosting>(model, "ck_raw_job_postings_source", "FACEBOOK", "COMPANY_SITE");
        AssertConstraint<RawJobPosting>(model, "ck_raw_job_postings_ingestion_status", "NORMALIZED", "DUPLICATE");
        AssertConstraint<RawJobPosting>(model, "ck_raw_job_postings_content_hash", "^[0-9a-f]{64}$");
        AssertConstraint<RawJobPosting>(model, "ck_raw_job_postings_source_url_hash", "^[0-9a-f]{64}$");
        AssertConstraint<RawJobPosting>(model, "ck_raw_job_postings_version", "version >= 1");
        AssertConstraint<JobPosting>(model, "ck_job_postings_verification_status", "LIKELY_EXPIRED");
        AssertConstraint<JobPosting>(model, "ck_job_postings_selection_status", "PENDING_ANALYSIS");
        AssertConstraint<JobPosting>(model, "ck_job_postings_salary_range", "salary_maximum >= salary_minimum");
        AssertConstraint<JobPosting>(model, "ck_job_postings_salary_currency", "^[A-Z]{3}$");
        AssertConstraint<JobPosting>(model, "ck_job_postings_technology_stack", "jsonb_typeof(technology_stack) = 'array'");
        AssertConstraint<JobApplication>(model, "ck_job_applications_status", "INTERVIEW", "WITHDRAWN");
        AssertConstraint<JobApplication>(model, "ck_job_applications_channel", "PLATFORM", "MANUAL");
        AssertConstraint<JobApplicationEvent>(model, "ck_job_application_events_event_type", "DOCUMENT_ATTACHED");
        AssertConstraint<JobApplicationEvent>(model, "ck_job_application_events_actor_type", "EMAIL_CONNECTOR");
        AssertConstraint<JobApplicationEvent>(model, "ck_job_application_events_from_status", "from_status IS NULL", "OFFER");
        AssertConstraint<JobApplicationEvent>(model, "ck_job_application_events_to_status", "to_status IS NULL", "REJECTED");
        AssertConstraint<JobApplicationDocument>(model, "ck_job_application_documents_content_hash", "^[0-9a-f]{64}$");
        AssertConstraint<RawJobPostingAttachment>(model, "ck_raw_job_posting_attachments_type", "IMAGE");
        AssertConstraint<RawJobPostingAttachment>(model, "ck_raw_job_posting_attachments_content_hash", "^[0-9a-f]{64}$");
        AssertConstraint<RawJobPostingAttachment>(model, "ck_raw_job_posting_attachments_file_size", "file_size_bytes > 0");
        AssertConstraint<CandidateJobPreferences>(model, "ck_candidate_job_preferences_singleton", "singleton_key = 'CURRENT'");
        AssertConstraint<CandidateJobPreferences>(model, "ck_candidate_job_preferences_salary_currency", "^[A-Z]{3}$");
        AssertConstraint<CandidateJobPreferences>(model, "ck_candidate_job_preferences_salary_consistency", "minimum_salary IS NULL", "salary_currency IS NULL", "salary_period IS NULL", "minimum_salary IS NOT NULL", "salary_currency IS NOT NULL", "salary_period IS NOT NULL");
        AssertConstraint<CandidateJobPreferences>(model, "ck_candidate_job_preferences_version", "version >= 1");
    }

    [Fact]
    public void Model_preserves_history_with_restrict_and_nulls_deleted_admin_actor()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;

        AssertDeleteBehavior<RawJobPosting>(model, nameof(RawJobPosting.JobPostingId), DeleteBehavior.Restrict);
        AssertDeleteBehavior<RawJobPosting>(model, nameof(RawJobPosting.DuplicateOfRawJobPostingId), DeleteBehavior.Restrict);
        AssertDeleteBehavior<JobApplication>(model, nameof(JobApplication.JobPostingId), DeleteBehavior.Restrict);
        AssertDeleteBehavior<JobApplicationEvent>(model, nameof(JobApplicationEvent.JobApplicationId), DeleteBehavior.Restrict);
        AssertDeleteBehavior<JobApplicationEvent>(model, nameof(JobApplicationEvent.ActorAdminUserId), DeleteBehavior.SetNull);
        AssertDeleteBehavior<JobApplicationDocument>(model, nameof(JobApplicationDocument.JobApplicationId), DeleteBehavior.Restrict);
        AssertDeleteBehavior<RawJobPostingAttachment>(model, nameof(RawJobPostingAttachment.RawJobPostingId), DeleteBehavior.Restrict);
    }

    [Fact]
    public void Model_configures_required_indexes_and_approved_uniqueness()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;
        var raw = model.FindEntityType(typeof(RawJobPosting))!;
        var application = model.FindEntityType(typeof(JobApplication))!;

        AssertIndex(raw, "uq_raw_job_postings_source_external_id", true, "source_external_id IS NOT NULL");
        AssertIndex(raw, "uq_raw_job_postings_ingestion_key", true, "ingestion_key IS NOT NULL");
        AssertIndex(raw, "uq_raw_job_postings_source_url_hash", true, "source_url_hash IS NOT NULL");
        AssertIndex(raw, "ix_raw_job_postings_content_hash", false);
        AssertIndex(raw, "ix_raw_job_postings_company_title_fingerprint", false);
        AssertIndex(raw, "ix_raw_job_postings_ingestion_status_created_at", false);
        AssertIndex(raw, "ix_raw_job_postings_job_posting_id", false);

        AssertIndex(model.FindEntityType(typeof(JobPosting))!, "ix_job_postings_selection_verification_created_at", false);
        AssertIndex(model.FindEntityType(typeof(JobPosting))!, "ix_job_postings_company_position", false);
        AssertIndex(model.FindEntityType(typeof(JobPosting))!, "ix_job_postings_expires_at", false, "expires_at IS NOT NULL");
        AssertIndex(model.FindEntityType(typeof(JobPosting))!, "ix_job_postings_archived_at", false, "archived_at IS NOT NULL");
        AssertIndex(application, "ix_job_applications_job_posting_id", true);
        AssertIndex(application, "ix_job_applications_status_updated_at", false);
        AssertIndex(application, "ix_job_applications_channel_applied_at", false);
        AssertIndex(model.FindEntityType(typeof(JobApplicationEvent))!, "ix_job_application_events_application_occurred_id", false);
        AssertIndex(model.FindEntityType(typeof(JobApplicationDocument))!, "ix_job_application_documents_application_type_created_id", false);
        var attachment=model.FindEntityType(typeof(RawJobPostingAttachment))!;
        AssertIndex(attachment,"uq_raw_job_posting_attachments_delivery",true,"telegram_message_id IS NOT NULL");
        AssertIndex(attachment,"ix_raw_job_posting_attachments_order",false);
        AssertIndex(attachment,"ix_raw_job_posting_attachments_content_hash",false);
        AssertIndex(model.FindEntityType(typeof(CandidateJobPreferences))!, "uq_candidate_job_preferences_singleton_key", true);

        Assert.DoesNotContain(raw.GetIndexes(), index =>
            index.IsUnique && index.Properties.Any(property => property.Name is nameof(RawJobPosting.ContentHash) or nameof(RawJobPosting.CompanyTitleFingerprint)));
        Assert.Equal(
            [nameof(RawJobPosting.Source), nameof(RawJobPosting.SourceExternalId)],
            raw.GetIndexes().Single(index => index.GetDatabaseName() == "uq_raw_job_postings_source_external_id").Properties.Select(property => property.Name));
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=portfolio_job_hunting_model_test", npgsql => npgsql.UseVector())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static IProperty Property<TEntity>(IModel model, string name) =>
        model.FindEntityType(typeof(TEntity))!.FindProperty(name)!;

    private static void AssertConstraint<TEntity>(IModel model, string name, params string[] fragments)
    {
        var constraint = model.FindEntityType(typeof(TEntity))!.GetCheckConstraints().Single(item => item.Name == name);
        Assert.All(fragments, fragment => Assert.Contains(fragment, constraint.Sql, StringComparison.Ordinal));
    }

    private static void AssertDeleteBehavior<TEntity>(IModel model, string propertyName, DeleteBehavior expected)
    {
        var foreignKey = model.FindEntityType(typeof(TEntity))!.GetForeignKeys()
            .Single(item => item.Properties.Any(property => property.Name == propertyName));
        Assert.Equal(expected, foreignKey.DeleteBehavior);
    }

    private static void AssertIndex(IEntityType entity, string name, bool unique, string? filter = null)
    {
        var index = entity.GetIndexes().Single(item => item.GetDatabaseName() == name);
        Assert.Equal(unique, index.IsUnique);
        Assert.Equal(filter, index.GetFilter());
    }
}
