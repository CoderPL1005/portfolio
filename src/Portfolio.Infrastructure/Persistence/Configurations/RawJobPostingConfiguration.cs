using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portfolio.Domain.Entities;

namespace Portfolio.Infrastructure.Persistence.Configurations;

public sealed class RawJobPostingConfiguration : IEntityTypeConfiguration<RawJobPosting>
{
    public void Configure(EntityTypeBuilder<RawJobPosting> builder)
    {
        builder.ToTable("raw_job_postings", table =>
        {
            table.HasTrigger("trg_raw_job_postings_updated_at");
            table.HasCheckConstraint("ck_raw_job_postings_source", "source IN ('FACEBOOK', 'INSTAGRAM', 'TOPCV', 'VIETNAMWORKS', 'COMPANY_SITE', 'MANUAL', 'OTHER')");
            table.HasCheckConstraint("ck_raw_job_postings_ingestion_status", "ingestion_status IN ('RECEIVED', 'NORMALIZED', 'DUPLICATE', 'REJECTED')");
            table.HasCheckConstraint("ck_raw_job_postings_source_url_hash", "source_url_hash IS NULL OR source_url_hash ~ '^[0-9a-f]{64}$'");
            table.HasCheckConstraint("ck_raw_job_postings_content_hash", "content_hash ~ '^[0-9a-f]{64}$'");
        });

        builder.HasKey(entity => entity.Id).HasName("raw_job_postings_pkey");
        builder.Property(entity => entity.Id).HasColumnName("id").HasColumnType("uuid").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(entity => entity.JobPostingId).HasColumnName("job_posting_id").HasColumnType("uuid");
        builder.Property(entity => entity.Source).HasColumnName("source").HasColumnType("character varying(30)").HasMaxLength(30);
        builder.Property(entity => entity.SourceExternalId).HasColumnName("source_external_id").HasColumnType("character varying(500)").HasMaxLength(500);
        builder.Property(entity => entity.IngestionKey).HasColumnName("ingestion_key").HasColumnType("character varying(500)").HasMaxLength(500);
        builder.Property(entity => entity.SourceUrl).HasColumnName("source_url").HasColumnType("text");
        builder.Property(entity => entity.SourceUrlHash).HasColumnName("source_url_hash").HasColumnType("character varying(64)").HasMaxLength(64);
        builder.Property(entity => entity.RawContent).HasColumnName("raw_content").HasColumnType("text");
        builder.Property(entity => entity.ContentHash).HasColumnName("content_hash").HasColumnType("character varying(64)").HasMaxLength(64);
        builder.Property(entity => entity.CompanyTitleFingerprint).HasColumnName("company_title_fingerprint").HasColumnType("character varying(255)").HasMaxLength(255);
        builder.Property(entity => entity.IngestionStatus).HasColumnName("ingestion_status").HasColumnType("character varying(30)").HasMaxLength(30).HasDefaultValue("RECEIVED");
        builder.Property(entity => entity.DuplicateOfRawJobPostingId).HasColumnName("duplicate_of_raw_job_posting_id").HasColumnType("uuid");
        builder.Property(entity => entity.Metadata).HasColumnName("metadata").HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
        builder.Property(entity => entity.DiscoveredAt).HasColumnName("discovered_at").HasColumnType("timestamp with time zone");
        builder.Property(entity => entity.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("NOW()");
        builder.Property(entity => entity.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("NOW()");

        builder.HasOne(entity => entity.JobPosting)
            .WithMany(entity => entity.RawJobPostings)
            .HasForeignKey(entity => entity.JobPostingId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.DuplicateOfRawJobPosting)
            .WithMany(entity => entity.DuplicateRawJobPostings)
            .HasForeignKey(entity => entity.DuplicateOfRawJobPostingId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(entity => new { entity.Source, entity.SourceExternalId })
            .HasDatabaseName("uq_raw_job_postings_source_external_id")
            .HasFilter("source_external_id IS NOT NULL")
            .IsUnique();
        builder.HasIndex(entity => entity.IngestionKey)
            .HasDatabaseName("uq_raw_job_postings_ingestion_key")
            .HasFilter("ingestion_key IS NOT NULL")
            .IsUnique();
        builder.HasIndex(entity => new { entity.Source, entity.SourceUrlHash })
            .HasDatabaseName("uq_raw_job_postings_source_url_hash")
            .HasFilter("source_url_hash IS NOT NULL")
            .IsUnique();
        builder.HasIndex(entity => entity.ContentHash).HasDatabaseName("ix_raw_job_postings_content_hash");
        builder.HasIndex(entity => entity.CompanyTitleFingerprint).HasDatabaseName("ix_raw_job_postings_company_title_fingerprint");
        builder.HasIndex(entity => new { entity.IngestionStatus, entity.CreatedAt })
            .HasDatabaseName("ix_raw_job_postings_ingestion_status_created_at")
            .IsDescending(false, true);
        builder.HasIndex(entity => entity.JobPostingId).HasDatabaseName("ix_raw_job_postings_job_posting_id");
    }
}
