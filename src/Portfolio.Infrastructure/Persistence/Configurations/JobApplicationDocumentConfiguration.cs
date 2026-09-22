using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portfolio.Domain.Entities;

namespace Portfolio.Infrastructure.Persistence.Configurations;

public sealed class JobApplicationDocumentConfiguration : IEntityTypeConfiguration<JobApplicationDocument>
{
    public void Configure(EntityTypeBuilder<JobApplicationDocument> builder)
    {
        builder.ToTable("job_application_documents", table =>
        {
            table.HasCheckConstraint("ck_job_application_documents_content_hash", "content_hash IS NULL OR content_hash ~ '^[0-9a-f]{64}$'");
            table.HasCheckConstraint("ck_job_application_documents_package_revision", "package_revision IS NULL OR package_revision = 1");
            table.HasCheckConstraint("ck_job_application_documents_managed_snapshot", "package_revision IS NULL OR (document_type = 'CV' AND storage_key IS NOT NULL AND file_name IS NOT NULL AND content_type = 'application/pdf' AND file_size_bytes > 0 AND file_size_bytes <= 10485760 AND content_hash IS NOT NULL AND source_canonical_cv_version >= 1 AND removed_at IS NULL)");
        });

        builder.HasKey(entity => entity.Id).HasName("job_application_documents_pkey");
        builder.Property(entity => entity.Id).HasColumnName("id").HasColumnType("uuid").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(entity => entity.JobApplicationId).HasColumnName("job_application_id").HasColumnType("uuid");
        builder.Property(entity => entity.DocumentType).HasColumnName("document_type").HasColumnType("character varying(50)").HasMaxLength(50);
        builder.Property(entity => entity.VersionLabel).HasColumnName("version_label").HasColumnType("character varying(100)").HasMaxLength(100);
        builder.Property(entity => entity.FileName).HasColumnName("file_name").HasColumnType("character varying(500)").HasMaxLength(500);
        builder.Property(entity => entity.StorageKey).HasColumnName("storage_key").HasColumnType("character varying(1000)").HasMaxLength(1000);
        builder.Property(entity => entity.ContentHash).HasColumnName("content_hash").HasColumnType("character varying(64)").HasMaxLength(64);
        builder.Property(entity => entity.ContentType).HasColumnName("content_type").HasColumnType("character varying(100)").HasMaxLength(100);
        builder.Property(entity => entity.FileSizeBytes).HasColumnName("file_size_bytes").HasColumnType("bigint");
        builder.Property(entity => entity.PackageRevision).HasColumnName("package_revision");
        builder.Property(entity => entity.SourceCanonicalCvVersion).HasColumnName("source_canonical_cv_version");
        builder.Property(entity => entity.Metadata).HasColumnName("metadata").HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
        builder.Property(entity => entity.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("NOW()");
        builder.Property(entity => entity.RemovedAt).HasColumnName("removed_at").HasColumnType("timestamp with time zone");

        builder.HasOne(entity => entity.JobApplication)
            .WithMany(entity => entity.Documents)
            .HasForeignKey(entity => entity.JobApplicationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(entity => new { entity.JobApplicationId, entity.DocumentType, entity.CreatedAt, entity.Id })
            .HasDatabaseName("ix_job_application_documents_application_type_created_id");
        builder.HasIndex(entity => new { entity.JobApplicationId, entity.PackageRevision })
            .HasDatabaseName("uq_job_application_documents_managed_cv_revision")
            .IsUnique()
            .HasFilter("document_type = 'CV' AND package_revision IS NOT NULL AND removed_at IS NULL");
    }
}
