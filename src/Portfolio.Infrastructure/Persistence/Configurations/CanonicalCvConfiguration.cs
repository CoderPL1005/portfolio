using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portfolio.Domain.Entities;

namespace Portfolio.Infrastructure.Persistence.Configurations;

public sealed class CanonicalCvConfiguration : IEntityTypeConfiguration<CanonicalCv>
{
    public void Configure(EntityTypeBuilder<CanonicalCv> builder)
    {
        builder.ToTable("canonical_cvs", table =>
        {
            table.HasCheckConstraint("ck_canonical_cvs_singleton", "singleton_key = 1");
            table.HasCheckConstraint("ck_canonical_cvs_content_type", "content_type = 'application/pdf'");
            table.HasCheckConstraint("ck_canonical_cvs_file_size", "file_size_bytes > 0 AND file_size_bytes <= 10485760");
            table.HasCheckConstraint("ck_canonical_cvs_content_hash", "content_hash ~ '^[0-9a-f]{64}$'");
            table.HasCheckConstraint("ck_canonical_cvs_version", "version >= 1");
        });

        builder.HasKey(entity => entity.Id).HasName("canonical_cvs_pkey");
        builder.Property(entity => entity.Id).HasColumnName("id").HasColumnType("uuid").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(entity => entity.SingletonKey).HasColumnName("singleton_key").HasColumnType("smallint").HasDefaultValue((short)1);
        builder.Property(entity => entity.StorageKey).HasColumnName("storage_key").HasColumnType("character varying(1000)").HasMaxLength(1000);
        builder.Property(entity => entity.FileName).HasColumnName("file_name").HasColumnType("character varying(255)").HasMaxLength(255);
        builder.Property(entity => entity.ContentType).HasColumnName("content_type").HasColumnType("character varying(100)").HasMaxLength(100);
        builder.Property(entity => entity.FileSizeBytes).HasColumnName("file_size_bytes").HasColumnType("bigint");
        builder.Property(entity => entity.ContentHash).HasColumnName("content_hash").HasColumnType("character varying(64)").HasMaxLength(64);
        builder.Property(entity => entity.Version).HasColumnName("version").HasDefaultValue(1).IsConcurrencyToken();
        builder.Property(entity => entity.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("NOW()");
        builder.Property(entity => entity.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("NOW()");

        builder.HasIndex(entity => entity.SingletonKey).IsUnique().HasDatabaseName("uq_canonical_cvs_singleton");
        builder.HasIndex(entity => entity.StorageKey).IsUnique().HasDatabaseName("uq_canonical_cvs_storage_key");
    }
}
