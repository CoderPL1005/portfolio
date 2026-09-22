using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portfolio.Domain.Entities;

namespace Portfolio.Infrastructure.Persistence.Configurations;

public sealed class JobApplicationConfiguration : IEntityTypeConfiguration<JobApplication>
{
    public void Configure(EntityTypeBuilder<JobApplication> builder)
    {
        builder.ToTable("job_applications", table =>
        {
            table.HasTrigger("trg_job_applications_updated_at");
            table.HasCheckConstraint("ck_job_applications_status", "status IN ('DRAFT', 'APPLIED', 'INTERVIEW', 'REJECTED', 'OFFER', 'WITHDRAWN')");
            table.HasCheckConstraint("ck_job_applications_channel", "channel IS NULL OR channel IN ('EMAIL', 'PLATFORM', 'MANUAL', 'OTHER')");
            table.HasCheckConstraint("ck_job_applications_version", "version >= 1");
            table.HasCheckConstraint("ck_job_applications_package_status", "package_status IN ('DRAFT', 'FINALIZED')");
            table.HasCheckConstraint("ck_job_applications_package_revision", "package_revision IN (0, 1)");
            table.HasCheckConstraint("ck_job_applications_package_state", "(package_status = 'DRAFT' AND package_revision = 0 AND package_job_posting_version IS NULL AND package_manifest_hash IS NULL AND package_finalized_at IS NULL AND package_finalized_by_admin_user_id IS NULL) OR (package_status = 'FINALIZED' AND package_revision = 1 AND package_job_posting_version IS NOT NULL AND package_manifest_hash ~ '^[0-9a-f]{64}$' AND package_finalized_at IS NOT NULL AND package_finalized_by_admin_user_id IS NOT NULL)");
        });

        builder.HasKey(entity => entity.Id).HasName("job_applications_pkey");
        builder.Property(entity => entity.Id).HasColumnName("id").HasColumnType("uuid").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(entity => entity.JobPostingId).HasColumnName("job_posting_id").HasColumnType("uuid");
        builder.Property(entity => entity.Status).HasColumnName("status").HasColumnType("character varying(30)").HasMaxLength(30).HasDefaultValue("DRAFT");
        builder.Property(entity => entity.Channel).HasColumnName("channel").HasColumnType("character varying(30)").HasMaxLength(30);
        builder.Property(entity => entity.ApplicationEmail).HasColumnName("application_email").HasColumnType("character varying(255)").HasMaxLength(255);
        builder.Property(entity => entity.ApplicationUrl).HasColumnName("application_url").HasColumnType("text");
        builder.Property(entity => entity.ExternalApplicationId).HasColumnName("external_application_id").HasColumnType("character varying(500)").HasMaxLength(500);
        builder.Property(entity => entity.AppliedAt).HasColumnName("applied_at").HasColumnType("timestamp with time zone");
        builder.Property(entity => entity.LastActivityAt).HasColumnName("last_activity_at").HasColumnType("timestamp with time zone");
        builder.Property(entity => entity.Notes).HasColumnName("notes").HasColumnType("text");
        builder.Property(entity => entity.PackageStatus).HasColumnName("package_status").HasColumnType("character varying(30)").HasMaxLength(30).HasDefaultValue("DRAFT");
        builder.Property(entity => entity.PackageRevision).HasColumnName("package_revision").HasDefaultValue(0);
        builder.Property(entity => entity.PackageJobPostingVersion).HasColumnName("package_job_posting_version");
        builder.Property(entity => entity.PackageManifestHash).HasColumnName("package_manifest_hash").HasColumnType("character varying(64)").HasMaxLength(64);
        builder.Property(entity => entity.PackageFinalizedAt).HasColumnName("package_finalized_at").HasColumnType("timestamp with time zone");
        builder.Property(entity => entity.PackageFinalizedByAdminUserId).HasColumnName("package_finalized_by_admin_user_id").HasColumnType("uuid");
        builder.Property(entity => entity.Version).HasColumnName("version").HasDefaultValue(1).IsConcurrencyToken();
        builder.Property(entity => entity.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("NOW()");
        builder.Property(entity => entity.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("NOW()");

        builder.HasOne(entity => entity.JobPosting)
            .WithMany(entity => entity.JobApplications)
            .HasForeignKey(entity => entity.JobPostingId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AdminUser>()
            .WithMany()
            .HasForeignKey(entity => entity.PackageFinalizedByAdminUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(entity => entity.JobPostingId).IsUnique().HasDatabaseName("ix_job_applications_job_posting_id");
        builder.HasIndex(entity => new { entity.Status, entity.UpdatedAt })
            .HasDatabaseName("ix_job_applications_status_updated_at")
            .IsDescending(false, true);
        builder.HasIndex(entity => new { entity.Channel, entity.AppliedAt })
            .HasDatabaseName("ix_job_applications_channel_applied_at")
            .IsDescending(false, true);
    }
}
