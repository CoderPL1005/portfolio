using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portfolio.Domain.Entities;

namespace Portfolio.Infrastructure.Persistence.Configurations;

public sealed class ProjectMediaConfiguration : IEntityTypeConfiguration<ProjectMedia>
{
    public void Configure(EntityTypeBuilder<ProjectMedia> builder)
    {
        builder.ToTable("project_media", table =>
            table.HasCheckConstraint("ck_project_media_role", "media_role IN ('THUMBNAIL', 'SCREENSHOT', 'ARCHITECTURE', 'DIAGRAM', 'OTHER')"));
        builder.HasKey(entity => entity.Id).HasName("project_media_pkey");
        builder.Property(entity => entity.Id).HasColumnName("id").HasColumnType("uuid").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(entity => entity.ProjectId).HasColumnName("project_id").HasColumnType("uuid");
        builder.Property(entity => entity.MediaAssetId).HasColumnName("media_asset_id").HasColumnType("uuid");
        builder.Property(entity => entity.MediaRole).HasColumnName("media_role").HasColumnType("character varying(30)").HasMaxLength(30).HasDefaultValue("SCREENSHOT");
        builder.Property(entity => entity.Caption).HasColumnName("caption").HasColumnType("character varying(500)").HasMaxLength(500);
        builder.Property(entity => entity.DisplayOrder).HasColumnName("display_order").HasDefaultValue(0);
        builder.Property(entity => entity.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("NOW()");
        builder.HasAlternateKey(entity => new { entity.ProjectId, entity.MediaAssetId, entity.MediaRole }).HasName("uq_project_media");
        builder.HasOne(entity => entity.Project).WithMany().HasForeignKey(entity => entity.ProjectId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(entity => entity.MediaAsset).WithMany().HasForeignKey(entity => entity.MediaAssetId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(entity => new { entity.ProjectId, entity.MediaRole, entity.DisplayOrder }).HasDatabaseName("ix_project_media_order");
    }
}
