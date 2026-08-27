using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portfolio.Domain.Entities;

namespace Portfolio.Infrastructure.Persistence.Configurations;

public sealed class SocialLinkConfiguration : IEntityTypeConfiguration<SocialLink>
{
    public void Configure(EntityTypeBuilder<SocialLink> builder)
    {
        builder.ToTable("social_links", table => table.HasTrigger("trg_social_links_updated_at"));
        builder.HasKey(entity => entity.Id).HasName("social_links_pkey");
        builder.Property(entity => entity.Id).HasColumnName("id").HasColumnType("uuid").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(entity => entity.Platform).HasColumnName("platform").HasColumnType("character varying(100)").HasMaxLength(100);
        builder.Property(entity => entity.Label).HasColumnName("label").HasColumnType("character varying(100)").HasMaxLength(100);
        builder.Property(entity => entity.Url).HasColumnName("url").HasColumnType("text");
        builder.Property(entity => entity.IconKey).HasColumnName("icon_key").HasColumnType("character varying(100)").HasMaxLength(100);
        builder.Property(entity => entity.DisplayOrder).HasColumnName("display_order").HasDefaultValue(0);
        builder.Property(entity => entity.IsVisible).HasColumnName("is_visible").HasDefaultValue(true);
        builder.Property(entity => entity.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("NOW()");
        builder.Property(entity => entity.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("NOW()");
        builder.HasIndex(entity => new { entity.IsVisible, entity.DisplayOrder }).HasDatabaseName("ix_social_links_visible_order");
    }
}
