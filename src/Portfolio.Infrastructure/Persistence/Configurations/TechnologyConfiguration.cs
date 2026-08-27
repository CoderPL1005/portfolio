using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portfolio.Domain.Entities;

namespace Portfolio.Infrastructure.Persistence.Configurations;

public sealed class TechnologyConfiguration : IEntityTypeConfiguration<Technology>
{
    public void Configure(EntityTypeBuilder<Technology> builder)
    {
        builder.ToTable("technologies", table => table.HasTrigger("trg_technologies_updated_at"));
        builder.HasKey(entity => entity.Id).HasName("technologies_pkey");
        builder.Property(entity => entity.Id).HasColumnName("id").HasColumnType("uuid").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(entity => entity.Name).HasColumnName("name").HasColumnType("character varying(100)").HasMaxLength(100);
        builder.Property(entity => entity.Category).HasColumnName("category").HasColumnType("character varying(50)").HasMaxLength(50);
        builder.Property(entity => entity.IconKey).HasColumnName("icon_key").HasColumnType("character varying(100)").HasMaxLength(100);
        builder.Property(entity => entity.WebsiteUrl).HasColumnName("website_url").HasColumnType("text");
        builder.Property(entity => entity.DisplayOrder).HasColumnName("display_order").HasDefaultValue(0);
        builder.Property(entity => entity.IsActive).HasColumnName("is_active").HasDefaultValue(true);
        builder.Property(entity => entity.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("NOW()");
        builder.Property(entity => entity.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("NOW()");
        builder.HasIndex(entity => new { entity.Category, entity.DisplayOrder }).HasDatabaseName("ix_technologies_category");
    }
}
