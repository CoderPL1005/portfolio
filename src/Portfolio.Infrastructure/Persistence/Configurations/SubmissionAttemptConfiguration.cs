using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portfolio.Domain.Entities;

namespace Portfolio.Infrastructure.Persistence.Configurations;

public sealed class SubmissionAttemptConfiguration : IEntityTypeConfiguration<SubmissionAttempt>
{
    public void Configure(EntityTypeBuilder<SubmissionAttempt> builder)
    {
        builder.ToTable("submission_attempts", table =>
        {
            table.HasCheckConstraint("ck_submission_attempts_provider", "provider IN ('EMAIL', 'COMPANY_SITE', 'TOPCV', 'VIETNAMWORKS', 'MANUAL')");
            table.HasCheckConstraint("ck_submission_attempts_status", "status IN ('CREATED', 'APPROVED', 'SUBMITTING', 'SUCCEEDED', 'FAILED', 'UNKNOWN')");
            table.HasCheckConstraint("ck_submission_attempts_idempotency_key", "idempotency_key ~ '^[0-9a-f]{64}$'");
            table.HasCheckConstraint("ck_submission_attempts_package_revision", "package_revision = 1");
            table.HasCheckConstraint("ck_submission_attempts_manifest_hash", "package_manifest_hash ~ '^[0-9a-f]{64}$'");
            table.HasCheckConstraint("ck_submission_attempts_application_version", "application_version_at_creation >= 1");
            table.HasCheckConstraint("ck_submission_attempts_version", "version >= 1");
            table.HasCheckConstraint("ck_submission_attempts_timestamps", "(started_at IS NULL OR started_at >= created_at) AND (completed_at IS NULL OR (started_at IS NOT NULL AND completed_at >= started_at))");
            table.HasCheckConstraint("ck_submission_attempts_outcome", "(status = 'SUCCEEDED' AND completed_at IS NOT NULL AND failure_code IS NULL AND failure_message IS NULL) OR (status IN ('FAILED', 'UNKNOWN') AND completed_at IS NOT NULL) OR (status IN ('CREATED', 'APPROVED', 'SUBMITTING') AND completed_at IS NULL AND provider_submission_id IS NULL AND failure_code IS NULL AND failure_message IS NULL)");
        });

        builder.HasKey(entity => entity.Id).HasName("submission_attempts_pkey");
        builder.Property(entity => entity.Id).HasColumnName("id").HasColumnType("uuid").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(entity => entity.JobApplicationId).HasColumnName("job_application_id").HasColumnType("uuid");
        builder.Property(entity => entity.Provider).HasColumnName("provider").HasColumnType("character varying(30)").HasMaxLength(30);
        builder.Property(entity => entity.Status).HasColumnName("status").HasColumnType("character varying(30)").HasMaxLength(30).HasDefaultValue("CREATED");
        builder.Property(entity => entity.IdempotencyKey).HasColumnName("idempotency_key").HasColumnType("character varying(64)").HasMaxLength(64);
        builder.Property(entity => entity.PackageRevision).HasColumnName("package_revision");
        builder.Property(entity => entity.PackageManifestHash).HasColumnName("package_manifest_hash").HasColumnType("character varying(64)").HasMaxLength(64);
        builder.Property(entity => entity.ApplicationVersionAtCreation).HasColumnName("application_version_at_creation");
        builder.Property(entity => entity.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("NOW()");
        builder.Property(entity => entity.CreatedByAdminUserId).HasColumnName("created_by_admin_user_id").HasColumnType("uuid");
        builder.Property(entity => entity.StartedAt).HasColumnName("started_at").HasColumnType("timestamp with time zone");
        builder.Property(entity => entity.CompletedAt).HasColumnName("completed_at").HasColumnType("timestamp with time zone");
        builder.Property(entity => entity.ProviderSubmissionId).HasColumnName("provider_submission_id").HasColumnType("character varying(500)").HasMaxLength(500);
        builder.Property(entity => entity.FailureCode).HasColumnName("failure_code").HasColumnType("character varying(100)").HasMaxLength(100);
        builder.Property(entity => entity.FailureMessage).HasColumnName("failure_message").HasColumnType("character varying(2000)").HasMaxLength(2000);
        builder.Property(entity => entity.Version).HasColumnName("version").HasDefaultValue(1).IsConcurrencyToken();

        builder.HasOne(entity => entity.JobApplication).WithMany(entity => entity.SubmissionAttempts)
            .HasForeignKey(entity => entity.JobApplicationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.CreatedByAdminUser).WithMany()
            .HasForeignKey(entity => entity.CreatedByAdminUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(entity => entity.IdempotencyKey).IsUnique().HasDatabaseName("uq_submission_attempts_idempotency_key");
        builder.HasIndex(entity => new { entity.JobApplicationId, entity.PackageRevision, entity.Provider })
            .IsUnique().HasDatabaseName("uq_submission_attempts_non_retryable_context")
            .HasFilter("status IN ('CREATED', 'APPROVED', 'SUBMITTING', 'SUCCEEDED', 'UNKNOWN')");
        builder.HasIndex(entity => new { entity.JobApplicationId, entity.CreatedAt, entity.Id })
            .HasDatabaseName("ix_submission_attempts_application_created_id").IsDescending(false, true, true);
    }
}
