using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portfolio.Domain.Entities;

namespace Portfolio.Infrastructure.Persistence.Configurations;

public sealed class MediaAssetConfiguration : IEntityTypeConfiguration<MediaAsset>
{
    public void Configure(EntityTypeBuilder<MediaAsset> builder)
    {
        builder.ToTable("media_assets", table =>
        {
            table.HasTrigger("trg_media_assets_updated_at");
            table.HasCheckConstraint("ck_media_assets_type", "media_type IN ('IMAGE', 'DOCUMENT', 'CV', 'OTHER')");
            table.HasCheckConstraint("ck_media_assets_file_size", "file_size IS NULL OR file_size >= 0");
        });
        builder.HasKey(entity => entity.Id).HasName("media_assets_pkey");
        builder.Property(entity => entity.Id).HasColumnName("id").HasColumnType("uuid").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(entity => entity.StorageKey).HasColumnName("storage_key").HasColumnType("text");
        builder.Property(entity => entity.PublicUrl).HasColumnName("public_url").HasColumnType("text");
        builder.Property(entity => entity.FileName).HasColumnName("file_name").HasColumnType("character varying(255)").HasMaxLength(255);
        builder.Property(entity => entity.MimeType).HasColumnName("mime_type").HasColumnType("character varying(150)").HasMaxLength(150);
        builder.Property(entity => entity.FileSize).HasColumnName("file_size").HasColumnType("bigint");
        builder.Property(entity => entity.AltText).HasColumnName("alt_text").HasColumnType("character varying(500)").HasMaxLength(500);
        builder.Property(entity => entity.MediaType).HasColumnName("media_type").HasColumnType("character varying(30)").HasMaxLength(30).HasDefaultValue("IMAGE");
        builder.Property(entity => entity.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("NOW()");
        builder.Property(entity => entity.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("NOW()");
        builder.HasAlternateKey(entity => entity.StorageKey).HasName("uq_media_assets_storage_key");
    }
}
