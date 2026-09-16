using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portfolio.Domain.Entities;

namespace Portfolio.Infrastructure.Persistence.Configurations;

public sealed class RawJobPostingAttachmentConfiguration : IEntityTypeConfiguration<RawJobPostingAttachment>
{
    public void Configure(EntityTypeBuilder<RawJobPostingAttachment> builder)
    {
        builder.ToTable("raw_job_posting_attachments", table =>
        {
            table.HasCheckConstraint("ck_raw_job_posting_attachments_type", "attachment_type = 'IMAGE'");
            table.HasCheckConstraint("ck_raw_job_posting_attachments_content_hash", "content_hash ~ '^[0-9a-f]{64}$'");
            table.HasCheckConstraint("ck_raw_job_posting_attachments_file_size", "file_size_bytes > 0");
            table.HasCheckConstraint("ck_raw_job_posting_attachments_dimensions", "width > 0 AND height > 0");
            table.HasCheckConstraint("ck_raw_job_posting_attachments_sort_order", "sort_order > 0");
            table.HasCheckConstraint("ck_raw_job_posting_attachments_message_id", "telegram_message_id > 0");
        });

        builder.HasKey(entity => entity.Id).HasName("raw_job_posting_attachments_pkey");
        builder.Property(entity => entity.Id).HasColumnName("id").HasColumnType("uuid").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(entity => entity.RawJobPostingId).HasColumnName("raw_job_posting_id").HasColumnType("uuid");
        builder.Property(entity => entity.AttachmentType).HasColumnName("attachment_type").HasColumnType("character varying(30)").HasMaxLength(30);
        builder.Property(entity => entity.StorageKey).HasColumnName("storage_key").HasColumnType("character varying(1000)").HasMaxLength(1000);
        builder.Property(entity => entity.ContentType).HasColumnName("content_type").HasColumnType("character varying(100)").HasMaxLength(100);
        builder.Property(entity => entity.ContentHash).HasColumnName("content_hash").HasColumnType("character varying(64)").HasMaxLength(64);
        builder.Property(entity => entity.FileSizeBytes).HasColumnName("file_size_bytes").HasColumnType("bigint");
        builder.Property(entity => entity.SortOrder).HasColumnName("sort_order").HasColumnType("bigint");
        builder.Property(entity => entity.TelegramMessageId).HasColumnName("telegram_message_id").HasColumnType("bigint");
        builder.Property(entity => entity.TelegramFileId).HasColumnName("telegram_file_id").HasColumnType("character varying(512)").HasMaxLength(512);
        builder.Property(entity => entity.TelegramFileUniqueId).HasColumnName("telegram_file_unique_id").HasColumnType("character varying(512)").HasMaxLength(512);
        builder.Property(entity => entity.Width).HasColumnName("width").HasColumnType("integer");
        builder.Property(entity => entity.Height).HasColumnName("height").HasColumnType("integer");
        builder.Property(entity => entity.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("NOW()");

        builder.HasOne(entity => entity.RawJobPosting)
            .WithMany(entity => entity.Attachments)
            .HasForeignKey(entity => entity.RawJobPostingId)
            .HasConstraintName("fk_raw_job_posting_attachments_raw_job_postings")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(entity => new { entity.RawJobPostingId, entity.TelegramMessageId })
            .HasDatabaseName("uq_raw_job_posting_attachments_delivery")
            .IsUnique();
        builder.HasIndex(entity => new { entity.RawJobPostingId, entity.SortOrder, entity.Id })
            .HasDatabaseName("ix_raw_job_posting_attachments_order");
        builder.HasIndex(entity => entity.ContentHash)
            .HasDatabaseName("ix_raw_job_posting_attachments_content_hash");
    }
}
